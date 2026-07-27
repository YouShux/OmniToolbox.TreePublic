using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.OmenService;
using OmenObjectTable = OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

namespace OmniToolbox.TreeHouse.General;

public sealed class FoodReminderFeature : TreeHouseFeatureBase
{
    private const uint FoodStatusId = 48;
    private const int MaxReminderLinks = 256;
    private const string CountdownAddonName = "ScreenInfo_CountDown";

    private readonly FoodReminderConfig config;
    private readonly System.Action saveConfig;
    private readonly Dictionary<uint, string> reminderTargetNames = [];
    private readonly Dictionary<uint, DalamudLinkPayload> reminderPayloads = [];
    private readonly HashSet<ReminderTargetKey> seenTargets = [];
    private FeatureLifetime? runtimeLifetime;
    private bool countdownProcessed;

    public FoodReminderFeature(FoodReminderConfig config, System.Action saveConfig)
        : base(new(
            "food-reminder",
            "Feature.FoodReminder.Title",
            "Feature.FoodReminder.Description",
            TreeHouseCategory.General,
            Commands:
            [
                new("Feature.FoodReminder.CommandDescription", "/omni 食物检查")
            ]))
    {
        this.config = config;
        this.saveConfig = saveConfig;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings() => FoodReminderPanel.Draw(config, SendTestReminder);

    public void CheckNow()
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.FoodReminder.NotLoggedIn"));
            return;
        }

        if (!IsAllowedTerritory())
        {
            OmniNotifier.Chat(OmniLoc.Get("Feature.FoodReminder.TerritoryDisabled"));
            return;
        }

