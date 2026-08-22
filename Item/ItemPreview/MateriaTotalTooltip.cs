using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;
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
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class MateriaTotalTooltip
{
    private const int MaxParameterCount = 5;
    private const int ParameterCountIndex = 21;
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
        var manager = TooltipManager.Instance();
        var enabledLifetime = new FeatureLifetime();
        enabledLifetime.Add(manager.TriggerItemDetailUpdate);
        try
        {
            enabledLifetime.Add(inventoryContext.Acquire().Dispose);
            manager.RegItem(OnItemTooltip);
            enabledLifetime.Add(() => manager.Unreg(OnItemTooltip));
            manager.TriggerItemDetailUpdate();
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

    private void OnItemTooltip(
        ItemKind itemKind,
        uint itemID,
        ref List<TooltipItemModification> modifications)
    {
        try
        {
            ApplyMateriaTotals(itemKind, itemID, modifications);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "build materia total tooltip");
        }
    }

    private void ApplyMateriaTotals(
        ItemKind itemKind,
        uint itemID,
        List<TooltipItemModification> modifications)
    {
        if (itemKind == ItemKind.EventItem ||
            itemID == 0 ||
            !inventoryContext.TryGet(itemID, out var hoveredItem) ||
            hoveredItem.IsSymbolic ||
            !LuminaGetter.TryGetRow<Item>(itemID, out var item) ||
            item.MateriaSlotCount == 0 ||
            !BuildStats(item, itemKind == ItemKind.Hq, hoveredItem))
        {
            return;
        }

        var numberArrayData = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.ItemDetail);
        if (numberArrayData == null ||
            numberArrayData->IntArray == null ||
            numberArrayData->Size <= ParameterCountIndex)
        {
            return;
        }

        appliedParameters.Clear();
        Span<TooltipItemType> preferredTargets = stackalloc TooltipItemType[(MaxParameterCount + 1) / 2];
        Span<TooltipItemType> fallbackTargets = stackalloc TooltipItemType[MaxParameterCount];
        var preferredTargetCount = 0;
        var fallbackTargetCount = 0;
        var currentParameterCount = Math.Clamp(
            numberArrayData->IntArray[ParameterCountIndex],
            0,
            MaxParameterCount);
        var requiredParameterCount = currentParameterCount;
        for (var index = 0; index < MaxParameterCount; index++)
        {
            var target = (TooltipItemType)((int)TooltipItemType.SpecialParam0 + index);
            var original = index < currentParameterCount
                ? ReadCurrentItemTooltipText(37 + index)
                : default;
            if (index >= currentParameterCount || original.IsEmpty)
            {
                modifications.Add(new()
                {
                    Target = target,
                    Type = TooltipModificationType.Contribute,
                    Text = default
                });

                if ((index & 1) == 0)
                {
                    preferredTargets[preferredTargetCount++] = target;
                }
                else
                {
                    fallbackTargets[fallbackTargetCount++] = target;
                }

                continue;
            }

            if (TryFindParameter(original.ExtractText(), out var parameterID))
            {
                if ((index & 1) == 0)
                {
                    modifications.Add(new()
                    {
                        Target = target,
                        Type = TooltipModificationType.Contribute,
                        Text = BuildTooltipLine(stats[parameterID])
                    });
                    appliedParameters.Add(parameterID);
                }
                else
                {
                    modifications.Add(new()
                    {
                        Target = target,
                        Type = TooltipModificationType.Contribute,
                        Text = default
                    });
                    fallbackTargets[fallbackTargetCount++] = target;
                }
            }
        }

        var nextPreferredTarget = 0;
        var nextFallbackTarget = 0;
        for (var index = 0; index < materiaParameterOrder.Count; index++)
        {
            var parameterID = materiaParameterOrder[index];
            if (!appliedParameters.Add(parameterID))
            {
                continue;
            }

            TooltipItemType target;
            if (nextPreferredTarget < preferredTargetCount)
            {
                target = preferredTargets[nextPreferredTarget++];
            }
            else if (nextFallbackTarget < fallbackTargetCount)
            {
                target = fallbackTargets[nextFallbackTarget++];
            }
            else
            {
                break;
            }

            requiredParameterCount = Math.Max(
                requiredParameterCount,
                (int)target - (int)TooltipItemType.SpecialParam0 + 1);

            modifications.Add(new()
            {
                Target = target,
                Type = TooltipModificationType.Append,
                Text = BuildTooltipLine(stats[parameterID])
            });

        }

        if (requiredParameterCount > currentParameterCount)
        {
            numberArrayData->IntArray[ParameterCountIndex] = requiredParameterCount;
        }
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

    private static ReadOnlySeString BuildTooltipLine(MateriaStat stat)
    {
        using var rented = new RentedSeStringBuilder();
        rented.Builder.Append(stat.Name).Append('+');
        AppendValue(rented.Builder, stat.Total, HighlightColor);
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
        return rented.Builder.ToReadOnlySeString();
    }

    private static ReadOnlySeString ReadCurrentItemTooltipText(int field)
    {
        var stringArrayData = AtkStage.Instance()->GetStringArrayData(StringArrayType.ItemDetail);
        if (stringArrayData == null ||
            stringArrayData->StringArray == null ||
            stringArrayData->Size <= field)
        {
            return default;
        }

        var pointer = stringArrayData->StringArray[field];
        return pointer.HasValue
            ? new ReadOnlySeString(new CStringPointer(pointer.Value).AsSpan())
            : default;
    }

    private static void AppendValue(SeStringBuilder builder, int value, ushort color)
    {
        builder
            .PushColorType(color)
            .Append(value.ToString())
            .PopColorType();
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
