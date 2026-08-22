using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ChatHotbarLock(ChatHotbarLockConfig config, Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ChatHotbarLockTitle"),
        Description = OmniLoc.Get("ChatHotbarLockDescription"),
        Category = ModuleCategory.Interface
    };

    private static readonly string[] ChatAddonNames =
        ["ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3"];
    private static readonly Vector2 ButtonSize = new(20f, 24f);
    private static readonly Vector2 LockedTextureCoordinates = new(88f, 0f);
    private static readonly Vector2 UnlockedTextureCoordinates = new(48f, 0f);

    private readonly Dictionary<string, TextureButtonNode> chatLockButtons = new(StringComparer.Ordinal);
    private FeatureLifetime? runtimeLifetime;
    private Hook<AtkUnitBase.Delegates.MoveDelta>? moveDeltaHook;
    private bool locksVisible;
    private bool showChatLock;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##chatHotbarLockOptions",
            3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##chatHotbarLockShowChatLock", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##chatHotbarLockHideLocks", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##chatHotbarLockModifier", ImGuiTableColumnFlags.WidthStretch, 2f);
        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var showChatLock = config.ShowChatLock;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.ChatHotbarLock.ShowChatLock")}##chatHotbarLockShowChatLock",
                ref showChatLock))
        {
            config.ShowChatLock = showChatLock;
            if (!showChatLock)
            {
                config.ChatLocked = false;
            }

            changed = true;
        }

        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        OmniControls.HelpIcon(OmniLoc.Get("Feature.ChatHotbarLock.ShowChatLock.Help"));

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

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            // 按注册顺序逆序释放：框架、插件事件、按钮、Hook，最后恢复原生显示。
            lifetime.Add(() => SetHotbarLockVisible(true));
            moveDeltaHook = DService.Instance().Hook.HookFromAddress<AtkUnitBase.Delegates.MoveDelta>(
                AtkUnitBase.Addresses.MoveDelta.Value,
                OnMoveDelta);
            lifetime.Add(moveDeltaHook.Dispose);
            moveDeltaHook.Enable();
            lifetime.Add(DisposeChatLockButtons);

            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            foreach (var addonName in ChatAddonNames)
            {
                addonEvents.Register(AddonEvent.PostSetup, addonName, OnChatAddon);
                addonEvents.Register(AddonEvent.PostRefresh, addonName, OnChatAddon);
                addonEvents.Register(AddonEvent.PostRequestedUpdate, addonName, OnChatAddon);
                addonEvents.Register(AddonEvent.PostDraw, addonName, OnChatAddon);
                addonEvents.Register(AddonEvent.PreFinalize, addonName, OnChatAddon);
            }

            addonEvents.Register(AddonEvent.PostSetup, "_ActionBar", OnActionBarAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, "_ActionBar", OnActionBarAddon);
            addonEvents.Register(AddonEvent.PostDraw, "_ActionBar", OnActionBarAddon);

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 16))
            {
                throw new InvalidOperationException("Chat/hotbar lock update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            runtimeLifetime = lifetime;
            locksVisible = ShouldShowLocks;
            showChatLock = config.ShowChatLock;
            RefreshChatAddons(locksVisible);
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
                moveDeltaHook = null;
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
            moveDeltaHook = null;
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var showLocks = ShouldShowLocks;
        if (locksVisible == showLocks && showChatLock == config.ShowChatLock)
        {
            return;
        }

        locksVisible = showLocks;
        showChatLock = config.ShowChatLock;
        RefreshChatAddons(showLocks);
        SetHotbarLockVisible(showLocks);
    }

    private void OnChatAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            DisposeChatLockButton(args.AddonName);
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null)
        {
            return;
        }

        EnsureChatLockButton(args.AddonName, addon);
        UpdateChatLockButton(args.AddonName, addon, locksVisible);
    }

    private void OnActionBarAddon(AddonEvent _, AddonArgs args) =>
        SetHotbarLockVisible(locksVisible, (AtkUnitBase*)args.Addon.Address);

    private void RefreshChatAddons(bool showLocks)
    {
        foreach (var addonName in ChatAddonNames)
        {
            if (!AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon))
            {
                DisposeChatLockButton(addonName);
                continue;
            }

            EnsureChatLockButton(addonName, addon);
            UpdateChatLockButton(addonName, addon, showLocks);
        }
    }

    private void EnsureChatLockButton(string addonName, AtkUnitBase* addon)
    {
        if (chatLockButtons.TryGetValue(addonName, out var existingButton))
        {
            if (RaptureAtkUnitManager.Instance()->GetAddonByNode((AtkResNode*)existingButton) == addon)
            {
                return;
            }

            DisposeChatLockButton(addonName);
        }

        var attachTarget = addonName == "ChatLog"
            ? addon->RootNode
            : ((AddonChatLogPanel*)addon)->ContainerNode;
        if (attachTarget == null)
        {
            return;
        }

        var button = CreateChatLockButton();
        button.AttachNode(attachTarget);
        chatLockButtons[addonName] = button;
    }

    private TextureButtonNode CreateChatLockButton()
    {
        var button = new TextureButtonNode
        {
            Size = ButtonSize,
            TexturePath = "ui/uld/ActionBar.tex",
            TextureCoordinates = config.ChatLocked ? LockedTextureCoordinates : UnlockedTextureCoordinates,
            TextureSize = ButtonSize,
            IsVisible = config.ShowChatLock && ShouldShowLocks,
            ShowClickableCursor = true,
        };
        button.SetTextTooltip(GetChatLockTooltip());
        button.ImageNode.Scale = new(0.9f, 0.9f);
        button.ImageNode.Origin = ButtonSize / 2f;
        button.OnClick = () => OnChatLockButtonClicked(button);
        return button;
    }

    private void UpdateChatLockButton(string addonName, AtkUnitBase* addon, bool showLocks)
    {
        if (!chatLockButtons.TryGetValue(addonName, out var button))
        {
            return;
        }

        var isMainChat = addonName == "ChatLog";
        var anchor = addon->GetNodeById(isMainChat ? 11u : 6u);
        if (anchor == null)
        {
            return;
        }

        var scale = AtkUnitBase.GetGlobalUIScale();
        button.Position = new(
            anchor->GetXFloat() + (isMainChat ? 50f : 32f) * scale,
            anchor->GetYFloat() + (isMainChat ? 2f : 2f * scale));
        button.Scale = new(scale, scale);
        button.IsVisible = addon->IsVisible && config.ShowChatLock && showLocks;
        UpdateChatLockButtonState(button);
    }

    private void OnChatLockButtonClicked(TextureButtonNode clickedButton)
    {
        config.ChatLocked = !config.ChatLocked;
        saveConfig();
        foreach (var button in chatLockButtons.Values)
        {
            UpdateChatLockButtonState(button);
        }

        clickedButton.ShowTooltip();
    }

    private void UpdateChatLockButtonState(TextureButtonNode button)
    {
        var textureCoordinates = config.ChatLocked ? LockedTextureCoordinates : UnlockedTextureCoordinates;
        if (button.TextureCoordinates == textureCoordinates)
        {
            return;
        }

        button.TextureCoordinates = textureCoordinates;
        button.SetTextTooltip(GetChatLockTooltip());
    }

    private string GetChatLockTooltip() => OmniLoc.Get(config.ChatLocked
        ? "Feature.ChatHotbarLock.Tooltip.Unlock"
        : "Feature.ChatHotbarLock.Tooltip.Lock");

    private void DisposeChatLockButtons()
    {
        foreach (var button in chatLockButtons.Values)
        {
            button.Dispose();
        }

        chatLockButtons.Clear();
    }

    private void DisposeChatLockButton(string addonName)
    {
        if (chatLockButtons.Remove(addonName, out var button))
        {
            button.Dispose();
        }
    }

    private bool OnMoveDelta(AtkUnitBase* addon, short* xDelta, short* yDelta)
    {
        if (config.ShowChatLock &&
            config.ChatLocked &&
            addon != null &&
            addon->NameString.StartsWith("ChatLog", StringComparison.Ordinal))
        {
            *xDelta = 0;
            *yDelta = 0;
            return false;
        }

        return moveDeltaHook!.Original(addon, xDelta, yDelta);
    }

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

    private static void SetHotbarLockVisible(bool visible)
    {
        if (AddonHelper.TryGetByName("_ActionBar", out AtkUnitBase* addon))
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
