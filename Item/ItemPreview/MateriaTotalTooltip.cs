using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.Exd;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class MateriaTotalTooltip
{
    private const string AddonName = "ItemDetail";
    private const int ParameterCountIndex = 21;
    private const int ParameterStartField = 37;
    private const int MaxParameterCount = 8;
    private const ushort HighlightColor = 506;
    private readonly ItemTooltipInventoryContext inventoryContext;
    private readonly Dictionary<uint, MateriaStat> stats = new(16);
    private readonly Dictionary<uint, string> parameterNames = new(16);
    private readonly List<uint> materiaParameterOrder = new(5);
    private readonly HashSet<uint> appliedParameters = new(8);
    private FeatureLifetime? lifetime;
    private long lastErrorLogTick;

    public MateriaTotalTooltip(ItemTooltipInventoryContext inventoryContext)
    {
        this.inventoryContext = inventoryContext;
    }

    public void SetEnabled(bool enabled)
    {
        if ((lifetime is not null) == enabled)
        {
            return;
        }

        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    private void Enable()
    {
        var tooltipManager = TooltipManager.Instance();
        var enabledLifetime = new FeatureLifetime();
        enabledLifetime.Add(tooltipManager.TriggerItemDetailUpdate);
        try
        {
            enabledLifetime.Add(inventoryContext.Acquire().Dispose);
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            enabledLifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PreRequestedUpdate, AddonName, OnItemDetailUpdate);
            tooltipManager.TriggerItemDetailUpdate();
            lifetime = enabledLifetime;
        }
        catch
        {
            enabledLifetime.Dispose();
            throw;
        }
    }

    private void Disable()
    {
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            lifetime = null;
        }
    }

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
            if (!TryFindParameter(ReadTooltipText(stringArrayData, ParameterStartField + index), out var parameterID))
            {
                continue;
            }

            WriteTooltipLine(stringArrayData, ParameterStartField + index, stats[parameterID]);
            appliedParameters.Add(parameterID);
        }

        for (var index = 0; index < materiaParameterOrder.Count && parameterCount < MaxParameterCount; index++)
        {
            var parameterID = materiaParameterOrder[index];
            if (!appliedParameters.Add(parameterID))
            {
                continue;
            }

            WriteTooltipLine(stringArrayData, ParameterStartField + parameterCount, stats[parameterID]);
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

        var itemRowData = ExdModule.GetItemRowById(item.RowId);
        if (itemRowData == null)
        {
            return false;
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

        var parameterID = materia.BaseParam.RowId;
            var delta = materia.Value[grade];
            if (parameterID == 0 || delta <= 0)
            {
                continue;
            }

            if (!stats.TryGetValue(parameterID, out var stat))
            {
                stat = new(GetParameterName(parameterID), 0, 0, 0);
            }

            if (stat.Delta == 0)
            {
                materiaParameterOrder.Add(parameterID);
                stat = stat with
                {
                    Maximum = (int)InventoryItem.GetParameterMaxValue(parameterID, itemRowData),
                };
            }

            stats[parameterID] = stat with { Delta = stat.Delta + delta };
        }

        return materiaParameterOrder.Count > 0;
    }

    private void AddBaseValue(uint parameterID, int value)
    {
        if (parameterID == 0 || value <= 0)
        {
            return;
        }

        stats[parameterID] = stats.TryGetValue(parameterID, out var stat)
            ? stat with { Original = stat.Original + value }
            : new(GetParameterName(parameterID), value, 0, 0);
    }

    private string GetParameterName(uint parameterID)
    {
        if (parameterNames.TryGetValue(parameterID, out var name))
        {
            return name;
        }

        name = LuminaGetter.TryGetRow<BaseParam>(parameterID, out var parameter)
            ? parameter.Name.ExtractText()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Param {parameterID}";
        }

        parameterNames[parameterID] = name;
        return name;
    }

    private bool TryFindParameter(string line, out uint parameterID)
    {
        for (var index = 0; index < materiaParameterOrder.Count; index++)
        {
            parameterID = materiaParameterOrder[index];
            if (line.Contains(stats[parameterID].Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        parameterID = 0;
        return false;
    }

    private static void WriteTooltipLine(StringArrayData* stringArrayData, int field, MateriaStat stat)
    {
        using var rented = new RentedSeStringBuilder();
        rented.Builder.Append(stat.Name).Append('+');
        AppendValue(rented.Builder, stat.Original, HighlightColor);
        rented.Builder
            .Append('\uFF08')
            .Append(OmniLoc.Get("ItemTooltip.MateriaTotal.Granted"));
        AppendValue(rented.Builder, stat.Delta, stat.ExceedsMaximum ? (ushort)14 : HighlightColor);
        rented.Builder
            .Append('\uFF0C')
            .Append(OmniLoc.Get(stat.ExceedsMaximum
                ? "ItemTooltip.MateriaTotal.Maximum"
                : "ItemTooltip.MateriaTotal.Current"));
        AppendValue(rented.Builder, stat.ExceedsMaximum ? stat.Maximum : stat.Total, HighlightColor);
        rented.Builder.Append('\uFF09');
        stringArrayData->SetValue(field, rented.Builder.GetViewAsSpan(), suppressUpdates: true);
    }

    private static void AppendValue(SeStringBuilder builder, int value, ushort color)
    {
        builder
            .PushColorType(color)
            .Append(value.ToString())
            .PopColorType();
    }

    private static string ReadTooltipText(StringArrayData* stringArrayData, int field)
    {
        var pointer = stringArrayData->StringArray[field];
        return pointer.HasValue
            ? new ReadOnlySeString(new CStringPointer(pointer.Value).AsSpan()).ExtractText()
            : string.Empty;
    }

    private static bool TryGetActiveItem(out uint itemID, out bool hq)
    {
        itemID = 0;
        hq = false;
        var agent = AgentItemDetail.Instance();
        if (agent == null || agent->ItemId == 0 || agent->ItemId >= 2_000_000)
        {
            return false;
        }

        hq = agent->ItemId >= 500_000;
        itemID = ItemUtil.GetBaseId(agent->ItemId).ItemId;
        return itemID != 0;
    }

    private void LogFailure(Exception exception, string operation)
    {
        var now = Environment.TickCount64;
        if (lastErrorLogTick != 0 && now - lastErrorLogTick < 10_000)
        {
            return;
        }

        lastErrorLogTick = now;
        DalamudServices.PluginLog.Warning(exception, "Materia total failed to {Operation}.", operation);
    }

    private readonly record struct MateriaStat(string Name, int Original, int Delta, int Maximum)
    {
        public int Total => Original + Delta;

        public bool ExceedsMaximum => Maximum > 0 && Total > Maximum;
    }
}
