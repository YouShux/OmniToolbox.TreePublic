using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class LockedDutyPreview(
    LockedDutyPreviewConfig config,
    Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("LockedDutyPreviewTitle"),
        Description = OmniLoc.Get("LockedDutyPreviewDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Combat/LockedDutyPreview-1.png",
        RequiresPrivateProvider = true
    };

    private const string ContentsFinderAddonName = "ContentsFinder";
    private const float ScreenPadding = 8f;

    private readonly LockedDutyPreviewResolver resolver = new();
    private readonly List<LockedDutyPreviewRow> lockedRows = [];
    private readonly List<LockedDutyPreviewRow> incompleteRows = [];
    private readonly List<LockedDutyPreviewRow> excludedRows = [];
    private FeatureLifetime? runtimeLifetime;
    private LockedDutyPreviewNativeUI? nativeUI;
    private bool contentsFinderWasVisible;
    private LockedDutyPreviewView currentView;
    private long nextRefreshTick;

    public override bool HasSettings => true;

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

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            nativeUI = new(OnViewChanged, OnExclude, OnRestore, row => OpenWiki(row.Name), CopyName);
            lifetime.Add(DisposeNativeUI);

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
            currentView = LockedDutyPreviewView.Locked;
            lockedRows.Clear();
            incompleteRows.Clear();
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
        nextRefreshTick = now + 5_000;
        resolver.Refresh();
        RebuildRows();
        UpdateNativeUIData();
    }

    private void DrawOverlay()
    {
        if (!DService.Instance().ClientState.IsLoggedIn ||
            !TryGetContentsFinder(out AtkUnitBase* addon) ||
            nativeUI is null)
        {
            nativeUI?.Close();
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

        var state = windowNode->GetNodeState();
        var previewState = previewAddon->WindowNode->GetNodeState();
        var display = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var windowX = state.TopLeft.X - previewState.Width - 2f;
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

        nativeUI.SetWindowPosition(new(windowX, windowY));
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
                OmniLoc.Get("Feature.LockedDutyPreview.Summary"),
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

    private static void CopyName(string dutyName) => ImGui.SetClipboardText(dutyName);

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
}

[Serializable]
public sealed class LockedDutyPreviewConfig
{
    public HashSet<uint> ExcludedContentFinderConditionIds { get; set; } = [];
}

internal static class LockedDutyPreviewPanel
{
    public static bool Draw(LockedDutyPreviewConfig config)
    {
        var changed = false;
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.LockedDutyPreview.ClearExcluded"), false))
        {
            changed = config.ExcludedContentFinderConditionIds.Count > 0;
            config.ExcludedContentFinderConditionIds.Clear();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(string.Format(
            OmniLoc.Get("Feature.LockedDutyPreview.ExcludedCount"),
            config.ExcludedContentFinderConditionIds.Count));

        return changed;
    }
}
