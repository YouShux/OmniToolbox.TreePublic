using System.Globalization;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.TreeHouse;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed class FloatingInfoOverlayConfig
{
    public float Opacity { get; set; } = 0.85f;

    public float Range { get; set; } = 50f;

    public int MaxObjects { get; set; } = 20;

    public float MergeDistance { get; set; } = 2f;

    public float Scale { get; set; } = 1f;

    public bool OnlyCasting { get; set; }

    public bool ShowPlayers { get; set; } = true;

    public bool ShowLocalPlayer { get; set; }

    public bool ShowBattleNpcs { get; set; } = true;

    public bool ShowEventNpcs { get; set; } = true;

    public bool ShowEventObjects { get; set; } = true;

    public bool ShowCompanions { get; set; } = true;

    public bool ShowNonEntityTargets { get; set; }

    public bool ShowEntityID { get; set; } = true;

    public bool ShowDataID { get; set; } = true;

    public bool ShowDecimalID { get; set; }

    public bool ShowHexID { get; set; }

    public bool ShowDistance { get; set; } = true;

    public bool ShowPosition { get; set; } = true;

    public bool ShowRotation { get; set; } = true;

    public bool ShowHealth { get; set; } = true;

    public bool ShowMana { get; set; } = true;

    public bool ShowMarker { get; set; } = true;

    public bool ShowCastInfo { get; set; } = true;

    public bool ShowStatusList { get; set; } = true;

    public bool EnableDataIDFilter { get; set; }

    public bool UseDataIDWhitelist { get; set; } = true;

    public List<uint> FilterDataIds { get; set; } = [];
}