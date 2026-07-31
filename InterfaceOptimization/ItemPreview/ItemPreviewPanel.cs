using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPreview;

internal sealed class ItemPreviewPanel(ItemConfig config)
{
    public bool Draw(out bool tooltipChanged, out bool imageContentChanged)
    {
        OmniControls.SectionLabel(OmniLoc.Get("Settings.Tooltip.Title"));
        ImGui.Spacing();
        tooltipChanged = DrawSupplementSettings();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        OmniControls.SectionLabel(OmniLoc.Get("Settings.ImagePreview.Title"));
        ImGui.Spacing();
        return DrawImageSettings(out imageContentChanged) || tooltipChanged;
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
        var showConsumableEffects = config.ShowConsumableEffectsInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowConsumableEffects",
                "##showConsumableEffectsInTooltip",
                null,
                ref showConsumableEffects))
        {
            config.ShowConsumableEffectsInTooltip = showConsumableEffects;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showBlindBoxProgress = config.ShowBlindBoxProgressInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowBlindBoxProgress",
                "##showBlindBoxProgressInTooltip",
                "Settings.Tooltip.ShowBlindBoxProgress.Help",
                ref showBlindBoxProgress))
        {
            config.ShowBlindBoxProgressInTooltip = showBlindBoxProgress;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showFadedOrchestrionProgress = config.ShowFadedOrchestrionProgressInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowFadedOrchestrionProgress",
                "##showFadedOrchestrionProgressInTooltip",
                null,
                ref showFadedOrchestrionProgress))
        {
            config.ShowFadedOrchestrionProgressInTooltip = showFadedOrchestrionProgress;
            changed = true;
        }

        ImGui.TableNextColumn();
        var showCollectableValue = config.ShowCollectableValueInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowCollectableValue",
                "##showCollectableValueInTooltip",
                "Settings.Tooltip.ShowCollectableValue.Help",
                ref showCollectableValue))
        {
            config.ShowCollectableValueInTooltip = showCollectableValue;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showMateriaTotal = config.ShowMateriaTotalInTooltip;
        if (DrawToggle(
                "Settings.Tooltip.ShowMateriaTotal",
                "##showMateriaTotalInTooltip",
                null,
                ref showMateriaTotal))
        {
            config.ShowMateriaTotalInTooltip = showMateriaTotal;
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

    private static bool DrawToggle(string labelKey, string id, string? helpKey, ref bool value)
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
