using System.Globalization;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.TreeHouse;
using OmenTools.ImGuiOm;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed class FloatingInfoOverlay(
    FloatingInfoOverlayConfig config,
    NonEntityTargetVisibility targetVisibility) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("FloatingInfoOverlayTitle"),
        Description = OmniLoc.Get("FloatingInfoOverlayDescription"),
        Category = ModuleCategory.Debug,
        RequiresPrivateProvider = true
    };

    private readonly FloatingInfoOverlayPanel panel = new(config);
    private FeatureLifetime? runtimeLifetime;

    public override bool HasSettings => true;

    public override bool DrawSettings() => panel.Draw();

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            var state = new FloatingInfoOverlayState(config, targetVisibility);
            var nativeUI = new FloatingInfoOverlayNativeUI(config, state);
            lifetime.Add(state.Dispose);
            if (!FrameworkManager.Instance().Reg(state.Update))
            {
                throw new InvalidOperationException("Floating info overlay update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(state.Update));
            DalamudServices.PluginInterface.UiBuilder.Draw += nativeUI.Draw;
            lifetime.Add(() => DalamudServices.PluginInterface.UiBuilder.Draw -= nativeUI.Draw);
            runtimeLifetime = lifetime;
            state.Update(DalamudServices.Framework);
        }
        catch
        {
            runtimeLifetime = null;
            lifetime.Dispose();
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        lifetime?.Dispose();
    }
}

[Serializable]
public sealed class FloatingInfoOverlayConfig
{
    public float Opacity { get; set; } = 0.85f;

    public float Range { get; set; } = 50f;

    public int MaxObjects { get; set; } = 20;

    public float MergeDistance { get; set; } = 2f;

    public float Scale { get; set; } = 1f;

    public bool OnlyCasting { get; set; }

    public bool ShowPlayers { get; set; } = true;

    public bool ShowLocalPlayer { get; set; }

    public bool ShowBattleNpcs { get; set; } = true;

    public bool ShowEventNpcs { get; set; } = true;

    public bool ShowEventObjects { get; set; } = true;

    public bool ShowCompanions { get; set; } = true;

    public bool ShowNonEntityTargets { get; set; }

    public bool ShowEntityID { get; set; } = true;

    public bool ShowDataID { get; set; } = true;

    public bool ShowDecimalID { get; set; }

    public bool ShowHexID { get; set; }

    public bool ShowDistance { get; set; } = true;

    public bool ShowPosition { get; set; } = true;

    public bool ShowRotation { get; set; } = true;

    public bool ShowHealth { get; set; } = true;

    public bool ShowMana { get; set; } = true;

    public bool ShowMarker { get; set; } = true;

    public bool ShowCastInfo { get; set; } = true;

    public bool ShowStatusList { get; set; } = true;

    public bool EnableDataIDFilter { get; set; }

    public bool UseDataIDWhitelist { get; set; } = true;

    public List<uint> FilterDataIds { get; set; } = [];
}

internal sealed class FloatingInfoOverlayPanel(FloatingInfoOverlayConfig config)
{
    private string filterInput = string.Empty;

    public bool Draw()
    {
        var changed = false;
        ImGui.TextUnformatted($"{OmniLoc.Get("Feature.FloatingInfoOverlay.General")}：");
        changed |= DrawGeneralSettings();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted($"{OmniLoc.Get("Feature.FloatingInfoOverlay.ObjectTypes")}：");
        changed |= DrawObjectTypes();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted($"{OmniLoc.Get("Feature.FloatingInfoOverlay.DisplayOptions")}：");
        changed |= DrawDisplayOptions();

        return changed;
    }

