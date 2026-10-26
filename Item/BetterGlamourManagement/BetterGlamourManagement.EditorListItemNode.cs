using System.Drawing;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourEditorListItemNode : ListItemNode<BetterGlamourEditorPair>, IListItemNode
    {
        public static float ItemHeight => 60f;

        private readonly EquipmentCell left = new();
        private readonly EquipmentCell right = new();

        public BetterGlamourEditorListItemNode()
        {
            left.AttachNode(this);
            right.AttachNode(this);
            DisableInteractions();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            var cellWidth = MathF.Max(1f, (Width - 12f) / 2f);
            left.Position = Vector2.Zero;
            right.Position = new(cellWidth + 12f, 0f);
            left.Size = right.Size = new(cellWidth, ItemHeight);
        }

        protected override void SetNodeData(BetterGlamourEditorPair itemData)
        {
            left.SetRow(itemData.Left);
            right.SetRow(itemData.Right);
        }

        private sealed class EquipmentCell : SimpleComponentNode
        {
            private readonly TextNode title;
            private readonly CollisionNode titleInput;
            private readonly BetterGlamourItemIconNode icon;
            private readonly BetterGlamourDyeColorNode[] dyes = new BetterGlamourDyeColorNode[2];
            private readonly TextureButtonNode swapDyes;
            private readonly CheckboxNode highlights;
            private BetterGlamourEditorRow? row;
            private bool settingData;

            public unsafe EquipmentCell()
            {
                title = new()
                {
                    AlignmentType = AlignmentType.Left,
                    TextFlags = TextFlags.Ellipsis
                };
                title.AttachNode(this);
                titleInput = new() { ShowClickableCursor = true };
                AtkEventListener.Delegates.ReceiveEvent onItemClick = (_, _, _, _, eventData) =>
                {
                    if (eventData is null)
                        return;
                    if (eventData->IsLeftClick)
                        row?.OpenItem();
                    else if (eventData->IsRightClick)
                    {
                        if (DalamudServices.KeyState[VirtualKey.CONTROL])
                            row?.UndoItem?.Invoke();
                        else
                            row?.ClearItem?.Invoke();
                    }
                };
                titleInput.AddEvent(AtkEventType.MouseClick, onItemClick);
                titleInput.AttachNode(this);
                icon = new();
                icon.CollisionNode.ShowClickableCursor = true;
                icon.CollisionNode.AddEvent(AtkEventType.MouseClick, onItemClick);
                icon.AttachNode(this);
                for (var index = 0; index < dyes.Length; index++)
                {
                    var channel = index;
                    var dye = new BetterGlamourDyeColorNode(2f, 0f, 0f);
                    dye.CollisionNode.ShowClickableCursor = true;
                    dye.CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
                    {
                        if (!dye.IsEnabled || eventData is null)
                            return;
                        if (eventData->IsLeftClick)
                            row?.OpenDye?.Invoke(channel);
                        else if (eventData->IsRightClick)
                        {
                            if (DalamudServices.KeyState[VirtualKey.CONTROL])
                                row?.UndoDye?.Invoke(channel);
                            else
                                row?.ClearDye?.Invoke(channel);
                        }
                    });
                    dye.AttachNode(this);
                    dyes[index] = dye;
                }
                swapDyes = new()
                {
                    TexturePath = "ui/uld/ColorantButton.tex",
                    TextureCoordinates = new(72f, 0f),
                    TextureSize = new(36f, 36f),
                    TextTooltip = LuminaWrapper.GetAddonText(15975),
                    OnClick = () => row?.SwapDyes?.Invoke()
                };
                swapDyes.AttachNode(this);
                highlights = new()
                {
                    DisableAutoResize = true,
                    OnClick = enabled =>
                    {
                        if (!settingData)
                            row?.SetHighlights?.Invoke(enabled);
                    }
                };
                highlights.AttachNode(this);
                CollisionNode.IsVisible = false;
            }

            public void SetRow(BetterGlamourEditorRow value)
            {
                row = value;
                if (value.Kind == BetterGlamourEditorItemKind.Item)
                    icon.SetItem(value.Item?.ItemID ?? 0, value.Item?.Stain0 ?? 0, value.Item?.Stain1 ?? 0);
                else
                {
                    icon.IconID = value.Kind == BetterGlamourEditorItemKind.Hairstyle
                        ? GetHairstyleIcon(value.Preset.HairstyleID)
                        : GetGlassesIcon(value.Preset.GlassesID);
                }
                icon.ItemTooltip = value.Kind == BetterGlamourEditorItemKind.Item ? value.Item?.ItemID ?? 0 : 0;
                var isHair = value.Kind == BetterGlamourEditorItemKind.Hairstyle;
                swapDyes.IsVisible = value.SwapDyes is not null;
                swapDyes.IsEnabled = value.Item is { } equipment && equipment.Stain0 != equipment.Stain1;
                highlights.IsVisible = isHair;
                settingData = true;
                try
                {
                    highlights.IsChecked = value.Preset.HighlightsEnabled ?? value.HairAppearance?.Highlights ?? false;
                }
                finally
                {
                    settingData = false;
                }
                highlights.IsEnabled = value.HairAppearance.HasValue;
                highlights.TextTooltip = OmniLoc.Get(value.HairAppearance.HasValue
                    ? "Feature.BetterGlamourManagement.Highlights"
                    : "Feature.BetterGlamourManagement.HairPaletteUnavailable");
                for (var channel = 0; channel < dyes.Length; channel++)
                {
                    var dye = dyes[channel];
                    dye.IsVisible = value.Kind != BetterGlamourEditorItemKind.Glasses;
                    if (isHair)
                    {
                        SetHairColor(dye, channel);
                    }
                    else if (value.Item is { } item)
                    {
                        var stain = channel == 0 ? item.Stain0 : item.Stain1;
                        var option = DyeOptions.Find(candidate => candidate.ID == stain);
                        dye.SetDye(stain, option.Color);
                        dye.IsEnabled = true;
                        var label = OmniLoc.Get(channel == 0
                            ? "Feature.BetterGlamourManagement.Column.Stain0"
                            : "Feature.BetterGlamourManagement.Column.Stain1");
                        dye.TextTooltip = $"{label}：{GetDyeName(stain)}\n{OmniLoc.Get("Feature.BetterGlamourManagement.DyeClickHint")}";
                    }
                }
                OnSizeChanged();
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                title.Position = new(64f, 3f);
                title.Size = new(MathF.Max(1f, Width - 64f), 22f);
                titleInput.Position = title.Position;
                titleInput.Size = title.Size;
                if (row is { } value)
                {
                    var name = value.Kind switch
                    {
                        BetterGlamourEditorItemKind.Hairstyle => GetHairstyleName(value.Preset.HairstyleID),
                        BetterGlamourEditorItemKind.Glasses => GetGlassesName(value.Preset.GlassesID),
                        _ => GetItemName(value.Item?.ItemID ?? 0)
                    };
                    var label = $"{value.PartName}：{name}";
                    title.String = label;
                    titleInput.TextTooltip = value.UndoItem is not null
                        ? $"{label}\n{OmniLoc.Get("Feature.BetterGlamourManagement.ItemClickHint")}"
                        : label;
                }
                icon.Position = new(0f, 2f);
                icon.Size = new(56f, 56f);
                dyes[0].Position = new(64f, 28f);
                dyes[1].Position = new(98f, 28f);
                foreach (var dye in dyes)
                    dye.Size = new(28f, 28f);
                swapDyes.Position = new(132f, 28f);
                swapDyes.Size = new(28f, 28f);
                highlights.Position = new(132f, 28f);
                highlights.Size = new(76f, 28f);
                highlights.Label.Width = highlights.Width - highlights.Height;
                highlights.Label.String = OmniLoc.Get("Feature.BetterGlamourManagement.Highlights");
            }

            private void SetHairColor(BetterGlamourDyeColorNode dye, int channel)
            {
                if (row is not { } data)
                    return;
                var appearance = data.HairAppearance;
                var label = OmniLoc.Get(channel == 0
                    ? "Feature.BetterGlamourManagement.HairColor"
                    : "Feature.BetterGlamourManagement.HighlightColor");
                if (appearance is not { } customize ||
                    !TryGetHairPalette(customize.Tribe, customize.Sex, channel == 1, out var colors))
                {
                    dye.SetColor(KnownColor.Gray.Vector());
                    dye.IsEnabled = false;
                    dye.TextTooltip = $"{label}：{OmniLoc.Get("Feature.BetterGlamourManagement.HairPaletteUnavailable")}";
                    return;
                }
                var colorID = channel == 0
                    ? data.Preset.HairColor ?? customize.HairColor
                    : data.Preset.HighlightColor ?? customize.HighlightsColor;
                dye.SetColor(colorID < colors.Length ? colors[colorID] : KnownColor.Gray.Vector());
                dye.IsEnabled = true;
                dye.TextTooltip = $"{label}：{colorID}";
            }
        }
    }
}
