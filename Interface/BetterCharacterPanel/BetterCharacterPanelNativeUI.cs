using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Controllers;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;
using KamiToolKit.Premade.Node.Simple;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.UI;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class BetterCharacterPanelNativeUI : IDisposable
{
    private const string CharacterStatusAddonName = "CharacterStatus";
    private const string CharacterAddonName = "Character";
    private const string CharacterInspectAddonName = "CharacterInspect";
    private const string GearSetListAddonName = "GearSetList";
    private const ushort GearSetReorderExtraWidth = 56;
    private const float GearSetReorderButtonSize = 32f;

    private readonly BetterCharacterPanelConfig config;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly BetterCharacterPanelStatusUI statusUI;
    private readonly NativeListController<AddonGearSetList, GearSetListItem> gearSetListController;
    private readonly Dictionary<uint, GearSetReorderButtons> gearSetButtons = [];
    private readonly Dictionary<nint, EquipmentLayoutSnapshot> equipmentLayouts = [];
    private readonly Dictionary<nint, short> childAddonPositions = [];

    private AddonCharacter* reversedCharacterAddon;
    private float originalCharacterNodeX;
    private AtkUnitBase* adjustedGearSetList;
    private GearSetListSnapshot gearSetListSnapshot;
    private bool gearSetListAdjusted;

    public BetterCharacterPanelNativeUI(BetterCharacterPanelConfig config)
    {
        this.config = config;
        statusUI = new(config);
        addonEvents = new(DalamudServices.AddonLifecycle);
        gearSetListController = new()
        {
            AddonName = GearSetListAddonName,
            ShouldModifyElement = (_, _) => config.ShowGearSetReorderButtons,
            GetPopulatorNode = GetGearSetPopulator,
            UpdateElement = UpdateGearSetListElement,
            ResetElement = (_, item) => RemoveGearSetButtons(item.NodeId)
        };

        lifetime.Add(RestoreAll);
        lifetime.Add(gearSetListController.Dispose);
        try
        {
            gearSetListController.Enable();
            if (!FrameworkManager.Instance().Reg(OnFrameworkUpdate, 100))
            {
                throw new InvalidOperationException("Better character panel update registration failed.");
            }

            lifetime.Add(() => FrameworkManager.Instance().Unreg(OnFrameworkUpdate));
            lifetime.Add(addonEvents.Dispose);
            RegisterAddonEvents();
            BootstrapOpenAddons();
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    public void RefreshSettings()
    {
        if (config.ShowUsefulStats
            && AddonHelper.TryGetByName(CharacterStatusAddonName, out AtkUnitBase* characterStatus)
            && characterStatus->IsAddonAndNodesReady())
        {
            statusUI.Setup(characterStatus);
            statusUI.Refresh(force: true);
        }
        else if (!config.ShowUsefulStats)
        {
            statusUI.Restore();
        }

        if (AddonHelper.TryGetByName<AddonCharacter>(CharacterAddonName, out var character)
            && character->AtkUnitBase.IsAddonAndNodesReady())
        {
            if (config.ReverseCharacterPanel)
            {
                ApplyReverseCharacterPanel(character);
                UpdateReverseCharacterPanel(character);
            }
            else
            {
                RestoreReverseCharacterPanel(character);
            }
        }

        RestoreAllEquipmentLayouts();
        if (config.AdjustEquipmentPositions)
        {
            ApplyOpenEquipmentLayouts();
        }

        if (AddonHelper.TryGetByName(GearSetListAddonName, out AtkUnitBase* gearSetList)
            && gearSetList->IsAddonAndNodesReady())
        {
            if (config.ShowGearSetReorderButtons)
            {
                SetupGearSetList(gearSetList);
                var list = gearSetList->GetComponentListById(7);
                if (list != null)
                {
                    list->IsUpdatePending = true;
                }
            }
            else
            {
                RestoreGearSetList(gearSetList);
            }
        }
        else if (!config.ShowGearSetReorderButtons)
        {
            ClearGearSetButtons();
        }
    }

    public void Dispose() => lifetime.Dispose();

    private void RegisterAddonEvents()
    {
        addonEvents.Register(AddonEvent.PostSetup, CharacterStatusAddonName, OnCharacterStatusAddon);
        addonEvents.Register(AddonEvent.PreRequestedUpdate, CharacterStatusAddonName, OnCharacterStatusAddon);
        addonEvents.Register(AddonEvent.PreFinalize, CharacterStatusAddonName, OnCharacterStatusAddon);
        addonEvents.Register(AddonEvent.PostSetup, CharacterAddonName, OnCharacterAddon);
        addonEvents.Register(AddonEvent.PreRequestedUpdate, CharacterAddonName, OnCharacterAddon);
        addonEvents.Register(AddonEvent.PreFinalize, CharacterAddonName, OnCharacterAddon);
        addonEvents.Register(AddonEvent.PostSetup, CharacterInspectAddonName, OnCharacterInspectAddon);
        addonEvents.Register(AddonEvent.PreFinalize, CharacterInspectAddonName, OnCharacterInspectAddon);
        addonEvents.Register(AddonEvent.PostSetup, GearSetListAddonName, OnGearSetListAddon);
        addonEvents.Register(AddonEvent.PreRequestedUpdate, GearSetListAddonName, OnGearSetListAddon);
        addonEvents.Register(AddonEvent.PreFinalize, GearSetListAddonName, OnGearSetListAddon);
    }

    private void BootstrapOpenAddons()
    {
        if (AddonHelper.TryGetByName(CharacterStatusAddonName, out AtkUnitBase* characterStatus)
            && characterStatus->IsAddonAndNodesReady())
        {
            statusUI.Setup(characterStatus);
        }

        if (AddonHelper.TryGetByName<AddonCharacter>(CharacterAddonName, out var character)
            && character->AtkUnitBase.IsAddonAndNodesReady())
        {
            ApplyCharacterEquipmentPositions((AtkUnitBase*)character);
            ApplyReverseCharacterPanel(character);
            UpdateReverseCharacterPanel(character);
        }

        if (AddonHelper.TryGetByName(CharacterInspectAddonName, out AtkUnitBase* inspect)
            && inspect->IsAddonAndNodesReady())
        {
            ApplyInspectEquipmentPositions(inspect);
        }

        if (AddonHelper.TryGetByName(GearSetListAddonName, out AtkUnitBase* gearSetList)
            && gearSetList->IsAddonAndNodesReady())
        {
            SetupGearSetList(gearSetList);
        }
    }

    private void OnCharacterStatusAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            statusUI.Restore();
            return;
        }

        var characterStatus = (AtkUnitBase*)args.Addon.Address;
        if (eventType == AddonEvent.PostSetup)
        {
            statusUI.Setup(characterStatus);
        }

        statusUI.Refresh();
    }

    private void OnCharacterAddon(AddonEvent eventType, AddonArgs args)
    {
        var character = (AddonCharacter*)args.Addon.Address;
        switch (eventType)
        {
            case AddonEvent.PostSetup:
                ApplyCharacterEquipmentPositions((AtkUnitBase*)character);
                ApplyReverseCharacterPanel(character);
                break;
            case AddonEvent.PreRequestedUpdate:
                UpdateReverseCharacterPanel(character);
                break;
            case AddonEvent.PreFinalize:
                RestoreEquipmentLayout((AtkUnitBase*)character);
                RestoreReverseCharacterPanel(character);
                break;
        }
    }

    private void OnCharacterInspectAddon(AddonEvent eventType, AddonArgs args)
    {
        var inspect = (AtkUnitBase*)args.Addon.Address;
        if (eventType == AddonEvent.PostSetup)
        {
            ApplyInspectEquipmentPositions(inspect);
        }
        else if (eventType == AddonEvent.PreFinalize)
        {
            RestoreEquipmentLayout(inspect);
        }
    }

    private void OnGearSetListAddon(AddonEvent eventType, AddonArgs args)
    {
        var gearSetList = (AtkUnitBase*)args.Addon.Address;
        switch (eventType)
        {
            case AddonEvent.PostSetup:
                SetupGearSetList(gearSetList);
                break;
            case AddonEvent.PreRequestedUpdate:
                UpdateGearSetList(gearSetList);
                break;
            case AddonEvent.PreFinalize:
                RestoreGearSetList(gearSetList);
                break;
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        statusUI.Refresh();
        if (reversedCharacterAddon != null)
        {
            UpdateReverseCharacterPanel(reversedCharacterAddon);
        }
    }

    private void ApplyReverseCharacterPanel(AddonCharacter* character)
    {
        if (character == null || !config.ReverseCharacterPanel)
        {
            RestoreReverseCharacterPanel(character);
            return;
        }

        if (reversedCharacterAddon == character)
        {
            return;
        }

        RestoreReverseCharacterPanel(reversedCharacterAddon);
        var characterNode = character->GetNodeById(10);
        if (characterNode == null)
        {
            return;
        }

        originalCharacterNodeX = characterNode->X;
        characterNode->SetPositionFloat(originalCharacterNodeX - 380f, characterNode->Y);
        reversedCharacterAddon = character;
    }

    private void UpdateReverseCharacterPanel(AddonCharacter* character)
    {
        if (character == null || !config.ReverseCharacterPanel)
        {
            RestoreReverseCharacterPanel(character);
            return;
        }

        if (reversedCharacterAddon != character)
        {
            ApplyReverseCharacterPanel(character);
        }

        if (reversedCharacterAddon != character || character->AtkUnitBase.RootNode == null)
        {
            return;
        }

        foreach (var child in character->AddonControl.ChildAddons)
        {
            var childInfo = child.Value;
            if (childInfo == null)
            {
                continue;
            }

            childAddonPositions.TryAdd((nint)childInfo, childInfo->PositionX);
            childInfo->PositionX = (short)(character->AtkUnitBase.RootNode->Width - 386f);
        }
    }

    private void RestoreReverseCharacterPanel(AddonCharacter* character)
    {
        if (character == null || reversedCharacterAddon != character)
        {
            return;
        }

        var characterNode = character->GetNodeById(10);
        if (characterNode != null)
        {
            characterNode->SetPositionFloat(originalCharacterNodeX, characterNode->Y);
        }

        foreach (var child in character->AddonControl.ChildAddons)
        {
            var childInfo = child.Value;
            if (childInfo != null && childAddonPositions.TryGetValue((nint)childInfo, out var originalX))
            {
                childInfo->PositionX = originalX;
            }
        }

        childAddonPositions.Clear();
        reversedCharacterAddon = null;
        originalCharacterNodeX = 0f;
    }

    private void ApplyOpenEquipmentLayouts()
    {
        if (AddonHelper.TryGetByName(CharacterAddonName, out AtkUnitBase* character)
            && character->IsAddonAndNodesReady())
        {
            ApplyCharacterEquipmentPositions(character);
        }

        if (AddonHelper.TryGetByName(CharacterInspectAddonName, out AtkUnitBase* inspect)
            && inspect->IsAddonAndNodesReady())
        {
            ApplyInspectEquipmentPositions(inspect);
        }
    }

    private void ApplyCharacterEquipmentPositions(AtkUnitBase* character)
    {
        if (!config.AdjustEquipmentPositions || character == null || equipmentLayouts.ContainsKey((nint)character))
        {
            return;
        }

        var snapshot = new EquipmentLayoutSnapshot(character);
        equipmentLayouts[(nint)character] = snapshot;
        var applied = config.SoulstoneAboveOffhand
            ? MoveNode(snapshot, 61, 262f, -1f)
              && MoveNode(snapshot, 46, 262f, 0f)
              && MoveNode(snapshot, 32, 280f, 25f)
            : ShiftUp(snapshot, 50, 262f, -1f, [56, 57, 58, 59, 60, 61])
              && ShiftUp(snapshot, 35, 262f, 0f, [41, 42, 43, 44, 45, 46])
              && ShiftUp(snapshot, 21, 280f, 25f, [27, 28, 29, 30, 31, 32]);
        if (!applied)
        {
            RestoreEquipmentLayout(character);
        }
    }

    private void ApplyInspectEquipmentPositions(AtkUnitBase* inspect)
    {
        if (!config.AdjustEquipmentPositions || inspect == null || equipmentLayouts.ContainsKey((nint)inspect))
        {
            return;
        }

        var snapshot = new EquipmentLayoutSnapshot(inspect);
        equipmentLayouts[(nint)inspect] = snapshot;
        var applied = config.SoulstoneAboveOffhand
            ? MoveNode(snapshot, 54, 262f, -119f)
              && MoveNode(snapshot, 68, 262f, -119f)
            : ShiftUp(snapshot, 43, 262f, -119f, [49, 50, 51, 52, 53, 54])
              && ShiftUp(snapshot, 57, 262f, -119f, [63, 64, 65, 66, 67, 68]);
        if (!applied)
        {
            RestoreEquipmentLayout(inspect);
        }
    }

    private static bool MoveNode(EquipmentLayoutSnapshot snapshot, uint nodeID, float x, float y)
    {
        var node = snapshot.Addon->GetNodeById(nodeID);
        if (node == null)
        {
            return false;
        }

        snapshot.Capture(node);
        node->SetPositionFloat(x, y);
        return true;
    }

    private static bool ShiftUp(
        EquipmentLayoutSnapshot snapshot,
        uint firstNodeID,
        float firstNodeX,
        float firstNodeY,
        ReadOnlySpan<uint> moveNodeIds)
    {
        var firstNode = snapshot.Addon->GetNodeById(firstNodeID);
        if (firstNode == null)
        {
            return false;
        }

        Span<nint> nodes = stackalloc nint[moveNodeIds.Length];
        for (var index = 0; index < moveNodeIds.Length; index++)
        {
            nodes[index] = (nint)snapshot.Addon->GetNodeById(moveNodeIds[index]);
            if (nodes[index] == nint.Zero)
            {
                return false;
            }
        }

        snapshot.Capture(firstNode);
        var previousX = firstNode->X;
        var previousY = firstNode->Y;
        firstNode->SetPositionFloat(firstNodeX, firstNodeY);
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = (AtkResNode*)nodes[index];
            snapshot.Capture(node);
            var nextX = node->X;
            var nextY = node->Y;
            node->SetPositionFloat(previousX, previousY);
            previousX = nextX;
            previousY = nextY;
        }

        return true;
    }

    private void RestoreEquipmentLayout(AtkUnitBase* targetAddon)
    {
        if (targetAddon == null || !equipmentLayouts.Remove((nint)targetAddon, out var snapshot))
        {
            return;
        }

        snapshot.Restore();
    }

    private void RestoreAllEquipmentLayouts()
    {
        while (equipmentLayouts.Count > 0)
        {
            using var enumerator = equipmentLayouts.GetEnumerator();
            enumerator.MoveNext();
            RestoreEquipmentLayout((AtkUnitBase*)enumerator.Current.Key);
        }
    }

    private void SetupGearSetList(AtkUnitBase* gearSetList)
    {
        if (gearSetList == null || !config.ShowGearSetReorderButtons)
        {
            RestoreGearSetList(gearSetList);
            return;
        }

        if (gearSetListAdjusted && adjustedGearSetList == gearSetList)
        {
            return;
        }

        RestoreGearSetList(adjustedGearSetList);
        var helpButton = gearSetList->GetNodeById(2);
        var countText = gearSetList->GetNodeById(10);
        var listNode = gearSetList->GetNodeById(7);
        if (listNode == null)
        {
            return;
        }

        ushort width;
        ushort height;
        gearSetList->GetSize(&width, &height, false);
        gearSetListSnapshot = new(
            width,
            height,
            (nint)helpButton,
            helpButton == null ? 0f : helpButton->X,
            (nint)countText,
            countText == null ? 0f : countText->X,
            (nint)listNode,
            listNode->Width);
        if (helpButton != null)
        {
            helpButton->SetPositionFloat(helpButton->X + GearSetReorderExtraWidth, helpButton->Y);
        }

        if (countText != null)
        {
            countText->SetPositionFloat(countText->X + GearSetReorderExtraWidth, countText->Y);
        }

        listNode->SetWidth((ushort)(listNode->Width + GearSetReorderExtraWidth));
        gearSetList->Size = new(width + GearSetReorderExtraWidth, height);
        adjustedGearSetList = gearSetList;
        gearSetListAdjusted = true;
    }

    private static AtkComponentListItemRenderer* GetGearSetPopulator(AddonGearSetList* addon)
    {
        var list = addon->GetComponentListById(7);
        return list == null ? null : list->FirstAtkComponentListItemRenderer;
    }

    private void UpdateGearSetList(AtkUnitBase* gearSetList)
    {
        if (!config.ShowGearSetReorderButtons)
        {
            RestoreGearSetList(gearSetList);
        }
        else if (!gearSetListAdjusted || adjustedGearSetList != gearSetList)
        {
            SetupGearSetList(gearSetList);
        }
    }

    private void RestoreGearSetList(AtkUnitBase* gearSetList)
    {
        ClearGearSetButtons();
        if (gearSetList == null || !gearSetListAdjusted || adjustedGearSetList != gearSetList)
        {
            return;
        }

        gearSetList->Size = new(gearSetListSnapshot.Width, gearSetListSnapshot.Height);
        var helpButton = (AtkResNode*)gearSetListSnapshot.HelpButton;
        if (helpButton != null)
        {
            helpButton->SetPositionFloat(gearSetListSnapshot.HelpButtonX, helpButton->Y);
        }

        var countText = (AtkResNode*)gearSetListSnapshot.CountText;
        if (countText != null)
        {
            countText->SetPositionFloat(gearSetListSnapshot.CountTextX, countText->Y);
        }

        var listNode = (AtkResNode*)gearSetListSnapshot.ListNode;
        if (listNode != null)
        {
            listNode->SetWidth(gearSetListSnapshot.ListWidth);
        }

        adjustedGearSetList = null;
        gearSetListSnapshot = default;
        gearSetListAdjusted = false;
    }

    private void UpdateGearSetListElement(AddonGearSetList* gearSetList, GearSetListItem item)
    {
        var renderer = item.Renderer;
        var ownerNode = renderer == null ? null : renderer->OwnerNode;
        var collisionNode = item.CollisionNode;
        var indexNode = item.GearSetIndexNode;
        if (!config.ShowGearSetReorderButtons
            || gearSetList == null
            || renderer == null
            || ownerNode == null
            || collisionNode == null
            || indexNode == null)
        {
            return;
        }

        if (!gearSetListAdjusted || adjustedGearSetList != (AtkUnitBase*)gearSetList)
        {
            SetupGearSetList((AtkUnitBase*)gearSetList);
        }

        if (!gearSetButtons.TryGetValue(item.NodeId, out var buttons))
        {
            var buttonX = collisionNode->AtkResNode.Width - GearSetReorderExtraWidth - 6f;
            buttons = new(ownerNode, buttonX);
            gearSetButtons[item.NodeId] = buttons;
            buttons.Attach(ownerNode);
            ownerNode->AtkResNode.SetWidth((ushort)Math.Max(0, ownerNode->AtkResNode.Width - 60));
        }

        buttons.Update(item.GearSetID, item.ItemIndex, item.IsChecked);
    }

    private void ClearGearSetButtons()
    {
        while (gearSetButtons.Count > 0)
        {
            using var enumerator = gearSetButtons.GetEnumerator();
            enumerator.MoveNext();
            RemoveGearSetButtons(enumerator.Current.Key);
        }
    }

    private void RemoveGearSetButtons(uint nodeID)
    {
        if (!gearSetButtons.Remove(nodeID, out var buttons))
        {
            return;
        }

        buttons.RestoreWidth();
        buttons.Dispose();
    }

    private void RestoreAll()
    {
        statusUI.Restore();
        RestoreReverseCharacterPanel(reversedCharacterAddon);
        RestoreGearSetList(adjustedGearSetList);
        RestoreAllEquipmentLayouts();
    }

    private sealed unsafe class GearSetListItem : ListItemData
    {
        public AtkComponentListItemRenderer* Renderer =>
            ItemRenderer != null
                ? ItemRenderer
                : ItemInfo != null
                    ? ItemInfo->ListItem->Renderer
                    : null;

        public AtkTextNode* GearSetIndexNode => GetNode<AtkTextNode>(1);

        public AtkCollisionNode* CollisionNode => Renderer == null ? null : Renderer->GetCollisionNodeById(16);

        public bool IsChecked => Renderer != null && Renderer->IsChecked;

        public int GearSetID => GearSetIndexNode != null
                                && int.TryParse(GearSetIndexNode->NodeText.ToString(), out var id)
            ? Math.Max(0, id - 1)
            : 0;
    }

    private sealed unsafe class GearSetReorderButtons : IDisposable
    {
        private readonly AtkComponentNode* ownerNode;
        private readonly ushort originalWidth;
        private readonly SimpleComponentNode container;
        private readonly CircleButtonNode upButton;
        private readonly CircleButtonNode downButton;
        private int gearSetID;

        public GearSetReorderButtons(AtkComponentNode* ownerNode, float x)
        {
            this.ownerNode = ownerNode;
            originalWidth = ownerNode->AtkResNode.Width;
            container = new()
            {
                Size = new(GearSetReorderExtraWidth - 4f, 28f),
                Position = new(x, 0f)
            };
            upButton = new()
            {
                Icon = ButtonIcon.UpArrow,
                Size = new(GearSetReorderButtonSize, GearSetReorderButtonSize),
                OnClick = MoveUp,
                IsEnabled = false
            };
            downButton = new()
            {
                Icon = ButtonIcon.ArrowDown,
                Size = new(GearSetReorderButtonSize, GearSetReorderButtonSize),
                Position = new(28f, 0f),
                OnClick = MoveDown,
                IsEnabled = false
            };
            upButton.SetTextTooltip(OmniLoc.Get("Feature.BetterCharacterPanel.GearSet.MoveUp"));
            downButton.SetTextTooltip(OmniLoc.Get("Feature.BetterCharacterPanel.GearSet.MoveDown"));
            upButton.AttachNode(container);
            downButton.AttachNode(container);
        }

        public void Attach(AtkComponentNode* target) => container.AttachNode(target, NodePosition.AsLastChild);

        public void Update(int id, int itemIndex, bool selected)
        {
            gearSetID = id;
            upButton.IsEnabled = itemIndex > 0;
            var gearsetModule = RaptureGearsetModule.Instance();
            downButton.IsEnabled = gearsetModule != null && itemIndex < gearsetModule->NumGearsets - 1;
            container.IsVisible = selected;
        }

        public void RestoreWidth()
        {
            if (ownerNode != null)
            {
                ownerNode->AtkResNode.SetWidth(originalWidth);
            }
        }

        public void Dispose() => container.Dispose();

        private void MoveUp()
        {
            var agent = AgentGearSet.Instance();
            if (agent != null)
            {
                agent->MoveSetUp(gearSetID);
            }
        }

        private void MoveDown()
        {
            var agent = AgentGearSet.Instance();
            if (agent != null)
            {
                agent->MoveSetDown(gearSetID);
            }
        }
    }

    private sealed unsafe class EquipmentLayoutSnapshot(AtkUnitBase* addon)
    {
        private readonly List<NodePositionSnapshot> positions = [];

        public AtkUnitBase* Addon { get; } = addon;

        public void Capture(AtkResNode* node) => positions.Add(new((nint)node, node->X, node->Y));

        public void Restore()
        {
            for (var index = positions.Count - 1; index >= 0; index--)
            {
                var position = positions[index];
                ((AtkResNode*)position.Address)->SetPositionFloat(position.X, position.Y);
            }
        }
    }

    private readonly record struct NodePositionSnapshot(nint Address, float X, float Y);

    private readonly record struct GearSetListSnapshot(
        ushort Width,
        ushort Height,
        nint HelpButton,
        float HelpButtonX,
        nint CountText,
        float CountTextX,
        nint ListNode,
        ushort ListWidth);
}
