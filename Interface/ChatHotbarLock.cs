using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ChatHotbarLock(ChatHotbarLockConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ChatHotbarLockTitle"),
        Description = OmniLoc.Get("ChatHotbarLockDescription"),
        Category = ModuleCategory.Interface
    };

    public override bool HasSettings => true;

    private FeatureLifetime? runtimeLifetime;
    private bool locksVisible;

    private bool ShouldShowLocks
    {
        get
        {
            if (!config.HideLocks)
            {
                return true;
            }

            var keyState = DService.Instance().KeyState;
            return config.ModifierKey switch
            {
                1 => keyState[VirtualKey.MENU] || keyState[(VirtualKey)0xA4] || keyState[(VirtualKey)0xA5],
                2 => keyState[VirtualKey.CONTROL] || keyState[(VirtualKey)0xA2] || keyState[(VirtualKey)0xA3],
                _ => keyState[VirtualKey.SHIFT] || keyState[(VirtualKey)0xA0] || keyState[(VirtualKey)0xA1],
            };
        }
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            // 卸载后恢复游戏原生锁按钮。
            lifetime.Add(() => SetHotbarLockVisible(true));
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, "_ActionBar", OnActionBarAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, "_ActionBar", OnActionBarAddon);
            addonEvents.Register(AddonEvent.PostDraw, "_ActionBar", OnActionBarAddon);

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 16))
            {
                throw new InvalidOperationException("Hotbar lock update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            runtimeLifetime = lifetime;
            locksVisible = ShouldShowLocks;
            SetHotbarLockVisible(locksVisible);
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                runtimeLifetime = null;
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            runtimeLifetime = null;
        }
    }

    public override bool DrawSettings()
    {
        var changed = false;
        using var table = OmniControls.SettingsTable(
            "##chatHotbarLockOptions",
            [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.ChatHotbarLock.HideLocks")),
             OmniControls.MeasureGroup([ImGui.CalcTextSize(OmniLoc.Get("Feature.ChatHotbarLock.Modifier")),
                 OmniControls.MeasureCombo(GetModifierName(config.ModifierKey), OmniTheme.Scale(140f)), OmniControls.HelpIconSize()], ImGui.GetStyle().ItemInnerSpacing.X)],
            ["##chatHotbarLockHideLocks", "##chatHotbarLockModifier"], weights: [1f, 2f],
            flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
        if (!table)
        {
            return false;
        }

        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var hideLocks = config.HideLocks;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.ChatHotbarLock.HideLocks")}##chatHotbarLockHideLocks",
                ref hideLocks))
        {
            config.HideLocks = hideLocks;
            changed = true;
        }

        var modifierKey = config.ModifierKey is 0 or 1 or 2 ? config.ModifierKey : 0;
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ChatHotbarLock.Modifier"));
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.SameLine(0f, spacing);
        if (OmniControls.BeginCombo(
                "##chatHotbarLockModifier",
                GetModifierName(modifierKey),
                MathF.Min(
                    OmniTheme.Scale(140f),
                    MathF.Max(
                        1f,
                        ImGui.GetContentRegionAvail().X -
                        OmniControls.HelpIconSize().X -
                        spacing))))
        {
            if (ImGui.Selectable(GetModifierName(0), modifierKey == 0))
            {
                modifierKey = 0;
            }

            if (ImGui.Selectable(GetModifierName(1), modifierKey == 1))
            {
                modifierKey = 1;
            }

            if (ImGui.Selectable(GetModifierName(2), modifierKey == 2))
            {
                modifierKey = 2;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, spacing);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ChatHotbarLock.Modifier.Help"));
        if (modifierKey != config.ModifierKey)
        {
            config.ModifierKey = modifierKey;
            changed = true;
        }

        return changed;
    }

    private static string GetModifierName(int modifierKey) => OmniLoc.Get(modifierKey switch
    {
        1 => "Feature.ChatHotbarLock.Modifier.Alt",
        2 => "Feature.ChatHotbarLock.Modifier.Control",
        _ => "Feature.ChatHotbarLock.Modifier.Shift"
    });

    private void OnFrameworkUpdate(IFramework _)
    {
        var showLocks = ShouldShowLocks;
        if (locksVisible == showLocks)
        {
            return;
        }

        locksVisible = showLocks;
        SetHotbarLockVisible(showLocks);
    }

    private void OnActionBarAddon(AddonEvent _, AddonArgs args) =>
        SetHotbarLockVisible(locksVisible, (AtkUnitBase*)args.Addon.Address);

    private static void SetHotbarLockVisible(bool visible)
    {
        if (AddonHelper.TryGetByName("_ActionBar", out var addon))
        {
            SetHotbarLockVisible(visible, addon);
        }
    }

    private static void SetHotbarLockVisible(bool visible, AtkUnitBase* addon)
    {
        if (addon == null)
        {
            return;
        }

        var node = addon->GetNodeById(21);
        var lockNode = node == null ? null : node->GetAsAtkComponentNode();
        if (lockNode != null)
        {
            lockNode->AtkResNode.ToggleVisibility(visible);
        }
    }
}

[Serializable]
public sealed class ChatHotbarLockConfig
{
    public bool ShowChatLock { get; set; } = true;

    public bool ChatLocked { get; set; }

    public bool HideLocks { get; set; }

    public int ModifierKey { get; set; }
}
