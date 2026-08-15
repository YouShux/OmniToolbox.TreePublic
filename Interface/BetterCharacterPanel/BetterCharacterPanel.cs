using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreePublic;

public sealed class BetterCharacterPanel(BetterCharacterPanelConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("BetterCharacterPanelTitle"),
        Description = OmniLoc.Get("BetterCharacterPanelDescription"),
        Category = ModuleCategory.Interface,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Interface/BetterCharacterPanel-1.png"
    };

    private BetterCharacterPanelNativeUI? nativeUI;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!BetterCharacterPanelPanel.Draw(config))
        {
            return false;
        }

        nativeUI?.RefreshSettings();
        return true;
    }

    protected override void OnEnable()
    {
        try
        {
            nativeUI = new(config);
        }
        catch
        {
            nativeUI = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            nativeUI?.Dispose();
        }
        finally
        {
            nativeUI = null;
        }
    }
}

[Serializable]
public sealed class BetterCharacterPanelConfig
{
    public bool ShowUsefulStats { get; set; } = true;

    public bool AdjustEquipmentPositions { get; set; } = true;

    public bool SoulstoneAboveOffhand { get; set; } = true;

    public bool ReverseCharacterPanel { get; set; } = true;

    public bool ShowGearSetReorderButtons { get; set; } = true;
}

internal static class BetterCharacterPanelPanel
{
    public static bool Draw(BetterCharacterPanelConfig config)
    {
        var changed = false;
        var style = ImGui.GetStyle();
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
        using var itemSpacing = ImRaii.PushStyle(
            ImGuiStyleVar.ItemSpacing,
            new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using (var table = ImRaii.Table(
                   "##betterCharacterPanelOptions",
                   4,
                   ImGuiTableFlags.SizingStretchProp,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##c0", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c1", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c2", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##c3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.ShowUsefulStats",
                        "usefulStats",
                        config.ShowUsefulStats,
                        out var showUsefulStats))
                {
                    config.ShowUsefulStats = showUsefulStats;
                    changed = true;
                }
                ImGuiOm.HelpMarker(OmniLoc.Get("Feature.BetterCharacterPanel.ShowUsefulStats.Help"));

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.ReverseCharacterPanel",
                        "reverse",
                        config.ReverseCharacterPanel,
                        out var reverseCharacterPanel))
                {
                    config.ReverseCharacterPanel = reverseCharacterPanel;
                    changed = true;
                }

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.ShowGearSetReorderButtons",
                        "gearSetReorder",
                        config.ShowGearSetReorderButtons,
                        out var showGearSetReorderButtons))
                {
                    config.ShowGearSetReorderButtons = showGearSetReorderButtons;
                    changed = true;
                }

                ImGui.TableNextColumn();
                if (DrawCheckbox(
                        "Feature.BetterCharacterPanel.AdjustEquipmentPositions",
                        "equipmentPositions",
                        config.AdjustEquipmentPositions,
                        out var adjustEquipmentPositions))
                {
                    config.AdjustEquipmentPositions = adjustEquipmentPositions;
                    changed = true;
                }

                ImGui.Spacing();
                ImGui.Indent(OmniTheme.Scale(26f));
                using (ImRaii.Disabled(!config.AdjustEquipmentPositions))
                {
                    if (DrawCheckbox(
                            "Feature.BetterCharacterPanel.SoulstoneAboveOffhand",
                            "soulstoneAboveOffhand",
                            config.SoulstoneAboveOffhand,
                            out var soulstoneAboveOffhand))
                    {
                        config.SoulstoneAboveOffhand = soulstoneAboveOffhand;
                        changed = true;
                    }
                }

                ImGui.Unindent(OmniTheme.Scale(26f));
            }
        }
        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        bool value,
        out bool updatedValue)
    {
        updatedValue = value;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterCharacterPanel{id}", ref updatedValue);
    }
}
