using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.Data;
using OmenTools.Info.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Interop.Game.AgentEvent;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmenTools.Threading.TaskHelper;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AutoLogin(
    AutoLoginConfig config,
    System.Action saveConfig) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("AutoLoginTitle"),
        Description = OmniLoc.Get("AutoLoginDescription"),
        Category = ModuleCategory.Daily,
        Commands =
        [
            new ModuleCommand(
                "Feature.AutoLogin.CommandDescription",
                "/omni 重新登陆 角色名@世界名")
        ]
    };

    private const int CHARACTER_SELECT_TIMEOUT_MS = 30_000;
    private const int LOBBY_TRAVEL_SETTLE_DELAY_MS = 2_500;

    private readonly TaskHelper tasks = new()
    {
        RetryIntervalMS = 100,
        TimeoutMS = 180_000
    };
    private AddonEventRegistry? addonEvents;
    private AutoLoginTarget? manualTarget;
    private bool suspendedUntilLogin;
    private bool crossDataCenterTravelInProgress;

    public void SuspendUntilNextLogin()
    {
        suspendedUntilLogin = true;
        tasks.Abort();
        manualTarget = null;
    }

    public void BeginCrossDataCenterTravel()
    {
        crossDataCenterTravelInProgress = true;
        SuspendUntilNextLogin();
    }

    private void OnLogin()
    {
        crossDataCenterTravelInProgress = false;
        suspendedUntilLogin = false;
    }

    public bool TryRelog(AutoLoginTarget target)
    {
        if (!IsEnabled ||
            suspendedUntilLogin ||
            !GameState.IsLoggedIn ||
            GameState.ContentFinderCondition != 0 ||
            tasks.IsBusy ||
            !config.Targets.Any(candidate =>
                candidate.WorldID == target.WorldID &&
                candidate.CharacterName.Equals(target.CharacterName, StringComparison.Ordinal)))
            return false;

        var current = DService.Instance().ObjectTable.LocalPlayer;
        if (current is not null &&
            current.Name.ToString().Equals(target.CharacterName, StringComparison.Ordinal) &&
            DService.Instance().PlayerState.CurrentWorld.RowId == target.WorldID)
            return false;

        manualTarget = target;
        tasks.Abort();
        tasks.Enqueue(
            () =>
            {
                if (!GameState.IsLoggedIn)
                    return true;

                ChatManager.Instance().SendMessage("/logout");
                return true;
            },
            "Request logout");
        tasks.Enqueue(
            () => !GameState.IsLoggedIn || AddonSelectYesnoEvent.ClickYes(),
            "Confirm logout",
            timeoutMS: 30_000);
        return true;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        using var cellPadding = ImRaii.PushStyle(
            ImGuiStyleVar.CellPadding,
            new Vector2(ImGui.GetStyle().CellPadding.X, OmniTheme.Scale(2f)));
        var rowContentHeight = MathF.Max(
            OmniTheme.CheckboxSize(),
            MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
        var addLabel = OmniLoc.Get("Feature.AutoLogin.Add");
        var deleteLabel = OmniLoc.Get("Feature.AutoLogin.Delete");
        var addSize = OmniControls.CompactButtonSize(addLabel);
        var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
        var actionLabel = OmniLoc.Get("Feature.AutoLogin.Column.Actions");
        var actionWidth = MathF.Max(ImGui.CalcTextSize(actionLabel).X, MathF.Max(addSize.X, deleteSize.X));
        var scroll = config.Targets.Count > 5;
        var tableHeight = scroll
            ? (rowContentHeight + ImGui.GetStyle().CellPadding.Y * 2f) * 6f +
              ImGui.GetStyle().ChildBorderSize * 2f
            : 0f;
        var addTarget = false;

        var nameWidth = ImGui.GetFontSize() * 6f;
        var worldWidth = ImGui.GetFontSize() * 6f;
        foreach (var target in config.Targets)
        {
            nameWidth = MathF.Max(nameWidth, ImGui.CalcTextSize(target.CharacterName).X);
            worldWidth = MathF.Max(worldWidth, ImGui.CalcTextSize(LuminaWrapper.GetWorldName(target.WorldID)).X + ImGui.GetTextLineHeight());
        }
        using (var table = OmniControls.DataTable(
                   "##AutoLoginTargets",
                   ["##autoLoginEnabled", OmniLoc.Get("Feature.AutoLogin.Column.CharacterName"),
                       OmniLoc.Get("Feature.AutoLogin.Column.World"), actionLabel],
                   [OmniControls.MeasureCheckbox(string.Empty).X, nameWidth, worldWidth, actionWidth],
                   [OmniControls.MeasureCheckbox(string.Empty).X, nameWidth, worldWidth, actionWidth], out var detailLayout,
                   ImGuiTableFlags.Borders |
                   ImGuiTableFlags.RowBg |
                   (scroll ? ImGuiTableFlags.ScrollY : ImGuiTableFlags.None) |
                   ImGuiTableFlags.NoSavedSettings |
                   ImGuiTableFlags.SizingStretchProp,
                   2, new Vector2(ImGui.GetContentRegionAvail().X, tableHeight), stretchColumn: 1))
        {
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            OmniControls.BeginTableHeaderRow(rowContentHeight);
            ImGui.TableNextColumn();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
            var allEnabled = config.Targets.Count > 0 && config.Targets.All(static target => target.Enabled);
            using (ImRaii.Disabled(config.Targets.Count == 0))
            {
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                if (OmniControls.Checkbox("##autoLoginAllEnabled", ref allEnabled))
                {
                    foreach (var target in config.Targets)
                    {
                        target.Enabled = allEnabled;
                    }

                    changed = true;
                }
            }

            if (!detailLayout)
            {
                OmniControls.TableHeader(OmniLoc.Get("Feature.AutoLogin.Column.CharacterName"), rowContentHeight);
                OmniControls.TableHeader(OmniLoc.Get("Feature.AutoLogin.Column.World"), rowContentHeight);
                OmniControls.TableHeader(actionLabel, rowContentHeight);
            }

            var removeIndex = -1;
            for (var index = 0; index < config.Targets.Count; index++)
            {
                var target = config.Targets[index];
                using var targetID = ImRaii.PushId($"autoLoginTarget{index}");
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);

                ImGui.TableNextColumn();
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                var enabled = target.Enabled;
                if (OmniControls.Checkbox("##enabled", ref enabled))
                {
                    target.Enabled = enabled;
                    changed = true;
                }

                OmniControls.NextTableField(OmniLoc.Get("Feature.AutoLogin.Column.CharacterName"), detailLayout);
                if (detailLayout)
                    OmniControls.TableTextWrappedCentered(target.CharacterName);
                else
                    OmniControls.TableTextCentered(target.CharacterName, rowContentHeight);

                OmniControls.NextTableField(OmniLoc.Get("Feature.AutoLogin.Column.World"), detailLayout);
                using (var rented = new RentedSeStringBuilder())
                {
                    var icon = rented.Builder
                        .AppendIcon((uint)BitmapFontIcon.CrossWorld)
                        .ToReadOnlySeString();
                    var worldName = LuminaWrapper.GetWorldName(target.WorldID);
                    var textWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X - ImGui.GetTextLineHeight());
                    var textSize = ImGui.CalcTextSize(worldName, false, textWidth);
                    var worldHeight = MathF.Max(rowContentHeight, textSize.Y);
                    var worldStyle = new SeStringDrawParams
                    {
                        TargetDrawList = default(ImDrawListPtr),
                        ScreenOffset = Vector2.Zero,
                        Font = ImGui.GetFont(),
                        FontSize = ImGui.GetFontSize(),
                        WrapWidth = float.MaxValue
                    };
                    var iconSize = ImGuiHelpers.SeStringWrapped(icon, worldStyle).Size;
                    var worldPosition = ImGui.GetCursorScreenPos();
                    var groupWidth = iconSize.X + textSize.X;
                    var groupLeft = worldPosition.X +
                                    MathF.Max(0f, (ImGui.GetContentRegionAvail().X - groupWidth) * 0.5f);
                    var textTop = worldPosition.Y + (worldHeight - textSize.Y) * 0.5f;
                    var textCenter = textSize.Y * 0.5f;
                    if (worldName.Length > 0)
                    {
                        var font = ImGui.GetFont();
                        var glyph = font.FindGlyph(worldName[0]);
                        if (glyph is not null)
                            textCenter = (glyph->Y0 + glyph->Y1) * 0.5f * ImGui.GetFontSize() / font.FontSize;
                    }

                    worldStyle.TargetDrawList = ImGui.GetWindowDrawList();
                    worldStyle.ScreenOffset = new Vector2(
                        groupLeft, textTop + textCenter - iconSize.Y * 0.5f);
                    ImGuiHelpers.SeStringWrapped(icon, worldStyle);
                    ImGui.GetWindowDrawList().AddText(
                        ImGui.GetFont(), ImGui.GetFontSize(),
                        new Vector2(groupLeft + iconSize.X, textTop),
                        ImGui.GetColorU32(ImGuiCol.Text),
                        worldName, textWidth);
                    ImGui.Dummy(new Vector2(0f, worldHeight));
                }

                OmniControls.NextTableField(actionLabel, detailLayout);
                OmniControls.CenterTableItem(deleteSize, rowContentHeight);
                if (OmniControls.SmallButton($"{deleteLabel}##delete", false, deleteSize))
                {
                    removeIndex = index;
                }

            }

            if (removeIndex >= 0)
            {
                config.Targets.RemoveAt(removeIndex);
                changed = true;
            }

            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
            ImGui.TableSetColumnIndex(detailLayout ? 0 : 3);
            OmniControls.CenterTableItem(addSize, rowContentHeight);
            addTarget = OmniControls.SmallButton($"{addLabel}##add", false, addSize);
        }

        if (addTarget)
        {
            changed |= TryAddTarget();
        }

        return changed;
    }

    public override bool TryHandleCommand(string command, string arguments)
    {
        if (!TryParseTarget(arguments, out var target))
        {
            return true;
        }

        TryRelog(target);
        return true;
    }

    protected override void OnEnable()
    {
        if (GameState.IsLoggedIn)
            suspendedUntilLogin = false;

        tasks.TimeoutAction = () => manualTarget = null;
        DService.Instance().ClientState.Login += OnLogin;
        addonEvents = new(DalamudServices.AddonLifecycle);
        addonEvents.Register(AddonEvent.PostSetup, "LobbyDKT", OnLobbyTravel);
        addonEvents.Register(AddonEvent.PostSetup, "_TitleMenu", OnTitleMenu);
        OnTitleMenu(AddonEvent.PostSetup, null);
    }

    protected override void OnDisable()
    {
        addonEvents?.Dispose();
        addonEvents = null;
        DService.Instance().ClientState.Login -= OnLogin;
        tasks.Abort();
        manualTarget = null;
        crossDataCenterTravelInProgress = false;
    }

    protected override bool OnInterruptAutomation()
    {
        if (!tasks.IsBusy)
        {
            return false;
        }

        SuspendUntilNextLogin();
        return true;
    }

    protected override void OnDispose()
    {
        tasks.Dispose();
    }

    private void OnLobbyTravel(AddonEvent eventType, AddonArgs args) => SuspendUntilNextLogin();

    private void OnTitleMenu(AddonEvent eventType, AddonArgs? args)
    {
        if (suspendedUntilLogin || crossDataCenterTravelInProgress)
        {
            return;
        }

        var lobbyTravelReady = IsLobbyTravelReady();
        var hasTarget = config.Targets.Any(target => target.Enabled);
        if (GameState.IsLoggedIn || tasks.IsBusy || lobbyTravelReady || !hasTarget)
        {
            return;
        }

        tasks.Abort();
        tasks.Enqueue(
            () =>
            {
                tasks.DelayNext(LOBBY_TRAVEL_SETTLE_DELAY_MS);
                return true;
            },
            "Wait for lobby travel state");
        tasks.Enqueue(
            () =>
            {
                if (IsLobbyTravelReady())
                {
                    return true;
                }

                if (GameState.IsLoggedIn)
                {
                    tasks.Abort();
                    return true;
                }

                if (AddonHelper.TryGetByName("_CharaSelectListMenu", out AtkUnitBase* characterSelect) &&
                    characterSelect->IsAddonAndNodesReady())
                    return true;

                if (!AddonHelper.TryGetByName("_TitleMenu", out AtkUnitBase* titleMenu) ||
                    !titleMenu->IsAddonAndNodesReady())
                    return false;

                AgentLobbyEvent.OpenCharacterSelect();
                return true;
            },
            "Open character selection");
        tasks.Enqueue(
            () =>
            {
                if (IsLobbyTravelReady())
                {
                    return true;
                }

                if (!AddonHelper.TryGetByName("_CharaSelectListMenu", out AtkUnitBase* addon) ||
                    !addon->IsAddonAndNodesReady())
                    return false;

                return true;
            },
            "Wait for character selection",
            timeoutMS: CHARACTER_SELECT_TIMEOUT_MS);
        tasks.Enqueue(
            () =>
            {
                if (IsLobbyTravelReady())
                {
                    return true;
                }

                if (!TryGetLoginTarget(out var target))
                {
                    return true;
                }

                var agent = AgentLobby.Instance();
                if (agent == null)
                {
                    return false;
                }

                var client = agent->LobbyData.LobbyUIClient;
                for (var index = 0; index < client.CurrentDataCenterCharacters.Count; index++)
                {
                    var entry = client.CurrentDataCenterCharacters[index];
                    if (entry.HomeWorldId != target.WorldID ||
                        !entry.NameString.Equals(target.CharacterName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var worldID = entry.LoginFlags == CharaSelectCharacterEntryLoginFlags.DCTraveling
                        ? entry.CurrentWorldId
                        : entry.HomeWorldId;
                    tasks.Enqueue(
                        () =>
                        {
                            if (IsLobbyTravelReady())
                            {
                                return true;
                            }

                            return AgentLobbyEvent.SelectWorldByID(worldID);
                        },
                        "Select target world");
                    tasks.Enqueue(
                        () =>
                        {
                            if (IsLobbyTravelReady())
                            {
                                return true;
                            }

                            var currentAgent = AgentLobby.Instance();
                            return currentAgent != null && currentAgent->WorldId == worldID;
                        },
                        "Wait for target world",
                        timeoutMS: CHARACTER_SELECT_TIMEOUT_MS);
                    tasks.Enqueue(
                        () =>
                        {
                            if (IsLobbyTravelReady())
                            {
                                return true;
                            }

                            return AgentLobbyEvent.SelectCharacter(character => character.ContentId == entry.ContentId);
                        },
                        "Select target character",
                        timeoutMS: CHARACTER_SELECT_TIMEOUT_MS);
                    tasks.Enqueue(
                        () =>
                        {
                            if (!GameState.IsLoggedIn)
                                return false;

                            manualTarget = null;
                            return true;
                        },
                        "Wait for login",
                        timeoutMS: 180_000);
                    return true;
                }

                manualTarget = null;
                return true;
            },
            "Select target character",
            timeoutMS: CHARACTER_SELECT_TIMEOUT_MS);
    }

    private bool IsLobbyTravelReady()
    {
        if (suspendedUntilLogin)
            return true;

        if (!Addons.LobbyDKT->IsAddonAndNodesReady())
            return false;

        SuspendUntilNextLogin();
        return true;
    }

    private bool TryAddTarget()
    {
        var localPlayer = DService.Instance().ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return false;
        }

        var target = new AutoLoginTarget(
            DService.Instance().PlayerState.CurrentWorld.RowId,
            localPlayer.Name.ToString());
        if (config.Targets.Contains(target))
        {
            return false;
        }

        config.Targets.Add(target);
        saveConfig();
        return true;
    }

    private bool TryGetLoginTarget(out AutoLoginTarget target)
    {
        target = manualTarget ?? config.Targets.FirstOrDefault(target => target.Enabled)!;
        return target is not null;
    }

    private static bool TryParseTarget(string text, out AutoLoginTarget target)
    {
        target = null!;
        var separator = text.LastIndexOf('@');
        if (separator <= 0 || separator == text.Length - 1)
        {
            return false;
        }

        var characterName = text[..separator].Trim();
        var worldName = text[(separator + 1)..].Trim();
        var world = Sheets.Worlds.Values.FirstOrDefault(
            value => value.Name.ToString().Equals(worldName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(characterName) || world.RowId == 0)
        {
            return false;
        }

        target = new(world.RowId, characterName);
        return true;
    }

}

public sealed class AutoLoginConfig
{
    public List<AutoLoginTarget> Targets { get; set; } = [];
}

public sealed record AutoLoginTarget(uint WorldID, string CharacterName)
{
    public bool Enabled { get; set; } = true;
}
