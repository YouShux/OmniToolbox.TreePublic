using System.Globalization;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.General;

internal static class HouseExpirationReminderPanel
{
    public static bool Draw(
        HouseExpirationReminderConfig config,
        HouseExpirationReminderFeature feature)
    {
        var changed = DrawNotificationSettings(config, feature);
        ImGui.Spacing();
        changed |= DrawHouseRecords(config, feature);
        ImGui.Spacing();
        changed |= DrawCharacterRecords(config, feature);
        return changed;
    }

    private static bool DrawNotificationSettings(
        HouseExpirationReminderConfig config,
        HouseExpirationReminderFeature feature)
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##houseExpirationNotificationSettings",
            4,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##chat", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##popup", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##threshold", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        ImGui.TableSetupColumn("##preview", ImGuiTableColumnFlags.WidthFixed, OmniTheme.SmallButtonSize().X);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var chatNotify = config.ChatNotify;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.HouseExpirationReminder.ChatNotify"),
                ref chatNotify))
        {
            config.ChatNotify = chatNotify;
            changed = true;
        }

        ImGui.TableNextColumn();
        var popupNotify = config.PopupNotify;
        if (OmniControls.Checkbox(
                OmniLoc.Get("Feature.HouseExpirationReminder.PopupNotify"),
                ref popupNotify))
        {
            config.PopupNotify = popupNotify;
            changed = true;
        }

        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(OmniTheme.Scale(76f));
        var warnDays = HouseExpirationReminderFeature.NormalizeWarnDays(config.WarnDays);
        if (OmniControls.InputInt("##houseExpirationWarnDays", ref warnDays))
        {
            config.WarnDays = Math.Clamp(
                warnDays,
                1,
                HouseExpirationReminderFeature.AutoDemolitionDays);
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.HouseExpirationReminder.WarnDays"));

        ImGui.TableNextColumn();
        if (OmniControls.SmallButton(
                OmniLoc.Get("Feature.HouseExpirationReminder.Preview"),
                false))
        {
            feature.Preview();
        }

        return changed;
    }

    private static bool DrawHouseRecords(
        HouseExpirationReminderConfig config,
        HouseExpirationReminderFeature feature)
    {
        var changed = false;
        var currentRecord = feature.GetCurrentCharacterRecord();
        using var table = ImRaii.Table(
            "##houseExpirationRecords",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##personal", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##freeCompany", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var personalNotify = config.PersonalNotify;
        if (DrawHouseRecord(
                "personal",
                "Feature.HouseExpirationReminder.PersonalNotify",
                currentRecord?.PersonalLastVisitUtc ?? DateTime.MinValue,
                ref personalNotify,
                feature))
        {
            config.PersonalNotify = personalNotify;
            changed = true;
        }

        ImGui.TableNextColumn();
        var freeCompanyNotify = config.FreeCompanyNotify;
        if (DrawHouseRecord(
                "freecompany",
                "Feature.HouseExpirationReminder.FreeCompanyNotify",
                currentRecord?.FreeCompanyLastVisitUtc ?? DateTime.MinValue,
                ref freeCompanyNotify,
                feature))
        {
            config.FreeCompanyNotify = freeCompanyNotify;
            changed = true;
        }

        return changed;
    }

    private static bool DrawHouseRecord(
        string houseType,
        string notifyLabelKey,
        DateTime lastVisitUtc,
        ref bool notify,
        HouseExpirationReminderFeature feature)
    {
        var changed = OmniControls.Checkbox(OmniLoc.Get(notifyLabelKey), ref notify);
        if (changed && !notify)
        {
            feature.ClearHouseTypeReminderKeys(houseType);
        }

        lastVisitUtc = HouseExpirationReminderFeature.AsUtc(lastVisitUtc);
        if (lastVisitUtc == DateTime.MinValue)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.HouseExpirationReminder.NotRecorded"));
            return changed;
        }

        ImGui.TextUnformatted(string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.HouseExpirationReminder.LastVisit"),
            lastVisitUtc.ToLocalTime()));
        ImGui.TextUnformatted(string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.HouseExpirationReminder.EstimatedRemaining"),
            Math.Max(0, feature.GetRemainingDays(lastVisitUtc))));
        return changed;
    }

    private static bool DrawCharacterRecords(
        HouseExpirationReminderConfig config,
        HouseExpirationReminderFeature feature)
    {
        if (config.CharacterRecords.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.HouseExpirationReminder.NoCharacters"));
            return false;
        }

        var clearLabel = OmniLoc.Get("Feature.HouseExpirationReminder.Clear");
        var clearButtonSize = OmniControls.CompactButtonSize(clearLabel);
        using var table = ImRaii.Table(
            "##houseExpirationCharacterRecords",
            4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.HouseExpirationReminder.Character"),
            ImGuiTableColumnFlags.WidthStretch,
            1.2f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.HouseExpirationReminder.PersonalInfo"),
            ImGuiTableColumnFlags.WidthStretch,
            1.4f);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.HouseExpirationReminder.FreeCompanyInfo"),
            ImGuiTableColumnFlags.WidthStretch,
            1.4f);
        ImGui.TableSetupColumn(
            clearLabel,
            ImGuiTableColumnFlags.WidthFixed,
            clearButtonSize.X);
        ImGui.TableHeadersRow();

        for (var index = 0; index < config.CharacterRecords.Count; index++)
        {
            var record = config.CharacterRecords[index];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetCharacterDisplayName(record));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatHouseInfo(record.PersonalLastVisitUtc, feature));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatHouseInfo(record.FreeCompanyLastVisitUtc, feature));
            ImGui.TableNextColumn();
            if (!OmniControls.SmallButton(
                    $"{clearLabel}##houseExpirationClear{record.CharacterKey}",
                    false,
                    clearButtonSize))
            {
                continue;
            }

            return feature.ClearCharacterRecord(record);
        }

        return false;
    }

    private static string GetCharacterDisplayName(HouseExpirationReminderCharacterRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.CharacterName) && string.IsNullOrWhiteSpace(record.HomeWorld))
        {
            return string.IsNullOrWhiteSpace(record.CharacterKey)
                ? OmniLoc.Get("Feature.HouseExpirationReminder.UnknownCharacter")
                : record.CharacterKey;
        }

        return $"{record.CharacterName}@{record.HomeWorld}";
    }

    private static string FormatHouseInfo(
        DateTime lastVisitUtc,
        HouseExpirationReminderFeature feature)
    {
        lastVisitUtc = HouseExpirationReminderFeature.AsUtc(lastVisitUtc);
        if (lastVisitUtc == DateTime.MinValue)
        {
            return OmniLoc.Get("Feature.HouseExpirationReminder.NotRecordedShort");
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            OmniLoc.Get("Feature.HouseExpirationReminder.RecordSummary"),
            lastVisitUtc.ToLocalTime(),
            Math.Max(0, feature.GetRemainingDays(lastVisitUtc)));
    }
}
