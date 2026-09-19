using Dalamud.Plugin.Services;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Config;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationMonitorHotkey(
    MitigationMonitorConfig config,
    Action saveConfig)
{
    private const int ESCAPE_KEY = 0x1B;
    private const int CONTROL_KEY = 0x11;
    private const int SHIFT_KEY = 0x10;
    private const int ALT_KEY = 0x12;
    private const int LEFT_SHIFT_KEY = 0xA0;
    private const int RIGHT_SHIFT_KEY = 0xA1;
    private const int LEFT_CONTROL_KEY = 0xA2;
    private const int RIGHT_CONTROL_KEY = 0xA3;
    private const int LEFT_ALT_KEY = 0xA4;
    private const int RIGHT_ALT_KEY = 0xA5;
    private const int NUMPAD0_KEY = 0x60;
    private const int NUMPAD9_KEY = 0x69;
    private const int F1_KEY = 0x70;
    private const int F24_KEY = 0x87;

    private static readonly MitigationHotkeyModifier[] Modifiers =
    [
        MitigationHotkeyModifier.None,
        MitigationHotkeyModifier.Control,
        MitigationHotkeyModifier.Shift,
        MitigationHotkeyModifier.Alt
    ];

    private static readonly HashSet<int> BindableKeys = BuildBindableKeys();

    private bool capturing;
    private bool hotkeyHeld;

    public static bool IsBindable(int key) => key == 0 || BindableKeys.Contains(key);

    public void Register(FeatureLifetime lifetime)
    {
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
        {
            throw new InvalidOperationException("Mitigation monitor hotkey registration failed.");
        }

        lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
    }

    public bool DrawModifierSetting()
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Modifier"));
        ImGui.SameLine();
        if (OmniControls.BeginCombo(
                "##MitigationHideHotkeyModifier",
                GetModifierName(config.HideHotkeyModifier),
                OmniTheme.Scale(90f)))
        {
            foreach (var modifier in Modifiers)
            {
                if (ImGui.Selectable(GetModifierName(modifier), modifier == config.HideHotkeyModifier))
                {
                    config.HideHotkeyModifier = modifier;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    public bool DrawKeySetting()
    {
        var changed = false;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Key"));
        ImGui.SameLine();
        if (OmniControls.SmallButton(
                $"{(capturing ? OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Capturing") : GetDisplayName(config.HideHotkey))}##MitigationHideHotkey",
                false,
                new(OmniTheme.Scale(128f), OmniTheme.SmallButtonSize().Y)))
        {
            capturing = true;
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton($"{OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Clear")}##MitigationHideHotkeyClear", false))
        {
            capturing = false;
            hotkeyHeld = false;
            config.HideHotkey = 0;
            changed = true;
        }

        if (capturing)
        {
            changed |= Capture();
        }

        return changed;
    }

    public void Reset()
    {
        capturing = false;
        hotkeyHeld = false;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!DService.Instance().ClientState.IsLoggedIn ||
            GameState.IsInPVPArea ||
            capturing ||
            config.HideHotkey == 0)
        {
            hotkeyHeld = false;
            return;
        }

        var pressed = BindableKeys.Contains(config.HideHotkey) &&
                      IsDown(config.HideHotkey) &&
                      IsConfiguredModifierPressed();
        if (!pressed)
        {
            hotkeyHeld = false;
            return;
        }

        Consume(config.HideHotkey);
        if (hotkeyHeld)
        {
            return;
        }

        hotkeyHeld = true;
        config.Visible = !config.Visible;
        if (config.Visible)
        {
            config.Collapsed = false;
        }

        saveConfig();
    }

    private bool Capture()
    {
        if (IsDown(ESCAPE_KEY))
        {
            capturing = false;
            return false;
        }

        foreach (var key in BindableKeys)
        {
            if (!IsDown(key))
            {
                continue;
            }

            capturing = false;
            config.HideHotkey = key;
            return true;
        }

        return false;
    }

    private bool IsConfiguredModifierPressed()
    {
        var control = IsDown(CONTROL_KEY) || IsDown(LEFT_CONTROL_KEY) || IsDown(RIGHT_CONTROL_KEY);
        var shift = IsDown(SHIFT_KEY) || IsDown(LEFT_SHIFT_KEY) || IsDown(RIGHT_SHIFT_KEY);
        var alt = IsDown(ALT_KEY) || IsDown(LEFT_ALT_KEY) || IsDown(RIGHT_ALT_KEY);
        return config.HideHotkeyModifier switch
        {
            MitigationHotkeyModifier.Control => control && !shift && !alt,
            MitigationHotkeyModifier.Shift => shift && !control && !alt,
            MitigationHotkeyModifier.Alt => alt && !control && !shift,
            _ => !control && !shift && !alt
        };
    }

    private void Consume(int key)
    {
        ReleaseKey(key);
        switch (config.HideHotkeyModifier)
        {
            case MitigationHotkeyModifier.Control:
                ReleaseKey(CONTROL_KEY);
                ReleaseKey(LEFT_CONTROL_KEY);
                ReleaseKey(RIGHT_CONTROL_KEY);
                break;
            case MitigationHotkeyModifier.Shift:
                ReleaseKey(SHIFT_KEY);
                ReleaseKey(LEFT_SHIFT_KEY);
                ReleaseKey(RIGHT_SHIFT_KEY);
                break;
            case MitigationHotkeyModifier.Alt:
                ReleaseKey(ALT_KEY);
                ReleaseKey(LEFT_ALT_KEY);
                ReleaseKey(RIGHT_ALT_KEY);
                break;
        }
    }

    private static bool IsDown(int key)
    {
        var keyState = DService.Instance().KeyState;
        return keyState.IsVirtualKeyValid(key) && keyState[key];
    }

    private static void ReleaseKey(int key)
    {
        var keyState = DService.Instance().KeyState;
        if (keyState.IsVirtualKeyValid(key))
        {
            keyState[key] = false;
        }
    }

    private static string GetModifierName(MitigationHotkeyModifier modifier) => OmniLoc.Get(modifier switch
    {
        MitigationHotkeyModifier.Control => "Feature.MitigationMonitor.Hotkey.Modifier.Control",
        MitigationHotkeyModifier.Shift => "Feature.MitigationMonitor.Hotkey.Modifier.Shift",
        MitigationHotkeyModifier.Alt => "Feature.MitigationMonitor.Hotkey.Modifier.Alt",
        _ => "Feature.MitigationMonitor.Hotkey.Modifier.None"
    });

    private static string GetDisplayName(int key)
    {
        if (key == 0)
        {
            return OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Unbound");
        }

        if (key is >= F1_KEY and <= F24_KEY)
        {
            return $"F{key - F1_KEY + 1}";
        }

        if (key is >= 0x41 and <= 0x5A || key is >= 0x30 and <= 0x39)
        {
            return ((char)key).ToString();
        }

        if (key is >= NUMPAD0_KEY and <= NUMPAD9_KEY)
        {
            return $"Num{key - NUMPAD0_KEY}";
        }

        return key switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "LWin",
            0x5C => "RWin",
            _ => OmniLoc.Get("Feature.MitigationMonitor.Hotkey.Unbound")
        };
    }

    private static HashSet<int> BuildBindableKeys()
    {
        var keys = new HashSet<int>
        {
            0x08, 0x09, 0x0D, 0x20, 0x21, 0x22, 0x23, 0x24,
            0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C
        };
        for (var key = 0x30; key <= 0x39; key++)
        {
            keys.Add(key);
        }

        for (var key = 0x41; key <= 0x5A; key++)
        {
            keys.Add(key);
        }

        for (var key = NUMPAD0_KEY; key <= NUMPAD9_KEY; key++)
        {
            keys.Add(key);
        }

        for (var key = F1_KEY; key <= F24_KEY; key++)
        {
            keys.Add(key);
        }

        return keys;
    }
}
