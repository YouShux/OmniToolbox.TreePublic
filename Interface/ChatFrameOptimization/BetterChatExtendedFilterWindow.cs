using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;
using OmniToolbox.UI;
using LogFilter = Lumina.Excel.Sheets.LogFilter;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class BetterChatExtendedFilterWindow : NativeAddon
{
    private readonly ChatFrameOptimizationConfig config;
    private readonly System.Action saveConfig;
    private readonly List<(SimpleComponentNode Row, CheckboxNode Checkbox)> filterNodes = [];
    private readonly List<TabBarRadioButtonNode> categoryTabs = [];
    private readonly HashSet<uint> draftRows = [];
    private static readonly (byte Category, string LabelKey)[] FilterTabs =
    [
        (1, "Feature.ChatFrameOptimization.BetterChat.Tab.Chat"),
        (4, "Feature.ChatFrameOptimization.BetterChat.Tab.Battle"),
        (2, "Feature.ChatFrameOptimization.BetterChat.Tab.Notice")
    ];
    private static readonly (byte Category, string LabelKey)[] BattleSubjects =
    [
        (4, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.4"),
        (5, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.5"),
        (6, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.6"),
        (7, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.7"),
        (8, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.8"),
        (9, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.9"),
        (10, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.10"),
        (11, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.11"),
        (12, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.12"),
        (13, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.13"),
        (14, "Feature.ChatFrameOptimization.BetterChat.FilterCategory.14")
    ];
    private BetterChatTabConfig? selectedTab;
    private int selectedTabIndex;
    private TextNode? tabNumber;
    private TextNode? tabName;
    private TextNode? subjectHeader;
    private TextDropDownNode? subjectSelector;
    private ScrollingAreaNode<VerticalListNode>? filterList;
    private SimpleNineGridNode? highlightedFilter;
    private TextNode? previewText;
    private LogFilter? previewRow;
    private TextButtonNode? applyButton;
    private CustomEventInterface? recommendedMenuEvent;
    private byte selectedCategory = 4;
    private byte selectedSubject = 4;
    private bool updating;

    [SetsRequiredMembers]
    public BetterChatExtendedFilterWindow(ChatFrameOptimizationConfig config, System.Action saveConfig)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        InternalName = "OmniBetterChatFilters";
        Title = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.FilterTitle");
        Subtitle = string.Empty;
        Size = new(610f, 585f);
        ContentPadding = Vector2.Zero;
        EnableContextMenu = false;
    }

    public void OpenFor(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= config.BetterChatTabs.Count ||
            config.BetterChatTabs[tabIndex].NativeTabIndex >= 0)
        {
            return;
        }

        CloseRecommendedMenu((AtkUnitBase*)this);
        selectedTabIndex = tabIndex;
        selectedTab = config.BetterChatTabs[tabIndex];
        draftRows.Clear();
        draftRows.UnionWith(selectedTab.FilterRows ?? BetterChatFilters.FromChatTypes(selectedTab.ChatTypes));
        if (IsOpen)
        {
            subjectSelector?.Collapse(false);
            WindowNode?.SetTitle(Title.ToString(), string.Empty);
            ApplyFilterState();
            return;
        }

        Open();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        ClearNodeReferences();
        SynchronizeScale(addon);

        // 采用 ConfigLogFilter.uld 的窗口局部坐标，缩放由 Addon 统一处理。
        tabNumber = new TextNode
        {
            Position = new(15f, 33f),
            Size = new(28f, 34f),
            AlignmentType = AlignmentType.Left,
            FontType = FontType.Axis,
            FontSize = 18,
            TextColor = ColorHelper.GetColor(2),
            TextOutlineColor = ColorHelper.GetColor(7),
        };
        tabNumber.AttachNode(this);

        tabName = new TextNode
        {
            Position = new(46f, 40f),
            Size = new(550f, 24f),
            AlignmentType = AlignmentType.Left,
            FontType = FontType.Axis,
            FontSize = 14,
            TextColor = ColorHelper.GetColor(2),
            TextOutlineColor = ColorHelper.GetColor(7),
        };
        tabName.AttachNode(this);

        for (var index = 0; index < FilterTabs.Length; index++)
        {
            var (category, labelKey) = FilterTabs[index];
            var tab = new TabBarRadioButtonNode
            {
                Position = new(index switch { 0 => 12f, 1 => 204f, _ => 398f }, 66f),
                Size = new(index == 0 ? 198f : 200f, 24f),
                String = OmniLoc.Get(labelKey),
                OnClick = () => SelectCategory(category),
            };
            tab.Component->SoundEffectId = 1;
            tab.AttachNode(this);
            categoryTabs.Add(tab);
        }

        subjectHeader = new TextNode
        {
            Position = new(23f, 92f),
            Size = new(215f, 16f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.FilterSubject"),
            AlignmentType = AlignmentType.Left,
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = ColorHelper.GetColor(3),
            TextOutlineColor = ColorHelper.GetColor(7),
        };
        subjectHeader.AttachNode(this);

        subjectSelector = new TextDropDownNode
        {
            Position = new(19f, 112f),
            Size = new(380f, 24f),
            Options = BattleSubjects.Select(subject => OmniLoc.Get(subject.LabelKey)).ToList(),
            OnOptionSelected = option =>
            {
                selectedSubject = BattleSubjects.First(subject => OmniLoc.Get(subject.LabelKey) == option).Category;
                if (filterList is not null)
                    filterList.ScrollPosition = 0;
                ApplyFilterState();
            },
        };
        subjectSelector.SelectedOption = OmniLoc.Get(BattleSubjects.First(subject => subject.Category == selectedSubject).LabelKey);
        subjectSelector.AttachNode(this);

        filterList = new()
        {
            Position = new(14f, 136f),
            Size = new(584f, 314f),
            ContentHeight = 0f,
            AutoHideScrollBar = true,
            ScrollSpeed = 22,
        };
        filterList.AttachNode(this);

        foreach (var row in BetterChatFilters.Rows)
        {
            var filterRow = new SimpleComponentNode { Size = new(568f, 22f) };
            var highlight = new SimpleNineGridNode
            {
                TexturePath = "ui/uld/ListItemA.tex",
                TextureCoordinates = Vector2.Zero,
                TextureSize = new(64f, 22f),
                Size = filterRow.Size,
                LeftOffset = 16f,
                RightOffset = 1f,
                IsVisible = false,
            };
            highlight.AttachNode(filterRow);

            var checkbox = new CheckboxNode
            {
                Position = new(12f, 0f),
                Height = 22f,
                OnClick = enabled =>
                {
                    if (updating)
                        return;

                    ShowPreview(row, highlight);
                    SetFilter(row.RowId, enabled);
                },
            };
            checkbox.DisableAutoResize = true;
            checkbox.Component->SoundEffectId = 1;
            checkbox.String = row.Name;
            checkbox.Width = filterRow.Width - 12f;
            checkbox.BoxBackground.Size = new(16f, 16f);
            checkbox.BoxForeground.Size = new(16f, 16f);
            checkbox.Label.FontSize = 12;
            checkbox.Label.Position = new(28f, 3f);
            checkbox.Label.Size = new(filterRow.Width - 56f, 18f);
            checkbox.AttachNode(filterRow);
            checkbox.AddEvent(AtkEventType.MouseOver, () => ShowPreview(row, highlight));
            checkbox.AddEvent(AtkEventType.FocusStart, () => ShowPreview(row, highlight));
            checkbox.AddEvent(AtkEventType.MouseOut, () => highlight.IsVisible = false);
            checkbox.AddEvent(AtkEventType.FocusStop, () => highlight.IsVisible = false);
            filterList.ContentNode.AddNode(filterRow);
            filterNodes.Add((filterRow, checkbox));
        }

        var previewNode = new SimpleNineGridNode
        {
            Position = new(15f, 453f),
            Size = new(582f, 52f),
            TexturePath = "ui/uld/ConfigLogFilter.tex",
            TextureCoordinates = new(72f, 0f),
            TextureSize = new(24f, 24f),
            TopOffset = 8f,
            BottomOffset = 8f,
            LeftOffset = 8f,
            RightOffset = 8f,
        };
        previewNode.AttachNode(this);

        previewText = new TextNode
        {
            Position = new(8f, 8f),
            Size = new(566f, 38f),
            AlignmentType = AlignmentType.TopLeft,
            FontType = FontType.Axis,
            FontSize = 14,
            LineSpacing = 18,
            TextFlags = TextFlags.WordWrap | TextFlags.MultiLine | TextFlags.Edge,
            TextColor = ColorHelper.GetColor(1),
            TextOutlineColor = ColorHelper.GetColor(51),
        };
        previewText.AttachNode(previewNode);

        var recommendedButton = new TextButtonNode
        {
            Position = new(14f, 510f),
            Size = new(125f, 28f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Recommended"),
            OnClick = OpenRecommendedMenu,
        };
        recommendedButton.Component->SoundEffectId = 1;
        recommendedButton.AttachNode(this);

        var selectAllButton = new TextButtonNode
        {
            Position = new(344f, 510f),
            Size = new(125f, 28f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.SelectAll"),
            OnClick = () => SelectCategoryRows(true),
        };
        selectAllButton.Component->SoundEffectId = 1;
        selectAllButton.AttachNode(this);

        var selectNoneButton = new TextButtonNode
        {
            Position = new(472f, 510f),
            Size = new(125f, 28f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.SelectNone"),
            OnClick = () => SelectCategoryRows(false),
        };
        selectNoneButton.Component->SoundEffectId = 1;
        selectNoneButton.AttachNode(this);

        applyButton = new TextButtonNode
        {
            Position = new(344f, 538f),
            Size = new(125f, 28f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Apply"),
            OnClick = ApplyFilters,
        };
        applyButton.Component->SoundEffectId = 1;
        applyButton.AttachNode(this);

        var closeButton = new TextButtonNode
        {
            Position = new(472f, 538f),
            Size = new(125f, 28f),
            String = OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.FilterClose"),
            OnClick = Close,
        };
        closeButton.Component->SoundEffectId = 1;
        closeButton.AttachNode(this);

        SelectCategory(selectedCategory);
    }

    protected override void OnShow(AtkUnitBase* addon) => SynchronizeScale(addon);

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        SynchronizeScale(addon);
        SynchronizeRecommendedMenu(addon);
    }

    protected override void OnHide(AtkUnitBase* addon)
    {
        subjectSelector?.Collapse(false);
        CloseRecommendedMenu(addon);
        selectedTab = null;
        draftRows.Clear();
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        CloseRecommendedMenu(addon);
        recommendedMenuEvent?.Dispose();
        recommendedMenuEvent = null;
        selectedTab = null;
        draftRows.Clear();
        ClearNodeReferences();
    }

    private static void SynchronizeScale(AtkUnitBase* addon)
    {
        var source = AddonHelper.GetByName("ConfigLogFilter");
        if (source is null || !source->IsReady || !source->IsVisible)
            source = AddonHelper.GetByName("ConfigCharacter");
        if (source is null || !source->IsReady)
            return;

        var scale = source->GetScale();
        if (MathF.Abs(addon->GetScale() - scale) > 0.001f)
            addon->SetScale(scale / AtkUnitBase.GetGlobalUIScale(), true);
    }

    private void ApplyFilterState()
    {
        if (selectedTab is null || filterList is null || filterNodes.Count != BetterChatFilters.Rows.Length)
            return;

        if (tabNumber is not null)
            tabNumber.String = selectedTabIndex < 31
                                   ? ((char)((int)SeIconChar.BoxedNumber1 + selectedTabIndex)).ToString()
                                   : (selectedTabIndex + 1).ToString();
        if (tabName is not null)
            tabName.String = selectedTab.Name;

        var isBattle = selectedCategory == 4;
        if (subjectHeader is not null)
            subjectHeader.IsVisible = isBattle;
        if (subjectSelector is not null)
            subjectSelector.IsVisible = isBattle;
        filterList.Position = new(14f, isBattle ? 136f : 92f);
        filterList.Size = new(584f, isBattle ? 314f : 358f);

        if (highlightedFilter is not null)
            highlightedFilter.IsVisible = false;
        highlightedFilter = null;

        updating = true;
        try
        {
            for (var index = 0; index < filterNodes.Count; index++)
            {
                var (filterRow, checkbox) = filterNodes[index];
                var row = BetterChatFilters.Rows[index];
                checkbox.IsChecked = draftRows.Contains(row.RowId);
                filterRow.IsVisible = IsSelectedCategory(row.Category) && (!isBattle || row.Category == selectedSubject);
            }
        }
        finally
        {
            updating = false;
        }

        filterList.ContentNode.RecalculateLayout();
        filterList.FitToContentHeight();
        if (previewText is not null)
        {
            previewRow = BetterChatFilters.Rows
                .Where(row => IsSelectedCategory(row.Category) && (!isBattle || row.Category == selectedSubject) && !row.Example.IsEmpty)
                .Select(static row => (LogFilter?)row)
                .FirstOrDefault();
            previewText.String = previewRow is { } row ? row.Example : string.Empty;
            RefreshColors();
        }
        UpdateApplyButton();
    }

    private void SelectCategory(byte category)
    {
        subjectSelector?.Collapse(false);
        selectedCategory = category;
        for (var index = 0; index < categoryTabs.Count; index++)
        {
            var selected = FilterTabs[index].Category == category;
            categoryTabs[index].IsChecked = selected;
            categoryTabs[index].IsSelected = selected;
        }
        if (filterList is not null)
            filterList.ScrollPosition = 0;
        ApplyFilterState();
    }

    private void ShowPreview(LogFilter row, SimpleNineGridNode highlight)
    {
        if (highlightedFilter is not null)
            highlightedFilter.IsVisible = false;
        highlightedFilter = highlight;
        highlight.IsVisible = true;
        previewRow = row;
        if (previewText is not null)
            previewText.String = row.Example;
        RefreshColors();
    }

    public void RefreshColors()
    {
        if (previewText is not null && previewRow is { } row)
            previewText.TextColor = BetterChatFilters.GetColor((ushort)row.LogKind, ColorHelper.GetColor(1));
    }

    private void SetFilter(uint rowID, bool enabled)
    {
        if (selectedTab is null)
            return;

        if (enabled)
        {
            draftRows.Add(rowID);
        }
        else
        {
            draftRows.Remove(rowID);
        }
        UpdateApplyButton();
    }

    private void SelectCategoryRows(bool enabled)
    {
        if (selectedTab is null)
            return;

        foreach (var row in BetterChatFilters.Rows)
        {
            if (!IsSelectedCategory(row.Category))
                continue;
            if (enabled)
                draftRows.Add(row.RowId);
            else
                draftRows.Remove(row.RowId);
        }
        ApplyFilterState();
    }

    private void ApplyFilters()
    {
        if (selectedTab is null || applyButton is not { IsEnabled: true })
            return;

        selectedTab.FilterRows = [.. draftRows.Order()];
        selectedTab.ChatTypes = BetterChatFilters.GetChatTypes(selectedTab.FilterRows);
        saveConfig();
        UpdateApplyButton();
    }

    private void UpdateApplyButton()
    {
        if (applyButton is not null && selectedTab is not null)
            applyButton.IsEnabled = !draftRows.SetEquals(selectedTab.FilterRows ?? BetterChatFilters.FromChatTypes(selectedTab.ChatTypes));
    }

    private void OpenRecommendedMenu()
    {
        var addon = (AtkUnitBase*)this;
        if (selectedTab is null || addon is null || !addon->IsReady)
            return;

        var module = RaptureAtkModule.Instance();
        var menu = AddonHelper.GetByName("SelectString");
        if (module is null || (menu is not null && menu->IsVisible))
            return;

        SynchronizeScale(addon);
        recommendedMenuEvent ??= new(OnRecommendedMenu);
        using var values = new AtkValueArray(
            default(AtkValue), default(AtkValue),
            OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.RecommendedPrompt"),
            default(AtkValue), default(AtkValue), default(AtkValue), default(AtkValue),
            OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Preset.General"),
            OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Preset.Battle"),
            OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.Preset.Story"));
        module->OpenAddon(34, (uint)values.Length, values, recommendedMenuEvent, 4, addon->Id, 4);
        SynchronizeRecommendedMenu(addon);
    }

    private static void SynchronizeRecommendedMenu(AtkUnitBase* addon)
    {
        var menu = AddonHelper.GetByName("SelectString");
        var stage = AtkStage.Instance();
        if (menu is null || !menu->IsReady || menu->BlockedParentId != addon->Id || stage is null)
            return;

        var scale = addon->GetScale();
        if (MathF.Abs(menu->GetScale() - scale) > 0.001f)
            menu->SetScale(scale / AtkUnitBase.GetGlobalUIScale(), true);
        var center = new Vector2(addon->X + (addon->GetScaledWidth(true) / 2f), addon->Y + (addon->GetScaledHeight(true) / 2f));
        var screenSize = (Vector2)stage->ScreenSize;
        if (stage->IsScreenSizeScaled)
            screenSize *= stage->ScreenSizeScale;
        var menuSize = new Vector2(menu->GetScaledWidth(true), menu->GetScaledHeight(true));
        var position = Vector2.Clamp(center - (menuSize / 2f), Vector2.Zero, Vector2.Max(Vector2.Zero, screenSize - menuSize));
        if (menu->X != (short)position.X || menu->Y != (short)position.Y)
            menu->SetPosition((short)position.X, (short)position.Y);
    }

    private AtkValue* OnRecommendedMenu(AtkModuleInterface.AtkEventInterface* thisPtr, AtkValue* returnValue,
        AtkValue* values, uint valueCount, ulong eventKind)
    {
        returnValue->SetBool(true);
        if (selectedTab is not null && eventKind == 4 && valueCount > 0 && values is not null &&
            values->Type == AtkValueType.Int && values->Int is >= 0 and < 3)
        {
            var preset = 1 << values->Int;
            draftRows.Clear();
            foreach (var row in BetterChatFilters.Rows)
            {
                if ((row.Preset & preset) != 0)
                    draftRows.Add(row.RowId);
            }
            ApplyFilterState();
        }
        return returnValue;
    }

    private static void CloseRecommendedMenu(AtkUnitBase* addon)
    {
        if (addon is null)
            return;
        var menu = AddonHelper.GetByName("SelectString");
        if (menu is null || menu->BlockedParentId != addon->Id)
            return;
        menu->Close(false);
    }

    private bool IsSelectedCategory(byte category) => selectedCategory switch
    {
        1 => category == 1,
        2 => category == 2,
        4 => category >= 4,
        _ => false
    };

    private void ClearNodeReferences()
    {
        filterNodes.Clear();
        categoryTabs.Clear();
        tabNumber = null;
        tabName = null;
        subjectHeader = null;
        subjectSelector = null;
        filterList = null;
        highlightedFilter = null;
        previewText = null;
        previewRow = null;
        applyButton = null;
    }
}
