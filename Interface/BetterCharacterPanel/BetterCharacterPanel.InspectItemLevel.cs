using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterCharacterPanel
{
    internal sealed unsafe partial class BetterCharacterPanelNativeUI
    {
        private ResNode? inspectItemLevelDisplay;
        private TextNode? inspectItemLevelText;
        private int displayedInspectItemLevel = -1;

        private void UpdateInspectItemLevel(AtkUnitBase* addon)
        {
            if (!config.ShowTargetItemLevel)
            {
                ClearInspectItemLevel();
                return;
            }

            if (!addon->IsAddonAndNodesReady())
                return;

            var model = addon->GetNodeById(41);
            if (model == null || model->ParentNode == null)
                return;

            if (inspectItemLevelDisplay == null)
            {
                inspectItemLevelDisplay = new()
                {
                    Size = new(80f, 24f),
                    IsVisible = false
                };
                inspectItemLevelDisplay.AttachNode(model->ParentNode);

                var icon = new SimpleImageNode
                {
                    Position = new(2f, 1f),
                    Size = new(24f),
                    TexturePath = "ui/uld/Character.tex",
                    TextureCoordinates = new(176f, 136f),
                    TextureSize = new(24f)
                };
                icon.AttachNode(inspectItemLevelDisplay);

                inspectItemLevelText = new()
                {
                    Size = new(80f, 24f),
                    FontType = FontType.Miedinger,
                    FontSize = 16,
                    AlignmentType = AlignmentType.Right,
                    LineSpacing = 24,
                    TextFlags = 0,
                    TextColor = ColorHelper.GetColor(41),
                    TextOutlineColor = ColorHelper.GetColor(7),
                    TextTooltip = LuminaWrapper.GetAddonText(2329)
                };
                inspectItemLevelText.AttachNode(inspectItemLevelDisplay);
            }

            inspectItemLevelDisplay.Position = new(model->X + model->Width - 88f, model->Y);

            var state = UIState.Instance();
            var agent = AgentInspect.Instance();
            var ready = state != null && agent != null &&
                        !state->Inspect.IsInspectRequested &&
                        state->Inspect.Type == 4 &&
                        state->Inspect.EntityId == agent->CurrentEntityId &&
                        agent->FetchCharacterDataStatus is 0 or 2;
            inspectItemLevelDisplay.IsVisible = ready;
            if (!ready || inspectItemLevelText == null || displayedInspectItemLevel == state->Inspect.AverageItemLevel)
                return;

            displayedInspectItemLevel = state->Inspect.AverageItemLevel;
            inspectItemLevelText.SetNumber(displayedInspectItemLevel, digits: 4, zeroPad: true);
        }

        private void ClearInspectItemLevel()
        {
            inspectItemLevelDisplay?.Dispose();
            inspectItemLevelDisplay = null;
            inspectItemLevelText = null;
            displayedInspectItemLevel = -1;
        }
    }
}
