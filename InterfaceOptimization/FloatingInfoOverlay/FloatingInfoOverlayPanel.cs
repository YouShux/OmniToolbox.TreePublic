using System.Globalization;
using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class FloatingInfoOverlayPanel(FloatingInfoOverlayConfig config)
{
    private const int VisibleFilterRows = 3;

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
                    config.EnableDataIdFilter,
                    value => config.EnableDataIdFilter = value);
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(!config.EnableDataIdFilter))
                {
                    changed |= DrawCheckbox(
                        "Feature.FloatingInfoOverlay.Whitelist",
                        "whitelist",
                        config.UseDataIdWhitelist,
                        value => config.UseDataIdWhitelist = value);
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
                    changed |= TryAddFilterDataId();
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
                    config.ShowDataId,
                    value => config.ShowDataId = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowEntityId",
                    "entityId",
                    config.ShowEntityId,
                    value => config.ShowEntityId = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowDecimalId",
                    "decimalId",
                    config.ShowDecimalId,
                    value => config.ShowDecimalId = value);
                ImGui.TableNextColumn();
                changed |= DrawCheckbox(
                    "Feature.FloatingInfoOverlay.ShowHexId",
                    "hexId",
                    config.ShowHexId,
                    value => config.ShowHexId = value);
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
        using var disabled = ImRaii.Disabled(!config.EnableDataIdFilter);
        var style = ImGui.GetStyle();
        var removeLabel = OmniLoc.Get("Feature.FloatingInfoOverlay.Remove");
        var removeButtonSize = OmniControls.CompactButtonSize(removeLabel);
        var tableHeight = MathF.Max(
                              OmniTheme.SmallButtonSize().Y,
                              ImGui.GetTextLineHeight() + style.CellPadding.Y * 2f) +
                          VisibleFilterRows * (removeButtonSize.Y + style.CellPadding.Y * 2f) +
                          OmniTheme.Scale(2f);
        using var table = ImRaii.Table(
            "##floatingInfoDataIds",
            4,
            ImGuiTableFlags.SizingStretchSame |
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY,
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
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow();
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.ShowDataId"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.DecimalId"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.Hex"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.FloatingInfoOverlay.Action"));
        if (config.FilterDataIds.Count == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(
                OmniLoc.Get("Feature.FloatingInfoOverlay.FilterEmpty"),
                color: ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            return false;
        }

        var removeIndex = -1;
        for (var index = 0; index < config.FilterDataIds.Count; index++)
        {
            var dataId = config.FilterDataIds[index];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(OmniLoc.Get("Feature.FloatingInfoOverlay.ShowDataId"));
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(dataId.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered($"0x{dataId:X8}");
            ImGui.TableNextColumn();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(
                0f,
                (ImGui.GetContentRegionAvail().X - removeButtonSize.X) * 0.5f));
            if (OmniControls.SmallButton(
                    $"{removeLabel}##remove{dataId}",
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
        var changed = OmniControls.SliderFloat(
            $"##floatingInfo{id}",
            ref value,
            minimum,
            maximum,
            format,
            width);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
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
        var changed = OmniControls.SliderInt(
            $"##floatingInfo{id}",
            ref value,
            minimum,
            maximum,
            "%d",
            width);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        return changed;
    }

    private bool TryAddFilterDataId()
    {
        if (!TryParseDataId(filterInput, out var dataId))
        {
            return false;
        }

        if (config.FilterDataIds.Contains(dataId))
        {
            return false;
        }

        config.FilterDataIds.Add(dataId);
        filterInput = string.Empty;
        return true;
    }

    private static bool TryParseDataId(string text, out uint dataId)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(
                text.AsSpan(2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out dataId);
        }

        return uint.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out dataId);
    }
}
