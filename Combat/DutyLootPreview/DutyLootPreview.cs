using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using KamiToolKit.Overlay.UiOverlay;
using ContentFinderConditionSheet = Lumina.Excel.Sheets.ContentFinderCondition;
using InstanceContentSheet = Lumina.Excel.Sheets.InstanceContent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Items;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class DutyLootPreview : ModuleBase
{
    private const uint DUTY_LOOT_ICON_ID = 65114;

    private readonly Config config;
    private readonly System.Action saveConfig;
    private readonly ItemPreviewService itemPreviewService;
    private readonly Func<bool> isQuestInformationEnabled;

    public DutyLootPreview(
        Config config,
        System.Action saveConfig,
        ItemPreviewService itemPreviewService,
        Func<bool> isQuestInformationEnabled)
    {
        this.config = config;
        this.saveConfig = saveConfig;
        this.itemPreviewService = itemPreviewService;
        this.isQuestInformationEnabled = isQuestInformationEnabled;
    }

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("DutyLootPreviewTitle"),
        Description = OmniLoc.Get("DutyLootPreviewDescription"),
        Category = ModuleCategory.Item,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Combat/DutyLootPreview-1.png"
    };

    private FeatureLifetime? runtimeLifetime;
    private LootWindow? window;
    private OverlayController? overlayController;
    private DutyLootOverlayNode? overlayNode;
    private uint activeDutyID;
    private bool exchangeDataPending;
    private long nextExchangeRetryAt;

    protected override void OnEnable()
    {
        var lifetime = new FeatureLifetime();
        try
        {
            LoadDrops();
            window = new(this);
            lifetime.Add(() =>
            {
                window?.Dispose();
                window = null;
            });

            overlayController = new();
            overlayNode = new DutyLootOverlayNode(this);
            overlayController.AddNode(overlayNode);
            lifetime.Add(() =>
            {
                overlayNode?.HideButtonsNow();
                overlayNode = null;
                overlayController?.Dispose();
                overlayController = null;
            });

            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate))
            {
                throw new InvalidOperationException("Duty loot preview update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            runtimeLifetime = lifetime;
            RefreshDuty();
        }
        catch
        {
            lifetime.Dispose();
            runtimeLifetime = null;
            throw;
        }
    }

    protected override void OnDisable()
    {
        var lifetime = runtimeLifetime;
        runtimeLifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            activeDutyID = 0;
            exchangeDataPending = false;
            nextExchangeRetryAt = 0;
            dropsByDuty.Clear();
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (DService.Instance().Condition.IsBetweenAreas || !UIModule.IsScreenReady())
        {
            activeDutyID = 0;
            window?.Close();
            return;
        }

        RefreshDuty();
    }

    private void RefreshDuty()
    {
        if (DService.Instance().Condition.IsBetweenAreas || !UIModule.IsScreenReady())
        {
            activeDutyID = 0;
            window?.Close();
            return;
        }

        var dutyID = GetActiveDutyID();
        if (activeDutyID == dutyID &&
            (!exchangeDataPending || Environment.TickCount64 < nextExchangeRetryAt))
        {
            return;
        }

        activeDutyID = dutyID;
        if (dutyID == 0)
        {
            window?.Close();
        }

        window?.SetItems(BuildItems(dutyID));
    }

    private static uint GetActiveDutyID()
    {
        if (!DService.Instance().ClientState.IsLoggedIn)
        {
            return 0;
        }

        var raidAgent = AgentRaidFinder.Instance();
        if (raidAgent != null && raidAgent->IsAddonShown())
        {
            var selectedDutyID = raidAgent->InterfaceSub.SelectedDutyId;
            if (selectedDutyID > 0)
            {
                return ToContentFinderConditionID((uint)selectedDutyID);
            }
        }

        var agent = AgentContentsFinder.Instance();
        if (agent != null && agent->IsAddonShown() && agent->SelectedDuty.ContentType == ContentsType.Regular &&
            agent->SelectedDuty.Id != 0)
        {
            return ToContentFinderConditionID(agent->SelectedDuty.Id);
        }

        var game = GameMain.Instance();
        return game != null ? ToContentFinderConditionID((uint)game->CurrentContentFinderConditionId) : 0;
    }

    private static uint ToContentFinderConditionID(uint dutyID)
    {
        if (dutyID == 0 || LuminaGetter.TryGetRow<ContentFinderConditionSheet>(dutyID, out _))
        {
            return dutyID;
        }

        return LuminaGetter.TryGetRow<InstanceContentSheet>(dutyID, out var instanceContent)
            ? instanceContent.ContentFinderCondition.RowId
            : dutyID;
    }

    private void ToggleWindow()
    {
        RefreshDuty();
        if (activeDutyID != 0)
        {
            window?.Toggle();
        }
    }

    // 按钮挂在自有悬浮层 Addon 上，不进入 _ToDoList / ContentsFinder / RaidFinder 的节点树；
    // 这些窗口刷新重建时会按自有结构遍历子节点，外来节点会让原生 RequestedUpdate 崩溃。
    private sealed class DutyLootOverlayNode : OverlayNode
    {
        private const float TODO_BUTTON_GAP = 6f;
        private const float TODO_BUTTON_FALLBACK_X_OFFSET = 220f;
        private const float TODO_BUTTON_Y_OFFSET = -10f;

        private readonly Func<bool> isQuestInformationEnabled;
        private readonly IconButtonNode todoButton;
        private readonly TextButtonNode finderButton;
        private readonly TextButtonNode raidFinderButton;
        private readonly TextNineGridNode todoTooltip;
        private readonly TextNineGridNode finderTooltip;
        private readonly TextNineGridNode raidFinderTooltip;

        public DutyLootOverlayNode(DutyLootPreview module)
        {
            isQuestInformationEnabled = module.isQuestInformationEnabled;
            todoButton = new()
            {
                IconId = DUTY_LOOT_ICON_ID,
                Size = new(44f, 44f),
                OnClick = module.ToggleWindow,
                IsVisible = false
            };
            todoButton.BackgroundNode.IsVisible = false;
            todoButton.AttachNode(this);
            todoTooltip = CreateTooltip(todoButton);

            finderButton = CreateFinderButton(module);
            finderButton.AttachNode(this);
            finderTooltip = CreateTooltip(finderButton);

            raidFinderButton = CreateFinderButton(module);
            raidFinderButton.AttachNode(this);
            raidFinderTooltip = CreateTooltip(raidFinderButton);
        }

        public override OverlayLayer OverlayLayer => OverlayLayer.AboveUserInterface;

        public void HideButtonsNow()
        {
            todoButton.IsVisible = false;
            todoTooltip.IsVisible = false;
            finderButton.IsVisible = false;
            finderTooltip.IsVisible = false;
            raidFinderButton.IsVisible = false;
            raidFinderTooltip.IsVisible = false;
        }

        protected override void OnUpdate()
        {
            var questInformationEnabled = isQuestInformationEnabled();
            var betweenAreas = DService.Instance().Condition.IsBetweenAreas;
            var screenReady = UIModule.IsScreenReady();
            var hidden = !questInformationEnabled || betweenAreas || !screenReady;
            IsVisible = !hidden;
            UpdateTodoButton(hidden);
            UpdateFinderButtons(hidden);
        }

        private void UpdateTodoButton(bool hidden)
        {
            if (hidden || !AddonHelper.TryGetByName<AddonToDoList>("_ToDoList", out var addon) ||
                !((AtkUnitBase*)addon)->IsAddonAndNodesReady())
            {
                todoButton.IsVisible = false;
                todoTooltip.IsVisible = false;
                return;
            }

            var dutyNameNode = FindDutyNameNode((AddonToDoList*)addon);
            var nameNode = dutyNameNode != null
                ? dutyNameNode
                : ((AtkUnitBase*)addon)->GetNodeById(4);
            var game = GameMain.Instance();
            var dutyID = game != null ? game->CurrentContentFinderConditionId : 0;
            todoButton.IsVisible = DService.Instance().ClientState.IsLoggedIn &&
                                   dutyID != 0 &&
                                   ((AtkUnitBase*)addon)->IsVisible &&
                                   nameNode != null && nameNode->GetVisibility();
            if (nameNode != null)
            {
                var scale = nameNode->GetScale();
                todoButton.Scale = scale;
                var nameSize = nameNode->GetSize();
                var nameHeight = nameSize.Y * scale.Y;
                todoButton.Position = new(
                    dutyNameNode != null
                        ? nameNode->ScreenX - (todoButton.Width + TODO_BUTTON_GAP) * scale.X
                        : nameNode->ScreenX + TODO_BUTTON_FALLBACK_X_OFFSET * scale.X,
                    nameNode->ScreenY +
                    (nameHeight - todoButton.Height * scale.Y) * 0.5f +
                    TODO_BUTTON_Y_OFFSET * scale.Y);
                PositionTooltip(todoTooltip, todoButton);
            }
        }

        private static AtkResNode* FindDutyNameNode(AddonToDoList* addon)
        {
            var game = GameMain.Instance();
            if (addon == null || game == null ||
                !LuminaGetter.TryGetRow<ContentFinderConditionSheet>(
                    ToContentFinderConditionID((uint)game->CurrentContentFinderConditionId),
                    out var duty))
            {
                return null;
            }

            var dutyName = duty.Name.ToString();
            if (dutyName.Length == 0)
            {
                return null;
            }

            AtkResNode* result = null;
            foreach (var pointer in addon->DutyFinderTextNodes.AsSpan())
            {
                var node = pointer.Value;
                if (node != null && ((AtkResNode*)node)->IsVisible() &&
                    node->NodeText.ToString().Contains(dutyName, StringComparison.Ordinal))
                {
                    result = (AtkResNode*)node;
                }
            }

            return result;
        }

        private void UpdateFinderButtons(bool hidden)
        {
            AtkUnitBase* contentsAddon = null;
            AtkComponentButton* contentsAnchor = null;
            if (!hidden &&
                AddonHelper.TryGetByName<AddonContentsFinder>("ContentsFinder", out var contentsFinder) &&
                ((AtkUnitBase*)contentsFinder)->IsAddonAndNodesReady())
            {
                contentsAddon = (AtkUnitBase*)contentsFinder;
                contentsAnchor = contentsFinder->ClearSelectionButton;
            }

            var agent = AgentContentsFinder.Instance();
            UpdateFinderButton(
                contentsAddon,
                contentsAnchor,
                finderButton,
                agent != null && agent->IsAddonShown() && agent->SelectedDuty.ContentType == ContentsType.Regular &&
                agent->SelectedDuty.Id != 0);

            AtkUnitBase* raidAddon = null;
            AtkComponentButton* raidAnchor = null;
            if (!hidden &&
                AddonHelper.TryGetByName<AddonRaidFinder>("RaidFinder", out var raidFinder) &&
                ((AtkUnitBase*)raidFinder)->IsAddonAndNodesReady())
            {
                raidAddon = (AtkUnitBase*)raidFinder;
                raidAnchor = raidFinder->UnselectButton;
            }

            var raidAgent = AgentRaidFinder.Instance();
            UpdateFinderButton(
                raidAddon,
                raidAnchor,
                raidFinderButton,
                raidAgent != null && raidAgent->IsAddonShown() && raidAgent->InterfaceSub.SelectedDutyId > 0);
        }

        private static TextButtonNode CreateFinderButton(DutyLootPreview module)
        {
            var button = new TextButtonNode
            {
                String = OmniLoc.Get("Feature.DutyLootPreview.Button"),
                OnClick = module.ToggleWindow,
                IsVisible = false
            };
            return button;
        }

        private void UpdateFinderButton(
            AtkUnitBase* addon,
            AtkComponentButton* anchorButton,
            TextButtonNode button,
            bool hasSelectedDuty)
        {
            var anchor = anchorButton != null ? anchorButton->OwnerNode : null;
            button.IsVisible = addon != null && addon->IsVisible && anchor != null && anchor->IsVisible();
            if (!button.IsVisible)
            {
                GetTooltip(button).IsVisible = false;
                return;
            }

            var anchorNode = (AtkResNode*)anchor;
            var anchorScale = anchorNode->GetScale();
            button.Size = new(112f, anchor->Height);
            button.Scale = anchorScale;
            button.Position = new(
                anchorNode->ScreenX - (button.Width + 6f) * anchorScale.X,
                anchorNode->ScreenY);
            button.IsEnabled = DService.Instance().ClientState.IsLoggedIn && hasSelectedDuty;
            PositionTooltip(GetTooltip(button), button);
        }

        private TextNineGridNode GetTooltip(NodeBase button) =>
            button == finderButton ? finderTooltip : raidFinderTooltip;

        private TextNineGridNode CreateTooltip(NodeBase button)
        {
            var tooltip = new TextNineGridNode
            {
                String = OmniLoc.Get("Feature.DutyLootPreview.ButtonTooltip"),
                IsVisible = false
            };
            tooltip.AlignmentType = AlignmentType.Center;
            tooltip.TextColor = ColorHelper.GetColor(50);
            tooltip.TextOutlineColor = ColorHelper.GetColor(7);
            tooltip.FontType = FontType.Axis;
            tooltip.FontSize = 18;
            tooltip.Size = tooltip.TextNode.GetTextDrawSize(false) + new Vector2(16f, 8f);
            tooltip.AttachNode(this);
            button.AddEvent(AtkEventType.MouseOver, () => tooltip.IsVisible = button.IsVisible);
            button.AddEvent(AtkEventType.MouseOut, () => tooltip.IsVisible = false);
            return tooltip;
        }

        private static void PositionTooltip(TextNineGridNode tooltip, NodeBase button)
        {
            tooltip.Scale = Vector2.One;
            tooltip.Position = button.Position + new Vector2(
                (button.Width * button.Scale.X - tooltip.Width) * 0.5f,
                -tooltip.Height - 4f);
        }
    }

    public sealed class Config
    {
        public HashSet<uint> FavoriteItems = [];
    }
}
