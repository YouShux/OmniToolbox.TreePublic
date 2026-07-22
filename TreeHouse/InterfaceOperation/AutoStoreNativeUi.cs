using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmniToolbox.Host;
using OmenTools;
using OmenTools.Dalamud.Services.ObjectTable.Abstractions.ObjectKinds;
using OmenTools.Extensions;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Helpers;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal enum AutoStorePreparation
{
    Ready,
    Waiting,
    Unavailable
}

internal static unsafe class AutoStoreNativeUi
{
    private const float InteractionDistance = 2.1f;
    private const float GeneralInteractionDistance = 4f;
    private const uint SummoningBellDataId = 2_000_401;
    private const uint InnSummoningBellDataId = 2_000_403;

    private static readonly string[] FreeCompanyChestNames =
        ["部队储物柜", "部隊儲物櫃", "部队储物箱", "部隊儲物箱", "Company Chest", "カンパニーチェスト"];
    private static readonly string[] GlamourDresserNames =
        ["投影台", "投影臺", "Glamour Dresser", "ミラージュドレッサー"];
    private static readonly string[] ArmoireNames =
        ["收藏柜", "收藏櫃", "Armoire", "愛蔵品キャビネット"];
    private static readonly string[] ArmoireStoreEntries =
        ["放入道具", "Entrust an Item", "アイテムを預ける"];
    private static readonly string[] ArmoireWithdrawEntries =
        ["取出道具", "Withdraw an Item", "アイテムを取り出す"];
    private static readonly string[] RetainerInventoryEntries =
        ["道具管理", "Entrust or withdraw items", "アイテムの受け渡し"];
    private static readonly string[] RetainerInventoryAddons =
        ["RetainerItemTransferList", "RetainerItemList", "RetainerInventory", "InventoryRetainerLarge"];

    public static AutoStorePreparation Prepare(
        AutoStoreDestinationKind facility,
        bool isStore,
        int retainerIndex,
        bool canInteract,
        out bool openedByCall)
    {
        openedByCall = false;
        return facility switch
        {
            AutoStoreDestinationKind.Saddlebag => PrepareSaddlebag(canInteract, out openedByCall),
            AutoStoreDestinationKind.FreeCompanyChest => PrepareFreeCompanyChest(canInteract, out openedByCall),
            AutoStoreDestinationKind.Retainer => PrepareRetainer(retainerIndex, canInteract, out openedByCall),
            AutoStoreDestinationKind.GlamourDresser => PrepareGlamourDresser(canInteract, out openedByCall),
            AutoStoreDestinationKind.Armoire => PrepareArmoire(isStore, canInteract, out openedByCall),
            _ => AutoStorePreparation.Unavailable
        };
    }

    public static void Close(AutoStoreDestinationKind facility)
    {
        switch (facility)
        {
            case AutoStoreDestinationKind.Saddlebag:
                CloseAddon("InventoryBuddy");
                break;
            case AutoStoreDestinationKind.FreeCompanyChest:
                CloseAddon("FreeCompanyChest");
                break;
            case AutoStoreDestinationKind.Retainer:
                CloseRetainer();
                break;
            case AutoStoreDestinationKind.GlamourDresser:
                CloseAddon("MiragePrismPrismBoxFilter");
                CloseAddon("MiragePrismPrismBoxCrystallize");
                CloseAddon("MiragePrismPrismBox");
                break;
            case AutoStoreDestinationKind.Armoire:
                CloseArmoire();
                break;
        }
    }

    public static void CloseArmoire()
    {
        CloseAddon("CabinetWithdraw");
        CloseAddon("Cabinet");
    }

    private static AutoStorePreparation PrepareSaddlebag(bool canInteract, out bool openedByCall)
    {
        openedByCall = false;
        if (DService.Instance().Condition[ConditionFlag.BoundByDuty])
        {
            return AutoStorePreparation.Unavailable;
        }

        var agent = GetSaddlebagAgent();
        if (agent is not null && agent->IsAddonShown())
        {
            var manager = InventoryManager.Instance();
            var container = manager is null
                ? null
                : manager->GetInventoryContainer(InventoryType.SaddleBag1);
            return container is not null && container->IsLoaded
                ? AutoStorePreparation.Ready
                : AutoStorePreparation.Waiting;
        }

        if (!canInteract || agent is null)
        {
            return AutoStorePreparation.Waiting;
        }

        agent->Show();
        openedByCall = true;
        return AutoStorePreparation.Waiting;
    }

    public static bool IsFreeCompanyChestReady()
    {
        var agent = AgentFreeCompanyChest.Instance();
        var manager = InventoryManager.Instance();
        var container = manager is null
            ? null
            : manager->GetInventoryContainer(InventoryType.FreeCompanyPage1);
        return agent is not null &&
               agent->IsAgentActive() &&
               IsAddonVisible("FreeCompanyChest") &&
               container is not null &&
               container->IsLoaded;
    }

