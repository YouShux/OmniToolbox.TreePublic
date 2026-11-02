using OmenTools.OmenService;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed partial class MitigationMonitor
{
    internal sealed class MitigationMonitorOverlay
    {
        private readonly MitigationMonitorConfig config;
        private readonly MitigationHistoryMenu   historyMenu;
        private readonly MitigationRecordTable   recordTable;
        private readonly OmniRecordWindow        frame;
        private readonly Action                  saveConfig;
        private Action?                          openSettingsAction;

        public MitigationMonitorOverlay
        (
            MitigationMonitorConfig config,
            MitigationCombatLog     combatLog,
            MitigationReplayStore   replayStore,
            Action                  saveConfig
        )
        {
            this.config     = config;
            this.saveConfig = saveConfig;
            historyMenu     = new(combatLog, replayStore);
            recordTable     = new(config, combatLog, saveConfig);
            frame = new(config, "###OmniMitigationMonitor", "###OmniMitigationMonitorCollapsed",
                new Vector2(MinimumWidth, 160f), saveConfig);
        }

        public void SetOpenSettingsAction(Action action) => openSettingsAction = action;

        public void RequestFocus() => frame.Open();

        public void Draw()
        {
            if (!config.Visible || GameState.IsInPVPArea)
            {
                frame.Close();
                return;
            }
            var title = OmniLoc.Get("Feature.MitigationMonitor.Title");
            frame.Draw(title, $"{title} · {OmniLoc.Get("Feature.MitigationMonitor.Expand")}", DrawContent);
        }

        public void ClearSelection()
        {
            historyMenu.ClearSelection();
            recordTable.ResetRuntime();
        }

        public void ResetRuntime()
        {
            historyMenu.ResetRuntime();
            recordTable.ResetRuntime();
            frame.Reset();
        }

        public void RequestLayoutRestore() => frame.RestoreLayout();

        private void DrawContent()
        {
            recordTable.Draw(historyMenu.ActiveHistoryKey, historyMenu.Open, frame.Collapse,
                ToggleLock, () => openSettingsAction?.Invoke());
            historyMenu.Draw();
        }

        private void ToggleLock()
        {
            config.Locked ^= true;
            saveConfig();
        }

        #region 常量

        internal const float MinimumWidth = 420f;

        #endregion
    }
}
