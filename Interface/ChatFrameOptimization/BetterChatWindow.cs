using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Threading;
using Dalamud.Game.Text;
using Dalamud.Game.Text.Evaluator;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using KamiToolKit.Timelines;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class BetterChatWindow : IDisposable
{
    private const float GAP = 4f;
    private const float CONTENT_PADDING = 4f;

    private readonly SortedDictionary<int, ChatLineLayout> messages = [];
    private readonly List<ChatLineLayout> visibleMessages = [];
    private readonly List<ChatLineNode> messageNodes = [];
    private readonly List<int> expiredIndices = [];
    private readonly List<TextNode> tabButtons = [];
    private IReadOnlyList<BetterChatTabConfig> tabConfigs = [];
    private ResNode? root;
    private ResNode? messageRoot;
    private CircleButtonNode? previousTabs;
    private CircleButtonNode? nextTabs;
    private CircleButtonNode? addTabButton;
    private CircleButtonNode? settingsButton;
    private TextNode? selectedIndicator;
    private SimpleComponentNode? messageClip;
    private ScrollBarNode? messageScrollBar;
    private SimpleNineGridNode? scrollTrack;
    private ChatLineNode? measureNode;
    private ChatLineNode? hoveredNode;
    private Vector2 nativeMessageSize;
    private Vector2 tabPosition;
    private Vector2 addTabPosition;
    private Vector2 addTabSize;
    private float tabHeight = 24f;
    private float nativeScale = 1f;
    private int selectedTab;
    private int firstVisibleTab;
    private int nextVisibleTab;
    private int lastNativeTabIndex = -1;
    private nint logModuleAddress;
    private ulong logContentID;
    private int observedCount;
    private int retainedFirstIndex;
    private int nextIncrementalIndex;
    private int incrementalFloor;
    private int incrementalCursor = -1;
    private int backfillCursor = -1;
    private int pendingReset;
    private int messagesNotification;
    private long remainingWorkTicks;
    private int formattedRevision;
    private int measurementRevision;
    private int filterRevision;
    private int measureCursor = -1;
    private int contentHeight;
    private int scrollPosition;
    private ushort hoveredLinkIndex;
    private uint hoveredLinkGroupID;
    private int hoveredEventParam;
    private bool messagesDirty;
    private bool scrollToBottom;
    private bool tabsDirty;
    private bool layoutDirty;
    private bool reflowDirty;
    private bool showTime;
    private bool use12HourClock;
    private bool useServerTime;
    private int serverTimeOffset;
    private TextAppearance messageAppearance = new(14, 17, FontType.Axis, ColorHelper.GetColor(8), ColorHelper.GetColor(7));
    private TextAppearance tabAppearance = new(14, 17, FontType.Axis, ColorHelper.GetColor(50), ColorHelper.GetColor(7));

    public int SelectedTabIndex => selectedTab;

    public bool IsOpen => root is not null;

    public ushort AttachedPanelID { get; private set; }

    public bool UsesNativeMessages => selectedTab < tabConfigs.Count && tabConfigs[selectedTab].NativeTabIndex >= 0;

    public System.Action? OnAddTab { get; set; }

    public System.Action? OnOpenSettings { get; set; }

    public void SetTabs(IReadOnlyList<BetterChatTabConfig> value)
    {
        tabConfigs = value;
        selectedTab = Math.Clamp(selectedTab, 0, Math.Max(0, value.Count - 1));
        firstVisibleTab = Math.Clamp(firstVisibleTab, 0, Math.Max(0, value.Count - 1));
        tabsDirty = true;
        messagesDirty = true;
        filterRevision++;
    }

    public void SyncToNative(AddonChatLog* source, AddonChatLogPanel* panel)
    {
        if (source is null || source->RootNode is null || source->BackgroundNode is null ||
            source->AddTabComponentNode is null || panel is null || !panel->IsReady)
            return;

        // 扩展消息和页签共享原生根节点的坐标与缩放。
        var scale = source->RootNode->GetScale();
        if (scale.X <= 0f || scale.Y <= 0f)
            return;

        var sourceMessageArea = panel is not null && panel->ChatComponent is not null
            ? (AtkResNode*)panel->ChatComponent->OwnerNode
            : null;
        if (sourceMessageArea is null || sourceMessageArea->ParentNode is null || panel->ChatText is null)
            return;
        if (root is not null && AttachedPanelID != panel->Id)
            Close((AtkUnitBase*)source);
        if (root is null)
            Open(source, panel);
        var position = sourceMessageArea->GetPosition();
        var localPosition = (position - source->RootNode->GetPosition()) / scale;
        var messageSize = sourceMessageArea->GetSize() / scale;
        var sourceAdd = (AtkResNode*)source->AddTabComponentNode;
        var addPosition = (sourceAdd->GetPosition() - position) / scale;
        var controlSize = sourceAdd->GetSize() / scale;
        var firstTab = source->ChatTabs[0].Value;
        var sourceTab = firstTab is null ? null : (AtkResNode*)firstTab->OwnerNode;
        var tabsPosition = sourceTab is null ? new Vector2(CONTENT_PADDING, addPosition.Y) : (sourceTab->GetPosition() - position) / scale;
        var rowHeight = sourceTab is null ? controlSize.Y : sourceTab->GetSize().Y / scale.Y;
        var footerBottom = MathF.Max(tabsPosition.Y + rowHeight, addPosition.Y + controlSize.Y);
        if (source->TextInput is not null)
        {
            var sourceInput = (AtkResNode*)source->TextInput->OwnerNode;
            if (sourceInput is not null)
            {
                var inputY = (sourceInput->GetPosition().Y - position.Y) / scale.Y;
                if (inputY > 0f)
                    messageSize.Y = MathF.Min(messageSize.Y, inputY - GAP);
                footerBottom = MathF.Max(footerBottom, (sourceInput->GetPosition().Y - position.Y + sourceInput->GetSize().Y) / scale.Y);
            }
        }

        var messageLocalSize = new Vector2(MathF.Floor(messageSize.X), MathF.Floor(messageSize.Y));

        var size = new Vector2(MathF.Floor(messageSize.X), MathF.Floor(MathF.Max(messageSize.Y, footerBottom)));
        if (messageSize.X <= 0f || messageSize.Y <= 0f)
            return;

        if (root!.Position != localPosition || nativeMessageSize != messageLocalSize || root.Size != size ||
            nativeScale != scale.Y || tabPosition != tabsPosition || tabHeight != rowHeight ||
            addTabPosition != addPosition || addTabSize != controlSize)
        {
            layoutDirty |= nativeScale != scale.Y || tabPosition != tabsPosition || tabHeight != rowHeight ||
                           addTabPosition != addPosition || addTabSize != controlSize;
            root.Position = localPosition;
            nativeMessageSize = messageLocalSize;
            nativeScale = scale.Y;
            root.Size = size;
            tabPosition = tabsPosition;
            tabHeight = rowHeight;
            addTabPosition = addPosition;
            addTabSize = controlSize;
        }

        var textNode = (AtkResNode*)panel->ChatText;
        messageRoot!.Position = textNode->Position;
        messageRoot.Size = textNode->Size;
        messageRoot.Scale = textNode->Scale;
        var textSize = new Vector2(MathF.Max(1f, textNode->Width), MathF.Max(1f, textNode->Height));
        if (messageClip!.Size != textSize)
        {
            scrollToBottom |= IsAtBottom();
            reflowDirty |= messageClip.Width != textSize.X;
            messageClip.Size = textSize;
        }
        var scrollBar = panel->LogViewer.ScrollBarNode;
        if (scrollBar is not null)
        {
            messageScrollBar!.Position = (((AtkResNode*)scrollBar)->GetPosition() - textNode->GetPosition()) / textNode->GetScale();
            messageScrollBar.Size = ((AtkResNode*)scrollBar)->GetSize() / textNode->GetScale();
            scrollTrack!.Size = messageScrollBar.Size;
            var nativeScrollBar = (AtkComponentScrollBar*)scrollBar->Component;
            messageScrollBar.Component->MinThumbLength = nativeScrollBar->MinThumbLength;
        }

        if (firstTab is not null)
            SyncTabAppearance(firstTab->ButtonTextNode);

        var sourceSettings = (AtkResNode*)source->SettingsComponentNode;
        if (settingsButton is not null && sourceSettings is not null)
        {
            settingsButton.Position = (sourceSettings->GetPosition() - position) / scale;
            settingsButton.Size = sourceSettings->GetSize() / scale;
        }
    }

    public void SyncAppearance(AtkTextNode* source, uint? configuredFontSize = null)
    {
        if (source is null)
            return;

        var appearance = ReadTextAppearance(source, ((AtkResNode*)source)->GetScale().Y);
        if (configuredFontSize is >= 8 and <= 64)
        {
            var fontSize = configuredFontSize.Value;
            appearance = appearance with { FontSize = fontSize, LineSpacing = Math.Max(fontSize, appearance.LineSpacing) };
        }
        if (messageAppearance == appearance)
            return;

        messageAppearance = appearance;
        reflowDirty = true;
        formattedRevision++;
    }

    public void SyncTabAppearance(AtkTextNode* source)
    {
        if (source is null)
            return;

        var appearance = ReadTextAppearance(source, nativeScale);
        if (tabAppearance == appearance)
            return;

        tabAppearance = appearance;
        tabsDirty = true;
    }

    private static TextAppearance ReadTextAppearance(AtkTextNode* source, float parentScale)
    {
        // ChatLogPanel 与 ChatLog 的缩放可以不同，转换到替代窗口的局部字号。
        var scale = ((AtkResNode*)source)->GetScale().Y / parentScale;
        return new(
            (uint)Math.Clamp(MathF.Round(source->FontSize * scale), 1f, 255f),
            (uint)Math.Clamp(MathF.Round(source->LineSpacing * scale), 1f, 255f),
            source->FontType,
            ByteColorExtensions.ToVector4(source->TextColor),
            ByteColorExtensions.ToVector4(source->EdgeColor));
    }

    public void SyncNativeTabIndex(int nativeTabIndex)
    {
        if (lastNativeTabIndex == nativeTabIndex)
            return;

        lastNativeTabIndex = nativeTabIndex;
        for (var index = 0; index < tabConfigs.Count; index++)
        {
            if (tabConfigs[index].NativeTabIndex != nativeTabIndex)
                continue;

            SelectTab(index, false);
            return;
        }
    }

    public void SelectTab(int index, bool updateNative = true)
    {
        if (index < 0 || index >= tabConfigs.Count)
            return;

        selectedTab = index;
        if (selectedTab < firstVisibleTab || selectedTab >= nextVisibleTab)
            firstVisibleTab = selectedTab;
        tabsDirty = true;
        messagesDirty = true;
        filterRevision++;
        scrollToBottom = true;

        if (!updateNative || tabConfigs[index].NativeTabIndex < 0)
            return;

        var source = AddonHelper.GetByName<AddonChatLog>("ChatLog");
        if (source is not null && source->IsReady && tabConfigs[index].NativeTabIndex < source->TabCount)
        {
            source->ChangeTab(tabConfigs[index].NativeTabIndex);
            lastNativeTabIndex = source->TabIndex;
        }
    }

    public void SyncTimeSettings(bool displayTime, bool twelveHourClock, bool serverTime)
    {
        var offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalSeconds;
        if (showTime == displayTime && use12HourClock == twelveHourClock && useServerTime == serverTime &&
            (!serverTime || serverTimeOffset == offset))
            return;
        showTime = displayTime;
        use12HourClock = twelveHourClock;
        useServerTime = serverTime;
        serverTimeOffset = offset;
        reflowDirty = true;
        formattedRevision++;
    }

    public void NotifyMessagesChanged() => Interlocked.Exchange(ref messagesNotification, 1);

    public void ResetMessages() => Interlocked.Exchange(ref pendingReset, 1);

    public void PollMessages()
    {
        var started = Stopwatch.GetTimestamp();
        remainingWorkTicks = Stopwatch.Frequency / 500;
        var reset = Interlocked.Exchange(ref pendingReset, 0) != 0;
        var notified = Interlocked.Exchange(ref messagesNotification, 0) != 0;
        var logModule = RaptureLogModule.Instance();
        if (logModule is null)
        {
            if (reset)
                ClearMessages(0, 0);
            remainingWorkTicks = Math.Max(0, remainingWorkTicks - (Stopwatch.GetTimestamp() - started));
            return;
        }

        var count = Math.Max(0, logModule->LogMessageCount);
        var retainedCount = logModule->GetLogMessageCount();
        var first = retainedCount == 0 ? count : Math.Max(0, logModule->GetLogMessageOverflow());
        if (reset || logModuleAddress != (nint)logModule || logContentID != logModule->LocalPlayerContentId ||
            count < observedCount || first < retainedFirstIndex)
        {
            ClearMessages(count, first);
            logModuleAddress = (nint)logModule;
            logContentID = logModule->LocalPlayerContentId;
        }
        else if (!notified && count == observedCount && first == retainedFirstIndex &&
                 incrementalCursor < incrementalFloor && backfillCursor < first)
        {
            remainingWorkTicks = Math.Max(0, remainingWorkTicks - (Stopwatch.GetTimestamp() - started));
            return;
        }

        observedCount = count;
        retainedFirstIndex = first;
        expiredIndices.Clear();
        foreach (var index in messages.Keys)
        {
            if (index >= first)
                break;
            expiredIndices.Add(index);
        }
        foreach (var index in expiredIndices)
            messages.Remove(index);
        messagesDirty |= expiredIndices.Count != 0;
        incrementalFloor = Math.Max(incrementalFloor, first);
        if (incrementalCursor < incrementalFloor && count > nextIncrementalIndex)
        {
            incrementalFloor = Math.Max(first, nextIncrementalIndex);
            incrementalCursor = count - 1;
            nextIncrementalIndex = count;
        }

        // 留一半预算给文本测量；新增区间与历史补读分别保留游标，失败后原位重试。
        var readDeadline = started + remainingWorkTicks / 2;
        while (incrementalCursor >= incrementalFloor && Stopwatch.GetTimestamp() < readDeadline)
        {
            if (!ReadMessage(logModule, incrementalCursor))
                break;
            incrementalCursor--;
        }
        if (incrementalCursor < incrementalFloor)
        {
            while (backfillCursor >= first && Stopwatch.GetTimestamp() < readDeadline)
            {
                if (!ReadMessage(logModule, backfillCursor))
                    break;
                backfillCursor--;
            }
        }
        remainingWorkTicks = Math.Max(0, remainingWorkTicks - (Stopwatch.GetTimestamp() - started));
    }

    private bool ReadMessage(RaptureLogModule* logModule, int index)
    {
        if (messages.ContainsKey(index))
            return true;
        if (!logModule->GetLogMessageDetail(index, out var sender, out var message, out var type,
                out var source, out var target, out var timestamp))
            return false;
        messages.Add(index, new(new(index, type, (byte)source, (byte)target, timestamp, sender, message),
            Math.Max(1, (int)messageAppearance.LineSpacing)));
        messagesDirty = true;
        return true;
    }

    private void ClearMessages(int count, int first)
    {
        messages.Clear();
        messagesDirty = true;
        scrollToBottom = true;
        observedCount = count;
        retainedFirstIndex = first;
        nextIncrementalIndex = count;
        incrementalFloor = count;
        incrementalCursor = count - 1;
        backfillCursor = count - 1;
    }

    public void RefreshColors()
    {
        formattedRevision++;
        reflowDirty = true;
    }

    public void Dispose()
    {
        Close();
        messages.Clear();
    }

    private void Open(AddonChatLog* source, AddonChatLogPanel* panel)
    {
        root = new();
        root.AttachNode((AtkUnitBase*)source);
        messageRoot = new();
        // 正文插入原生文本的绘制位置，保留同一组件内的背景、淡出和尺寸调整按钮。
        messageRoot.AttachNode(panel->ChatText, NodePosition.AfterTarget);
        AttachedPanelID = panel->Id;

        messageClip = new()
        {
            NodeFlags = NodeFlags.Visible | NodeFlags.Clip | NodeFlags.EmitsEvents | NodeFlags.RespondToMouse,
        };
        messageClip.AttachNode(messageRoot);
        messageClip.AddEvent(AtkEventType.MouseWheel, OnMouseWheel);
        messageClip.CollisionNode.AddEvent(AtkEventType.MouseWheel, OnMouseWheel);
        messageScrollBar = new() { ScrollSpeed = 36, HideWhenDisabled = true };
        // 原生滚动条只驱动 32 位逻辑位置，正文节点始终保持视口内的局部坐标。
        messageScrollBar.Component->IsAcceptingMouseWheelEvents = true;
        messageScrollBar.OnValueChanged = SetScrollPosition;
        messageScrollBar.AttachNode(messageRoot);
        SyncScrollBarSkin();
        measureNode = new(this, false) { IsVisible = false };
        measureNode.AttachNode(messageClip);

        previousTabs = new() { Icon = ButtonIcon.LeftArrow };
        previousTabs.CollisionNode.AddEvent(AtkEventType.MouseClick, (listener, type, param, atkEvent, data) =>
        {
            if (data->MouseData.ButtonId != 0)
                return;
            atkEvent->SetEventIsHandled(true);
            firstVisibleTab = Math.Max(0, firstVisibleTab - Math.Max(1, nextVisibleTab - firstVisibleTab));
            tabsDirty = true;
        });
        previousTabs.SetTextTooltip(OmniLoc.Get("Common.Previous"));
        previousTabs.AttachNode(root);
        nextTabs = new() { Icon = ButtonIcon.RightArrow };
        nextTabs.CollisionNode.AddEvent(AtkEventType.MouseClick, (listener, type, param, atkEvent, data) =>
        {
            if (data->MouseData.ButtonId != 0)
                return;
            atkEvent->SetEventIsHandled(true);
            firstVisibleTab = Math.Min(nextVisibleTab, Math.Max(0, tabConfigs.Count - 1));
            tabsDirty = true;
        });
        nextTabs.SetTextTooltip(OmniLoc.Get("Common.Next"));
        nextTabs.AttachNode(root);

        addTabButton = new() { Icon = ButtonIcon.Add };
        addTabButton.CollisionNode.AddEvent(AtkEventType.MouseClick, (listener, type, param, atkEvent, data) =>
        {
            if (data->MouseData.ButtonId != 0)
                return;
            atkEvent->SetEventIsHandled(true);
            OnAddTab?.Invoke();
        });
        addTabButton.SetTextTooltip(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.AddTab"));
        addTabButton.AttachNode(root);
        settingsButton = new() { Icon = ButtonIcon.GearCog };
        settingsButton.CollisionNode.AddEvent(AtkEventType.MouseClick, (listener, type, param, atkEvent, data) =>
        {
            if (data->MouseData.ButtonId != 0)
                return;
            atkEvent->SetEventIsHandled(true);
            OnOpenSettings?.Invoke();
        });
        settingsButton.SetTextTooltip(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.ExtendedFilters"));
        settingsButton.AttachNode(root);
        selectedIndicator = new()
        {
            String = "•",
            AlignmentType = AlignmentType.Center,
            TextColor = KnownColor.PaleGoldenrod.ToVector4(),
        };
        selectedIndicator.AttachNode(root);

        messagesDirty = true;
        tabsDirty = true;
        layoutDirty = true;
        reflowDirty = true;
    }

    public void Close() => Close((AtkUnitBase*)AddonHelper.GetByName<AddonChatLog>("ChatLog"));

    public void Close(AtkUnitBase* addon)
    {
        ClearHoveredLink();
        if (root is not null)
        {
            var panel = RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID);
            // 悬停缓存可能指向任意子碰撞节点，释放整棵挂接节点前一起清除。
            var collisions = AtkStage.Instance()->AtkCollisionManager;
            if (collisions is not null)
            {
                var hovered = (AtkResNode*)collisions->IntersectingCollisionNode;
                var messageNode = messageRoot is null ? null : (AtkResNode*)messageRoot;
                while (hovered is not null && hovered != (AtkResNode*)root && hovered != messageNode)
                    hovered = hovered->ParentNode;
                if (hovered is not null)
                {
                    collisions->IntersectingCollisionNode = null;
                    collisions->IntersectingAddon = null;
                }
            }

            root.HideTooltip();
            messageRoot?.HideTooltip();
            NativeNodeDetach.DetachAndDestroyComponent(previousTabs);
            NativeNodeDetach.DetachAndDestroyComponent(nextTabs);
            NativeNodeDetach.DetachAndDestroyComponent(addTabButton);
            NativeNodeDetach.DetachAndDestroyComponent(settingsButton);
            foreach (var node in messageNodes)
            {
                NativeNodeDetach.DetachAndDestroy(node);
                node.ReleaseOriginalText();
            }
            if (measureNode is not null)
            {
                NativeNodeDetach.DetachAndDestroy(measureNode);
                measureNode.ReleaseOriginalText();
            }
            NativeNodeDetach.DetachAndDestroyComponent(messageScrollBar);
            NativeNodeDetach.DetachAndDestroyComponent(messageClip);
            NativeNodeDetach.DetachAndDestroy(messageRoot);
            NativeNodeDetach.DetachAndDestroy(root);
            NativeNodeDetach.UpdateNodeLists(addon);
            NativeNodeDetach.UpdateNodeLists(panel);
        }

        tabButtons.Clear();
        root = null;
        messageRoot = null;
        previousTabs = null;
        nextTabs = null;
        addTabButton = null;
        settingsButton = null;
        selectedIndicator = null;
        messageNodes.Clear();
        messageClip = null;
        messageScrollBar = null;
        scrollTrack = null;
        measureNode = null;
        nativeMessageSize = Vector2.Zero;
        AttachedPanelID = 0;
        lastNativeTabIndex = -1;
    }

    public void UpdateContent(AddonChatLog* source)
    {
        if (messageClip is null)
            return;

        if (messageRoot!.IsVisible == UsesNativeMessages)
            ClearHoveredLink();
        messageRoot!.IsVisible = !UsesNativeMessages;
        settingsButton!.IsVisible = !UsesNativeMessages;
        if (tabsDirty || layoutDirty)
            RebuildTabs((AtkUnitBase*)source);
        if (UsesNativeMessages)
        {
            layoutDirty = false;
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var followBottom = scrollToBottom || IsAtBottom();
        var anchor = GetScrollAnchor();
        var positionsChanged = messagesDirty || reflowDirty;
        if (messagesDirty)
        {
            visibleMessages.Clear();
            if (selectedTab < tabConfigs.Count)
            {
                var tab = tabConfigs[selectedTab];
                foreach (var layout in messages.Values)
                {
                    var line = layout.Line;
                    if (layout.FilterRevision != filterRevision)
                    {
                        layout.MatchesFilter = BetterChatFilters.Matches(tab, line.Type, line.Source, line.Target);
                        layout.FilterRevision = filterRevision;
                    }
                    if (layout.MatchesFilter)
                        visibleMessages.Add(layout);
                }
            }
            measureCursor = visibleMessages.Count - 1;
        }
        if (reflowDirty)
        {
            measurementRevision++;
            measureCursor = visibleMessages.Count - 1;
        }
        if (positionsChanged)
        {
            UpdateLinePositions();
            RestoreScrollAnchor(anchor, followBottom);
        }

        var deadline = started + remainingWorkTicks;
        // 优先测量当前视口，再从最新行向旧历史推进，逐帧更新换行高度。
        if (followBottom)
        {
            for (var index = visibleMessages.Count - 1; index >= 0 && Stopwatch.GetTimestamp() < deadline; index--)
            {
                var layout = visibleMessages[index];
                if (layout.Top + layout.Height < scrollPosition - (int)messageClip.Height)
                    break;
                positionsChanged |= MeasureLine(layout);
            }
        }
        else
        {
            var firstVisible = FindLineAt(Math.Max(0, scrollPosition - (int)messageClip.Height));
            for (var index = firstVisible; index < visibleMessages.Count && Stopwatch.GetTimestamp() < deadline; index++)
            {
                var layout = visibleMessages[index];
                if (layout.Top > scrollPosition + (int)messageClip.Height * 2)
                    break;
                positionsChanged |= MeasureLine(layout);
            }
        }
        while (measureCursor >= 0 && Stopwatch.GetTimestamp() < deadline)
            positionsChanged |= MeasureLine(visibleMessages[measureCursor--]);
        remainingWorkTicks = Math.Max(0, remainingWorkTicks - (Stopwatch.GetTimestamp() - started));

        if (positionsChanged)
        {
            UpdateLinePositions();
            RestoreScrollAnchor(anchor, followBottom);
        }
        SyncScrollBar();
        RenderViewport();
        RefreshHoveredLink();
        messagesDirty = false;
        scrollToBottom = false;
        layoutDirty = false;
        reflowDirty = false;
    }

    private bool MeasureLine(ChatLineLayout layout)
    {
        if (layout.MeasurementRevision == measurementRevision)
            return false;
        ApplyTextAppearance(measureNode!, messageAppearance);
        measureNode!.Resize(messageClip!.Width, GetDisplayText(layout));
        layout.Height = (int)measureNode.Height;
        layout.MeasurementRevision = measurementRevision;
        return true;
    }

    private void UpdateLinePositions()
    {
        var height = 0;
        foreach (var layout in visibleMessages)
        {
            layout.Top = height;
            height += layout.Height;
        }
        contentHeight = height;
    }

    private int FindLineAt(int position)
    {
        var first = 0;
        var end = visibleMessages.Count;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            var line = visibleMessages[middle];
            if (line.Top + line.Height <= position)
                first = middle + 1;
            else
                end = middle;
        }
        return first;
    }

    private (int Index, int Offset) GetScrollAnchor()
    {
        var index = FindLineAt(scrollPosition);
        return index < visibleMessages.Count
            ? (visibleMessages[index].Line.Index, scrollPosition - visibleMessages[index].Top)
            : (-1, 0);
    }

    private void RestoreScrollAnchor((int Index, int Offset) anchor, bool followBottom)
    {
        if (followBottom)
        {
            scrollPosition = GetMaximumScroll();
            return;
        }
        var first = 0;
        var end = visibleMessages.Count;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (visibleMessages[middle].Line.Index < anchor.Index)
                first = middle + 1;
            else
                end = middle;
        }
        if (first < visibleMessages.Count)
        {
            var line = visibleMessages[first];
            scrollPosition = line.Top + Math.Min(anchor.Offset, line.Height - 1);
        }
        scrollPosition = Math.Clamp(scrollPosition, 0, GetMaximumScroll());
    }

    private int GetMaximumScroll() => Math.Max(0, contentHeight - (int)(messageClip?.Height ?? 0f));

    private bool IsAtBottom() => scrollPosition >= GetMaximumScroll() - 2;

    private void SetScrollPosition(int position)
    {
        position = Math.Clamp(position, 0, GetMaximumScroll());
        if (scrollPosition == position)
            return;
        scrollPosition = position;
        scrollToBottom = false;
        SyncScrollBar();
    }

    private void OnMouseWheel(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* atkEvent, AtkEventData* data)
    {
        atkEvent->SetEventIsHandled(true);
        SetScrollPosition(scrollPosition - data->MouseData.WheelDirection * 36);
    }

    private void SyncScrollBar()
    {
        if (messageScrollBar is null)
            return;
        var component = messageScrollBar.Component;
        var maximum = GetMaximumScroll();
        var length = Math.Max(1, (int)messageScrollBar.Height);
        var thumbLength = maximum == 0 ? length : Math.Clamp(
            (int)((long)length * (int)messageClip!.Height / Math.Max(1, contentHeight)),
            Math.Min(length, Math.Max(1, (int)component->MinThumbLength)), length);
        component->ScrollMaxPosition = maximum;
        component->ScrollbarLength = (short)length;
        component->ContentNodeOffScreenLength = 0;
        component->EmptyLength = length - thumbLength;
        scrollPosition = Math.Clamp(scrollPosition, 0, maximum);
        component->ScrollPosition = scrollPosition;
        component->PendingScrollPosition = scrollPosition;
        messageScrollBar.ForegroundButtonNode.Height = thumbLength;
        messageScrollBar.ForegroundButtonNode.Y = maximum == 0 ? 0f :
            (float)((double)scrollPosition / maximum * component->EmptyLength);
        messageScrollBar.IsVisible = maximum != 0;
        messageScrollBar.IsEnabled = maximum != 0;
        component->IsAcceptingMouseWheelEvents = true;
    }

    private void SyncScrollBarSkin()
    {
        var thumb = (SimpleNineGridNode)messageScrollBar!.ForegroundButtonNode.ButtonTexture;
        thumb.TexturePath = "ui/uld/ScrollBarB.tex";
        thumb.TextureCoordinates = Vector2.Zero;
        thumb.TextureSize = new(12f, 24f);
        thumb.Offsets = new(5f, 5f, 4f, 5f);
        thumb.AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, 9)
            .AddFrame(1, alpha: 255, addColor: Vector3.Zero, multiplyColor: new(100f))
            .EndFrameSet()
            .BeginFrameSet(10, 19)
            .AddFrame(10, alpha: 0, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(12, alpha: 255)
            .EndFrameSet()
            .BeginFrameSet(20, 29)
            .AddFrame(20, alpha: 255, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(22, addColor: new(60f))
            .EndFrameSet()
            .BeginFrameSet(30, 39)
            .AddFrame(30, alpha: 178, addColor: Vector3.Zero, multiplyColor: new(50f))
            .EndFrameSet()
            .BeginFrameSet(40, 49)
            .AddFrame(40, alpha: 255, addColor: new(60f), multiplyColor: new(100f))
            .AddFrame(49, addColor: Vector3.Zero)
            .EndFrameSet()
            .BeginFrameSet(50, 59)
            .AddFrame(50, alpha: 255, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(59, alpha: 0)
            .EndFrameSet());

        scrollTrack = new()
        {
            TexturePath = "ui/uld/ScrollBarB.tex",
            TextureCoordinates = new(12f, 0f),
            TextureSize = new(12f, 24f),
            Offsets = new(5f, 5f, 5f, 6f),
        };
        scrollTrack.AttachNode(messageScrollBar.BackgroundButtonNode);
        messageScrollBar.BackgroundButtonNode.Component->ButtonBGNode = scrollTrack;
        messageScrollBar.BackgroundButtonNode.AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, 59)
            .AddLabel(1, 1, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(9, 0, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(10, 2, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(19, 0, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(20, 3, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(29, 0, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(40, 6, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(49, 0, AtkTimelineJumpBehavior.PlayOnce, 0)
            .AddLabel(50, 4, AtkTimelineJumpBehavior.Start, 0)
            .AddLabel(59, 0, AtkTimelineJumpBehavior.PlayOnce, 0)
            .EndFrameSet());
        scrollTrack.AddTimeline(new TimelineBuilder()
            .BeginFrameSet(1, 9)
            .AddFrame(1, alpha: 0, addColor: Vector3.Zero, multiplyColor: new(100f))
            .EndFrameSet()
            .BeginFrameSet(10, 19)
            .AddFrame(10, alpha: 0, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(12, alpha: 255)
            .EndFrameSet()
            .BeginFrameSet(20, 29)
            .AddFrame(20, alpha: 255, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(22, addColor: new(20f))
            .EndFrameSet()
            .BeginFrameSet(40, 49)
            .AddFrame(40, alpha: 255, addColor: new(20f), multiplyColor: new(100f))
            .AddFrame(42, addColor: Vector3.Zero)
            .EndFrameSet()
            .BeginFrameSet(50, 59)
            .AddFrame(50, alpha: 255, addColor: Vector3.Zero, multiplyColor: new(100f))
            .AddFrame(52, alpha: 0)
            .EndFrameSet());
    }

    private void OnLinkEvent(ChatLineNode node, AtkEventType type, int param, AtkEvent* atkEvent, AtkEventData* data)
    {
        if (type is AtkEventType.LinkMouseClick or AtkEventType.LinkMouseOver)
        {
            var input = UIInputData.Instance();
            if (input is null || messageClip is null ||
                !messageClip.CheckCollision(new Vector2(input->CursorInputs.PositionX, input->CursorInputs.PositionY)))
                return;
        }
        if (type == AtkEventType.LinkMouseOver)
        {
            if (data is null || data->LinkData is null)
                return;
            var panel = (AddonChatLogPanel*)RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID);
            if (panel is null || !panel->IsReady || panel->IsResizing)
                return;
            ClearHoveredLink();
            hoveredNode = node;
            node.ShowClickableCursor = true;
            hoveredLinkIndex = data->LinkData->LinkIndex;
            hoveredLinkGroupID = data->LinkData->LinkGroupId;
            hoveredEventParam = param;
        }
        ForwardLinkEvent(node, type, param, atkEvent, data);
        if (type == AtkEventType.LinkMouseOut)
        {
            if (hoveredNode == node)
                hoveredNode = null;
            node.ShowClickableCursor = false;
        }
        atkEvent->SetEventIsHandled(true);
    }

    private void ForwardLinkEvent(ChatLineNode node, AtkEventType type, int param, AtkEvent* atkEvent, AtkEventData* data)
    {
        var panel = (AddonChatLogPanel*)RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID);
        if (panel is null || !panel->IsReady || node.Node is null)
            return;
        var chatText = panel->ChatText;
        var viewerText = panel->LogViewer.ChatText;
        try
        {
            panel->ChatText = node.Node;
            panel->LogViewer.ChatText = node.Node;
            panel->ReceiveEvent(type, param, atkEvent, data);
        }
        finally
        {
            panel->ChatText = chatText;
            panel->LogViewer.ChatText = viewerText;
        }
    }

    private void ClearHoveredLink(ChatLineNode? changedNode = null)
    {
        if (hoveredNode is not { } node || (changedNode is not null && node != changedNode))
            return;
        hoveredNode = null;
        if (node.Node is null)
            return;
        if (node.Node->LinkData is not null)
        {
            foreach (var pointer in *node.Node->LinkData)
            {
                var link = pointer.Value;
                if (link is null || link->LinkIndex != hoveredLinkIndex || link->LinkGroupId != hoveredLinkGroupID)
                    continue;
                var panel = RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID);
                var atkEvent = new AtkEvent
                {
                    Node = node,
                    Target = node,
                    Listener = (AtkEventListener*)panel,
                    Param = (uint)hoveredEventParam,
                    State = new() { EventType = AtkEventType.LinkMouseOut },
                };
                var data = new AtkEventData { LinkData = link };
                ForwardLinkEvent(node, AtkEventType.LinkMouseOut, hoveredEventParam, &atkEvent, &data);
                break;
            }
        }
        node.HideTooltip();
        node.ShowClickableCursor = false;
    }

    private void RefreshHoveredLink()
    {
        var input = UIInputData.Instance();
        var collisions = AtkStage.Instance()->AtkCollisionManager;
        var panel = (AddonChatLogPanel*)RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID);
        if (input is null || collisions is null || panel is null || !panel->IsReady || panel->IsResizing)
        {
            ClearHoveredLink();
            return;
        }
        var position = new Vector2(input->CursorInputs.PositionX, input->CursorInputs.PositionY);
        if (!messageClip!.CheckCollision(position) || collisions->IntersectingAddon != (AtkUnitBase*)panel)
        {
            ClearHoveredLink();
            return;
        }
        if (hoveredNode is not null)
            return;

        foreach (var row in messageNodes)
        {
            if (!row.IsVisible || (AtkResNode*)collisions->IntersectingCollisionNode != (AtkResNode*)row ||
                !row.CheckCollision(position) || row.Node->LinkData is null)
                continue;
            // LinkData 的边界已采用渲染像素；重排后重新命中，不缓存原生链接指针。
            var local = position - new Vector2((short)(int)row.ScreenX, (short)(int)row.ScreenY);
            foreach (var pointer in *row.Node->LinkData)
            {
                var link = pointer.Value;
                if (link is null || local.X < link->MinX || local.X >= link->MaxX ||
                    local.Y < link->MinY || local.Y >= link->MaxY)
                    continue;
                var atkEvent = new AtkEvent
                {
                    Node = row,
                    Target = row,
                    Listener = (AtkEventListener*)panel,
                    State = new() { EventType = AtkEventType.LinkMouseOver },
                };
                var data = new AtkEventData { LinkData = link };
                OnLinkEvent(row, AtkEventType.LinkMouseOver, 0, &atkEvent, &data);
                return;
            }
            return;
        }
    }

    private void RenderViewport()
    {
        var padding = Math.Max(0, (int)messageClip!.Height - contentHeight);
        var first = FindLineAt(Math.Max(0, scrollPosition - (int)messageClip.Height));
        var end = scrollPosition + (int)messageClip.Height * 2;
        var used = 0;
        var nodesChanged = false;
        for (var index = first; index < visibleMessages.Count; index++)
        {
            var layout = visibleMessages[index];
            if (layout.Top > end)
                break;
            if (layout.MeasurementRevision != measurementRevision)
                continue;
            var position = new Vector2(0f, layout.Top - scrollPosition + padding);
            // 邻近历史只参与测量；完全离开正文视口的行不参与绘制和链接命中。
            if (position.Y >= messageClip.Height || position.Y + layout.Height <= 0f)
                continue;
            if (used == messageNodes.Count)
            {
                var node = new ChatLineNode(this, true) { IsVisible = false };
                node.AttachNode(messageClip);
                messageNodes.Add(node);
                nodesChanged = true;
            }
            var row = messageNodes[used++];
            if (row.Index != layout.Line.Index || row.MeasurementRevision != measurementRevision)
            {
                ClearHoveredLink(row);
                ApplyTextAppearance(row, messageAppearance);
                row.Resize(messageClip.Width, GetDisplayText(layout));
                row.Index = layout.Line.Index;
                row.MeasurementRevision = measurementRevision;
            }
            if (row.Position != position)
                ClearHoveredLink(row);
            row.Position = position;
            row.IsVisible = true;
        }
        for (var index = used; index < messageNodes.Count; index++)
        {
            if (messageNodes[index].IsVisible)
                ClearHoveredLink(messageNodes[index]);
            messageNodes[index].IsVisible = false;
        }
        if (nodesChanged)
            NativeNodeDetach.UpdateNodeLists(RaptureAtkUnitManager.Instance()->GetAddonById(AttachedPanelID));
    }

    private void RebuildTabs(AtkUnitBase* addon)
    {
        if (previousTabs is null || nextTabs is null || addTabButton is null || selectedIndicator is null)
            return;

        // 页签在窗口终结前复用，鼠标输入缓存的事件目标始终有效。
        var nodesChanged = false;
        while (tabButtons.Count < tabConfigs.Count)
        {
            var tabIndex = tabButtons.Count;
            var button = new TextNode
            {
                AlignmentType = AlignmentType.Center,
                TextFlags = TextFlags.Ellipsis,
                ShowClickableCursor = true,
                IsVisible = false,
            };
            button.AddEvent(AtkEventType.MouseClick, (listener, type, param, atkEvent, data) =>
            {
                atkEvent->SetEventIsHandled(true);
                if (data->MouseData.ButtonId == 0)
                    SelectTab(tabIndex);
                else if (data->MouseData.ButtonId == 1)
                {
                    SelectTab(tabIndex);
                    OnOpenSettings?.Invoke();
                }
            });
            button.AttachNode(root);
            tabButtons.Add(button);
            nodesChanged = true;
        }
        if (tabsDirty)
        {
            for (var index = 0; index < tabConfigs.Count; index++)
            {
                var button = tabButtons[index];
                ApplyTextAppearance(button, tabAppearance);
                button.String = tabConfigs[index].Name;
                button.SetTextTooltip(tabConfigs[index].Name);
            }
            ApplyTextAppearance(selectedIndicator, tabAppearance);
            selectedIndicator.TextColor = KnownColor.PaleGoldenrod.ToVector4();
        }

        var controlSize = MathF.Max(1f, MathF.Min(addTabSize.X, addTabSize.Y));
        var hasPrevious = firstVisibleTab > 0;
        var controlsWidth = hasPrevious ? controlSize + GAP : 0f;
        var availableWidth = MathF.Max(1f, addTabPosition.X - tabPosition.X - controlsWidth - GAP);
        var requiredWidth = 0f;
        var indicatorWidth = MathF.Max(8f, tabAppearance.FontSize * 0.6f);
        for (var index = firstVisibleTab; index < tabConfigs.Count; index++)
        {
            var button = tabButtons[index];
            requiredWidth += MathF.Max(24f, button.GetTextDrawSize(tabConfigs[index].Name, false).X + indicatorWidth + GAP) + GAP;
            if (requiredWidth - GAP <= availableWidth)
                continue;
            availableWidth = MathF.Max(1f, availableWidth - controlSize - GAP);
            break;
        }
        var x = tabPosition.X;
        var visibleTabCount = 0;
        selectedIndicator.IsVisible = false;
        for (var index = firstVisibleTab; index < tabConfigs.Count; index++)
        {
            var button = tabButtons[index];
            var width = MathF.Min(availableWidth, MathF.Max(24f, button.GetTextDrawSize(tabConfigs[index].Name, false).X + indicatorWidth + GAP));
            if (visibleTabCount > 0 && x + width > tabPosition.X + availableWidth)
                break;

            button.Position = new(x + indicatorWidth, tabPosition.Y);
            var buttonSize = new Vector2(MathF.Max(1f, width - indicatorWidth), tabHeight);
            if (button.Size != buttonSize)
            {
                button.Size = buttonSize;
                button.String = tabConfigs[index].Name;
            }
            button.IsVisible = true;
            if (index == selectedTab)
            {
                selectedIndicator.Position = new(x, tabPosition.Y);
                selectedIndicator.Size = new(indicatorWidth, tabHeight);
                selectedIndicator.IsVisible = true;
            }
            visibleTabCount++;
            x += width + GAP;
        }

        nextVisibleTab = firstVisibleTab + visibleTabCount;
        for (var index = 0; index < tabButtons.Count; index++)
            if (index < firstVisibleTab || index >= nextVisibleTab)
                tabButtons[index].IsVisible = false;
        var hasNext = nextVisibleTab < tabConfigs.Count;
        var controlX = addTabPosition.X - (controlSize + GAP) * ((hasPrevious ? 1 : 0) + (hasNext ? 1 : 0));
        var controlY = tabPosition.Y + (tabHeight - controlSize) / 2f;
        previousTabs.Size = new(controlSize);
        nextTabs.Size = new(controlSize);
        previousTabs.IsVisible = hasPrevious;
        nextTabs.IsVisible = hasNext;
        if (hasPrevious)
        {
            previousTabs.Position = new(controlX, controlY);
            controlX += controlSize + GAP;
        }
        if (hasNext)
            nextTabs.Position = new(controlX, controlY);
        addTabButton.Position = addTabPosition;
        addTabButton.Size = addTabSize;
        if (nodesChanged)
            NativeNodeDetach.UpdateNodeLists(addon);
        tabsDirty = false;
    }

    private static void ApplyTextAppearance(TextNode node, TextAppearance appearance)
    {
        node.FontSize = appearance.FontSize;
        node.LineSpacing = appearance.LineSpacing;
        node.FontType = appearance.FontType;
        node.TextColor = appearance.Color with { W = 1f };
        node.TextOutlineColor = appearance.OutlineColor;
    }

    private ReadOnlySeString GetDisplayText(ChatLineLayout layout)
    {
        if (layout.FormattedRevision == formattedRevision)
            return layout.Text;
        var line = layout.Line;
        using var builder = new RentedSeStringBuilder();
        if (showTime && line.Timestamp != 0)
        {
            // Addon 原始行保留客户端的时间宏；服务端时钟抵消当前 Windows 时区偏移。
            var template = DService.Instance().Data.Excel.GetSheet<RawRow>(name: "Addon")!
                .GetRow(use12HourClock ? 7841u : 7840u).ReadStringColumn(0);
            var timestamp = useServerTime ? line.Timestamp - serverTimeOffset : line.Timestamp;
            SeStringParameter[] parameters = [unchecked((uint)timestamp)];
            builder.Builder.Append(DService.Instance().SeStringEvaluator.Evaluate(template, parameters));
        }
        var text = FormatMessage((XivChatType)line.Type, new(line.Sender), new(line.Message));
        layout.Text = builder.Builder.PushColorRgba(BetterChatFilters.GetColor(line.Type, messageAppearance.Color))
            .Append(text).PopColor().ToReadOnlySeString();
        layout.FormattedRevision = formattedRevision;
        return layout.Text;
    }

    private static ReadOnlySeString FormatMessage(XivChatType type, ReadOnlySeString senderText, ReadOnlySeString messageText)
    {
        var services = DService.Instance();
        if (services.Data.GetExcelSheet<LogKind>().TryGetRow((uint)type, out var row) && !row.Format.IsEmpty)
        {
            try
            {
                SeStringParameter[] parameters =
                [
                    services.SeStringEvaluator.Evaluate(senderText),
                    services.SeStringEvaluator.Evaluate(messageText)
                ];
                var formatted = services.SeStringEvaluator.Evaluate(row.Format, parameters);
                if (!formatted.IsEmpty)
                    return new(formatted.Data.ToArray());
            }
            catch
            {
                // 日志格式宏的参数不兼容时保留原始发送者与正文。
            }
        }

        using var builder = new RentedSeStringBuilder();
        if (!senderText.IsEmpty)
            builder.Builder.Append(senderText).Append(": ");
        return builder.Builder.Append(messageText).ToReadOnlySeString();
    }

    private readonly record struct TextAppearance(
        uint FontSize, uint LineSpacing, FontType FontType, Vector4 Color, Vector4 OutlineColor);

    private sealed record ChatLine(int Index, ushort Type, byte Source, byte Target, int Timestamp, byte[] Sender, byte[] Message);

    private sealed class ChatLineLayout(ChatLine line, int height)
    {
        public ChatLine Line { get; } = line;
        public int Top { get; set; }
        public int Height { get; set; } = height;
        public int MeasurementRevision { get; set; } = -1;
        public int FormattedRevision { get; set; } = -1;
        public int FilterRevision { get; set; } = -1;
        public bool MatchesFilter { get; set; }
        public ReadOnlySeString Text { get; set; }
    }

    private sealed class ChatLineNode : TextNode
    {
        private Utf8String* originalText;

        public int Index { get; set; } = -1;
        public int MeasurementRevision { get; set; } = -1;

        public ChatLineNode(BetterChatWindow owner, bool interactive)
        {
            originalText = Utf8String.CreateEmpty();
            FontSize = 14;
            LineSpacing = 17;
            AlignmentType = AlignmentType.TopLeft;
            TextFlags = TextFlags.MultiLine | TextFlags.WordWrap | TextFlags.Edge | TextFlags.LinkData | TextFlags.OverflowHidden;
            if (!interactive)
                return;
            AddNodeFlags(NodeFlags.EmitsEvents, NodeFlags.RespondToMouse, NodeFlags.HasCollision);
            AddEvent(AtkEventType.MouseWheel, owner.OnMouseWheel);
            AddEvent(AtkEventType.LinkMouseClick, ReceiveLinkEvent);
            AddEvent(AtkEventType.LinkMouseOver, ReceiveLinkEvent);
            AddEvent(AtkEventType.LinkMouseOut, ReceiveLinkEvent);

            return;

            void ReceiveLinkEvent(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* atkEvent, AtkEventData* data)
                => owner.OnLinkEvent(this, type, param, atkEvent, data);
        }

        protected override void Dispose(bool disposing, bool isNativeDestructor)
        {
            if (disposing && Node is not null)
                Node->OriginalTextPointer = null;
            base.Dispose(disposing, isNativeDestructor);
        }

        public void ReleaseOriginalText()
        {
            if (originalText is null)
                return;
            originalText->Dtor(true);
            originalText = null;
        }

        public void Resize(float width, ReadOnlySeString text)
        {
            Width = MathF.Max(1f, MathF.Floor(width));
            using var builder = new RentedSeStringBuilder();
            fixed (byte* pointer = builder.Builder.Append(text).GetViewAsSpan())
                originalText->SetString(pointer);
            // ApplyTextFlow 会清空 NodeText，原文必须保存在独立且稳定的缓冲中。
            Node->SetText(originalText->StringPtr);
            Node->ApplyTextFlow();
            Height = MathF.Ceiling(MathF.Max(LineSpacing, GetTextDrawSize(false).Y));
        }
    }
}
