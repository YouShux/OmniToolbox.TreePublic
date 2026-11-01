using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class AutoDeleteDeepDungeonSave
{
    private string[]? dungeonNames;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var floorRangeTemplate = OmniLoc.Get(this, "FloorRange");
        var names = dungeonNames ??= Enumerable.Range(1, 4).Select(id => LuminaWrapper.GetDeepDungeonName((uint)id)).ToArray();
        var nameWidth = 0f;
        foreach (var name in names)
            nameWidth = MathF.Max(nameWidth, ImGui.CalcTextSize(name).X);

        var checkboxSize = OmniControls.MeasureCheckbox(string.Empty);
        var rowHeight = MathF.Max(checkboxSize.Y, ImGui.GetFrameHeight());
        var rangeWidth = MathF.Max(checkboxSize.X,
            ImGui.CalcTextSize(string.Format(floorRangeTemplate, DisplayFloorRanges[^1].Minimum, DisplayFloorRanges[^1].Maximum)).X);
        var style = ImGui.GetStyle();
        var headerHeight = (ImGui.GetTextLineHeight() * 2f) + style.ItemInnerSpacing.Y;
        var minimumWidth = nameWidth + (rangeWidth * DisplayFloorRanges.Length) +
                           ((style.CellPadding.X * 2f) * (DisplayFloorRanges.Length + 1)) + DisplayFloorRanges.Length + 2f;
        var scrollHorizontally = minimumWidth > ImGui.GetContentRegionAvail().X;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame;
        if (scrollHorizontally)
            flags |= ImGuiTableFlags.ScrollX;
        var tableHeight = scrollHorizontally
            ? headerHeight + (rowHeight * names.Length) + (style.CellPadding.Y * 2f * (names.Length + 1)) +
              style.ScrollbarSize + 2f
            : 0f;
        using var table = ImRaii.Table("##floorRanges", DisplayFloorRanges.Length + 1, flags,
                                      new Vector2(0f, tableHeight), scrollHorizontally ? minimumWidth : 0f);
        if (!table)
            return false;

        ImGui.TableSetupColumn("##dungeon", ImGuiTableColumnFlags.WidthFixed, nameWidth);
        foreach (var range in DisplayFloorRanges)
            ImGui.TableSetupColumn($"##range{range.Minimum}-{range.Maximum}", ImGuiTableColumnFlags.WidthStretch);
        OmniControls.BeginTableHeaderRow(headerHeight);
        OmniControls.TableHeader(string.Empty, headerHeight);
        foreach (var range in DisplayFloorRanges)
            OmniControls.TableHeader(string.Format(floorRangeTemplate, range.Minimum, range.Maximum), headerHeight);

        for (uint dungeonID = 1; dungeonID <= 4; dungeonID++)
        {
            using var dungeonScope = ImRaii.PushId((int)dungeonID);
            var ranges = GetFloorRanges(dungeonID);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(names[dungeonID - 1], rowHeight);
            config.AllowedFloorSets.TryGetValue(dungeonID, out var selectedFloorSets);

            foreach (var range in DisplayFloorRanges)
            {
                ImGui.TableNextColumn();
                if (!ranges.Contains(range))
                    continue;

                using var floorScope = ImRaii.PushId(range.Minimum);
                OmniControls.CenterTableItem(checkboxSize, rowHeight);
                var enabled = selectedFloorSets is not null && range.IsSelected(selectedFloorSets);
                if (!OmniControls.Checkbox("##allowed", ref enabled))
                    continue;

                selectedFloorSets ??= [];
                for (var floorSet = (range.Minimum - 1) / 10; floorSet <= (range.Maximum - 1) / 10; floorSet++)
                {
                    if (enabled)
                        selectedFloorSets.Add(floorSet);
                    else
                        selectedFloorSets.Remove(floorSet);
                }
                if (selectedFloorSets.Count == 0)
                    config.AllowedFloorSets.Remove(dungeonID);
                else
                    config.AllowedFloorSets[dungeonID] = selectedFloorSets;
                changed = true;
            }
        }
        return changed;
    }

    public override bool ResetSettings()
    {
        if (config.AllowedFloorSets.Count == 0)
            return false;

        config.AllowedFloorSets.Clear();
        return true;
    }

    private static ReadOnlySpan<FloorRange> GetFloorRanges(uint dungeonID) => dungeonID switch
    {
        1 => PalaceFloorRanges,
        2 or 3 => OtherFloorRanges,
        4 => PilgrimFloorRanges,
        _ => []
    };

    private readonly record struct FloorRange(int Minimum, int Maximum)
    {
        public bool Contains(int floor) => floor >= Minimum && floor <= Maximum;

        public bool IsSelected(HashSet<int> selectedFloorSets)
        {
            for (var floorSet = (Minimum - 1) / 10; floorSet <= (Maximum - 1) / 10; floorSet++)
                if (!selectedFloorSets.Contains(floorSet))
                    return false;
            return true;
        }
    }

    public sealed class Settings
    {
        public Dictionary<uint, HashSet<int>> AllowedFloorSets = [];
    }

    #region 预置数据

    private static readonly FloorRange[] PalaceFloorRanges = [new(1, 50), new(51, 200)];
    private static readonly FloorRange[] OtherFloorRanges = [new(1, 20), new(21, 100)];
    private static readonly FloorRange[] PilgrimFloorRanges = [new(1, 20), new(21, 30), new(31, 50), new(51, 70), new(71, 100)];
    private static readonly FloorRange[] DisplayFloorRanges = PalaceFloorRanges.Concat(OtherFloorRanges).Concat(PilgrimFloorRanges)
        .Distinct().OrderBy(range => range.Minimum).ThenBy(range => range.Maximum).ToArray();

    #endregion
}
