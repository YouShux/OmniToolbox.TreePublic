using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using Lumina.Data;
using OmniToolbox.Host;
using OmniToolbox.UI;

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
        private readonly List<BetterGlamourDyeGridRow> rows = [];
        private Vector4[] colors = [];
        private byte selectedID;
        private Action<byte>? onSelected;
        private ListNode<BetterGlamourDyeGridRow, BetterGlamourDyeGridRowNode>? listNode;
        private bool pendingRefresh;

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public BetterGlamourHairColorNativeUI()
        {
            InternalName = "OmniBetterGlamourHairColor";
            Title = OmniLoc.Get("Feature.BetterGlamourManagement.HairColor");
            Size = new(380f, 440f);
            ContentPadding = new(10f, 8f);
            RememberClosePosition = true;
            CreateWindowNode = static () => new WindowNode { ShowCloseButton = true };
        }

        public void OpenFor(Vector4[] palette, byte colorID, bool highlights, Action<byte> selected)
        {
            colors = palette;
            selectedID = colorID;
            onSelected = selected;
            Title = OmniLoc.Get(highlights
                ? "Feature.BetterGlamourManagement.HighlightColor"
                : "Feature.BetterGlamourManagement.HairColor");
            if (!IsOpen)
                Open();
            RebuildRows();
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            listNode = new()
            {
                ItemSpacing = 0f,
                ShowNoResultsPlaceholder = false,
                AutoResetScroll = false,
                OptionsList = rows
            };
            listNode.AttachNode(this);
            ResizeContent();
            RebuildRows();
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            ResizeContent();
            if (pendingRefresh)
            {
                pendingRefresh = false;
                RebuildRows();
            }
            listNode?.Update();
        }

        protected override unsafe void OnHide(AtkUnitBase* addon)
        {
            listNode?.HideTooltip();
            onSelected = null;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon)
        {
            listNode = null;
            rows.Clear();
            colors = [];
            onSelected = null;
            pendingRefresh = false;
        }

        private void RebuildRows()
        {
            listNode?.HideTooltip();
            rows.Clear();
            for (var offset = 0; offset < colors.Length; offset += 8)
            {
                var options = new List<GlamourDyeOption>();
                for (var index = offset; index < Math.Min(offset + 8, colors.Length); index++)
                    options.Add(new((byte)index, $"{Title}：{index}", colors[index]));
                rows.Add(new(options, selectedID, SelectColor, IsHairColor: true));
            }
            if (listNode is not null)
                listNode.OptionsList = rows;
        }

        private void SelectColor(GlamourDyeOption option)
        {
            if (option.ID >= colors.Length || onSelected is null)
                return;
            selectedID = option.ID;
            onSelected(option.ID);
            pendingRefresh = true;
        }

        private void ResizeContent()
        {
            if (listNode is null || listNode.Position == ContentStartPosition && listNode.Size == ContentSize)
                return;
            listNode.Position = ContentStartPosition;
            listNode.Size = ContentSize;
        }
    }
}
