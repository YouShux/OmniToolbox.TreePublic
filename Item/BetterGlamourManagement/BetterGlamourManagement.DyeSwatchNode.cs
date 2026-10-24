using System.Drawing;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourDyeSwatchNode : BetterGlamourDyeColorNode
    {
        private BetterGlamourDyeGridRow? row;

        public BetterGlamourDyeSwatchNode(int index) : base(0f, 0f, 0f)
        {
            CollisionNode.AddEvent(AtkEventType.MouseClick, () =>
            {
                if (row is not null && index < row.Options.Count)
                {
                    row.OnSelected(row.Options[index]);
                }
            });
            CollisionNode.ShowClickableCursor = true;
        }

        public void SetOption(BetterGlamourManagement.GlamourDyeOption? option, BetterGlamourDyeGridRow itemRow)
        {
            row = itemRow;
            IsVisible = option is not null;
            if (option is not { } dye)
            {
                CollisionNode.SetTextTooltip(string.Empty);
                return;
            }

            if (itemRow.IsHairColor)
                SetColor(dye.Color);
            else
                SetDye(dye.ID, dye.Color);
            CollisionNode.SetTextTooltip(dye.Name);
            IsSelected = dye.ID == itemRow.SelectedID;
        }
    }

}
