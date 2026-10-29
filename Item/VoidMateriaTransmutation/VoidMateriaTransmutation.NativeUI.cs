using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class VoidMateriaTransmutation
{
    private TextButtonNode?                                                                                    batchButton;
    private bool                                                                                               displayedRunning;
    private (Vector2 Position, ushort Width, ushort LabelWidth, ushort BackgroundWidth, ushort CollisionWidth) manualButtonLayout;

    private void UpdateBatchButton(AtkUnitBase* addon)
    {
        if (!ownsWindow || !hasSceneOverrides || !addon->IsAddonAndNodesReady())
            return;

        var running = batchTasks?.IsBusy == true;
        if (batchButton is null)
        {
            var manual = addon->GetComponentButtonById(5);
            var collision = manual == null ? null : manual->UldManager.SearchNodeById(4);
            if (manual == null || manual->OwnerNode == null || manual->ButtonTextNode == null ||
                manual->ButtonBGNode == null || collision == null)
                return;

            var node = manual->OwnerNode;
            manualButtonLayout = (new(node->X, node->Y), node->Width, manual->ButtonTextNode->Width,
                manual->ButtonBGNode->Width, collision->Width);
            const float MARGIN = 16f;
            const float GAP = 8f;
            var width = (ushort)MathF.Floor((addon->RootNode->Width - MARGIN * 2f - GAP) / 2f);
            var delta = width - manualButtonLayout.Width;
            node->SetPositionFloat(MARGIN, manualButtonLayout.Position.Y);
            node->SetWidth(width);
            manual->ButtonTextNode->SetWidth((ushort)(manualButtonLayout.LabelWidth + delta));
            manual->ButtonBGNode->SetWidth((ushort)(manualButtonLayout.BackgroundWidth + delta));
            collision->SetWidth((ushort)(manualButtonLayout.CollisionWidth + delta));
            displayedRunning               = running;
            batchButton                    = new()
            {
                Position = new(MARGIN + width + GAP, manualButtonLayout.Position.Y),
                Size = new(width, node->Height),
                String = OmniLoc.Get(this, running
                    ? "Feature.VoidMateriaTransmutation.BatchStop"
                    : "Feature.VoidMateriaTransmutation.BatchStart"),
                OnClick = ToggleBatch
            };
            batchButton.LabelNode.FontSize = manual->ButtonTextNode->FontSize;
            batchButton.AttachNode(addon->RootNode);
        }

        if (displayedRunning != running)
        {
            displayedRunning   = running;
            batchButton.String = OmniLoc.Get(this, running
                ? "Feature.VoidMateriaTransmutation.BatchStop"
                : "Feature.VoidMateriaTransmutation.BatchStart");
        }

        batchButton.IsEnabled = running || GetSelectionAgent() != null;
    }

    private void ReleaseBatchButton()
    {
        if (batchButton is null)
            return;

        batchButton.Dispose();
        batchButton = null;
        if (!AddonHelper.TryGetByName("TradeMultiple", out var addon) || !addon->IsAddonAndNodesReady())
            return;

        var manual = addon->GetComponentButtonById(5);
        if (manual == null || manual->OwnerNode == null)
            return;

        manual->OwnerNode->SetPositionFloat(manualButtonLayout.Position.X, manualButtonLayout.Position.Y);
        manual->OwnerNode->SetWidth(manualButtonLayout.Width);
        if (manual->ButtonTextNode != null)
            manual->ButtonTextNode->SetWidth(manualButtonLayout.LabelWidth);
        if (manual->ButtonBGNode != null)
            manual->ButtonBGNode->SetWidth(manualButtonLayout.BackgroundWidth);
        var collision = manual->UldManager.SearchNodeById(4);
        if (collision != null)
        {
            collision->SetWidth(manualButtonLayout.CollisionWidth);
        }
    }
}
