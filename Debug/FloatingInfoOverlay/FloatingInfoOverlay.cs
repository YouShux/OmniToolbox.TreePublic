using System.Globalization;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.TreeHouse;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
internal sealed partial class FloatingInfoOverlay
(
    FloatingInfoOverlayConfig config,
    NonEntityTargetVisibility targetVisibility
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Debug,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "FloatingInfoOverlayTitle",
            ["Description"] = "FloatingInfoOverlayDescription",
            ["Feature.FloatingInfoOverlay.Action"] = "Feature.FloatingInfoOverlay.Action",
            ["Feature.FloatingInfoOverlay.Add"] = "Feature.FloatingInfoOverlay.Add",
            ["Feature.FloatingInfoOverlay.CastType.Charge"] = "Feature.FloatingInfoOverlay.CastType.Charge",
            ["Feature.FloatingInfoOverlay.CastType.Circle"] = "Feature.FloatingInfoOverlay.CastType.Circle",
            ["Feature.FloatingInfoOverlay.CastType.Cone"] = "Feature.FloatingInfoOverlay.CastType.Cone",
            ["Feature.FloatingInfoOverlay.CastType.Line"] = "Feature.FloatingInfoOverlay.CastType.Line",
            ["Feature.FloatingInfoOverlay.CastType.None"] = "Feature.FloatingInfoOverlay.CastType.None",
            ["Feature.FloatingInfoOverlay.CastType.Rectangle"] = "Feature.FloatingInfoOverlay.CastType.Rectangle",
            ["Feature.FloatingInfoOverlay.DecimalId"] = "Feature.FloatingInfoOverlay.DecimalId",
            ["Feature.FloatingInfoOverlay.DisplayOptions"] = "Feature.FloatingInfoOverlay.DisplayOptions",
            ["Feature.FloatingInfoOverlay.EnableDataIdFilter"] = "Feature.FloatingInfoOverlay.EnableDataIdFilter",
            ["Feature.FloatingInfoOverlay.FilterInput"] = "Feature.FloatingInfoOverlay.FilterInput",
            ["Feature.FloatingInfoOverlay.General"] = "Feature.FloatingInfoOverlay.General",
            ["Feature.FloatingInfoOverlay.Hex"] = "Feature.FloatingInfoOverlay.Hex",
            ["Feature.FloatingInfoOverlay.Line.CastAction"] = "Feature.FloatingInfoOverlay.Line.CastAction",
            ["Feature.FloatingInfoOverlay.Line.CastActionWithoutName"] = "Feature.FloatingInfoOverlay.Line.CastActionWithoutName",
            ["Feature.FloatingInfoOverlay.Line.CastRange"] = "Feature.FloatingInfoOverlay.Line.CastRange",
            ["Feature.FloatingInfoOverlay.Line.CastRotation"] = "Feature.FloatingInfoOverlay.Line.CastRotation",
            ["Feature.FloatingInfoOverlay.Line.CastShape"] = "Feature.FloatingInfoOverlay.Line.CastShape",
            ["Feature.FloatingInfoOverlay.Line.CastTime"] = "Feature.FloatingInfoOverlay.Line.CastTime",
            ["Feature.FloatingInfoOverlay.Line.Casting"] = "Feature.FloatingInfoOverlay.Line.Casting",
            ["Feature.FloatingInfoOverlay.Line.DataId"] = "Feature.FloatingInfoOverlay.Line.DataId",
            ["Feature.FloatingInfoOverlay.Line.DataIdHex"] = "Feature.FloatingInfoOverlay.Line.DataIdHex",
            ["Feature.FloatingInfoOverlay.Line.Distance"] = "Feature.FloatingInfoOverlay.Line.Distance",
            ["Feature.FloatingInfoOverlay.Line.EntityId"] = "Feature.FloatingInfoOverlay.Line.EntityId",
            ["Feature.FloatingInfoOverlay.Line.EntityIdHex"] = "Feature.FloatingInfoOverlay.Line.EntityIdHex",
            ["Feature.FloatingInfoOverlay.Line.Health"] = "Feature.FloatingInfoOverlay.Line.Health",
            ["Feature.FloatingInfoOverlay.Line.Mana"] = "Feature.FloatingInfoOverlay.Line.Mana",
            ["Feature.FloatingInfoOverlay.Line.Marker"] = "Feature.FloatingInfoOverlay.Line.Marker",
            ["Feature.FloatingInfoOverlay.Line.Page"] = "Feature.FloatingInfoOverlay.Line.Page",
            ["Feature.FloatingInfoOverlay.Line.Position"] = "Feature.FloatingInfoOverlay.Line.Position",
            ["Feature.FloatingInfoOverlay.Line.Rotation"] = "Feature.FloatingInfoOverlay.Line.Rotation",
            ["Feature.FloatingInfoOverlay.Line.Status"] = "Feature.FloatingInfoOverlay.Line.Status",
            ["Feature.FloatingInfoOverlay.Line.StatusCount"] = "Feature.FloatingInfoOverlay.Line.StatusCount",
            ["Feature.FloatingInfoOverlay.Line.StatusParam"] = "Feature.FloatingInfoOverlay.Line.StatusParam",
            ["Feature.FloatingInfoOverlay.Line.StatusWithoutName"] = "Feature.FloatingInfoOverlay.Line.StatusWithoutName",
            ["Feature.FloatingInfoOverlay.Line.Type"] = "Feature.FloatingInfoOverlay.Line.Type",
            ["Feature.FloatingInfoOverlay.Marker.Attack1"] = "Feature.FloatingInfoOverlay.Marker.Attack1",
            ["Feature.FloatingInfoOverlay.Marker.Attack2"] = "Feature.FloatingInfoOverlay.Marker.Attack2",
            ["Feature.FloatingInfoOverlay.Marker.Attack3"] = "Feature.FloatingInfoOverlay.Marker.Attack3",
            ["Feature.FloatingInfoOverlay.Marker.Attack4"] = "Feature.FloatingInfoOverlay.Marker.Attack4",
            ["Feature.FloatingInfoOverlay.Marker.Attack5"] = "Feature.FloatingInfoOverlay.Marker.Attack5",
            ["Feature.FloatingInfoOverlay.Marker.Attack6"] = "Feature.FloatingInfoOverlay.Marker.Attack6",
            ["Feature.FloatingInfoOverlay.Marker.Attack7"] = "Feature.FloatingInfoOverlay.Marker.Attack7",
            ["Feature.FloatingInfoOverlay.Marker.Attack8"] = "Feature.FloatingInfoOverlay.Marker.Attack8",
            ["Feature.FloatingInfoOverlay.Marker.Bind1"] = "Feature.FloatingInfoOverlay.Marker.Bind1",
            ["Feature.FloatingInfoOverlay.Marker.Bind2"] = "Feature.FloatingInfoOverlay.Marker.Bind2",
            ["Feature.FloatingInfoOverlay.Marker.Bind3"] = "Feature.FloatingInfoOverlay.Marker.Bind3",
            ["Feature.FloatingInfoOverlay.Marker.Circle"] = "Feature.FloatingInfoOverlay.Marker.Circle",
            ["Feature.FloatingInfoOverlay.Marker.Cross"] = "Feature.FloatingInfoOverlay.Marker.Cross",
            ["Feature.FloatingInfoOverlay.Marker.Ignore1"] = "Feature.FloatingInfoOverlay.Marker.Ignore1",
            ["Feature.FloatingInfoOverlay.Marker.Ignore2"] = "Feature.FloatingInfoOverlay.Marker.Ignore2",
            ["Feature.FloatingInfoOverlay.Marker.Square"] = "Feature.FloatingInfoOverlay.Marker.Square",
            ["Feature.FloatingInfoOverlay.Marker.Triangle"] = "Feature.FloatingInfoOverlay.Marker.Triangle",
            ["Feature.FloatingInfoOverlay.MaxObjects"] = "Feature.FloatingInfoOverlay.MaxObjects",
            ["Feature.FloatingInfoOverlay.MergeDistance"] = "Feature.FloatingInfoOverlay.MergeDistance",
            ["Feature.FloatingInfoOverlay.MergeDistance.Help"] = "Feature.FloatingInfoOverlay.MergeDistance.Help",
            ["Feature.FloatingInfoOverlay.NonEntityTarget"] = "Feature.FloatingInfoOverlay.NonEntityTarget",
            ["Feature.FloatingInfoOverlay.ObjectKind.BattleNpc"] = "Feature.FloatingInfoOverlay.ObjectKind.BattleNpc",
            ["Feature.FloatingInfoOverlay.ObjectKind.Companion"] = "Feature.FloatingInfoOverlay.ObjectKind.Companion",
            ["Feature.FloatingInfoOverlay.ObjectKind.EventNpc"] = "Feature.FloatingInfoOverlay.ObjectKind.EventNpc",
            ["Feature.FloatingInfoOverlay.ObjectKind.EventObject"] = "Feature.FloatingInfoOverlay.ObjectKind.EventObject",
            ["Feature.FloatingInfoOverlay.ObjectKind.NonEntity"] = "Feature.FloatingInfoOverlay.ObjectKind.NonEntity",
            ["Feature.FloatingInfoOverlay.ObjectKind.Other"] = "Feature.FloatingInfoOverlay.ObjectKind.Other",
            ["Feature.FloatingInfoOverlay.ObjectKind.Player"] = "Feature.FloatingInfoOverlay.ObjectKind.Player",
            ["Feature.FloatingInfoOverlay.ObjectTypes"] = "Feature.FloatingInfoOverlay.ObjectTypes",
            ["Feature.FloatingInfoOverlay.OnlyCasting"] = "Feature.FloatingInfoOverlay.OnlyCasting",
            ["Feature.FloatingInfoOverlay.Opacity"] = "Feature.FloatingInfoOverlay.Opacity",
            ["Feature.FloatingInfoOverlay.Range"] = "Feature.FloatingInfoOverlay.Range",
            ["Feature.FloatingInfoOverlay.Remove"] = "Feature.FloatingInfoOverlay.Remove",
            ["Feature.FloatingInfoOverlay.Scale"] = "Feature.FloatingInfoOverlay.Scale",
            ["Feature.FloatingInfoOverlay.ShowBattleNpcs"] = "Feature.FloatingInfoOverlay.ShowBattleNpcs",
            ["Feature.FloatingInfoOverlay.ShowCastInfo"] = "Feature.FloatingInfoOverlay.ShowCastInfo",
            ["Feature.FloatingInfoOverlay.ShowCompanions"] = "Feature.FloatingInfoOverlay.ShowCompanions",
            ["Feature.FloatingInfoOverlay.ShowDataId"] = "Feature.FloatingInfoOverlay.ShowDataId",
            ["Feature.FloatingInfoOverlay.ShowDecimalId"] = "Feature.FloatingInfoOverlay.ShowDecimalId",
            ["Feature.FloatingInfoOverlay.ShowDistance"] = "Feature.FloatingInfoOverlay.ShowDistance",
            ["Feature.FloatingInfoOverlay.ShowEntityId"] = "Feature.FloatingInfoOverlay.ShowEntityId",
            ["Feature.FloatingInfoOverlay.ShowEventNpcs"] = "Feature.FloatingInfoOverlay.ShowEventNpcs",
            ["Feature.FloatingInfoOverlay.ShowEventObjects"] = "Feature.FloatingInfoOverlay.ShowEventObjects",
            ["Feature.FloatingInfoOverlay.ShowHealth"] = "Feature.FloatingInfoOverlay.ShowHealth",
            ["Feature.FloatingInfoOverlay.ShowHexId"] = "Feature.FloatingInfoOverlay.ShowHexId",
            ["Feature.FloatingInfoOverlay.ShowLocalPlayer"] = "Feature.FloatingInfoOverlay.ShowLocalPlayer",
            ["Feature.FloatingInfoOverlay.ShowMana"] = "Feature.FloatingInfoOverlay.ShowMana",
            ["Feature.FloatingInfoOverlay.ShowMarker"] = "Feature.FloatingInfoOverlay.ShowMarker",
            ["Feature.FloatingInfoOverlay.ShowMarker.Help"] = "Feature.FloatingInfoOverlay.ShowMarker.Help",
            ["Feature.FloatingInfoOverlay.ShowNonEntityTargets"] = "Feature.FloatingInfoOverlay.ShowNonEntityTargets",
            ["Feature.FloatingInfoOverlay.ShowPlayers"] = "Feature.FloatingInfoOverlay.ShowPlayers",
            ["Feature.FloatingInfoOverlay.ShowPosition"] = "Feature.FloatingInfoOverlay.ShowPosition",
            ["Feature.FloatingInfoOverlay.ShowRotation"] = "Feature.FloatingInfoOverlay.ShowRotation",
            ["Feature.FloatingInfoOverlay.ShowStatusList"] = "Feature.FloatingInfoOverlay.ShowStatusList",
            ["Feature.FloatingInfoOverlay.Whitelist"] = "Feature.FloatingInfoOverlay.Whitelist"
        });

    private readonly FloatingInfoOverlayPanel panel = new(config);

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        var state = new FloatingInfoOverlayState(config, targetVisibility);
        var nativeUI = new FloatingInfoOverlayNativeUI(config, state);
        lifetime.Add(state.Dispose);
        if (!FrameworkManager.Instance().Reg(state.Update))
        {
            throw new InvalidOperationException("Floating info overlay update registration failed.");
        }

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(state.Update));
        lifetime.OnStopping(() => DalamudServices.PluginInterface.UiBuilder.Draw -= nativeUI.Draw);
        DalamudServices.PluginInterface.UiBuilder.Draw += nativeUI.Draw;
        state.Update(DalamudServices.Framework);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => panel.Draw();
}