    private bool DrawGeneralSettings()
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##floatingInfoGeneral",
                   2,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var opacity = config.Opacity;
                if (DrawFloatSlider(
                        "Feature.FloatingInfoOverlay.Opacity",
                        "opacity",
                        ref opacity,
                        0.1f,
                        1f,
                        "%.2f"))
                {
                    config.Opacity = opacity;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var range = config.Range;
                if (DrawFloatSlider(
                        "Feature.FloatingInfoOverlay.Range",
                        "range",
                        ref range,
                        5f,
                        200f,
                        "%.0fm"))
                {
                    config.Range = range;
                    changed = true;
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var maxObjects = config.MaxObjects;
                if (DrawIntSlider(
                        "Feature.FloatingInfoOverlay.MaxObjects",
                        "maxObjects",
                        ref maxObjects,
                        1,
                        100))
                {
                    config.MaxObjects = maxObjects;
                    changed = true;
                }

                ImGui.TableNextColumn();
                var mergeDistance = config.MergeDistance;
                if (DrawFloatSlider(
                        "Feature.FloatingInfoOverlay.MergeDistance",
                        "mergeDistance",
                        ref mergeDistance,
                        0f,
                        50f,
                        "%.0fm"))
                {
                    config.MergeDistance = mergeDistance;
                    changed = true;
                }

                ImGuiOm.HelpMarker(OmniLoc.Get("Feature.FloatingInfoOverlay.MergeDistance.Help"));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var scale = config.Scale;
                if (DrawFloatSlider(
                        "Feature.FloatingInfoOverlay.Scale",
                        "scale",
                        ref scale,
                        0.3f,
                        3f,
                        "%.2f"))
                {
                    config.Scale = scale;
                    changed = true;
                }
            }
        }

        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
        var addLabel = OmniLoc.Get("Feature.FloatingInfoOverlay.Add");
        var addButtonSize = OmniControls.CompactButtonSize(addLabel);
        using (var table = ImRaii.Table(
                   "##floatingInfoFilters",
                   5,
                   ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableSetupColumn("##onlyCasting", ImGuiTableColumnFlags.WidthStretch, 1.05f);
                ImGui.TableSetupColumn("##enableFilter", ImGuiTableColumnFlags.WidthStretch, 1.2f);
                ImGui.TableSetupColumn("##whitelist", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("##filterInput", ImGuiTableColumnFlags.WidthStretch, 1.4f);
                ImGui.TableSetupColumn("##add", ImGuiTableColumnFlags.WidthFixed, addButtonSize.X);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.OnlyCasting",
                    "onlyCasting",
                    config.OnlyCasting,
                    value => config.OnlyCasting = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.EnableDataIdFilter",
                    "enableFilter",
                    config.EnableDataIDFilter,
                    value => config.EnableDataIDFilter = value);
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(!config.EnableDataIDFilter))
                {
                    changed |= DrawCheckbox(
                        "Feature.FloatingInfoOverlay.Whitelist",
                        "whitelist",
                        config.UseDataIDWhitelist,
                        value => config.UseDataIDWhitelist = value);
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                OmniControls.InputTextWithHint(
                    "##floatingInfoDataIdInput",
                    OmniLoc.Get("Feature.FloatingInfoOverlay.FilterInput"),
                    ref filterInput,
                    32);
                ImGui.TableNextColumn();
                if (OmniControls.SmallButton(
                        $"{addLabel}##addDataId",
                        false,
                        addButtonSize))
                {
                    changed |= TryAddFilterDataID();
                }
            }
        }

