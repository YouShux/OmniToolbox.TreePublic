using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace OmniToolbox.TreePublic;

public sealed partial class ArmoireRecord
{
    internal sealed class ArmoireRecordNativeUI : NativeAddon
    {
        private readonly ArmoireRecord                                  feature;
        private readonly List<ArmoireRecordItem>                        items          = [];
        private TextNode?                                               summaryNode;
        private TextNode?                                               emptyNode;
        private ListNode<ArmoireRecordItem, ArmoireRecordListItemNode>? listNode;
        private TextButtonNode?                                         retrieveButton;
        private TextButtonNode?                                         storeButton;
        private string                                                  summaryText    = string.Empty;
        private bool                                                    isStoring;
        private bool                                                    isRetrieving;
        private bool                                                    canRetrieve;
        private Action?                                                 onRetrieve;
        private Action?                                                 onStore;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public ArmoireRecordNativeUI(ArmoireRecord feature)
        {
            this.feature          = feature;
            InternalName          = "OmniArmoireRecord";
            Title                 = OmniLoc.Get("Feature.ArmoireRecord.Title");
            Subtitle              = string.Empty;
            Size                  = new(420f, 406f);
            ContentPadding        = new(8f, 0f);
            RememberClosePosition = false;
            CreateWindowNode      = static () => new WindowNode { ShowCloseButton = false };
            RespectCloseAll       = false;
            DisableClose          = true;
            EnableContextMenu     = false;
        }

        protected override unsafe void OnSetup
        (
            AtkUnitBase*   addon,
            Span<AtkValue> atkValueSpan
        )
        {
            summaryNode = CreateTextNode();
            emptyNode   = new()
            {
                AlignmentType = AlignmentType.TopLeft,
                TextFlags = TextFlags.MultiLine,
                IsVisible = false
            };
            emptyNode.AttachNode(this);

            listNode = new()
            {
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                ItemSpacing = 2f,
                OptionsList = items
            };
            listNode.AttachNode(this);

            retrieveButton = new()
            {
                String = OmniLoc.Get("Feature.ArmoireRecord.Retrieve"),
                OnClick = () => onRetrieve?.Invoke()
            };
            retrieveButton.AttachNode(this);

            storeButton = new()
            {
                String = OmniLoc.Get("Feature.ArmoireRecord.Store"),
                OnClick = () => onStore?.Invoke()
            };
            storeButton.AttachNode(this);

            ResizeContent();
            ApplyData();
            feature.SyncWindowPosition();
        }

        protected override unsafe void OnDraw(AtkUnitBase* addon) => feature.SyncWindowPosition();

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            summaryNode    = null;
            emptyNode      = null;
            listNode       = null;
            retrieveButton = null;
            storeButton    = null;
        }

        public void UpdateData
        (
            string                           summary,
            IReadOnlyList<ArmoireRecordItem> newItems,
            bool                             storing,
            bool                             retrieving,
            bool                             retrieveAvailable,
            Action                           retrieve,
            Action                           store
        )
        {
            summaryText  = summary;
            isStoring    = storing;
            isRetrieving = retrieving;
            canRetrieve  = retrieveAvailable;
            onRetrieve   = retrieve;
            onStore      = store;
            items.Clear();
            items.AddRange(newItems);
            ApplyData();
        }

        private TextNode CreateTextNode()
        {
            var node = new TextNode
            {
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.Ellipsis
            };
            node.AttachNode(this);
            return node;
        }

        private void ApplyData()
        {
            if (summaryNode is not null)
            {
                summaryNode.String = summaryText;
            }

            if (listNode is not null)
            {
                listNode.OptionsList = items;
            }

            if (emptyNode is not null)
            {
                emptyNode.IsVisible = items.Count == 0;
                emptyNode.String    = emptyNode.IsVisible
                    ? OmniLoc.Get("Feature.ArmoireRecord.Empty")
                    : string.Empty;
            }

            if (retrieveButton is not null)
            {
                retrieveButton.IsEnabled =
                    !isStoring &&
                    !isRetrieving &&
                    canRetrieve &&
                    items.Exists(static item => item.CanRetrieve);
                retrieveButton.String    = OmniLoc.Get(isRetrieving
                    ? "Feature.ArmoireRecord.Retrieving"
                    : "Feature.ArmoireRecord.Retrieve");
            }

            if (storeButton is not null)
            {
                storeButton.IsEnabled =
                    !isStoring &&
                    !isRetrieving &&
                    items.Exists(static item => item.CanDirectStore);
                storeButton.String    = OmniLoc.Get(isStoring
                    ? "Feature.ArmoireRecord.Storing"
                    : "Feature.ArmoireRecord.Store");
            }
        }

        private void ResizeContent()
        {
            var x = ContentStartPosition.X;
            var y = ContentStartPosition.Y + 4f;

            if (summaryNode is not null)
            {
                summaryNode.Position = new(x, y);
                summaryNode.Size     = new(ContentSize.X, 22f);
            }

            y += 28f;
            var listHeight = (ArmoireRecordListItemNode.ItemHeight + 2f) * 5;
            if (emptyNode is not null)
            {
                emptyNode.Position = new(x, y + 8f);
                emptyNode.Size     = new(ContentSize.X, 56f);
            }

            if (listNode is not null)
            {
                listNode.Position = new(x + 2f, y);
                listNode.Size     = new(ContentSize.X - 4f, listHeight);
            }

            var buttonY = y + listHeight + 12f;
            var buttonWidth = MathF.Max(80f, (ContentSize.X - 10f) / 2f);

            if (retrieveButton is not null)
            {
                retrieveButton.Position = new(x, buttonY);
                retrieveButton.Size     = new(buttonWidth, 28f);
            }

            if (storeButton is not null)
            {
                storeButton.Position = new(x + buttonWidth + 10f, buttonY);
                storeButton.Size     = new(buttonWidth, 28f);
            }
        }
    }

}
