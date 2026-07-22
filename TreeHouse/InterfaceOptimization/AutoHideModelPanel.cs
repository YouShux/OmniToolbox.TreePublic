using OmniToolbox.Config;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal static class AutoHideModelPanel
{
    public static bool Draw(AutoHideModelConfig config)
    {
        var changed = DrawVisibilityTable(config);
        ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(6f)));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.IncludeSelf",
            "includeSelf",
            config.IncludeSelf,
            value => config.IncludeSelf = value);
        ImGui.SameLine(0f, OmniTheme.Scale(16f));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.HideGroundHealingEffects",
            "groundEffects",
            config.HideGroundHealingEffects,
            value => config.HideGroundHealingEffects = value);
        ImGui.SameLine(0f, OmniTheme.Scale(16f));
        changed |= DrawCheckbox(
            "Feature.AutoHideModel.ShowTargetOfTarget",
            "targetOfTarget",
            config.ShowTargetOfTarget,
            value => config.ShowTargetOfTarget = value);
        return changed;
    }

    private static bool DrawVisibilityTable(AutoHideModelConfig config)
    {
        using var table = ImRaii.Table(
            "##autoHideModelSettings",
            8,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("##hideAll");
        ImGui.TableSetupColumn("##combat");
        ImGui.TableSetupColumn("##party");
        ImGui.TableSetupColumn("##friend");
        ImGui.TableSetupColumn("##company");
        ImGui.TableSetupColumn("##dead");
        ImGui.TableSetupColumn("##npc");
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        DrawHeader(string.Empty);
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideAll"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideInCombat"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowParty"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowFriends"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowFreeCompany"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.ShowDead"));
        DrawHeader(OmniLoc.Get("Feature.AutoHideModel.HideUnimportantNpcs"));

        var changed = DrawUnitRow(
            "Feature.AutoHideModel.Players",
            "players",
            config.Players,
            true,
            config.HideUnimportantNpcs,
            value => config.HideUnimportantNpcs = value);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Pets",
            "pets",
            config.Pets,
            false,
            false,
            null);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Chocobos",
            "chocobos",
            config.Chocobos,
            false,
            false,
            null);
        changed |= DrawUnitRow(
            "Feature.AutoHideModel.Minions",
            "minions",
            config.Minions,
            false,
            false,
            null);
        return changed;
    }

    private static bool DrawUnitRow(
        string labelKey,
        string id,
        AutoHideUnitConfig config,
        bool showDeadOption,
        bool hideUnimportantNpcs,
        Action<bool>? setHideUnimportantNpcs)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(OmniLoc.Get(labelKey));
        var changed = DrawCenteredCheckbox(
            $"{id}HideAll",
            config.HideAll,
            value => config.HideAll = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Combat",
            config.HideInCombat,
            value => config.HideInCombat = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Party",
            config.ShowParty,
            value => config.ShowParty = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Friends",
            config.ShowFriends,
            value => config.ShowFriends = value);
        changed |= DrawCenteredCheckbox(
            $"{id}Company",
            config.ShowFreeCompany,
            value => config.ShowFreeCompany = value);
        if (showDeadOption)
        {
            changed |= DrawCenteredCheckbox(
                $"{id}Dead",
                config.ShowDead,
                value => config.ShowDead = value);
        }
        else
        {
            DrawDisabledCell();
        }

        if (setHideUnimportantNpcs is not null)
        {
            changed |= DrawCenteredCheckbox(
                $"{id}UnimportantNpcs",
                hideUnimportantNpcs,
                setHideUnimportantNpcs);
        }
        else
        {
            DrawDisabledCell();
        }

        return changed;
    }

    private static bool DrawCenteredCheckbox(string id, bool value, Action<bool> setValue)
    {
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight()) * 0.5f));
        if (!ImGui.Checkbox($"##autoHideModel{id}", ref value))
        {
            return false;
        }

        setValue(value);
        return true;
    }

    private static void DrawHeader(string text)
    {
        ImGui.TableNextColumn();
        if (text.Length == 0)
        {
            return;
        }

        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextUnformatted(text);
    }

    private static void DrawDisabledCell()
    {
        ImGui.TableNextColumn();
        var text = "-";
        ImGui.SetCursorPosX(
            ImGui.GetCursorPosX() +
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextDisabled(text);
    }

    private static bool DrawCheckbox(
        string key,
        string id,
        bool value,
        Action<bool> setValue)
    {
        if (!ImGui.Checkbox($"{OmniLoc.Get(key)}##autoHideModel{id}", ref value))
        {
            return false;
        }

        setValue(value);
        return true;
    }
}
