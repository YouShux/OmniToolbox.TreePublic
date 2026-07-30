using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Dalamud.Services.Game.Object.Abstractions.ObjectKinds;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Models;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed unsafe class DisplayIdInformationCombat : IDisposable
{
    private static readonly CompSig ActionEffectSig = new(
        "40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 70 4C 8B BD");

    private const uint SelfCastNodeId = 32620;
    private const uint TargetCastNodeId = 32621;

    private readonly DisplayIdInformationConfig config;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly DisplayIdInformationCache flyTextCache = new();
    private readonly List<uint> currentCastActionIds = [];
    private readonly List<uint> previousCastActionIds = [];
    private readonly Dictionary<string, List<uint>> castActionIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> castTextByName = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> actionNameCache = [];
    private Dictionary<uint, List<IdName>>? actionIconMap;
    private Dictionary<uint, List<IdName>>? statusIconMap;
    private ExcelSheet<LuminaAction>? actionSheet;
    private ExcelSheet<LuminaStatus>? statusSheet;
    private Hook<ActionEffectDelegate>? actionEffectHook;
    private bool actionEffectHookUnavailable;
    private bool castNodesVisible;

    private delegate void ActionEffectDelegate(
        uint sourceId,
        nint sourceCharacter,
        nint position,
        nint effectHeader,
        nint effectArray,
        nint effectTail);

    public DisplayIdInformationCombat(DisplayIdInformationConfig config)
    {
        this.config = config;
        addonEvents = new(DalamudServices.AddonLifecycle);

        try
        {
            lifetime.Add(ShutdownActionEffectHook);
            DService.Instance().FlyText.FlyTextCreated += OnFlyTextCreated;
            lifetime.Add(() => DService.Instance().FlyText.FlyTextCreated -= OnFlyTextCreated);
            lifetime.Add(RemoveCastNodes);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PreDraw, "_CastBar", OnCastBarAddon);
            addonEvents.Register(AddonEvent.PreDraw, "_TargetInfoCastBar", OnCastBarAddon);
            addonEvents.Register(AddonEvent.PreDraw, "_TargetInfoCastBarMainTarget", OnCastBarAddon);
            UpdateActionEffectHook();
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    public void Update()
    {
        UpdateActionEffectHook();
        if (config.DisplayActionId &&
            config.DisplayCastBarActionId &&
            DService.Instance().ClientState.IsLoggedIn &&
            !DService.Instance().Condition[ConditionFlag.BetweenAreas])
        {
            RefreshCastActionIds();
            return;
        }

        ClearCastActionIds();
        if (castNodesVisible)
        {
            HideCastNodes();
        }
    }

    public void Dispose()
    {
        try
        {
            lifetime.Dispose();
        }
        finally
        {
            flyTextCache.Clear();
            currentCastActionIds.Clear();
            previousCastActionIds.Clear();
            castActionIdsByName.Clear();
            castTextByName.Clear();
            actionNameCache.Clear();
            actionIconMap?.Clear();
            statusIconMap?.Clear();
        }
    }

    private void UpdateActionEffectHook()
    {
        if (config.DisplayActionId && config.DisplayFlyTextDamageActionId)
        {
            EnsureActionEffectHook();
        }
        else
        {
            ShutdownActionEffectHook();
        }
    }

    private void EnsureActionEffectHook()
    {
        if (actionEffectHook is not null || actionEffectHookUnavailable)
        {
            return;
        }

        try
        {
            actionEffectHook = ActionEffectSig.GetHook<ActionEffectDelegate>(OnActionEffect);
            actionEffectHook.Enable();
        }
        catch (Exception ex)
        {
            actionEffectHookUnavailable = true;
            actionEffectHook?.Dispose();
            actionEffectHook = null;
            DalamudServices.PluginLog.Warning(ex, "Display ID ActionEffect hook initialization failed.");
        }
    }

    private void ShutdownActionEffectHook()
    {
        actionEffectHook?.Dispose();
        actionEffectHook = null;
    }

    private void OnActionEffect(
        uint sourceId,
        nint sourceCharacter,
        nint position,
        nint effectHeader,
        nint effectArray,
        nint effectTail)
    {
        try
        {
            CaptureActionEffects(effectHeader, effectArray);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Debug(ex, "Display ID ActionEffect capture failed.");
        }

        actionEffectHook!.Original(sourceId, sourceCharacter, position, effectHeader, effectArray, effectTail);
    }

    private void CaptureActionEffects(nint effectHeader, nint effectArray)
    {
        if (!config.DisplayActionId ||
            !config.DisplayFlyTextDamageActionId ||
            effectHeader == nint.Zero ||
            effectArray == nint.Zero)
        {
            return;
        }

        var header = (ActionEffectHeader*)effectHeader;
        var targetCount = Math.Min((int)header->TargetCount, 32);
        if (header->ActionId == 0 || targetCount == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var entries = (ActionEffectEntry*)effectArray;
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
        {
            for (var effectIndex = 0; effectIndex < 8; effectIndex++)
            {
                var entry = entries[targetIndex * 8 + effectIndex];
                if (entry.Type != 0 && entry.Value != 0)
                {
                    flyTextCache.RememberAction(header->ActionId, entry.Value, now);
                }
            }
        }
    }

    private void OnFlyTextCreated(
        ref FlyTextKind kind,
        ref int value1,
        ref int value2,
        ref SeString text1,
        ref SeString text2,
        ref uint color,
        ref uint icon,
        ref uint damageTypeIcon,
        ref float yOffset,
        ref bool handled)
    {
        if (config.DisplayStatusId &&
            config.DisplayStatusIdAppendToName &&
            DisplayIdInformationFormatter.IsStatusFlyTextKind(kind))
        {
            AppendStatusId(ref text1, icon, kind);
            AppendStatusId(ref text2, icon, kind);
        }

        if (!config.DisplayActionId ||
            !config.DisplayFlyTextDamageActionId ||
            !DisplayIdInformationFormatter.IsDamageFlyTextKind(kind))
        {
            return;
        }

        if (TryResolveDamageActionId(value1, text1, text2, out var actionId) ||
            value2 > 0 && ActionExists((uint)value2) && (actionId = (uint)value2) != 0)
        {
            AppendDamageActionId(ref text1, ref text2, actionId);
            return;
        }

        AppendActionId(ref text1, value2, icon);
        AppendActionId(ref text2, value2, icon);
    }

    private bool TryResolveDamageActionId(int value1, SeString text1, SeString text2, out uint actionId)
    {
        actionId = 0;
        var value = value1 > 0 ? (uint)value1 : 0;
        if (value == 0 && !TryExtractFirstUInt(text2.TextValue, out value))
        {
            TryExtractFirstUInt(text1.TextValue, out value);
        }

        return value != 0 &&
               flyTextCache.TryTakeAction(value, Environment.TickCount64, out actionId) &&
               ActionExists(actionId);
    }

    private static void AppendDamageActionId(ref SeString text1, ref SeString text2, uint actionId)
    {
        var suffix = $"({actionId})";
        if (text1.TextValue.Contains(suffix, StringComparison.Ordinal) ||
            text2.TextValue.Contains(suffix, StringComparison.Ordinal) ||
            AppendSuffix(ref text2, suffix))
        {
            return;
        }

        AppendSuffix(ref text1, suffix);
    }

    private static bool AppendSuffix(ref SeString text, string suffix)
    {
        if (string.IsNullOrWhiteSpace(text.TextValue))
        {
            return false;
        }

        text = new SeStringBuilder().Append(text.TextValue + suffix).Build();
        return true;
    }

    private void AppendStatusId(ref SeString text, uint iconId, FlyTextKind kind)
    {
        var raw = text.TextValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var name = TrimFlyTextSign(raw.TrimStart());
        if (string.IsNullOrWhiteSpace(name) || !TryResolveStatusId(iconId, name, kind, out var statusId, out var matchedName))
        {
            return;
        }

        var modified = DisplayIdInformationFormatter.InsertFlyTextId(raw, matchedName, statusId);
        if (!string.Equals(raw, modified, StringComparison.Ordinal))
        {
            text = new SeStringBuilder().Append(modified).Build();
        }
    }

    private bool TryResolveStatusId(
        uint iconId,
        string name,
        FlyTextKind kind,
        out uint statusId,
        out string matchedName)
    {
        var now = Environment.TickCount64;
        if (kind is FlyTextKind.BuffFading or FlyTextKind.DebuffFading &&
            flyTextCache.TryGetStatus(iconId, name, now, out statusId, out matchedName))
        {
            return true;
        }

        if (!TryResolveActiveStatus(iconId, name, out statusId, out matchedName) &&
            !TryResolveStatusFromSheet(iconId, name, out statusId, out matchedName))
        {
            return false;
        }

        flyTextCache.RememberStatus(iconId, statusId, matchedName, now);
        return true;
    }

    private bool TryResolveActiveStatus(uint iconId, string name, out uint statusId, out string matchedName)
    {
        statusId = 0;
        matchedName = string.Empty;
        var bestLength = -1;
        foreach (var gameObject in DService.Instance().ObjectTable)
        {
            if (gameObject is not IBattleChara battleChara)
            {
                continue;
            }

            var statuses = battleChara.ToStruct()->GetStatusManager();
            if (statuses == null)
            {
                continue;
            }

            for (var index = 0; index < statuses->NumValidStatuses; index++)
            {
                var status = statuses->Status[index];
                if (status.StatusId == 0 || !GetStatusSheet().TryGetRow(status.StatusId, out var row))
                {
                    continue;
                }

                if (iconId < row.Icon || iconId > row.Icon + status.Param)
                {
                    continue;
                }

                var rowName = row.Name.ToString();
                if (rowName.Length <= bestLength || !NameMatches(name, rowName))
                {
                    continue;
                }

                statusId = row.RowId;
                matchedName = rowName;
                bestLength = rowName.Length;
            }
        }

        return statusId != 0;
    }

    private bool TryResolveStatusFromSheet(uint iconId, string name, out uint statusId, out string matchedName)
    {
        statusId = 0;
        matchedName = string.Empty;
        if (!GetStatusIconMap().TryGetValue(iconId, out var statuses))
        {
            return false;
        }

        return TrySelectName(statuses, name, out statusId, out matchedName);
    }

    private void AppendActionId(ref SeString text, int value2, uint iconId)
    {
        var raw = text.TextValue;
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains('(') && raw.Contains(')'))
        {
            return;
        }

        var name = TrimFlyTextSign(raw.TrimStart());
        if (string.IsNullOrWhiteSpace(name) || IsDigits(name) ||
            !TryResolveActionId(value2, iconId, name, out var actionId, out var matchedName))
        {
            return;
        }

        var modified = DisplayIdInformationFormatter.InsertFlyTextId(raw, matchedName, actionId);
        if (!string.Equals(raw, modified, StringComparison.Ordinal))
        {
            text = new SeStringBuilder().Append(modified).Build();
        }
    }

    private bool TryResolveActionId(int value2, uint iconId, string name, out uint actionId, out string matchedName)
    {
        actionId = 0;
        matchedName = string.Empty;
        if (value2 > 0 && GetActionSheet().TryGetRow((uint)value2, out var action))
        {
            var actionName = action.Name.ToString();
            if (NameMatches(name, actionName))
            {
                actionId = action.RowId;
                matchedName = actionName;
                return true;
            }
        }

        return iconId != 0 &&
               GetActionIconMap().TryGetValue(iconId, out var actions) &&
               TrySelectName(actions, name, out actionId, out matchedName);
    }

    private void RefreshCastActionIds()
    {
        currentCastActionIds.Clear();
        foreach (var gameObject in DService.Instance().ObjectTable)
        {
            if (gameObject is not IBattleChara { IsCasting: true } battleChara ||
                battleChara.CastActionID == 0 ||
                currentCastActionIds.Contains(battleChara.CastActionID))
            {
                continue;
            }

            currentCastActionIds.Add(battleChara.CastActionID);
        }

        currentCastActionIds.Sort();
        if (ListsEqual(currentCastActionIds, previousCastActionIds))
        {
            return;
        }

        previousCastActionIds.Clear();
        previousCastActionIds.AddRange(currentCastActionIds);
        castActionIdsByName.Clear();
        castTextByName.Clear();
        for (var index = 0; index < currentCastActionIds.Count; index++)
        {
            var actionId = currentCastActionIds[index];
            if (!TryGetActionName(actionId, out var actionName))
            {
                continue;
            }

            if (!castActionIdsByName.TryGetValue(actionName, out var ids))
            {
                ids = [];
                castActionIdsByName[actionName] = ids;
            }

            ids.Add(actionId);
        }

        foreach (var (name, ids) in castActionIdsByName)
        {
            castTextByName[name] = DisplayIdInformationFormatter.FormatSortedCastIds(ids);
        }
    }

    private void ClearCastActionIds()
    {
        if (previousCastActionIds.Count == 0 && castActionIdsByName.Count == 0)
        {
            return;
        }

        currentCastActionIds.Clear();
        previousCastActionIds.Clear();
        castActionIdsByName.Clear();
        castTextByName.Clear();
    }

    private void OnCastBarAddon(AddonEvent _, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        var nodeId = args.AddonName == "_CastBar" ? SelfCastNodeId : TargetCastNodeId;
        if (!config.DisplayActionId || !config.DisplayCastBarActionId)
        {
            ToggleNode(addon, nodeId, false);
            return;
        }

        var battleChara = args.AddonName == "_CastBar"
            ? DService.Instance().ObjectTable.LocalPlayer
            : GetCurrentTarget();
        if (battleChara is not { IsCasting: true } ||
            battleChara.CastActionID == 0 ||
            !TryGetActionName(battleChara.CastActionID, out var actionName) ||
            !TryFindTextNode(addon, actionName, out var nameNode))
        {
            ToggleNode(addon, nodeId, false);
            return;
        }

        var idText = castTextByName.TryGetValue(actionName, out var mapped)
            ? mapped
            : $"({battleChara.CastActionID})";
        UpdateCastNode(addon, nameNode, nodeId, idText, args.AddonName == "_CastBar");
        castNodesVisible = true;
    }

    private bool TryGetActionName(uint actionId, out string name)
    {
        if (actionNameCache.TryGetValue(actionId, out name!))
        {
            return true;
        }

        if (!GetActionSheet().TryGetRow(actionId, out var action))
        {
            name = string.Empty;
            return false;
        }

        name = action.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        actionNameCache[actionId] = name;
        return true;
    }

    private static bool TryFindTextNode(AtkUnitBase* addon, string text, out AtkTextNode* result)
    {
        result = null;
        if (addon == null || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var bestLength = int.MaxValue;
        var bestExact = false;
        for (var index = 0; index < addon->UldManager.NodeListCount; index++)
        {
            var node = addon->UldManager.NodeList[index];
            if (node == null || !node->IsVisible() || node->Type != NodeType.Text)
            {
                continue;
            }

            var textNode = (AtkTextNode*)node;
            var value = textNode->NodeText.ToString();
            if (!value.Contains(text, StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = value.Trim();
            var exact = string.Equals(trimmed, text, StringComparison.Ordinal);
            if (result == null || exact && !bestExact || exact == bestExact && trimmed.Length < bestLength)
            {
                result = textNode;
                bestExact = exact;
                bestLength = trimmed.Length;
            }
        }

        return result != null;
    }

    private static void UpdateCastNode(
        AtkUnitBase* addon,
        AtkTextNode* nameNode,
        uint nodeId,
        string idText,
        bool self)
    {
        var idNode = EnsureCastNode(addon, nameNode, nodeId);
        if (idNode == null)
        {
            return;
        }

        idNode->AtkResNode.ToggleVisibility(true);
        idNode->TextFlags = (nameNode->TextFlags | TextFlags.AutoAdjustNodeSize) & ~TextFlags.MultiLine;
        idNode->FontSize = (byte)Math.Max(8, self ? nameNode->FontSize : nameNode->FontSize - 4);
        idNode->AtkResNode.Color = nameNode->AtkResNode.Color;
        idNode->TextColor = nameNode->TextColor;
        idNode->EdgeColor = nameNode->EdgeColor;

        if (self)
        {
            idNode->SetText(new SeStringBuilder().Append(nameNode->NodeText.ToString()).Build().EncodeWithNullTerminator());
            idNode->ResizeNodeForCurrentText();
            var nameWidth = Math.Max(10f, idNode->AtkResNode.Width);
            idNode->SetText(new SeStringBuilder().Append(idText).Build().EncodeWithNullTerminator());
            idNode->ResizeNodeForCurrentText();
            var targetX = nameNode->AtkResNode.X + nameWidth;
            var maximumX = nameNode->AtkResNode.X + Math.Max(0f, nameNode->AtkResNode.Width - idNode->AtkResNode.Width - 2f);
            idNode->AtkResNode.SetXFloat(Math.Min(targetX, maximumX));
            idNode->AtkResNode.SetYFloat(nameNode->AtkResNode.Y + 2f);
            return;
        }

        idNode->SetText(new SeStringBuilder().Append(idText).Build().EncodeWithNullTerminator());
        idNode->ResizeNodeForCurrentText();
        idNode->AtkResNode.SetXFloat(Math.Max(
            nameNode->AtkResNode.X,
            nameNode->AtkResNode.X + nameNode->AtkResNode.Width - idNode->AtkResNode.Width));
        idNode->AtkResNode.SetYFloat(Math.Max(
            nameNode->AtkResNode.Y + 2f,
            nameNode->AtkResNode.Y + Math.Max(8f, nameNode->AtkResNode.Height) - 4f));
    }

    private static AtkTextNode* EnsureCastNode(AtkUnitBase* addon, AtkTextNode* anchor, uint nodeId)
    {
        var existing = FindTextNodeById(addon, nodeId);
        if (existing != null)
        {
            return existing;
        }

        var created = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        created->AtkResNode.Type = NodeType.Text;
        created->AtkResNode.NodeId = nodeId;
        created->AtkResNode.NodeFlags = NodeFlags.AnchorLeft | NodeFlags.AnchorTop;
        created->AtkResNode.Color = anchor->AtkResNode.Color;
        created->TextColor = anchor->TextColor;
        created->EdgeColor = anchor->EdgeColor;
        created->FontSize = anchor->FontSize;
        created->TextFlags = (anchor->TextFlags | TextFlags.AutoAdjustNodeSize) & ~TextFlags.MultiLine;
        created->AtkResNode.ParentNode = anchor->AtkResNode.ParentNode;
        created->AtkResNode.PrevSiblingNode = &anchor->AtkResNode;
        created->AtkResNode.NextSiblingNode = anchor->AtkResNode.NextSiblingNode;
        anchor->AtkResNode.NextSiblingNode = &created->AtkResNode;
        if (created->AtkResNode.NextSiblingNode != null)
        {
            created->AtkResNode.NextSiblingNode->PrevSiblingNode = &created->AtkResNode;
        }

        addon->UldManager.UpdateDrawNodeList();
        return created;
    }

    private void RemoveCastNodes()
    {
        RemoveCastNode(AddonHelper.GetByName("_CastBar"), SelfCastNodeId);
        RemoveCastNode(AddonHelper.GetByName("_TargetInfoCastBar"), TargetCastNodeId);
        RemoveCastNode(AddonHelper.GetByName("_TargetInfoCastBarMainTarget"), TargetCastNodeId);
        castNodesVisible = false;
    }

    private void HideCastNodes()
    {
        ToggleNode(AddonHelper.GetByName("_CastBar"), SelfCastNodeId, false);
        ToggleNode(AddonHelper.GetByName("_TargetInfoCastBar"), TargetCastNodeId, false);
        ToggleNode(AddonHelper.GetByName("_TargetInfoCastBarMainTarget"), TargetCastNodeId, false);
        castNodesVisible = false;
    }

    private static void RemoveCastNode(AtkUnitBase* addon, uint nodeId)
    {
        var node = FindTextNodeById(addon, nodeId);
        if (node == null)
        {
            return;
        }

        var resourceNode = &node->AtkResNode;
        if (resourceNode->ParentNode != null && resourceNode->ParentNode->ChildNode == resourceNode)
        {
            resourceNode->ParentNode->ChildNode = resourceNode->PrevSiblingNode;
        }

        if (resourceNode->PrevSiblingNode != null)
        {
            resourceNode->PrevSiblingNode->NextSiblingNode = resourceNode->NextSiblingNode;
        }

        if (resourceNode->NextSiblingNode != null)
        {
            resourceNode->NextSiblingNode->PrevSiblingNode = resourceNode->PrevSiblingNode;
        }

        addon->UldManager.UpdateDrawNodeList();
        resourceNode->Destroy(true);
    }

    private static void ToggleNode(AtkUnitBase* addon, uint nodeId, bool visible)
    {
        var node = FindTextNodeById(addon, nodeId);
        if (node != null)
        {
            node->AtkResNode.ToggleVisibility(visible);
        }
    }

    private static AtkTextNode* FindTextNodeById(AtkUnitBase* addon, uint nodeId)
    {
        if (addon == null)
        {
            return null;
        }

        for (var index = 0; index < addon->UldManager.NodeListCount; index++)
        {
            var node = addon->UldManager.NodeList[index];
            if (node != null && node->NodeId == nodeId && node->Type == NodeType.Text)
            {
                return (AtkTextNode*)node;
            }
        }

        return null;
    }

    private static IBattleChara? GetCurrentTarget()
    {
        var target = TargetSystem.Instance()->Target;
        return target == null
            ? null
            : DService.Instance().ObjectTable.CreateObjectReference((nint)target) as IBattleChara;
    }

    private ExcelSheet<LuminaAction> GetActionSheet() =>
        actionSheet ??= DService.Instance().Data.GetExcelSheet<LuminaAction>();

    private ExcelSheet<LuminaStatus> GetStatusSheet() =>
        statusSheet ??= DService.Instance().Data.GetExcelSheet<LuminaStatus>();

    private bool ActionExists(uint actionId) => GetActionSheet().HasRow(actionId);

    private Dictionary<uint, List<IdName>> GetActionIconMap()
    {
        if (actionIconMap is not null)
        {
            return actionIconMap;
        }

        actionIconMap = [];
        foreach (var action in GetActionSheet())
        {
            var name = action.Name.ToString();
            if (action.RowId == 0 || action.Icon == 0 || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!actionIconMap.TryGetValue(action.Icon, out var actions))
            {
                actions = [];
                actionIconMap[action.Icon] = actions;
            }

            actions.Add(new(action.RowId, name));
        }

        return actionIconMap;
    }

    private Dictionary<uint, List<IdName>> GetStatusIconMap()
    {
        if (statusIconMap is not null)
        {
            return statusIconMap;
        }

        statusIconMap = [];
        foreach (var status in GetStatusSheet())
        {
            var name = status.Name.ToString();
            if (status.RowId == 0 || status.Icon == 0 || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!statusIconMap.TryGetValue(status.Icon, out var statuses))
            {
                statuses = [];
                statusIconMap[status.Icon] = statuses;
            }

            statuses.Add(new(status.RowId, name));
        }

        return statusIconMap;
    }

    private static bool TrySelectName(IReadOnlyList<IdName> values, string text, out uint id, out string name)
    {
        id = 0;
        name = string.Empty;
        var bestLength = -1;
        var bestIndex = int.MaxValue;
        for (var index = 0; index < values.Count; index++)
        {
            var matchIndex = text.IndexOf(values[index].Name, StringComparison.Ordinal);
            if (matchIndex < 0 ||
                matchIndex > bestIndex ||
                matchIndex == bestIndex && values[index].Name.Length <= bestLength)
            {
                continue;
            }

            id = values[index].Id;
            name = values[index].Name;
            bestLength = name.Length;
            bestIndex = matchIndex;
        }

        return id != 0;
    }

    private static bool NameMatches(string text, string name) =>
        !string.IsNullOrWhiteSpace(name) && text.Contains(name, StringComparison.Ordinal);

    private static string TrimFlyTextSign(string text) =>
        text.Length > 0 && text[0] is '+' or '＋' or '-' or '－' or '–' or '—'
            ? text[1..].TrimStart()
            : text;

    private static bool IsDigits(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    private static bool TryExtractFirstUInt(string text, out uint value)
    {
        ulong parsed = 0;
        var started = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is >= '0' and <= '9')
            {
                started = true;
                parsed = parsed * 10 + (uint)(text[index] - '0');
                if (parsed > uint.MaxValue)
                {
                    value = 0;
                    return false;
                }

                continue;
            }

            if (started && text[index] != ',')
            {
                break;
            }
        }

        value = (uint)parsed;
        return started && value != 0;
    }

    private static bool ListsEqual(IReadOnlyList<uint> left, IReadOnlyList<uint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct ActionEffectHeader
    {
        [System.Runtime.InteropServices.FieldOffset(8)]
        public uint ActionId;

        [System.Runtime.InteropServices.FieldOffset(32)]
        public byte TargetCount;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Size = 8)]
    private struct ActionEffectEntry
    {
        public byte Type;
        public byte Param1;
        public byte Param2;
        public byte Param3;
        public uint Value;
    }

    private readonly record struct IdName(uint Id, string Name);
}
