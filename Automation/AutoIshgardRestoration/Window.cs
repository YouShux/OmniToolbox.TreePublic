using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.Dalamud;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.AddonEvent;
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    private bool windowOpen;

    private bool windowExpanded;

    private bool windowCollapsed;

    private bool windowSizePending;

    public override bool DrawSettings()
    {
        EnsureHostIntegration();
        if (ImGui.Button("自动重建伊修加德")) OpenConfigurationWindow();
        return false;
    }

    private void DrawConfigurationWindow()
    {
        if (!windowOpen)
        {
            return;
        }

        using var font = OmniFonts.GetUIFont().Push();
        using var style = new ComicStyleScope();
        var compactSize = OmniTheme.Scale(new Vector2(340f, 116f));
        if (!windowExpanded && !windowCollapsed && !string.IsNullOrWhiteSpace(lastError))
        {
            var errorWidth = MathF.Max(
                1f, compactSize.X - 2f * (OmniTheme.ChromeFrameInset() + OmniTheme.WindowInset()));
            compactSize.Y += ImGui.CalcTextSize($"错误：{lastError}", false, errorWidth).Y +
                             ImGui.GetStyle().ItemSpacing.Y;
        }

        var targetSize = windowCollapsed
            ? OmniTheme.CollapsedWindowSize(OmniTheme.Scale(windowExpanded ? 720f : 340f))
            : windowExpanded ? OmniTheme.Scale(new Vector2(720f, 760f)) : compactSize;
        // The compact and collapsed views are intentionally fixed-size. Only the
        // expanded configuration view remains user-resizable.
        if (!windowExpanded || windowCollapsed || windowSizePending)
        {
            ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always);
            windowSizePending = false;
        }
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground;
        if (!windowExpanded || windowCollapsed) flags |= ImGuiWindowFlags.NoResize;
        if (!ImGui.Begin("###AutoIshgardRestorationWindow", flags))
        {
            ImGui.End();
            return;
        }

        try
        {
            var windowPosition = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var framePosition = windowCollapsed
                ? windowPosition + new Vector2(OmniTheme.CollapsedHeaderSafeInset(), OmniTheme.CollapsedHeaderTop())
                : windowPosition + new Vector2(OmniTheme.ChromeFrameInset());
            var frameSize = windowCollapsed
                ? new Vector2(
                    MathF.Max(1f, windowSize.X - OmniTheme.CollapsedHeaderSafeInset() * 2f),
                    OmniTheme.TitleBarHeight())
                : windowSize - new Vector2(OmniTheme.ChromeFrameInset() * 2f);
            var chrome = OmniWindowChrome.Draw(
                framePosition,
                frameSize,
                windowCollapsed,
                "自动重建伊修加德",
                "##collapseAutoIshgardRestoration",
                "##closeAutoIshgardRestoration");
            if (chrome.ToggleCollapse)
            {
                windowCollapsed = !windowCollapsed;
                windowSizePending = true;
            }

            if (chrome.CloseClicked)
            {
                windowOpen = false;
            }

            if (!windowOpen || windowCollapsed || chrome.ToggleCollapse)
            {
                return;
            }

            var contentPosition = framePosition + new Vector2(
                OmniTheme.WindowInset(),
                OmniTheme.TitleBarHeight() + OmniTheme.WindowInset());
            var contentSize = new Vector2(
                MathF.Max(1f, frameSize.X - OmniTheme.WindowInset() * 2f),
                MathF.Max(
                    1f,
                    frameSize.Y - OmniTheme.TitleBarHeight() - OmniTheme.WindowInset() * 2f));
            ImGui.SetCursorScreenPos(contentPosition);
            if (!windowExpanded)
            {
                DrawWindowActionRow("展开", contentSize.X, () => { windowExpanded = true; windowSizePending = true; });
                if (!string.IsNullOrWhiteSpace(lastError))
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentSize.X);
                    ImGui.TextUnformatted($"错误：{lastError}");
                    ImGui.PopTextWrapPos();
                }

                return;
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            try
            {
                using var content = ImRaii.Child(
                    "##autoIshgardWindowContent",
                    contentSize,
                    false,
                    ImGuiWindowFlags.None);
                if (!content)
                {
                    return;
                }

                if (DrawConfigurationContents())
                {
                    SaveConfiguration();
                }
            }
            finally
            {
                ImGui.PopStyleVar();
                ImGui.PopStyleColor();
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawWindowActionRow(string secondaryLabel, float availableWidth, System.Action secondaryAction)
    {
        var rowPosition = ImGui.GetCursorScreenPos();
        var buttonHeight = OmniTheme.SmallButtonSize().Y;
        var secondarySize = OmniControls.CompactButtonSize(secondaryLabel);
        var primaryWidth = MathF.Min(
            OmniTheme.Scale(154f),
            MathF.Max(1f, availableWidth - secondarySize.X - ImGui.GetStyle().ItemSpacing.X));
        if (OmniControls.SmallButton(
                running || startPending ? "停止" : "启动",
                running || startPending,
                new Vector2(primaryWidth, buttonHeight)))
        {
            ToggleProduction();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X + MathF.Max(0f, availableWidth - secondarySize.X),
            rowPosition.Y));
        if (OmniControls.SmallButton(secondaryLabel, false, secondarySize))
        {
            secondaryAction();
        }

        ImGui.SetCursorScreenPos(new Vector2(
            rowPosition.X,
            rowPosition.Y + MathF.Max(buttonHeight, secondarySize.Y) + ImGui.GetStyle().ItemSpacing.Y));
    }

    private unsafe bool DrawConfigurationContents()
    {
        var changed = false;
        DrawWindowActionRow("收起", ImGui.GetContentRegionAvail().X, () => { windowExpanded = false; windowSizePending = true; });

        ImGui.Separator();
        // Keep settings editable after a failed start so the user can correct
        // the recipe and retry with the Start button.
        using var disabled = ImRaii.Disabled(running || startPending);
        var playerState = DalamudServices.PlayerState;
        var jobID = playerState.IsLoaded ? playerState.ClassJob.RowId : 0;
        var level = playerState.IsLoaded ? playerState.Level : (short)0;
        var selected = FindRecipe(config.SelectedRecipeID);

        ImGui.TextUnformatted(jobID is >= 8 and <= 15
            ? $"当前职业：{GetJobName(jobID)}  等级：{level}"
            : "当前不是生产职业，请切换职业。");

        var preview = selected is { } value && value.JobID == jobID
            ? FormatRecipe(value)
            : jobID is >= 8 and <= 15
                ? "请先选择你要生产的物品"
                : "请切换至生产职业";
        ImGui.SetNextItemWidth(GetLabeledControlWidth(520f, "生产内容"));
        using (var combo = ImRaii.Combo("生产内容", preview))
        {
            if (combo)
            {
                foreach (var recipe in Recipes)
                {
                    if (recipe.JobID != jobID)
                    {
                        continue;
                    }

                    var available = recipe.Level <= level;
                    using (ImRaii.Disabled(!available))
                    {
                        if (OmniControls.RoundedSelectable(
                                FormatRecipe(recipe),
                                config.SelectedRecipeID == recipe.RecipeID,
                                size: new Vector2(0f, OmniTheme.SmallButtonSize().Y)) && available)
                        {
                            config.SelectedRecipeID = recipe.RecipeID;
                            changed = true;
                        }
                    }
                }
            }
        }

        ImGui.Separator();
        var stopMode = config.StopMode;
        var stopModePreview = stopMode == 0
            ? "背包剩余空格达到下限时提交"
            : "目标物品达到指定数量时提交";
        ImGui.SetNextItemWidth(GetLabeledControlWidth(360f, "物品提交条件"));
        using (var combo = ImRaii.Combo("物品提交条件", stopModePreview))
        {
            if (combo)
            {
                var optionSize = new Vector2(0f, OmniTheme.SmallButtonSize().Y);
                ImGui.Dummy(new Vector2(0f, OmniTheme.Scale(4f)));
                if (OmniControls.RoundedSelectable(
                        "背包剩余空格达到下限时提交",
                        stopMode == 0,
                        size: optionSize))
                {
                    config.StopMode = 0;
                    changed = true;
                }

                if (OmniControls.RoundedSelectable(
                        "目标物品达到指定数量时提交",
                        stopMode == 1,
                        size: optionSize))
                {
                    config.StopMode = 1;
                    changed = true;
                }
            }
        }

        if (config.StopMode == 0)
        {
            ImGui.TextUnformatted("最少保留背包空格");
            var freeSlots = config.MinimumFreeSlots;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##minimumFreeSlots", ref freeSlots))
            {
                config.MinimumFreeSlots = Math.Clamp(freeSlots, 1, 139);
                changed = true;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }
        else
        {
            ImGui.TextUnformatted("自动提交时的目标物品数量");
            var targetCount = config.TargetItemCount;
            ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
            if (OmniControls.InputInt("##targetItemCount", ref targetCount))
            {
                config.TargetItemCount = Math.Clamp(targetCount, 1, 999);
                changed = true;
            }

            changed |= ImGui.IsItemDeactivatedAfterEdit();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("库啵好运票达到数量时开始抽奖");
        var ticketThreshold = config.TicketThreshold;
        ImGui.SetNextItemWidth(OmniTheme.Scale(160f));
        if (OmniControls.InputInt("##ticketThreshold", ref ticketThreshold))
        {
            config.TicketThreshold = Math.Clamp(ticketThreshold, 1, 10);
                changed = true;
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        changed |= DrawScripPurchaseSettings();

        if (selected is { } current)
        {
            var snapshot = ReadInventory(current.ItemID);
            ImGui.TextUnformatted($"背包空格：{snapshot.FreeSlots} / {snapshot.TotalSlots}");
            ImGui.TextUnformatted($"{current.ItemName}：{snapshot.ItemCount} 个");
        }

        UpdateVoucherCount(DateTime.UtcNow);
        ImGui.TextUnformatted($"好运票：{FormatVoucherCount(voucherCount, voucherLimit)}");

        ImGui.TextUnformatted($"状态：{status}");
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            ImGui.TextWrapped($"错误：{lastError}");
        }

        return changed;
    }

    private static float GetLabeledControlWidth(float preferredWidth, string label)
    {
        var available = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X -
                        ImGui.GetStyle().ItemSpacing.X;
        return MathF.Max(OmniTheme.Scale(160f), MathF.Min(OmniTheme.Scale(preferredWidth), available));
    }

    private void OpenConfigurationWindow()
    {
        windowOpen = true;
        windowExpanded = false;
        windowCollapsed = false;
        windowSizePending = true;
    }
}
