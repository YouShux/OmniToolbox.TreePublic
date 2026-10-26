using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;

namespace OmniToolbox.TreePublic;

public sealed partial class LockedDutyPreview
{
    internal sealed class LockedDutyPreviewNativeUI : NativeAddon
    {
        private List<LockedDutyPreviewRow> rows = [];
        private TextNode? summaryNode;
        private ListNode<LockedDutyPreviewRow, LockedDutyPreviewListItemNode>? listNode;
        private TextButtonNode? lockedTabButton;
        private TextButtonNode? incompleteTabButton;
        private TextButtonNode? excludedTabButton;
        private LockedDutyPreviewView currentView;
        private string summaryText = string.Empty;
        private readonly Action<LockedDutyPreviewView> onViewChanged;
        private readonly Action<LockedDutyPreviewRow> onExclude;
        private readonly Action<LockedDutyPreviewRow> onRestore;
        private readonly Action<LockedDutyPreviewRow> onWiki;
        private readonly Action<string> onCopyName;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public LockedDutyPreviewNativeUI(
            Action<LockedDutyPreviewView> onViewChanged,
            Action<LockedDutyPreviewRow> onExclude,
            Action<LockedDutyPreviewRow> onRestore,
            Action<LockedDutyPreviewRow> onWiki,
            Action<string> onCopyName)
        {
            InternalName = "OmniLockedDutiesNative";
            Title = OmniLoc.Get("Feature.LockedDutyPreview.Title");
            Subtitle = string.Empty;
            Size = new(360f, 400f);
            ContentPadding = new(8f, 0f);
            RememberClosePosition = false;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = false };
            RespectCloseAll = false;
            DisableClose = true;
            EnableContextMenu = false;
            this.onViewChanged = onViewChanged;
            this.onExclude = onExclude;
            this.onRestore = onRestore;
            this.onWiki = onWiki;
            this.onCopyName = onCopyName;
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            var source = AddonHelper.GetByName("ContentsFinder");
            if (source != null && source->WindowNode != null)
                addon->SetScale(source->WindowNode->GetScale().X / AtkUnitBase.GetGlobalUIScale(), true);
            LockedDutyPreviewListItemNode.SetCallbacks(onExclude, onRestore, onWiki);
            summaryNode = new()
            {
                Position = ContentStartPosition,
                Size = new(ContentSize.X, 24f),
                AlignmentType = AlignmentType.Left
            };
            summaryNode.AttachNode(this);

            listNode = new()
            {
                ItemSpacing = ITEM_SPACING,
                Position = new(ContentStartPosition.X + 2f, ContentStartPosition.Y + 30f),
                Size = new(ContentSize.X - 4f, ContentSize.Y - 72f),
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = rows,
                OnItemSelected = item => onCopyName(item.Name)
            };
            listNode.AttachNode(this);

            lockedTabButton = new()
            {
                String = OmniLoc.Get("Feature.LockedDutyPreview.Tab.Locked"),
                OnClick = () => SetView(LockedDutyPreviewView.Locked)
            };
            lockedTabButton.AttachNode(this);

            incompleteTabButton = new()
            {
                String = OmniLoc.Get("Feature.LockedDutyPreview.Tab.Incomplete"),
                OnClick = () => SetView(LockedDutyPreviewView.Incomplete)
            };
            incompleteTabButton.AttachNode(this);

            excludedTabButton = new()
            {
                String = OmniLoc.Get("Feature.LockedDutyPreview.Tab.Excluded"),
                OnClick = () => SetView(LockedDutyPreviewView.Excluded)
            };
            excludedTabButton.AttachNode(this);

            ResizeContent();
            ApplyData();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            ResizeContent();
            listNode?.Update();
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            LockedDutyPreviewListItemNode.ClearCallbacks();
            summaryNode = null;
            listNode = null;
            lockedTabButton = null;
            incompleteTabButton = null;
            excludedTabButton = null;
        }

        public void UpdateData(
            string summary,
            List<LockedDutyPreviewRow> rows,
            LockedDutyPreviewView view)
        {
            summaryText = summary;
            currentView = view;
            this.rows = rows;
            ApplyData();
        }

        public void ClearCallbacks() => LockedDutyPreviewListItemNode.ClearCallbacks();

        private void SetView(LockedDutyPreviewView view)
        {
            if (currentView == view)
            {
                return;
            }

            currentView = view;
            onViewChanged(view);
            ApplyData();
        }

        private void ApplyData()
        {
            if (summaryNode is not null)
            {
                summaryNode.String = summaryText;
            }

            if (listNode is not null)
            {
                listNode.OptionsList = rows;
            }

            if (lockedTabButton is not null)
            {
                lockedTabButton.IsChecked = currentView == LockedDutyPreviewView.Locked;
            }

            if (incompleteTabButton is not null)
            {
                incompleteTabButton.IsChecked = currentView == LockedDutyPreviewView.Incomplete;
            }

            if (excludedTabButton is not null)
            {
                excludedTabButton.IsChecked = currentView == LockedDutyPreviewView.Excluded;
            }
        }

        private void ResizeContent()
        {
            if (summaryNode is not null)
            {
                summaryNode.Size = new(ContentSize.X, 24f);
            }

            if (listNode is not null)
            {
                listNode.Position = new(ContentStartPosition.X + 2f, ContentStartPosition.Y + 30f);
                listNode.Size = new(ContentSize.X - 4f, MathF.Max(60f, ContentSize.Y - 72f));
            }

            var bottomY = ContentStartPosition.Y + ContentSize.Y - 32f;
            var tabGap = 4f;
            var tabWidth = MathF.Max(80f, (ContentSize.X - (tabGap * 2f)) / 3f);
            if (lockedTabButton is not null)
            {
                lockedTabButton.Position = new(ContentStartPosition.X, bottomY);
                lockedTabButton.Size = new(tabWidth, 28f);
            }

            if (incompleteTabButton is not null)
            {
                incompleteTabButton.Position = new(ContentStartPosition.X + tabWidth + tabGap, bottomY);
                incompleteTabButton.Size = new(tabWidth, 28f);
            }

            if (excludedTabButton is not null)
            {
                excludedTabButton.Position = new(
                    ContentStartPosition.X + ((tabWidth + tabGap) * 2f),
                    bottomY);
                excludedTabButton.Size = new(tabWidth, 28f);
            }
        }

        #region 常量

        private const float ITEM_SPACING = 2f;

        #endregion
    }

}