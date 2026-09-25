using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe class DutyReadyClassSwitching : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DutyReadyClassSwitchingTitle"),
        Description = OmniLoc.Get("DutyReadyClassSwitchingDescription"),
        Category = ModuleCategory.Combat
    };

    private const string ADDON_NAME = "ContentsFinderConfirm";
    private const uint CLASS_JOB_ICON_BASE = 62100;
    private const uint CLASS_JOB_ICON_LIMIT = 62200;

    private AddonEventRegistry? addonEvents;
    private nint addonAddress;
    private bool switchAttempted;
    private bool addonVisible;

    protected override void OnEnable()
    {
        var events = new AddonEventRegistry(DalamudServices.AddonLifecycle);
        try
        {
            events.Register(AddonEvent.PostSetup, ADDON_NAME, OnAddonEvent);
            events.Register(AddonEvent.PostUpdate, ADDON_NAME, OnAddonEvent);
            events.Register(AddonEvent.PreFinalize, ADDON_NAME, OnAddonEvent);
            addonEvents = events;

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

    protected override void OnDisable()
    {
        addonEvents?.Dispose();
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

        OmniNotifier.Chat(OmniLoc.Get("Feature.DutyReadyClassSwitching.NoGearset"));
    }

    private void ClearState()
    {
        addonAddress = nint.Zero;
        switchAttempted = false;
        addonVisible = false;
    }
}