    public static bool IsRetainerReady(int retainerIndex)
    {
        var retainerManager = RetainerManager.Instance();
        var activeRetainer = retainerManager is null ? null : retainerManager->GetActiveRetainer();
        if (!DService.Instance().Condition[ConditionFlag.OccupiedSummoningBell] ||
            !IsAnyAddonVisible(RetainerInventoryAddons) ||
            activeRetainer is null ||
            activeRetainer->RetainerId == 0)
        {
            return false;
        }

        if (retainerIndex >= 0)
        {
            var requestedRetainer = retainerManager->GetRetainerBySortedIndex((uint)retainerIndex);
            if (requestedRetainer is null || requestedRetainer->RetainerId != activeRetainer->RetainerId)
            {
                return false;
            }
        }

        var manager = InventoryManager.Instance();
        var container = manager is null
            ? null
            : manager->GetInventoryContainer(InventoryType.RetainerPage1);
        return container is not null && container->IsLoaded;
    }

    public static bool TryGetRetainerIndex(ulong retainerId, out int index)
    {
        var manager = RetainerManager.Instance();
        if (manager is not null)
        {
            for (var current = 0U; current < manager->GetRetainerCount(); current++)
            {
                var retainer = manager->GetRetainerBySortedIndex(current);
                if (retainer is not null && retainer->RetainerId == retainerId)
                {
                    index = (int)current;
                    return true;
                }
            }
        }

        index = -1;
        return false;
    }

    public static bool IsRetainerIndexUnavailable(int retainerIndex)
    {
        var manager = RetainerManager.Instance();
        return manager is not null &&
               manager->IsReady &&
               (uint)retainerIndex >= manager->GetRetainerCount();
    }

    private static AutoStorePreparation PrepareFreeCompanyChest(bool canInteract, out bool openedByCall)
    {
        openedByCall = false;
        if (IsFreeCompanyChestReady())
        {
            return AutoStorePreparation.Ready;
        }

        if (IsAddonVisible("FreeCompanyChest") || !canInteract)
        {
            return AutoStorePreparation.Waiting;
        }

        openedByCall = TryInteractNearest(AutoStoreDestinationKind.FreeCompanyChest, GeneralInteractionDistance);
        return openedByCall
            ? AutoStorePreparation.Waiting
            : AutoStorePreparation.Unavailable;
    }

    private static AutoStorePreparation PrepareRetainer(
        int retainerIndex,
        bool canInteract,
        out bool openedByCall)
    {
        openedByCall = false;
        if (IsRetainerReady(retainerIndex))
        {
            return AutoStorePreparation.Ready;
        }

        if (IsAddonVisible("SelectString") || IsAddonVisible("SelectIconString"))
        {
            if (canInteract &&
                !AddonSelectStringEvent.Select(RetainerInventoryEntries))
            {
                AddonSelectIconStringEvent.Select(RetainerInventoryEntries);
            }

            return AutoStorePreparation.Waiting;
        }

        if (AddonHelper.TryGetByName("RetainerList", out AtkUnitBase* retainerList) &&
            retainerList->IsAddonAndNodesReady())
        {
            if (canInteract)
            {
                var selectionIndex = retainerIndex < 0 ? 0 : retainerIndex;
                var manager = RetainerManager.Instance();
                if (manager is null || (uint)selectionIndex >= manager->GetRetainerCount())
                {
                    return AutoStorePreparation.Unavailable;
                }

                retainerList->Callback(2, selectionIndex, 0, 0);
            }

            return AutoStorePreparation.Waiting;
        }

        if (DService.Instance().Condition[ConditionFlag.OccupiedSummoningBell] || !canInteract)
        {
            return AutoStorePreparation.Waiting;
        }

        openedByCall = TryInteractNearest(AutoStoreDestinationKind.Retainer, GeneralInteractionDistance);
        return openedByCall
            ? AutoStorePreparation.Waiting
            : AutoStorePreparation.Unavailable;
    }

    private static AutoStorePreparation PrepareGlamourDresser(bool canInteract, out bool openedByCall)
    {
        openedByCall = false;
        var manager = MirageManager.Instance();
        if (manager is not null && manager->PrismBoxLoaded)
        {
            return AutoStorePreparation.Ready;
        }

        if (IsAddonVisible("MiragePrismPrismBox") || !canInteract)
        {
            return AutoStorePreparation.Waiting;
        }

        openedByCall = TryInteractNearest(AutoStoreDestinationKind.GlamourDresser);
        return openedByCall
            ? AutoStorePreparation.Waiting
            : AutoStorePreparation.Unavailable;
    }

