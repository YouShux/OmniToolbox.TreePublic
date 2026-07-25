using System.Globalization;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using OmniToolbox.Config;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.General;

public sealed unsafe class HouseExpirationReminderFeature(
    HouseExpirationReminderConfig config,
    Action saveConfig) : TreeHouseFeatureBase(new(
        "house-expiration-reminder",
        "Feature.HouseExpirationReminder.Title",
        "Feature.HouseExpirationReminder.Description",
        TreeHouseCategory.General))
{
    internal const int AutoDemolitionDays = 45;
    internal const int DefaultWarnDays = 10;
    private const uint RefreshIntervalMilliseconds = 1_000;

    private ulong lastDetectedHouseId;
    private bool lastInsideOwnedHouse;

    public override bool HasSettings => true;

    public override bool DrawSettings() => HouseExpirationReminderPanel.Draw(config, this);

    protected override void OnEnable()
    {
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, RefreshIntervalMilliseconds))
        {
            throw new InvalidOperationException("House expiration reminder update registration failed.");
        }
    }

    protected override void OnDisable()
    {
        FrameworkManager.Instance().Unreg(OnFrameworkUpdate);
        lastInsideOwnedHouse = false;
        lastDetectedHouseId = 0;
    }

    internal HouseExpirationReminderCharacterRecord? GetCurrentCharacterRecord() =>
        GetCurrentCharacterRecord(false);

    internal int GetRemainingDays(DateTime lastVisitUtc) =>
        (int)Math.Ceiling((AsUtc(lastVisitUtc).AddDays(AutoDemolitionDays) - DateTime.UtcNow).TotalDays);

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

        var currentHouseId = manager->GetCurrentIndoorHouseId();
        var currentId = currentHouseId.Id;
        if (currentId == 0)
        {
            ResetHousingState();
            return;
        }

        if (currentHouseId.IsApartment ||
            currentHouseId.RoomNumber > 0 ||
            currentHouseId.IsWorkshop ||
            manager->IsInWorkshop())
        {
            lastInsideOwnedHouse = false;
            lastDetectedHouseId = currentId;
            return;
        }

        var personalHouseId = HousingManager.GetOwnedHouseId(EstateType.PersonalEstate);
        if (personalHouseId.Id != 0 && personalHouseId.Id == currentId)
        {
            RecordHouseEntry(true, currentId);
            return;
        }

        var freeCompanyHouseId = HousingManager.GetOwnedHouseId(EstateType.FreeCompanyEstate);
        if (freeCompanyHouseId.Id != 0 && freeCompanyHouseId.Id == currentId)
        {
            RecordHouseEntry(false, currentId);
            return;
        }

        lastInsideOwnedHouse = false;
        lastDetectedHouseId = currentId;
    }

    private void RecordHouseEntry(bool personal, ulong houseId)
    {
        if (lastInsideOwnedHouse && lastDetectedHouseId == houseId)
        {
            return;
        }

        var record = GetCurrentCharacterRecord(true);
        if (record is null)
        {
            lastInsideOwnedHouse = false;
            lastDetectedHouseId = houseId;
            return;
        }

        if (personal)
        {
            record.PersonalLastVisitUtc = DateTime.UtcNow;
            record.PersonalHouseId = houseId;
            RemoveReminderKeys(GetReminderKeyPrefix(record, "personal"));
        }
        else
        {
            record.FreeCompanyLastVisitUtc = DateTime.UtcNow;
            record.FreeCompanyHouseId = houseId;
            RemoveReminderKeys(GetReminderKeyPrefix(record, "freecompany"));
        }

        saveConfig();
        lastInsideOwnedHouse = true;
        lastDetectedHouseId = houseId;
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
                record.PersonalLastVisitUtc,
                warnDays);
        }

        if (config.FreeCompanyNotify)
        {
            CheckHouseReminder(
                record,
                "freecompany",
                OmniLoc.Get("Feature.HouseExpirationReminder.FreeCompanyHouse"),
                record.FreeCompanyLastVisitUtc,
                warnDays);
        }
    }

    private void CheckHouseReminder(
        HouseExpirationReminderCharacterRecord record,
        string houseType,
        string houseName,
        DateTime lastVisitUtc,
        int warnDays)
    {
        var reminderPrefix = GetReminderKeyPrefix(record, houseType);
        lastVisitUtc = AsUtc(lastVisitUtc);
        if (lastVisitUtc == DateTime.MinValue)
        {
            TrySendDataRefreshReminder(
                $"{reminderPrefix}missing",
                string.Format(
                    CultureInfo.CurrentCulture,
                    OmniLoc.Get("Feature.HouseExpirationReminder.HouseMissing"),
                    houseName));
            return;
        }

        var remaining = GetRemainingDays(lastVisitUtc);
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
        record.ContentId = contentId;
        return record;
    }

    private static bool TryGetCurrentCharacter(
        out string key,
        out string name,
        out string world,
        out ulong contentId)
    {
        key = string.Empty;
        name = string.Empty;
        world = string.Empty;
        contentId = 0;

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
        contentId = services.PlayerState.ContentId;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
        {
            return false;
        }

        key = contentId != 0
            ? contentId.ToString(CultureInfo.InvariantCulture)
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
        lastDetectedHouseId = 0;
    }

    internal static int NormalizeWarnDays(int warnDays) =>
        Math.Clamp(warnDays <= 0 ? DefaultWarnDays : warnDays, 1, AutoDemolitionDays);

    private static string GetReminderKeyPrefix(
        HouseExpirationReminderCharacterRecord record,
        string houseType) =>
        $"character:{record.CharacterKey}:{houseType}:";

    internal static DateTime AsUtc(DateTime value) =>
        value == DateTime.MinValue
            ? DateTime.MinValue
            : value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