        return DrawFilterTable() || changed;
    }

    private bool DrawObjectTypes()
    {
        var changed = false;
        using var table = ImRaii.Table(
            "##floatingInfoObjectTypes",
            4,
            ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowPlayers",
            "players",
            config.ShowPlayers,
            value => config.ShowPlayers = value);
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowLocalPlayer",
            "localPlayer",
            config.ShowLocalPlayer,
            value => config.ShowLocalPlayer = value);
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowBattleNpcs",
            "battleNpcs",
            config.ShowBattleNpcs,
            value => config.ShowBattleNpcs = value);
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowEventNpcs",
            "eventNpcs",
            config.ShowEventNpcs,
            value => config.ShowEventNpcs = value);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowEventObjects",
            "eventObjects",
            config.ShowEventObjects,
            value => config.ShowEventObjects = value);
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowCompanions",
            "companions",
            config.ShowCompanions,
            value => config.ShowCompanions = value);
        ImGui.TableNextColumn();
        changed |= DrawCheckbox(
            "Feature.FloatingInfoOverlay.ShowNonEntityTargets",
            "nonEntityTargets",
            config.ShowNonEntityTargets,
            value => config.ShowNonEntityTargets = value);
        return changed;
    }

    private bool DrawDisplayOptions()
    {
        var changed = false;
        using (var table = ImRaii.Table(
                   "##floatingInfoDisplayOptions",
                   4,
                   ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX,
                   new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            if (table)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowDataId",
                    "dataId",
                    config.ShowDataID,
                    value => config.ShowDataID = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowEntityId",
                    "entityId",
                    config.ShowEntityID,
                    value => config.ShowEntityID = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowDecimalId",
                    "decimalId",
                    config.ShowDecimalID,
                    value => config.ShowDecimalID = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowHexId",
                    "hexId",
                    config.ShowHexID,
                    value => config.ShowHexID = value);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowDistance",
                    "distance",
                    config.ShowDistance,
                    value => config.ShowDistance = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowPosition",
                    "position",
                    config.ShowPosition,
                    value => config.ShowPosition = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowRotation",
                    "rotation",
                    config.ShowRotation,
                    value => config.ShowRotation = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowHealth",
                    "health",
                    config.ShowHealth,
                    value => config.ShowHealth = value);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowMana",
                    "mana",
                    config.ShowMana,
                    value => config.ShowMana = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowMarker",
                    "marker",
                    config.ShowMarker,
                    value => config.ShowMarker = value);
                ImGuiOm.HelpMarker(OmniLoc.Get("Feature.FloatingInfoOverlay.ShowMarker.Help"));
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowCastInfo",
                    "castInfo",
                    config.ShowCastInfo,
                    value => config.ShowCastInfo = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowStatusList",
                    "statusList",
                    config.ShowStatusList,
                    value => config.ShowStatusList = value);
            }
        }

        return changed;
    }

    private bool DrawFilterTable()
    {
        var changed = false;
        using var disabled = ImRaii.Disabled(!config.EnableDataIDFilter);
        var style = ImGui.GetStyle();
        var removeLabel = OmniLoc.Get("Feature.FloatingInfoOverlay.Remove");
        var removeButtonSize = OmniControls.CompactButtonSize(removeLabel);
        var rowHeight = removeButtonSize.Y + style.CellPadding.Y * 2f;
        var scrollable = config.FilterDataIds.Count > 3;
        var tableHeight = MathF.Max(
                              OmniTheme.SmallButtonSize().Y,
                              ImGui.GetTextLineHeight() + style.CellPadding.Y * 2f) +
                          Math.Clamp(config.FilterDataIds.Count, 1, 3) *
                          rowHeight +
                          style.ItemSpacing.Y;
        using var table = ImRaii.Table(
            "##floatingInfoDataIds",
            4,
            ImGuiTableFlags.SizingStretchSame |
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            (scrollable ? ImGuiTableFlags.ScrollY : ImGuiTableFlags.None),
            new Vector2(ImGui.GetContentRegionAvail().X, tableHeight));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##dataId", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##decimalId", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##hexId", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn(
            "##action",
            ImGuiTableColumnFlags.WidthFixed,
            removeButtonSize.X + style.CellPadding.X * 2f);
        if (scrollable)
        {
            ImGui.TableSetupScrollFreeze(0, 1);
        }

        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.ShowDataId"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.DecimalId"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.Hex"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.Action"));
        if (config.FilterDataIds.Count == 0)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            for (var column = 0; column < 4; column++)
            {
                ImGui.TableNextColumn();
                OmniControls.TableTextCentered("-");
            }

            return false;
        }

        var removeIndex = -1;
        for (var index = 0; index < config.FilterDataIds.Count; index++)
        {
            var dataID = config.FilterDataIds[index];
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(dataID.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(dataID.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered($"0x{dataID:X8}");
            ImGui.TableNextColumn();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(
                0f,
                (ImGui.GetContentRegionAvail().X - removeButtonSize.X) * 0.5f));
            if (OmniControls.SmallButton(
                    $"{removeLabel}##remove{dataID}",
                    false,
                    removeButtonSize))
            {
                removeIndex = index;
            }
        }

        if (removeIndex >= 0)
        {
            config.FilterDataIds.RemoveAt(removeIndex);
            changed = true;
        }

        return changed;
    }

    private static bool DrawCheckbox(
        string key,
        string id,
        bool value,
        Action<bool> setValue)
    {
        if (!OmniControls.Checkbox($"{OmniLoc.Get(key)}##floatingInfo{id}", ref value))
        {
            return false;
        }

        setValue(value);
        return true;
    }

    private static bool DrawFloatSlider(
        string key,
        string id,
        ref float value,
        float minimum,
        float maximum,
        string format)
    {
        var label = OmniLoc.Get(key);
        var width = MathF.Max(
            1f,
            MathF.Min(
                OmniTheme.Scale(260f),
                ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X - ImGui.GetStyle().ItemSpacing.X));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        var changed = OmniControls.SliderFloat(
            $"##floatingInfo{id}",
            ref value,
            minimum,
            maximum,
            format,
            width);
        return changed;
    }

    private static bool DrawIntSlider(
        string key,
        string id,
        ref int value,
        int minimum,
        int maximum)
    {
        var label = OmniLoc.Get(key);
        var width = MathF.Max(
            1f,
            MathF.Min(
                OmniTheme.Scale(260f),
                ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X - ImGui.GetStyle().ItemSpacing.X));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        var changed = OmniControls.SliderInt(
            $"##floatingInfo{id}",
            ref value,
            minimum,
            maximum,
            "%d",
            width);
        return changed;
    }

    private bool TryAddFilterDataID()
    {
        if (!TryParseDataID(filterInput, out var dataID))
        {
            return false;
        }

        if (config.FilterDataIds.Contains(dataID))
        {
            return false;
        }

        config.FilterDataIds.Add(dataID);
        filterInput = string.Empty;
        return true;
    }

    private static bool TryParseDataID(string text, out uint dataID)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(
                text.AsSpan(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out dataID);
        }

        return uint.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out dataID);
    }
}