        try
        {
            if (CheckAndNotify() == 0)
            {
                OmniNotifier.Chat(OmniLoc.Get("Feature.FoodReminder.CheckComplete"));
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Food reminder manual check failed.");
            OmniNotifier.Chat(OmniLoc.Get("Feature.FoodReminder.CheckFailed"));
        }
    }

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var addonEvents = new AddonEventRegistry(DalamudServices.AddonLifecycle);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PostSetup, CountdownAddonName, OnCountdownAddon);
            addonEvents.Register(AddonEvent.PostRequestedUpdate, CountdownAddonName, OnCountdownAddon);
            addonEvents.Register(AddonEvent.PreFinalize, CountdownAddonName, OnCountdownAddon);

            DalamudServices.ChatGui.ChatMessage += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGui.ChatMessage -= OnChatMessage);
            runtimeLifetime = lifetime;
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            countdownProcessed = false;
            seenTargets.Clear();
            ClearReminderLinks();
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (!DService.Instance().ClientState.IsLoggedIn || !IsAllowedTerritory())
            {
                return;
            }

            var text = message.Message.TextValue;
            if (IsCountdownStopMessage(text))
            {
                countdownProcessed = false;
                return;
            }

            if (!countdownProcessed && IsCountdownStartMessage(text))
            {
                countdownProcessed = true;
                CheckAndNotify();
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Food reminder countdown chat handling failed.");
        }
    }

    private void OnCountdownAddon(AddonEvent type, AddonArgs _)
    {
        try
        {
            if (type == AddonEvent.PreFinalize)
            {
                countdownProcessed = false;
                return;
            }

            if (!DService.Instance().ClientState.IsLoggedIn ||
                !IsAllowedTerritory() ||
                countdownProcessed)
            {
                return;
            }

            countdownProcessed = true;
            CheckAndNotify();
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Food reminder countdown addon handling failed.");
        }
    }

    private int CheckAndNotify()
    {
        var count = 0;
        seenTargets.Clear();
        var services = DService.Instance();
        if (services.ObjectTable.LocalPlayer is { } localPlayer)
        {
            count += TryRemind(new(
                localPlayer.Name,
                GetJobName(localPlayer),
                services.PlayerState.ContentId,
                localPlayer.GameObjectID,
                localPlayer.EntityID,
                GetFoodRemainingSeconds(localPlayer)));
        }

        foreach (var member in services.PartyList)
        {
            if (member.EntityId == 0)
            {
                continue;
            }

            var memberObject = services.ObjectTable.SearchByEntityID(
                member.EntityId,
                OmenObjectTable.CharactersRange) as IBattleChara;
            count += TryRemind(new(
                member.Name.TextValue,
                GetJobName(member.ClassJob),
                member.ContentId,
                memberObject?.GameObjectID ?? 0,
                member.EntityId,
                GetFoodRemainingSeconds(memberObject)));
        }

        return count;
    }

    private int TryRemind(FoodCheckTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Name) || !seenTargets.Add(GetDeduplicationKey(target)))
        {
            return 0;
        }

        if (!ShouldRemind(target))
        {
            return 0;
        }

        SendMissingFoodReminder(target);
        return 1;
    }

    private bool ShouldRemind(FoodCheckTarget target) =>
        target.FoodRemainingSeconds <= 0f ||
        target.FoodRemainingSeconds <= Math.Clamp(config.ThresholdSeconds, 0, 7200);

    private void SendMissingFoodReminder(FoodCheckTarget target)
    {
        var displayName = config.TargetNameMode == 1
            ? string.IsNullOrWhiteSpace(target.JobName) ? target.Name : target.JobName
            : string.IsNullOrWhiteSpace(target.Name) ? target.JobName : target.Name;
        var payload = RegisterReminderLink(displayName);
        var message = new SeStringBuilder()
            .AddUiForeground(1)
            .Append(target.Name)
            .Append(" ")
            .Append(OmniLoc.Get("Feature.FoodReminder.MissingFood"))
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .Add(payload)
            .Add(new UIForegroundPayload(34))
            .Append(OmniLoc.Get("Feature.FoodReminder.ReminderTag"))
            .Add(UIForegroundPayload.UIForegroundOff)
            .Add(RawPayload.LinkTerminator)
            .Build();
        OmniNotifier.Chat(message);
    }

    private DalamudLinkPayload RegisterReminderLink(string targetName)
    {
        if (reminderPayloads.Count >= MaxReminderLinks)
        {
            ClearReminderLinks();
        }

        var manager = LinkPayloadManager.Instance();
        var payload = manager.Reg(OnReminderLinkClicked, out var commandId);
        reminderPayloads[commandId] = payload;
        reminderTargetNames[commandId] = targetName;
        return payload;
    }

    private void OnReminderLinkClicked(uint commandId, SeString link)
    {
        if (!reminderTargetNames.TryGetValue(commandId, out var targetName))
        {
            return;
        }

        _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
            ChatManager.Instance().SendMessage("/p " + targetName + NormalizePartyMessage()));
    }

    private void ClearReminderLinks()
    {
        var manager = LinkPayloadManager.Instance();
        foreach (var commandId in reminderPayloads.Keys)
        {
            manager.Unreg(commandId);
        }

        reminderPayloads.Clear();
        reminderTargetNames.Clear();
    }

    private void SendTestReminder()
    {
        var local = DService.Instance().ObjectTable.LocalPlayer;
        var displayName = config.TargetNameMode == 1 && local is not null
            ? GetJobName(local)
            : local?.Name;
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? OmniLoc.Get("Feature.FoodReminder.TestTarget")
            : displayName;

        _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
            ChatManager.Instance().SendMessage("/p " + displayName + NormalizePartyMessage()));
    }

    private string NormalizePartyMessage()
    {
        var value = config.PartyMessage?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = OmniLoc.Get("Feature.FoodReminder.DefaultPartyMessage");
        config.PartyMessage = value;
        saveConfig();
        return value;
    }

    private bool IsAllowedTerritory() =>
        config.AllowedTerritoryIds.Count > 0 &&
        config.AllowedTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType);

    private static bool IsCountdownStartMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (IsCountdownStopMessage(value))
        {
            return false;
        }

        return value.Contains("倒计时", StringComparison.Ordinal) ||
               value.Contains("倒數", StringComparison.Ordinal) ||
               value.Contains("Battle commencing in", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("戦闘開始まで", StringComparison.Ordinal) ||
               (value.Contains("战斗", StringComparison.Ordinal) || value.Contains("戰鬥", StringComparison.Ordinal)) &&
               (value.Contains("开始", StringComparison.Ordinal) || value.Contains("開始", StringComparison.Ordinal)) &&
               value.Contains("秒", StringComparison.Ordinal);
    }

    private static bool IsCountdownStopMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var hasCountdown = value.Contains("倒计时", StringComparison.Ordinal) ||
                           value.Contains("倒數", StringComparison.Ordinal) ||
                           value.Contains("countdown", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("カウントダウン", StringComparison.Ordinal);
        if (!hasCountdown)
        {
            return false;
        }

        return value.Contains("取消", StringComparison.Ordinal) ||
               value.Contains("中止", StringComparison.Ordinal) ||
               value.Contains("停止", StringComparison.Ordinal) ||
               value.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("stopped", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("aborted", StringComparison.OrdinalIgnoreCase);
    }

    private static float GetFoodRemainingSeconds(IBattleChara? target)
    {
        if (target is null || !target.StatusList.TryGetStatus(FoodStatusId, out var status, out _) || status is null)
        {
            return 0f;
        }

        return MathF.Max(0f, status.RemainingTime);
    }

    private static string GetJobName(ICharacter character) => GetJobName(character.ClassJob);

    private static string GetJobName(Lumina.Excel.RowRef<ClassJob> classJob)
    {
        var name = classJob.ValueNullable?.Name.ToString().Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = classJob.ValueNullable?.Abbreviation.ToString().Trim() ?? string.Empty;
        }

        return name switch
        {
            "暗黑骑士" => "黑骑",
            "绝枪战士" => "绝枪",
            "白魔法师" => "白魔",
            "占星术士" => "占星",
            "龙骑士" => "龙骑",
            "武士" => "盘子",
            "钐镰客" => "镰刀",
            "蝰蛇剑士" => "蝰蛇",
            "吟游诗人" => "诗人",
            "机工士" => "机工",
            "黑魔法师" => "黑魔",
            "召唤师" => "召唤",
            "赤魔法师" => "赤魔",
            "青魔法师" => "青魔",
            "绘灵法师" => "画家",
            _ => name
        };
    }

    private static ReminderTargetKey GetDeduplicationKey(FoodCheckTarget target) =>
        target.ContentId != 0
            ? new(1, target.ContentId, string.Empty)
            : target.GameObjectId != 0
                ? new(2, target.GameObjectId, string.Empty)
                : target.EntityId != 0
                    ? new(3, target.EntityId, string.Empty)
                    : new(0, 0, target.Name);

    private readonly record struct FoodCheckTarget(
        string Name,
        string JobName,
        ulong ContentId,
        ulong GameObjectId,
        uint EntityId,
        float FoodRemainingSeconds);

    private readonly record struct ReminderTargetKey(byte Kind, ulong Value, string Name);
}
