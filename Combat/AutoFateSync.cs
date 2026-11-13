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
    private bool syncConfirmed;
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

    private unsafe void OnUpdate(IFramework _)
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
            services.ObjectTable.LocalPlayer is not { } player)
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
        if (syncConfirmed) return;
        if (manager->IsSyncedToFate(fate))
        {
            syncConfirmed = true;
            return;
        }
        if (TargetManager.Target is not IBattleNPC { IsDead: false } target || target.FateID != currentFateID ||
            fate->MaxLevel == 0 || player.Level <= fate->MaxLevel || condition.IsOnMount ||
            condition[ConditionFlag.InFlight] || condition[ConditionFlag.Unconscious])
            return;

        var now = Environment.TickCount64;
        if (now < nextSyncAttempt) return;
        nextSyncAttempt = now + 1_000;
        FateCommand.SyncLevel(currentFateID);
    }

    private void SetCurrentFate(ushort fateID)
    {
        currentFateID = fateID;
        syncConfirmed = false;
        nextSyncAttempt = 0;
    }
}
