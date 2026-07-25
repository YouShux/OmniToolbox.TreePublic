using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.Script;

internal static class TreasureMapPanel
{
    private static int itemIdInput;

    public static void Normalize(TreasureMapConfig config) => config.PriceLimit = Math.Max(0, config.PriceLimit);

    public static bool Draw(
        TreasureMapConfig config,
        ITreasureMapService service,
        bool featureEnabled,
        bool privateProviderAvailable,
        string privateProviderReasonKey)
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##treasureMapOptions",
                   4,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var blockOtherTreasureTargets = config.BlockOtherTreasureTargets;
                changed |= DrawCheckbox(
                    "Feature.TreasureMap.BlockOthers",
                    "blockOthers",
                    ref blockOtherTreasureTargets);
                config.BlockOtherTreasureTargets = blockOtherTreasureTargets;
                ImGui.TableNextColumn();
                var openMapAfterDecipher = config.OpenMapAfterDecipher;
                changed |= DrawCheckbox(
                    "Feature.TreasureMap.OpenMap",
                    "openMap",
                    ref openMapAfterDecipher,
                    "Feature.TreasureMap.OpenMap.Tooltip");
                config.OpenMapAfterDecipher = openMapAfterDecipher;
                ImGui.TableNextColumn();
                var collectMoneyBags = config.CollectMoneyBags;
                using (ImRaii.Disabled(!privateProviderAvailable))
                {
                    changed |= DrawCheckbox(
                        "Feature.TreasureMap.CollectMoneyBags",
                        "collectMoneyBags",
                        ref collectMoneyBags,
                        privateProviderAvailable
                            ? "Feature.TreasureMap.CollectMoneyBags.Tooltip"
                            : privateProviderReasonKey);
                }

                config.CollectMoneyBags = collectMoneyBags;
                ImGui.TableNextColumn();
                var enableOneClickPurchase = config.EnableOneClickPurchase;
                changed |= DrawCheckbox(
                    "Feature.TreasureMap.OneClick",
                    "oneClick",
                    ref enableOneClickPurchase,
                    "Feature.TreasureMap.OneClick.Tooltip");
                config.EnableOneClickPurchase = enableOneClickPurchase;
            }
        }

        var teleportAfterDecipher = config.TeleportAfterDecipher;
        changed |= DrawCheckbox(
            "Feature.TreasureMap.AutoTeleport",
            "autoTeleport",
            ref teleportAfterDecipher,
            "Feature.TreasureMap.AutoTeleport.Tooltip");
        config.TeleportAfterDecipher = teleportAfterDecipher;

        ImGui.Spacing();
        using (var table = ImRaii.Table(
                   "##treasureMapPurchase",
                   3,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.TreasureMap.PriceLimit"));
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var priceLimit = config.PriceLimit;
                if (OmniControls.InputInt("##treasureMapPriceLimit", ref priceLimit))
                {
                    config.PriceLimit = Math.Max(0, priceLimit);
                }

                changed |= ImGui.IsItemDeactivatedAfterEdit();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(OmniLoc.Get("Feature.TreasureMap.ItemId"));
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (OmniControls.InputInt("##treasureMapItemId", ref itemIdInput))
                {
                    itemIdInput = Math.Max(0, itemIdInput);
                }

                ImGui.TableNextColumn();
                ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeightWithSpacing()));
                using (ImRaii.Disabled(
                           !featureEnabled ||
                           service.IsBusy ||
                           !config.EnableOneClickPurchase ||
                           itemIdInput == 0))
                {
                    if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.TreasureMap.Start")}##treasureMapStart", false))
                    {
                        service.RequestStart((uint)itemIdInput, config.PriceLimit);
                    }
                }

                ImGui.SameLine();
                using (ImRaii.Disabled(!service.IsBusy))
                {
                    if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.TreasureMap.Stop")}##treasureMapStop", false))
                    {
                        service.Cancel();
                    }
                }
            }
        }

        if (itemIdInput > 0)
        {
            var itemName = LuminaWrapper.GetItemName((uint)itemIdInput);
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(itemName)
                ? OmniLoc.Get("Feature.TreasureMap.InvalidItem")
                : itemName);
        }

        ImGui.TextDisabled(OmniLoc.Get(service.StatusKey));
        ImGui.TextDisabled(OmniLoc.Get("Feature.TreasureMap.Command"));
        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        ref bool value,
        string tooltipKey = "")
    {
        var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##treasureMap{id}", ref value);
        if (tooltipKey.Length != 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(OmniLoc.Get(tooltipKey));
        }

        return changed;
    }
}
