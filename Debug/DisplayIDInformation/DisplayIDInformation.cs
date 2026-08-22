using Dalamud.Plugin.Services;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmniToolbox.Config;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class DisplayIDInformation(
    DisplayIDInformationConfig config,
    IconBrowser iconBrowser) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DisplayIdInformationTitle"),
        Description = OmniLoc.Get("DisplayIdInformationDescription"),
        Category = ModuleCategory.Debug,
        Commands =
        [
            new ModuleCommand("Feature.DisplayIdInformation.CommandDescription", "/omni 图标浏览器")
        ]
    };

    private FeatureLifetime? runtimeLifetime;
    private DisplayIDInformationNativeUI? nativeUI;
    private DisplayIDInformationCombat? combat;

    public override bool HasSettings => true;

    public override bool DrawSettings() => DisplayIDInformationPanel.Draw(config, iconBrowser);

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            nativeUI = new(config);
            lifetime.Add(nativeUI.Dispose);
            combat = new(config);
            lifetime.Add(combat.Dispose);
            if (!FrameworkManager.Instance().Reg(OnUpdate, 100))
            {
                throw new InvalidOperationException("Display ID information update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnUpdate));
            runtimeLifetime = lifetime;
            OnUpdate(DService.Instance().Framework);
        }
        catch
        {
            runtimeLifetime = null;
            nativeUI = null;
            combat = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            runtimeLifetime = null;
            nativeUI = null;
            combat = null;
        }
    }

    private void OnUpdate(IFramework _)
    {
        nativeUI?.UpdateDtr();
        combat?.Update();
    }
}

[Serializable]
public sealed class DisplayIDInformationConfig
{
    public bool DisplayItemID { get; set; } = true;
    public bool DisplayActionID { get; set; } = true;
    public bool DisplayActionIDResolved { get; set; } = true;
    public bool DisplayActionIDOriginal { get; set; } = true;
    public bool DisplayCastBarActionID { get; set; } = true;
    public bool DisplayFlyTextDamageActionID { get; set; } = true;
    public bool DisplayTargetID { get; set; } = true;
    public bool DisplayTargetIDBattleNPC { get; set; } = true;
    public bool DisplayTargetIDEventNPC { get; set; } = true;
    public bool DisplayTargetIDCompanion { get; set; } = true;
    public bool DisplayTargetIDOthers { get; set; } = true;
    public bool DisplayStatusID { get; set; } = true;
    public bool DisplayStatusIDAppendToName { get; set; } = true;
    public bool DisplayWeatherID { get; set; } = true;
    public bool DisplayZoneInfo { get; set; } = true;
    public bool DisplayIconID { get; set; }
}

internal static class DisplayIDInformationPanel
{
    public static bool Draw(DisplayIDInformationConfig config, IconBrowser iconBrowser)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##displayIdInformationSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##displayIdItem", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##displayIdWeather", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##displayIdZone", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##displayIdIcon", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Item", "item", config.DisplayItemID, out var displayItemId))
        {
            config.DisplayItemID = displayItemId;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Weather", "weather", config.DisplayWeatherID, out var displayWeatherId))
        {
            config.DisplayWeatherID = displayWeatherId;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Zone", "zone", config.DisplayZoneInfo, out var displayZoneInfo))
        {
            config.DisplayZoneInfo = displayZoneInfo;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Icon", "icon", config.DisplayIconID, out var displayIconID))
        {
            config.DisplayIconID = displayIconID;
            changed = true;
        }

        var buttonLabel = OmniLoc.Get("IconBrowser.Title");
        var buttonSize = OmniControls.CompactButtonSize(buttonLabel);
        if (ImGui.GetContentRegionAvail().X > buttonSize.X + ImGui.GetStyle().ItemSpacing.X)
        {
            ImGui.SameLine();
        }

        if (OmniControls.SmallButton(
                buttonLabel,
                false,
                new Vector2(MathF.Min(buttonSize.X, ImGui.GetContentRegionAvail().X), buttonSize.Y)))
        {
            iconBrowser.Toggle();
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.IconBrowser.Help"));
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawStatus(config);
        ImGui.TableNextColumn();
        changed |= DrawAction(config);
        ImGui.TableNextColumn();
        changed |= DrawTarget(config);
        return changed;
    }

    private static bool DrawStatus(DisplayIDInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Status", "status", config.DisplayStatusID, out var displayStatusId))
        {
            config.DisplayStatusID = displayStatusId;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Indent(OmniTheme.Scale(26f));
        using (ImRaii.Disabled(!config.DisplayStatusID))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "statusFlyText", config.DisplayStatusIDAppendToName, out var displayFlyText))
            {
                config.DisplayStatusIDAppendToName = displayFlyText;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.StatusFlyText.Help"));
        }

        ImGui.Unindent(OmniTheme.Scale(26f));

        return changed;
    }

    private static bool DrawAction(DisplayIDInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Action", "action", config.DisplayActionID, out var displayActionId))
        {
            config.DisplayActionID = displayActionId;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Indent(OmniTheme.Scale(26f));
        using (ImRaii.Disabled(!config.DisplayActionID))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.Resolved", "resolved", config.DisplayActionIDResolved, out var displayResolved))
            {
                config.DisplayActionIDResolved = displayResolved;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Original", "original", config.DisplayActionIDOriginal, out var displayOriginal))
            {
                config.DisplayActionIDOriginal = displayOriginal;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "actionFlyText", config.DisplayFlyTextDamageActionID, out var displayFlyText))
            {
                config.DisplayFlyTextDamageActionID = displayFlyText;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.ActionFlyText.Help"));
            if (DrawCheckbox("Feature.DisplayIdInformation.CastBar", "castBar", config.DisplayCastBarActionID, out var displayCastBar))
            {
                config.DisplayCastBarActionID = displayCastBar;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.CastBar.Help"));
        }

        ImGui.Unindent(OmniTheme.Scale(26f));

        return changed;
    }

    private static bool DrawTarget(DisplayIDInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Target", "target", config.DisplayTargetID, out var displayTargetId))
        {
            config.DisplayTargetID = displayTargetId;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Indent(OmniTheme.Scale(26f));
        using (ImRaii.Disabled(!config.DisplayTargetID))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.BattleNpc", "battleNpc", config.DisplayTargetIDBattleNPC, out var battleNpc))
            {
                config.DisplayTargetIDBattleNPC = battleNpc;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.EventNpc", "eventNpc", config.DisplayTargetIDEventNPC, out var eventNpc))
            {
                config.DisplayTargetIDEventNPC = eventNpc;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Companion", "companion", config.DisplayTargetIDCompanion, out var companion))
            {
                config.DisplayTargetIDCompanion = companion;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Others", "others", config.DisplayTargetIDOthers, out var others))
            {
                config.DisplayTargetIDOthers = others;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.Others.Help"));
        }

        ImGui.Unindent(OmniTheme.Scale(26f));

        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
    {
        value = current;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##displayId{id}", ref value);
    }
}
