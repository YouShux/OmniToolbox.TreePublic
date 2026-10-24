using System.Drawing;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed class BetterGlamourEditorListItemNode : ListItemNode<BetterGlamourEditorPair>, IListItemNode
    {
        public static float ItemHeight => 64f;

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
            var cellWidth = MathF.Max(1f, (Width - 28f) / 2f);
            left.Position = Vector2.Zero;
            right.Position = new(cellWidth + 28f, 0f);
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
            private readonly CheckboxNode highlights;
            private BetterGlamourEditorRow? row;
            private bool settingData;

            public EquipmentCell()
            {
                title = new()
                {
                    AlignmentType = AlignmentType.Left,
                    TextFlags = TextFlags.Ellipsis
                };
                title.AttachNode(this);
                titleInput = new() { ShowClickableCursor = true };
                titleInput.AddEvent(AtkEventType.MouseClick, () => row?.OpenItem());
                titleInput.AttachNode(this);
                icon = new();
                icon.CollisionNode.ShowClickableCursor = true;
                icon.CollisionNode.AddEvent(AtkEventType.MouseClick, () => row?.OpenItem());
                icon.AttachNode(this);
                for (var index = 0; index < dyes.Length; index++)
                {
                    var channel = index;
                    var dye = new BetterGlamourDyeColorNode(2f, 0f, 0f);
                    dye.CollisionNode.ShowClickableCursor = true;
                    dye.CollisionNode.AddEvent(AtkEventType.MouseClick, () =>
                    {
                        if (dye.IsEnabled)
                            row?.OpenDye?.Invoke(channel);
                    });
                    dye.AttachNode(this);
                    dyes[index] = dye;
                }
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
                var name = value.Kind switch
                {
                    BetterGlamourEditorItemKind.Hairstyle => GetHairstyleName(value.Preset.HairstyleID),
                    BetterGlamourEditorItemKind.Glasses => GetGlassesName(value.Preset.GlassesID),
                    _ => GetItemName(value.Item?.ItemID ?? 0)
                };
                title.String = $"{value.PartName}：{name}";
                titleInput.TextTooltip = title.String;
                icon.IconID = value.Kind switch
                {
                    BetterGlamourEditorItemKind.Hairstyle => GetHairstyleIcon(value.Preset.HairstyleID),
                    BetterGlamourEditorItemKind.Glasses => GetGlassesIcon(value.Preset.GlassesID),
                    _ => LuminaWrapper.GetItemIconID(value.Item?.ItemID ?? 0)
                };
                icon.ItemTooltip = value.Kind == BetterGlamourEditorItemKind.Item ? value.Item?.ItemID ?? 0 : 0;
                var isHair = value.Kind == BetterGlamourEditorItemKind.Hairstyle;
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
                        var tooltip = $"{label}：{GetDyeName(stain)}";
                        if (item.ItemID == 0 ||
                            LuminaGetter.TryGetRow<Item>(item.ItemID, out var itemRow) && itemRow.DyeCount <= channel)
                            tooltip += $"\n{OmniLoc.Get("Feature.BetterGlamourManagement.DyeMayNotDisplay")}";
                        dye.TextTooltip = tooltip;
                    }
                }
                OnSizeChanged();
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                title.Position = new(66f, 3f);
                title.Size = new(MathF.Max(1f, Width - 66f), 22f);
                titleInput.Position = title.Position;
                titleInput.Size = title.Size;
                icon.Position = new(0f, 4f);
                icon.Size = new(56f, 56f);
                dyes[0].Position = new(66f, 30f);
                dyes[1].Position = new(100f, 30f);
                foreach (var dye in dyes)
                    dye.Size = new(28f, 28f);
                highlights.Position = new(134f, 30f);
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
