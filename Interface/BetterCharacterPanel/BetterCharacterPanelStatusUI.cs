using System.Globalization;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using OmniToolbox.Config;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class BetterCharacterPanelStatusUI(BetterCharacterPanelConfig config) : IDisposable
{
    private const float RowHeight = 20f;
    private static readonly ByteColor SecondaryTextColor = new() { A = 0xFF, R = 0xA0, G = 0xA0, B = 0xA0 };

    private readonly List<NodeBase> injectedNodes = [];
    private readonly Dictionary<nint, NodeSnapshot> nodeSnapshots = [];
    private readonly Dictionary<nint, TextSnapshot> textSnapshots = [];

    private AtkUnitBase* addon;
    private AtkResNode* attributes;
    private AtkResNode* offensive;
    private AtkResNode* defensive;
    private AtkResNode* physicalProperties;
    private AtkResNode* gear;
    private AtkResNode* piety;
    private AtkResNode* tenacity;
    private AtkResNode* spellSpeed;
    private AtkResNode* skillSpeed;
    private AtkResNode* crafting;
    private AtkResNode* gathering;
    private StatRow? directHitChance;
    private StatRow? directHitDamage;
    private StatRow? determinationBonus;
    private StatRow? criticalChance;
    private StatRow? criticalDamage;
    private StatRow? criticalExpected;
    private StatRow? physicalMitigation;
    private StatRow? magicMitigation;
    private StatRow? skillSpeedBonus;
    private StatRow? skillGcd;
    private StatRow? spellSpeedBonus;
    private StatRow? spellGcd;
    private StatRow? tenacityMitigation;
    private StatRow? tenacityDamage;
    private StatRow? manaPerTick;
    private StatRow? expectedDamage;
    private StatRow? expectedHeal;
    private StatRow? craftingPoints;
    private StatRow? gatheringPoints;
    private BetterCharacterPanelStats? lastStats;
    private bool layoutApplied;

    public bool Setup(AtkUnitBase* characterStatus)
    {
        if (!config.ShowUsefulStats || characterStatus == null)
        {
            Restore();
            return false;
        }

        if (layoutApplied && addon == characterStatus)
        {
            return true;
        }

        Restore();
        addon = characterStatus;
        attributes = addon->UldManager.SearchNodeById(26);
        var mentalProperties = addon->UldManager.SearchNodeById(58);
        offensive = addon->UldManager.SearchNodeById(36);
        defensive = addon->UldManager.SearchNodeById(44);
        physicalProperties = addon->UldManager.SearchNodeById(51);
        gear = addon->UldManager.SearchNodeById(80);
        var roleProperties = addon->UldManager.SearchNodeById(86);
        crafting = addon->UldManager.SearchNodeById(73);
        gathering = addon->UldManager.SearchNodeById(66);
        if (attributes == null
            || mentalProperties == null
            || offensive == null
            || defensive == null
            || physicalProperties == null
            || roleProperties == null)
        {
            Restore();
            return false;
        }

        var mindNode = attributes->ChildNode;
        var intelligenceNode = Previous(mindNode);
        var vitalityNode = Previous(intelligenceNode);
        var dexterityNode = Previous(vitalityNode);
        var strengthNode = Previous(dexterityNode);
        var magicAttackPotency = Previous(mentalProperties->ChildNode);
        var healingMagicPotency = Previous(magicAttackPotency);
        if (mindNode == null
            || intelligenceNode == null
            || vitalityNode == null
            || dexterityNode == null
            || strengthNode == null
            || magicAttackPotency == null
            || healingMagicPotency == null)
        {
            Restore();
            return false;
        }

        SetPosition(mindNode, 10f, 40f);
        SetPosition(intelligenceNode, 10f, 40f);
        SetPosition(vitalityNode, vitalityNode->X, 20f);
        SetPosition(dexterityNode, dexterityNode->X, 40f);
        SetPosition(strengthNode, strengthNode->X, 40f);
        AddNativeTooltip((AtkComponentNode*)mindNode, BetterCharacterPanelTooltip.MainStat);
        AddNativeTooltip((AtkComponentNode*)intelligenceNode, BetterCharacterPanelTooltip.MainStat);
        AddNativeTooltip((AtkComponentNode*)dexterityNode, BetterCharacterPanelTooltip.MainStat);
        AddNativeTooltip((AtkComponentNode*)strengthNode, BetterCharacterPanelTooltip.MainStat);
        AddNativeTooltip((AtkComponentNode*)vitalityNode, BetterCharacterPanelTooltip.Vitality);

        var attributesHeight = 150f;
        SetPosition(healingMagicPotency, healingMagicPotency->X, -attributesHeight - 10f);
        expectedHeal = AddRow(
            (AtkComponentNode*)healingMagicPotency,
            "Feature.BetterCharacterPanel.Row.ExpectedHeal",
            BetterCharacterPanelTooltip.ExpectedHeal,
            hideOriginal: true);

        attributesHeight += RowHeight;
        SetPosition(magicAttackPotency, magicAttackPotency->X, -attributesHeight - 10f);
        SetVisible(Previous(magicAttackPotency), false);
        expectedDamage = AddRow(
            (AtkComponentNode*)magicAttackPotency,
            "Feature.BetterCharacterPanel.Row.ExpectedDamage",
            BetterCharacterPanelTooltip.ExpectedDamage,
            hideOriginal: true);

        var offensiveHeight = 130f;
        SetPosition(offensive, offensive->X, attributesHeight);
        var directHitNode = offensive->ChildNode;
        if (directHitNode == null)
        {
            Restore();
            return false;
        }

        SetPosition(directHitNode, directHitNode->X, 120f);
        directHitChance = AddRow(
            (AtkComponentNode*)directHitNode,
            "Feature.BetterCharacterPanel.Row.DirectHitChance",
            BetterCharacterPanelTooltip.DirectHit);
        offensiveHeight += RowHeight;
        MoveY(magicAttackPotency, -RowHeight);
        MoveY(healingMagicPotency, -RowHeight);
        directHitDamage = AddRow(
            (AtkComponentNode*)directHitNode,
            "Feature.BetterCharacterPanel.Row.DirectHitDamage",
            BetterCharacterPanelTooltip.DirectHit);

        var determinationNode = Previous(directHitNode);
        var criticalNode = Previous(determinationNode);
        if (determinationNode == null || criticalNode == null)
        {
            Restore();
            return false;
        }

        SetPosition(determinationNode, determinationNode->X, 80f);
        determinationBonus = AddRow(
            (AtkComponentNode*)determinationNode,
            "Feature.BetterCharacterPanel.Row.DeterminationBonus",
            BetterCharacterPanelTooltip.Determination);
        criticalChance = AddRow(
            (AtkComponentNode*)criticalNode,
            "Feature.BetterCharacterPanel.Row.CriticalChance",
            BetterCharacterPanelTooltip.CriticalHit);
        criticalDamage = AddRow(
            (AtkComponentNode*)criticalNode,
            "Feature.BetterCharacterPanel.Row.CriticalDamage",
            BetterCharacterPanelTooltip.CriticalHit);
        offensiveHeight += RowHeight;
        MoveY(directHitNode, RowHeight);
        MoveY(determinationNode, RowHeight);
        MoveY(magicAttackPotency, -RowHeight);
        MoveY(healingMagicPotency, -RowHeight);
        criticalExpected = AddRow(
            (AtkComponentNode*)criticalNode,
            "Feature.BetterCharacterPanel.Row.CriticalExpected",
            BetterCharacterPanelTooltip.CriticalHit);

        SetPosition(defensive, defensive->X, attributesHeight);
        var magicDefenseNode = defensive->ChildNode;
        var defenseNode = Previous(magicDefenseNode);
        if (magicDefenseNode == null || defenseNode == null)
        {
            Restore();
            return false;
        }

        SetPosition(magicDefenseNode, magicDefenseNode->X, 60f);
        magicMitigation = AddRow(
            (AtkComponentNode*)magicDefenseNode,
            "Feature.BetterCharacterPanel.Row.MagicMitigation",
            BetterCharacterPanelTooltip.MagicDefense);
        physicalMitigation = AddRow(
            (AtkComponentNode*)defenseNode,
            "Feature.BetterCharacterPanel.Row.PhysicalMitigation",
            BetterCharacterPanelTooltip.Defense);

        SetPosition(mentalProperties, 0f, attributesHeight + offensiveHeight);
        spellSpeed = mentalProperties->ChildNode;
        if (spellSpeed == null)
        {
            Restore();
            return false;
        }

        SetVisible(Previous(Previous(magicAttackPotency)), false);
        spellSpeedBonus = AddRow(
            (AtkComponentNode*)spellSpeed,
            "Feature.BetterCharacterPanel.Row.SpellSpeedBonus",
            BetterCharacterPanelTooltip.Speed);
        spellGcd = AddRow(
            (AtkComponentNode*)spellSpeed,
            "Feature.BetterCharacterPanel.Row.Gcd",
            BetterCharacterPanelTooltip.Speed);

        SetPosition(physicalProperties, physicalProperties->X, attributesHeight + offensiveHeight + 40f);
        skillSpeed = physicalProperties->ChildNode;
        if (skillSpeed == null)
        {
            Restore();
            return false;
        }

        SetPosition(skillSpeed, skillSpeed->X, 20f);
        SetVisible(Previous(skillSpeed), false);
        SetSpeedHeaderText(skillSpeed, OmniLoc.Get("Feature.BetterCharacterPanel.Row.SpeedHeader"));
        skillSpeedBonus = AddRow(
            (AtkComponentNode*)skillSpeed,
            "Feature.BetterCharacterPanel.Row.SkillSpeedBonus",
            BetterCharacterPanelTooltip.Speed);
        skillGcd = AddRow(
            (AtkComponentNode*)skillSpeed,
            "Feature.BetterCharacterPanel.Row.Gcd",
            BetterCharacterPanelTooltip.Speed);

        if (gear != null)
        {
            SetPosition(gear, 183f, attributesHeight + offensiveHeight + 40f);
        }

        SetPosition(roleProperties, roleProperties->X, 60f);
        piety = roleProperties->ChildNode;
        tenacity = Previous(piety);
        if (piety != null)
        {
            SetPosition(piety, piety->X, 20f);
            manaPerTick = AddRow(
                (AtkComponentNode*)piety,
                "Feature.BetterCharacterPanel.Row.ManaPerTick",
                BetterCharacterPanelTooltip.Piety);
        }

        if (tenacity != null)
        {
            tenacityMitigation = AddRow(
                (AtkComponentNode*)tenacity,
                "Feature.BetterCharacterPanel.Row.TenacityMitigation",
                BetterCharacterPanelTooltip.Tenacity);
            tenacityDamage = AddRow(
                (AtkComponentNode*)tenacity,
                "Feature.BetterCharacterPanel.Row.TenacityDamage",
                BetterCharacterPanelTooltip.Tenacity);
            SetVisible(Previous(tenacity), false);
        }

        if (crafting != null)
        {
            SetPosition(crafting, 0f, 80f);
            var controlNode = crafting->ChildNode;
            if (controlNode != null)
            {
                craftingPoints = AddRow(
                    (AtkComponentNode*)controlNode,
                    "Feature.BetterCharacterPanel.Row.CraftingPoints",
                    null,
                    copyColor: true,
                    expandCollision: false);
                SetVisible(Previous(Previous(controlNode)), false);
            }
        }

        if (gathering != null)
        {
            SetPosition(gathering, 0f, 80f);
            var perceptionNode = gathering->ChildNode;
            if (perceptionNode != null)
            {
                gatheringPoints = AddRow(
                    (AtkComponentNode*)perceptionNode,
                    "Feature.BetterCharacterPanel.Row.GatheringPoints",
                    null,
                    copyColor: true,
                    expandCollision: false);
                SetVisible(Previous(Previous(perceptionNode)), false);
            }
        }

        layoutApplied = HasRequiredRows();
        if (!layoutApplied)
        {
            Restore();
            return false;
        }

        addon->UldManager.UpdateDrawNodeList();
        Refresh(force: true);
        return true;
    }

    public void Refresh(bool force = false)
    {
        if (!config.ShowUsefulStats)
        {
            Restore();
            return;
        }

        if (!layoutApplied || addon == null || !IsVisible(addon))
        {
            return;
        }

        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return;
        }

        var stats = BetterCharacterPanelState.Calculate(playerState);
        if (!force && lastStats is { } previous && previous == stats)
        {
            return;
        }

        UpdateJobVisibility(stats.JobID);
        lastStats = stats;
        if (BetterCharacterPanelState.IsCrafter(stats.JobID))
        {
            SetRowText(craftingPoints, stats.CraftingPoints.ToString(CultureInfo.CurrentCulture));
            return;
        }

        if (BetterCharacterPanelState.IsGatherer(stats.JobID))
        {
            SetRowText(gatheringPoints, stats.GatheringPoints.ToString(CultureInfo.CurrentCulture));
            return;
        }

        SetRowText(criticalChance, stats.CriticalRate.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetRowText(criticalDamage, stats.CriticalDamage.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetRowText(
            criticalExpected,
            (stats.CriticalRate.DisplayValue * (stats.CriticalDamage.DisplayValue - 1d))
            .ToString("P1", CultureInfo.CurrentCulture));
        SetTooltip(criticalChance, BetterCharacterPanelTooltip.CriticalHit, stats);
        SetTooltip(criticalDamage, BetterCharacterPanelTooltip.CriticalHit, stats);
        SetTooltip(criticalExpected, BetterCharacterPanelTooltip.CriticalHit, stats);

        SetRowText(directHitChance, stats.DirectHit.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetRowText(directHitDamage, (stats.DirectHit.DisplayValue * 0.25d).ToString("P1", CultureInfo.CurrentCulture));
        SetTooltip(directHitChance, BetterCharacterPanelTooltip.DirectHit, stats);
        SetTooltip(directHitDamage, BetterCharacterPanelTooltip.DirectHit, stats);

        SetRowText(determinationBonus, stats.Determination.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetTooltip(determinationBonus, BetterCharacterPanelTooltip.Determination, stats);
        SetRowText(physicalMitigation, stats.PhysicalMitigation.DisplayValue.ToString("P0", CultureInfo.CurrentCulture));
        SetTooltip(physicalMitigation, BetterCharacterPanelTooltip.Defense, stats);
        SetRowText(magicMitigation, stats.MagicMitigation.DisplayValue.ToString("P0", CultureInfo.CurrentCulture));
        SetTooltip(magicMitigation, BetterCharacterPanelTooltip.MagicDefense, stats);

        if (BetterCharacterPanelState.IsCaster(stats.JobID))
        {
            SetRowText(spellSpeedBonus, stats.Speed.SpeedBonus.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
            SetRowText(spellGcd, Format("Feature.BetterCharacterPanel.Value.Seconds", stats.Speed.MainGcd.DisplayValue));
            SetTooltip(spellSpeedBonus, BetterCharacterPanelTooltip.Speed, stats);
            SetTooltip(spellGcd, BetterCharacterPanelTooltip.Speed, stats);
        }
        else
        {
            SetRowText(skillSpeedBonus, stats.Speed.SpeedBonus.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
            SetRowText(skillGcd, Format("Feature.BetterCharacterPanel.Value.Seconds", stats.Speed.MainGcd.DisplayValue));
            SetTooltip(skillSpeedBonus, BetterCharacterPanelTooltip.Speed, stats);
            SetTooltip(skillGcd, BetterCharacterPanelTooltip.Speed, stats);
        }

        SetRowText(tenacityMitigation, stats.TenacityMitigation.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetRowText(tenacityDamage, stats.TenacityDamage.DisplayValue.ToString("P1", CultureInfo.CurrentCulture));
        SetTooltip(tenacityMitigation, BetterCharacterPanelTooltip.Tenacity, stats);
        SetTooltip(tenacityDamage, BetterCharacterPanelTooltip.Tenacity, stats);
        SetRowText(manaPerTick, stats.Piety.DisplayValue.ToString("F0", CultureInfo.CurrentCulture));
        SetTooltip(manaPerTick, BetterCharacterPanelTooltip.Piety, stats);
        SetRowText(expectedDamage, OmniNumberFormatter.Format((long)Math.Round(stats.Expected.AverageDamage)));
        SetTooltip(expectedDamage, BetterCharacterPanelTooltip.ExpectedDamage, stats);
        SetRowText(expectedHeal, OmniNumberFormatter.Format((long)Math.Round(stats.Expected.AverageHeal)));
        SetTooltip(expectedHeal, BetterCharacterPanelTooltip.ExpectedHeal, stats);
    }

    public void Restore()
    {
        if (addon == null)
        {
            ClearState();
            return;
        }

        for (var index = injectedNodes.Count - 1; index >= 0; index--)
        {
            injectedNodes[index].Dispose();
        }

        foreach (var snapshot in textSnapshots.Values)
        {
            var node = (AtkTextNode*)snapshot.Address;
            node->TextColor = snapshot.Color;
            node->SetText(snapshot.Text);
        }

        foreach (var snapshot in nodeSnapshots.Values)
        {
            var node = (AtkResNode*)snapshot.Address;
            node->SetPositionFloat(snapshot.X, snapshot.Y);
            node->SetWidth(snapshot.Width);
            node->SetHeight(snapshot.Height);
            node->NodeFlags = snapshot.NodeFlags;
            node->ToggleVisibility(snapshot.Visible);
        }

        addon->UldManager.UpdateDrawNodeList();
        ClearState();
    }

    public void Dispose() => Restore();

    private StatRow? AddRow(
        AtkComponentNode* parent,
        string labelKey,
        BetterCharacterPanelTooltip? tooltip,
        bool hideOriginal = false,
        bool copyColor = false,
        bool expandCollision = true)
    {
        if (parent == null || parent->Component == null)
        {
            return null;
        }

        var collision = parent->Component->UldManager.RootNode;
        var valueTemplateNode = collision == null ? null : collision->PrevSiblingNode;
        var labelTemplateNode = valueTemplateNode == null ? null : valueTemplateNode->PrevSiblingNode;
        if (collision == null
            || valueTemplateNode == null
            || valueTemplateNode->GetNodeType() is not NodeType.Text
            || labelTemplateNode == null
            || labelTemplateNode->GetNodeType() is not NodeType.Text)
        {
            return null;
        }

        var valueTemplate = (AtkTextNode*)valueTemplateNode;
        var labelTemplate = (AtkTextNode*)labelTemplateNode;

        if (!hideOriginal)
        {
            CaptureNode((AtkResNode*)parent);
            parent->AtkResNode.SetHeight((ushort)(parent->AtkResNode.Height + RowHeight));
            if (expandCollision)
            {
                CaptureNode(collision);
                collision->SetHeight((ushort)(collision->Height + RowHeight));
            }
        }

        var rowY = parent->AtkResNode.Height - 24f;
        var label = CreateTextNode(labelTemplate, rowY, copyColor ? null : SecondaryTextColor);
        label.AttachNode(labelTemplate, NodePosition.AfterTarget);
        label.String = OmniLoc.Get(labelKey);
        var value = CreateTextNode(valueTemplate, rowY, copyColor ? null : SecondaryTextColor);
        value.AttachNode(labelTemplate, NodePosition.AfterTarget);
        injectedNodes.Add(label);
        injectedNodes.Add(value);
        CollisionNode? tooltipNode = null;
        if (tooltip is { } entry)
        {
            tooltipNode = new()
            {
                Position = new(collision->X, rowY),
                Size = new(parent->AtkResNode.Width, valueTemplate->AtkResNode.Height)
            };
            tooltipNode.SetTextTooltip(BetterCharacterPanelState.BuildTooltip(entry, default));
            tooltipNode.AttachNode(label.Node, NodePosition.AfterTarget);
            injectedNodes.Add(tooltipNode);
        }

        if (hideOriginal)
        {
            CaptureNode((AtkResNode*)labelTemplate);
            CaptureNode((AtkResNode*)valueTemplate);
            CaptureText(valueTemplate);
            labelTemplate->AtkResNode.ToggleVisibility(false);
            valueTemplate->TextColor.A = 0;
        }

        parent->Component->UldManager.UpdateDrawNodeList();
        return new(label, value, tooltipNode);
    }

    private TextNode CreateTextNode(AtkTextNode* template, float y, ByteColor? color)
    {
        var node = new TextNode();
        node.Node->AtkResNode.SetPositionFloat(template->AtkResNode.X, y);
        node.Node->AtkResNode.SetWidth(template->AtkResNode.Width);
        node.Node->AtkResNode.SetHeight(template->AtkResNode.Height);
        node.Node->SetFont(template->FontType);
        node.Node->SetAlignment(template->AlignmentType);
        node.Node->LineSpacing = template->LineSpacing;
        node.Node->TextColor = color ?? template->TextColor;
        node.Node->EdgeColor = template->EdgeColor;
        node.Node->BackgroundColor = template->BackgroundColor;
        node.Node->TextFlags = template->TextFlags;
        node.Node->AtkResNode.DrawFlags = template->AtkResNode.DrawFlags;
        node.Node->AtkResNode.NodeFlags = template->AtkResNode.NodeFlags;
        node.Node->FontSize = template->FontSize;
        return node;
    }

    private void AddNativeTooltip(AtkComponentNode* parent, BetterCharacterPanelTooltip tooltip)
    {
        if (parent == null || parent->Component == null || parent->Component->UldManager.RootNode == null)
        {
            return;
        }

        var source = parent->Component->UldManager.RootNode;
        var collision = new CollisionNode
        {
            Position = new(source->X, source->Y),
            Size = new(source->Width, source->Height)
        };
        collision.SetTextTooltip(BetterCharacterPanelState.BuildTooltip(tooltip, default));
        collision.AttachNode(source, NodePosition.AfterTarget);
        injectedNodes.Add(collision);
    }

    private void UpdateJobVisibility(int jobID)
    {
        if (attributes == null)
        {
            return;
        }

        var mindNode = attributes->ChildNode;
        var intelligenceNode = Previous(mindNode);
        var dexterityNode = Previous(Previous(intelligenceNode));
        var strengthNode = Previous(dexterityNode);
        if (BetterCharacterPanelState.IsCrafter(jobID) || BetterCharacterPanelState.IsGatherer(jobID))
        {
            SetVisible(mindNode, false);
            SetVisible(intelligenceNode, false);
            SetVisible(dexterityNode, false);
            SetVisible(strengthNode, false);
            SetVisible(offensive, false);
            SetVisible(defensive, false);
            SetVisible(physicalProperties, false);
            SetVisible(gear, false);
            SetRowParentVisible(expectedDamage, false);
            SetRowParentVisible(expectedHeal, false);
            SetVisible(crafting, BetterCharacterPanelState.IsCrafter(jobID));
            SetVisible(gathering, BetterCharacterPanelState.IsGatherer(jobID));
            return;
        }

        SetVisible(offensive, true);
        SetVisible(defensive, true);
        SetVisible(physicalProperties, true);
        SetVisible(gear, true);
        SetRowParentVisible(expectedDamage, true);
        SetRowParentVisible(expectedHeal, BetterCharacterPanelState.IsCaster(jobID));
        SetVisible(crafting, false);
        SetVisible(gathering, false);
        if (BetterCharacterPanelState.IsCaster(jobID))
        {
            SetVisible(skillSpeed, false);
            SetVisible(spellSpeed, true);
            SetVisible(mindNode, BetterCharacterPanelState.UsesMind(jobID));
            SetVisible(intelligenceNode, !BetterCharacterPanelState.UsesMind(jobID));
            SetVisible(dexterityNode, false);
            SetVisible(strengthNode, false);
            SetVisible(piety, BetterCharacterPanelState.UsesMind(jobID));
            SetVisible(tenacity, false);
            return;
        }

        SetVisible(skillSpeed, true);
        SetVisible(spellSpeed, false);
        SetVisible(mindNode, false);
        SetVisible(intelligenceNode, false);
        SetVisible(dexterityNode, BetterCharacterPanelState.UsesDexterity(jobID));
        SetVisible(strengthNode, !BetterCharacterPanelState.UsesDexterity(jobID));
        SetVisible(piety, false);
        SetVisible(tenacity, BetterCharacterPanelState.UsesTenacity(jobID));
    }

    private bool HasRequiredRows() =>
        directHitChance is not null
        && directHitDamage is not null
        && determinationBonus is not null
        && criticalChance is not null
        && criticalDamage is not null
        && criticalExpected is not null
        && physicalMitigation is not null
        && magicMitigation is not null
        && skillSpeedBonus is not null
        && skillGcd is not null
        && spellSpeedBonus is not null
        && spellGcd is not null
        && tenacityMitigation is not null
        && tenacityDamage is not null
        && manaPerTick is not null
        && expectedDamage is not null
        && expectedHeal is not null;

    private void CaptureNode(AtkResNode* node)
    {
        if (node == null || nodeSnapshots.ContainsKey((nint)node))
        {
            return;
        }

        nodeSnapshots[(nint)node] = new(
            (nint)node,
            node->X,
            node->Y,
            node->Width,
            node->Height,
            node->IsVisible(),
            node->NodeFlags);
    }

    private void CaptureText(AtkTextNode* node)
    {
        if (node == null || textSnapshots.ContainsKey((nint)node))
        {
            return;
        }

        textSnapshots[(nint)node] = new((nint)node, node->NodeText.ToString(), node->TextColor);
    }

    private void SetPosition(AtkResNode* node, float x, float y)
    {
        if (node == null)
        {
            return;
        }

        CaptureNode(node);
        node->SetPositionFloat(x, y);
    }

    private void MoveY(AtkResNode* node, float amount)
    {
        if (node != null)
        {
            SetPosition(node, node->X, node->Y + amount);
        }
    }

    private void SetVisible(AtkResNode* node, bool visible)
    {
        if (node == null)
        {
            return;
        }

        CaptureNode(node);
        node->ToggleVisibility(visible);
    }

    private void SetSpeedHeaderText(AtkResNode* speedNode, string text)
    {
        var header = Previous(Previous(speedNode));
        var textNode = header == null || header->ChildNode == null
            ? null
            : (AtkTextNode*)header->ChildNode->PrevSiblingNode;
        if (textNode == null)
        {
            return;
        }

        CaptureText(textNode);
        textNode->SetText(text);
    }

    private static void SetRowText(StatRow? row, string text)
    {
        if (row is not null)
        {
            row.Value.String = text;
        }
    }

    private static void SetTooltip(
        StatRow? row,
        BetterCharacterPanelTooltip tooltip,
        in BetterCharacterPanelStats stats)
    {
        if (row is not null)
        {
            if (row.Tooltip is not null)
            {
                row.Tooltip.SetTextTooltip(BetterCharacterPanelState.BuildTooltip(tooltip, stats));
            }
        }
    }

    private void SetRowParentVisible(StatRow? row, bool visible)
    {
        if (row is not null)
        {
            SetVisible(row.Value.Node->AtkResNode.ParentNode, visible);
        }
    }

    private void ClearState()
    {
        injectedNodes.Clear();
        nodeSnapshots.Clear();
        textSnapshots.Clear();
        addon = null;
        attributes = null;
        offensive = null;
        defensive = null;
        physicalProperties = null;
        gear = null;
        piety = null;
        tenacity = null;
        spellSpeed = null;
        skillSpeed = null;
        crafting = null;
        gathering = null;
        directHitChance = null;
        directHitDamage = null;
        determinationBonus = null;
        criticalChance = null;
        criticalDamage = null;
        criticalExpected = null;
        physicalMitigation = null;
        magicMitigation = null;
        skillSpeedBonus = null;
        skillGcd = null;
        spellSpeedBonus = null;
        spellGcd = null;
        tenacityMitigation = null;
        tenacityDamage = null;
        manaPerTick = null;
        expectedDamage = null;
        expectedHeal = null;
        craftingPoints = null;
        gatheringPoints = null;
        lastStats = null;
        layoutApplied = false;
    }

    private static AtkResNode* Previous(AtkResNode* node, int count = 1)
    {
        while (node != null && count-- > 0)
        {
            node = node->PrevSiblingNode;
        }

        return node;
    }

    private static bool IsVisible(AtkUnitBase* unitBase) =>
        unitBase != null
        && unitBase->IsVisible
        && unitBase->RootNode != null
        && unitBase->RootNode->IsVisible()
        && (unitBase->VisibilityFlags & 5) == 0;

    private static string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, OmniLoc.Get(key), values);

    private sealed record StatRow(TextNode Label, TextNode Value, CollisionNode? Tooltip);

    private readonly record struct NodeSnapshot(
        nint Address,
        float X,
        float Y,
        ushort Width,
        ushort Height,
        bool Visible,
        NodeFlags NodeFlags);

    private readonly record struct TextSnapshot(nint Address, string Text, ByteColor Color);
}
