using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class LockedDutyPreview(
    LockedDutyPreview.Settings config,
    Action saveConfig) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/LockedDutyPreview-1.png",
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "LockedDutyPreviewTitle",
            ["Description"] = "LockedDutyPreviewDescription",
            ["Feature.LockedDutyPreview.ClearExcluded"] = "Feature.LockedDutyPreview.ClearExcluded",
            ["Feature.LockedDutyPreview.Exclude"] = "Feature.LockedDutyPreview.Exclude",
            ["Feature.LockedDutyPreview.ExcludedCount"] = "Feature.LockedDutyPreview.ExcludedCount",
            ["Feature.LockedDutyPreview.Restore"] = "Feature.LockedDutyPreview.Restore",
            ["Feature.LockedDutyPreview.Summary"] = "Feature.LockedDutyPreview.Summary",
            ["Feature.LockedDutyPreview.Tab.Excluded"] = "Feature.LockedDutyPreview.Tab.Excluded",
            ["Feature.LockedDutyPreview.Tab.Incomplete"] = "Feature.LockedDutyPreview.Tab.Incomplete",
            ["Feature.LockedDutyPreview.Tab.Locked"] = "Feature.LockedDutyPreview.Tab.Locked",
            ["Feature.LockedDutyPreview.Title"] = "Feature.LockedDutyPreview.Title",
            ["Feature.LockedDutyPreview.Wiki"] = "Feature.LockedDutyPreview.Wiki"
        });

    public override bool HasSettings => true;

    private readonly LockedDutyPreviewResolver resolver = new();
    private readonly List<LockedDutyPreviewRow> lockedRows = [];
    private readonly List<LockedDutyPreviewRow> incompleteRows = [];
    private readonly List<LockedDutyPreviewRow> excludedRows = [];
    private LockedDutyPreviewNativeUI? nativeUI;
    private bool contentsFinderWasVisible;
    private LockedDutyPreviewView currentView;
    private long nextRefreshTick;

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            contentsFinderWasVisible = false;
            nextRefreshTick = 0;
            currentView = LockedDutyPreviewView.Locked;
            lockedRows.Clear();
            incompleteRows.Clear();
            excludedRows.Clear();
        });
        nativeUI = new(OnViewChanged, OnExclude, OnRestore, row => OpenWiki(row.Name), name => ImGui.SetClipboardText(name));
        lifetime.Add(DisposeNativeUI);

        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
        {
            throw new InvalidOperationException("Locked-duty preview update registration failed.");
        }

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
        lifetime.OnStopping(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
    }

    public override bool DrawSettings()
    {
        var changed = LockedDutyPreviewPanel.Draw(config);
        if (changed)
        {
            RebuildRows();
            UpdateNativeUIData();
        }

        return changed;
    }

    private void DrawOverlay()
    {
        if (DalamudServices.IsUnloading || !IsEnabled || Activation.IsStopping || nativeUI is null)
        {
            return;
        }

        if (!DService.Instance().ClientState.IsLoggedIn ||
            !TryGetContentsFinder(out var addon))
        {
            nativeUI.Close();
            return;
        }

        var windowNode = addon->WindowNode;
        var stage = AtkStage.Instance();
        if (windowNode == null || stage == null)
        {
            nativeUI.Close();
            return;
        }

        if (!nativeUI.IsOpen)
        {
            nativeUI.Open();
            var agent = AgentContentsFinder.Instance();
            if (agent != null)
            {
                agent->FocusAddon();
            }
        }

        AtkUnitBase* previewAddon = nativeUI;
        if (previewAddon == null || previewAddon->WindowNode == null)
        {
            return;
        }

        OmniNativeWindowAttachment.SyncBeside(nativeUI, previewAddon, addon, 2f, 0f);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (DalamudServices.IsUnloading || !IsEnabled || Activation.IsStopping || nativeUI is null)
        {
            return;
        }

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
        nextRefreshTick = now + 5_000;
        resolver.Refresh();
        RebuildRows();
        UpdateNativeUIData();
    }

    private static bool TryGetContentsFinder(out AtkUnitBase* addon)
    {
        addon = null;
        if (!AddonHelper.TryGetByName(CONTENTS_FINDER_ADDON_NAME, out addon) ||
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
        incompleteRows.Clear();
        excludedRows.Clear();
        var duties = resolver.Duties;
        for (var index = 0; index < duties.Count; index++)
        {
            var duty = duties[index];
            var isExcluded = config.ExcludedContentFinderConditionIds.Contains(duty.ContentFinderConditionID);
            if (isExcluded)
            {
                excludedRows.Add(new(duty.ContentFinderConditionID, duty.Name, true));
            }

            if (!duty.IsUnlocked && !isExcluded)
            {
                lockedRows.Add(new(duty.ContentFinderConditionID, duty.Name, false));
            }

            if (duty.IsUnlocked && !duty.IsCompleted && !isExcluded)
            {
                incompleteRows.Add(new(duty.ContentFinderConditionID, duty.Name, false));
            }
        }
    }

    private void UpdateNativeUIData()
    {
        if (nativeUI is null)
        {
            return;
        }

        nativeUI.UpdateData(
            string.Format(
                OmniLoc.Get(this, "Feature.LockedDutyPreview.Summary"),
                resolver.UnlockedCount,
                lockedRows.Count,
                incompleteRows.Count,
                excludedRows.Count),
            currentView switch
            {
                LockedDutyPreviewView.Incomplete => incompleteRows,
                LockedDutyPreviewView.Excluded => excludedRows,
                _ => lockedRows
            },
            currentView);
    }

    private void OnViewChanged(LockedDutyPreviewView view)
    {
        currentView = view;
        UpdateNativeUIData();
    }

    private void OnExclude(LockedDutyPreviewRow row)
    {
        if (!config.ExcludedContentFinderConditionIds.Add(row.ContentFinderConditionID))
        {
            return;
        }

        saveConfig();
        RebuildRows();
        UpdateNativeUIData();
    }

    private void OnRestore(LockedDutyPreviewRow row)
    {
        if (!config.ExcludedContentFinderConditionIds.Remove(row.ContentFinderConditionID))
        {
            return;
        }

        saveConfig();
        RebuildRows();
        UpdateNativeUIData();
    }

    private static void OpenWiki(string dutyName) =>
        Util.OpenLink($"https://ff14.huijiwiki.com/wiki/{Uri.EscapeDataString(dutyName)}");

    private void DisposeNativeUI()
    {
        var ui = nativeUI;
        nativeUI = null;
        if (ui is null)
        {
            return;
        }

        ui.ClearCallbacks();
        ui.Close();
        ui.Dispose();
    }

    #region 常量

    private const string CONTENTS_FINDER_ADDON_NAME = "ContentsFinder";

    #endregion

    public sealed class Settings
    {
        public HashSet<uint> ExcludedContentFinderConditionIds { get; set; } = [];
    }
}
