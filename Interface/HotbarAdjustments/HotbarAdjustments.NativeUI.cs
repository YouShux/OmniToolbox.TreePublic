using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using OmenTools;
using OmenTools.Interop.Game.Helpers;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HotbarAdjustments
{
    private bool ShouldShowLocks
    {
        get
        {
            if (!config.HideLocks)
                return true;

            var keyState = DService.Instance().KeyState;
            return config.ModifierKey switch
            {
                1 => keyState[VirtualKey.MENU] || keyState[(VirtualKey)0xA4] || keyState[(VirtualKey)0xA5],
                2 => keyState[VirtualKey.CONTROL] || keyState[(VirtualKey)0xA2] || keyState[(VirtualKey)0xA3],
                _ => keyState[VirtualKey.SHIFT] || keyState[(VirtualKey)0xA0] || keyState[(VirtualKey)0xA1]
            };
        }
    }

    private void OnActionBarAddon
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        var addon = (AddonActionBarBase*)args.Addon.Address;
        UpdateBackgrounds(args.AddonName, addon, config.HideBackground);
        if (args.AddonName == "_ActionBar")
            SetHotbarLockVisible((AddonActionBar*)addon, ShouldShowLocks);
    }

    private void OnActionBarFinalize
    (
        AddonEvent _,
        AddonArgs  args
    )
    {
        var addon = (AddonActionBarBase*)args.Addon.Address;
        UpdateBackgrounds(args.AddonName, addon, false);
    }

    private void UpdateAllHotbars(bool restore)
    {
        foreach (var addonName in AddonNames)
        {
            if (!AddonHelper.TryGetByName(addonName, out AddonActionBarBase* addon))
                continue;

            UpdateBackgrounds(addonName, addon, !restore && config.HideBackground);
            if (addonName == "_ActionBar")
                SetHotbarLockVisible((AddonActionBar*)addon, restore || ShouldShowLocks);
        }

        if (restore)
            slotBackgrounds.Clear();
    }

    private void UpdateBackgrounds
    (
        string              addonName,
        AddonActionBarBase* addon,
        bool                adjust
    )
    {
        if (addon is null || (!adjust && slotBackgrounds.Count == 0))
            return;

        var hotbarModule = adjust ? RaptureHotbarModule.Instance() : null;
        if (adjust && hotbarModule is null)
            return;

        var opacity = float.IsFinite(config.BackgroundOpacity) ? Math.Clamp(config.BackgroundOpacity, 0f, 1f) : 0f;
        var slots = addon->ActionBarSlotVector.AsSpan();
        for (var index = 0; index < slots.Length; index++)
        {
            var dragDrop = slots[index].ComponentDragDrop;
            var background = dragDrop is null ? null : dragDrop->GetImageNodeById(3);
            if (background is null)
                continue;

            var key = (addonName, index);
            var hotbarSlot = adjust ? hotbarModule->GetSlotById(addon->RaptureHotbarId, (uint)index) : null;
            if (hotbarSlot is not null && hotbarSlot->CommandType != RaptureHotbarModule.HotbarSlotType.Empty)
            {
                if (!slotBackgrounds.TryGetValue(key, out var state))
                {
                    state = new BackgroundState(background->AtkResNode.IsVisible(),
                        new Vector2(background->X, background->Y), new Vector2(background->Width, background->Height),
                        new Vector2(background->ScaleX, background->ScaleY), background->Color.A);
                    slotBackgrounds.Add(key, state);
                }
                var icon = dragDrop->AtkComponentIcon;
                var frame = icon is null ? null : icon->Frame;
                var width = frame is not null && frame->Width > 0 ? frame->Width : (ushort)state.Size.X;
                var height = frame is not null && frame->Height > 0 ? frame->Height : (ushort)state.Size.Y;
                background->SetPositionFloat(state.Position.X + (state.Size.X - width) * 0.5f, state.Position.Y);
                if (state.Size.X > 0f && state.Size.Y > 0f)
                    background->SetScale(state.Scale.X * width / state.Size.X, state.Scale.Y * height / state.Size.Y);
                background->Color.A = (byte)MathF.Round(state.Alpha * opacity);
                background->AtkResNode.ToggleVisibility(opacity > 0f);
            }
            else if (slotBackgrounds.Remove(key, out var state))
            {
                background->SetPositionFloat(state.Position.X, state.Position.Y);
                background->SetScale(state.Scale.X, state.Scale.Y);
                background->Color.A = state.Alpha;
                background->AtkResNode.ToggleVisibility(state.WasVisible);
            }
        }
    }

    private static void SetHotbarLockVisible
    (
        AddonActionBar* addon,
        bool            visible
    )
    {
        var checkbox = addon->PadlockCheckbox;
        if (checkbox is not null && checkbox->OwnerNode is not null)
            checkbox->OwnerNode->AtkResNode.ToggleVisibility(visible);
    }

    private readonly record struct BackgroundState
    (
        bool    WasVisible,
        Vector2 Position,
        Vector2 Size,
        Vector2 Scale,
        byte    Alpha
    );
}
