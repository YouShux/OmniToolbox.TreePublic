using OmniToolbox.Config;
using OmniToolbox.UI;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class DisplayIdInformationPanel
{
    public static bool Draw(DisplayIdInformationConfig config, IconBrowser iconBrowser)
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
        if (DrawCheckbox("Feature.DisplayIdInformation.Item", "item", config.DisplayItemId, out var displayItemId))
        {
            config.DisplayItemId = displayItemId;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Weather", "weather", config.DisplayWeatherId, out var displayWeatherId))
        {
            config.DisplayWeatherId = displayWeatherId;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Zone", "zone", config.DisplayZoneInfo, out var displayZoneInfo))
        {
            config.DisplayZoneInfo = displayZoneInfo;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.DisplayIdInformation.Icon", "icon", config.DisplayIconId, out var displayIconId))
        {
            config.DisplayIconId = displayIconId;
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

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawStatus(config);
        ImGui.TableNextColumn();
        changed |= DrawAction(config);
        ImGui.TableNextColumn();
        changed |= DrawTarget(config);
        return changed;
    }

    private static bool DrawStatus(DisplayIdInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Status", "status", config.DisplayStatusId, out var displayStatusId))
        {
            config.DisplayStatusId = displayStatusId;
            changed = true;
        }

        using (ImRaii.PushIndent())
        using (ImRaii.Disabled(!config.DisplayStatusId))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "statusFlyText", config.DisplayStatusIdAppendToName, out var displayFlyText))
            {
                config.DisplayStatusIdAppendToName = displayFlyText;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.StatusFlyText.Help"));
        }

        return changed;
    }

    private static bool DrawAction(DisplayIdInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Action", "action", config.DisplayActionId, out var displayActionId))
        {
            config.DisplayActionId = displayActionId;
            changed = true;
        }

        using (ImRaii.PushIndent())
        using (ImRaii.Disabled(!config.DisplayActionId))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.Resolved", "resolved", config.DisplayActionIdResolved, out var displayResolved))
            {
                config.DisplayActionIdResolved = displayResolved;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Original", "original", config.DisplayActionIdOriginal, out var displayOriginal))
            {
                config.DisplayActionIdOriginal = displayOriginal;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.FlyText", "actionFlyText", config.DisplayFlyTextDamageActionId, out var displayFlyText))
            {
                config.DisplayFlyTextDamageActionId = displayFlyText;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.ActionFlyText.Help"));
            if (DrawCheckbox("Feature.DisplayIdInformation.CastBar", "castBar", config.DisplayCastBarActionId, out var displayCastBar))
            {
                config.DisplayCastBarActionId = displayCastBar;
                changed = true;
            }

            ImGuiOm.HelpMarker(OmniLoc.Get("Feature.DisplayIdInformation.CastBar.Help"));
        }

        return changed;
    }

    private static bool DrawTarget(DisplayIdInformationConfig config)
    {
        var changed = false;
        if (DrawCheckbox("Feature.DisplayIdInformation.Target", "target", config.DisplayTargetId, out var displayTargetId))
        {
            config.DisplayTargetId = displayTargetId;
            changed = true;
        }

        using (ImRaii.PushIndent())
        using (ImRaii.Disabled(!config.DisplayTargetId))
        {
            if (DrawCheckbox("Feature.DisplayIdInformation.BattleNpc", "battleNpc", config.DisplayTargetIdBattleNpc, out var battleNpc))
            {
                config.DisplayTargetIdBattleNpc = battleNpc;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.EventNpc", "eventNpc", config.DisplayTargetIdEventNpc, out var eventNpc))
            {
                config.DisplayTargetIdEventNpc = eventNpc;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Companion", "companion", config.DisplayTargetIdCompanion, out var companion))
            {
                config.DisplayTargetIdCompanion = companion;
                changed = true;
            }

            if (DrawCheckbox("Feature.DisplayIdInformation.Others", "others", config.DisplayTargetIdOthers, out var others))
            {
                config.DisplayTargetIdOthers = others;
                changed = true;
            }
        }

        return changed;
    }

    private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
    {
        value = current;
        return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##displayId{id}", ref value);
    }
}
