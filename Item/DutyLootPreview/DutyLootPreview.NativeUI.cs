using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.ContextMenu;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmenTools;
using OmenTools.Extensions;
using OmniToolbox.Host;
using OmniToolbox.Items;
using OmniToolbox.UI;
using ItemContextMenu = KamiToolKit.ContextMenu.ContextMenu;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class DutyLootPreview
{
    private sealed class LootWindow : NativeAddon
    {
        private readonly DutyLootPreview owner;
        private List<LootItem> items = [];
        private ListNode<LootItem, LootItemNode>? list;
        private TextNode? hint;
        private long nextStatusUpdate;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public LootWindow(DutyLootPreview owner)
        {
            this.owner = owner;
            InternalName = "OmniDutyLootPreview";
            Title = OmniLoc.Get("DutyLootPreviewTitle");
            Subtitle = string.Empty;
            Size = new(350f, 519f);
            ContentPadding = new(8f, 0f);
            CreateWindowNode = static () => new WindowNode();
        }

        public void SetItems(List<LootItem> newItems)
        {
            items = newItems;
            UpdateList();
        }

        protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            list = new()
            {
                ItemSpacing = 2f,
                Position = ContentStartPosition,
                Size = ContentSize,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = [],
                OnItemSelected = item =>
                {
                    if (item is not null)
                    {
                        owner.itemPreviewService.TryOn(item.Row.RowId);
                    }
                }
            };
            list.AttachNode(this);

            hint = new()
            {
                Position = list.Position,
                Size = list.Size,
                AlignmentType = AlignmentType.Center,
                TextFlags = TextFlags.MultiLine | TextFlags.Edge | TextFlags.WordWrap,
                LineSpacing = 18
            };
            hint.AttachNode(this);
            UpdateList();
        }

        protected override void OnUpdate(AtkUnitBase* addon)
        {
            if (DService.Instance().Condition.IsBetweenAreas || !UIModule.IsScreenReady())
            {
                return;
            }

            if (Environment.TickCount64 < nextStatusUpdate)
            {
                return;
            }

            nextStatusUpdate = Environment.TickCount64 + 1000;
            list?.Update();
        }

        protected override void OnFinalize(AtkUnitBase* addon)
        {
            list = null;
            hint = null;
        }

        public void UpdateList()
        {
            if (list is null || hint is null)
            {
                return;
            }

            // 当前组件通过空列表归零滚动，再替换结果，避免保留上一个副本的滚动位置。
            list.OptionsList = [];
            list.OptionsList = items;
            list.Update();
            var hasItems = items.Count != 0;
            list.IsVisible = hasItems;
            hint.String = hasItems ? string.Empty : OmniLoc.Get("Feature.DutyLootPreview.Empty");
            hint.Position = ContentStartPosition;
            hint.Size = ContentSize;
            hint.IsVisible = !hasItems;
        }
    }

    private sealed class LootItemNode : ListItemNode<LootItem>, IListItemNode
    {
        public static float ItemHeight => 44f;

        private readonly LootIconNode icon;
        private readonly TextNode name;
        private readonly SimpleImageNode favorite;
        private readonly SimpleImageNode checkmark;
        private readonly ItemContextMenu menu = new();

        public LootItemNode()
        {
            icon = new();
            icon.AttachNode(this);
            favorite = new()
            {
                TexturePath = "ui/uld/MinionNoteBook.tex",
                TextureCoordinates = new(96f, 0f),
                TextureSize = new(20f, 20f),
                IsVisible = false
            };
            favorite.AttachNode(this);
            name = new()
            {
                TextFlags = TextFlags.Ellipsis,
                AlignmentType = AlignmentType.Left
            };
            name.AttachNode(this);
            checkmark = new()
            {
                TexturePath = "ui/uld/RecipeNoteBook.tex",
                TextureCoordinates = new(60f, 28f),
                TextureSize = new(28f, 24f),
                IsVisible = false
            };
            checkmark.AttachNode(this);
            AddEvent(AtkEventType.MouseClick, OnMouseClick);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            var iconSize = MathF.Min(Width, Height - 2f);
            icon.Size = new(iconSize, iconSize);
            icon.Position = new(2f, (Height - iconSize) / 2f);
            favorite.Size = new(28f * 20f / 44f, 28f * 20f / 44f);
            favorite.Position = new(icon.Position.X + iconSize - favorite.Width, -2f);
            checkmark.Size = new(28f, 24f);
            checkmark.Position = new Vector2(icon.Position.X + iconSize, icon.Position.Y + iconSize) -
                                 checkmark.Size * 0.8f;
            name.Position = new(icon.Position.X + iconSize + 4f, (Height - 20f) / 2f);
            name.Size = new(Width - name.Position.X - 8f, 20f);
        }

        protected override void SetNodeData(LootItem item)
        {
            icon.SetIcon(item.Row.Icon);
            name.String = item.Name;
            ItemTooltip = item.Row.RowId;
            favorite.IsVisible = item.IsFavorite;
            checkmark.IsVisible = item.IsUnlocked;
        }

        public override void Update()
        {
            if (ItemData is not { } item)
            {
                return;
            }

            favorite.IsVisible = item.IsFavorite;
            checkmark.IsVisible = DService.Instance().ClientState.IsLoggedIn && item.IsUnlocked;
        }

        private void OnMouseClick(
            AtkEventListener* listener,
            AtkEventType eventType,
            int eventParam,
            AtkEvent* atkEvent,
            AtkEventData* eventData)
        {
            if (eventData->MouseData.ButtonId != 1 || ItemData is not { } item ||
                !DService.Instance().ClientState.IsLoggedIn)
            {
                return;
            }

            var itemID = item.Row.RowId;
            var owner = item.Owner;
            menu.Clear();
            if (owner.itemPreviewService.CanTryOn(itemID))
            {
                menu.AddItem(OmniLoc.Get("Feature.DutyLootPreview.TryOn"), () => owner.itemPreviewService.TryOn(itemID));
            }

            menu.AddItem(
                OmniLoc.Get(item.IsFavorite
                    ? "Feature.DutyLootPreview.Unfavorite"
                    : "Feature.DutyLootPreview.Favorite"),
                () =>
                {
                    if (!owner.config.FavoriteItems.Add(itemID))
                    {
                        owner.config.FavoriteItems.Remove(itemID);
                    }

                    owner.saveConfig();
                    owner.window?.UpdateList();
                });
            menu.AddItem(OmniLoc.Get("Feature.DutyLootPreview.Search"), () =>
            {
                var finder = ItemFinderModule.Instance();
                if (finder != null)
                {
                    finder->SearchForItem(itemID);
                }
            });
            menu.AddItem(OmniLoc.Get("Feature.DutyLootPreview.Link"), () => ItemLinkService.TryInsert(itemID));
            menu.AddItem(OmniLoc.Get("Feature.DutyLootPreview.Recipes"), () =>
            {
                var recipes = AgentRecipeProductList.Instance();
                if (recipes != null)
                {
                    recipes->SearchForRecipesUsingItem(itemID);
                }
            });
            menu.Open();
            atkEvent->SetEventIsHandled();
        }

        protected override void Dispose(bool isNativeDestructor)
        {
            if (IsDisposed)
                return;

            menu.Dispose();
            base.Dispose(isNativeDestructor);
        }

        private sealed class LootIconNode : SimpleComponentNode
        {
            private readonly IconNode iconNode = new();

            public LootIconNode()
            {
                CollisionNode.NodeFlags = 0;
                iconNode.CollisionNode.NodeFlags = 0;
                iconNode.AttachNode(this);
            }

            public void SetIcon(uint iconID) => iconNode.IconId = iconID;

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();

                var scale = MathF.Max(0.01f, MathF.Min(Width, Height) / 48f);
                iconNode.Size = new(48f, 48f);
                iconNode.Scale = new(scale, scale);
                iconNode.Position = new(
                    (Width - 48f * scale) / 2f,
                    (Height - 48f * scale) / 2f);
            }
        }
    }
}
