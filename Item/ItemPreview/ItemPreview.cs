using OmniToolbox.Collections;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Tooltips;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class ItemPreview : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ItemPreviewTitle"),
        Description = OmniLoc.Get("ItemPreviewDescription"),
        Category = ModuleCategory.Item
    };

    private const string SupplementPreviewImageBaseURL =
        "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/ItemPreview-";

    private readonly ItemConfig config;
    private readonly ItemPreviewService itemPreviewService;
    private readonly ItemSupplementTooltip itemSupplementTooltip;
    private readonly CollectableValueTooltip collectableValueTooltip;
    private readonly MateriaTotalTooltip materiaTotalTooltip;
    private ItemDetailImagePreview? imagePreview;

    public ItemPreview(
        ItemConfig config,
        ItemPreviewService itemPreviewService,
        ItemSupplementTooltip itemSupplementTooltip,
        ItemTooltipInventoryContext inventoryContext)
    {
        this.config = config;
        this.itemPreviewService = itemPreviewService;
        this.itemSupplementTooltip = itemSupplementTooltip;
        collectableValueTooltip = new(inventoryContext);
        materiaTotalTooltip = new(inventoryContext);
    }

    public override bool HasSettings => true;

    public bool CanPreview(uint? itemID) => IsEnabled && itemPreviewService.CanPreview(itemID);

    public bool CanPreview(CollectionEntry item) => IsEnabled && itemPreviewService.CanPreview(item);

    public bool Preview(uint itemID) => IsEnabled && itemPreviewService.Preview(itemID);

    public bool Preview(CollectionEntry item) => IsEnabled && itemPreviewService.Preview(item);

    public bool TryOn(uint itemID) => IsEnabled && itemPreviewService.TryOn(itemID);

    public bool ExecuteEmote(uint emoteID) => itemPreviewService.ExecuteEmote(emoteID);

    public uint ResolveCollectionIcon(CollectionEntry item) => itemPreviewService.ResolveCollectionIcon(item);

    public void ClearPreview() => itemPreviewService.ClearPreview();

    public void ClearPreviewResidue() => itemPreviewService.ClearPreviewResidue();

    public override bool DrawSettings()
    {
        OmniControls.SectionLabel(OmniLoc.Get("Settings.Tooltip.Title"));
        ImGui.Spacing();
        var tooltipChanged = DrawSupplementSettings();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        OmniControls.SectionLabel(OmniLoc.Get("Settings.ImagePreview.Title"));
        OmniControls.PreviewImageIcon(
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/ItemPreview-1.png");
        ImGui.Spacing();
        if (!DrawImageSettings(out var imageContentChanged) && !tooltipChanged)
        {
            return false;
        }

        if (imageContentChanged)
        {
            imagePreview?.Refresh();
        }

        if (tooltipChanged)
        {
            itemSupplementTooltip.SetPreviewEnabled(IsEnabled);
            collectableValueTooltip.SetEnabled(IsEnabled && config.ShowCollectableValueInTooltip);
            materiaTotalTooltip.SetEnabled(IsEnabled && config.ShowMateriaTotalInTooltip);
        }

        return true;
    }

    protected override void OnEnable()
    {
        imagePreview = new(config.ImagePreview, itemPreviewService);
        try
        {
            itemSupplementTooltip.SetPreviewEnabled(true);
            collectableValueTooltip.SetEnabled(config.ShowCollectableValueInTooltip);
            materiaTotalTooltip.SetEnabled(config.ShowMateriaTotalInTooltip);
        }
        catch
        {
            materiaTotalTooltip.SetEnabled(false);
            collectableValueTooltip.SetEnabled(false);
            itemSupplementTooltip.SetPreviewEnabled(false);
            imagePreview?.Dispose();
            imagePreview = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            itemSupplementTooltip.SetPreviewEnabled(false);
            collectableValueTooltip.SetEnabled(false);
            materiaTotalTooltip.SetEnabled(false);
            itemPreviewService.ClearPreview();
        }
        finally
        {
            imagePreview?.Dispose();
            imagePreview = null;
        }
    }

    private bool DrawSupplementSettings()
    {
        using var table = ImRaii.Table(
            "##itemSupplementPreviewSettingsTable",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##supplement1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##supplement2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##supplement3", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##supplement4", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = false;
        var showShopSource = config.ShowShopSourceInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowShopSource",
                "##showShopSourceInTooltip",
                null,
                ref showShopSource,
                $"{SupplementPreviewImageBaseURL}8.png"))
        {
            config.ShowShopSourceInTooltip = showShopSource;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showDutySource = config.ShowDutySourceInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowDutySource",
                "##showDutySourceInTooltip",
                null,
                ref showDutySource,
                $"{SupplementPreviewImageBaseURL}9.png"))
        {
            config.ShowDutySourceInTooltip = showDutySource;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showConsumableEffects = config.ShowConsumableEffectsInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowConsumableEffects",
                "##showConsumableEffectsInTooltip",
                null,
                ref showConsumableEffects,
                $"{SupplementPreviewImageBaseURL}2.png"))
        {
            config.ShowConsumableEffectsInTooltip = showConsumableEffects;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showBlindBoxProgress = config.ShowBlindBoxProgressInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowBlindBoxProgress",
                "##showBlindBoxProgressInTooltip",
                null,
                ref showBlindBoxProgress,
                $"{SupplementPreviewImageBaseURL}3.png"))
        {
            config.ShowBlindBoxProgressInTooltip = showBlindBoxProgress;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showFadedOrchestrionProgress = config.ShowFadedOrchestrionProgressInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowFadedOrchestrionProgress",
                "##showFadedOrchestrionProgressInTooltip",
                null,
                ref showFadedOrchestrionProgress,
                $"{SupplementPreviewImageBaseURL}4.png"))
        {
            config.ShowFadedOrchestrionProgressInTooltip = showFadedOrchestrionProgress;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showCollectableValue = config.ShowCollectableValueInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowCollectableValue",
                "##showCollectableValueInTooltip",
                null,
                ref showCollectableValue,
                $"{SupplementPreviewImageBaseURL}5.png"))
        {
            config.ShowCollectableValueInTooltip = showCollectableValue;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showMateriaTotal = config.ShowMateriaTotalInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowMateriaTotal",
                "##showMateriaTotalInTooltip",
                null,
                ref showMateriaTotal,
                $"{SupplementPreviewImageBaseURL}6.png"))
        {
            config.ShowMateriaTotalInTooltip = showMateriaTotal;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showItemSetContents = config.ShowItemSetContentsInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowItemSets",
                "##showItemSetContentsInTooltip",
                null,
                ref showItemSetContents,
                $"{SupplementPreviewImageBaseURL}7.png"))
        {
            config.ShowItemSetContentsInTooltip = showItemSetContents;
            changed = true;
        }

        return changed;
    }

    private bool DrawImageSettings(out bool imageContentChanged)
    {
        imageContentChanged = DrawImageToggles();
        ImGui.Spacing();
        using var table = ImRaii.Table(
            "##itemImagePreviewOptionsTable",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return imageContentChanged;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        var changed = imageContentChanged;
        changed |= DrawScale();
        changed |= DrawPosition();
        return changed;
    }

    private bool DrawImageToggles()
    {
        using var table = ImRaii.Table(
            "##itemImagePreviewTogglesTable",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##preview1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##preview2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##preview3", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##preview4", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = false;
        var showMounts = config.ImagePreview.ShowMounts;
        if (DrawToggle("Settings.ImagePreview.Mounts", "##showMountImagePreview", null, ref showMounts))
        {
            config.ImagePreview.ShowMounts = showMounts;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showMinions = config.ImagePreview.ShowMinions;
        if (DrawToggle("Settings.ImagePreview.Minions", "##showMinionImagePreview", null, ref showMinions))
        {
            config.ImagePreview.ShowMinions = showMinions;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showHairstyles = config.ImagePreview.ShowHairstyles;
        if (DrawToggle("Settings.ImagePreview.Hairstyles", "##showHairstyleImagePreview", null, ref showHairstyles))
        {
            config.ImagePreview.ShowHairstyles = showHairstyles;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showFashionAccessories = config.ImagePreview.ShowFashionAccessories;
        if (DrawToggle(
                "Settings.ImagePreview.FashionAccessories",
                "##showFashionAccessoryImagePreview",
                null,
                ref showFashionAccessories))
        {
            config.ImagePreview.ShowFashionAccessories = showFashionAccessories;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showExchangeRewards = config.ImagePreview.ShowExchangeRewards;
        if (DrawToggle(
                "Settings.ImagePreview.ExchangeRewards",
                "##showExchangeRewardImagePreview",
                "Settings.ImagePreview.ExchangeRewards.Help",
                ref showExchangeRewards))
        {
            config.ImagePreview.ShowExchangeRewards = showExchangeRewards;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showPaintings = config.ImagePreview.ShowPaintings;
        if (DrawToggle("Settings.ImagePreview.Paintings", "##showPaintingImagePreview", null, ref showPaintings))
        {
            config.ImagePreview.ShowPaintings = showPaintings;
            changed = true;
        }

        return changed;
    }

    private bool DrawScale()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.ImagePreview.Scale"));
        ImGui.TableNextColumn();
        var scale = config.ImagePreview.Scale;
        if (OmniControls.SliderFloat(
                "##itemImagePreviewScale",
                ref scale,
                0.1f,
                3f,
                "%.2f",
                ImGui.GetContentRegionAvail().X))
        {
            config.ImagePreview.Scale = scale;
        }

        return ImGui.IsItemDeactivatedAfterEdit();
    }

    private bool DrawPosition()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Settings.ImagePreview.Position"));
        ImGui.TableNextColumn();
        if (!OmniControls.BeginCombo(
                "##itemImagePreviewPosition",
                OmniLoc.Get(config.ImagePreview.Position == ItemImagePreviewPosition.Left
                    ? "Settings.ImagePreview.Position.Left"
                    : "Settings.ImagePreview.Position.Right"),
                ImGui.GetContentRegionAvail().X))
        {
            return false;
        }

        var changed = DrawPositionOption(ItemImagePreviewPosition.Right, "Settings.ImagePreview.Position.Right");
        changed |= DrawPositionOption(ItemImagePreviewPosition.Left, "Settings.ImagePreview.Position.Left");
        ImGui.EndCombo();
        return changed;
    }

    private bool DrawPositionOption(ItemImagePreviewPosition position, string labelKey)
    {
        var selected = config.ImagePreview.Position == position;
        if (!ImGui.Selectable(OmniLoc.Get(labelKey), selected) || selected)
        {
            return false;
        }

        config.ImagePreview.Position = position;
        return true;
    }

    private static bool DrawToggle(
        string labelKey,
        string id,
        string? helpKey,
        ref bool value,
        string? previewImageURL = null)
    {
        var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}{id}", ref value);
        if (previewImageURL is not null)
        {
            OmniControls.PreviewImageIcon(previewImageURL);
        }

        if (helpKey is not null)
        {
            ImGui.SameLine(0f, OmniTheme.Scale(6f));
            OmniControls.HelpIcon(OmniLoc.Get(helpKey));
        }

        return changed;
    }
}
