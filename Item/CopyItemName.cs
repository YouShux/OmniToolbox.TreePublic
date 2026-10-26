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

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed class CopyItemName : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Item
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "CopyItemNameTitle",
            ["Description"] = "CopyItemNameDescription"
        });

    private bool hotkeyHeld;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));

        if (!FrameworkManager.Instance().Reg(OnUpdate))
        {
            throw new InvalidOperationException("Copy item name update registration failed.");
        }
    }

    protected override void OnStop()
    {
        hotkeyHeld = false;
    }

    private void OnUpdate(IFramework _)
    {
        if (!CopyShortcut.IsPressed(ref hotkeyHeld))
            return;
        var hoveredItem = DService.Instance().GameGUI.HoveredItem;
        if (hoveredItem == 0)
        {
            return;
        }

        CopyShortcut.Consume();
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
