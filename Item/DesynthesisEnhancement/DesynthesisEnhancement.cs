using Dalamud.Utility;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.Lifecycle;
using OmniToolbox.Tooltips;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreePublic;

public sealed class DesynthesisEnhancement(
    DesynthesisEnhancementConfig config,
    HookRegistry hookRegistry,
    ItemSupplementTooltip itemSupplementTooltip) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DesynthesisEnhancementTitle"),
        Description = OmniLoc.Get("DesynthesisEnhancementDescription"),
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Item/DesynthesisEnhancement-1.png"
    };

    private DesynthesisEnhancementNativeUI? nativeUI;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        if (!DesynthesisEnhancementPanel.Draw(config))
        {
            return false;
        }

        nativeUI?.RefreshSettings();
        itemSupplementTooltip.SetDesynthesisEnabled(IsEnabled, config.ShowTooltipText);
        return true;
    }

    protected override void OnEnable()
    {
        nativeUI = new(config, hookRegistry);
        try
        {
            itemSupplementTooltip.SetDesynthesisEnabled(true, config.ShowTooltipText);
        }
        catch
        {
            nativeUI.Dispose();
            nativeUI = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            itemSupplementTooltip.SetDesynthesisEnabled(false, config.ShowTooltipText);
        }
        finally
        {
            nativeUI?.Dispose();
            nativeUI = null;
        }
    }
}

[Serializable]
public sealed class DesynthesisEnhancementConfig
{
    public bool ShowTooltipText { get; set; } = true;
    public bool LockGearsetItems { get; set; } = true;
    public bool LockCustomItems { get; set; } = true;
    public List<uint> CustomLockItemIds { get; set; } = [];
}

internal static class DesynthesisEnhancementPanel
{
    private static int customItemIDInput;

    public static bool Draw(DesynthesisEnhancementConfig config)
    {
        var changed = DrawSwitches(config);
        if (!config.LockCustomItems || config.CustomLockItemIds.Count == 0)
        {
            return changed;
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        var rows = new ItemSelectionTableRow[config.CustomLockItemIds.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new(config.CustomLockItemIds[index], true, true);
        }

        var change = ItemSelectionTable.Draw("desynthesisCustomLockItems", rows, showEnabledColumn: false);
        if (change.Action == ItemSelectionTableAction.Delete)
        {
            changed |= config.CustomLockItemIds.Remove(change.ItemID);
        }

        return changed;
    }

    private static bool DrawSwitches(DesynthesisEnhancementConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##desynthesisEnhancementSwitches",
            3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##desynthesisSwitch0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##desynthesisSwitch1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##desynthesisSwitch2", ImGuiTableColumnFlags.WidthStretch, 2f);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var value = config.ShowTooltipText;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.TooltipText",
            "desynthesisTooltipText",
            ref value))
        {
            config.ShowTooltipText = value;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.TooltipText.Help"));

        ImGui.TableNextColumn();
        value = config.LockGearsetItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockGearsetItems",
            "desynthesisLockGearsetItems",
            ref value))
        {
            config.LockGearsetItems = value;
            changed = true;
        }

        ImGui.TableNextColumn();
        value = config.LockCustomItems;
        if (DrawCheckbox(
            "Feature.DesynthesisEnhancement.LockCustomItems",
            "desynthesisLockCustomItems",
            ref value))
        {
            config.LockCustomItems = value;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        using (ImRaii.Disabled(!config.LockCustomItems))
        {
            ImGui.SetNextItemWidth(OmniTheme.Scale(120f));
            if (OmniControls.InputInt("##desynthesisCustomItemId", ref customItemIDInput) && customItemIDInput < 0)
            {
                customItemIDInput = 0;
            }

            var itemID = ItemUtil.GetBaseId((uint)Math.Max(0, customItemIDInput)).ItemId;
            ImGui.SameLine();
            if (OmniControls.SmallButton(OmniLoc.Get("Feature.DesynthesisEnhancement.Add"), false) &&
                itemID > 0 &&
                !config.CustomLockItemIds.Contains(itemID))
            {
                config.CustomLockItemIds.Add(itemID);
                changed = true;
            }
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DesynthesisEnhancement.LockCustomItems.Help"));
        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, ref bool value) =>
        OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##{id}", ref value);
}
