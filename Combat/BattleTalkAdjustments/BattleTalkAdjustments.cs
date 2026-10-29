using System.Diagnostics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe class BattleTalkAdjustments : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "BattleTalkAdjustmentsTitle",
            ["Description"] = "BattleTalkAdjustmentsDescription",
            ["Feature.BattleTalkAdjustments.Offset"] = "Feature.BattleTalkAdjustments.Offset",
            ["Feature.BattleTalkAdjustments.PreviewSpeaker"] = "Feature.BattleTalkAdjustments.PreviewSpeaker",
            ["Feature.BattleTalkAdjustments.PreviewText"] = "Feature.BattleTalkAdjustments.PreviewText",
            ["Feature.BattleTalkAdjustments.Scale"] = "Feature.BattleTalkAdjustments.Scale",
            ["Feature.BattleTalkAdjustments.Territory.Blacklist"] = "Feature.BattleTalkAdjustments.Territory.Blacklist",
            ["Feature.BattleTalkAdjustments.Territory.Whitelist"] = "Feature.BattleTalkAdjustments.Territory.Whitelist",
            ["Feature.BattleTalkAdjustments.Territory.WorkMode"] = "Feature.BattleTalkAdjustments.Territory.WorkMode",
            ["Feature.BattleTalkAdjustments.Territory.WorkMode.Help"] = "Feature.BattleTalkAdjustments.Territory.WorkMode.Help"
        });

    private readonly BattleTalkAdjustmentsConfig config;
    private readonly Stopwatch                   previewTimer      = new();
    private readonly TerritorySelector           territorySelector = new("battleTalkAdjustmentsTerritory");
    private Vector2                              nativeScale       = Vector2.One;
    private bool                                 hasNativeScale;
    private bool                                 rootHidden;

    public BattleTalkAdjustments(BattleTalkAdjustmentsConfig config)
    {
        this.config  = config;
        config.Scale = Math.Clamp(config.Scale <= 0f ? 1f : config.Scale, 0.01f, 3f);
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreShow,
            ADDON_NAME,
            OnPreShow));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreUpdate,
            ADDON_NAME,
            OnLayoutUpdated));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostShow,
            ADDON_NAME,
            OnLayoutUpdated));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreDraw,
            ADDON_NAME,
            OnPreDraw));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostUpdate,
            ADDON_NAME,
            OnLayoutUpdated));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PostDraw,
            ADDON_NAME,
            OnPostDraw));
        lifetime.OnStopping(() => DalamudServices.AddonLifecycle.UnregisterListener(
            AddonEvent.PreFinalize,
            ADDON_NAME,
            OnPreFinalize));
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreShow, ADDON_NAME, OnPreShow);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreUpdate, ADDON_NAME, OnLayoutUpdated);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostShow, ADDON_NAME, OnLayoutUpdated);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, ADDON_NAME, OnPreDraw);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, ADDON_NAME, OnLayoutUpdated);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, ADDON_NAME, OnPostDraw);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, ADDON_NAME, OnPreFinalize);
        ApplyLayout(Addons.BattleTalk);
    }

    protected override void OnStop()
    {
        Restore(Addons.BattleTalk);
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var settingsChanged = false;
        var scaleLabel = OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Scale");
        var offsetLabel = OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Offset");
        var scaleSize = OmniControls.MeasureFloatInput(config.Scale, "%.2fx");
        var offsetSize = OmniControls.MeasureGroup([OmniControls.MeasureInput("-1500"), OmniControls.MeasureInput("-1500")]);
        var groupWidth = MathF.Max(ImGui.CalcTextSize(scaleLabel).X + scaleSize.X,
            ImGui.CalcTextSize(offsetLabel).X + offsetSize.X) + ImGui.GetStyle().ItemSpacing.X;
        using (var table = OmniControls.SettingsTable(
            "##battleTalkAdjustmentsSettings",
            [new(groupWidth, scaleSize.Y), new(groupWidth, offsetSize.Y)],
            ["##battleTalkAdjustmentsScaleColumn", "##battleTalkAdjustmentsOffsetColumn"],
            flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 2))
        {
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(scaleLabel);
            OmniControls.SameLineOrWrap(scaleSize.X);
            var scale = config.Scale;
            if (OmniControls.DragFloat(
                    "##battleTalkAdjustmentsScale",
                    ref scale,
                    0.05f,
                    0.01f,
                    3f,
                    "%.2fx",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X),
                    ImGuiSliderFlags.AlwaysClamp))
            {
                config.Scale    = scale;
                settingsChanged = true;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(offsetLabel);
            OmniControls.SameLineOrWrap(offsetSize.X);
            var offset = new Vector2(config.OffsetX, config.OffsetY);
            if (OmniControls.DragFloat2(
                    "##battleTalkAdjustmentsOffset",
                    ref offset,
                    1f,
                    -1500f,
                    1500f,
                    "%.0f",
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                config.OffsetX  = (int)MathF.Round(offset.X);
                config.OffsetY  = (int)MathF.Round(offset.Y);
                settingsChanged = true;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Territory.WorkMode"));
        ImGui.SameLine();
        if (OmniControls.RadioButton(
                $"{OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Territory.Blacklist")}##battleTalkAdjustmentsBlacklist",
                !config.UseWhitelist))
        {
            config.UseWhitelist = false;
            changed             = true;
        }

        ImGui.SameLine();
        if (OmniControls.RadioButton(
                $"{OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Territory.Whitelist")}##battleTalkAdjustmentsWhitelist",
                config.UseWhitelist))
        {
            config.UseWhitelist = true;
            changed             = true;
        }

        ImGui.SameLine();
        OmniControls.HelpIcon(OmniLoc.Get(this, "Feature.BattleTalkAdjustments.Territory.WorkMode.Help"));
        ImGui.Spacing();
        changed |= territorySelector.Draw(
            config.UseWhitelist ? config.WhitelistTerritoryIDs : config.BlacklistTerritoryIDs,
            string.Empty);

        if (settingsChanged && IsTerritoryAllowed(DService.Instance().ClientState.TerritoryType) &&
            (!previewTimer.IsRunning || previewTimer.ElapsedMilliseconds > 10000))
        {
            var battleTalk = Addons.BattleTalk;
            if (battleTalk == null || !battleTalk->IsVisible)
            {
                UIModule.Instance()->ShowBattleTalk(
                    OmniLoc.Get(this, "Feature.BattleTalkAdjustments.PreviewSpeaker"),
                    OmniLoc.Get(this, "Feature.BattleTalkAdjustments.PreviewText"),
                    10f,
                    1);
                previewTimer.Restart();
            }
        }

        return changed;
    }

    private void OnPreDraw
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible || addon->RootNode == null)
        {
            return;
        }

        RestoreVisibility(addon);
        if (!IsTerritoryAllowed(DService.Instance().ClientState.TerritoryType))
        {
            if (addon->RootNode->IsVisible())
            {
                addon->RootNode->ToggleVisibility(false);
                rootHidden = true;
            }

            return;
        }

        ApplyLayout(addon);
    }

    private void OnPreShow
    (
        AddonEvent type,
        AddonArgs  args
    ) => Restore((AtkUnitBase*)args.Addon.Address);

    private void OnLayoutUpdated
    (
        AddonEvent type,
        AddonArgs  args
    ) => ApplyLayout((AtkUnitBase*)args.Addon.Address);

    private void OnPostDraw
    (
        AddonEvent type,
        AddonArgs  args
    ) => RestoreVisibility((AtkUnitBase*)args.Addon.Address);

    private void OnPreFinalize
    (
        AddonEvent type,
        AddonArgs  args
    )
    {
        rootHidden     = false;
        hasNativeScale = false;
    }

    private bool IsTerritoryAllowed(uint territoryID) => config.UseWhitelist
        ? config.WhitelistTerritoryIDs.Contains(territoryID)
        : !config.BlacklistTerritoryIDs.Contains(territoryID);

    private void ApplyLayout(AtkUnitBase* addon)
    {
        if (addon == null || addon->RootNode == null)
            return;

        if (!hasNativeScale)
        {
            nativeScale    = new(addon->RootNode->ScaleX, addon->RootNode->ScaleY);
            hasNativeScale = true;
        }

        addon->RootNode->SetPositionFloat(addon->X + config.OffsetX, addon->Y + config.OffsetY);
        addon->RootNode->SetScale(nativeScale.X * config.Scale, nativeScale.Y * config.Scale);
    }

    private void RestoreVisibility(AtkUnitBase* addon)
    {
        if (!rootHidden)
            return;

        if (addon != null && addon->RootNode != null)
            addon->RootNode->ToggleVisibility(true);

        rootHidden = false;
    }

    private void Restore(AtkUnitBase* addon)
    {
        RestoreVisibility(addon);
        if (addon == null || addon->RootNode == null)
        {
            hasNativeScale = false;
            return;
        }

        if (!hasNativeScale)
            return;

        addon->RootNode->SetPositionFloat(addon->X, addon->Y);
        addon->RootNode->SetScale(nativeScale.X, nativeScale.Y);
        hasNativeScale = false;
    }

    #region 常量

    private const string ADDON_NAME = "_BattleTalk";

    #endregion
}
