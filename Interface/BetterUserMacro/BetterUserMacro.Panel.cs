using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterUserMacro
{
    internal static class Panel
    {
        public static bool Draw(Settings config)
        {
            var changed = false;
            var style = ImGui.GetStyle();
            using var cellPadding = ImRaii.PushStyle(
                ImGuiStyleVar.CellPadding,
                new Vector2(Math.Clamp(style.CellPadding.X * 0.9f, 5f, 11f), style.CellPadding.Y));
            using var itemSpacing = ImRaii.PushStyle(
                ImGuiStyleVar.ItemSpacing,
                new Vector2(Math.Clamp(style.ItemSpacing.X, 9f, 17f), style.ItemSpacing.Y));
            using var table = OmniControls.SettingsTable(
                "##betterUserMacroSettings",
                [OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterUserMacro.Tooltips")),
             OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterUserMacro.LineNumbers")),
             OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterUserMacro.DragSwap")),
             OmniControls.MeasureCheckbox(OmniLoc.Get("Feature.BetterUserMacro.CustomIcons")) +
             new Vector2(OmniControls.HelpIconSize().X + style.ItemSpacing.X, 0f)],
                ["##c0", "##c1", "##c2", "##c3"], weights: [1f, 1f, 1f, 1.25f], columnsPerRow: 4);
            if (!table)
            {
                return false;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var tooltips = config.Tooltips;
            if (DrawCheckbox(
                    "Feature.BetterUserMacro.Tooltips",
                    "tooltips",
                    ref tooltips,
                    $"{PREVIEW_IMAGE_BASE_URL}1.png"))
            {
                config.Tooltips = tooltips;
                changed         = true;
            }

            ImGui.TableNextColumn();
            var lineNumbers = config.LineNumbers;
            if (DrawCheckbox(
                    "Feature.BetterUserMacro.LineNumbers",
                    "lineNumbers",
                    ref lineNumbers,
                    $"{PREVIEW_IMAGE_BASE_URL}2.png"))
            {
                config.LineNumbers = lineNumbers;
                changed            = true;
            }

            ImGui.TableNextColumn();
            var dragSwap = config.DragSwap;
            if (DrawCheckbox("Feature.BetterUserMacro.DragSwap", "dragSwap", ref dragSwap))
            {
                config.DragSwap = dragSwap;
                changed         = true;
            }

            ImGui.TableNextColumn();
            var customIcons = config.CustomIcons;
            if (DrawCheckbox(
                    "Feature.BetterUserMacro.CustomIcons",
                    "customIcons",
                    ref customIcons,
                    $"{PREVIEW_IMAGE_BASE_URL}3.png"))
            {
                config.CustomIcons = customIcons;
                changed            = true;
            }

            OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
            OmniControls.HelpIcon(OmniLoc.Get("Feature.BetterUserMacro.CustomIcons.Help"));

            return changed;
        }

        private static bool DrawCheckbox
        (
            string   labelKey,
            string   id,
            ref bool value,
            string?  previewImageURL = null
        )
        {
            var changed = OmniControls.Checkbox($"{OmniLoc.Get(labelKey)}##betterUserMacro{id}", ref value);
            if (previewImageURL is not null)
            {
                OmniControls.PreviewImageIcon(previewImageURL);
            }
            else
            {
                OmniControls.SameLineOrWrap(OmniControls.HelpIconSize().X);
                OmniControls.HelpIcon(OmniLoc.Get($"{labelKey}.Help"));
            }

            return changed;
        }

        #region 常量

        private const string PREVIEW_IMAGE_BASE_URL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Interface/BetterUserMacro-";

        #endregion
    }

}
