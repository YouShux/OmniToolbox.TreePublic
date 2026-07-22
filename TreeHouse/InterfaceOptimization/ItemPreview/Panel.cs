using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization.ItemPreview;

internal sealed class ItemPreviewPanel(ItemConfig config)
{
    public bool Draw(out bool tooltipChanged, out bool imageContentChanged)
    {
        tooltipChanged = DrawSupplementSettings();
        ImGui.Dummy(new Vector2(0f, OmniTheme.ContentGap()));
        OmniControls.SectionLabel(OmniLoc.Get("Settings.ImagePreview.Title"));
        ImGui.TextWrapped(OmniLoc.Get("Settings.ImagePreview.Description"));
        ImGui.Spacing();
        return DrawImageSettings(out imageContentChanged) || tooltipChanged;
    }

    private bool DrawSupplementSettings()
    {
        using var table = ImRaii.Table(
            "##itemSupplementPreviewSettingsTable",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        var changed = false;
        var showConsumableEffects = config.ShowConsumableEffectsInTooltip;
        if (DrawCheckboxRow(
                "Settings.Tooltip.ShowConsumableEffects",
                "##showConsumableEffectsInTooltip",
                ref showConsumableEffects))
        {
            config.ShowConsumableEffectsInTooltip = showConsumableEffects;
            changed = true;
        }

        var showBlindBoxProgress = config.ShowBlindBoxProgressInTooltip;
        if (DrawCheckboxRow(
                "Settings.Tooltip.ShowBlindBoxProgress",
                "##showBlindBoxProgressInTooltip",
                ref showBlindBoxProgress))
        {
            config.ShowBlindBoxProgressInTooltip = showBlindBoxProgress;
            changed = true;
        }

        var showFadedOrchestrionProgress = config.ShowFadedOrchestrionProgressInTooltip;
        if (DrawCheckboxRow(
                "Settings.Tooltip.ShowFadedOrchestrionProgress",
                "##showFadedOrchestrionProgressInTooltip",
                ref showFadedOrchestrionProgress))
        {
            config.ShowFadedOrchestrionProgressInTooltip = showFadedOrchestrionProgress;
            changed = true;
        }

        return changed;
    }

    private bool DrawImageSettings(out bool imageContentChanged)
    {
        using var table = ImRaii.Table(
            "##itemImagePreviewSettingsTable",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            imageContentChanged = false;
            return false;
        }

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.65f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.35f);
        imageContentChanged = DrawImageToggles();
        var changed = imageContentChanged;
        changed |= DrawScale();
        changed |= DrawPosition();
        return changed;
    }

    private bool DrawImageToggles()
    {
        var changed = false;
        var showMounts = config.ImagePreview.ShowMounts;
        if (DrawCheckboxRow("Settings.ImagePreview.Mounts", "##showMountImagePreview", ref showMounts))
        {
            config.ImagePreview.ShowMounts = showMounts;
            changed = true;
        }

        var showMinions = config.ImagePreview.ShowMinions;
        if (DrawCheckboxRow("Settings.ImagePreview.Minions", "##showMinionImagePreview", ref showMinions))
        {
            config.ImagePreview.ShowMinions = showMinions;
            changed = true;
        }

        var showHairstyles = config.ImagePreview.ShowHairstyles;
        if (DrawCheckboxRow("Settings.ImagePreview.Hairstyles", "##showHairstyleImagePreview", ref showHairstyles))
        {
            config.ImagePreview.ShowHairstyles = showHairstyles;
            changed = true;
        }

        var showPaintings = config.ImagePreview.ShowPaintings;
        if (DrawCheckboxRow("Settings.ImagePreview.Paintings", "##showPaintingImagePreview", ref showPaintings))
        {
            config.ImagePreview.ShowPaintings = showPaintings;
            changed = true;
        }

        var showFashionAccessories = config.ImagePreview.ShowFashionAccessories;
        if (DrawCheckboxRow(
                "Settings.ImagePreview.FashionAccessories",
                "##showFashionAccessoryImagePreview",
                ref showFashionAccessories))
        {
            config.ImagePreview.ShowFashionAccessories = showFashionAccessories;
            changed = true;
        }

        var showExchangeRewards = config.ImagePreview.ShowExchangeRewards;
        if (DrawCheckboxRow(
                "Settings.ImagePreview.ExchangeRewards",
                "##showExchangeRewardImagePreview",
                ref showExchangeRewards))
        {
            config.ImagePreview.ShowExchangeRewards = showExchangeRewards;
            changed = true;
        }

        var showUnlockStatus = config.ImagePreview.ShowUnlockStatus;
        if (DrawCheckboxRow(
                "Settings.ImagePreview.UnlockStatus",
                "##showImagePreviewUnlockStatus",
                ref showUnlockStatus))
        {
            config.ImagePreview.ShowUnlockStatus = showUnlockStatus;
            changed = true;
        }

        return changed;
    }

    private bool DrawScale()
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
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

    private static bool DrawCheckboxRow(string labelKey, string id, ref bool value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        return OmniControls.Checkbox(id, ref value);
    }
}
