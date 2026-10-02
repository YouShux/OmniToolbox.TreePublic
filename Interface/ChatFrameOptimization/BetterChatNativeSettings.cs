using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using OmenTools.Extensions;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class BetterChatNativeSettings : IDisposable
{
    private readonly Action<int> openExtendedFilter;
    private readonly List<TextButtonNode> buttons = [];
    private readonly List<TextNode> numbers = [];
    private readonly List<int> customTabIndices = [];
    private AtkUnitBase* addon;

    public BetterChatNativeSettings(Action<int> openExtendedFilter)
    {
        this.openExtendedFilter = openExtendedFilter;
    }

    public void Update(AtkUnitBase* currentAddon, IReadOnlyList<BetterChatTabConfig> tabs)
    {
        if (currentAddon is null || !currentAddon->IsReady)
        {
            return;
        }

        if (addon != currentAddon)
        {
            Clear();
            addon = currentAddon;
        }

        customTabIndices.Clear();
        for (var index = 0; index < tabs.Count; index++)
        {
            if (tabs[index].NativeTabIndex < 0)
                customTabIndices.Add(index);
        }

        if (customTabIndices.Count == 0 || !TryFindFilterAnchor(currentAddon, out var anchorPosition, out var anchorSize))
        {
            foreach (var button in buttons)
                button.IsVisible = false;
            foreach (var number in numbers)
                number.IsVisible = false;
            return;
        }

        var changed = false;
        while (buttons.Count < customTabIndices.Count)
        {
            var slot = buttons.Count;
            var number = new TextNode
            {
                AlignmentType = AlignmentType.Center,
                TextColor = KnownColor.White.ToVector4(),
            };
            number.AttachNode(currentAddon);
            numbers.Add(number);

            var button = new TextButtonNode
            {
                OnClick = () =>
                {
                    if (slot < customTabIndices.Count)
                        openExtendedFilter(customTabIndices[slot]);
                }
            };
            button.SetTextTooltip(OmniLoc.Get("Feature.ChatFrameOptimization.BetterChat.ExtendedFilters"));
            button.AttachNode(currentAddon);
            buttons.Add(button);
            changed = true;
        }

        var gap = MathF.Max(2f, anchorSize.Y * 0.08f);
        var numberWidth = MathF.Max(anchorSize.Y, anchorSize.Y * 1.25f);
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            button.IsVisible = index < customTabIndices.Count;
            numbers[index].IsVisible = button.IsVisible;
            if (!button.IsVisible)
                continue;

            var tab = tabs[customTabIndices[index]];
            var rowPosition = anchorPosition + new Vector2(0f, anchorSize.Y + gap + index * (anchorSize.Y + gap));
            numbers[index].String = (index + 5).ToString();
            numbers[index].Position = new(rowPosition.X - numberWidth - gap, rowPosition.Y);
            numbers[index].Size = new(numberWidth, anchorSize.Y);
            button.String = tab.Name;
            button.Position = rowPosition;
            button.Size = anchorSize;
        }

        if (changed)
            NativeNodeDetach.UpdateNodeLists(currentAddon);
    }

    public void Clear()
    {
        var currentAddon = addon;
        foreach (var button in buttons)
            NativeNodeDetach.DetachAndDestroyComponent(button);
        foreach (var number in numbers)
            NativeNodeDetach.DetachAndDestroy(number);
        buttons.Clear();
        numbers.Clear();
        customTabIndices.Clear();
        addon = null;
        if (currentAddon is not null)
            NativeNodeDetach.UpdateNodeLists(currentAddon);
    }

    public void Dispose() => Clear();

    private static bool TryFindFilterAnchor(AtkUnitBase* addon, out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;
        if (addon->RootNode is null)
            return false;

        var rootPosition = addon->RootNode->GetPosition();
        var rootScale = addon->RootNode->GetScale();
        if (rootScale.X <= 0f || rootScale.Y <= 0f)
            return false;

        var candidates = new List<FilterButtonCandidate>();
        var visited = new HashSet<nint>();
        for (var index = 0; index < addon->UldManager.NodeListCount; index++)
            CollectCandidates(addon->UldManager.NodeList[index], rootPosition, rootScale, visited, candidates);

        CollectCandidates(addon->RootNode, rootPosition, rootScale, visited, candidates);

        candidates.Sort(static (left, right) =>
        {
            var result = left.Position.Y.CompareTo(right.Position.Y);
            return result != 0 ? result : left.Position.X.CompareTo(right.Position.X);
        });

        List<FilterButtonCandidate>? best = null;
        for (var start = 0; start < candidates.Count; start++)
        {
            var current = new List<FilterButtonCandidate> { candidates[start] };
            for (var next = start + 1; next < candidates.Count; next++)
            {
                var previous = current[^1];
                var candidate = candidates[next];
                var sameColumn = MathF.Abs(candidate.Position.X - previous.Position.X) <= 16f;
                var sameWidth = MathF.Abs(candidate.Size.X - previous.Size.X) <= 20f;
                var gap = candidate.Position.Y - previous.Position.Y;
                if (!sameColumn || !sameWidth || gap < previous.Size.Y * 0.4f || gap > previous.Size.Y * 2.5f + 16f)
                    break;
                current.Add(candidate);
            }

            if (current.Count >= 4 &&
                (best is null || current.Count > best.Count ||
                 current.Count == best.Count && current[^1].Position.Y > best[^1].Position.Y))
            {
                best = current;
            }
        }

        if (best is null)
            return false;

        // The first four entries are the native ConfigLog filter buttons. Any
        // custom buttons appended by this class must not move the anchor.
        var anchor = best[Math.Min(3, best.Count - 1)];
        position = anchor.Position;
        size = anchor.Size;
        return true;
    }

    private static void CollectCandidates(
        AtkResNode* node,
        Vector2 rootPosition,
        Vector2 rootScale,
        HashSet<nint> visited,
        List<FilterButtonCandidate> candidates)
    {
        if (node is null || !visited.Add((nint)node))
            return;

        if (node->GetNodeType() is NodeType.Component && node->GetVisibility())
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component is not null)
            {
                var componentType = component->GetComponentType();
                if (componentType is ComponentType.Button or ComponentType.CheckBox or
                    ComponentType.RadioButton or ComponentType.ListItemRenderer)
                {
                    var nodeSize = node->GetSize() / rootScale;
                    if (nodeSize.X >= 100f && nodeSize.Y is >= 16f and <= 48f)
                    {
                        var candidate = new FilterButtonCandidate(
                            (node->GetPosition() - rootPosition) / rootScale,
                            nodeSize);
                        var duplicate = candidates.Any(existing =>
                            MathF.Abs(existing.Position.X - candidate.Position.X) <= 2f &&
                            MathF.Abs(existing.Position.Y - candidate.Position.Y) <= 2f &&
                            MathF.Abs(existing.Size.X - candidate.Size.X) <= 4f &&
                            MathF.Abs(existing.Size.Y - candidate.Size.Y) <= 4f);
                        if (!duplicate)
                            candidates.Add(candidate);
                    }
                }

                for (var index = 0; index < component->UldManager.NodeListCount; index++)
                    CollectCandidates(component->UldManager.NodeList[index], rootPosition, rootScale, visited, candidates);
                CollectCandidates(component->UldManager.RootNode, rootPosition, rootScale, visited, candidates);
            }
        }

        for (var child = node->ChildNode; child is not null; child = child->NextSiblingNode)
            CollectCandidates(child, rootPosition, rootScale, visited, candidates);
    }

    private readonly record struct FilterButtonCandidate(Vector2 Position, Vector2 Size);
}
