using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class DutyReadyClassSwitching : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DutyReadyClassSwitchingTitle",
            ["Description"] = "DutyReadyClassSwitchingDescription"
        });

    private AddonEventRegistry? addonEvents;
    private nint addonAddress;
    private bool switchAttempted;
    private bool addonVisible;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => addonEvents?.Dispose());

        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        addonEvents = events;
        try
        {
            events.Register(AddonEvent.PostSetup, ADDON_NAME, OnAddonEvent);
            events.Register(AddonEvent.PostUpdate, ADDON_NAME, OnAddonEvent);
            events.Register(AddonEvent.PreFinalize, ADDON_NAME, OnAddonEvent);

            if (AddonHelper.TryGetByName<AtkUnitBase>(ADDON_NAME, out var addon))
            {
                TrySwitch(addon);
                addonAddress = (nint)addon;
                addonVisible = addon->IsVisible;
            }
        }
        catch
        {
            events.Dispose();
            addonEvents = null;
            ClearState();
            throw;
        }
    }

    protected override void OnStop()
    {
        addonEvents = null;
        ClearState();
    }

    private void OnAddonEvent(AddonEvent eventType, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (eventType == AddonEvent.PreFinalize)
        {
            ClearState();
            return;
        }

        if (addon == null)
        {
            return;
        }

        if (!addon->IsVisible)
        {
            addonVisible = false;
            switchAttempted = false;
            return;
        }

        if (addonAddress != (nint)addon || !addonVisible)
        {
            addonAddress = (nint)addon;
            switchAttempted = false;
        }

        addonVisible = true;

        if (!switchAttempted)
        {
            TrySwitch(addon);
        }
    }

    private void TrySwitch(AtkUnitBase* addon)
    {
        var node = addon->GetNodeById(40);
        var imageNode = node == null ? null : node->GetAsAtkImageNode();
        if (imageNode == null ||
            imageNode->PartsList == null ||
            imageNode->PartsList->Parts == null)
        {
            return;
        }

        var asset = imageNode->PartsList->Parts[imageNode->PartId].UldAsset;
        var resource = asset == null ? null : asset->AtkTexture.Resource;
        var iconID = resource == null ? 0u : resource->IconId;
        if (iconID is < CLASS_JOB_ICON_BASE or >= CLASS_JOB_ICON_LIMIT)
        {
            return;
        }

        switchAttempted = true;
        var classJobID = iconID - CLASS_JOB_ICON_BASE;
        if (LocalPlayerState.ClassJob == classJobID ||
            LocalPlayerState.TryFindClassJobGearset(classJobID, out var gearsetID) &&
            LocalPlayerState.SwitchGearset(gearsetID))
        {
            return;
        }
    }

    private void ClearState()
    {
        addonAddress = nint.Zero;
        switchAttempted = false;
        addonVisible = false;
    }

    #region 常量

    private const string ADDON_NAME = "ContentsFinderConfirm";

    private const uint CLASS_JOB_ICON_BASE = 62100;

    private const uint CLASS_JOB_ICON_LIMIT = 62200;

    #endregion
}
