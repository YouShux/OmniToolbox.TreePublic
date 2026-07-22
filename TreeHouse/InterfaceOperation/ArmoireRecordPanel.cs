using OmniToolbox.Config;
using OmniToolbox.UI;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreeHouse.InterfaceOperation;

internal static class ArmoireRecordPanel
{
    public static bool Draw(ArmoireRecordConfig config, IArmoireRecordService service, bool enabled)
    {
        var changed = DrawTopSettings(config);

        ImGui.Spacing();
        ImGui.TextUnformatted(OmniLoc.Get("Feature.ArmoireRecord.Scope.Title"));
        changed |= DrawScopes(config);

        ImGui.Spacing();
        using var disabled = ImRaii.Disabled(!enabled || service.IsStoring || service.IsRetrieving);
        if (ImGui.Button($"{OmniLoc.Get("Feature.ArmoireRecord.Check")}##armoireRecordCheck"))
        {
            service.RequestCheck();
        }

        return changed;
    }

    private static bool DrawTopSettings(ArmoireRecordConfig config)
    {
        var style = ImGui.GetStyle();
        using var spacing = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y))
            .Push(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##armoireRecordTopSettings",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        SetupColumns();
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var changed = DrawCheckbox(
            "Feature.ArmoireRecord.CheckOnOpen",
            "checkOnOpen",
            config.CheckOnCabinetOpen,
            out var checkOnOpen);
        if (changed)
        {
            config.CheckOnCabinetOpen = checkOnOpen;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ArmoireRecord.CheckOnOpen.Help"));
        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.ExcludeDyed",
            "excludeDyed",
            config.ExcludeDyedItems,
            out var excludeDyed))
        {
            config.ExcludeDyedItems = excludeDyed;
            changed = true;
        }

        ImGuiOm.HelpMarker(OmniLoc.Get("Feature.ArmoireRecord.ExcludeDyed.Help"));
        return changed;
    }

    private static bool DrawScopes(ArmoireRecordConfig config)
    {
        var style = ImGui.GetStyle();
        using var spacing = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y))
            .Push(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
        using var table = ImRaii.Table(
            "##armoireRecordScopes",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        SetupColumns();
        var changed = false;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Backpack", "backpack", config.ScanBackpack, out var backpack))
        {
            config.ScanBackpack = backpack;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Armory", "armory", config.ScanArmory, out var armory))
        {
            config.ScanArmory = armory;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Saddlebag", "saddlebag", config.ScanSaddlebag, out var saddlebag))
        {
            config.ScanSaddlebag = saddlebag;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox("Feature.ArmoireRecord.Scope.Retainers", "retainers", config.ScanRetainers, out var retainers))
        {
            config.ScanRetainers = retainers;
            changed = true;
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.Scope.FreeCompanyChest",
            "freeCompanyChest",
            config.ScanFreeCompanyChest,
            out var freeCompanyChest))
        {
            config.ScanFreeCompanyChest = freeCompanyChest;
            changed = true;
        }

        ImGui.TableNextColumn();
        if (DrawCheckbox(
            "Feature.ArmoireRecord.Scope.GlamourDresser",
            "glamourDresser",
            config.ScanGlamourDresser,
            out var glamourDresser))
        {
            config.ScanGlamourDresser = glamourDresser;
            changed = true;
        }

        return changed;
    }

    private static void SetupColumns()
    {
        ImGui.TableSetupColumn("##armoireRecordColumn0", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn1", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn2", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##armoireRecordColumn3", ImGuiTableColumnFlags.WidthStretch, 1.25f);
    }

    private static bool DrawCheckbox(string labelKey, string id, bool current, out bool value)
    {
        value = current;
        return ImGui.Checkbox($"{OmniLoc.Get(labelKey)}##armoireRecord{id}", ref value);
    }
}
