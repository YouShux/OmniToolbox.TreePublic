using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoQuest
{
    private sealed class QuestScript
    {
        private readonly Dictionary<string, LuaFunction> functions = new(StringComparer.Ordinal);
        private string? definitionOwner;

        public string? Error { get; private set; }

        public static QuestScript Parse(byte[] bytes, CancellationToken cancellationToken)
        {
            var script = new QuestScript();
            try
            {
                var reader = new LuaChunkReader(bytes, cancellationToken);
                script.Index(reader.Read());
            }
            catch (InvalidDataException exception)
            {
                script.Error = exception.Message;
            }
            catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                script.Error = "script_bytecode_invalid";
            }
            return script;
        }

        public bool Contains(string function) => functions.ContainsKey(function);
        public int ParameterCount(string function) => functions.TryGetValue(function, out var value) ? value.Parameters : -1;

        public bool TryEvaluate
        (
            string name,
            IReadOnlyDictionary<string, uint> symbols,
            Func<string, object?[], object?[]> native,
            object?[] parameters,
            out object?[] values,
            out string? error
        )
        {
            values = [];
            error = Error;
            if (error is not null)
                return false;
            if (!functions.TryGetValue(name, out var function))
            {
                error = "script_function_missing";
                return false;
            }
            try
            {
                var budget = new LuaBudget();
                values = Execute(function, parameters, symbols, native, budget, 0);
                return true;
            }
            catch (LuaEvaluationException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                error = "script_bytecode_invalid";
                return false;
            }
        }

        private void Index(LuaFunction function, int depth = 0)
        {
            if (depth > 64 || function.Upvalues != 0 || function.Parameters != 0)
                throw new InvalidDataException("script_definition_unresolved");
            var registers = new object?[Math.Max(256, (int)function.Stack)];
            foreach (var instruction in function.Code)
            {
                var op = (int)(instruction & 63);
                var a = (int)((instruction >> 6) & 255);
                var b = (int)(instruction >> 23);
                var c = (int)((instruction >> 14) & 511);
                var bx = (int)(instruction >> 14);
                switch (op)
                {
                    case 0: registers[a] = registers[b]; break;
                    case 1: registers[a] = function.Constants[bx]; break;
                    case 2:
                        if (c != 0) throw new InvalidDataException("script_definition_unresolved");
                        registers[a] = b != 0;
                        break;
                    case 3: Array.Clear(registers, a, b - a + 1); break;
                    case 5:
                        if (function.Constants[bx] is not string global)
                            throw new InvalidDataException("script_definition_unresolved");
                        registers[a] = global == "print" ? new LuaCallable(global) : new LuaClassScope(global);
                        break;
                    case 36:
                        var child = function.Children[bx];
                        if (child.Upvalues != 0) throw new InvalidDataException("script_definition_unresolved");
                        registers[a] = child;
                        break;
                    case 9:
                        var key = b >= 256 ? function.Constants[b - 256] : registers[b];
                        var value = c >= 256 ? function.Constants[c - 256] : registers[c];
                        if (registers[a] is not LuaClassScope scope || key is not string name)
                            throw new InvalidDataException("script_definition_unresolved");
                        if (value is not LuaFunction closure)
                        {
                            if (functions.ContainsKey(name)) throw new InvalidDataException("script_definition_unresolved");
                            break;
                        }
                        if (definitionOwner is not null && definitionOwner != scope.Name ||
                            functions.TryGetValue(name, out var existing) && !ReferenceEquals(existing, closure))
                            throw new InvalidDataException("script_definition_unresolved");
                        definitionOwner = scope.Name;
                        functions[name] = closure;
                        break;
                    case 28:
                        if (c != 1 || b == 0) throw new InvalidDataException("script_definition_unresolved");
                        if (registers[a] is LuaFunction initializer && b == 1)
                            Index(initializer, depth + 1);
                        else if (registers[a] is not LuaCallable { Name: "print" })
                            throw new InvalidDataException("script_definition_unresolved");
                        Array.Clear(registers, a, registers.Length - a);
                        break;
                    case 30:
                        if (b != 1) throw new InvalidDataException("script_definition_unresolved");
                        return;
                    default: throw new InvalidDataException("script_definition_unresolved");
                }
            }
            throw new InvalidDataException("script_definition_unresolved");
        }

        private object?[] Execute
        (
            LuaFunction function,
            object?[] arguments,
            IReadOnlyDictionary<string, uint> symbols,
            Func<string, object?[], object?[]> native,
            LuaBudget budget,
            int depth
        )
        {
            if (depth > 24 || function.Upvalues != 0)
                throw new LuaEvaluationException("script_closure_unsupported");
            var registers = new object?[Math.Max(256, (int)function.Stack)];
            Array.Copy(arguments, registers, Math.Min(arguments.Length, function.Parameters));
            var top = (int)function.Parameters;
            for (var pc = 0; pc < function.Code.Length; pc++)
            {
                if (--budget.Remaining < 0)
                    throw new LuaEvaluationException("script_budget_exceeded");
                var instruction = function.Code[pc];
                var op = (int)(instruction & 63);
                var a = (int)((instruction >> 6) & 255);
                var b = (int)(instruction >> 23);
                var c = (int)((instruction >> 14) & 511);
                var bx = (int)(instruction >> 14);
                var sbx = bx - 131071;
                object? RK(int value) => value >= 256 ? function.Constants[value - 256] : registers[value];
                switch (op)
                {
                    case 0: registers[a] = registers[b]; break;
                    case 1: registers[a] = function.Constants[bx]; break;
                    case 2:
                        registers[a] = b != 0;
                        if (c != 0) pc++;
                        break;
                    case 3: Array.Clear(registers, a, b - a + 1); break;
                    case 5:
                        registers[a] = function.Constants[bx] is string global
                            ? new LuaCallable(global) : throw new LuaEvaluationException("script_global_unsupported");
                        break;
                    case 6: registers[a] = Get(registers[b], RK(c), symbols); break;
                    case 9:
                        if (registers[a] is not LuaTable table)
                            throw new LuaEvaluationException("script_write_unsupported");
                        table.Set(RK(b), RK(c));
                        break;
                    case 10: registers[a] = new LuaTable(); break;
                    case 11:
                        registers[a + 1] = registers[b];
                        registers[a] = RK(c) is string method
                            ? new LuaCallable(method) : throw new LuaEvaluationException("script_method_unsupported");
                        break;
                    case 12: registers[a] = Number(RK(b)) + Number(RK(c)); break;
                    case 13: registers[a] = Number(RK(b)) - Number(RK(c)); break;
                    case 14: registers[a] = Number(RK(b)) * Number(RK(c)); break;
                    case 15:
                        if (Number(RK(c)) == 0) throw new LuaEvaluationException("script_divide_by_zero");
                        registers[a] = Number(RK(b)) / Number(RK(c));
                        break;
                    case 16:
                        if (Number(RK(c)) == 0) throw new LuaEvaluationException("script_divide_by_zero");
                        registers[a] = Number(RK(b)) - Math.Floor(Number(RK(b)) / Number(RK(c))) * Number(RK(c));
                        break;
                    case 17: registers[a] = Math.Pow(Number(RK(b)), Number(RK(c))); break;
                    case 18: registers[a] = -Number(registers[b]); break;
                    case 19: registers[a] = !Truth(registers[b]); break;
                    case 20:
                        registers[a] = registers[b] switch
                        {
                            LuaTable array => (double)array.Count,
                            string text => (double)text.Length,
                            _ => throw new LuaEvaluationException("script_length_unsupported")
                        };
                        break;
                    case 22: pc += sbx; break;
                    case 23:
                        if (Equal(RK(b), RK(c)) != (a != 0)) pc++;
                        break;
                    case 24:
                        if ((Number(RK(b)) < Number(RK(c))) != (a != 0)) pc++;
                        break;
                    case 25:
                        if ((Number(RK(b)) <= Number(RK(c))) != (a != 0)) pc++;
                        break;
                    case 26:
                        if (Truth(registers[a]) != (c != 0)) pc++;
                        break;
                    case 27:
                        if (Truth(registers[b]) == (c != 0)) registers[a] = registers[b];
                        else pc++;
                        break;
                    case 28:
                    case 29:
                        var count = b == 0 ? top - a - 1 : b - 1;
                        if (count < 0 || a + count >= registers.Length)
                            throw new LuaEvaluationException("script_arguments_invalid");
                        var callArguments = registers.AsSpan(a + 1, count).ToArray();
                        object?[] returned;
                        if (registers[a] is LuaFunction local)
                            returned = Execute(local, callArguments, symbols, native, budget, depth + 1);
                        else if (registers[a] is LuaCallable callable)
                        {
                            if (functions.TryGetValue(callable.Name, out var helper))
                                returned = Execute(helper, callArguments, symbols, native, budget, depth + 1);
                            else if (callable.Name == "unpack" && callArguments.FirstOrDefault() is LuaTable unpack)
                                returned = unpack.Values();
                            else
                                returned = native(callable.Name, callArguments);
                        }
                        else throw new LuaEvaluationException("script_call_unsupported");
                        if (op == 29) return returned;
                        var resultCount = c == 0 ? returned.Length : c - 1;
                        if (a + resultCount > registers.Length)
                            throw new LuaEvaluationException("script_results_invalid");
                        for (var index = 0; index < resultCount; index++)
                            registers[a + index] = index < returned.Length ? returned[index] : null;
                        top = a + resultCount;
                        break;
                    case 30:
                        var returnCount = b == 0 ? top - a : b - 1;
                        if (returnCount < 0 || a + returnCount > registers.Length)
                            throw new LuaEvaluationException("script_return_invalid");
                        return registers.AsSpan(a, returnCount).ToArray();
                    case 31:
                        var next = Number(registers[a]) + Number(registers[a + 2]);
                        var increment = Number(registers[a + 2]);
                        if (increment > 0 ? next <= Number(registers[a + 1]) : next >= Number(registers[a + 1]))
                        {
                            registers[a] = next;
                            registers[a + 3] = next;
                            pc += sbx;
                        }
                        break;
                    case 32:
                        registers[a] = Number(registers[a]) - Number(registers[a + 2]);
                        pc += sbx;
                        break;
                    case 34:
                        if (registers[a] is not LuaTable list || c == 0)
                            throw new LuaEvaluationException("script_list_unsupported");
                        var listCount = b == 0 ? top - a - 1 : b;
                        for (var index = 1; index <= listCount; index++)
                            list.Set((double)((c - 1) * 50 + index), registers[a + index]);
                        break;
                    case 36: registers[a] = function.Children[bx]; break;
                    default: throw new LuaEvaluationException($"script_opcode_unsupported:{op}");
                }
                if (pc < -1 || pc >= function.Code.Length)
                    throw new LuaEvaluationException("script_jump_invalid");
            }
            return [];
        }

        private object? Get(object? owner, object? key, IReadOnlyDictionary<string, uint> symbols)
        {
            if (owner is LuaTable table)
                return table.Get(key);
            if (owner is LuaSelf && key is string name)
            {
                if (functions.ContainsKey(name)) return new LuaCallable(name);
                if (symbols.TryGetValue(name, out var value)) return (double)value;
                if (name == "SEQ_FINISH") return 255d;
                if (name == "SEQ_OFFER") return 0d;
                if (name.StartsWith("SEQ_", StringComparison.Ordinal) && byte.TryParse(name.AsSpan(4), out var sequence))
                    return (double)sequence;
                if (name is "NUM_OF_ITEMS_FILTER_NQ" or "NUM_OF_ITEMS_FILTER_HQ" or "NUM_OF_ITEMS_FILTER_ALL" or "NUM_OF_ITEMS_FILTER_NQ_OR_HQ" or "QUALIFICATION_ITEM")
                    return new LuaHostConstant(name);
            }
            throw new LuaEvaluationException("script_symbol_unresolved");
        }

        public static bool Truth(object? value) => value is LuaUnknown unknown
            ? throw new LuaEvaluationException(unknown.Reason) : value is not null && value is not false;

        public static double Number(object? value) => value switch
        {
            double number => number,
            uint number => number,
            int number => number,
            byte number => number,
            ushort number => number,
            LuaUnknown unknown => throw new LuaEvaluationException(unknown.Reason),
            _ => throw new LuaEvaluationException("script_number_unresolved")
        };

        public static uint Unsigned(object? value)
        {
            var number = Number(value);
            if (!double.IsFinite(number) || number < 0 || number > uint.MaxValue || number != Math.Truncate(number))
                throw new LuaEvaluationException("script_unsigned_number_invalid");
            return (uint)number;
        }

        private static bool Equal(object? left, object? right)
        {
            if (left is LuaUnknown unknownLeft) throw new LuaEvaluationException(unknownLeft.Reason);
            if (right is LuaUnknown unknownRight) throw new LuaEvaluationException(unknownRight.Reason);
            if (left is LuaHostConstant && right is not LuaHostConstant || right is LuaHostConstant && left is not LuaHostConstant)
                throw new LuaEvaluationException("script_host_constant_value_unresolved");
            return left is double || right is double
                ? left is not null && right is not null && IsNumber(left) && IsNumber(right) && Number(left) == Number(right)
                : Equals(left, right);
        }

        private static bool IsNumber(object value) => value is double or uint or int or byte or ushort;

        private sealed class LuaBudget
        {
            public int Remaining = 20_000;
        }
    }

    private sealed class LuaTable
    {
        private readonly Dictionary<object, object?> entries = [];

        public int Count
        {
            get
            {
                var count = 0;
                while (entries.ContainsKey((double)(count + 1))) count++;
                return count;
            }
        }

        public object? Get(object? key) => key is null ? null : entries.GetValueOrDefault(key);

        public void Set(object? key, object? value)
        {
            if (key is null) throw new LuaEvaluationException("script_table_key_invalid");
            if (value is null) entries.Remove(key);
            else entries[key] = value;
        }

        public object?[] Values() => Enumerable.Range(1, Count).Select(index => Get((double)index)).ToArray();
    }

    private sealed record LuaFunction(byte Parameters, byte Upvalues, byte Stack, uint[] Code, object?[] Constants, LuaFunction[] Children);
    private sealed record LuaCallable(string Name);
    private sealed record LuaClassScope(string Name);
    private sealed record LuaHostConstant(string Name);
    private sealed record LuaUnknown(string Reason);
    private sealed record LuaSelf;
    private sealed record LuaActor(uint ID);
    private sealed record LuaPlayer;
    private sealed class LuaEvaluationException(string message) : Exception(message);

    private sealed class LuaChunkReader(byte[] bytes, CancellationToken cancellationToken)
    {
        private int offset;
        private bool littleEndian;
        private int integerSize;
        private int sizeT;
        private int numberSize;
        private bool integralNumbers;

        public LuaFunction Read()
        {
            if (bytes.Length < 12 || bytes[0] != 0x1B || bytes[1] != 'L' || bytes[2] != 'u' || bytes[3] != 'a' ||
                bytes[4] != 0x51 || bytes[5] != 0 || bytes[6] > 1 || bytes[9] != 4 || bytes[11] > 1)
                throw new InvalidDataException("script_format_unsupported");
            littleEndian = bytes[6] == 1;
            integerSize = bytes[7];
            sizeT = bytes[8];
            numberSize = bytes[10];
            integralNumbers = bytes[11] != 0;
            if (integerSize is not (4 or 8) || sizeT is not (4 or 8) || numberSize is not (4 or 8))
                throw new InvalidDataException("script_encoding_unsupported");
            offset = 12;
            var root = ReadFunction(0);
            if (offset != bytes.Length)
                throw new InvalidDataException("script_trailing_data");
            return root;
        }

        private LuaFunction ReadFunction(int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 64)
                throw new InvalidDataException("script_nesting_invalid");
            ReadString();
            ReadInteger();
            ReadInteger();
            var upvalues = ReadByte();
            var parameters = ReadByte();
            ReadByte();
            var stack = ReadByte();
            if (stack == 0 || parameters > stack)
                throw new InvalidDataException("script_stack_invalid");
            var code = new uint[ReadCount()];
            for (var index = 0; index < code.Length; index++)
                code[index] = checked((uint)ReadUnsigned(4));
            var constants = new object?[ReadCount()];
            for (var index = 0; index < constants.Length; index++)
                constants[index] = ReadByte() switch
                {
                    0 => null,
                    1 => ReadByte() != 0,
                    3 => ReadNumber(),
                    4 => ReadString(),
                    _ => throw new InvalidDataException("script_constant_unsupported")
                };
            var children = new LuaFunction[ReadCount()];
            for (var index = 0; index < children.Length; index++)
                children[index] = ReadFunction(depth + 1);
            Skip(checked(ReadCount() * integerSize));
            var locals = ReadCount();
            for (var index = 0; index < locals; index++)
            {
                ReadString();
                ReadInteger();
                ReadInteger();
            }
            var names = ReadCount();
            for (var index = 0; index < names; index++)
                ReadString();
            return new(parameters, upvalues, stack, code, constants, children);
        }

        private byte ReadByte()
        {
            if (offset >= bytes.Length)
                throw new InvalidDataException("script_truncated");
            return bytes[offset++];
        }

        private ulong ReadUnsigned(int count)
        {
            if (count > bytes.Length - offset)
                throw new InvalidDataException("script_truncated");
            var span = bytes.AsSpan(offset, count);
            offset += count;
            return count switch
            {
                4 when littleEndian => BinaryPrimitives.ReadUInt32LittleEndian(span),
                4 => BinaryPrimitives.ReadUInt32BigEndian(span),
                8 when littleEndian => BinaryPrimitives.ReadUInt64LittleEndian(span),
                8 => BinaryPrimitives.ReadUInt64BigEndian(span),
                _ => throw new InvalidDataException("script_integer_unsupported")
            };
        }

        private long ReadInteger() => integerSize == 4
            ? unchecked((int)ReadUnsigned(4)) : unchecked((long)ReadUnsigned(8));

        private int ReadCount()
        {
            var value = ReadInteger();
            if (value < 0 || value > 1_000_000 || value > bytes.Length - offset)
                throw new InvalidDataException("script_count_invalid");
            return (int)value;
        }

        private string? ReadString()
        {
            var length = ReadUnsigned(sizeT);
            if (length == 0) return null;
            if (length > (ulong)(bytes.Length - offset) || length > 1_000_000 || bytes[offset + (int)length - 1] != 0)
                throw new InvalidDataException("script_string_invalid");
            var value = Encoding.UTF8.GetString(bytes, offset, (int)length - 1);
            offset += (int)length;
            return value;
        }

        private double ReadNumber()
        {
            var value = ReadUnsigned(numberSize);
            if (integralNumbers) return numberSize == 4 ? unchecked((int)value) : unchecked((long)value);
            return numberSize == 4 ? BitConverter.Int32BitsToSingle(unchecked((int)value)) : BitConverter.Int64BitsToDouble(unchecked((long)value));
        }

        private void Skip(int count)
        {
            if (count < 0 || count > bytes.Length - offset)
                throw new InvalidDataException("script_debug_data_invalid");
            offset += count;
        }
    }
}
