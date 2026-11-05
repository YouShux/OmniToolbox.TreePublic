using OmniToolbox.UI.Widgets;
using System.Globalization;
using System.IO;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using static OmniToolbox.TreePublic.MitigationMonitor;

namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor
{
    internal sealed class MitigationHistoryMenu
    (
        MitigationCombatLog     combatLog,
        MitigationReplayStore   replayStore
    ) : IOmniRecordHistorySource<MitigationCombatHistory>
    {
        private readonly List<MitigationCombatHistory> items          = new(40);
        private string[]                               importFiles    = [];
        private long                                   historyVersion = -1;

        public string? ActiveHistoryKey { get; private set; }

        public void Open() => ImGui.OpenPopup(HISTORY_POPUP_ID);

        public void Draw()
        {
            if (!ImGui.IsPopupOpen(HISTORY_POPUP_ID))
                return;
            RefreshItems();
            OmniRecordHistoryMenu.Draw(HISTORY_POPUP_ID, items, this);
        }

        OmniRecordHistoryMenu.Labels IOmniRecordHistorySource<MitigationCombatHistory>.Labels => new(
            OmniLoc.Get("Feature.MitigationMonitor.History.Realtime"),
            OmniLoc.Get("Feature.MitigationMonitor.History.Import"),
            OmniLoc.Get("Feature.MitigationMonitor.History.Export"),
            OmniLoc.Get("Feature.MitigationMonitor.History.Empty"));

        long IOmniRecordHistorySource<MitigationCombatHistory>.Revision => historyVersion + 1;

        bool IOmniRecordHistorySource<MitigationCombatHistory>.IsCurrentSelected => ActiveHistoryKey is null;

        bool IOmniRecordHistorySource<MitigationCombatHistory>.CanTransfer => true;

        string IOmniRecordHistorySource<MitigationCombatHistory>.GetID(MitigationCombatHistory item) => item.Key;

        string IOmniRecordHistorySource<MitigationCombatHistory>.GetLabel(MitigationCombatHistory item) => GetItemLabel(item);

        bool IOmniRecordHistorySource<MitigationCombatHistory>.IsSelected(MitigationCombatHistory item) => item.Key == ActiveHistoryKey;

        void IOmniRecordHistorySource<MitigationCombatHistory>.SelectCurrent() => ActiveHistoryKey = null;

        void IOmniRecordHistorySource<MitigationCombatHistory>.Select(MitigationCombatHistory item) => ActiveHistoryKey = item.Key;

        void IOmniRecordHistorySource<MitigationCombatHistory>.Import()
        {
            importFiles = replayStore.GetImportableFiles();
            ImGui.OpenPopup(IMPORT_POPUP_ID);
        }

        void IOmniRecordHistorySource<MitigationCombatHistory>.Export(MitigationCombatHistory item) => replayStore.Export(item);

        void IOmniRecordHistorySource<MitigationCombatHistory>.DrawFooter() => DrawImportPopup();

        public void ClearSelection()
        {
            ActiveHistoryKey = null;
            historyVersion   = -1;
        }

        public void ResetRuntime()
        {
            ClearSelection();
            items.Clear();
            importFiles = [];
        }

        private void RefreshItems()
        {
            if (historyVersion == combatLog.HistoryVersion)
            {
                return;
            }

            historyVersion = combatLog.CopyHistory(historyVersion, items);
            if (ActiveHistoryKey == null)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item.Key == ActiveHistoryKey)
                {
                    return;
                }
            }

            ActiveHistoryKey = null;
        }

        private void DrawImportPopup()
        {
            if (!ImGui.IsPopupOpen(IMPORT_POPUP_ID))
                return;

            var contentWidth = OmniTextMeasurements.Measure(OmniLoc.Get("Feature.MitigationMonitor.History.NoImportFiles")).X;
            if (importFiles.Length == 0)
                contentWidth = MathF.Max(contentWidth, OmniTextMeasurements.Measure(replayStore.ExportDirectory).X);
            else
                foreach (var file in importFiles)
                    contentWidth = MathF.Max(contentWidth, OmniTextMeasurements.Measure(Path.GetFileName(file)).X);
            var style = ImGui.GetStyle();
            var workSize = OmniWindowPlacement.GetWorkArea().Size;
            var minimumSize = Vector2.Min(new(contentWidth + style.FramePadding.X * 2f +
                style.WindowPadding.X * 2f + style.WindowBorderSize * 2f + style.ScrollbarSize, 0f), workSize);
            OmniControls.SetNextAutoResizeWindowSizeConstraints(minimumSize, workSize);
            ImGui.SetNextWindowSize(new(minimumSize.X, 0f), ImGuiCond.Always);
            var fontSize = ImGui.GetFontSize();
            using var popup = ImRaii.Popup(IMPORT_POPUP_ID);
            if (!popup)
            {
                return;
            }

            OmniRecordWindow.SetWindowFontSize(fontSize);
            using var scale = new OmniTheme.ScaleScope(ImGui.GetFontSize() / OmniTheme.REFERENCE_FONT_SIZE);
            if (importFiles.Length == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.MitigationMonitor.History.NoImportFiles"));
                using var wrap = ImRaii.TextWrapPos(0f);
                ImGui.TextDisabled(replayStore.ExportDirectory);
            }
            else
            {
                foreach (var file in importFiles)
                {
                    if (ImGui.Selectable($"{Path.GetFileName(file)}##{file}") && replayStore.Import(file) is { } imported)
                    {
                        ActiveHistoryKey = combatLog.AddImported(imported);
                        historyVersion   = -1;
                        ImGui.CloseCurrentPopup();
                    }

                    if (ImGui.IsItemHovered())
                    {
                        OmniControls.HelpTooltip(file);
                    }
                }
            }

        }

        private static string GetItemLabel(MitigationCombatHistory item) =>
            string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.History.Item"),
                item.ElapsedLabel,
                string.IsNullOrWhiteSpace(item.ZoneName)
                    ? OmniLoc.Get("Feature.MitigationMonitor.History.UnknownZone")
                    : item.ZoneName,
                item.StartUTC.ToLocalTime());

        #region 常量

        private const string HISTORY_POPUP_ID = "##MitigationHistoryPopup";

        private const string IMPORT_POPUP_ID = "##MitigationImportPopup";

        #endregion
    }

}
