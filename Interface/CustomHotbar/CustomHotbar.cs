using System.Globalization;
using Dalamud.Interface;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class CustomHotbar : ModuleBase
{
    public const int SlotCount = 12;

    public override ModuleInfo Info { get; } = new()
    {
        Title = "自定义热键栏",
        Description = "在屏幕上放置一组可自定义图标、悬浮说明与指令的热键栏, 点击图标即可执行对应的游戏或插件指令",
        Category = ModuleCategory.Interface,
        Author = "WYJD"
    };

    private readonly CustomHotbarConfig config;
    private readonly Action saveConfig;
    private readonly CustomHotbarOverlay overlay;
    private FeatureLifetime? runtimeLifetime;

    public CustomHotbar() : this(new CustomHotbarConfig())
    {
    }

    public CustomHotbar(CustomHotbarConfig config) : this(config, static () => { })
    {
    }

    public CustomHotbar(CustomHotbarConfig config, Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        if (NormalizeConfig())
        {
            saveConfig();
        }

        overlay = new(config, saveConfig);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = CustomHotbarPanel.Draw(config, OpenIconBrowser);
        if (changed)
        {
            NormalizeConfig();
            saveConfig();
        }

        return changed;
    }

    public override bool ResetSettings()
    {
        config.Bars = [new CustomHotbarBarConfig { Name = "热键栏 1" }];
        NormalizeConfig();
        saveConfig();
        return true;
    }

    protected override void OnEnable()
    {
        NormalizeConfig();
        var lifetime = new FeatureLifetime();
        try
        {
            var windowManager = WindowManager.Instance();
            _ = windowManager.WindowSystem;
            windowManager.PostDraw += overlay.Draw;
            lifetime.Add(() => windowManager.PostDraw -= overlay.Draw);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                runtimeLifetime = null;
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
    }

    internal bool NormalizeConfig()
    {
        var changed = false;
        config.Bars ??= [];

        for (var index = 0; index < config.Bars.Count; index++)
        {
            var bar = config.Bars[index];
            if (string.IsNullOrWhiteSpace(bar.Name))
            {
                bar.Name = $"热键栏 {index + 1}";
                changed = true;
            }

            changed |= NormalizeBar(bar);
        }

        return changed;
    }

    internal static bool NormalizeBar(CustomHotbarBarConfig bar)
    {
        var changed = false;

        var clampedScale = Math.Clamp(bar.GlobalScale <= 0f ? 1f : bar.GlobalScale, 0.25f, 3f);
        if (MathF.Abs(clampedScale - bar.GlobalScale) > 0.0001f)
        {
            bar.GlobalScale = clampedScale;
            changed = true;
        }

        var clampedOpacity = Math.Clamp(bar.Opacity <= 0f ? 0.85f : bar.Opacity, 0.05f, 1f);
        if (MathF.Abs(clampedOpacity - bar.Opacity) > 0.0001f)
        {
            bar.Opacity = clampedOpacity;
            changed = true;
        }

        if (!Enum.IsDefined(bar.Layout))
        {
            bar.Layout = CustomHotbarLayout.TwelveByOne;
            changed = true;
        }

        bar.Slots ??= [];
        while (bar.Slots.Count < SlotCount)
        {
            bar.Slots.Add(new());
            changed = true;
        }

        if (bar.Slots.Count > SlotCount)
        {
            bar.Slots.RemoveRange(SlotCount, bar.Slots.Count - SlotCount);
            changed = true;
        }

        foreach (var slot in bar.Slots)
        {
            slot.Tooltip = (slot.Tooltip ?? string.Empty).Trim();
            slot.Command = (slot.Command ?? string.Empty).Trim();
        }

        return changed;
    }

    internal static bool TryNormalizeCommand(string? value, out string command)
    {
        command = (value ?? string.Empty).Trim().TrimEnd(';').Trim();
        if (command.Length == 0)
        {
            return false;
        }

        if (command[0] == '／')
        {
            command = $"/{command[1..]}";
        }
        else if (command[0] == '＼')
        {
            command = $"\\{command[1..]}";
        }
        else if (command[0] is not ('/' or '\\'))
        {
            command = $"/{command}";
        }

        return true;
    }

    internal static void ExecuteCommand(string command)
    {
        if (!TryNormalizeCommand(command, out var normalized))
        {
            return;
        }

        if (!DalamudServices.CommandManager.ProcessCommand(normalized))
        {
            ChatManager.Instance().SendCommand(normalized);
        }
    }
}

public enum CustomHotbarLayout
{
    TwelveByOne,
    SixByTwo,
    FourByThree,
    ThreeByFour,
    TwoBySix,
    OneByTwelve
}

internal static class CustomHotbarLayoutExtensions
{
    public static int Columns(this CustomHotbarLayout layout) => layout switch
    {
        CustomHotbarLayout.TwelveByOne => 12,
        CustomHotbarLayout.SixByTwo => 6,
        CustomHotbarLayout.FourByThree => 4,
        CustomHotbarLayout.ThreeByFour => 3,
        CustomHotbarLayout.TwoBySix => 2,
        CustomHotbarLayout.OneByTwelve => 1,
        _ => 12
    };

    public static int Rows(this CustomHotbarLayout layout) => CustomHotbar.SlotCount / layout.Columns();

    public static string DisplayName(this CustomHotbarLayout layout) => $"{layout.Columns()} × {layout.Rows()}";
}

[Serializable]
public sealed class CustomHotbarSlot
{
    public uint IconID { get; set; }
    public string Tooltip { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}

[Serializable]
public sealed class CustomHotbarBarConfig
{
    public const float DefaultSlotSize = 40f;
    public const float DefaultSlotSpacing = 4f;

    public string Name { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public Vector2 Position { get; set; } = new(400f, 300f);
    public float GlobalScale { get; set; } = 1f;
    public float Opacity { get; set; } = 0.85f;
    public bool Locked { get; set; }
    public CustomHotbarLayout Layout { get; set; } = CustomHotbarLayout.TwelveByOne;
    public List<CustomHotbarSlot> Slots { get; set; } = [];

    public float EffectiveScale => MathF.Max(0.01f, GlobalScale);
    public float ScaleValue => OmniTheme.ScaleValue * EffectiveScale;
    public float Scale(float value) => value * ScaleValue;
    public Vector2 Scale(Vector2 value) => value * ScaleValue;
}

[Serializable]
public sealed class CustomHotbarConfig
{
    public List<CustomHotbarBarConfig> Bars { get; set; } = [];
}

internal sealed class CustomHotbarOverlay(CustomHotbarConfig config, Action saveConfig)
{
    private bool geometryDirty;
    private readonly CustomHotbarSlotDragState slotDrag = new();

    public void Draw()
    {
        using var font = FontManager.Instance().UIFont.Push();

        for (var index = 0; index < config.Bars.Count; index++)
        {
            var bar = config.Bars[index];
            if (bar.Visible)
            {
                DrawBar(bar, index);
            }
        }

        UpdateSlotDrag();
    }

    private void DrawBar(CustomHotbarBarConfig bar, int index)
    {
        var columns = bar.Layout.Columns();
        var slotSize = bar.Scale(CustomHotbarBarConfig.DefaultSlotSize);
        var spacing = bar.Scale(CustomHotbarBarConfig.DefaultSlotSpacing);

        ImGui.SetNextWindowPos(bar.Position, ImGuiCond.FirstUseEver);

        var flags = ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoTitleBar |
                    ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoResize |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoDocking |
                    ImGuiWindowFlags.NoFocusOnAppearing;
        if (bar.Locked)
        {
            flags |= ImGuiWindowFlags.NoMove;
        }

        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, bar.Scale(new Vector2(6f)))
            .Push(ImGuiStyleVar.WindowRounding, bar.Scale(6f))
            .Push(ImGuiStyleVar.WindowBorderSize, OmniTheme.BorderThickness() * bar.EffectiveScale);
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, new Vector4(0.045f, 0.047f, 0.052f, bar.Opacity))
            .Push(ImGuiCol.Border, new Vector4(0.72f, 0.75f, 0.8f, 0.42f));

        if (ImGui.Begin($"{bar.Name}###OmniCustomHotbar{index}", flags))
        {
            ImGui.SetWindowFontScale(bar.EffectiveScale);
            var drawList = ImGui.GetWindowDrawList();
            var slots = bar.Slots;
            for (var slotIndex = 0; slotIndex < slots.Count && slotIndex < CustomHotbar.SlotCount; slotIndex++)
            {
                if (slotIndex % columns > 0)
                {
                    ImGui.SameLine(0f, spacing);
                }

                var slot = slots[slotIndex];
                var slotPosition = ImGui.GetCursorScreenPos();
                var slotSizeVector = new Vector2(slotSize);

                if (ImGui.InvisibleButton($"##omniCustomHotbarSlot{slotIndex}", slotSizeVector) &&
                    !string.IsNullOrWhiteSpace(slot.Command))
                {
                    CustomHotbar.ExecuteCommand(slot.Command);
                }

                if (slotDrag.SourceIndex < 0 && ImGui.IsItemHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Right))
                {
                    slotDrag.BarIndex = index;
                    slotDrag.SourceIndex = slotIndex;
                    slotDrag.TargetIndex = slotIndex;
                    slotDrag.GrabOffset = ImGui.GetMousePos() - slotPosition;
                }

                if (slotDrag.BarIndex == index && slotDrag.SourceIndex >= 0 &&
                    IsMouseOverSlot(slotPosition, slotSizeVector, spacing * 0.5f))
                {
                    slotDrag.TargetIndex = slotIndex;
                }

                if (ImGui.IsItemHovered() && slotDrag.SourceIndex < 0)
                {
                    drawList.AddRectFilled(
                        slotPosition,
                        slotPosition + slotSizeVector,
                        ImGui.GetColorU32(ImGui.IsItemActive() ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered));
                    if (!string.IsNullOrWhiteSpace(slot.Tooltip))
                    {
                        OmniControls.HelpTooltip(slot.Tooltip);
                    }
                }

                var isDragTarget = slotDrag.BarIndex == index && slotDrag.SourceIndex >= 0 &&
                                   slotDrag.TargetIndex == slotIndex && slotDrag.TargetIndex != slotDrag.SourceIndex;
                DrawSlotIcon(drawList, isDragTarget ? 0u : slot.IconID, slotPosition, slotSizeVector, bar.EffectiveScale);
            }

            if (slotDrag.BarIndex == index && slotDrag.SourceIndex >= 0 && slotDrag.SourceIndex < slots.Count)
            {
                var floatingPosition = ImGui.GetMousePos() - slotDrag.GrabOffset;
                DrawSlotIcon(ImGui.GetForegroundDrawList(), slots[slotDrag.SourceIndex].IconID, floatingPosition, new Vector2(slotSize), bar.EffectiveScale);
            }

            UpdateWindowGeometry(bar);
        }

        ImGui.End();
    }

    private static void DrawSlotIcon(ImDrawListPtr drawList, uint iconID, Vector2 position, Vector2 size, float scale)
    {
        var rounding = size.X * 0.08f;
        drawList.AddRectFilled(position, position + size, 0x99000000, rounding);
        if (iconID > 0 && ImageHelper.GetGameIcon(iconID) is { } texture)
        {
            drawList.AddImage(texture.Handle, position, position + size);
        }

        drawList.AddRect(position, position + size, 0xFFC0C8D0, rounding, ImDrawFlags.None, MathF.Max(1f, scale));
    }

    private void UpdateWindowGeometry(CustomHotbarBarConfig bar)
    {
        if (bar.Locked)
        {
            return;
        }

        var position = ImGui.GetWindowPos();
        if (Vector2.DistanceSquared(position, bar.Position) > 0.25f)
        {
            bar.Position = position;
            geometryDirty = true;
        }

        if (geometryDirty && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            geometryDirty = false;
            saveConfig();
        }
    }

    private void UpdateSlotDrag()
    {
        if (slotDrag.SourceIndex < 0)
        {
            return;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            CommitSlotDrag();
        }
        else if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            slotDrag.Cancel();
        }
    }

    private void CommitSlotDrag()
    {
        var barIndex = slotDrag.BarIndex;
        var sourceIndex = slotDrag.SourceIndex;
        var targetIndex = slotDrag.TargetIndex;
        slotDrag.Cancel();

        if (barIndex < 0 || barIndex >= config.Bars.Count)
        {
            return;
        }

        var slots = config.Bars[barIndex].Slots;
        if (sourceIndex < 0 || sourceIndex >= slots.Count ||
            targetIndex < 0 || targetIndex >= slots.Count ||
            sourceIndex == targetIndex)
        {
            return;
        }

        (slots[sourceIndex], slots[targetIndex]) = (slots[targetIndex], slots[sourceIndex]);
        saveConfig();
    }

    private static bool IsMouseOverSlot(Vector2 position, Vector2 size, float halfGap)
    {
        var mouse = ImGui.GetMousePos();
        return mouse.X >= position.X - halfGap && mouse.X < position.X + size.X + halfGap &&
               mouse.Y >= position.Y - halfGap && mouse.Y < position.Y + size.Y + halfGap;
    }
}

