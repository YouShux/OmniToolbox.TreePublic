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
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HouseExpirationReminder(
    HouseExpirationReminderConfig config,
    Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("HouseExpirationReminderTitle"),
        Description = OmniLoc.Get("HouseExpirationReminderDescription"),
        Category = ModuleCategory.Daily
    };

    public override bool HasSettings => true;

    private ulong lastDetectedHouseID;
    private bool lastInsideOwnedHouse;

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

    public override bool DrawSettings() => Panel.Draw(config, this);

    internal HouseExpirationReminderCharacterRecord? GetCurrentCharacterRecord() =>
        GetCurrentCharacterRecord(false);

    internal int GetRemainingDays(DateTime lastVisitUTC) =>
        (int)Math.Ceiling((AsUTC(lastVisitUTC).AddDays(AutoDemolitionDays) - DateTime.UtcNow).TotalDays);

    internal void Preview() => SendReminder(
            OmniLoc.Get("Feature.HouseExpirationReminder.Title"),
            string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.HouseExpirationReminder.RemainingMessage"),
                OmniLoc.Get("Feature.HouseExpirationReminder.PersonalHouse"),
                NormalizeWarnDays(config.WarnDays)));

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
            record = new()
            {
                CharacterKey = key
            };
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

    #region 常量

    internal const int AutoDemolitionDays = 45;

    internal const int DefaultWarnDays = 10;

    #endregion
}