    private static AutoStorePreparation PrepareArmoire(
        bool isStore,
        bool canInteract,
        out bool openedByCall)
    {
        openedByCall = false;
        var uiState = UIState.Instance();
        if (uiState is not null &&
            uiState->Cabinet.IsCabinetLoaded() &&
            (IsAddonVisible("Cabinet") || IsAddonVisible("CabinetWithdraw")))
        {
            return AutoStorePreparation.Ready;
        }

        if (IsAddonVisible("SelectString"))
        {
            if (canInteract)
            {
                AddonSelectStringEvent.Select(isStore ? ArmoireStoreEntries : ArmoireWithdrawEntries);
            }

            return AutoStorePreparation.Waiting;
        }

        if (IsAddonVisible("Cabinet") || IsAddonVisible("CabinetWithdraw") || !canInteract)
        {
            return AutoStorePreparation.Waiting;
        }

        openedByCall = TryInteractNearest(AutoStoreDestinationKind.Armoire);
        return openedByCall
            ? AutoStorePreparation.Waiting
            : AutoStorePreparation.Unavailable;
    }

    internal static bool IsFacilityObject(AutoStoreDestinationKind facility, IGameObject gameObject)
    {
        if (!gameObject.IsValid() ||
            gameObject.ObjectKind is not (ObjectKind.EventObj or ObjectKind.HousingEventObject or ObjectKind.EventNpc))
        {
            return false;
        }

        return facility switch
        {
            AutoStoreDestinationKind.FreeCompanyChest => ContainsName(gameObject.Name, FreeCompanyChestNames),
            AutoStoreDestinationKind.Retainer =>
                gameObject.DataID is SummoningBellDataId or InnSummoningBellDataId,
            AutoStoreDestinationKind.GlamourDresser => ContainsName(gameObject.Name, GlamourDresserNames),
            AutoStoreDestinationKind.Armoire => ContainsName(gameObject.Name, ArmoireNames),
            _ => false
        };
    }

    private static bool TryInteractNearest(
        AutoStoreDestinationKind facility,
        float maxDistance = InteractionDistance)
    {
        var player = DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            return false;
        }

        IGameObject? target = null;
        var bestDistanceSquared = maxDistance * maxDistance;
        foreach (var gameObject in DService.Instance().ObjectTable)
        {
            if (!IsFacilityObject(facility, gameObject))
            {
                continue;
            }

            var dx = player.Position.X - gameObject.Position.X;
            var dz = player.Position.Z - gameObject.Position.Z;
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            target = gameObject;
        }

        var targetSystem = TargetSystem.Instance();
        var targetObject = target is null ? null : target.ToStruct();
        if (targetSystem is null || targetObject is null)
        {
            return false;
        }

        targetSystem->Target = targetObject;
        if (targetSystem->InteractWithObject(targetObject, false) == 0)
        {
            return false;
        }

        if (target!.ObjectKind == ObjectKind.EventObj)
        {
            targetSystem->OpenObjectInteraction(targetObject);
        }

        return true;
    }

    private static bool ContainsName(string text, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (text.Contains(names[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAddonVisible(string addonName) =>
        AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon) && addon->IsVisible;

    private static bool IsAnyAddonVisible(IReadOnlyList<string> addonNames)
    {
        for (var index = 0; index < addonNames.Count; index++)
        {
            if (IsAddonVisible(addonNames[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static void CloseRetainer()
    {
        var module = AgentModule.Instance();
        var agent = module is null ? null : module->GetAgentByInternalId(AgentId.Retainer);
        if (agent is not null && agent->IsAgentActive())
        {
            var unitManager = RaptureAtkUnitManager.Instance();
            var addon = unitManager is null
                ? null
                : unitManager->GetAddonById((ushort)agent->GetAddonId());
            if (addon is not null)
            {
                addon->Close(true);
            }

            var inventoryAgent = module->GetAgentByInternalId(AgentId.Inventory);
            var inventoryAddon = inventoryAgent is null || unitManager is null
                ? null
                : unitManager->GetAddonById((ushort)inventoryAgent->GetAddonId());
            if (inventoryAddon is not null)
            {
                inventoryAddon->Callback(-1);
            }

            AgentId.Retainer.SendEvent(0, -1);
        }

        if (IsAddonVisible("SelectString"))
        {
            AddonSelectStringEvent.Select(-1);
        }

        if (IsAddonVisible("SelectIconString"))
        {
            AddonSelectIconStringEvent.Select(-1);
        }

        CloseAddon("RetainerList");
    }

    private static void CloseAddon(string addonName)
    {
        if (!AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon) || !addon->IsVisible)
        {
            return;
        }

        try
        {
            addon->Close(true);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Failed to close addon {AddonName}.", addonName);
        }
    }

    private static AgentInterface* GetSaddlebagAgent()
    {
        var module = AgentModule.Instance();
        return module is null
            ? null
            : module->GetAgentByInternalId(AgentId.InventoryBuddy);
    }
}
