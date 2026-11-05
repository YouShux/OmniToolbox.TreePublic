using OmniToolbox.UI.Widgets;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreePublic;

public sealed partial class LootRollHistory
{
    private sealed partial class HistoryWindow : IOmniRecordHistorySource<HistoryWindow.SessionOption>
    {
        private long historyMenuRevision = 1;

        long IOmniRecordHistorySource<SessionOption>.Revision => historyMenuRevision * 2 + (page == HistoryPage.Random ? 1 : 0);

        OmniRecordHistoryMenu.Labels IOmniRecordHistorySource<SessionOption>.Labels =>
            new(string.Empty, Text("Import"), Text("Export"), Text("Empty"));

        float IOmniRecordHistorySource<SessionOption>.HeaderWidth =>
            MathF.Ceiling(OmniTextMeasurements.Measure(Text("Loot")).X + ImGui.GetStyle().FramePadding.X * 2f) +
            MathF.Ceiling(OmniTextMeasurements.Measure(Text("Random")).X + ImGui.GetStyle().FramePadding.X * 2f) +
            ImGui.GetStyle().ItemSpacing.X;

        bool IOmniRecordHistorySource<SessionOption>.IsCurrentSelected =>
            SelectedSession is null && page != HistoryPage.Details;

        bool IOmniRecordHistorySource<SessionOption>.CanTransfer =>
            feature.characterID != 0 && renderedSnapshot is { } snapshot &&
            (page == HistoryPage.Random ? snapshot.Status : snapshot.LootStatus) != HistoryStatus.ReadFailed && transfer is null;

        string IOmniRecordHistorySource<SessionOption>.GetID(SessionOption item) => item.ID.ToString();

        string IOmniRecordHistorySource<SessionOption>.GetLabel(SessionOption item) => item.Label;

        bool IOmniRecordHistorySource<SessionOption>.IsSelected(SessionOption item) =>
            SelectedSession == item.ID && page != HistoryPage.Details;

        void IOmniRecordHistorySource<SessionOption>.SelectCurrent() => SelectSession(null);

        void IOmniRecordHistorySource<SessionOption>.Select(SessionOption item) => SelectSession(item.ID);

        void IOmniRecordHistorySource<SessionOption>.Import()
        {
            ImGui.CloseCurrentPopup();
            StartTransfer(true);
        }

        void IOmniRecordHistorySource<SessionOption>.Export(SessionOption item)
        {
            ImGui.CloseCurrentPopup();
            StartTransfer(false, item.ID);
        }

        void IOmniRecordHistorySource<SessionOption>.DrawHeader()
        {
            if (ImGui.SmallButton(Text("Loot")))
            {
                page = HistoryPage.Loot;
                SelectSession(null);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
            if (ImGui.SmallButton(Text("Random")))
            {
                page = HistoryPage.Random;
                SelectSession(null);
                ImGui.CloseCurrentPopup();
            }
            OmniControls.HelpTooltip(Text("RandomHelp"));
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        }
    }
}
