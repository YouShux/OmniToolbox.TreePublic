using Dalamud.Interface;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private int draggedEntryIndex = -1;
    private string draggedEntryType = string.Empty;

    private int DrawReorderHandle(string payloadType, int index, float height)
    {
        var label = OmniLoc.Get("Feature.MultiDock.Reorder");
        OmniControls.IconButton("reorder", FontAwesomeIcon.Bars, false, new Vector2(height), label);
        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                draggedEntryIndex = index;
                draggedEntryType = payloadType;
                ImGui.SetDragDropPayload(payloadType, []);
                ImGui.TextUnformatted(label);
            }
        }
        using (var target = ImRaii.DragDropTarget())
        {
            if (target)
            {
                var payload = ImGui.AcceptDragDropPayload(payloadType);
                if (!payload.IsNull && payload.IsDelivery() && draggedEntryType == payloadType)
                {
                    var sourceIndex = draggedEntryIndex;
                    draggedEntryIndex = -1;
                    draggedEntryType = string.Empty;
                    return sourceIndex == index ? -1 : sourceIndex;
                }
            }
        }
        return -1;
    }

    private static bool MoveEntry<T>(List<T> entries, int from, int to)
    {
        if (from < 0 || to < 0 || from >= entries.Count || to >= entries.Count || from == to)
        {
            return false;
        }
        var entry = entries[from];
        entries.RemoveAt(from);
        entries.Insert(to, entry);
        return true;
    }
}
