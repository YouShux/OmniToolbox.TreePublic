using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed class DynamicBattleEffects(DynamicBattleEffects.Settings config) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Combat,
        RequiresPrivateProvider = true
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "DynamicBattleEffectsTitle",
            ["Description"] = "DynamicBattleEffectsDescription",
            ["Feature.DynamicBattleEffects.DutyType"] = "Feature.DynamicBattleEffects.DutyType",
            ["Feature.DynamicBattleEffects.EightPlayers"] = "Feature.DynamicBattleEffects.EightPlayers",
            ["Feature.DynamicBattleEffects.FourPlayers"] = "Feature.DynamicBattleEffects.FourPlayers",
            ["Feature.DynamicBattleEffects.LargeDuty"] = "Feature.DynamicBattleEffects.LargeDuty",
            ["Feature.DynamicBattleEffects.TwentyFourPlayers"] = "Feature.DynamicBattleEffects.TwentyFourPlayers"
        });

    public override bool HasSettings => true;

    private static readonly string[] ConfigKeys =
        ["BattleEffectSelf", "BattleEffectParty", "BattleEffectOther", "BattleEffectPvPEnemyPc"];
    private static readonly uint[] TargetTextIDs = [4087, 4088, 4089, 4109];

    private uint[]? originalValues;
    private uint[]? appliedValues;
    private Profile? appliedProfile;
    private uint appliedDutyID;
    private ulong originalContentID;

    protected override void OnStart(FeatureLifetime activation)
    {
        activation.OnStopping(() => FrameworkManager.Instance().Unreg(OnUpdate));
        activation.OnStopping(() => DService.Instance().ClientState.Logout -= OnLogout);

        if (!FrameworkManager.Instance().Reg(OnUpdate, 500))
        {
            throw new InvalidOperationException("Dynamic battle effects update registration failed.");
        }

        DService.Instance().ClientState.Logout += OnLogout;
    }

    protected override void OnStop()
    {
        RestoreOriginal();
    }

    public override bool DrawSettings()
    {
        string[] labels =
        [
            OmniLoc.Get(this, "Feature.DynamicBattleEffects.DutyType"),
            LuminaWrapper.GetAddonText(TargetTextIDs[0]),
            LuminaWrapper.GetAddonText(TargetTextIDs[1]),
            LuminaWrapper.GetAddonText(TargetTextIDs[2]),
            LuminaWrapper.GetAddonText(TargetTextIDs[3])
        ];
        var comboWidth = OmniControls.MeasureCombo(GetModeText(DisplayMode.All)).X;
        var tableWidth = ImGui.GetContentRegionAvail().X;
        Span<float> widths = stackalloc float[labels.Length];
        Span<float> desiredWidths = stackalloc float[labels.Length];
        widths[0] = MathF.Ceiling(MathF.Max(OmniControls.MeasureCheckbox(labels[0]).X,
            OmniControls.MeasureCheckbox(OmniLoc.Get(this, "Feature.DynamicBattleEffects.LargeDuty")).X));
        desiredWidths[0] = widths[0];
        var modeWidth = (tableWidth - widths[0] - ImGui.GetStyle().CellPadding.X * labels.Length * 2f -
            labels.Length - 1f) / TargetTextIDs.Length;
        for (var index = 1; index < labels.Length; index++)
        {
            widths[index] = MathF.Ceiling(MathF.Max(comboWidth, OmniControls.MeasureTableHeader(labels[index]).X));
            desiredWidths[index] = MathF.Max(widths[index], modeWidth);
        }

        using var table = OmniControls.DataTable("##dynamicBattleEffects", labels, widths, desiredWidths,
            out var detailLayout, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable,
            new Vector2(tableWidth, 0f), stretchColumn: 4);
        if (!table)
        {
            return false;
        }

        OmniControls.BeginTableHeaderRow();
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        var allEnabled = config.FourPlayers.Enabled && config.EightPlayers.Enabled &&
            config.TwentyFourPlayers.Enabled && config.LargeDuty.Enabled;
        var changed = OmniControls.Checkbox($"{labels[0]}##allDutyTypes", ref allEnabled);
        if (changed)
        {
            config.FourPlayers.Enabled = allEnabled;
            config.EightPlayers.Enabled = allEnabled;
            config.TwentyFourPlayers.Enabled = allEnabled;
            config.LargeDuty.Enabled = allEnabled;
        }

        if (!detailLayout)
        {
            for (var index = 1; index < labels.Length; index++)
            {
                OmniControls.TableHeader(labels[index]);
            }
        }

        changed |= DrawProfile("Feature.DynamicBattleEffects.FourPlayers", config.FourPlayers, labels, detailLayout);
        changed |= DrawProfile("Feature.DynamicBattleEffects.EightPlayers", config.EightPlayers, labels, detailLayout);
        changed |= DrawProfile("Feature.DynamicBattleEffects.TwentyFourPlayers", config.TwentyFourPlayers, labels, detailLayout);
        changed |= DrawProfile("Feature.DynamicBattleEffects.LargeDuty", config.LargeDuty, labels, detailLayout);
        if (changed && IsEnabled)
        {
            Refresh(true);
        }

        return changed;
    }

    private static bool DrawProfile(string labelKey, Profile profile, ReadOnlySpan<string> labels, bool detailLayout)
    {
        using var id = ImRaii.PushId(labelKey);
        ImGui.TableNextRow();
        OmniControls.NextTableField(labels[0], detailLayout);
        var changed = OmniControls.Checkbox(OmniLoc.Get(labelKey), ref profile.Enabled);
        using var disabled = ImRaii.Disabled(!profile.Enabled);
        changed |= DrawMode(0, ref profile.Self, labels[1], detailLayout);
        changed |= DrawMode(1, ref profile.Party, labels[2], detailLayout);
        changed |= DrawMode(2, ref profile.Other, labels[3], detailLayout);
        changed |= DrawMode(3, ref profile.Enemy, labels[4], detailLayout);
        return changed;
    }

    private static bool DrawMode(int index, ref DisplayMode mode, string label, bool detailLayout)
    {
        OmniControls.NextTableField(label, detailLayout);
        using var combo = OmniControls.Combo($"##mode{index}", GetModeText(mode), ImGui.GetContentRegionAvail().X);
        if (!combo)
        {
            return false;
        }

        var changed = false;
        for (var value = DisplayMode.All; value <= DisplayMode.None; value++)
        {
            if (ImGui.Selectable(GetModeText(value), mode == value))
            {
                mode = value;
                changed = true;
            }
        }

        return changed;
    }

    private void OnUpdate(IFramework _) => Refresh();

    private void OnLogout(int logoutType, int logoutCode) => RestoreOriginal();

    private void Refresh(bool force = false)
    {
        var services = DService.Instance();
        if (!GameState.IsLoggedIn || services.Condition.IsBetweenAreas)
        {
            return;
        }

        var contentID = DalamudServices.PlayerState.ContentId;
        if (originalValues is not null && originalContentID != contentID)
        {
            originalValues = null;
            appliedValues = null;
            appliedProfile = null;
        }

        Profile? profile = null;
        var dutyID = GameState.ContentFinderCondition;
        if (dutyID != 0 &&
            LuminaGetter.TryGetRow<ContentFinderCondition>(dutyID, out var duty) &&
            duty.ContentMemberType.ValueNullable is { } members)
        {
            profile = (members.MembersPerParty * members.PartyCount) switch
            {
                4 => config.FourPlayers,
                8 => config.EightPlayers,
                24 => config.TwentyFourPlayers,
                > 24 => config.LargeDuty,
                _ => null
            };
        }

        if (profile is not { Enabled: true })
        {
            RestoreOriginal();
            return;
        }

        if (!force && dutyID == appliedDutyID && ReferenceEquals(profile, appliedProfile))
        {
            return;
        }

        var uiConfig = services.GameConfig.UiConfig;
        var currentValues = new uint[ConfigKeys.Length];
        for (var index = 0; index < ConfigKeys.Length; index++)
        {
            if (!uiConfig.TryGetUInt(ConfigKeys[index], out currentValues[index]))
            {
                return;
            }
        }

        originalValues ??= currentValues;
        originalContentID = contentID;
        appliedValues ??= (uint[])currentValues.Clone();
        DisplayMode[] modes = [profile.Self, profile.Party, profile.Other, profile.Enemy];
        for (var index = 0; index < ConfigKeys.Length; index++)
        {
            var value = modes[index] is >= DisplayMode.All and <= DisplayMode.None ? (uint)modes[index] : 0u;
            if (currentValues[index] != value)
            {
                uiConfig.Set(ConfigKeys[index], value);
            }

            appliedValues[index] = value;
        }

        appliedProfile = profile;
        appliedDutyID = dutyID;
    }

    private void RestoreOriginal()
    {
        if (originalValues is null || appliedValues is null)
        {
            return;
        }

        var services = DService.Instance();
        if (!services.ClientState.IsLoggedIn || DalamudServices.PlayerState.ContentId == originalContentID)
        {
            var uiConfig = services.GameConfig.UiConfig;
            for (var index = 0; index < ConfigKeys.Length; index++)
            {
                if (uiConfig.TryGetUInt(ConfigKeys[index], out var current) &&
                    current == appliedValues[index] && current != originalValues[index])
                {
                    uiConfig.Set(ConfigKeys[index], originalValues[index]);
                }
            }
        }

        originalValues = null;
        appliedValues = null;
        appliedProfile = null;
        appliedDutyID = 0;
        originalContentID = 0;
    }

    private static string GetModeText(DisplayMode mode) => LuminaWrapper.GetAddonText(mode switch
    {
        DisplayMode.Limited => 7824,
        DisplayMode.None => 7825,
        _ => 7823
    });

    public sealed class Settings
    {
        public Profile FourPlayers = new();
        public Profile EightPlayers = new();
        public Profile TwentyFourPlayers = new();
        public Profile LargeDuty = new();
    }

    public sealed class Profile
    {
        public bool Enabled;
        public DisplayMode Self;
        public DisplayMode Party = DisplayMode.Limited;
        public DisplayMode Other = DisplayMode.Limited;
        public DisplayMode Enemy;
    }

    public enum DisplayMode : uint
    {
        All,
        Limited,
        None
    }
}
