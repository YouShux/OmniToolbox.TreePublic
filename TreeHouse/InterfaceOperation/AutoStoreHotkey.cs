using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using OmniToolbox.Config;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal sealed unsafe class AutoStoreHotkey(
    AutoStoreConfig config,
    Func<bool> toggleList)
{
    private const int ControlKey = 0x11;
    private const int ShiftKey = 0x10;
    private const int AltKey = 0x12;
    private const int LeftShiftKey = 0xA0;
    private const int RightShiftKey = 0xA1;
    private const int LeftControlKey = 0xA2;
    private const int RightControlKey = 0xA3;
    private const int LeftAltKey = 0xA4;
    private const int RightAltKey = 0xA5;

    private static readonly int[] BindableKeys = BuildBindableKeys();

    private bool capturing;
    private bool hotkeyHeld;

    public bool IsCapturing => capturing;

    public void Register(FeatureLifetime lifetime)
    {
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
        {
            throw new InvalidOperationException("Auto-store list hotkey registration failed.");
        }

        lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
    }

    public void StartCapture() => capturing = true;

    public bool Capture()
    {
        if (!capturing)
        {
            return false;
        }

        foreach (var key in BindableKeys)
        {
            if (!IsDown(key))
            {
                continue;
            }

            config.ListHotkey = key;
            capturing = false;
            Consume(key, config.ListHotkeyModifier);
            return true;
        }

        return false;
    }

    public void Reset()
    {
        capturing = false;
        hotkeyHeld = false;
    }

    public static string Format(AutoStoreConfig value)
    {
        if (value.ListHotkey == 0)
        {
            return OmniLoc.Get("Feature.AutoStore.Hotkey.Unbound");
        }

        var name = ((VirtualKey)value.ListHotkey).ToString();
        return name.StartsWith("KEY_", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var atkModule = RaptureAtkModule.Instance();
        if (capturing ||
            config.ListHotkey == 0 ||
            ImGui.GetIO().WantTextInput ||
            atkModule != null && atkModule->IsTextInputActive())
        {
            hotkeyHeld = false;
            return;
        }

        var pressed = Array.BinarySearch(BindableKeys, config.ListHotkey) >= 0 &&
                      IsDown(config.ListHotkey) &&
                      IsConfiguredModifierPressed();
        if (!pressed)
        {
            hotkeyHeld = false;
            return;
        }

        Consume(config.ListHotkey, config.ListHotkeyModifier);
        if (hotkeyHeld)
        {
            return;
        }

        hotkeyHeld = true;
        toggleList();
    }

    private bool IsConfiguredModifierPressed()
    {
        var control = IsControlDown();
        var shift = IsShiftDown();
        var alt = IsAltDown();
        return config.ListHotkeyModifier switch
        {
            AutoStoreHotkeyModifier.Control => control && !shift && !alt,
            AutoStoreHotkeyModifier.Shift => shift && !control && !alt,
            AutoStoreHotkeyModifier.Alt => alt && !control && !shift,
            _ => !control && !shift && !alt
        };
    }

    private static void Consume(int key, AutoStoreHotkeyModifier modifier)
    {
        Release(key);
        switch (modifier)
        {
            case AutoStoreHotkeyModifier.Control:
                Release(ControlKey);
                Release(LeftControlKey);
                Release(RightControlKey);
                break;
            case AutoStoreHotkeyModifier.Shift:
                Release(ShiftKey);
                Release(LeftShiftKey);
                Release(RightShiftKey);
                break;
            case AutoStoreHotkeyModifier.Alt:
                Release(AltKey);
                Release(LeftAltKey);
                Release(RightAltKey);
                break;
        }
    }

    private static bool IsShiftDown() => IsDown(ShiftKey) || IsDown(LeftShiftKey) || IsDown(RightShiftKey);

    private static bool IsControlDown() => IsDown(ControlKey) || IsDown(LeftControlKey) || IsDown(RightControlKey);

    private static bool IsAltDown() => IsDown(AltKey) || IsDown(LeftAltKey) || IsDown(RightAltKey);

    private static bool IsDown(int key) => DService.Instance().KeyState[(VirtualKey)key];

    private static void Release(int key) => DService.Instance().KeyState[(VirtualKey)key] = false;

    private static int[] BuildBindableKeys()
    {
        var keys = new List<int>(152);
        for (var key = 0x08; key <= 0x87; key++)
        {
            if (key is not (ShiftKey or ControlKey or AltKey))
            {
                keys.Add(key);
            }
        }

        for (var key = 0xBA; key <= 0xDF; key++)
        {
            if (key is not (LeftShiftKey or RightShiftKey or LeftControlKey or RightControlKey or LeftAltKey or RightAltKey))
            {
                keys.Add(key);
            }
        }

        return [.. keys];
    }
}
