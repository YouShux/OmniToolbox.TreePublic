using OmniToolbox.UI.Widgets;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using OmenTools;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using TreeToolKit;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryWindow(LootRollHistory feature) : IEscapeClosableWindow
    {
        private readonly EscapeCloseController escape = new(DService.Instance().KeyState);
        private readonly OmniRecordWindow frame = new(feature.config, WINDOW_NAME, COLLAPSED_NAME,
            new Vector2(460f, 150f), () => feature.SaveHostConfig?.Invoke());
        private bool capturingKey;
        private bool hideKeyHeld;
        private int keySettingFrame;

        public bool IsOpen { get; private set; } = feature.config.Visible;

        public bool IsFocused => IsOpen && frame.IsFocused;

        public void Open(bool requestFocus = true)
        {
            var settingsChanged = !feature.config.Visible || feature.config.Collapsed;
            IsOpen = true;
            frame.Open(requestFocus);
            if (settingsChanged)
            {
                feature.config.Visible = true;
                feature.SaveHostConfig?.Invoke();
            }
        }

        public void Close()
        {
            frame.Close();
            IsOpen = false;
            capturingKey = false;
            if (!feature.Activation.IsStopping && feature.config.Visible)
            {
                feature.config.Visible = false;
                feature.SaveHostConfig?.Invoke();
            }
        }

        public void ToggleVisibility()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Draw()
        {
            FinishTransfer();
            if (!IsOpen)
            {
                OmniWindowChrome.UpdateEscapeClose(escape, this);
                return;
            }

            frame.Draw(feature.Title, $"{feature.Title} · {Text("Expand")}", DrawContent,
                () => OmniWindowChrome.UpdateEscapeClose(escape, this));
        }

        private void DrawContent()
        {
            using var scale = new OmniTheme.ScaleScope(ImGui.GetFontSize() / OmniTheme.REFERENCE_FONT_SIZE);
            if (!capturingKey && !ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId))
                OmniWindowChrome.UpdateEscapeClose(escape, this);
            if (!IsOpen)
                return;
            RefreshRows();
            if (page == HistoryPage.Loot)
                DrawLootRecords();
            else
            {
                DrawToolbar();
                if (page != HistoryPage.Loot)
                    DrawRecords();
            }
        }

        private void DrawToolbar()
        {
            var cursor = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            if (page == HistoryPage.Details &&
                OmniRecordWindow.DrawHeaderButton("##lootBack", FontAwesomeIcon.ArrowLeft,
                    OmniTheme.Scale(new Vector2(22f, 20f)), Text("Back")))
            {
                page = HistoryPage.Loot;
                restoreListScroll = true;
            }
            cursor.X += MathF.Max(0f, width - GetHeaderControlsWidth());
            ImGui.SetCursorScreenPos(cursor);
            DrawHeaderControls();
        }

        private void DrawHeaderControls()
        {
            var size = OmniTheme.Scale(new Vector2(22f, 20f));
            var spacing = OmniTheme.Scale(3f);
            var historyLabel = SelectedSession is { } selected
                ? $"{Text("History")} · {Array.Find(Sessions, entry => entry.ID == selected)?.Label}" : Text("History");
            if (OmniRecordWindow.DrawHeaderButton("##lootHistory", FontAwesomeIcon.History, size, historyLabel))
                ImGui.OpenPopup(HISTORY_POPUP);
            ImGui.SameLine(0f, spacing);
            var collapsePosition = ImGui.GetCursorScreenPos();
            if (OmniRecordWindow.DrawHeaderButton("##lootCollapse", FontAwesomeIcon.ChevronCircleUp, size, Text("Collapse")))
                frame.Collapse(collapsePosition);
            ImGui.SameLine(0f, spacing);
            if (OmniRecordWindow.DrawHeaderButton("##lootLock", feature.config.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                    size, Text(feature.config.Locked ? "Unlock" : "Lock")))
            {
                feature.config.Locked ^= true;
                feature.SaveHostConfig?.Invoke();
            }
            ImGui.SameLine(0f, spacing);
            if (OmniRecordWindow.DrawHeaderButton("##lootSettings", FontAwesomeIcon.Cog, size, Text("Settings")))
                feature.openSettingsAction?.Invoke();
            OmniRecordHistoryMenu.Draw(HISTORY_POPUP, Sessions, this);
        }

        private static float GetHeaderControlsWidth() => OmniTheme.Scale(22f * 4f + 3f * 3f);

        public bool DrawHideKeySetting()
        {
            keySettingFrame = ImGui.GetFrameCount();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Text("HideKey"));
            var keyLabel = capturingKey ? Text("Capturing") : feature.config.HideKey == 0
                ? Text("Unbound") : ((VirtualKey)feature.config.HideKey).ToString();
            OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(keyLabel).X);
            if (OmniControls.SmallButton($"{keyLabel}##lootHideKey", capturingKey))
                capturingKey = true;
            OmniControls.SameLineOrWrap(OmniControls.CompactButtonSize(Text("ClearKey")).X);
            if (OmniControls.SmallButton($"{Text("ClearKey")}##lootClearKey", false))
            {
                capturingKey = false;
                var changed = feature.config.HideKey != 0;
                feature.config.HideKey = 0;
                return changed;
            }
            if (!capturingKey)
                return false;
            var keys = DService.Instance().KeyState;
            if (keys[VirtualKey.ESCAPE])
            {
                capturingKey = false;
                keys[VirtualKey.ESCAPE] = false;
                return false;
            }
            foreach (var key in keys.GetValidVirtualKeys())
            {
                if ((int)key is < 0x30 or > 0x87 || !keys[key])
                    continue;
                feature.config.HideKey = (int)key;
                keys[key] = false;
                capturingKey = false;
                hideKeyHeld = true;
                return true;
            }
            return false;
        }

        public float ModifierSettingWidth => OmniTextMeasurements.Measure(Text("Modifier")).X +
                                             ImGui.GetStyle().ItemSpacing.X + OmniTheme.Scale(72f);

        public float KeySettingWidth => OmniTextMeasurements.Measure(Text("HideKey")).X +
                                        OmniControls.CompactButtonSize(capturingKey ? Text("Capturing") :
                                            feature.config.HideKey == 0 ? Text("Unbound") : ((VirtualKey)feature.config.HideKey).ToString()).X +
                                        OmniControls.CompactButtonSize(Text("ClearKey")).X + ImGui.GetStyle().ItemSpacing.X * 2f;

        public bool DrawModifierSetting()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Text("Modifier"));
            OmniControls.SameLineOrWrap(OmniTheme.Scale(72f));
            if (!OmniControls.BeginCombo("##lootModifier", Text($"Modifier.{feature.config.HideKeyModifier}"), OmniTheme.Scale(72f)))
                return false;
            var changed = false;
            foreach (var modifier in Enum.GetValues<LootHotkeyModifier>())
                if (ImGui.Selectable(Text($"Modifier.{modifier}"), feature.config.HideKeyModifier == modifier))
                {
                    changed = feature.config.HideKeyModifier != modifier;
                    feature.config.HideKeyModifier = modifier;
                }
            ImGui.EndCombo();
            return changed;
        }

        public void UpdateHotkey()
        {
            if (capturingKey && ImGui.GetFrameCount() - keySettingFrame > 1)
                capturingKey = false;
            var keys = DService.Instance().KeyState;
            var key = feature.config.HideKey;
            var control = keys[VirtualKey.CONTROL] || keys[VirtualKey.LCONTROL] || keys[VirtualKey.RCONTROL];
            var shift = keys[VirtualKey.SHIFT] || keys[VirtualKey.LSHIFT] || keys[VirtualKey.RSHIFT];
            var alt = keys[VirtualKey.MENU] || keys[VirtualKey.LMENU] || keys[VirtualKey.RMENU];
            var modifierMatches = feature.config.HideKeyModifier switch
            {
                LootHotkeyModifier.Control => control && !shift && !alt,
                LootHotkeyModifier.Shift => shift && !control && !alt,
                LootHotkeyModifier.Alt => alt && !control && !shift,
                _ => !control && !shift && !alt
            };
            if (capturingKey || key == 0 || !keys.IsVirtualKeyValid(key) ||
                !DService.Instance().ClientState.IsLoggedIn || ImGui.GetIO().WantTextInput ||
                !modifierMatches)
            {
                hideKeyHeld = false;
                return;
            }
            var pressed = keys[key];
            if (pressed)
            {
                keys[key] = false;
                var modifierKey = feature.config.HideKeyModifier switch
                {
                    LootHotkeyModifier.Control => VirtualKey.CONTROL,
                    LootHotkeyModifier.Shift => VirtualKey.SHIFT,
                    LootHotkeyModifier.Alt => VirtualKey.MENU,
                    _ => (VirtualKey)0
                };
                if (modifierKey != 0)
                {
                    keys[modifierKey] = false;
                    var leftKey = feature.config.HideKeyModifier switch
                    {
                        LootHotkeyModifier.Control => VirtualKey.LCONTROL,
                        LootHotkeyModifier.Shift => VirtualKey.LSHIFT,
                        _ => VirtualKey.LMENU
                    };
                    keys[leftKey] = false;
                    keys[(int)leftKey + 1] = false;
                }
                if (!hideKeyHeld)
                    ToggleVisibility();
            }
            hideKeyHeld = pressed;
        }

        #region 常量

        private const string WINDOW_NAME = "###LootRollHistoryWindow";
        private const string COLLAPSED_NAME = "###LootRollHistoryCollapsed";
        private const string HISTORY_POPUP = "##LootHistoryMenu";

        #endregion
    }
}
