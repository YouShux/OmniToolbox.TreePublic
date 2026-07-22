using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class AutoGcSealPanel
{
    private const int GridColumns = 4;
    private const int VisibleRows = 6;
    private static int customItemIdInput;

    public static bool NormalizeConfig(AutoGcSealConfig config)
    {
        return config.CustomExcludedItemIds.Remove(0);
    }

    public static bool Draw(
        AutoGcSealConfig config,
        bool enabled,
        Action openSupply,
        Action openExchange)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##autoGcSealSettings",
                   3,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (DrawCheckbox(
                    "Feature.AutoGcSeal.SkipHq",
                    "skipHq",
                    config.SkipHqItems,
                    out var skipHq))
                {
                    config.SkipHqItems = skipHq;
                    changed = true;
                }
                ImGui.TableNextColumn();
                if (DrawCheckbox(
                    "Feature.AutoGcSeal.SkipMateria",
                    "skipMateria",
                    config.SkipMateriaItems,
                    out var skipMateria))
                {
                    config.SkipMateriaItems = skipMateria;
                    changed = true;
                }
                ImGui.TableNextColumn();
                if (DrawCheckbox(
                    "Feature.AutoGcSeal.ExcludeProtected",
                    "excludeProtected",
                    config.SkipExcludedItems,
                    out var excludeProtected))
                {
                    config.SkipExcludedItems = excludeProtected;
                    changed = true;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (DrawUnsignedInput(
                    "Feature.AutoGcSeal.Threshold",
                    "threshold",
                    config.ExchangeThreshold,
                    out var threshold))
                {
                    config.ExchangeThreshold = threshold;
                    changed = true;
                }
                ImGui.TableNextColumn();
                if (DrawUnsignedInput(
                    "Feature.AutoGcSeal.Reserve",
                    "reserve",
                    config.ReservedSeals,
                    out var reserve))
                {
                    config.ReservedSeals = reserve;
                    changed = true;
                }
                ImGui.TableNextColumn();
                if (DrawUnsignedInput(
                    "Feature.AutoGcSeal.ExchangeItemId",
                    "exchangeItem",
                    config.ExchangeItemId,
                    out var exchangeItemId))
                {
                    config.ExchangeItemId = exchangeItemId;
                    changed = true;
                }
            }
        }

        var exchangeItemName = LuminaWrapper.GetItemName(config.ExchangeItemId);
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(exchangeItemName)
            ? OmniLoc.Get("Feature.AutoGcSeal.InvalidItem")
            : $"{exchangeItemName} ({config.ExchangeItemId})");

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoGcSeal.CustomExcludeId"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(110f));
        if (ImGui.InputInt("##autoGcSealCustomExclude", ref customItemIdInput) && customItemIdInput < 0)
        {
            customItemIdInput = 0;
        }

        var normalizedItemId = NormalizeItemId((uint)Math.Max(0, customItemIdInput));
        ImGui.SameLine();
        if (ImGui.Button($"{OmniLoc.Get("Feature.AutoGcSeal.Add")}##autoGcSealAdd") &&
            normalizedItemId > 0 &&
            config.CustomExcludedItemIds.Add(normalizedItemId))
        {
            changed = true;
        }

        if (normalizedItemId > 0)
        {
            ImGui.SameLine(0f, OmniTheme.Scale(10f));
            var itemName = LuminaWrapper.GetItemName(normalizedItemId);
            if (string.IsNullOrWhiteSpace(itemName))
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.AutoGcSeal.InvalidItem"));
            }
            else
            {
                ImGui.TextUnformatted($"{itemName} ({normalizedItemId})");
            }
        }

        DrawExcludedItems(config, ref changed);

        ImGui.Spacing();
        using (ImRaii.Disabled(!enabled))
        {
            if (ImGui.Button($"{OmniLoc.Get("Feature.AutoGcSeal.OpenSupply")}##autoGcSealOpenSupply"))
            {
                openSupply();
            }

            ImGui.SameLine();
            if (ImGui.Button($"{OmniLoc.Get("Feature.AutoGcSeal.OpenExchange")}##autoGcSealOpenExchange"))
            {
                openExchange();
            }
        }

        return changed;
    }

    private static void DrawExcludedItems(AutoGcSealConfig config, ref bool changed)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.AutoGcSeal.ExcludedItems"));
        if (config.CustomExcludedItemIds.Count == 0)
        {
            return;
        }

        var ids = new uint[config.CustomExcludedItemIds.Count];
        config.CustomExcludedItemIds.CopyTo(ids);
        Array.Sort(ids);
        var rowHeight = ImGui.GetFrameHeightWithSpacing();
        using var table = ImRaii.Table(
            "##autoGcSealExcludedItems",
            GridColumns,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingStretchSame,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * VisibleRows + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return;
        }

        uint removeItemId = 0;
        for (var index = 0; index < ids.Length; index++)
        {
            ImGui.TableNextColumn();
            ImGui.PushID((int)ids[index]);
            var itemName = LuminaWrapper.GetItemName(ids[index]);
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(itemName) ? ids[index].ToString() : itemName);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{OmniLoc.Get("Feature.AutoGcSeal.ItemIdTooltip")}: {ids[index]}");
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(OmniLoc.Get("Feature.AutoGcSeal.Delete")))
            {
                removeItemId = ids[index];
            }

            ImGui.PopID();
        }

        if (removeItemId != 0 && config.CustomExcludedItemIds.Remove(removeItemId))
        {
            changed = true;
        }
    }

    private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
    {
        value = current;
        return ImGui.Checkbox($"{OmniLoc.Get(labelKey)}##autoGcSeal{id}", ref value);
    }

    private static uint NormalizeItemId(uint itemId) =>
        itemId < 2_000_000 ? itemId % 500_000 : itemId;

    private static bool DrawUnsignedInput(string labelKey, string id, uint current, out uint value)
    {
        var input = (int)Math.Min(int.MaxValue, current);
        ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
        if (!ImGui.InputInt($"{OmniLoc.Get(labelKey)}##autoGcSeal{id}", ref input))
        {
            value = current;
            return false;
        }

        value = (uint)Math.Max(0, input);
        return true;
    }
}
