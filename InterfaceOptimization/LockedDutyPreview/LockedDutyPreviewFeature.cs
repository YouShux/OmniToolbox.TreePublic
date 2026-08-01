using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed unsafe class LockedDutyPreviewFeature(
    LockedDutyPreviewConfig config,
    Action saveConfig) : TreeHouseFeatureBase(new(
    "locked-duty-preview",
    "Feature.LockedDutyPreview.Title",
    "Feature.LockedDutyPreview.Description",
    TreeHouseCategory.InterfaceOptimization,
    true))
{
    private const string ContentsFinderAddonName = "ContentsFinder";
    private const int RefreshIntervalMs = 5_000;
    private const float ScreenPadding = 8f;
    private const float ContentsFinderGap = 38f;

    private readonly LockedDutyPreviewResolver resolver = new();
    private readonly List<LockedDutyPreviewRow> lockedRows = [];
    private readonly List<LockedDutyPreviewRow> excludedRows = [];
    private FeatureLifetime? runtimeLifetime;
    private LockedDutyPreviewNativeUi? nativeUi;
    private bool contentsFinderWasVisible;
    private bool showExcludedView;
    private long nextRefreshTick;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = LockedDutyPreviewPanel.Draw(config);
        if (changed)
        {
            RebuildRows();
            UpdateNativeUiData();
        }

        return changed;
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            nativeUi = new(OnViewChanged, OnExclude, OnRestore, row => OpenWiki(row.Name), CopyName);
            lifetime.Add(DisposeNativeUi);

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Locked-duty preview update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);
            runtimeLifetime = lifetime;
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            contentsFinderWasVisible = false;
            nextRefreshTick = 0;
            showExcludedView = false;
            lockedRows.Clear();
            excludedRows.Clear();
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!DService.Instance().ClientState.IsLoggedIn ||
            !TryGetContentsFinder(out _))
        {
            contentsFinderWasVisible = false;
            return;
        }

        var now = Environment.TickCount64;
        if (contentsFinderWasVisible && now < nextRefreshTick)
        {
            return;
        }

        contentsFinderWasVisible = true;
        nextRefreshTick = now + RefreshIntervalMs;
        resolver.Refresh();
        RebuildRows();
        UpdateNativeUiData();
    }

    private void DrawOverlay()
    {
        if (!DService.Instance().ClientState.IsLoggedIn ||
            !TryGetContentsFinder(out AtkUnitBase* addon) ||
            nativeUi is null)
        {
            nativeUi?.Close();
            return;
        }

        var windowNode = addon->WindowNode;
        var stage = AtkStage.Instance();
        if (windowNode == null || stage == null)
        {
            nativeUi.Close();
            return;
        }

        if (!nativeUi.IsOpen)
        {
            nativeUi.Open();
            var agent = AgentContentsFinder.Instance();
            if (agent != null)
            {
                agent->FocusAddon();
            }
        }

        AtkUnitBase* previewAddon = nativeUi;
        if (previewAddon == null || previewAddon->WindowNode == null)
        {
            return;
        }

        var state = windowNode->GetNodeState();
        var previewState = previewAddon->WindowNode->GetNodeState();
        var display = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var windowX = state.TopLeft.X - previewState.Width - ContentsFinderGap;
        var windowY = state.TopLeft.Y;
        windowX = Math.Max(ScreenPadding, Math.Min(
            windowX,
            display.X - previewState.Width - ScreenPadding));
        if (windowY < ScreenPadding)
        {
            windowY = ScreenPadding;
        }

        if (windowY + previewState.Height > display.Y - ScreenPadding)
        {
            windowY = Math.Max(ScreenPadding, display.Y - previewState.Height - ScreenPadding);
        }

        nativeUi.SetWindowPosition(new(windowX, windowY));
    }

    private static bool TryGetContentsFinder(out AtkUnitBase* addon)
    {
        addon = null;
        if (!AddonHelper.TryGetByName(ContentsFinderAddonName, out addon) ||
            addon == null ||
            !addon->IsVisible ||
            !addon->IsAddonAndNodesReady())
        {
            addon = null;
            return false;
        }

        return addon->AtkValues == null ||
               addon->AtkValuesCount <= 1 ||
               !addon->AtkValues[1].Bool;
    }

    private void RebuildRows()
    {
        lockedRows.Clear();
        excludedRows.Clear();
        var excludedIds = config.ExcludedContentFinderConditionIds;
        var duties = resolver.Duties;
        for (var index = 0; index < duties.Count; index++)
        {
            var duty = duties[index];
            var isExcluded = excludedIds.Contains(duty.ContentFinderConditionId);
            if (isExcluded)
            {
                excludedRows.Add(new(duty.ContentFinderConditionId, duty.Name, true));
            }

            if (!duty.IsUnlocked && !isExcluded)
            {
                lockedRows.Add(new(duty.ContentFinderConditionId, duty.Name, false));
            }
        }
    }

    private void UpdateNativeUiData()
    {
        if (nativeUi is null)
        {
            return;
        }

        nativeUi.UpdateData(
            string.Format(
                OmniLoc.Get("Feature.LockedDutyPreview.Summary"),
                resolver.UnlockedCount,
                lockedRows.Count,
                excludedRows.Count),
            showExcludedView ? excludedRows : lockedRows,
            showExcludedView);
    }

    private void OnViewChanged(bool showExcluded)
    {
        showExcludedView = showExcluded;
        UpdateNativeUiData();
    }

    private void OnExclude(LockedDutyPreviewRow row)
    {
        if (!config.ExcludedContentFinderConditionIds.Add(row.ContentFinderConditionId))
        {
            return;
        }

        saveConfig();
        RebuildRows();
        UpdateNativeUiData();
    }

    private void OnRestore(LockedDutyPreviewRow row)
    {
        if (!config.ExcludedContentFinderConditionIds.Remove(row.ContentFinderConditionId))
        {
            return;
        }

        saveConfig();
        RebuildRows();
        UpdateNativeUiData();
    }

    private static void OpenWiki(string dutyName) =>
        Util.OpenLink($"https://ff14.huijiwiki.com/wiki/{Uri.EscapeDataString(dutyName)}");

    private static void CopyName(string dutyName) => ImGui.SetClipboardText(dutyName);

    private void DisposeNativeUi()
    {
        var ui = nativeUi;
        nativeUi = null;
        if (ui is null)
        {
            return;
        }

        ui.ClearCallbacks();
        ui.Close();
        ui.Dispose();
    }
}
