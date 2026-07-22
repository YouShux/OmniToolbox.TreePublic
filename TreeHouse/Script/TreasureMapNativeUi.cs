using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;

namespace OmniToolbox.TreeHouse.Script;

internal static unsafe class TreasureMapNativeUi
{
    private const uint DecipherGeneralActionId = 19;

    public static bool TryResolveMap(uint itemId, out TreasureMapDefinition map)
    {
        foreach (var row in LuminaGetter.Get<TreasureHuntRank>())
        {
            if (row.ItemName.RowId != itemId)
            {
                continue;
            }

            map = new(itemId, row.RowId, LuminaWrapper.GetItemName(itemId));
            return map.Name.Length != 0;
        }

        map = default;
        return false;
    }

    public static bool TryGetCurrentMap(out uint rankId, out uint subRowId)
    {
        var manager = EventItemManager.Instance();
        if (manager == null)
        {
            rankId = 0;
            subRowId = 0;
            return false;
        }

        rankId = manager->GetTreasureHuntRank();
        subRowId = manager->GetTreasureSpotSubKey();
        return rankId != 0;
    }

    public static bool TryResolveLocation(uint rankId, uint subRowId, out TreasureMapLocation location)
    {
        if (!LuminaGetter.TryGetSubRow<TreasureSpot>(rankId, out var spot, (int)subRowId) ||
            spot.Location.RowId == 0)
        {
            location = default;
            return false;
        }

        var map = spot.Location.Value.Map.Value;
        if (map.RowId == 0 || map.TerritoryType.RowId == 0)
        {
            location = default;
            return false;
        }

        location = new(
            map.TerritoryType.RowId,
            map.RowId,
            new Vector3(spot.Location.Value.X, 0f, spot.Location.Value.Z));
        return true;
    }

    public static bool TryUseDecipher()
    {
        var manager = ActionManager.Instance();
        return manager != null &&
               manager->GetActionStatus(ActionType.GeneralAction, DecipherGeneralActionId) == 0 &&
               manager->UseAction(ActionType.GeneralAction, DecipherGeneralActionId);
    }

    public static bool TrySelectMap(string name) =>
        AddonSelectIconStringEvent.Select(name) || AddonSelectStringEvent.Select(name);

    public static bool TryConfirmDecipher(string name) => AddonSelectYesnoEvent.ClickYes(name);

    public static bool IsDecipherConfirmationVisible() =>
        AddonHelper.TryGetByName("SelectYesno", out AtkUnitBase* addon) &&
        addon->IsVisible &&
        addon->IsAddonAndNodesReady();

    public static void OpenMap(TreasureMapLocation location) =>
        DService.Instance().GameGUI.OpenMapWithMapLink(
            location.TerritoryId,
            location.MapId,
            location.Position);
}
