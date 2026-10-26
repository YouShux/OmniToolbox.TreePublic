using System.Globalization;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class HouseExpirationReminder
{
    internal static class Panel
    {
        public static bool Draw(
            HouseExpirationReminderConfig config,
            HouseExpirationReminder feature)
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
            HouseExpirationReminder feature)
        {
            var changed = false;
            using var table = OmniControls.SettingsTable(
                "##houseExpirationNotificationSettings",
                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.HouseExpirationReminder.ChatNotify")),
                OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.HouseExpirationReminder.PopupNotify")),
                OmniControls.MeasureGroup([ImGui.CalcTextSize(OmniLoc.Get("Feature.HouseExpirationReminder.WarnDays")),
                    OmniControls.MeasureInput("45", step: 1)]),
                OmniControls.CompactButtonSize(OmniLoc.Get("Feature.HouseExpirationReminder.Preview"))],
                ["##chat", "##popup", "##threshold", "##preview"],
                flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX, columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

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
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(OmniLoc.Get("Feature.HouseExpirationReminder.WarnDays"));
            var inputWidth = OmniControls.MeasureInput("45", step: 1).X;
            OmniControls.SameLineOrWrap(inputWidth);
            ImGui.SetNextItemWidth(inputWidth);
            var warnDays = HouseExpirationReminder.NormalizeWarnDays(config.WarnDays);
            if (OmniControls.InputInt("##houseExpirationWarnDays", ref warnDays))
            {
                config.WarnDays = Math.Clamp(
                    warnDays,
                    1,
                    HouseExpirationReminder.AutoDemolitionDays);
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();

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
            HouseExpirationReminder feature)
        {
            var changed = false;
            var currentRecord = feature.GetCurrentCharacterRecord();
            using var table = OmniControls.SettingsTable(
                "##houseExpirationRecords",
                [new(ImGui.GetFontSize() * 20f, ImGui.GetFrameHeight()), new(ImGui.GetFontSize() * 20f, ImGui.GetFrameHeight())],
                ["##personal", "##freeCompany"], flags: ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var personalNotify = config.PersonalNotify;
            if (DrawHouseRecord(
                    "personal",
                    "Feature.HouseExpirationReminder.PersonalNotify",
                    currentRecord?.PersonalLastVisitUTC ?? DateTime.MinValue,
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
                    currentRecord?.FreeCompanyLastVisitUTC ?? DateTime.MinValue,
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
            DateTime lastVisitUTC,
            ref bool notify,
            HouseExpirationReminder feature)
        {
            using var wrap = ImRaii.TextWrapPos(0f);
            var changed = OmniControls.Checkbox(OmniLoc.Get(notifyLabelKey), ref notify);
            if (changed && !notify)
            {
                feature.ClearHouseTypeReminderKeys(houseType);
            }

            lastVisitUTC = HouseExpirationReminder.AsUTC(lastVisitUTC);
            if (lastVisitUTC == DateTime.MinValue)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.HouseExpirationReminder.NotRecorded"));
                return changed;
            }

            ImGui.TextUnformatted(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.LastVisit"),
                lastVisitUTC.ToLocalTime()));
            ImGui.TextUnformatted(string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.EstimatedRemaining"),
                Math.Max(0, feature.GetRemainingDays(lastVisitUTC))));
            return changed;
        }

        private static bool DrawCharacterRecords(
            HouseExpirationReminderConfig config,
            HouseExpirationReminder feature)
        {
            if (config.CharacterRecords.Count == 0)
            {
                ImGui.TextDisabled(OmniLoc.Get("Feature.HouseExpirationReminder.NoCharacters"));
                return false;
            }

            var clearLabel = OmniLoc.Get("Feature.HouseExpirationReminder.Clear");
            var clearButtonSize = OmniControls.CompactButtonSize(clearLabel);
            using var table = OmniControls.DataTable(
                "##houseExpirationCharacterRecords",
                [OmniLoc.Get("Feature.HouseExpirationReminder.Character"), OmniLoc.Get("Feature.HouseExpirationReminder.PersonalInfo"),
                OmniLoc.Get("Feature.HouseExpirationReminder.FreeCompanyInfo"), clearLabel],
                [ImGui.GetFontSize() * 8f, ImGui.GetFontSize() * 10f, ImGui.GetFontSize() * 10f, clearButtonSize.X],
                [ImGui.GetFontSize() * 12f, ImGui.GetFontSize() * 14f, ImGui.GetFontSize() * 14f, clearButtonSize.X], out var detailLayout,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
                new Vector2(ImGui.GetContentRegionAvail().X, 0f), stretchColumn: 1);
            if (!table)
            {
                return false;
            }

            ImGui.TableHeadersRow();
            using var wrap = ImRaii.TextWrapPos(0f);

            for (var index = 0; index < config.CharacterRecords.Count; index++)
            {
                var record = config.CharacterRecords[index];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetCharacterDisplayName(record));
                OmniControls.NextTableField(OmniLoc.Get("Feature.HouseExpirationReminder.PersonalInfo"), detailLayout);
                ImGui.TextUnformatted(FormatHouseInfo(record.PersonalLastVisitUTC, feature));
                OmniControls.NextTableField(OmniLoc.Get("Feature.HouseExpirationReminder.FreeCompanyInfo"), detailLayout);
                ImGui.TextUnformatted(FormatHouseInfo(record.FreeCompanyLastVisitUTC, feature));
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
            DateTime lastVisitUTC,
            HouseExpirationReminder feature)
        {
            lastVisitUTC = HouseExpirationReminder.AsUTC(lastVisitUTC);
            if (lastVisitUTC == DateTime.MinValue)
            {
                return OmniLoc.Get("Feature.HouseExpirationReminder.NotRecordedShort");
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.RecordSummary"),
                lastVisitUTC.ToLocalTime(),
                Math.Max(0, feature.GetRemainingDays(lastVisitUTC)));
        }
    }

}