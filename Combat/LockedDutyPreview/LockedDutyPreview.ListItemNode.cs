using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class LockedDutyPreview
{
    internal sealed class LockedDutyPreviewListItemNode : ListItemNode<LockedDutyPreviewRow>, IListItemNode
    {
        public static float ItemHeight => 28f;

        private static Action<LockedDutyPreviewRow>? OnExclude;
        private static Action<LockedDutyPreviewRow>? OnRestore;
        private static Action<LockedDutyPreviewRow>? OnWiki;

        private readonly TextNode nameNode;
        private readonly TextButtonNode primaryButton;
        private readonly TextButtonNode wikiButton;
        private LockedDutyPreviewRow item;

        public LockedDutyPreviewListItemNode()
        {
            nameNode = new()
            {
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.Ellipsis,
                ShowClickableCursor = true
            };
            nameNode.AttachNode(this);

            primaryButton = new()
            {
                OnClick = () =>
                {
                    if (item.IsExcluded)
                    {
                        OnRestore?.Invoke(item);
                    }
                    else
                    {
                        OnExclude?.Invoke(item);
                    }
                }
            };
            primaryButton.AttachNode(this);

            wikiButton = new()
            {
                String = OmniLoc.Get("Feature.LockedDutyPreview.Wiki"),
                OnClick = () => OnWiki?.Invoke(item)
            };
            wikiButton.AttachNode(this);
        }

        public static void SetCallbacks(
            Action<LockedDutyPreviewRow> exclude,
            Action<LockedDutyPreviewRow> restore,
            Action<LockedDutyPreviewRow> wiki)
        {
            OnExclude = exclude;
            OnRestore = restore;
            OnWiki = wiki;
        }

        public static void ClearCallbacks()
        {
            OnExclude = null;
            OnRestore = null;
            OnWiki = null;
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            const float GAP = 4f;
            var primaryWidth = item.IsExcluded ? 72f : 52f;
            var wikiWidth = item.IsExcluded ? 0f : 52f;
            var rightWidth = primaryWidth + (item.IsExcluded ? 0f : GAP + wikiWidth);
            nameNode.Position = new(2f, 4f);
            nameNode.Size = new(MathF.Max(40f, Width - rightWidth - GAP - 4f), Height - 2f);
            primaryButton.Position = new(Width - rightWidth, 0f);
            primaryButton.Size = new(primaryWidth, 26f);
            wikiButton.Position = new(Width - wikiWidth, 0f);
            wikiButton.Size = new(wikiWidth, 26f);
        }

        protected override void SetNodeData(LockedDutyPreviewRow itemData)
        {
            item = itemData;
            nameNode.String = itemData.Name;
            primaryButton.String = OmniLoc.Get(itemData.IsExcluded
                ? "Feature.LockedDutyPreview.Restore"
                : "Feature.LockedDutyPreview.Exclude");
            wikiButton.IsVisible = !itemData.IsExcluded;
            OnSizeChanged();
        }
    }

}