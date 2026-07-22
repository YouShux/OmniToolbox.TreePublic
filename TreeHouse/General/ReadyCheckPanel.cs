using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreeHouse.General;

internal static class ReadyCheckPanel
{
    public static bool Draw(ReadyCheckConfig config)
    {
        var changed = DrawClearSettings(config);
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.IconSettings"));
        using var table = ImRaii.Table(
            "##readyCheckIconSettings",
            3,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.List"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.Offset"), ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn(OmniLoc.Get("Feature.ReadyCheck.Scale"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableHeadersRow();
        var partyOffset = config.PartyIconOffset;
        var partyScale = config.PartyIconScale;
        changed |= DrawIconSettings(
            "party",
            "Feature.ReadyCheck.PartyList",
            ref partyOffset,
            ref partyScale);
        config.PartyIconOffset = partyOffset;
        config.PartyIconScale = partyScale;

        var allianceOffset = config.AllianceIconOffset;
        var allianceScale = config.AllianceIconScale;
        changed |= DrawIconSettings(
            "alliance",
            "Feature.ReadyCheck.AllianceList",
            ref allianceOffset,
            ref allianceScale);
        config.AllianceIconOffset = allianceOffset;
        config.AllianceIconScale = allianceScale;

        var crossWorldAllianceOffset = config.CrossWorldAllianceIconOffset;
        var crossWorldAllianceScale = config.CrossWorldAllianceIconScale;
        changed |= DrawIconSettings(
            "crossWorldAlliance",
            "Feature.ReadyCheck.CrossWorldAllianceList",
            ref crossWorldAllianceOffset,
            ref crossWorldAllianceScale);
        config.CrossWorldAllianceIconOffset = crossWorldAllianceOffset;
        config.CrossWorldAllianceIconScale = crossWorldAllianceScale;
        return changed;
    }

    private static bool DrawClearSettings(ReadyCheckConfig config)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##readyCheckClearSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##readyCheckClearTerritory", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearCombat", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearDutyCombat", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##readyCheckClearTime", ImGuiTableColumnFlags.WidthStretch, 1.25f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnTerritoryChanged",
                "territory",
                config.ClearOnTerritoryChanged,
                out var clearOnTerritoryChanged))
        {
            config.ClearOnTerritoryChanged = clearOnTerritoryChanged;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnCombat",
                "combat",
                config.ClearOnCombat,
                out var clearOnCombat))
        {
            config.ClearOnCombat = clearOnCombat;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearOnInstancedCombat",
                "instancedCombat",
                config.ClearOnInstancedCombat,
                out var clearOnInstancedCombat))
        {
            config.ClearOnInstancedCombat = clearOnInstancedCombat;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
                "Feature.ReadyCheck.ClearAfterTime",
                "time",
                config.ClearAfterTime,
                out var clearAfterTime))
        {
            config.ClearAfterTime = clearAfterTime;
            changed = true;
        }

        using (ImRaii.Disabled(!config.ClearAfterTime))
        {
            ImGui.SetNextItemWidth(
                ImGui.CalcTextSize("000").X + ImGui.GetStyle().FramePadding.X * 4f);
            var seconds = Math.Clamp(config.ClearAfterSeconds, 30, 900);
            if (ImGui.InputInt("##readyCheckClearAfterSeconds", ref seconds, 0, 0))
            {
                config.ClearAfterSeconds = Math.Clamp(seconds, 30, 900);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
            ImGui.SameLine();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.ReadyCheck.Seconds"));
        }

        return changed;
    }

    private static bool DrawCheckbox(
        string labelKey,
        string id,
        bool current,
        out bool value)
    {
        value = current;
        return ImGui.Checkbox($"{OmniLoc.Get(labelKey)}##readyCheckClear{id}", ref value);
    }

    private static bool DrawIconSettings(
        string id,
        string labelKey,
        ref Vector2 offset,
        ref float scale)
    {
        var changed = false;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.DragFloat2(
            $"##readyCheck{id}Offset",
            ref offset,
            1f,
            -100f,
            100f,
            "%.0f");

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.DragFloat(
            $"##readyCheck{id}Scale",
            ref scale,
            0.05f,
            0.3f,
            5f,
            "%.2f",
            ImGuiSliderFlags.AlwaysClamp);

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        return changed;
    }
}
