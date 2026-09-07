using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal static class FoodReminderPanel
{
    private static readonly TerritorySelector TerritorySelector = new("foodReminderTerritory");

    public static bool Draw(FoodReminderConfig config, System.Action sendTestReminder)
    {
        var changed = NormalizeConfig(config);
        changed |= DrawReminderSettings(config);
        changed |= DrawPartyMessage(config, sendTestReminder);
        changed |= DrawTerritorySelector(config);
        return changed;
    }

    internal static bool NormalizeConfig(FoodReminderConfig config)
    {
        var changed = false;
        config.AllowedTerritoryIds ??= [];
        config.BlacklistTerritoryIds ??= [];
        config.WhitelistTerritoryIds ??= [];
        if (config.AllowedTerritoryIds.Count > 0)
        {
            config.WhitelistTerritoryIds.UnionWith(config.AllowedTerritoryIds);
            config.AllowedTerritoryIds.Clear();
            config.UseWhitelist = true;
            changed = true;
        }

        if (config.TargetNameMode is < 0 or > 1)
        {
            config.TargetNameMode = Math.Clamp(config.TargetNameMode, 0, 1);
            changed = true;
        }

        var threshold = Math.Clamp(config.ThresholdSeconds, 0, 7200);
        if (threshold != config.ThresholdSeconds)
        {
            config.ThresholdSeconds = threshold;
            changed = true;
        }

        return changed;
    }

    private static bool DrawReminderSettings(FoodReminderConfig config)
    {
        var changed = false;
        using var rowStyle = ImRaii.PushStyle(
            ImGuiStyleVar.FramePadding,
            new Vector2(
                ImGui.GetStyle().FramePadding.X,
                MathF.Max(0f, (OmniTheme.CheckboxSize() - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.TargetNameMode"));
        ImGui.SameLine();
        var characterName = config.TargetNameMode == 0;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.FoodReminder.TargetNameMode.CharacterName"),
                ref characterName))
        {
            config.TargetNameMode = characterName ? 0 : 1;
            changed = true;
        }

        ImGui.SameLine();
        var jobName = config.TargetNameMode == 1;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.FoodReminder.TargetNameMode.JobName"),
                ref jobName))
        {
            config.TargetNameMode = jobName ? 1 : 0;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(4f));
        OmniControls.HelpIcon(OmniLoc.Get("Feature.FoodReminder.TargetNameMode.Help"));
        ImGui.SameLine(0f, OmniTheme.Scale(22f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.Threshold"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(110f));
        var threshold = config.ThresholdSeconds;
        if (OmniControls.InputInt("##foodReminderThreshold", ref threshold, 0, 0))
        {
            config.ThresholdSeconds = Math.Clamp(threshold, 0, 7200);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.Seconds"));
        ImGui.SameLine(0f, OmniTheme.Scale(4f));
        OmniControls.HelpIcon(OmniLoc.Get("Feature.FoodReminder.Threshold.Help"));
        return changed;
    }

    private static bool DrawPartyMessage(FoodReminderConfig config, System.Action sendTestReminder)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.PartyMessage"));
        var message = config.PartyMessage ?? OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage");
        var inputSize = new Vector2(MathF.Max(OmniTheme.Scale(240f), ImGui.GetContentRegionAvail().X), OmniTheme.Scale(72f));
        if (OmniControls.InputTextMultiline(
                "##foodReminderPartyMessage",
                ref message,
                1024,
                inputSize))
        {
            config.PartyMessage = string.IsNullOrWhiteSpace(message)
                ? OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage")
                : message;
        }

        var changed = ImGui.IsItemDeactivatedAfterEdit();

        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.Reset"), false))
        {
            config.PartyMessage = OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage");
            changed = true;
        }

        ImGui.SameLine();
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.FoodReminder.Test"), false))
        {
            sendTestReminder();
        }

        return changed;
    }

    private static bool DrawTerritorySelector(FoodReminderConfig config)
    {
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.FoodReminder.Territory.WorkMode"));
        ImGui.SameLine();
        var changed = false;
        if (ImGui.RadioButton(
                $"{OmniLoc.Get("Feature.FoodReminder.Territory.Blacklist")}##foodReminderBlacklist",
                !config.UseWhitelist))
        {
            config.UseWhitelist = false;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton(
                $"{OmniLoc.Get("Feature.FoodReminder.Territory.Whitelist")}##foodReminderWhitelist",
                config.UseWhitelist))
        {
            config.UseWhitelist = true;
            changed = true;
        }

        ImGui.SameLine();
        OmniControls.HelpIcon(OmniLoc.Get("Feature.FoodReminder.Territory.WorkMode.Help"));
        ImGui.Spacing();
        changed |= TerritorySelector.Draw(
            config.UseWhitelist ? config.WhitelistTerritoryIds : config.BlacklistTerritoryIds,
            OmniLoc.Get(config.UseWhitelist
                ? "Feature.FoodReminder.Territory.Whitelist.Empty"
                : "Feature.FoodReminder.Territory.Blacklist.Empty"));
        return changed;
    }
}
