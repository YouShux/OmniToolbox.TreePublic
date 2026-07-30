using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

public sealed class CopyItemNameFeature : TreeHouseFeatureBase
{
    private bool hotkeyHeld;

    public CopyItemNameFeature() : base(new(
        "copy-item-name",
        "Feature.CopyItemName.Title",
        "Feature.CopyItemName.Description",
        TreeHouseCategory.General))
    {
    }

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
        if (hoveredItem < 2_000_000)
        {
            hoveredItem %= 500_000;
        }

        if (!LuminaGetter.TryGetRow<Item>((uint)hoveredItem, out var item))
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
