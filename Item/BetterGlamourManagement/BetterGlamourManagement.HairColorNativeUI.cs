using TreeToolKit;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;
using Lumina.Data;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    private static readonly Lazy<byte[]> HairColorData = new(() =>
    {
        try
        {
            return DalamudServices.DataManager.GetFile<FileResource>("chara/xls/charamake/human.cmp")?.Data ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    });
    private static readonly Dictionary<(byte Tribe, byte Sex, bool Highlights), Vector4[]> HairPalettes = [];

    private static bool TryGetHairPalette(byte tribe, byte sex, bool highlights, out Vector4[] colors)
    {
        colors = [];
        if (tribe == 0 || sex > 1)
            return false;
        var key = highlights ? ((byte)0, (byte)0, true) : (tribe, sex, false);
        if (HairPalettes.TryGetValue(key, out var cached))
        {
            colors = cached;
            return colors.Length != 0;
        }
        var data = HairColorData.Value;
        var offset = highlights ? 0x400 : 0x4800 + ((tribe - 1) * 2 + sex) * 0x1400 + 4 * 0x400;
        var count = highlights ? 192 : 208;
        if (offset > data.Length - count * 4)
        {
            HairPalettes[key] = [];
            return false;
        }
        colors = new Vector4[count];
        for (var index = 0; index < count; index++)
        {
            var position = offset + index * 4;
            colors[index] = new(data[position] / 255f, data[position + 1] / 255f, data[position + 2] / 255f, 1f);
        }
        HairPalettes[key] = colors;
        return true;
    }

    internal sealed class BetterGlamourHairColorNativeUI : NativeAddon
    {
        private readonly Vector4[][] colors = [[], []];
        private readonly byte[] selectedIDs = new byte[2];
        private readonly byte[] originalIDs = new byte[2];
        private readonly List<HairColorSwatchNode>[] swatches = [[], []];
        private readonly HairColorSwatchNode?[,] previewNodes = new HairColorSwatchNode?[2, 2];
        private readonly TextNode?[,] previewLabels = new TextNode?[2, 2];
        private readonly SimpleImageNode?[] selectionFrames = new SimpleImageNode?[2];
        private Action<int, byte>? onSelected;
        private Action<bool>? onHighlightsChanged;
        private TextNode? hairLabel;
        private CheckboxNode? highlightsCheckbox;
        private bool highlightsEnabled;
        private Vector2 lastContentPosition;
        private Vector2 lastContentSize;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourHairColorNativeUI()
        {
            InternalName = "OmniBetterGlamourHairColor";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.HairColors.Title");
            Subtitle = string.Empty;
            Size = new(340f, 360f);
            ContentPadding = new(10f, 8f);
            RememberClosePosition = true;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = true };
        }

        public void OpenFor(
            Vector4[] hairPalette,
            Vector4[] highlightPalette,
            byte hairColor,
            byte highlightColor,
            bool enabled,
            byte originalHairColor,
            byte originalHighlightColor,
            Action<int, byte> selected,
            Action<bool> highlightsChanged)
        {
            colors[0] = hairPalette;
            colors[1] = highlightPalette;
            selectedIDs[0] = hairColor;
            selectedIDs[1] = highlightColor;
            originalIDs[0] = originalHairColor;
            originalIDs[1] = originalHighlightColor;
            highlightsEnabled = enabled;
            onSelected = selected;
            onHighlightsChanged = highlightsChanged;
            if (!IsOpen)
            {
                Size = GetRequiredContentSize() + new Vector2(ContentPadding.X * 2f + 8f, ContentPadding.Y + 54f);
                Open();
            }
            else
                RebuildPalettes();
        }

        public void RefreshAppearance(byte hairColor, byte highlightColor, bool enabled)
        {
            if (!IsOpen)
                return;
            selectedIDs[0] = hairColor;
            selectedIDs[1] = highlightColor;
            highlightsEnabled = enabled;
            RefreshSelection();
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            hairLabel = new()
            {
                String = OmniLoc.Get("Feature.BetterGlamourManagement.HairColor"),
                FontSize = 16,
                AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.None
            };
            hairLabel.AttachNode(this);
            highlightsCheckbox = new()
            {
                Height = 22f,
                String = OmniLoc.Get("Feature.BetterGlamourManagement.HairColors.EnableHighlights"),
                OnClick = SetHighlights
            };
            highlightsCheckbox.AttachNode(this);
            for (var channel = 0; channel < colors.Length; channel++)
            {
                for (var comparison = 0; comparison < 2; comparison++)
                {
                    var label = new TextNode
                    {
                        String = OmniLoc.Get(comparison == 0
                            ? "Feature.BetterGlamourManagement.HairColors.Current"
                            : "Feature.BetterGlamourManagement.HairColors.Original"),
                        AlignmentType = AlignmentType.Center,
                        FontSize = 14,
                        TextFlags = TextFlags.None
                    };
                    label.AttachNode(this);
                    previewLabels[channel, comparison] = label;
                    var preview = new HairColorSwatchNode();
                    preview.AttachNode(this);
                    previewNodes[channel, comparison] = preview;
                }
            }
            RebuildPalettes();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            ResizeContent();
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            RootNode.HideTooltip();
            onSelected = null;
            onHighlightsChanged = null;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            hairLabel = null;
            highlightsCheckbox = null;
            Array.Clear(previewNodes);
            Array.Clear(previewLabels);
            Array.Clear(selectionFrames);
            for (var channel = 0; channel < colors.Length; channel++)
            {
                swatches[channel].Clear();
                colors[channel] = [];
            }
            onSelected = null;
            onHighlightsChanged = null;
            lastContentPosition = lastContentSize = Vector2.Zero;
        }

        private unsafe void RebuildPalettes()
        {
            highlightsCheckbox?.HideTooltip();
            for (var channel = 0; channel < colors.Length; channel++)
            {
                selectionFrames[channel]?.Dispose();
                var nodes = swatches[channel];
                while (nodes.Count > colors[channel].Length)
                {
                    nodes[^1].Dispose();
                    nodes.RemoveAt(nodes.Count - 1);
                }
                var paletteChannel = channel;
                var name = OmniLoc.Get(channel == 0
                    ? "Feature.BetterGlamourManagement.HairColor"
                    : "Feature.BetterGlamourManagement.HighlightColor");
                for (var index = 0; index < colors[channel].Length; index++)
                {
                    if (index >= nodes.Count)
                    {
                        var colorID = (byte)index;
                        var swatch = new HairColorSwatchNode();
                        swatch.CollisionNode.AddEvent(AtkEventType.MouseClick, (_, _, _, _, eventData) =>
                        {
                            if (eventData is not null && eventData->IsLeftClick)
                                SelectColor(paletteChannel, colorID);
                        });
                        swatch.AttachNode(this);
                        nodes.Add(swatch);
                    }
                    nodes[index].Color = colors[channel][index];
                    nodes[index].CollisionNode.SetTextTooltip($"{name}：{index}");
                }
                var frame = new SimpleImageNode
                {
                    TexturePath = "ui/uld/CharaMake.tex",
                    TextureCoordinates = new(428f, 180f),
                    TextureSize = new(20f, 16f),
                    Size = new(20f, 16f),
                    Scale = new((SWATCH_WIDTH + 4f) / 20f, (SWATCH_HEIGHT + 4f) / 16f)
                };
                frame.AttachNode(this);
                selectionFrames[channel] = frame;
            }
            lastContentSize = Vector2.Zero;
            ResizeContent();
        }

        private void SelectColor(int channel, byte colorID)
        {
            if (colorID >= colors[channel].Length || onSelected is null || channel == 1 && !highlightsEnabled)
                return;
            selectedIDs[channel] = colorID;
            RefreshSelection();
            onSelected(channel, colorID);
        }

        private void SetHighlights(bool enabled)
        {
            if (highlightsEnabled == enabled)
                return;
            highlightsEnabled = enabled;
            RefreshSelection();
            onHighlightsChanged?.Invoke(enabled);
        }

        private void RefreshSelection()
        {
            if (highlightsCheckbox is not null && highlightsCheckbox.IsChecked != highlightsEnabled)
                highlightsCheckbox.IsChecked = highlightsEnabled;
            for (var channel = 0; channel < colors.Length; channel++)
            {
                var enabled = channel == 0 || highlightsEnabled;
                foreach (var swatch in swatches[channel])
                {
                    swatch.Alpha = enabled ? 1f : 0.45f;
                    swatch.CollisionNode.ShowClickableCursor = enabled;
                }
                if (selectionFrames[channel] is { } frame)
                {
                    var index = selectedIDs[channel];
                    frame.IsVisible = index < swatches[channel].Count;
                    if (frame.IsVisible)
                        frame.Position = swatches[channel][index].Position - new Vector2(2f);
                    frame.Alpha = enabled ? 1f : 0.45f;
                }
                for (var comparison = 0; comparison < 2; comparison++)
                {
                    if (previewNodes[channel, comparison] is not { } preview)
                        continue;
                    var index = comparison == 0 ? selectedIDs[channel] : originalIDs[channel];
                    preview.IsVisible = index < colors[channel].Length;
                    if (preview.IsVisible)
                        preview.Color = colors[channel][index];
                }
            }
        }

        private void ResizeContent()
        {
            if (hairLabel is null || highlightsCheckbox is null)
                return;
            var requiredSize = GetRequiredContentSize();
            if (ContentSize != requiredSize)
                SetWindowSize(Size - ContentSize + requiredSize);
            if (lastContentPosition == ContentStartPosition && lastContentSize == ContentSize)
                return;
            lastContentPosition = ContentStartPosition;
            lastContentSize = ContentSize;
            hairLabel.Position = ContentStartPosition + new Vector2(2f, 0f);
            hairLabel.Size = new(GRID_WIDTH, 22f);
            highlightsCheckbox.Position = hairLabel.Position + new Vector2(GRID_WIDTH + PANEL_GAP, 0f);
            var comparisonY = ContentStartPosition.Y + requiredSize.Y - 46f;
            for (var channel = 0; channel < colors.Length; channel++)
            {
                var start = ContentStartPosition + new Vector2(2f + channel * (GRID_WIDTH + PANEL_GAP), 28f);
                for (var index = 0; index < swatches[channel].Count; index++)
                {
                    swatches[channel][index].Position = start + new Vector2(index % COLUMN_COUNT * SWATCH_WIDTH, index / COLUMN_COUNT * SWATCH_HEIGHT);
                    swatches[channel][index].Size = new(SWATCH_WIDTH, SWATCH_HEIGHT);
                }
                for (var comparison = 0; comparison < 2; comparison++)
                {
                    var position = new Vector2(start.X + comparison * (GRID_WIDTH + 4f) / 2f, comparisonY);
                    if (previewLabels[channel, comparison] is { } label)
                    {
                        label.Position = position;
                        label.Size = new((GRID_WIDTH - 4f) / 2f, 20f);
                    }
                    if (previewNodes[channel, comparison] is { } preview)
                    {
                        preview.Position = position + new Vector2(0f, 20f);
                        preview.Size = new((GRID_WIDTH - 4f) / 2f, 24f);
                    }
                }
            }
            RefreshSelection();
        }

        private Vector2 GetRequiredContentSize() => new(
            GRID_WIDTH * 2f + PANEL_GAP + 4f,
            Math.Max((colors[0].Length + COLUMN_COUNT - 1) / COLUMN_COUNT, (colors[1].Length + COLUMN_COUNT - 1) / COLUMN_COUNT) * SWATCH_HEIGHT + 86f);

        private sealed class HairColorSwatchNode : SimpleComponentNode
        {
            private readonly ColorImageNode colorNode = new();

            public HairColorSwatchNode() => colorNode.AttachNode(this);

            public override Vector4 Color
            {
                get => colorNode.Color;
                set => colorNode.Color = value;
            }

            protected override void OnSizeChanged()
            {
                base.OnSizeChanged();
                colorNode.Size = Size;
            }
        }

        #region 常量

        private const int COLUMN_COUNT = 8;
        private const float SWATCH_WIDTH = 24f;
        private const float SWATCH_HEIGHT = 18f;
        private const float GRID_WIDTH = COLUMN_COUNT * SWATCH_WIDTH;
        private const float PANEL_GAP = 20f;

        #endregion
    }
}
