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
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
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
using OmenTools.Info.Game.Packets.Upstream;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;
using GameEventHandlerContent = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerContent;
using GameEventID = FFXIVClientStructs.FFXIV.Client.Game.Event.EventId;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace OmniToolbox.TreePublic;

public sealed class AutoIshgardRestoration(AutoIshgardRestorationConfig config) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "自动重建伊修加德",
        Description = "准备好生产材料后，模块将通过Artisan插件进行自动生产，随后进行自动提交与库啵好运道",
        Category = ModuleCategory.Automation,
        Author = "Angelways",
        SupportUrls = ["https://github.com/Angelways"],
        Commands =
        [
            new ModuleCommand(
                "/omni 自动重建伊修加德 → 打开自动重建伊修加德窗口",
                "/omni 自动重建伊修加德"),
            new ModuleCommand(
                "/omni 开关自动重建伊修加德 → 控制重建伊修加德自动化流程",
                "/omni 开关自动重建伊修加德")
        ]
    };

    private static readonly GameInventoryType[] MainInventoryTypes =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4
    ];

    private static readonly RecipeOption[] Recipes =
    [
        new(34433, 31913, 8, 20, false, "第四期重建用的合板"),
        new(34441, 31921, 8, 40, false, "第四期重建用的木箱"),
        new(34449, 31929, 8, 60, false, "第四期重建用的纺车"),
        new(34457, 31937, 8, 70, false, "第四期重建用的梯子"),
        new(34465, 31945, 8, 80, false, "第四期重建用的睡床"),
        new(34473, 31953, 8, 80, true,  "第四期重建用的特供冰盒"),

        new(34434, 31914, 9, 20, false, "第四期重建用的合金"),
        new(34442, 31922, 9, 40, false, "第四期重建用的铁钉"),
        new(34450, 31930, 9, 60, false, "第四期重建用的手斧"),
        new(34458, 31938, 9, 70, false, "第四期重建用的锯子"),
        new(34466, 31946, 9, 80, false, "第四期重建用的火炉"),
        new(34474, 31954, 9, 80, true,  "第四期重建用的特供风向陆行鸟"),

        new(34435, 31915, 10, 20, false, "第四期重建用的金属板"),
        new(34443, 31923, 10, 40, false, "第四期重建用的铆钉"),
        new(34451, 31931, 10, 60, false, "第四期重建用的吊锅"),
        new(34459, 31939, 10, 70, false, "第四期重建用的口罩"),
        new(34467, 31947, 10, 80, false, "第四期重建用的街灯"),
        new(34475, 31955, 10, 80, true,  "第四期重建用的特供部队储物柜"),

        new(34436, 31916, 11, 20, false, "第四期重建用的金属锭"),
        new(34444, 31924, 11, 40, false, "第四期重建用的铁环"),
        new(34452, 31932, 11, 60, false, "第四期重建用的裁衣工具"),
        new(34460, 31940, 11, 70, false, "第四期重建用的石材"),
        new(34468, 31948, 11, 80, false, "第四期重建用的篝火台"),
        new(34476, 31956, 11, 80, true,  "第四期重建用的特供天文仪"),

        new(34437, 31917, 12, 20, false, "第四期重建用的鞣革"),
        new(34445, 31925, 12, 40, false, "第四期重建用的皮绳"),
        new(34453, 31933, 12, 60, false, "第四期重建用的皮袋"),
        new(34461, 31941, 12, 70, false, "第四期重建用的长靴"),
        new(34469, 31949, 12, 80, false, "第四期重建用的工作服"),
        new(34477, 31957, 12, 80, true,  "第四期重建用的特供工具腰带"),

        new(34438, 31918, 13, 20, false, "第四期重建用的草绳"),
        new(34446, 31926, 13, 40, false, "第四期重建用的布料"),
        new(34454, 31934, 13, 60, false, "第四期重建用的扫把"),
        new(34462, 31942, 13, 70, false, "第四期重建用的手套"),
        new(34470, 31950, 13, 80, false, "第四期重建用的遮蓬"),
        new(34478, 31958, 13, 80, true,  "第四期重建用的特供坎肩"),

        new(34439, 31919, 14, 20, false, "第四期重建用的墨水"),
        new(34447, 31927, 14, 40, false, "第四期重建用的植物油"),
        new(34455, 31935, 14, 60, false, "第四期重建用的圣水"),
        new(34463, 31943, 14, 70, false, "第四期重建用的肥皂"),
        new(34471, 31951, 14, 80, false, "第四期重建用的植物成长剂"),
        new(34479, 31959, 14, 80, true,  "第四期重建用的特供幻药"),

        new(34440, 31920, 15, 20, false, "第四期重建用的麻乳"),
        new(34448, 31928, 15, 40, false, "第四期重建用的芝麻饼干"),
        new(34456, 31936, 15, 60, false, "第四期重建用的红茶"),
        new(34464, 31944, 15, 70, false, "第四期重建用的药汤"),
        new(34472, 31952, 15, 80, false, "第四期重建用的炖菜"),
        new(34480, 31960, 15, 80, true,  "第四期重建用的特供冰糕")
    ];

    private ICallGateSubscriber<ushort, int, object>? craftItem;
    private ICallGateSubscriber<bool>? artisanIsBusy;
    private ICallGateSubscriber<bool>? getStopRequest;
    private ICallGateSubscriber<bool, object>? setStopRequest;
    private DateTime nextCheckAt;
    private DateTime phaseStartedAt;
    private DateTime nextActionAt;
    private bool running;
    private bool windowOpen;
    private bool windowExpanded;
    private bool windowCollapsed;
    private bool windowConfigChanged;
    private bool artisanStartedByModule;
    private bool vnavPathStartedByModule;
    private uint activeRecipeID;
    private uint activeItemID;
    private AutomationPhase phase;
    private int itemCountBeforeTurnIn;
    private uint scripCountBeforeTurnIn;
    private int ticketsToPlay;
    private bool lotteryScratched;
    private bool lotteryTicketCounted;
    private int lotteryScratchAttempts;
    private int craftCycleStartingItemCount;
    private DateTime? movableSince;
    private Vector3 initialDestination;
    private DateTime nextDebugReadAt;
    private int debugVoucherCount = -1;
    private int debugVoucherLimit = -1;
    private string status = "待机";
    private string lastError = string.Empty;

    private const uint FIRMAMENT_TERRITORY_ID = 886;
    private const string FIRMAMENT_TELEPORT_COMMAND = "/pdrtelepo 无名众人广场";
    private const uint SKYBUILDERS_SCRIP_ITEM_ID = 28063;
    private const int RIGHT_LOTTERY_EVENT_PARAM = 22;
    private const uint APPRAISER_NPC_ID_1 = 1031690;
    private const uint APPRAISER_NPC_ID_2 = 1031677;
    private const uint LOTTERY_NPC_ID = 1031692;
    private static readonly Regex VOUCHER_PATTERN = new("^(\\d+)/(\\d+)$", RegexOptions.Compiled);

    public override bool HasSettings => true;

    protected override void OnEnable()
    {
        craftItem = DalamudServices.PluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        artisanIsBusy = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        getStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
        setStopRequest = DalamudServices.PluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
        DalamudServices.Framework.Update += OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawConfigurationWindow;
    }

    protected override void OnDisable()
    {
        DalamudServices.Framework.Update -= OnFrameworkUpdate;
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawConfigurationWindow;
        windowOpen = false;
        windowExpanded = false;
        windowCollapsed = false;
        StopProduction("模块已停用");
        craftItem = null;
        artisanIsBusy = null;
        getStopRequest = null;
        setStopRequest = null;
    }

    protected override bool OnInterruptAutomation()
    {
        if (!running)
        {
            return false;
        }

        StopProduction("已由 Omni 中断");
        return true;
    }

    public override bool TryHandleCommand(string command, string arguments)
    {
        if (string.Equals(command.Trim(), "自动重建伊修加德", StringComparison.OrdinalIgnoreCase))
        {
            OpenConfigurationWindow();
            return true;
        }

        if (string.Equals(command.Trim(), "开关自动重建伊修加德", StringComparison.OrdinalIgnoreCase))
        {
            ToggleProduction();
            return true;
        }

        return false;
    }

    public override bool DrawSettings()
    {
        var changed = windowConfigChanged;
        windowConfigChanged = false;

        if (ImGui.Button("自动重建伊修加德"))
        {
            OpenConfigurationWindow();
        }

        return changed;
    }

    private void DrawConfigurationWindow()
    {
        if (!windowOpen)
        {
            return;
        }

        using var font = OmniFonts.GetUIFont().Push();
        using var style = new ComicStyleScope();
        var targetSize = windowCollapsed
            ? OmniTheme.CollapsedWindowSize(OmniTheme.Scale(windowExpanded ? 720f : 340f))
            : OmniTheme.Scale(windowExpanded ? new Vector2(720f, 760f) : new Vector2(340f, 116f));
        ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always);
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize;
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
                DrawWindowActionRow("展开", contentSize.X, () => windowExpanded = true);
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
                    windowConfigChanged = true;
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
                running ? "停止" : "启动",
                running,
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
        DrawWindowActionRow("收起", ImGui.GetContentRegionAvail().X, () => windowExpanded = false);

        ImGui.Separator();
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
        }

        changed |= ImGui.IsItemDeactivatedAfterEdit();

        if (selected is { } current)
        {
            var snapshot = ReadInventory(current.ItemID);
            ImGui.TextUnformatted($"背包空格：{snapshot.FreeSlots} / {snapshot.TotalSlots}");
            ImGui.TextUnformatted($"{current.ItemName}：{snapshot.ItemCount} 个");
        }

        UpdateDebugCounters(DateTime.UtcNow);
        ImGui.TextUnformatted($"好运票：{FormatDebugCounter(debugVoucherCount, debugVoucherLimit)}");

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
    }

    private void ToggleProduction()
    {
        if (running)
        {
            StopProduction("已手动停止");
        }
        else
        {
            StartProduction();
        }
    }

    public override bool ResetSettings()
    {
        var defaults = new AutoIshgardRestorationConfig();
        config.SelectedRecipeID = defaults.SelectedRecipeID;
        config.StopMode = defaults.StopMode;
        config.MinimumFreeSlots = defaults.MinimumFreeSlots;
        config.TargetItemCount = defaults.TargetItemCount;
        config.TicketThreshold = defaults.TicketThreshold;
        return true;
    }

    private void StartProduction()
    {
        if (running)
        {
            return;
        }

        lastError = string.Empty;
        var playerState = DalamudServices.PlayerState;
        if (!playerState.IsLoaded)
        {
            Fail("角色尚未登录。");
            return;
        }

        var currentJobID = playerState.ClassJob.RowId;
        if (currentJobID is < 8 or > 15)
        {
            Fail("当前不是生产职业，请切换职业。");
            return;
        }

        var recipe = FindRecipe(config.SelectedRecipeID);
        if (recipe is null || recipe.Value.JobID != currentJobID)
        {
            Fail("请先选择你要生产的物品。");
            return;
        }

        if (playerState.Level < recipe.Value.Level)
        {
            Fail($"当前职业等级不足，需要 {recipe.Value.Level} 级。");
            return;
        }

        try
        {
            if (artisanIsBusy?.InvokeFunc() == true)
            {
                Fail("Artisan 正在执行其他任务。");
                return;
            }

            running = true;
            activeRecipeID = recipe.Value.RecipeID;
            activeItemID = recipe.Value.ItemID;
            nextCheckAt = DateTime.UtcNow;

            if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
            {
                if (!TrySendCommand(FIRMAMENT_TELEPORT_COMMAND))
                {
                    FailAutomation("无法执行传送指令 /pdrtelepo 无名众人广场。");
                    return;
                }

                EnterPhase(AutomationPhase.WaitForFirmament);
                status = "正在前往无名众人广场，等待进入天穹街（区域 886）";
                return;
            }

            StartCraftingCycle(recipe.Value, "开始");
        }
        catch (Exception ex)
        {
            FailAutomation($"无法启动自动流程：{ex.Message}");
        }
    }

    private void StopProduction(string reason)
    {
        if (artisanStartedByModule)
        {
            try
            {
                if (getStopRequest?.InvokeFunc() != true)
                {
                    setStopRequest?.InvokeAction(true);
                }
            }
            catch (Exception ex)
            {
                lastError = $"停止 Artisan 失败：{ex.Message}";
            }
            finally
            {
                artisanStartedByModule = false;
            }
        }

        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        artisanStartedByModule = false;
        vnavPathStartedByModule = false;
        status = reason;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!running || DateTime.UtcNow < nextCheckAt)
        {
            return;
        }

        nextCheckAt = DateTime.UtcNow.AddMilliseconds(100);
        try
        {
            UpdateDebugCounters(DateTime.UtcNow);
            var activeRecipe = FindRecipe(activeRecipeID);
            if (activeRecipe is null || activeRecipe.Value.ItemID != activeItemID)
            {
                StopProduction("运行数据无效，已停止");
                return;
            }

            DriveAutomation(activeRecipe.Value);
        }
        catch (Exception ex)
        {
            StopProduction("监控异常，已停止");
            lastError = ex.Message;
        }
    }

    private void DriveAutomation(RecipeOption recipe)
    {
        switch (phase)
        {
            case AutomationPhase.WaitForFirmament:
                DriveWaitForFirmament();
                break;
            case AutomationPhase.WaitForPlayerMovable:
                DriveWaitForPlayerMovable();
                break;
            case AutomationPhase.MoveToInitialPoint:
                DriveMoveToInitialPoint(recipe);
                break;
            case AutomationPhase.WaitArtisanStart:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == true)
                {
                    EnterPhase(AutomationPhase.Crafting);
                    status = $"Artisan 生产中：{recipe.ItemName}";
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已完成本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.Crafting:
            {
                var snapshot = ReadInventory(activeItemID);
                if (ShouldStop(snapshot, out var reason))
                {
                    RequestArtisanStop();
                    EnterPhase(AutomationPhase.WaitArtisanStop);
                    status = $"{reason}；等待 Artisan 停止";
                }
                else if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    if (snapshot.ItemCount > craftCycleStartingItemCount)
                    {
                        BeginSubmission("Artisan 已结束本轮生产");
                    }
                    else
                    {
                        CompleteAutomation($"无法继续制作 {recipe.ItemName}，可能缺少材料或不满足制作条件");
                    }
                }

                break;
            }
            case AutomationPhase.WaitArtisanStop:
                if (artisanIsBusy?.InvokeFunc() == false)
                {
                    artisanStartedByModule = false;
                    BeginSubmission("Artisan 已停止");
                }
                else if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
                {
                    FailAutomation("等待 Artisan 停止超时。");
                }
                break;
            case AutomationPhase.MoveToAppraiser:
                DriveStartNPCEvent([APPRAISER_NPC_ID_1, APPRAISER_NPC_ID_2],
                    AutomationPhase.OpenAppraiser, "提交NPC");
                break;
            case AutomationPhase.OpenAppraiser:
                DriveOpenAppraiser();
                break;
            case AutomationPhase.SelectJob:
                DriveSelectJob(recipe);
                break;
            case AutomationPhase.SelectItem:
                DriveSelectItem(recipe);
                break;
            case AutomationPhase.FillRequest:
                DriveFillRequest();
                break;
            case AutomationPhase.WaitTurnIn:
                DriveWaitTurnIn(recipe);
                break;
            case AutomationPhase.WaitSupplyRefresh:
                DriveWaitSupplyRefresh(recipe);
                break;
            case AutomationPhase.MoveToLottery:
                DriveStartNPCEvent([LOTTERY_NPC_ID], AutomationPhase.OpenLottery, "库啵好运道NPC");
                break;
            case AutomationPhase.OpenLottery:
                DriveOpenLottery();
                break;
            case AutomationPhase.PlayLottery:
                DrivePlayLottery();
                break;
            case AutomationPhase.PostLottery:
                DrivePostLottery(recipe);
                break;
            case AutomationPhase.PrepareNextCycle:
                DrivePrepareNextCycle(recipe);
                break;
        }
    }

    private void DriveWaitForFirmament()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType == FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            EnterPhase(AutomationPhase.WaitForPlayerMovable);
            status = "已进入天穹街，等待玩家连续可动 1 秒";
            return;
        }

        status = "等待进入天穹街（区域 886）";
        if (PhaseTimedOut(TimeSpan.FromMinutes(3)))
        {
            FailAutomation("执行传送指令后，3 分钟内未进入天穹街（区域 886）。");
        }
    }

    private void DriveWaitForPlayerMovable()
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            movableSince = null;
            status = "区域尚未稳定，等待进入天穹街（区域 886）";
            return;
        }

        if (!IsPlayerMovable())
        {
            movableSince = null;
            status = "已进入天穹街，等待玩家恢复可动";
            return;
        }

        movableSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - movableSince.Value < TimeSpan.FromSeconds(1))
        {
            status = "玩家已可动，等待状态持续 1 秒";
            return;
        }

        if (!vnavmeshIPC.IsPluginEnabled())
        {
            FailAutomation("vnavmesh 未安装或未启用。");
            return;
        }

        if (!vnavmeshIPC.GetIsNavReady())
        {
            status = "玩家已可动，等待 vnavmesh 导航网格准备完成";
            if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
            {
                FailAutomation("进入天穹街后，vnavmesh 导航网格未能准备完成。");
            }
            return;
        }

        initialDestination = new Vector3(
            38f + (Random.Shared.NextSingle() * 9f),
            -16f,
            162f + (Random.Shared.NextSingle() * 14f));
        try
        {
            vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
            vnavPathStartedByModule = true;
        }
        catch (Exception ex)
        {
            FailAutomation($"无法执行初始导航：{ex.Message}");
            return;
        }
        EnterPhase(AutomationPhase.MoveToInitialPoint);
        nextActionAt = DateTime.UtcNow.AddSeconds(5);
        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
    }

    private void DriveMoveToInitialPoint(RecipeOption recipe)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("前往随机生产点时离开了天穹街（区域 886）。");
            return;
        }

        var player = OmenTools.DService.Instance().ObjectTable.LocalPlayer;
        if (player is null)
        {
            status = "等待读取玩家位置";
            return;
        }

        if (Vector3.Distance(player.Position, initialDestination) <= 2f)
        {
            StopOwnedVnavPath();
            StartCraftingCycle(recipe, "已到达随机生产点");
            return;
        }

        if (!vnavmeshIPC.GetIsPathfindRunning() && DateTime.UtcNow >= nextActionAt)
        {
            try
            {
                vnavmeshIPC.PathfindAndMoveTo(initialDestination, false);
                vnavPathStartedByModule = true;
            }
            catch (Exception ex)
            {
                FailAutomation($"无法再次执行初始导航：{ex.Message}");
                return;
            }
            nextActionAt = DateTime.UtcNow.AddSeconds(5);
        }

        status = $"正在前往随机生产点：{FormatPosition(initialDestination)}";
        if (PhaseTimedOut(TimeSpan.FromSeconds(90)))
        {
            FailAutomation("前往随机生产点超时。");
        }
    }

    private void StartCraftingCycle(RecipeOption recipe, string reason)
    {
        var snapshot = ReadInventory(recipe.ItemID);
        if (ShouldStop(snapshot, out var stopReason))
        {
            BeginSubmission(stopReason);
            return;
        }

        if (artisanIsBusy?.InvokeFunc() == true)
        {
            FailAutomation("Artisan 正在执行其他任务，无法开始下一轮生产。");
            return;
        }

        if (getStopRequest?.InvokeFunc() == true)
        {
            setStopRequest?.InvokeAction(false);
        }

        var amount = config.StopMode == 1
            ? Math.Max(1, config.TargetItemCount - snapshot.ItemCount)
            : 9999;
        craftCycleStartingItemCount = snapshot.ItemCount;
        if (craftItem is null)
        {
            FailAutomation("Artisan 插件接口不可用。");
            return;
        }

        artisanStartedByModule = true;
        craftItem.InvokeAction((ushort)recipe.RecipeID, amount);
        EnterPhase(AutomationPhase.WaitArtisanStart);
        status = $"{reason}；正在启动 Artisan 制作 {recipe.ItemName}";
    }

    private void BeginSubmission(string reason)
    {
        if (OmenTools.DService.Instance().ClientState.TerritoryType != FIRMAMENT_TERRITORY_ID)
        {
            FailAutomation("自动提交仅支持在天穹街内启动（区域 886）。");
            return;
        }

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            CompleteAutomation("生产结束，但背包中没有可提交的目标物品，可能已无法继续制作");
            return;
        }

        EnterPhase(AutomationPhase.MoveToAppraiser);
        status = $"{reason}；前往提交NPC";
    }

    private void RequestArtisanStop()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        if (getStopRequest?.InvokeFunc() != true)
        {
            setStopRequest?.InvokeAction(true);
        }
    }

    private void DriveStartNPCEvent(uint[] npcIDs, AutomationPhase nextPhase, string label)
    {
        if (IsOccupied())
        {
            status = $"等待交互结束后打开{label}";
            return;
        }

        var npc = OmenTools.DService.Instance().ObjectTable
            .FirstOrDefault(o => npcIDs.Contains(GetBaseID(o.Address)) && o.IsTargetable);
        if (npc is null)
        {
            FailAutomation($"未找到{label}。");
            return;
        }

        if (!SendNPCEventStart(npc.Address))
        {
            FailAutomation($"无法打开{label}界面。");
            return;
        }

        EnterPhase(nextPhase);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        status = $"正在打开{label}界面";
    }

    private unsafe void DriveOpenAppraiser()
    {
        if (GetAddon("HWDSupply") != null)
        {
            EnterPhase(AutomationPhase.SelectJob);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "提交界面已打开";
            return;
        }

        var talk = GetAddon("Talk");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDSupply 提交界面。");
        }
    }

    private unsafe void DriveSelectJob(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = ValueType.Int, Int = 0 };
        values[1] = new AtkValue { Type = ValueType.Int, Int = (int)recipe.JobID - 8 };
        addon->FireCallback(2, values, true);
        EnterPhase(AutomationPhase.SelectItem);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
        status = $"已选择{GetJobName(recipe.JobID)}提交列表";
    }

    private unsafe void DriveSelectItem(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (ReadInventory(activeItemID).ItemCount <= 0)
        {
            FinishSubmissionOrStartLottery(recipe);
            return;
        }

        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            FailAutomation("HWDSupply 提交界面意外关闭。");
            return;
        }

        var row = FindSupplyRow(addon, activeItemID);
        if (row < 0)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation($"提交列表中未找到 {recipe.ItemName}；请确认物品收藏价值达到最低要求。");
            }
            return;
        }

        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = ValueType.Int, Int = 1 };
        values[1] = new AtkValue { Type = ValueType.Int, Int = row };
        itemCountBeforeTurnIn = ReadInventory(activeItemID).ItemCount;
        scripCountBeforeTurnIn = ReadSkybuildersScrips();
        addon->FireCallback(2, values, true);
        EnterPhase(AutomationPhase.FillRequest);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        status = $"正在提交：{recipe.ItemName}";
    }

    private unsafe void DriveFillRequest()
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        var requestBase = GetAddon("Request");
        if (requestBase == null)
        {
            if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("提交物品后未出现 Request 窗口。");
            }
            return;
        }

        var request = (AddonRequest*)requestBase;
        if (request->HandOverButton != null && request->HandOverButton->IsEnabled)
        {
            ClickButton(requestBase, request->HandOverButton);
            EnterPhase(AutomationPhase.WaitTurnIn);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "已点击交纳，等待完成";
            return;
        }

        var picker = GetAddon("ContextIconMenu");
        if (picker != null)
        {
            var icon = picker->AtkValuesCount > 11 && picker->AtkValues[11].Type == ValueType.UInt
                ? picker->AtkValues[11].UInt
                : 0u;
            var pickValues = stackalloc AtkValue[4];
            pickValues[0] = new AtkValue { Type = ValueType.Int, Int = 0 };
            pickValues[1] = new AtkValue { Type = ValueType.Int, Int = 0 };
            pickValues[2] = new AtkValue { Type = ValueType.UInt, UInt = icon };
            pickValues[3] = new AtkValue { Type = ValueType.UInt, UInt = 0 };
            picker->FireCallback(4, pickValues, true);
        }
        else
        {
            var openValues = stackalloc AtkValue[4];
            openValues[0] = new AtkValue { Type = ValueType.Int, Int = 2 };
            openValues[1] = new AtkValue { Type = ValueType.UInt, UInt = 0 };
            openValues[2] = new AtkValue { Type = ValueType.UInt, UInt = 0 };
            openValues[3] = new AtkValue { Type = ValueType.UInt, UInt = 0 };
            requestBase->FireCallback(4, openValues, true);
        }

        nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("自动放入收藏品超时。");
        }
    }

    private unsafe void DriveWaitTurnIn(RecipeOption recipe)
    {
        var yesNoBase = GetAddon("SelectYesno");
        if (DateTime.UtcNow >= nextActionAt && IsOccupied() && yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            }
            return;
        }

        if (TryDetectCompletedTurnIn(out var currentCount, out var currentScrips))
        {
            EnterWaitSupplyRefresh(currentCount, currentScrips);
            return;
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
        {
            FailAutomation("交纳后物品数量没有减少。");
        }
    }

    private unsafe void DriveWaitSupplyRefresh(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var request = GetAddon("Request");
        if (request != null && !PhaseTimedOut(TimeSpan.FromSeconds(2)))
        {
            status = $"等待本次提交窗口关闭（剩余 {itemCountBeforeTurnIn} 个）";
            return;
        }

        if (request != null)
        {
            CloseAddon("Request");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            status = "正在关闭已完成的提交窗口";
            if (!PhaseTimedOut(TimeSpan.FromSeconds(5)))
            {
                return;
            }
        }

        if (GetAddon("HWDSupply") == null)
        {
            if (IsOccupied())
            {
                if (PhaseTimedOut(TimeSpan.FromSeconds(12)))
                {
                    FailAutomation("提交后未能返回物品列表。");
                }
                return;
            }

            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = "提交界面已关闭，正在重新打开";
            return;
        }

        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"库啵好运票达到 {vouchers} 张，停止提交并前往抽奖";
            return;
        }

        if (itemCountBeforeTurnIn > 0)
        {
            EnterPhase(AutomationPhase.SelectItem);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
            status = $"继续提交，背包剩余 {itemCountBeforeTurnIn} 个";
        }
        else
        {
            FinishSubmissionOrStartLottery(recipe);
        }
    }

    private unsafe void FinishSubmissionOrStartLottery(RecipeOption recipe)
    {
        if (TryReadVoucherCount(out var vouchers, out _) && vouchers >= config.TicketThreshold)
        {
            ticketsToPlay = vouchers;
            CloseAddon("HWDSupply");
            EnterPhase(AutomationPhase.MoveToLottery);
            status = $"提交完成，持有 {vouchers} 张票，前往抽奖";
            return;
        }

        BeginNextCraftingCycle($"{recipe.ItemName} 已全部提交");
    }

    private unsafe void DriveOpenLottery()
    {
        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待库啵好运道界面初始化";
                if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
                {
                    FailAutomation("HWDLottery 界面已显示但未完成初始化。");
                }
                return;
            }

            PrepareLotteryCard();
            return;
        }

        if (DateTime.UtcNow >= nextActionAt)
        {
            var yesNoBase = GetAddon("SelectYesno");
            var talk = GetAddon("Talk");
            if (yesNoBase != null)
            {
                var yesNo = (AddonSelectYesno*)yesNoBase;
                if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
                {
                    ClickButton(yesNoBase, yesNo->YesButton);
                }
            }
            else if (talk != null)
            {
                talk->FireCallbackInt(0);
            }

            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(15)))
        {
            FailAutomation("未能打开 HWDLottery 抽奖界面。");
        }
    }

    private unsafe void DrivePlayLottery()
    {
        var addon = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (addon == null)
        {
            if (lotteryTicketCounted)
            {
                EnterPhase(AutomationPhase.PostLottery);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(700);
                status = $"已完成一张票，剩余 {ticketsToPlay} 张";
            }
            else if (PhaseTimedOut(TimeSpan.FromSeconds(10)))
            {
                FailAutomation("抽奖界面在刮奖前关闭。");
            }
            return;
        }

        if (!addon->AtkUnitBase.IsReady)
        {
            status = "等待库啵好运道界面准备完成";
            return;
        }

        if (addon->Stage >= 2)
        {
            lotteryScratched = true;
        }

        if (lotteryTicketCounted && addon->Stage < 2 && ticketsToPlay > 0)
        {
            PrepareLotteryCard();
            return;
        }

        if (addon->Stage == 3 && addon->CloseButton != null && addon->CloseButton->IsEnabled)
        {
            if (!lotteryTicketCounted)
            {
                ticketsToPlay = Math.Max(0, ticketsToPlay - 1);
                lotteryTicketCounted = true;
            }

            if (DateTime.UtcNow >= nextActionAt)
            {
                ClickButton(&addon->AtkUnitBase, addon->CloseButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
                status = $"本张票已揭晓，正在关闭结果（剩余 {ticketsToPlay} 张）";
            }
        }
        else if (addon->Stage == 2)
        {
            status = $"库啵好运道揭晓中（剩余 {ticketsToPlay} 张）";
        }
        else if (!lotteryScratched && DateTime.UtcNow >= nextActionAt)
        {
            var evt = new AtkEvent { Param = RIGHT_LOTTERY_EVENT_PARAM };
            var eventData = default(AtkEventData);
            addon->AtkUnitBase.ReceiveEvent(
                AtkEventType.ButtonClick,
                RIGHT_LOTTERY_EVENT_PARAM,
                &evt,
                &eventData);
            lotteryScratchAttempts++;
            nextActionAt = DateTime.UtcNow.AddMilliseconds(1500);
            status = $"已触发右侧三格按钮，第 {lotteryScratchAttempts} 次尝试，等待界面确认";
        }

        if (PhaseTimedOut(TimeSpan.FromSeconds(30)))
        {
            FailAutomation("等待库啵好运道揭晓超时。");
        }
    }

    private unsafe void DrivePostLottery(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        var lottery = (AddonHWDLottery*)GetAddon("HWDLottery");
        if (lottery != null)
        {
            if (!lottery->AtkUnitBase.IsReady)
            {
                status = "等待下一张库啵好运票界面初始化";
                return;
            }

            if (lottery->Stage < 2 && ticketsToPlay > 0)
            {
                PrepareLotteryCard();
            }
            else
            {
                EnterPhase(AutomationPhase.PlayLottery);
                nextActionAt = DateTime.UtcNow;
                status = "正在继续抽奖";
            }
            return;
        }

        var talk = GetAddon("Talk");
        if (talk != null)
        {
            talk->FireCallbackInt(0);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }

        var yesNoBase = GetAddon("SelectYesno");
        if (yesNoBase != null)
        {
            var yesNo = (AddonSelectYesno*)yesNoBase;
            if (yesNo->YesButton != null && yesNo->YesButton->IsEnabled)
            {
                ClickButton(yesNoBase, yesNo->YesButton);
                nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            }
            return;
        }

        if (IsOccupied())
        {
            return;
        }

        if (ticketsToPlay > 0)
        {
            var npc = OmenTools.DService.Instance().ObjectTable
                .FirstOrDefault(o => GetBaseID(o.Address) == LOTTERY_NPC_ID && o.IsTargetable);
            if (npc is null)
            {
                FailAutomation("未找到库啵好运道NPC。");
                return;
            }

            if (!SendNPCEventStart(npc.Address))
            {
                FailAutomation("无法打开库啵好运道NPC界面。");
                return;
            }

            EnterPhase(AutomationPhase.OpenLottery);
            nextActionAt = DateTime.UtcNow.AddMilliseconds(600);
            lotteryScratched = false;
            lotteryTicketCounted = false;
            lotteryScratchAttempts = 0;
            status = $"继续抽奖，剩余 {ticketsToPlay} 张";
            return;
        }

        if (ReadInventory(activeItemID).ItemCount > 0)
        {
            EnterPhase(AutomationPhase.MoveToAppraiser);
            status = $"抽奖完成，返回提交剩余的 {recipe.ItemName}";
        }
        else
        {
            BeginNextCraftingCycle("物品提交及库啵好运道抽奖完成");
        }
    }

    private unsafe void BeginNextCraftingCycle(string reason)
    {
        CloseAddon("Request");
        CloseAddon("HWDSupply");
        EnterPhase(AutomationPhase.PrepareNextCycle);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = $"{reason}；准备下一轮生产";
    }

    private unsafe void DrivePrepareNextCycle(RecipeOption recipe)
    {
        if (DateTime.UtcNow < nextActionAt)
        {
            return;
        }

        if (GetAddon("Request") != null || GetAddon("HWDSupply") != null)
        {
            CloseAddon("Request");
            CloseAddon("HWDSupply");
            nextActionAt = DateTime.UtcNow.AddMilliseconds(500);
            status = "正在关闭提交界面，准备下一轮生产";
            return;
        }

        if (IsOccupied())
        {
            status = "等待交互结束，准备下一轮生产";
            if (PhaseTimedOut(TimeSpan.FromSeconds(20)))
            {
                FailAutomation("准备下一轮生产时，玩家长时间处于不可操作状态。");
            }
            return;
        }

        StartCraftingCycle(recipe, "开始下一轮");
    }

    private void PrepareLotteryCard()
    {
        EnterPhase(AutomationPhase.PlayLottery);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        status = $"准备选择右侧三个格子，剩余 {ticketsToPlay} 张";
    }

    private bool TryDetectCompletedTurnIn(out int currentCount, out uint currentScrips)
    {
        currentCount = ReadInventory(activeItemID).ItemCount;
        currentScrips = ReadSkybuildersScrips();
        return currentCount < itemCountBeforeTurnIn || currentScrips > scripCountBeforeTurnIn;
    }

    private void EnterWaitSupplyRefresh(int currentCount, uint currentScrips)
    {
        var detectedByScrips = currentScrips > scripCountBeforeTurnIn;
        itemCountBeforeTurnIn = currentCount;
        scripCountBeforeTurnIn = currentScrips;
        EnterPhase(AutomationPhase.WaitSupplyRefresh);
        nextActionAt = DateTime.UtcNow.AddMilliseconds(800);
        status = detectedByScrips
            ? $"已通过天穹街振兴票变化确认提交，等待界面刷新（剩余 {currentCount} 个）"
            : $"已提交 1 件，等待提交界面刷新（剩余 {currentCount} 个）";
    }

    private static unsafe uint ReadSkybuildersScrips()
    {
        var manager = CurrencyManager.Instance();
        return manager == null ? 0 : manager->GetItemCount(SKYBUILDERS_SCRIP_ITEM_ID);
    }

    private static unsafe bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null)
        {
            return false;
        }

        var node = button->AtkComponentBase.OwnerNode;
        if (node == null)
        {
            return false;
        }

        var registered = node->AtkResNode.AtkEventManager.Event;
        if (registered == null)
        {
            return false;
        }

        var evt = default(AtkEvent);
        evt.Target = (AtkEventTarget*)node;
        evt.Listener = (AtkEventListener*)addon;
        var eventData = default(AtkEventData);
        addon->ReceiveEvent(registered->State.EventType, (int)registered->Param, &evt, &eventData);
        return true;
    }

    private static unsafe uint GetBaseID(nint address) =>
        address == nint.Zero ? 0 : ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->BaseId;

    private static unsafe bool SendNPCEventStart(nint address)
    {
        var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address;
        if (gameObject == null || !TryResolveNPCEventID(gameObject, out var eventID))
        {
            return false;
        }

        var objectID = gameObject->GetGameObjectId();
        new EventStartPackt(objectID, eventID).Send();
        return true;
    }

    private static unsafe bool TryResolveNPCEventID(
        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* gameObject,
        out GameEventID eventID)
    {
        eventID = default;

        if (gameObject->EventId.Id != 0)
        {
            eventID = gameObject->EventId;
            if (eventID.ContentId == GameEventHandlerContent.HwdDev)
            {
                return true;
            }
        }

        if (gameObject->EventHandler != null)
        {
            var primary = gameObject->EventHandler->GetEventId();
            if (primary.Id != 0)
            {
                if (primary.ContentId == GameEventHandlerContent.HwdDev)
                {
                    eventID = primary;
                    return true;
                }

                if (eventID.Id == 0)
                {
                    eventID = primary;
                }
            }
        }

        var handlers = stackalloc GameEventHandler*[32];
        var handlerCount = Math.Clamp(gameObject->GetEventHandlersImpl(handlers), 0, 32);
        for (var index = 0; index < handlerCount; index++)
        {
            var handler = handlers[index];
            if (handler == null)
            {
                continue;
            }

            var candidate = handler->GetEventId();
            if (candidate.Id == 0)
            {
                continue;
            }

            if (candidate.ContentId == GameEventHandlerContent.HwdDev)
            {
                eventID = candidate;
                return true;
            }

            if (eventID.Id == 0)
            {
                eventID = candidate;
            }
        }

        return eventID.Id != 0;
    }

    private static bool IsOccupied()
    {
        var condition = OmenTools.DService.Instance().Condition;
        return condition[ConditionFlag.Occupied]
            || condition[ConditionFlag.OccupiedInEvent]
            || condition[ConditionFlag.OccupiedInQuestEvent]
            || condition[ConditionFlag.Occupied30]
            || condition[ConditionFlag.Occupied33]
            || condition[ConditionFlag.Occupied38]
            || condition[ConditionFlag.Occupied39];
    }

    private static unsafe bool IsPlayerMovable()
    {
        var services = OmenTools.DService.Instance();
        var player = services.ObjectTable.LocalPlayer;
        if (!services.ClientState.IsLoggedIn || player is null)
        {
            return false;
        }

        var condition = services.Condition;
        return !condition[ConditionFlag.BetweenAreas]
            && !condition[ConditionFlag.BetweenAreas51]
            && !condition[ConditionFlag.Occupied]
            && !condition[ConditionFlag.Occupied30]
            && !condition[ConditionFlag.Occupied33]
            && !condition[ConditionFlag.Occupied38]
            && !condition[ConditionFlag.Occupied39]
            && !condition[ConditionFlag.OccupiedInEvent]
            && !condition[ConditionFlag.OccupiedInQuestEvent]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent]
            && !condition[ConditionFlag.OccupiedSummoningBell]
            && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.WatchingCutscene78]
            && !condition[ConditionFlag.Unconscious]
            && !condition[ConditionFlag.Casting]
            && !condition[ConditionFlag.Casting87]
            && !condition[ConditionFlag.Mounting]
            && !condition[ConditionFlag.Mounting71]
            && !condition[ConditionFlag.BeingMoved]
            && !condition[ConditionFlag.InThatPosition]
            && GetAddon("NowLoading") == null
            && GetAddon("FadeMiddle") == null
            && GetAddon("FadeBack") == null;
    }

    private static bool TrySendCommand(string command)
    {
        try
        {
            return OmenTools.DService.Instance().Command.ProcessCommand(command);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatPosition(Vector3 position) =>
        $"{position.X.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{position.Y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        position.Z.ToString("0.0", CultureInfo.InvariantCulture);

    private static unsafe AtkUnitBase* GetAddon(string name)
    {
        var addon = OmenTools.DService.Instance().GameGUI.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible ? addon : null;
    }

    private static unsafe void CloseAddon(string name)
    {
        var addon = GetAddon(name);
        if (addon != null)
        {
            addon->Close(true);
        }
    }

    private static unsafe int FindSupplyRow(AtkUnitBase* addon, uint itemID)
    {
        var target = itemID + 500000;
        const int INDEX_OFFSET = 18;
        for (var i = 0; i + INDEX_OFFSET < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.UInt || value.UInt != target)
            {
                continue;
            }

            ref var index = ref addon->AtkValues[i + INDEX_OFFSET];
            return index.Type == ValueType.UInt ? (int)index.UInt : -1;
        }

        return -1;
    }

    private static unsafe bool TryReadVoucherCount(out int current, out int limit)
    {
        current = -1;
        limit = -1;
        var addon = GetAddon("HWDSupply");
        if (addon == null)
        {
            return false;
        }

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            ref var value = ref addon->AtkValues[i];
            if (value.Type != ValueType.String || value.String.Value == null)
            {
                continue;
            }

            var text = Marshal.PtrToStringUTF8((nint)value.String.Value);
            if (text is null)
            {
                continue;
            }

            var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var match = VOUCHER_PATTERN.Match(normalized);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var count)
                && int.TryParse(match.Groups[2].Value, out var parsedLimit)
                && parsedLimit == 10)
            {
                current = count;
                limit = 10;
                return true;
            }
        }

        return false;
    }

    private unsafe void UpdateDebugCounters(DateTime now)
    {
        if (now < nextDebugReadAt)
        {
            return;
        }

        nextDebugReadAt = now.AddMilliseconds(500);
        debugVoucherCount = -1;
        debugVoucherLimit = -1;

        TryReadVoucherCount(out debugVoucherCount, out debugVoucherLimit);
    }

    private static string FormatDebugCounter(int count, int limit) =>
        count >= 0 && limit >= 0 ? $"{count}/{limit}" : "未识别";

    private void EnterPhase(AutomationPhase next)
    {
        phase = next;
        phaseStartedAt = DateTime.UtcNow;
    }

    private bool PhaseTimedOut(TimeSpan timeout) => DateTime.UtcNow - phaseStartedAt > timeout;

    private void CompleteAutomation(string message)
    {
        running = false;
        phase = AutomationPhase.Idle;
        activeRecipeID = 0;
        activeItemID = 0;
        ticketsToPlay = 0;
        lotteryScratched = false;
        lotteryTicketCounted = false;
        lotteryScratchAttempts = 0;
        craftCycleStartingItemCount = 0;
        movableSince = null;
        initialDestination = default;
        status = message;
    }

    private void FailAutomation(string message)
    {
        StopOwnedArtisan();
        StopOwnedVnavPath();

        running = false;
        phase = AutomationPhase.Idle;
        lastError = message;
        status = "自动流程已停止";
    }

    private void StopOwnedArtisan()
    {
        if (!artisanStartedByModule)
        {
            return;
        }

        try
        {
            if (getStopRequest?.InvokeFunc() != true)
            {
                setStopRequest?.InvokeAction(true);
            }
        }
        catch (Exception ex)
        {
            lastError = $"停止 Artisan 失败：{ex.Message}";
        }
        finally
        {
            artisanStartedByModule = false;
        }
    }

    private void StopOwnedVnavPath()
    {
        if (!vnavPathStartedByModule)
        {
            return;
        }

        try
        {
            vnavmeshIPC.StopPathfind();
        }
        catch (Exception ex)
        {
            lastError = $"停止导航失败：{ex.Message}";
        }
        finally
        {
            vnavPathStartedByModule = false;
        }
    }

    private bool ShouldStop(InventorySnapshot snapshot, out string reason)
    {
        if (config.StopMode == 0 && snapshot.FreeSlots <= config.MinimumFreeSlots)
        {
            reason = $"背包只剩 {snapshot.FreeSlots} 格，已停止生产并准备提交";
            return true;
        }

        if (config.StopMode == 1 && snapshot.ItemCount >= config.TargetItemCount)
        {
            reason = $"目标物品已达到 {snapshot.ItemCount} 个，已停止生产并准备提交";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static InventorySnapshot ReadInventory(uint itemID)
    {
        var freeSlots = 0;
        var totalSlots = 0;
        var itemCount = 0;
        foreach (var type in MainInventoryTypes)
        {
            var items = OmenTools.DService.Instance().GameInventory.GetInventoryItems(type);
            totalSlots += items.Length;
            foreach (ref readonly var item in items)
            {
                if (item.IsEmpty)
                {
                    freeSlots++;
                }
                else if (item.BaseItemId == itemID)
                {
                    itemCount += item.Quantity;
                }
            }
        }

        return new InventorySnapshot(freeSlots, totalSlots, itemCount);
    }

    private void Fail(string message)
    {
        lastError = message;
        status = "无法开始";
    }

    private static RecipeOption? FindRecipe(uint recipeID)
    {
        foreach (var recipe in Recipes)
        {
            if (recipe.RecipeID == recipeID)
            {
                return recipe;
            }
        }

        return null;
    }

    private static string GetJobName(uint jobID) =>
        LuminaGetter.TryGetRow<ClassJob>(jobID, out var job)
            ? job.Name.ExtractText()
            : $"职业 #{jobID}";

    private static string FormatRecipe(RecipeOption recipe) =>
        $"{recipe.Level}级{(recipe.IsExpert ? "高难度" : string.Empty)} · {recipe.ItemName}" +
        (recipe.Level == 80 ? "（可以获得库啵好运章）" : string.Empty);

    private readonly record struct RecipeOption(
        uint RecipeID,
        uint ItemID,
        uint JobID,
        int Level,
        bool IsExpert,
        string ItemName);

    private readonly record struct InventorySnapshot(int FreeSlots, int TotalSlots, int ItemCount);

    private enum AutomationPhase
    {
        Idle,
        WaitForFirmament,
        WaitForPlayerMovable,
        MoveToInitialPoint,
        WaitArtisanStart,
        Crafting,
        WaitArtisanStop,
        MoveToAppraiser,
        OpenAppraiser,
        SelectJob,
        SelectItem,
        FillRequest,
        WaitTurnIn,
        WaitSupplyRefresh,
        MoveToLottery,
        OpenLottery,
        PlayLottery,
        PostLottery,
        PrepareNextCycle
    }
}

[Serializable]
public sealed class AutoIshgardRestorationConfig
{
    [JsonPropertyName("SelectedRecipeId")]
    public uint SelectedRecipeID { get; set; }

    public int StopMode { get; set; }

    public int MinimumFreeSlots { get; set; } = 10;

    public int TargetItemCount { get; set; } = 30;

    public int TicketThreshold { get; set; } = 5;
}
