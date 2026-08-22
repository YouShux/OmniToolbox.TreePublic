using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.OmenService;
using OmenObjectTable = OmenTools.Dalamud.Services.Game.Object.Abstractions.IObjectTable;

namespace OmniToolbox.TreePublic;

public sealed class FoodReminder : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("FoodReminderTitle"),
        Description = OmniLoc.Get("FoodReminderDescription"),
        Category = ModuleCategory.Combat,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Combat/FoodReminder-1.png",
        Commands =
        [
            new ModuleCommand("Feature.FoodReminder.CommandDescription", "/omni 食物检查")
        ]
    };

    private readonly FoodReminderConfig config;
    private readonly System.Action saveConfig;
    private readonly Dictionary<uint, string> reminderTargetNames = [];
    private readonly Dictionary<uint, DalamudLinkPayload> reminderPayloads = [];
    private readonly HashSet<ReminderTargetKey> seenTargets = [];
    private FeatureLifetime? runtimeLifetime;
    private bool countdownProcessed;

    public FoodReminder(FoodReminderConfig config, System.Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        if (FoodReminderPanel.NormalizeConfig(config))
        {
            saveConfig();
        }
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
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Food reminder update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
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
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Food reminder countdown handling failed.");
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
        if (reminderPayloads.Count >= 256)
        {
            ClearReminderLinks();
        }

        var manager = LinkPayloadManager.Instance();
        var payload = manager.Reg(OnReminderLinkClicked, out var commandID);
        reminderPayloads[commandID] = payload;
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
        foreach (var commandID in reminderPayloads.Keys)
        {
            manager.Unreg(commandID);
        }

        reminderPayloads.Clear();
        reminderTargetNames.Clear();
    }

    private void SendTestReminder()
    {
        var local = DService.Instance().ObjectTable.LocalPlayer;
        var displayName = config.TargetNameMode == 1 && local is not null
            ? GetJobName(local.ClassJob)
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

[Serializable]
public sealed class FoodReminderConfig
{
    public int TargetNameMode { get; set; } = 1;

    public string PartyMessage { get; set; } = "吃一下食物！";

    public int ThresholdSeconds { get; set; } = 480;

    public bool UseWhitelist { get; set; }

    public HashSet<uint> BlacklistTerritoryIds { get; set; } = [];

    public HashSet<uint> WhitelistTerritoryIds { get; set; } = [];

    public HashSet<uint> AllowedTerritoryIds { get; set; } = [];
}
