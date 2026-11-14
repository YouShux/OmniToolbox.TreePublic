using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed class AutoFateSync : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new() { Category = ModuleCategory.Combat };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "Feature.AutoFateSync.Title",
            ["Description"] = "Feature.AutoFateSync.Description"
        });

    private ushort currentFateID;
    private long nextSyncAttempt;

    public Func<bool>? IsAutomationRunning { get; set; }

    protected override void OnStart(FeatureLifetime activation)
    {
        SetCurrentFate(0);
        var framework = FrameworkManager.Instance();
        if (!framework.Reg(OnUpdate, 500))
            throw new InvalidOperationException("FATE level sync update registration failed.");
        activation.OnStopping(() => framework.Unreg(OnUpdate));
    }

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!IsEnabled || Activation.IsStopping) return;
        if (IsAutomationRunning?.Invoke() == true)
        {
            SetCurrentFate(0);
            return;
        }

        var services = DService.Instance();
        var condition = services.Condition;
        if (!services.ClientState.IsLoggedIn || condition.IsBetweenAreas ||
            services.ObjectTable.LocalPlayer is null)
        {
            SetCurrentFate(0);
            return;
        }

        var manager = FateManager.Instance();
        var fate = manager == null ? null : manager->CurrentFate;
        if (fate == null || fate->FateId == 0 || fate->State is not (FateState.Preparing or FateState.Running))
        {
            SetCurrentFate(0);
            return;
        }

        if (currentFateID != fate->FateId)
            SetCurrentFate(fate->FateId);
        if (TargetManager.Target is IBattleNPC { IsDead: false } target && target.FateID == currentFateID)
            EnsureTargetLevel(target, ref nextSyncAttempt, out _);
    }

    // 自动流程直接使用同一同步逻辑，无需启用面向手动战斗的模块。
    public static unsafe bool EnsureTargetLevel(IBattleNPC target, ref long nextAttempt, out bool requested)
    {
        requested = false;
        var fateID = target.FateID;
        if (fateID == 0) return true;
        var services = DService.Instance();
        var condition = services.Condition;
        var manager = FateManager.Instance();
        if (manager == null || !services.ClientState.IsLoggedIn || condition.IsBetweenAreas ||
            services.ObjectTable.LocalPlayer is not { } player)
            return false;

        var fate = manager->GetFateById(fateID);
        if (fate == null || fate->State is not (FateState.Preparing or FateState.Running) || fate->MaxLevel == 0)
            return false;
        if (player.Level <= fate->MaxLevel || manager->IsSyncedToFate(fate))
            return true;
        if (manager->GetCurrentFateId() != fateID || condition.IsOnMount ||
            condition[ConditionFlag.InFlight] || condition[ConditionFlag.Unconscious])
            return false;

        var now = Environment.TickCount64;
        if (now >= nextAttempt)
        {
            nextAttempt = now + 1_000;
            FateCommand.SyncLevel(fateID);
            requested = true;
        }
        return false;
    }

    private void SetCurrentFate(ushort fateID)
    {
        currentFateID = fateID;
        nextSyncAttempt = 0;
    }
}
