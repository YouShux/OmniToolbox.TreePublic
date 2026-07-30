using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemQuery;

internal sealed class ItemQueryPanel
{
    private readonly ItemConfig itemConfig;
    private readonly PlayerInventoryService inventoryService;
    private readonly Action refreshItemTooltip;

    public ItemQueryPanel(
        ItemConfig itemConfig,
        PlayerInventoryService inventoryService,
        Action refreshItemTooltip)
    {
        this.itemConfig = itemConfig;
        this.inventoryService = inventoryService;
        this.refreshItemTooltip = refreshItemTooltip;
    }

    public bool Draw()
    {
        var changed = DrawLocationSettings();
        ImGui.Dummy(new Vector2(0f, OmniTheme.ContentGap()));
        changed |= DrawTooltipSettings();
        return changed;
    }

    private bool DrawLocationSettings()
    {
        OmniControls.SectionLabel(OmniLoc.Get("Settings.Inventory.Title"));
        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##itemQueryLocationSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##location1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##location2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##location3", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##location4", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = DrawLocationToggle(
            "ItemSearch.Location.Inventory",
            "##showInventory",
            ItemInventoryLocation.Inventory);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "Settings.Inventory.ArmoryAndEquipped",
            "##showArmoryAndEquipped",
            ItemInventoryLocation.Armory | ItemInventoryLocation.Equipped);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.Saddlebag",
            "##showSaddlebag",
            ItemInventoryLocation.Saddlebag);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle("ItemSearch.Location.Retainer", "##showRetainer", ItemInventoryLocation.Retainer);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.FreeCompanyChest",
            "##showFreeCompanyChest",
            ItemInventoryLocation.FreeCompanyChest);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.GlamourDresser",
            "##showGlamourDresser",
            ItemInventoryLocation.GlamourDresser);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle("ItemSearch.Location.Armoire", "##showArmoire", ItemInventoryLocation.Armoire);
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.RetainerMarket",
            "##showRetainerMarket",
            ItemInventoryLocation.RetainerMarket);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.HousingStoreroom",
            "##showHousingStoreroom",
            ItemInventoryLocation.HousingStoreroom);
        return changed;
    }

    private bool DrawLocationToggle(string labelKey, string id, ItemInventoryLocation locations)
    {
        var enabled = (itemConfig.InventoryDisplayLocations & locations) == locations;
        if (!OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}{id}", ref enabled))
        {
            return false;
        }

        itemConfig.InventoryDisplayLocations = enabled
            ? itemConfig.InventoryDisplayLocations | locations
            : itemConfig.InventoryDisplayLocations & ~locations;
        inventoryService.RefreshDisplaySettings();
        refreshItemTooltip();
        return true;
    }

    private bool DrawTooltipSettings()
    {
        OmniControls.SectionLabel(OmniLoc.Get("Settings.Tooltip.Title"));
        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##itemQueryTooltipSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##tooltip1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##tooltip2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##tooltip3", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##tooltip4", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showOwnedCount = itemConfig.ShowOwnedCountInTooltip;
        var changed = DrawTooltipToggle(
            "Settings.Tooltip.ShowOwnedCount",
            "##showOwnedCountInTooltip",
            "Settings.Tooltip.ShowOwnedCount.Help",
            ref showOwnedCount);
        if (changed)
        {
            itemConfig.ShowOwnedCountInTooltip = showOwnedCount;
        }

        ImGui.TableNextColumn();
        var showPatchVersion = itemConfig.ShowPatchVersionInTooltip;
        if (DrawTooltipToggle(
                "Settings.Tooltip.ShowPatchVersion",
                "##showPatchVersionInTooltip",
                null,
                ref showPatchVersion))
        {
            itemConfig.ShowPatchVersionInTooltip = showPatchVersion;
            changed = true;
        }

        ImGui.TableNextColumn();
        var hideCraftRepair = itemConfig.HideCraftRepairSectionInTooltip;
        if (DrawTooltipToggle(
                "Settings.Tooltip.HideCraftRepair",
                "##hideCraftRepairSectionInTooltip",
                "Settings.Tooltip.HideCraftRepair.Help",
                ref hideCraftRepair))
        {
            itemConfig.HideCraftRepairSectionInTooltip = hideCraftRepair;
            changed = true;
        }

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(!itemConfig.ShowOwnedCountInTooltip))
        {
            changed |= DrawOwnedCountMode();
        }

        if (changed)
        {
            refreshItemTooltip();
        }

        return changed;
    }

    private bool DrawOwnedCountMode()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.Tooltip.DisplayMode"));
        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        if (!OmniControls.BeginCombo(
                "##ownedCountDisplayMode",
                OmniLoc.Get(itemConfig.OwnedCountDisplayMode == OwnedCountDisplayMode.Detailed
                    ? "Settings.Tooltip.Mode.Detailed"
                    : "Settings.Tooltip.Mode.Brief"),
                ImGui.GetContentRegionAvail().X,
                ImGuiComboFlags.HeightLarge))
        {
            return false;
        }

        var changed = DrawOwnedCountModeOption(OwnedCountDisplayMode.Brief, "Settings.Tooltip.Mode.Brief");
        changed |= DrawOwnedCountModeOption(OwnedCountDisplayMode.Detailed, "Settings.Tooltip.Mode.Detailed");
        ImGui.EndCombo();
        return changed;
    }

    private bool DrawOwnedCountModeOption(OwnedCountDisplayMode mode, string labelKey)
    {
        var selected = itemConfig.OwnedCountDisplayMode == mode;
        if (!ImGui.Selectable(OmniLoc.Get(labelKey), selected) || selected)
        {
            return false;
        }

        itemConfig.OwnedCountDisplayMode = mode;
        return true;
    }

    private static bool DrawTooltipToggle(string labelKey, string id, string? helpKey, ref bool value)
    {
        var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}{id}", ref value);
        if (helpKey is not null)
        {
            ImGui.SameLine(0f, OmniTheme.Scale(6f));
            OmniControls.HelpIcon(OmniLoc.Get(helpKey));
        }

        return changed;
    }
}
