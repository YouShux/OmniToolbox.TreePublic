using FFXIVClientStructs.FFXIV.Component.GUI;

namespace OmniToolbox.TreePublic;

internal sealed unsafe class ListItemLockStateCache
{
    private const byte LockedAlpha = 0x90;
    internal const int MaxDepth = 8;
    private readonly Dictionary<nint, NodeFlags> nodeFlags = [];
    private readonly Dictionary<nint, NodeAlpha> nodeAlphas = [];

    public void Apply(AtkComponentListItemRenderer* renderer, bool locked)
    {
        if (renderer == null)
        {
            return;
        }

        var owner = renderer->AtkComponentButton.AtkComponentBase.OwnerNode;
        if (owner != null)
        {
            ApplyInteraction(&owner->AtkResNode, locked, 0);
        }

        var root = renderer->UldManager.RootNode;
        if (root == null)
        {
            return;
        }

        ApplyAlpha(root, locked, 0);
        ApplyInteraction(root, locked, 0);
    }

    public void Restore()
    {
        foreach (var (address, flags) in nodeFlags)
        {
            ((AtkResNode*)address)->NodeFlags = flags;
        }

        foreach (var (address, alpha) in nodeAlphas)
        {
            var node = (AtkResNode*)address;
            node->Color.A = alpha.Node;
            if (alpha.IsText && node->Type == NodeType.Text)
            {
                var text = (AtkTextNode*)node;
                text->TextColor.A = alpha.Text;
                text->EdgeColor.A = alpha.Edge;
            }
        }

        Forget();
    }

    public void Forget()
    {
        nodeFlags.Clear();
        nodeAlphas.Clear();
    }

    private void ApplyAlpha(AtkResNode* node, bool locked, int depth)
    {
        if (node == null || depth > MaxDepth)
        {
            return;
        }

        var address = (nint)node;
        if (locked)
        {
            if (!nodeAlphas.ContainsKey(address))
            {
                nodeAlphas[address] = node->Type == NodeType.Text
                    ? new(node->Color.A, ((AtkTextNode*)node)->TextColor.A, ((AtkTextNode*)node)->EdgeColor.A, true)
                    : new(node->Color.A, 0, 0, false);
            }

            node->Color.A = LockedAlpha;
            if (node->Type == NodeType.Text)
            {
                var text = (AtkTextNode*)node;
                text->TextColor.A = LockedAlpha;
                text->EdgeColor.A = LockedAlpha;
            }
        }
        else if (nodeAlphas.TryGetValue(address, out var original))
        {
            node->Color.A = original.Node;
            if (original.IsText && node->Type == NodeType.Text)
            {
                var text = (AtkTextNode*)node;
                text->TextColor.A = original.Text;
                text->EdgeColor.A = original.Edge;
            }
        }

        for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
        {
            ApplyAlpha(child, locked, depth + 1);
        }
    }

    private void ApplyInteraction(AtkResNode* node, bool locked, int depth)
    {
        if (node == null || depth > MaxDepth)
        {
            return;
        }

        const NodeFlags interactionFlags =
            NodeFlags.RespondToMouse | NodeFlags.EmitsEvents | NodeFlags.HasCollision;
        var address = (nint)node;
        if (!locked && nodeFlags.TryGetValue(address, out var original))
        {
            node->NodeFlags = original;
        }
        else if (locked && ((node->NodeFlags & interactionFlags) != 0 || node->Type == NodeType.Collision))
        {
            if (!nodeFlags.ContainsKey(address))
            {
                nodeFlags[address] = node->NodeFlags;
            }

            node->NodeFlags &= ~interactionFlags;
        }

        for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
        {
            ApplyInteraction(child, locked, depth + 1);
        }
    }

    private readonly record struct NodeAlpha(byte Node, byte Text, byte Edge, bool IsText);
}
