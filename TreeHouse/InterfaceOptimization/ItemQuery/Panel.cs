using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemQuery;

internal sealed class ItemQueryPanel
{
    private readonly ItemConfig itemConfig;
    private readonly InventoryConfig inventoryConfig;
    private readonly PlayerInventoryService inventoryService;
    private readonly Action refreshItemTooltip;

    public ItemQueryPanel(
        ItemConfig itemConfig,
        InventoryConfig inventoryConfig,
        PlayerInventoryService inventoryService,
        Action refreshItemTooltip)
    {
        this.itemConfig = itemConfig;
        this.inventoryConfig = inventoryConfig;
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
        ImGui.TextWrapped(OmniLoc.Get("Settings.Inventory.Description"));
        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##itemQueryLocationSettings",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        var changed = DrawStatisticsScope();
        changed |= DrawLocationToggle(
            "ItemSearch.Location.Inventory",
            "##showInventory",
            ItemInventoryLocation.Inventory);
        changed |= DrawLocationToggle(
            "Settings.Inventory.ArmoryAndEquipped",
            "##showArmoryAndEquipped",
            ItemInventoryLocation.Armory | ItemInventoryLocation.Equipped);
        changed |= DrawLocationToggle(
            "ItemSearch.Location.Saddlebag",
            "##showSaddlebag",
            ItemInventoryLocation.Saddlebag);
        changed |= DrawLocationToggle("ItemSearch.Location.Retainer", "##showRetainer", ItemInventoryLocation.Retainer);
        changed |= DrawLocationToggle(
            "ItemSearch.Location.FreeCompanyChest",
            "##showFreeCompanyChest",
            ItemInventoryLocation.FreeCompanyChest);
        changed |= DrawLocationToggle(
            "ItemSearch.Location.GlamourDresser",
            "##showGlamourDresser",
            ItemInventoryLocation.GlamourDresser);
        changed |= DrawLocationToggle("ItemSearch.Location.Armoire", "##showArmoire", ItemInventoryLocation.Armoire);
        changed |= DrawLocationToggle(
            "ItemSearch.Location.RetainerMarket",
            "##showRetainerMarket",
            ItemInventoryLocation.RetainerMarket);
        changed |= DrawLocationToggle(
            "ItemSearch.Location.HousingStoreroom",
            "##showHousingStoreroom",
            ItemInventoryLocation.HousingStoreroom);
        return changed;
    }

    private bool DrawStatisticsScope()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.Inventory.StatisticsScope"));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                "##inventoryStatisticsScope",
                OmniLoc.Get(inventoryConfig.StatisticsScope == InventoryStatisticsScope.AllCharacters
                    ? "Settings.Inventory.StatisticsScope.AllCharacters"
                    : "Settings.Inventory.StatisticsScope.CurrentCharacter"),
                ImGui.GetContentRegionAvail().X,
                ImGuiComboFlags.HeightLarge))
        {
            return false;
        }

        var changed = DrawStatisticsScopeOption(
            InventoryStatisticsScope.CurrentCharacter,
            "Settings.Inventory.StatisticsScope.CurrentCharacter");
        changed |= DrawStatisticsScopeOption(
            InventoryStatisticsScope.AllCharacters,
            "Settings.Inventory.StatisticsScope.AllCharacters");
        ImGui.EndCombo();
        return changed;
    }

    private bool DrawStatisticsScopeOption(InventoryStatisticsScope scope, string labelKey)
    {
        var selected = inventoryConfig.StatisticsScope == scope;
        if (!ImGui.Selectable(OmniLoc.Get(labelKey), selected) || selected)
        {
            return false;
        }

        inventoryConfig.StatisticsScope = scope;
        inventoryService.RefreshDisplaySettings();
        refreshItemTooltip();
        return true;
    }

    private bool DrawLocationToggle(string labelKey, string id, ItemInventoryLocation locations)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        var enabled = (itemConfig.InventoryDisplayLocations & locations) == locations;
        if (!OmniControls.Checkbox(id, ref enabled))
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
        ImGui.TextWrapped(OmniLoc.Get("Settings.Tooltip.Description"));
        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##itemQueryTooltipSettings",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        var showOwnedCount = itemConfig.ShowOwnedCountInTooltip;
        var changed = DrawTooltipToggle(
            "Settings.Tooltip.ShowOwnedCount",
            "##showOwnedCountInTooltip",
            ref showOwnedCount);
        if (changed)
        {
            itemConfig.ShowOwnedCountInTooltip = showOwnedCount;
        }

        if (itemConfig.ShowOwnedCountInTooltip)
        {
            changed |= DrawOwnedCountMode();
        }

        var showPatchVersion = itemConfig.ShowPatchVersionInTooltip;
        if (DrawTooltipToggle(
                "Settings.Tooltip.ShowPatchVersion",
                "##showPatchVersionInTooltip",
                ref showPatchVersion))
        {
            itemConfig.ShowPatchVersionInTooltip = showPatchVersion;
            changed = true;
        }

        var hideCraftRepair = itemConfig.HideCraftRepairSectionInTooltip;
        if (DrawTooltipToggle(
                "Settings.Tooltip.HideCraftRepair",
                "##hideCraftRepairSectionInTooltip",
                ref hideCraftRepair))
        {
            itemConfig.HideCraftRepairSectionInTooltip = hideCraftRepair;
            changed = true;
        }

        if (changed)
        {
            refreshItemTooltip();
        }

        return changed;
    }

    private bool DrawOwnedCountMode()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.Tooltip.DisplayMode"));
        ImGui.TableNextColumn();
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

    private static bool DrawTooltipToggle(string labelKey, string id, ref bool value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        return OmniControls.Checkbox(id, ref value);
    }

}