internal sealed class CustomHotbarSlotDragState
{
    public int BarIndex = -1;
    public int SourceIndex = -1;
    public int TargetIndex = -1;
    public Vector2 GrabOffset;

    public void Cancel()
    {
        BarIndex = -1;
        SourceIndex = -1;
        TargetIndex = -1;
        GrabOffset = Vector2.Zero;
    }
}

internal static class CustomHotbarPanel
{
    private const string SlotReorderPayload = "OmniCustomHotbarSlotReorder";

    private static readonly (CustomHotbarLayout Layout, string Name)[] LayoutOptions =
    [
        (CustomHotbarLayout.TwelveByOne, "12 × 1"),
        (CustomHotbarLayout.SixByTwo, "6 × 2"),
        (CustomHotbarLayout.FourByThree, "4 × 3"),
        (CustomHotbarLayout.ThreeByFour, "3 × 4"),
        (CustomHotbarLayout.TwoBySix, "2 × 6"),
        (CustomHotbarLayout.OneByTwelve, "1 × 12")
    ];

    private static float IconPreviewSize => OmniTheme.Scale(24f);

    private static int selectedBarIndex;
    private static int draggedSlotBarIndex = -1;
    private static int draggedSlotIndex = -1;

    public static bool Draw(CustomHotbarConfig config, Action<Action<uint>> openIconBrowser)
    {
        var changed = false;

        changed |= DrawBarSelector(config);

        if (config.Bars.Count == 0)
        {
            ImGui.TextUnformatted("暂无热键栏, 点击\"新增热键栏\"创建");
            return changed;
        }

        selectedBarIndex = Math.Clamp(selectedBarIndex, 0, config.Bars.Count - 1);
        var bar = config.Bars[selectedBarIndex];

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        changed |= DrawBarEditor(bar, selectedBarIndex, openIconBrowser);
        return changed;
    }

