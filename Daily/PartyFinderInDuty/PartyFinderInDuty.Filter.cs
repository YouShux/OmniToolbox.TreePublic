using Dalamud.Game.Gui.PartyFinder.Types;
using Lumina.Excel.Sheets;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI.Theme;
using OmniToolbox.UI.Widgets;

namespace OmniToolbox.Modules;

public sealed partial class PartyFinderInDuty
{
    private DutyOption[]? dutyOptions;
    private DutyOption[]? matchingDuties;
    private string dutySearch = string.Empty;
    private bool hiddenOnly;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var hiddenOnlyLabel = OmniLoc.Get(this, "HiddenOnly");
        var searchWidth = ImGui.GetContentRegionAvail().X - OmniControls.MeasureCheckbox(hiddenOnlyLabel).X -
                          ImGui.GetStyle().ItemSpacing.X;
        var searchChanged = OmniControls.InputTextWithHint("##dutySearch", OmniLoc.Get(this, "DutySearch"),
            ref dutySearch, 128, MathF.Max(1f, searchWidth));
        ImGui.SameLine();
        searchChanged |= OmniControls.Checkbox(hiddenOnlyLabel, ref hiddenOnly);

        dutyOptions ??= LuminaGetter.Get<ContentFinderCondition>()
            .Where(static row => row.RowId != 0 && row.ContentType.RowId != 0 && !row.Name.IsEmpty)
            .Select(static row =>
            {
                var type = row.ContentType.ValueNullable;
                return new DutyOption(row.RowId, row.Name.ExtractText(),
                    type?.Name.ExtractText() ?? string.Empty, type?.Icon ?? 0);
            })
            .Where(static duty => !string.IsNullOrWhiteSpace(duty.Name))
            .OrderBy(static duty => duty.Name, StringComparer.CurrentCulture)
            .ThenBy(static duty => duty.ID)
            .ToArray();
        if (searchChanged)
            matchingDuties = null;
        if (matchingDuties is null)
        {
            var query = dutySearch.Trim();
            matchingDuties = query.Length == 0 && !hiddenOnly ? dutyOptions :
                dutyOptions.Where(duty => (!hiddenOnly || config.HiddenDutyIDs.Contains(duty.ID)) &&
                    (duty.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     duty.Type.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        if (matchingDuties.Length == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get(this, "NoMatchingDuties"));
            return false;
        }

        var checkboxSize = OmniTheme.CheckboxSize();
        var iconSize = OmniTheme.Scale(26f);
        var rowHeight = MathF.Max(checkboxSize, MathF.Max(iconSize, ImGui.GetTextLineHeight()));
        var rowStride = rowHeight + ImGui.GetStyle().CellPadding.Y * 2f;
        using var table = ImRaii.Table("##hiddenDuties", 3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(0f, rowStride * (Math.Min(matchingDuties.Length, 8) + 1)));
        if (!table)
            return false;

        if (searchChanged)
            ImGui.SetScrollY(0f);
        ImGui.TableSetupColumn("##hidden", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, rowHeight);
        ImGui.TableSetupColumn(OmniLoc.Get(this, "DutyName"), ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn(OmniLoc.Get(this, "DutyType"), ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow(rowHeight);
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var allHidden = matchingDuties.All(duty => config.HiddenDutyIDs.Contains(duty.ID));
        OmniControls.CenterTableItem(new Vector2(rowHeight), rowHeight);
        var changed = OmniControls.Checkbox("##hideAll", ref allHidden);
        if (changed)
        {
            foreach (var duty in matchingDuties)
            {
                if (allHidden)
                    config.HiddenDutyIDs.Add(duty.ID);
                else
                    config.HiddenDutyIDs.Remove(duty.ID);
            }
        }
        OmniControls.HelpTooltip(OmniLoc.Get(this, allHidden ? "UnhideMatchingDuties" : "HideMatchingDuties"));
        OmniControls.TableHeader(OmniLoc.Get(this, "DutyName"), rowHeight);
        OmniControls.TableHeader(OmniLoc.Get(this, "DutyType"), rowHeight);

        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(matchingDuties.Length, rowStride);
            while (clipper.Step())
            {
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                {
                    var duty = matchingDuties[index];
                    using var id = ImRaii.PushId((int)duty.ID);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                    ImGui.TableNextColumn();
                    OmniControls.CenterTableItem(new Vector2(rowHeight), rowHeight);
                    var hidden = config.HiddenDutyIDs.Contains(duty.ID);
                    if (OmniControls.Checkbox("##hidden", ref hidden))
                    {
                        if (hidden)
                            config.HiddenDutyIDs.Add(duty.ID);
                        else
                            config.HiddenDutyIDs.Remove(duty.ID);
                        changed = true;
                    }

                    ImGui.TableNextColumn();
                    var position = ImGui.GetCursorScreenPos();
                    if (duty.IconID != 0)
                        FramedGameIcon.Draw(duty.IconID, position + new Vector2(0f, (rowHeight - iconSize) * 0.5f),
                            new Vector2(iconSize), drawFrame: false);
                    var nameOffset = iconSize + ImGui.GetStyle().ItemInnerSpacing.X;
                    var nameSize = OmniTextMeasurements.Measure(duty.Name);
                    var cellWidth = ImGui.GetContentRegionAvail().X;
                    ImGui.GetWindowDrawList().AddText(
                        position + new Vector2(nameOffset, (rowHeight - nameSize.Y) * 0.5f),
                        OmniTheme.Color(OmniTheme.Tokens.Text), duty.Name);
                    ImGui.Dummy(new Vector2(MathF.Max(1f, cellWidth), rowHeight));
                    if (nameOffset + nameSize.X > cellWidth)
                        OmniControls.HelpTooltip(duty.Name);

                    ImGui.TableNextColumn();
                    OmniControls.TableTextCentered(duty.Type, rowHeight);
                }
            }
        }
        finally
        {
            clipper.End();
            clipper.Destroy();
        }

        if (changed)
            RefreshDutyFilter();
        return changed;
    }

    public override bool ResetSettings()
    {
        if (config.HiddenDutyIDs.Count == 0)
            return false;

        config.HiddenDutyIDs.Clear();
        RefreshDutyFilter();
        return true;
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (windowOpen && IsDutyHidden((uint)listing.DutyType, listing.RawDuty) && CanOperatePartyFinder())
            args.Visible = false;
    }

    private bool IsDutyHidden(uint dutyType, ushort dutyID) =>
        dutyType == (uint)DutyType.Normal && config.HiddenDutyIDs.Contains(dutyID);

    private void RefreshDutyFilter()
    {
        if (hiddenOnly)
            matchingDuties = null;

        if (!windowOpen || !CanOperatePartyFinder())
            return;

        if (UsesSharedData)
            injectionRequested = true;
        else
            nativeRefreshRequested = true;
    }

    private readonly record struct DutyOption(uint ID, string Name, string Type, uint IconID);

    public sealed class Settings
    {
        public HashSet<uint> HiddenDutyIDs { get; set; } = [];
    }
}
