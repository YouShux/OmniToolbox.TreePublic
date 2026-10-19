using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using InteropGenerator.Runtime;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Lumina.Enums;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using LuminaAetheryte = Lumina.Excel.Sheets.Aetheryte;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;
using ModuleBase = OmniToolbox.Common.Module.Abstractions.ModuleBase;

namespace OmniToolbox.TreePublic;

public sealed partial class AetheryteModelReplacement
{
    internal static class Panel
    {
        public static bool Draw(AetheryteModelReplacement module, Settings config)
        {
            var presets = module.GetPresets();
            if (module.IsPresetScanPending)
            {
                ImGui.TextUnformatted(OmniLoc.Get("Feature.AetheryteModelReplacement.PresetsLoading"));
                return false;
            }

            if (presets.Count == 0)
            {
                ImGui.TextUnformatted(OmniLoc.Get("Feature.AetheryteModelReplacement.PresetsUnavailable"));
                return false;
            }

            var changed = false;
            var rules = config.Rules;
            var rowContentHeight = MathF.Max(
                OmniTheme.CheckboxSize(),
                MathF.Max(OmniTheme.SmallButtonSize().Y, ImGui.GetFrameHeight()));
            var deleteLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.DeleteRule");
            var addLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.AddRule");
            var deleteSize = OmniControls.CompactButtonSize(deleteLabel);
            var addSize = OmniControls.CompactButtonSize(addLabel);
            var actionLabel = OmniLoc.Get("Feature.AetheryteModelReplacement.Actions");
            var actionWidth = MathF.Max(deleteSize.X, addSize.X) +
                              ImGui.GetStyle().CellPadding.X * 2f;
            var visibleRows = Math.Min(5, Math.Max(1, rules.Count + 1));
            var tableHeight = rowContentHeight * (visibleRows + 1) +
                              ImGui.GetStyle().CellPadding.Y * (visibleRows + 2) * 2f +
                              ImGui.GetStyle().FrameBorderSize * 2f;
            using (var table = OmniControls.DataTable(
                       "##aetheryteModelReplacementSettings",
                       [string.Empty, OmniLoc.Get("Feature.AetheryteModelReplacement.Source"),
                       OmniLoc.Get("Feature.AetheryteModelReplacement.Target"), actionLabel],
                       [OmniTheme.CheckboxSize(), ImGui.GetFontSize() * 12f, ImGui.GetFontSize() * 12f, actionWidth],
                       [OmniTheme.CheckboxSize(), ImGui.GetFontSize() * 18f, ImGui.GetFontSize() * 18f, actionWidth], out var detailLayout,
                       ImGuiTableFlags.Borders |
                       ImGuiTableFlags.RowBg |
                       ImGuiTableFlags.ScrollY |
                       ImGuiTableFlags.SizingStretchProp,
                       new Vector2(ImGui.GetContentRegionAvail().X, tableHeight), stretchColumn: 1))
            {
                if (!table)
                {
                    return changed;
                }

                ImGui.TableSetupScrollFreeze(0, 1);
                OmniControls.BeginTableHeaderRow(rowContentHeight);
                ImGui.TableNextColumn();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                var allEnabled = rules.Count > 0 && rules.All(static rule => rule.Enabled);
                using (ImRaii.Disabled(rules.Count == 0))
                {
                    OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                    if (OmniControls.Checkbox("##aetheryteAllEnabled", ref allEnabled))
                    {
                        foreach (var rule in rules)
                        {
                            rule.Enabled = allEnabled;
                        }

                        changed = true;
                    }
                }

                if (!detailLayout)
                {
                    OmniControls.TableHeader(OmniLoc.Get("Feature.AetheryteModelReplacement.Source"), rowContentHeight);
                    OmniControls.TableHeader(OmniLoc.Get("Feature.AetheryteModelReplacement.Target"), rowContentHeight);
                    OmniControls.TableHeader(actionLabel, rowContentHeight);
                }

                var removeIndex = -1;
                for (var index = 0; index < rules.Count; index++)
                {
                    var rule = rules[index];
                    ImGui.PushID($"aetheryteRule{index}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                    ImGui.TableNextColumn();
                    var enabled = rule.Enabled;
                    OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
                    if (OmniControls.Checkbox("##enabled", ref enabled))
                    {
                        rule.Enabled = enabled;
                        changed = true;
                    }

                    OmniControls.NextTableField(OmniLoc.Get("Feature.AetheryteModelReplacement.Source"), detailLayout);
                    var usedSources = rules
                        .Where((candidate, candidateIndex) => candidateIndex != index)
                        .Select(static candidate => (candidate.SourceTerritoryID, candidate.SourceAetheryteRowID))
                        .ToHashSet();
                    var sourceOptions = presets
                        .Where(preset => !usedSources.Contains((preset.TerritoryID, preset.RowID)) ||
                                         (preset.TerritoryID == rule.SourceTerritoryID &&
                                          preset.RowID == rule.SourceAetheryteRowID))
                        .ToArray();
                    var source = sourceOptions.FirstOrDefault(preset =>
                                     preset.TerritoryID == rule.SourceTerritoryID &&
                                     preset.RowID == rule.SourceAetheryteRowID) ??
                                 sourceOptions.FirstOrDefault(preset =>
                                     preset.SharedGroupPath.Equals(rule.SourceSharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                                 sourceOptions.FirstOrDefault();
                    OmniControls.CenterTableItem(
                        new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                        rowContentHeight);
                    if (source is not null && DrawPresetCombo("source", sourceOptions, source, true, out var selectedSource))
                    {
                        rule.SourceTerritoryID = selectedSource.TerritoryID;
                        rule.SourceAetheryteRowID = selectedSource.RowID;
                        rule.SourceSharedGroupPath = selectedSource.SharedGroupPath;
                        changed = true;
                    }

                    OmniControls.NextTableField(OmniLoc.Get("Feature.AetheryteModelReplacement.Target"), detailLayout);
                    var target = presets.FirstOrDefault(preset =>
                                      preset.SharedGroupPath.Equals(rule.TargetSharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                                 presets[0];
                    OmniControls.CenterTableItem(
                        new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                        rowContentHeight);
                    if (DrawPresetCombo("target", presets, target, false, out var selectedTarget))
                    {
                        rule.TargetSharedGroupPath = selectedTarget.SharedGroupPath;
                        changed = true;
                    }

                    ImGui.TableNextColumn();
                    OmniControls.CenterTableItem(deleteSize, rowContentHeight);
                    if (OmniControls.SmallButton($"{deleteLabel}##delete", false, deleteSize))
                    {
                        removeIndex = index;
                    }

                    ImGui.PopID();
                }

                if (removeIndex >= 0)
                {
                    rules.RemoveAt(removeIndex);
                    changed = true;
                }

                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowContentHeight);
                ImGui.TableSetColumnIndex(detailLayout ? 0 : 3);
                OmniControls.CenterTableItem(addSize, rowContentHeight);
                if (OmniControls.SmallButton($"{addLabel}##add", false, addSize))
                {
                    changed |= AddRule(config, presets);
                }
            }

            return changed;
        }

        private static bool AddRule(
            Settings config,
            IReadOnlyList<AetherytePreset> presets)
        {
            var usedSources = config.Rules
                .Select(static rule => (rule.SourceTerritoryID, rule.SourceAetheryteRowID))
                .ToHashSet();
            var source = presets.FirstOrDefault(preset => !usedSources.Contains((preset.TerritoryID, preset.RowID)));
            if (source is null)
            {
                return false;
            }

            var target = presets.FirstOrDefault(preset =>
                             !preset.SharedGroupPath.Equals(source.SharedGroupPath, StringComparison.OrdinalIgnoreCase)) ??
                         source;
            config.Rules.Add(new(source.SharedGroupPath, target.SharedGroupPath)
            {
                SourceTerritoryID = source.TerritoryID,
                SourceAetheryteRowID = source.RowID
            });
            return true;
        }

        private static bool DrawPresetCombo(
            string id,
            IReadOnlyList<AetherytePreset> options,
            AetherytePreset current,
            bool matchIdentity,
            out AetherytePreset selected)
        {
            selected = current;
            if (!OmniControls.BeginCombo(
                    $"##aetheryteModelReplacement{id}",
                    current.DisplayName,
                    MathF.Max(1f, ImGui.GetContentRegionAvail().X)))
            {
                return false;
            }

            var changed = false;
            foreach (var option in options)
            {
                var isSelected = matchIdentity
                    ? option.TerritoryID == current.TerritoryID && option.RowID == current.RowID
                    : option.SharedGroupPath.Equals(current.SharedGroupPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option.DisplayName, isSelected))
                {
                    selected = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
            return changed;
        }
    }

}
