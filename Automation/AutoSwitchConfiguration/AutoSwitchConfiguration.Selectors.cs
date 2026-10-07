using Lumina.Excel;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.ImGuiOm.Widgets.Combos;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoSwitchConfiguration
{
    private static uint? DrawSelectionButton<T>(string id, string preview, ref string search,
        LuminaSearcher<T> searcher, uint selectedID, int columnCount, System.Action setupColumns,
        System.Action drawHeaders, Func<T, bool, bool> drawColumns,
        Func<List<T>, IReadOnlyList<T>>? orderResults = null) where T : struct, IExcelRow<T>
    {
        using var scope = ImRaii.PushId(id);
        if (OmniControls.SmallButton($"{preview}###select", false))
            ImGui.OpenPopup("##selection");

        using var popupStyles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, OmniTheme.PopupPadding())
            .Push(ImGuiStyleVar.PopupRounding, OmniTheme.Scale(OmniTheme.Tokens.BorderRadius))
            .Push(ImGuiStyleVar.PopupBorderSize, 0f);
        var viewport = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSize(Vector2.Min(new Vector2(ImGui.GetFontSize() * 36f, ImGui.GetFontSize() * 22f),
            Vector2.Max(Vector2.One, viewport - ImGui.GetStyle().WindowPadding * 2f)));
        using var background = ImRaii.PushColor(ImGuiCol.PopupBg, OmniTheme.Tokens.Surface);
        using var popup = ImRaii.Popup("##selection", ImGuiWindowFlags.NoSavedSettings);
        if (!popup)
            return null;

        OmniControls.DrawPopupBackground();
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        if (OmniControls.InputTextWithHint("##search", OmniLoc.Get("Feature.AutoSwitchConfiguration.Search"),
                ref search, 128, ImGui.GetContentRegionAvail().X))
            searcher.Search(search);

        var results = orderResults is null ? searcher.SearchResult : orderResults(searcher.SearchResult);
        if (results.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.AutoSwitchConfiguration.NoMatches"));
            return null;
        }

        using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, 0f));
        using var table = ImRaii.Table("##options", columnCount,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            ImGui.GetContentRegionAvail());
        if (!table)
            return null;

        setupColumns();
        ImGui.TableSetupScrollFreeze(0, 1);
        drawHeaders();
        foreach (var item in results)
        {
            if (item.RowId == 0)
                continue;

            using var rowID = ImRaii.PushId((int)item.RowId);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var selected = item.RowId == selectedID;
            var clicked = ImGui.RadioButton("##selected", selected);
            clicked |= drawColumns(item, selected);
            if (!clicked)
                continue;

            ImGui.CloseCurrentPopup();
            return item.RowId;
        }
        return null;
    }

    private sealed class ZoneSelectionButton(string id) : ZoneSelectCombo(id)
    {
        public bool DrawButton(string preview)
        {
            var selected = DrawSelectionButton(ID, preview, ref SearchWord, Searcher, SelectedID,
                GetTableColumnCount(), () => SetupColumns(ComboSelectionMode.Radio), DrawHeaders,
                (item, active) => DrawDataColumns(item, ComboSelectionMode.Radio, active));
            if (selected is not { } selectedID)
                return false;

            SelectedID = selectedID;
            return true;
        }
    }

    private sealed class JobSelectionButton(string id) : JobSelectCombo(id)
    {
        private List<ClassJob>? searchResults;
        private List<ClassJob> orderedResults = [];

        public bool DrawButton(string preview)
        {
            var selected = DrawSelectionButton(ID, preview, ref SearchWord, Searcher, SelectedID,
                GetTableColumnCount(), () => SetupColumns(ComboSelectionMode.Radio), DrawHeaders,
                (item, active) => DrawDataColumns(item, ComboSelectionMode.Radio, active), OrderResults);
            if (selected is not { } selectedID)
                return false;

            SelectedID = selectedID;
            return true;
        }

        private IReadOnlyList<ClassJob> OrderResults(List<ClassJob> results)
        {
            if (ReferenceEquals(searchResults, results))
                return orderedResults;

            searchResults = results;
            orderedResults = new(results);
            orderedResults.Sort(static (left, right) =>
            {
                var order = GetRoleOrder(left).CompareTo(GetRoleOrder(right));
                return order != 0 ? order : left.RowId.CompareTo(right.RowId);
            });
            return orderedResults;
        }

        private static int GetRoleOrder(ClassJob job)
        {
            if (job.JobIndex == 0 && job.DohDolJobIndex == -1)
                return job.Role switch
                {
                    1 => 7,
                    4 => 8,
                    _ => 9
                };

            return job.ToJobType() switch
            {
                ClassJobType.Tank => 0,
                ClassJobType.PureHealer or ClassJobType.ShieldHealer => 1,
                ClassJobType.Melee => 2,
                ClassJobType.PhysicalRanged => 3,
                ClassJobType.MagicalRanged => 4,
                ClassJobType.Crafter => 5,
                ClassJobType.Gatherer => 6,
                _ => 10
            };
        }
    }
}