    private static bool DrawBarSelector(CustomHotbarConfig config)
    {
        var changed = false;
        var width = ImGui.GetContentRegionAvail().X * 0.5f;

        var preview = config.Bars.Count > 0 && selectedBarIndex < config.Bars.Count
            ? config.Bars[selectedBarIndex].Name
            : string.Empty;
        if (OmniControls.BeginCombo("##customHotbarBarSelector", preview, width))
        {
            for (var index = 0; index < config.Bars.Count; index++)
            {
                if (ImGui.Selectable($"{config.Bars[index].Name}##bar{index}", index == selectedBarIndex))
                {
                    selectedBarIndex = index;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (OmniControls.IconButton("customHotbarAddBar", FontAwesomeIcon.Plus, false, "新增热键栏"))
        {
            var offset = 30f * config.Bars.Count;
            config.Bars.Add(new CustomHotbarBarConfig
            {
                Name = $"热键栏 {config.Bars.Count + 1}",
                Position = new Vector2(400f + offset, 300f + offset)
            });
            selectedBarIndex = config.Bars.Count - 1;
            changed = true;
        }

        if (config.Bars.Count > 0 && selectedBarIndex < config.Bars.Count)
        {
            ImGui.SameLine();
            if (OmniControls.IconButton("customHotbarRemoveBar", FontAwesomeIcon.Trash, false, "删除此热键栏 (不可恢复, 该热键栏的所有格子配置都会丢失)"))
            {
                config.Bars.RemoveAt(selectedBarIndex);
                selectedBarIndex = Math.Max(0, selectedBarIndex - 1);
                changed = true;
            }
        }

        return changed;
    }

    private static bool DrawBarEditor(CustomHotbarBarConfig bar, int barIndex, Action<Action<uint>> openIconBrowser)
    {
        var changed = false;

        var name = bar.Name;
        ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
        if (ImGui.InputText("名称##customHotbarBarName", ref name, 64))
        {
            bar.Name = name;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var visible = bar.Visible;
        if (OmniControls.Checkbox("显示##customHotbarBarVisible", ref visible))
        {
            bar.Visible = visible;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var locked = bar.Locked;
        if (OmniControls.Checkbox("锁定位置##customHotbarLocked", ref locked))
        {
            bar.Locked = locked;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon("锁定后无法拖动热键栏窗口, 防止误触移动");

        if (OmniControls.BeginCombo("布局##customHotbarLayout", bar.Layout.DisplayName(), OmniTheme.Scale(120f)))
        {
            foreach (var (layout, layoutName) in LayoutOptions)
            {
                if (ImGui.Selectable($"{layoutName}##customHotbarLayout{layout}", bar.Layout == layout))
                {
                    bar.Layout = layout;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, OmniTheme.Scale(6f));
        OmniControls.HelpIcon("热键栏格子的排列方式, 与游戏内热键栏的 12 格布局一致");

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var scale = bar.GlobalScale;
        if (OmniControls.DragFloat("缩放##customHotbarScale", ref scale, 0.05f, 0.25f, 3f, "%.2f", OmniTheme.Scale(110f), ImGuiSliderFlags.AlwaysClamp))
        {
            bar.GlobalScale = scale;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine(0f, OmniTheme.Scale(12f));
        var opacity = bar.Opacity;
        if (OmniControls.DragFloat("不透明度##customHotbarOpacity", ref opacity, 0.01f, 0.05f, 1f, "%.2f", OmniTheme.Scale(110f), ImGuiSliderFlags.AlwaysClamp))
        {
            bar.Opacity = opacity;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        changed |= DrawSlotsTable(bar, barIndex, openIconBrowser);
        return changed;
    }

    private static bool DrawSlotsTable(CustomHotbarBarConfig bar, int barIndex, Action<Action<uint>> openIconBrowser)
    {
        var changed = false;
        var columns = bar.Layout.Columns();

        using var table = ImRaii.Table("##customHotbarSlots", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(-1f, OmniTheme.Scale(320f)));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("位置", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(80f));
        ImGui.TableSetupColumn("图标", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(200f));
        ImGui.TableSetupColumn("悬浮说明", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("执行指令", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("排序", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(64f));

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        string[] headers = ["位置", "图标", "悬浮说明", "执行指令", "排序"];
        for (var column = 0; column < headers.Length; column++)
        {
            ImGui.TableSetColumnIndex(column);
            var headerWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (headerWidth - ImGui.CalcTextSize(headers[column]).X) * 0.5f));
            ImGui.TableHeader(headers[column]);
        }

        var rowHeight = MathF.Max(IconPreviewSize, ImGui.GetFrameHeight()) + ImGui.GetStyle().CellPadding.Y * 2f;
        for (var index = 0; index < bar.Slots.Count && index < CustomHotbar.SlotCount; index++)
        {
            var slot = bar.Slots[index];
            ImGui.PushID(index);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

            ImGui.TableNextColumn();
            CenterCellContent(rowHeight, ImGui.GetTextLineHeight());
            ImGui.TextUnformatted($"第{index / columns + 1}行 第{index % columns + 1}列");

            ImGui.TableNextColumn();
            CenterCellContent(rowHeight, IconPreviewSize);
            changed |= DrawIconCell(slot, openIconBrowser);

            ImGui.TableNextColumn();
            CenterCellContent(rowHeight, ImGui.GetFrameHeight());
            var tooltip = slot.Tooltip;
            ImGui.SetNextItemWidth(-1f);
            if (OmniControls.InputTextWithHint("##tooltip", "鼠标悬浮时显示的说明文本", ref tooltip, 128))
            {
                slot.Tooltip = tooltip;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();

            ImGui.TableNextColumn();
            CenterCellContent(rowHeight, ImGui.GetFrameHeight());
            var command = slot.Command;
            ImGui.SetNextItemWidth(-1f);
            if (OmniControls.InputTextWithHint("##command", "如 /ac 技能名 或 /p 文本", ref command, 128))
            {
                slot.Command = command;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();

            ImGui.TableNextColumn();
            CenterCellContent(rowHeight, ImGui.GetFrameHeight());
            changed |= DrawSlotReorderHandle(barIndex, bar.Slots, index);

            ImGui.PopID();
        }

        return changed;
    }

    private static void CenterCellContent(float rowHeight, float contentHeight)
    {
        var offset = (rowHeight - ImGui.GetStyle().CellPadding.Y * 2f - contentHeight) * 0.5f;
        if (offset > 0f)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        }
    }

    private static bool DrawIconCell(CustomHotbarSlot slot, Action<Action<uint>> openIconBrowser)
    {
        var changed = false;

        var cursor = ImGui.GetCursorScreenPos();
        var previewSize = IconPreviewSize;
        if (slot.IconID > 0 && ImageHelper.GetGameIcon(slot.IconID) is { } texture)
        {
            ImGui.GetWindowDrawList().AddImage(texture.Handle, cursor, cursor + new Vector2(previewSize));
        }
        else
        {
            ImGui.GetWindowDrawList().AddRect(cursor, cursor + new Vector2(previewSize), 0xFF808080, OmniTheme.Scale(2f));
        }

        ImGui.Dummy(new Vector2(previewSize));
        ImGui.SameLine();

        var iconText = slot.IconID.ToString(CultureInfo.InvariantCulture);
        ImGui.SetNextItemWidth(OmniTheme.Scale(70f));
        if (ImGui.InputText("##iconId", ref iconText, 16, ImGuiInputTextFlags.CharsDecimal) &&
            uint.TryParse(iconText, out var iconID))
        {
            slot.IconID = iconID;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine();
        if (OmniControls.SmallButton("选择##pickIcon", false))
        {
            openIconBrowser(iconID => slot.IconID = iconID);
        }

        OmniControls.HelpTooltip("打开图标浏览器");

        return changed;
    }

    private static bool DrawSlotReorderHandle(int barIndex, List<CustomHotbarSlot> slots, int index)
    {
        OmniControls.IconButton($"##customHotbarSlotReorder{barIndex}_{index}", FontAwesomeIcon.Bars, false, "按住拖动以调整格子位置");

        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                if (ImGui.SetDragDropPayload(SlotReorderPayload, []))
                {
                    draggedSlotBarIndex = barIndex;
                    draggedSlotIndex = index;
                }

                ImGui.TextUnformatted($"拖动: 第 {index + 1} 格");
            }
        }

        using var target = ImRaii.DragDropTarget();
        if (!target)
        {
            return false;
        }

        var payload = ImGui.AcceptDragDropPayload(SlotReorderPayload);
        if (payload.IsNull ||
            !payload.IsDelivery() ||
            draggedSlotBarIndex != barIndex ||
            draggedSlotIndex < 0 ||
            draggedSlotIndex == index)
        {
            return false;
        }

        (slots[draggedSlotIndex], slots[index]) = (slots[index], slots[draggedSlotIndex]);
        draggedSlotBarIndex = -1;
        draggedSlotIndex = -1;
        return true;
    }

}
