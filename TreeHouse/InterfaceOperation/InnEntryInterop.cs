using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Packets.Upstream;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using IAetheryteEntry = OmenTools.Dalamud.Services.AetheryteList.Abstractions.IAetheryteEntry;
using IGameObject = OmenTools.Dalamud.Services.ObjectTable.Abstractions.ObjectKinds.IGameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static unsafe class InnEntryInterop
{
    public const uint GridaniaTerritoryId = 132;
    private const uint GridaniaInnEventId = 131080;
    private static readonly Vector3 GridaniaInnEntrance = new(26f, -38f, 101f);
    private static readonly string[] InnFacilityNames =
    [
        "投影台",
        "投影臺",
        "Glamour Dresser",
        "ミラージュドレッサー",
        "收藏柜",
        "收藏櫃",
        "Armoire",
        "愛蔵品キャビネット"
    ];

    public static bool IsInnRoomLikeArea()
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || services.ObjectTable.LocalPlayer is null)
        {
            return false;
        }

        if (GameState.TerritoryIntendedUse is TerritoryIntendedUse.Inn or TerritoryIntendedUse.GrandCompanyBarracks ||
            IsAddonVisible("MiragePrismPrismBox") ||
            IsAddonVisible("Cabinet"))
        {
            return true;
        }

        foreach (var gameObject in services.ObjectTable)
        {
            if (!gameObject.IsValid() ||
                gameObject.ObjectKind is not (ObjectKind.EventObj or ObjectKind.HousingEventObject or ObjectKind.EventNpc))
            {
                continue;
            }

            for (var index = 0; index < InnFacilityNames.Length; index++)
            {
                if (gameObject.Name.Contains(InnFacilityNames[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryTeleportToGridania()
    {
        var aetherytes = DService.Instance().AetheryteList;
        IAetheryteEntry? target = null;
        var count = aetherytes.Length;
        for (var index = 0; index < count; index++)
        {
            var entry = aetherytes[index];
            if (entry is null || entry.TerritoryID != GridaniaTerritoryId)
            {
                continue;
            }

            if (target is null ||
                entry.GilCost < target.GilCost ||
                entry.GilCost == target.GilCost && entry.SubIndex < target.SubIndex ||
                entry.GilCost == target.GilCost && entry.SubIndex == target.SubIndex && entry.AetheryteID < target.AetheryteID)
            {
                target = entry;
            }
        }

        var telepo = Telepo.Instance();
        if (target is null || telepo == null)
        {
            return false;
        }

        telepo->Teleport(target.AetheryteID, target.SubIndex);
        return true;
    }

    public static bool SendToEntrance()
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return false;
        }

        PositionUpdatePacket.Send(localPlayer.Rotation, GridaniaInnEntrance);
        return true;
    }

    public static bool IsInnEventNearby()
    {
        var framework = EventFramework.Instance();
        return framework != null && framework->IsEventIDNearby((EventId)GridaniaInnEventId);
    }

    public static bool TryStartInnEvent()
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return false;
        }

        new EventStartPackt(
            TryFindInnEventObject(out var gameObject) ? gameObject.GameObjectID : localPlayer.EntityID,
            GridaniaInnEventId).Send();
        return true;
    }

    private static bool TryFindInnEventObject(out IGameObject gameObject)
    {
        foreach (var candidate in DService.Instance().ObjectTable)
        {
            if (!candidate.IsValid() || candidate.Address == nint.Zero)
            {
                continue;
            }

            var nativeObject = (CSGameObject*)candidate.Address;
            if (nativeObject != null && nativeObject->EventId.Id == GridaniaInnEventId)
            {
                gameObject = candidate;
                return true;
            }
        }

        gameObject = null!;
        return false;
    }

    private static bool IsAddonVisible(string addonName) =>
        AddonHelper.TryGetByName(addonName, out AtkUnitBase* addon) && addon->IsAddonAndNodesReady();
}
