using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed class CopyItemName : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("CopyItemNameTitle"),
        Description = OmniLoc.Get("CopyItemNameDescription"),
        Category = ModuleCategory.Item
    };

    private bool hotkeyHeld;

    protected override void OnEnable()
    {
        if (!FrameworkManager.Instance().Reg(OnUpdate))
        {
            throw new InvalidOperationException("Copy item name update registration failed.");
        }
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnUpdate);
        hotkeyHeld = false;
    }

    private void OnUpdate(IFramework _)
    {
        var keyState = DService.Instance().KeyState;
        if (!keyState[VirtualKey.CONTROL] || !keyState[VirtualKey.C] ||
            keyState[VirtualKey.MENU] || keyState[VirtualKey.SHIFT])
        {
            hotkeyHeld = false;
            return;
        }

        if (hotkeyHeld)
        {
            return;
        }

        hotkeyHeld = true;
        var hoveredItem = DService.Instance().GameGUI.HoveredItem;
        if (hoveredItem == 0)
        {
            return;
        }

        keyState[VirtualKey.C] = false;
        keyState[VirtualKey.CONTROL] = false;
        if (!LuminaGetter.TryGetRow<Item>(ItemUtil.GetBaseId((uint)hoveredItem).ItemId, out var item))
        {
            return;
        }

        var name = item.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        ImGui.SetClipboardText(name);
    }
}
