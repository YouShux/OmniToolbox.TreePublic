using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Tooltips;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed unsafe class MateriaTotalNativeUi : IDisposable
{
    private const string AddonName = "ItemDetail";
    private const int ParameterCountIndex = 21;
    private const int ParameterStartField = 37;
    private const int MaxParameterCount = 8;
    private const ushort HighlightColor = 506;
    private const long ErrorLogThrottleMs = 10_000;
    private readonly MateriaTotalConfig config;
    private readonly ItemTooltipInventoryContext inventoryContext;
    private readonly TooltipManager tooltipManager;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly Dictionary<uint, MateriaStat> stats = new(16);
    private readonly Dictionary<uint, string> parameterNames = new(16);
    private readonly List<uint> materiaParameterOrder = new(5);
    private readonly HashSet<uint> appliedParameters = new(8);
    private long lastErrorLogTick;

    public MateriaTotalNativeUi(MateriaTotalConfig config, ItemTooltipInventoryContext inventoryContext)
    {
        this.config = config;
        this.inventoryContext = inventoryContext;
        tooltipManager = TooltipManager.Instance();
        addonEvents = new(DalamudServices.AddonLifecycle);

        lifetime.Add(tooltipManager.TriggerItemDetailUpdate);
        lifetime.Add(inventoryContext.Acquire().Dispose);
        lifetime.Add(addonEvents.Dispose);

        try
        {
            addonEvents.Register(AddonEvent.PreRequestedUpdate, AddonName, OnItemDetailUpdate);
            tooltipManager.TriggerItemDetailUpdate();
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    public void RefreshSettings() => tooltipManager.TriggerItemDetailUpdate();

    public void Dispose() => lifetime.Dispose();

    private void OnItemDetailUpdate(AddonEvent _, AddonArgs args)
    {
        try
        {
            ApplyMateriaTotals();
        }
        catch (Exception ex)
        {
            LogFailure(ex, "update materia totals");
        }
    }

    private void ApplyMateriaTotals()
    {
        if (!TryGetActiveItem(out var itemId, out var hq) ||
            !inventoryContext.TryGet(itemId, out var hoveredItem) ||
            hoveredItem.IsSymbolic ||
            !LuminaGetter.TryGetRow<Item>(itemId, out var item) ||
            item.MateriaSlotCount == 0 ||
            !BuildStats(item, hq, hoveredItem))
        {
            return;
        }

        var stage = AtkStage.Instance();
        if (stage == null)
        {
            return;
        }

        var numberArrayData = stage->GetNumberArrayData(NumberArrayType.ItemDetail);
        var stringArrayData = stage->GetStringArrayData(StringArrayType.ItemDetail);
        if (numberArrayData == null ||
            numberArrayData->IntArray == null ||
            numberArrayData->Size <= ParameterCountIndex ||
            stringArrayData == null ||
            stringArrayData->Size < ParameterStartField + MaxParameterCount)
        {
            return;
        }

        appliedParameters.Clear();
        var parameterCount = Math.Clamp(numberArrayData->IntArray[ParameterCountIndex], 0, MaxParameterCount);
        for (var index = 0; index < parameterCount; index++)
        {
            var source = ReadTooltipText(stringArrayData, ParameterStartField + index);
            if (!TryFindParameter(source, out var parameterId))
            {
                continue;
            }

            WriteTooltipLine(stringArrayData, ParameterStartField + index, stats[parameterId]);
            appliedParameters.Add(parameterId);
        }

        for (var index = 0; index < materiaParameterOrder.Count && parameterCount < MaxParameterCount; index++)
        {
            var parameterId = materiaParameterOrder[index];
            if (!appliedParameters.Add(parameterId))
            {
                continue;
            }

            WriteTooltipLine(stringArrayData, ParameterStartField + parameterCount, stats[parameterId]);
            parameterCount++;
        }

        numberArrayData->IntArray[ParameterCountIndex] = parameterCount;
    }

    private bool BuildStats(Item item, bool hq, InventoryItem hoveredItem)
    {
        stats.Clear();
        materiaParameterOrder.Clear();

        for (var index = 0; index < item.BaseParam.Count; index++)
        {
            AddBaseValue(item.BaseParam[index].RowId, item.BaseParamValue[index]);
        }

        if (hq)
        {
            for (var index = 0; index < item.BaseParamSpecial.Count; index++)
            {
                AddBaseValue(item.BaseParamSpecial[index].RowId, item.BaseParamValueSpecial[index]);
            }
        }

        for (var index = 0; index < hoveredItem.Materia.Length; index++)
        {
            if (!LuminaGetter.TryGetRow<Materia>(hoveredItem.Materia[index], out var materia))
            {
                continue;
            }

            var grade = hoveredItem.MateriaGrades[index];
            if (grade <= 0 || grade > materia.Value.Count)
            {
                continue;
            }

            var parameterId = materia.BaseParam.RowId;
            var delta = materia.Value[grade];
            if (parameterId == 0 || delta <= 0)
            {
                continue;
            }

            if (!stats.TryGetValue(parameterId, out var stat))
            {
                stat = new(GetParameterName(parameterId), 0, 0);
            }

            if (stat.Delta == 0)
            {
                materiaParameterOrder.Add(parameterId);
            }

            stats[parameterId] = stat with { Delta = stat.Delta + delta };
        }

        return materiaParameterOrder.Count > 0;
    }

    private void AddBaseValue(uint parameterId, int value)
    {
        if (parameterId == 0 || value <= 0)
        {
            return;
        }

        stats[parameterId] = stats.TryGetValue(parameterId, out var stat)
            ? stat with { Original = stat.Original + value }
            : new(GetParameterName(parameterId), value, 0);
    }

    private string GetParameterName(uint parameterId)
    {
        if (parameterNames.TryGetValue(parameterId, out var name))
        {
            return name;
        }

        name = LuminaGetter.TryGetRow<BaseParam>(parameterId, out var parameter)
            ? parameter.Name.ExtractText()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Param {parameterId}";
        }

        parameterNames[parameterId] = name;
        return name;
    }

    private bool TryFindParameter(string line, out uint parameterId)
    {
        for (var index = 0; index < materiaParameterOrder.Count; index++)
        {
            parameterId = materiaParameterOrder[index];
            if (line.Contains(stats[parameterId].Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        parameterId = 0;
        return false;
    }

    private void WriteTooltipLine(StringArrayData* stringArrayData, int field, MateriaStat stat)
    {
        using var rented = new RentedSeStringBuilder();
        rented.Builder.Append(stat.Name).Append(" +");
        AppendValue(rented.Builder, config.ShowTotal ? stat.Total : stat.Original);
        if (!config.ShowTotal)
        {
            rented.Builder.Append("[+");
            AppendValue(rented.Builder, stat.Delta);
            rented.Builder.Append("=");
            AppendValue(rented.Builder, stat.Total);
            rented.Builder.Append("]");
        }

        stringArrayData->SetValue(field, rented.Builder.GetViewAsSpan(), suppressUpdates: true);
    }

    private void AppendValue(SeStringBuilder builder, int value)
    {
        if (config.HighlightStats)
        {
            builder.PushColorType(HighlightColor);
        }

        builder.Append(value.ToString());
        if (config.HighlightStats)
        {
            builder.PopColorType();
        }
    }

    private static string ReadTooltipText(StringArrayData* stringArrayData, int field)
    {
        var pointer = stringArrayData->StringArray[field];
        return pointer.HasValue
            ? new ReadOnlySeString(new CStringPointer(pointer.Value).AsSpan()).ExtractText()
            : string.Empty;
    }

    private static bool TryGetActiveItem(out uint itemId, out bool hq)
    {
        itemId = 0;
        hq = false;
        var agent = AgentItemDetail.Instance();
        if (agent == null || agent->ItemId == 0 || agent->ItemId >= 2_000_000)
        {
            return false;
        }

        hq = agent->ItemId >= 500_000;
        itemId = agent->ItemId % 500_000;
        return itemId != 0;
    }

    private void LogFailure(Exception exception, string operation)
    {
        var now = Environment.TickCount64;
        if (lastErrorLogTick != 0 && now - lastErrorLogTick < ErrorLogThrottleMs)
        {
            return;
        }

        lastErrorLogTick = now;
        DalamudServices.PluginLog.Warning(exception, "Materia total failed to {Operation}.", operation);
    }

    private readonly record struct MateriaStat(string Name, int Original, int Delta)
    {
        public int Total => Original + Delta;
    }
}
