using OmniToolbox.UI.Widgets;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private bool DrawWindowSettings()
    {
        var changed = false;
        using var table = ImRaii.Table("##lootSettings", 1, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return false;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var inputWidth = OmniTheme.Scale(120f);
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.LootRollHistory.Scale"));
            OmniControls.SameLineOrWrap(inputWidth);
            OmniControls.SliderFloat("##lootScale", ref config.GlobalScale, 0.5f, 2f, "%.1fx", inputWidth);
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }
        var opacityLabel = OmniLoc.Get(this, "Feature.LootRollHistory.Opacity");
        OmniControls.SameLineOrWrap(OmniControls.MeasureGroup([OmniTextMeasurements.Measure(opacityLabel), new Vector2(inputWidth, 0f)]).X);
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(opacityLabel);
            OmniControls.SameLineOrWrap(inputWidth);
            var opacity = config.Opacity * 100f;
            if (OmniControls.SliderFloat("##lootOpacity", ref opacity, 0f, 100f, "%.0f%%", inputWidth))
                config.Opacity = opacity / 100f;
            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }
        if (window is { } historyWindow)
        {
            OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(OmniLoc.Get(this, "Feature.LootRollHistory.Open")).X);
            if (OmniControls.SmallButton(OmniLoc.Get(this, "Feature.LootRollHistory.Open"), false))
                historyWindow.Open();
            OmniControls.SameLineOrWrap(historyWindow.ModifierSettingWidth);
            using (ImRaii.Group())
                changed |= historyWindow.DrawModifierSetting();
            OmniControls.SameLineOrWrap(historyWindow.KeySettingWidth);
            using (ImRaii.Group())
                changed |= historyWindow.DrawHideKeySetting();
        }
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.LootRollHistory.ReplaySaveCount"));
        OmniControls.SameLineOrWrap(OmniTheme.Scale(96f));
        OmniControls.InputInt("##lootReplaySaveCount", ref config.ReplaySaveCount, OmniTheme.Scale(96f));
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            config.ReplaySaveCount = Math.Clamp(config.ReplaySaveCount, 1, MAX_LOOT_SESSIONS);
            storage?.SetLootLimit(config.ReplaySaveCount);
            changed = true;
        }
        OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(OmniLoc.Get(this, "Feature.LootRollHistory.ClearRecords")).X);
        using (ImRaii.Disabled(characterID == 0 || storage?.GetSnapshot(characterID) is not { LootStatus: not HistoryStatus.ReadFailed }))
            if (OmniControls.SmallButton(OmniLoc.Get(this, "Feature.LootRollHistory.ClearRecords"), false))
                ClearRecords();
        return changed;
    }

    private void NormalizeSettings()
    {
        config.GlobalScale = float.IsFinite(config.GlobalScale) ? Math.Clamp(config.GlobalScale, 0.5f, 2f) : 1f;
        config.Opacity = float.IsFinite(config.Opacity) ? Math.Clamp(config.Opacity, 0f, 1f) : 0.7f;
        config.ReplaySaveCount = Math.Clamp(config.ReplaySaveCount, 1, MAX_LOOT_SESSIONS);
        if (!Enum.IsDefined(config.HideKeyModifier))
            config.HideKeyModifier = LootHotkeyModifier.None;
        if (!float.IsFinite(config.Position.X) || !float.IsFinite(config.Position.Y))
            config.Position = new(520f, 320f);
        if (!float.IsFinite(config.Size.X) || !float.IsFinite(config.Size.Y))
            config.Size = new(660f, 280f);
        config.Size = Vector2.Max(config.Size, new(460f, 150f));
        if (!float.IsFinite(config.CollapsedPosition.X) || !float.IsFinite(config.CollapsedPosition.Y))
            config.CollapsedPosition = config.Position;
    }

    internal sealed class Settings : IOmniRecordWindowSettings
    {
        public bool Visible = true;
        public float GlobalScale = 1f;
        public float Opacity = 0.7f;
        public int HideKey;
        public LootHotkeyModifier HideKeyModifier;
        public int ReplaySaveCount = 100;
        public bool Locked;
        public bool Collapsed;
        public Vector2 Position = new(520f, 320f);
        public Vector2 CollapsedPosition;
        public Vector2 Size = new(660f, 280f);

        bool IOmniRecordWindowSettings.Locked => Locked;
        bool IOmniRecordWindowSettings.Collapsed { get => Collapsed; set => Collapsed = value; }
        Vector2 IOmniRecordWindowSettings.Position { get => Position; set => Position = value; }
        Vector2 IOmniRecordWindowSettings.CollapsedPosition { get => CollapsedPosition; set => CollapsedPosition = value; }
        Vector2 IOmniRecordWindowSettings.Size { get => Size; set => Size = value; }
        float IOmniRecordWindowSettings.GlobalScale => GlobalScale;
        float IOmniRecordWindowSettings.Opacity => Opacity;
    }

    internal enum LootHotkeyModifier
    {
        None,
        Control,
        Shift,
        Alt
    }
}
