using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Notifications;
using OmenObjectTable = OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed partial class FoodReminder : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/FoodReminder-1.png",
        Commands =
        [
            new ModuleCommand("Feature.FoodReminder.CommandDescription", OmniCommandCatalog.Format("foodcheck"))
        ]
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "FoodReminderTitle",
            ["Description"] = "FoodReminderDescription",
            ["Feature.FoodReminder.CommandDescription"] = "Feature.FoodReminder.CommandDescription",
            ["Feature.FoodReminder.DefaultPartyMessage"] = "Feature.FoodReminder.DefaultPartyMessage",
            ["Feature.FoodReminder.MissingFood"] = "Feature.FoodReminder.MissingFood",
            ["Feature.FoodReminder.PartyMessage"] = "Feature.FoodReminder.PartyMessage",
            ["Feature.FoodReminder.ReminderTag"] = "Feature.FoodReminder.ReminderTag",
            ["Feature.FoodReminder.Reset"] = "Feature.FoodReminder.Reset",
            ["Feature.FoodReminder.Seconds"] = "Feature.FoodReminder.Seconds",
            ["Feature.FoodReminder.TargetNameMode"] = "Feature.FoodReminder.TargetNameMode",
            ["Feature.FoodReminder.TargetNameMode.CharacterName"] = "Feature.FoodReminder.TargetNameMode.CharacterName",
            ["Feature.FoodReminder.TargetNameMode.Help"] = "Feature.FoodReminder.TargetNameMode.Help",
            ["Feature.FoodReminder.TargetNameMode.JobName"] = "Feature.FoodReminder.TargetNameMode.JobName",
            ["Feature.FoodReminder.Territory.Blacklist"] = "Feature.FoodReminder.Territory.Blacklist",
            ["Feature.FoodReminder.Territory.Whitelist"] = "Feature.FoodReminder.Territory.Whitelist",
            ["Feature.FoodReminder.Territory.WorkMode"] = "Feature.FoodReminder.Territory.WorkMode",
            ["Feature.FoodReminder.Territory.WorkMode.Help"] = "Feature.FoodReminder.Territory.WorkMode.Help",
            ["Feature.FoodReminder.Test"] = "Feature.FoodReminder.Test",
            ["Feature.FoodReminder.TestTarget"] = "Feature.FoodReminder.TestTarget",
            ["Feature.FoodReminder.Threshold"] = "Feature.FoodReminder.Threshold",
            ["Feature.FoodReminder.Threshold.Help"] = "Feature.FoodReminder.Threshold.Help"
        });

    public override bool HasSettings => true;

    private readonly FoodReminderConfig config;
    private readonly System.Action saveConfig;
    private readonly Dictionary<uint, string> reminderTargetNames = [];
    private readonly HashSet<ReminderTargetKey> seenTargets = [];
    private bool countdownProcessed;

    public FoodReminder(FoodReminderConfig config, System.Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        if (Panel.NormalizeConfig(config))
        {
            saveConfig();
        }
    }

    protected override void OnStart(FeatureLifetime activation)
    {
        var lifetime = Activation;
        lifetime.Add(() =>
        {
            countdownProcessed = false;
            seenTargets.Clear();
            ClearReminderLinks();
        });
        if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
        {
            throw new InvalidOperationException("Food reminder update registration failed.");
        }

        lifetime.OnStopping(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
    }

    public override bool DrawSettings() => Panel.Draw(config, SendTestReminder);

    public void CheckNow()
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            return;
        }

        if (!IsAllowedTerritory())
        {
            return;
        }

        try
        {
            CheckAndNotify();
        }
        catch (Exception)
        {
        }
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        try
        {
            var agent = AgentCountDownSettingDialog.Instance();
            if (agent == null || !agent->Active || agent->TimeRemaining <= 0f)
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
        catch (Exception)
        {
        }
    }

    private void CheckAndNotify()
    {
        seenTargets.Clear();
        var services = DService.Instance();
        if (services.ObjectTable.LocalPlayer is { } localPlayer)
        {
            TryRemind(new(
                localPlayer.Name,
                GetJobName(localPlayer.ClassJob),
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
            TryRemind(new(
                                   member.Name.TextValue,
                                   GetJobName(member.ClassJob),
                                   member.ContentId,
                                   memberObject?.GameObjectID ?? 0,
                                   member.EntityId,
                                   GetFoodRemainingSeconds(memberObject)));
        }
    }

    private void TryRemind(FoodCheckTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Name) || !seenTargets.Add(GetDeduplicationKey(target)))
        {
            return;
        }

        if (!ShouldRemind(target))
        {
            return;
        }

        SendMissingFoodReminder(target);
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
            .Append(OmniLoc.Get(this, "Feature.FoodReminder.MissingFood"))
            .AddUiForegroundOff()
            .Add(RawPayload.LinkTerminator)
            .Add(payload)
            .Add(new UIForegroundPayload(34))
            .Append(OmniLoc.Get(this, "Feature.FoodReminder.ReminderTag"))
            .Add(UIForegroundPayload.UIForegroundOff)
            .Add(RawPayload.LinkTerminator)
            .Build();
        OmniNotifier.Chat(message);
    }

    private DalamudLinkPayload RegisterReminderLink(string targetName)
    {
        if (reminderTargetNames.Count >= 256)
        {
            ClearReminderLinks();
        }

        var manager = LinkPayloadManager.Instance();
        var payload = manager.Reg(OnReminderLinkClicked, out var commandID);
        reminderTargetNames[commandID] = targetName;
        return payload;
    }

    private void OnReminderLinkClicked(uint commandID, SeString link)
    {
        if (!reminderTargetNames.TryGetValue(commandID, out var targetName))
        {
            return;
        }

        _ = DalamudServices.Framework.RunOnFrameworkThread(() =>
            ChatManager.Instance().SendMessage("/p " + targetName + NormalizePartyMessage()));
    }

    private void ClearReminderLinks()
    {
        var manager = LinkPayloadManager.Instance();
        foreach (var commandID in reminderTargetNames.Keys)
        {
            manager.Unreg(commandID);
        }

        reminderTargetNames.Clear();
    }

    private void SendTestReminder()
    {
        var local = DService.Instance().ObjectTable.LocalPlayer;
        var displayName = config.TargetNameMode == 1 && local is not null
            ? GetJobName(local.ClassJob)
            : local?.Name;
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? OmniLoc.Get(this, "Feature.FoodReminder.TestTarget")
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

        value = OmniLoc.Get(this, "Feature.FoodReminder.DefaultPartyMessage");
        config.PartyMessage = value;
        saveConfig();
        return value;
    }

    private bool IsAllowedTerritory() =>
        config.UseWhitelist
            ? config.WhitelistTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType)
            : !config.BlacklistTerritoryIds.Contains(DService.Instance().ClientState.TerritoryType);

    private static float GetFoodRemainingSeconds(IBattleChara? target)
    {
        if (target is null || !target.StatusList.TryGetStatus(48, out var status, out _) || status is null)
        {
            return 0f;
        }

        return MathF.Max(0f, status.RemainingTime);
    }

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
        target.ContentID != 0
            ? new(1, target.ContentID, string.Empty)
            : target.GameObjectID != 0
            ? new(2, target.GameObjectID, string.Empty)
            : target.EntityID != 0
            ? new(3, target.EntityID, string.Empty)
            : new(0, 0, target.Name);

    private readonly record struct FoodCheckTarget(
        string Name,
        string JobName,
        ulong ContentID,
        ulong GameObjectID,
        uint EntityID,
        float FoodRemainingSeconds);

    private readonly record struct ReminderTargetKey(byte Kind, ulong Value, string Name);
}
