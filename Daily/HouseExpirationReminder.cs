using System.Globalization;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class HouseExpirationReminder(
    HouseExpirationReminderConfig config,
    Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("HouseExpirationReminderTitle"),
        Description = OmniLoc.Get("HouseExpirationReminderDescription"),
        Category = ModuleCategory.Daily
    };

    internal const int AutoDemolitionDays = 45;
    internal const int DefaultWarnDays = 10;
    private ulong lastDetectedHouseID;
    private bool lastInsideOwnedHouse;

    public override bool HasSettings => true;

    public override bool DrawSettings() => HouseExpirationReminderPanel.Draw(config, this);

    protected override void OnEnable()
    {
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 1_000))
        {
            throw new InvalidOperationException("House expiration reminder update registration failed.");
        }
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        lastInsideOwnedHouse = false;
        lastDetectedHouseID = 0;
    }

    internal HouseExpirationReminderCharacterRecord? GetCurrentCharacterRecord() =>
        GetCurrentCharacterRecord(false);

    internal int GetRemainingDays(DateTime lastVisitUTC) =>
        (int)Math.Ceiling((AsUTC(lastVisitUTC).AddDays(AutoDemolitionDays) - DateTime.UtcNow).TotalDays);

    internal void Preview()
    {
        SendReminder(
            OmniLoc.Get("Feature.HouseExpirationReminder.Title"),
            string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.RemainingMessage"),
                OmniLoc.Get("Feature.HouseExpirationReminder.PersonalHouse"),
                NormalizeWarnDays(config.WarnDays)));
    }

    internal bool ClearCharacterRecord(HouseExpirationReminderCharacterRecord record)
    {
        if (!config.CharacterRecords.Remove(record))
        {
            return false;
        }

        RemoveReminderKeys($"character:{record.CharacterKey}:");
        return true;
    }

    internal void ClearHouseTypeReminderKeys(string houseType)
    {
        foreach (var record in config.CharacterRecords)
        {
            RemoveReminderKeys(GetReminderKeyPrefix(record, houseType));
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        DetectHousingState();
        CheckExpirationReminders();
    }

    private void DetectHousingState()
    {
        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn)
        {
            ResetHousingState();
            return;
        }

        var manager = HousingManager.Instance();
        if (manager is null || !manager->IsInside())
        {
            ResetHousingState();
            return;
        }

        var currentHouseID = manager->GetCurrentIndoorHouseId();
        var currentID = currentHouseID.Id;
        if (currentID == 0)
        {
            ResetHousingState();
            return;
        }

        if (currentHouseID.IsApartment ||
            currentHouseID.RoomNumber > 0 ||
            currentHouseID.IsWorkshop ||
            manager->IsInWorkshop())
        {
            lastInsideOwnedHouse = false;
            lastDetectedHouseID = currentID;
            return;
        }

        var personalHouseID = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
        if (personalHouseID.Id != 0 && personalHouseID.Id == currentID)
        {
            RecordHouseEntry(true, currentID);
            return;
        }

        var freeCompanyHouseID = HousingManager.GetOwnedHouseId(EstateType.FreeCompanyEstate);
        if (freeCompanyHouseID.Id != 0 && freeCompanyHouseID.Id == currentID)
        {
            RecordHouseEntry(false, currentID);
            return;
        }

        lastInsideOwnedHouse = false;
        lastDetectedHouseID = currentID;
    }

    private void RecordHouseEntry(bool personal, ulong houseID)
    {
        if (lastInsideOwnedHouse && lastDetectedHouseID == houseID)
        {
            return;
        }

        var record = GetCurrentCharacterRecord(true);
        if (record is null)
        {
            lastInsideOwnedHouse = false;
            lastDetectedHouseID = houseID;
            return;
        }

        if (personal)
        {
            record.PersonalLastVisitUTC = DateTime.UtcNow;
            record.PersonalHouseID = houseID;
            RemoveReminderKeys(GetReminderKeyPrefix(record, "personal"));
        }
        else
        {
            record.FreeCompanyLastVisitUTC = DateTime.UtcNow;
            record.FreeCompanyHouseID = houseID;
            RemoveReminderKeys(GetReminderKeyPrefix(record, "freecompany"));
        }

        saveConfig();
        lastInsideOwnedHouse = true;
        lastDetectedHouseID = houseID;
    }

    private void CheckExpirationReminders()
    {
        var record = GetCurrentCharacterRecord(false);
        if (record is null)
        {
            if (config.PersonalNotify || config.FreeCompanyNotify)
            {
                TrySendDataRefreshReminder(
                    "current:missing",
                    OmniLoc.Get("Feature.HouseExpirationReminder.CurrentCharacterMissing"));
            }

            return;
        }

        var warnDays = NormalizeWarnDays(config.WarnDays);
        if (config.PersonalNotify)
        {
            CheckHouseReminder(
                record,
                "personal",
                OmniLoc.Get("Feature.HouseExpirationReminder.PersonalHouse"),
                record.PersonalLastVisitUTC,
                warnDays);
        }

        if (config.FreeCompanyNotify)
        {
            CheckHouseReminder(
                record,
                "freecompany",
                OmniLoc.Get("Feature.HouseExpirationReminder.FreeCompanyHouse"),
                record.FreeCompanyLastVisitUTC,
                warnDays);
        }
    }

    private void CheckHouseReminder(
        HouseExpirationReminderCharacterRecord record,
        string houseType,
        string houseName,
        DateTime lastVisitUTC,
        int warnDays)
    {
        var reminderPrefix = GetReminderKeyPrefix(record, houseType);
        lastVisitUTC = AsUTC(lastVisitUTC);
        if (lastVisitUTC == DateTime.MinValue)
        {
            TrySendDataRefreshReminder(
                $"{reminderPrefix}missing",
                string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.HouseExpirationReminder.HouseMissing"),
                    houseName));
            return;
        }

        var remaining = GetRemainingDays(lastVisitUTC);
        if (remaining < 0)
        {
            TrySendDataRefreshReminder(
                $"{reminderPrefix}expired",
                string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.HouseExpirationReminder.HouseExpired"),
                    houseName,
                    AutoDemolitionDays));
            return;
        }

        if (remaining > warnDays || !AddReminderKey($"{reminderPrefix}{remaining}"))
        {
            return;
        }

        SendReminder(
            OmniLoc.Get("Feature.HouseExpirationReminder.Title"),
            string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.RemainingMessage"),
                houseName,
                remaining));
    }

    private void TrySendDataRefreshReminder(string key, string details)
    {
        if (AddReminderKey(key))
        {
            SendReminder(OmniLoc.Get("Feature.HouseExpirationReminder.Title"), details);
        }
    }

    private bool AddReminderKey(string key)
    {
        if (!config.ReminderKeys.Add(key))
        {
            return false;
        }

        saveConfig();
        return true;
    }

    private void RemoveReminderKeys(string prefix) =>
        config.ReminderKeys.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));

    private HouseExpirationReminderCharacterRecord? GetCurrentCharacterRecord(bool create)
    {
        if (!TryGetCurrentCharacter(out var key, out var name, out var world, out var contentId))
        {
            return null;
        }

        HouseExpirationReminderCharacterRecord? record = null;
        foreach (var candidate in config.CharacterRecords)
        {
            if (string.Equals(candidate.CharacterKey, key, StringComparison.Ordinal))
            {
                record = candidate;
                break;
            }
        }

        if (record is null && create)
        {
            record = new() { CharacterKey = key };
            config.CharacterRecords.Add(record);
        }

        if (record is null)
        {
            return null;
        }

        record.CharacterName = name;
        record.HomeWorld = world;
        record.ContentID = contentId;
        return record;
    }

    private static bool TryGetCurrentCharacter(
        out string key,
        out string name,
        out string world,
        out ulong contentID)
    {
        key = string.Empty;
        name = string.Empty;
        world = string.Empty;
        contentID = 0;

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn ||
            services.ObjectTable.LocalPlayer is not { } localPlayer ||
            !services.PlayerState.IsLoaded ||
            services.PlayerState.HomeWorld.ValueNullable is not { } homeWorld)
        {
            return false;
        }

        name = localPlayer.Name;
        world = homeWorld.Name.ToString();
        contentID = services.PlayerState.ContentId;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
        {
            return false;
        }

        key = contentID != 0
            ? contentID.ToString(CultureInfo.InvariantCulture)
            : $"{name}@{world}";
        return true;
    }

    private void SendReminder(string title, string details)
    {
        if (config.ChatNotify)
        {
            OmniNotifier.Chat($"{title}：{details}");
        }

        if (config.PopupNotify)
        {
            OmniNotifier.Popup(title, details, NotificationType.Success);
        }
    }

    private void ResetHousingState()
    {
        lastInsideOwnedHouse = false;
        lastDetectedHouseID = 0;
    }

    internal static int NormalizeWarnDays(int warnDays) =>
        Math.Clamp(warnDays <= 0 ? DefaultWarnDays : warnDays, 1, AutoDemolitionDays);

    private static string GetReminderKeyPrefix(
        HouseExpirationReminderCharacterRecord record,
        string houseType) =>
        $"character:{record.CharacterKey}:{houseType}:";

    internal static DateTime AsUTC(DateTime value) =>
        value == DateTime.MinValue
            ? DateTime.MinValue
            : value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

[Serializable]
public sealed class HouseExpirationReminderConfig
{
    public bool ChatNotify { get; set; } = true;

    public bool PopupNotify { get; set; } = true;

    public bool PersonalNotify { get; set; } = true;

    public bool FreeCompanyNotify { get; set; }

    public int WarnDays { get; set; } = 10;

    public HashSet<string> ReminderKeys { get; set; } = [];

    public List<HouseExpirationReminderCharacterRecord> CharacterRecords { get; set; } = [];
}

[Serializable]
public sealed class HouseExpirationReminderCharacterRecord
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    public ulong ContentID { get; set; }

    public DateTime PersonalLastVisitUTC { get; set; } = DateTime.MinValue;

    public DateTime FreeCompanyLastVisitUTC { get; set; } = DateTime.MinValue;

    public ulong PersonalHouseID { get; set; }

    public ulong FreeCompanyHouseID { get; set; }
}

internal static class HouseExpirationReminderPanel
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
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.HouseExpirationReminder.WarnDays"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(OmniTheme.Scale(76f));
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
            ImGui.TextUnformatted(FormatHouseInfo(record.PersonalLastVisitUTC, feature));
            ImGui.TableNextColumn();
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
