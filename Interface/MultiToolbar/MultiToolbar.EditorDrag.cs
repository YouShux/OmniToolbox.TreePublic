using System.Linq;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private (int From, MultiToolbarWidgetSide? Side, int To, bool Dtr)? pendingWidgetDrop;

    private List<int> GetWidgetEditorColumnOrder()
    {
        var order = config.WidgetEditorColumnOrder;
        if (order is null || order.Count != 4 || order.Distinct().Count() != 4 || order.Any(value => value is < 0 or > 3))
        {
            config.WidgetEditorColumnOrder = order = [0, 1, 2, 3];
        }
        return order;
    }

    private static MultiToolbarWidgetSide GetWidgetEditorSide(List<int> order, int column) =>
        (MultiToolbarWidgetSide)order.Take(column).Count(value => value != 3);

    private static string WidgetEditorSideKey(MultiToolbarWidgetSide side) => side switch
    {
        MultiToolbarWidgetSide.Left => "Feature.MultiToolbar.WidgetSideLeft",
        MultiToolbarWidgetSide.Center => "Feature.MultiToolbar.WidgetSideCenter",
        _ => "Feature.MultiToolbar.WidgetSideRight"
    };

    private bool SwapWidgetEditorColumns(List<int> order, int from, int to)
    {
        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }
        var oldOrder = order.ToArray();
        (order[from], order[to]) = (order[to], order[from]);
        var oldSides = oldOrder.Where(value => value != 3).ToArray();
        var newSides = order.Where(value => value != 3).ToArray();
        foreach (var widget in config.Widgets)
        {
            if (widget.Type != MultiToolbarWidgetType.DtrList && (uint)widget.Side < 3)
            {
                widget.Side = (MultiToolbarWidgetSide)Array.IndexOf(newSides, oldSides[(int)widget.Side]);
            }
        }
        if (oldOrder[from] == 3 || oldOrder[to] == 3)
        {
            var dtrWidgets = config.Widgets.Where(widget => widget.Type == MultiToolbarWidgetType.DtrList).ToArray();
            if (dtrWidgets.Length == 0)
            {
                var widget = new MultiToolbarWidgetConfig { Type = MultiToolbarWidgetType.DtrList };
                config.Widgets.Add(widget);
                dtrWidgets = [widget];
            }
            var side = (MultiToolbarWidgetSide)Math.Min(order.IndexOf(3), 2);
            foreach (var widget in dtrWidgets)
            {
                widget.Side = side;
                config.Widgets.Remove(widget);
            }
            var insertion = config.Widgets.FindIndex(widget => widget.Side == side);
            config.Widgets.InsertRange(insertion < 0 ? config.Widgets.Count : insertion, dtrWidgets);
        }
        return true;
    }

    private void AcceptDtrWidgetDrop(MultiToolbarWidgetSide side, int index)
    {
        using var target = ImRaii.DragDropTarget();
        if (target)
        {
            AcceptWidgetEditorPayload("OMNI_TOOLBAR_DTR", side, index, true);
        }
    }

    private void DrawWidgetEditorDropArea(MultiToolbarWidgetSide? side)
    {
        ImGui.InvisibleButton("##widgetDropArea", new Vector2(
            MathF.Max(1f, ImGui.GetContentRegionAvail().X),
            MathF.Max(OmniTheme.Scale(40f), ImGui.GetContentRegionAvail().Y)));
        using var target = ImRaii.DragDropTarget();
        if (!target)
        {
            return;
        }
        if (side.HasValue)
        {
            AcceptWidgetEditorPayload("OMNI_TOOLBAR_DTR", side, config.Widgets.Count, true);
        }
        if (side.HasValue || (draggedEntryType == "OMNI_TOOLBAR_WIDGET" &&
            (uint)draggedEntryIndex < config.Widgets.Count && config.Widgets[draggedEntryIndex].Type == MultiToolbarWidgetType.DtrSingle))
        {
            AcceptWidgetEditorPayload("OMNI_TOOLBAR_WIDGET", side,
                side.HasValue ? config.Widgets.Count : config.DtrOrder.Count, false);
        }
    }

    private void AcceptReturnedDtrDrop(int index)
    {
        if (draggedEntryType != "OMNI_TOOLBAR_WIDGET" || (uint)draggedEntryIndex >= config.Widgets.Count ||
            config.Widgets[draggedEntryIndex].Type != MultiToolbarWidgetType.DtrSingle)
        {
            return;
        }
        using var target = ImRaii.DragDropTarget();
        if (target)
        {
            AcceptWidgetEditorPayload("OMNI_TOOLBAR_WIDGET", null, index, false);
        }
    }

    private void AcceptWidgetEditorPayload(string type, MultiToolbarWidgetSide? side, int to, bool dtr)
    {
        var payload = ImGui.AcceptDragDropPayload(type);
        if (!payload.IsNull && payload.IsDelivery() && draggedEntryType == type)
        {
            pendingWidgetDrop = (draggedEntryIndex, side, to, dtr);
            draggedEntryIndex = -1;
            draggedEntryType = string.Empty;
        }
    }

    private bool ApplyWidgetEditorDrop()
    {
        if (pendingWidgetDrop is not { } drop)
        {
            return false;
        }
        pendingWidgetDrop = null;
        if (drop.Dtr)
        {
            if ((uint)drop.From >= config.DtrOrder.Count || !drop.Side.HasValue)
            {
                return false;
            }
            var title = config.DtrOrder[drop.From];
            if (config.Widgets.Any(widget => widget.Type == MultiToolbarWidgetType.DtrSingle && widget.DtrTitle == title))
            {
                return false;
            }
            config.Widgets.Insert(Math.Clamp(drop.To, 0, config.Widgets.Count), new MultiToolbarWidgetConfig
            {
                Type = MultiToolbarWidgetType.DtrSingle, Side = drop.Side.Value, DtrTitle = title
            });
            return true;
        }
        if ((uint)drop.From >= config.Widgets.Count)
        {
            return false;
        }
        var moved = config.Widgets[drop.From];
        if (!drop.Side.HasValue)
        {
            if (moved.Type != MultiToolbarWidgetType.DtrSingle)
            {
                return false;
            }
            config.Widgets.RemoveAt(drop.From);
            var order = config.DtrOrder.IndexOf(moved.DtrTitle);
            if (order >= 0)
            {
                MoveEntry(config.DtrOrder, order, Math.Clamp(drop.To, 0, config.DtrOrder.Count - 1));
            }
            return true;
        }
        moved.Side = drop.Side.Value;
        config.Widgets.RemoveAt(drop.From);
        config.Widgets.Insert(Math.Clamp(drop.To, 0, config.Widgets.Count), moved);
        return true;
    }
}
