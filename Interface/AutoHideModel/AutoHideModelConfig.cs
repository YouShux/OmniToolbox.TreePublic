using System.Diagnostics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data.Icons;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;
using ClientObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using ModuleBase = OmniToolbox.Common.Module.Abstractions.ModuleBase;

namespace OmniToolbox.TreePublic;

public sealed class AutoHideModelConfig
{
    public AutoHideUnitConfig Players { get; set; } = new()
    {
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true,
        ShowDead = true
    };

    public AutoHideUnitConfig Pets { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public AutoHideUnitConfig Beasts { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public AutoHideUnitConfig Chocobos { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public AutoHideUnitConfig Minions { get; set; } = new()
    {
        HideAll = true,
        ShowParty = true,
        ShowFriends = true,
        ShowFreeCompany = true
    };

    public bool IncludeSelf { get; set; }

    public bool HideGroundHealingEffects { get; set; } = true;

    public bool ShowTargetOfTarget { get; set; } = true;

    public bool ReduceOnScreenPlayers { get; set; } = true;

    public bool HideUnimportantNpcs { get; set; } = true;

    public bool HideSmallHouses { get; set; }

    public bool HideMediumHouses { get; set; }

    public bool HideLargeHouses { get; set; }

    public bool HideYardObjects { get; set; } = true;
}