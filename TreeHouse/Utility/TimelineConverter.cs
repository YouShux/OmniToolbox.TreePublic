using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OmniToolbox.TreeHouse.Utility;

public static partial class TimelineConverter
{
    private const string AssemblySuffix = ", YouShu";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly IReadOnlyDictionary<string, string> JobNames = new Dictionary<string, string>
    {
        ["占星"] = "Astrologian",
        ["贤者"] = "Sage",
        ["学者"] = "Scholar",
        ["白魔"] = "WhiteMage"
    };
    private static readonly IReadOnlyDictionary<string, string> ToolTypeNames = new Dictionary<string, string>
    {
        ["YouShu.工具箱.时间轴.M10S狂浪腾空, YouShu"] = "YouShu.Tool.Timeline.M10S狂浪腾空, YouShu",
        ["YouShu.工具箱.时间轴.副本自动吃药, YouShu"] = "YouShu.Tool.Timeline.副本自动吃药, YouShu",
        ["YouShu.工具箱.时间轴.场地中轴半程放置, YouShu"] = "YouShu.Tool.Timeline.场地中轴半程放置, YouShu",
        ["YouShu.工具箱.时间轴.坦克无敌检测, YouShu"] = "YouShu.Tool.Timeline.坦克无敌检测, YouShu",
        ["YouShu.工具箱.触发器.绝欧目标选择, YouShu"] = "YouShu.Tool.Timeline.绝欧目标选择, YouShu",
        ["YouShu.工具箱.触发器.绝欧麻将状态, YouShu"] = "YouShu.Tool.Timeline.绝欧麻将状态, YouShu"
    };

    public static TimelineConversionResult ConvertDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return TimelineConversionResult.DirectoryMissing(directoryPath);
        }

        var result = new TimelineConversionResult(directoryPath);
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    ConvertFile(filePath, result);
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Failures.Add($"{directoryPath}: {ex.Message}");
        }

        return result;
    }

    public static TimelineConversionResult CleanupBackups(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return TimelineConversionResult.DirectoryMissing(directoryPath, true);
        }

        var result = new TimelineConversionResult(directoryPath) { IsCleanup = true };
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.timeline-converter.bak", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(filePath);
                    result.DeletedBackups++;
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Failures.Add($"{directoryPath}: {ex.Message}");
        }

        return result;
    }

    internal static string ConvertContent(string content, out int replacementCount)
    {
        var source = Encoding.UTF8.GetBytes(content);
        var reader = new Utf8JsonReader(source, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var replacements = new List<JsonReplacement>();
        string? propertyName = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                propertyName = reader.GetString();
                continue;
            }

            if (reader.TokenType == JsonTokenType.String && propertyName == "$type" &&
                reader.GetString() is { } typeName && ConvertTypeName(typeName) is { } converted)
            {
                replacements.Add(new(
                    checked((int)reader.TokenStartIndex),
                    checked((int)(reader.BytesConsumed - reader.TokenStartIndex)),
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(converted, JsonOptions))));
            }

            propertyName = null;
        }

        replacementCount = replacements.Count;
        if (replacementCount == 0)
        {
            return content;
        }

        using var output = new MemoryStream(source.Length);
        var sourceOffset = 0;
        foreach (var replacement in replacements)
        {
            output.Write(source, sourceOffset, replacement.Offset - sourceOffset);
            output.Write(replacement.Value);
            sourceOffset = replacement.Offset + replacement.Length;
        }

        output.Write(source, sourceOffset, source.Length - sourceOffset);
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static void ConvertFile(string filePath, TimelineConversionResult result)
    {
        var content = File.ReadAllText(filePath);
        var converted = ConvertContent(content, out var replacementCount);
        if (replacementCount == 0)
        {
            return;
        }

        var backupPath = filePath + ".timeline-converter.bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(filePath, backupPath);
        }

        File.WriteAllText(filePath, converted);
        result.ConvertedFiles++;
        result.Replacements += replacementCount;
    }

    private static string? ConvertTypeName(string typeName)
    {
        if (ToolTypeNames.TryGetValue(typeName, out var converted))
        {
            return converted;
        }

        var match = JobTimelineTypePattern().Match(typeName);
        if (!match.Success)
        {
            return null;
        }

        var oldJob = match.Groups[1].Value;
        var category = match.Groups[2].Value == "行为设置" ? "Actions" : "Conditions";
        return $"YouShu.{JobNames[oldJob]}.Timeline.{category}.{ConvertClassName(oldJob, match.Groups[3].Value)}{AssemblySuffix}";
    }

    private static string ConvertClassName(string oldJob, string oldClassName)
    {
        if (oldClassName == "学者时间轴GetAetherflowCharges")
        {
            return "以太超流层数";
        }

        var className = oldClassName
            .Replace($"{oldJob}时间轴行为_", string.Empty, StringComparison.Ordinal)
            .Replace($"{oldJob}时间轴_", string.Empty, StringComparison.Ordinal)
            .Replace($"{oldJob}时间轴", string.Empty, StringComparison.Ordinal);
        className = className switch
        {
            "QT设置" => "QtSetting",
            "QT检测" => "QtCheck",
            _ => className
        };
        return className.StartsWith("检测", StringComparison.Ordinal) ? className[2..] : className;
    }

    [GeneratedRegex("^YouShu\\.(占星|贤者|学者|白魔)\\.时间轴\\.(行为设置|条件设置)\\.([^,\"]+), YouShu$")]
    private static partial Regex JobTimelineTypePattern();

    private readonly record struct JsonReplacement(int Offset, int Length, byte[] Value);
}

public sealed class TimelineConversionResult(string directoryPath)
{
    public string DirectoryPath { get; } = directoryPath;

    public int ConvertedFiles { get; set; }

    public int Replacements { get; set; }

    public int DeletedBackups { get; set; }

    public bool IsCleanup { get; init; }

    public bool DirectoryExists { get; init; } = true;

    public List<string> Failures { get; } = [];

    public static TimelineConversionResult DirectoryMissing(string directoryPath, bool cleanup = false) =>
        new(directoryPath) { DirectoryExists = false, IsCleanup = cleanup };
}
