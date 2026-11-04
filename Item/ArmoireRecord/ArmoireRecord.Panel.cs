using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.ExecuteCommand.Implementations;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Commands;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Items;
using OmniToolbox.Notifications;
using OmniToolbox.TreeHouse.InterfaceOperation;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class ArmoireRecord
{
    internal static class ArmoireRecordPanel
    {
        private static readonly (string Key, ItemInventoryLocation Location,
            Func<ArmoireRecordConfig, bool> Get, Action<ArmoireRecordConfig, bool> Set)[] Columns =
        [
            ("Backpack", ItemInventoryLocation.Inventory, static c => c.ScanBackpack, static (c, v) => c.ScanBackpack = v),
            ("Armory", ItemInventoryLocation.Armory, static c => c.ScanArmory, static (c, v) => c.ScanArmory = v),
            ("Saddlebag", ItemInventoryLocation.Saddlebag, static c => c.ScanSaddlebag, static (c, v) => c.ScanSaddlebag = v),
            ("Retainers", ItemInventoryLocation.Retainer, static c => c.ScanRetainers, static (c, v) => c.ScanRetainers = v),
            ("FreeCompanyChest", ItemInventoryLocation.FreeCompanyChest, static c => c.ScanFreeCompanyChest, static (c, v) => c.ScanFreeCompanyChest = v),
            ("GlamourDresser", ItemInventoryLocation.GlamourDresser, static c => c.ScanGlamourDresser, static (c, v) => c.ScanGlamourDresser = v),
            ("Gearsets", ItemInventoryLocation.Gearset, static c => c.ScanGearsets, static (c, v) => c.ScanGearsets = v),
            ("Dyed", ItemInventoryLocation.None, static c => c.ScanDyedItems, static (c, v) => c.ScanDyedItems = v)
        ];

        public static bool Draw(ArmoireRecordConfig config)
        {
            var changed = DrawTopSettings(config);

            ImGui.Spacing();
            changed |= DrawScopes(config);

            return changed;
        }

        private static bool DrawTopSettings(ArmoireRecordConfig config)
        {
            var changed = DrawCheckbox(
                "Feature.ArmoireRecord.CheckOnOpen",
                "checkOnOpen",
                config.CheckOnCabinetOpen,
                out var checkOnOpen);
            if (changed)
            {
                config.CheckOnCabinetOpen = checkOnOpen;
            }

            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.ArmoireRecord.CheckOnOpen.Help"), referenceText: OmniLoc.Get("Feature.ArmoireRecord.CheckOnOpen"));
            return changed;
        }

        private static bool DrawScopes(ArmoireRecordConfig config)
        {
            var style = ImGui.GetStyle();
            var scopeLabel = OmniLoc.Get("Feature.ArmoireRecord.Scope.Title");
            var excludeLabel = OmniLoc.Get("Feature.ArmoireRecord.Scope.Exclude");
            var helpIconWidth = OmniControls.HelpIconSize().X + style.ItemSpacing.X;
            var labelWidth = MathF.Max(ImGui.CalcTextSize(scopeLabel).X, ImGui.CalcTextSize(excludeLabel).X) + helpIconWidth;
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var rowHeight = MathF.Max(OmniTheme.CheckboxSize(), ImGui.GetFrameHeight());
            var columnWidth = OmniTheme.CheckboxSize();
            foreach (var column in Columns)
            {
                var width = ImGui.CalcTextSize(OmniLoc.Get($"Feature.ArmoireRecord.Scope.{column.Key}")).X;
                columnWidth = MathF.Max(columnWidth, width + (column.Key == "Gearsets" ? helpIconWidth : 0f));
            }
            var detailLayout = availableWidth < labelWidth + Columns.Length * columnWidth +
                style.CellPadding.X * (Columns.Length + 1) * 2f + Columns.Length + 2f;
            using var table = ImRaii.Table(
                "##armoireRecordScopes",
                detailLayout ? 1 : Columns.Length + 1,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame |
                ImGuiTableFlags.NoSavedSettings,
                new Vector2(availableWidth, 0f));
            if (!table)
            {
                return false;
            }

            ImGui.TableSetupColumn("##scope", detailLayout
                ? ImGuiTableColumnFlags.WidthStretch : ImGuiTableColumnFlags.WidthFixed, detailLayout ? 1f : labelWidth);
            if (!detailLayout)
            {
                foreach (var column in Columns)
                    ImGui.TableSetupColumn(OmniLoc.Get($"Feature.ArmoireRecord.Scope.{column.Key}"));
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers, rowHeight);
                ImGui.TableNextColumn();
                foreach (var column in Columns)
                {
                    ImGui.TableNextColumn();
                    if (column.Key == "Gearsets")
                        DrawGearsetHeader(rowHeight);
                    else
                        OmniControls.TableTextCentered(OmniLoc.Get($"Feature.ArmoireRecord.Scope.{column.Key}"), rowHeight);
                }
            }
            var changed = false;
            for (var row = 0; row < 2; row++)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                ImGui.TableNextColumn();
                DrawScopeLabel(
                    row == 0 ? scopeLabel : excludeLabel,
                    row == 0
                        ? OmniLoc.Get("Feature.ArmoireRecord.Scope.Title.Help")
                        : OmniLoc.Get("Feature.ArmoireRecord.Scope.Exclude.Help"),
                    rowHeight);
                foreach (var column in Columns)
                {
                    OmniControls.NextTableField(OmniLoc.Get($"Feature.ArmoireRecord.Scope.{column.Key}"), detailLayout);
                    if (detailLayout && column.Key == "Gearsets")
                        OmniControls.HelpTooltip(OmniLoc.Get("Feature.ArmoireRecord.Scope.Gearsets.Help"));
                    var value = row == 0 ? column.Get(config) :
                        column.Location == ItemInventoryLocation.None ? config.ExcludeDyedItems :
                        column.Location == ItemInventoryLocation.Gearset ? config.ExcludeGearsetItems :
                        (config.ExcludedLocations & column.Location) != 0;
                    if (!detailLayout)
                        OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowHeight);
                    if (!OmniControls.Checkbox($"##armoireRecord{row}{column.Key}", ref value))
                    {
                        continue;
                    }
                    if (row == 0)
                    {
                        column.Set(config, value);
                    }
                    else if (column.Location == ItemInventoryLocation.None)
                    {
                        config.ExcludeDyedItems = value;
                    }
                    else if (column.Location == ItemInventoryLocation.Gearset)
                    {
                        config.ExcludeGearsetItems = value;
                    }
                    else
                    {
                        config.ExcludedLocations = value
                            ? config.ExcludedLocations | column.Location
                            : config.ExcludedLocations & ~column.Location;
                    }
                    changed = true;
                }
            }
            return changed;
        }

        private static void DrawScopeLabel
        (
            string label,
            string help,
            float  rowHeight
        )
        {
            var style = ImGui.GetStyle();
            var size = ImGui.CalcTextSize(label) + new Vector2(OmniControls.HelpIconSize().X + style.ItemSpacing.X, 0f);
            var cursor = ImGui.GetCursorPos();
            var availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPos(
                new Vector2(
                    cursor.X + MathF.Max(0f, (availableWidth - size.X) * 0.5f),
                    cursor.Y + MathF.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
            using (ImRaii.TextWrapPos(0f))
                ImGui.TextUnformatted(label);
            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X, style.ItemSpacing.X);
            OmniControls.HelpIcon(help, referenceText: label);
        }

        private static void DrawGearsetHeader(float rowHeight)
        {
            var style = ImGui.GetStyle();
            var label = OmniLoc.Get("Feature.ArmoireRecord.Scope.Gearsets");
            var size = ImGui.CalcTextSize(label) + new Vector2(OmniControls.HelpIconSize().X + style.ItemSpacing.X, 0f);
            var cursor = ImGui.GetCursorPos();
            var availableWidth = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPos(
                new Vector2(
                    cursor.X + MathF.Max(0f, (availableWidth - size.X) * 0.5f),
                    cursor.Y + MathF.Max(0f, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f)));
            ImGui.TextUnformatted(label);
            ImGui.SameLine(0f, style.ItemSpacing.X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.ArmoireRecord.Scope.Gearsets.Help"), referenceText: label);
        }

        private static bool DrawCheckbox
        (
            string   labelKey,
            string   id,
            bool     current,
            out bool value
        )
        {
            value = current;
            return OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##armoireRecord{id}", ref value);
        }
    }

}
