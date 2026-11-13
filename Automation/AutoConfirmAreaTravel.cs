using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class AutoConfirmAreaTravel : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Automation,
        Author   = "YouShu"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "Feature.AutoConfirmAreaTravel.Title",
            ["Description"] = "Feature.AutoConfirmAreaTravel.Description"
        });

    private readonly Dictionary<string, uint> destinations = new(StringComparer.Ordinal);
    private bool awaitingPrompt;

    protected override void OnStart(FeatureLifetime activation)
    {
        foreach (var warp in LuminaGetter.Get<Warp>())
        {
            if (warp.Question.IsEmpty)
                continue;

            var question = NormalizePrompt(warp.Question.ToString());
            var destination = GetFreeDestination(warp, question);
            // 同一句提示若还用于其他传送或带条件的确认，则留给玩家处理。
            if (!destinations.TryAdd(question, destination) && destinations[question] != destination)
                destinations[question] = 0;
        }

        awaitingPrompt = true;
        var lifecycle = DService.Instance().AddonLifecycle;
        activation.OnStopping(() => lifecycle.UnregisterListener(OnConfirmation));
        lifecycle.RegisterListener(AddonEvent.PostSetup, ADDON_NAME, OnConfirmation);
        lifecycle.RegisterListener(AddonEvent.PostUpdate, ADDON_NAME, OnConfirmation);
    }

    protected override void OnStop()
    {
        destinations.Clear();
        awaitingPrompt = false;
    }

    private void OnConfirmation(AddonEvent type, AddonArgs args)
    {
        if (Activation.IsStopping)
            return;
        if (type == AddonEvent.PostSetup)
            awaitingPrompt = true;
        if (!awaitingPrompt)
            return;

        var addon = (AddonSelectYesno*)args.Addon.Address;
        if (addon == null || !addon->AtkUnitBase.IsAddonAndNodesReady() || addon->AtkValuesCount == 0 ||
            addon->AtkValues[0].Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString) ||
            !addon->AtkValues[0].String.HasValue)
            return;

        var prompt = NormalizePrompt(new ReadOnlySeStringSpan(addon->AtkValues[0].String.AsSpan()).ExtractText());
        if (prompt.Length == 0)
            return;

        var services = DService.Instance();
        var destination = destinations.GetValueOrDefault(prompt);
        if (destination == 0 || destination == services.ClientState.TerritoryType ||
            !services.ClientState.IsLoggedIn || services.ObjectTable.LocalPlayer == null ||
            services.Condition.IsBetweenAreas || services.Condition[ConditionFlag.InCombat] ||
            services.Condition[ConditionFlag.Unconscious])
        {
            awaitingPrompt = false;
            return;
        }

        // 复选框的内部节点可以保持可见，即使窗口隐藏了整个控件。
        var checkbox = addon->ConfirmCheckBox;
        if (checkbox != null && checkbox->OwnerNode != null && checkbox->OwnerNode->AtkResNode.IsVisible())
        {
            awaitingPrompt = false;
            return;
        }

        var yes = addon->YesButton;
        if (yes == null || yes->OwnerNode == null || !yes->OwnerNode->AtkResNode.IsVisible() || !yes->IsEnabled)
            return;
        awaitingPrompt = false;
        addon->AtkUnitBase.Callback(0);
    }

    private static uint GetFreeDestination(Warp warp, string question)
    {
        if (warp.WarpLogic.RowId != 1 || warp.PopRange.RowId == 0 || warp.ConfirmEvent.RowId != 0 ||
            warp.StartCutscene.RowId != 0 || warp.EndCutscene.RowId != 0 ||
            warp.WarpCondition.ValueNullable is not { Gil: 0 } ||
            warp.TerritoryType.ValueNullable is not { } territory ||
            territory.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty ||
            !question.Contains(place.Name.ToString(), StringComparison.OrdinalIgnoreCase))
            return 0;

        return territory.RowId;
    }

    private static string NormalizePrompt(string text) =>
        text.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

    private const string ADDON_NAME = "SelectYesno";
}
