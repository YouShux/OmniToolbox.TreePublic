using System.Linq;
using Dalamud.Interface;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private readonly List<MultiToolbar> auxiliaryToolbars = [];
    private string selectedBarID = "main";

    private MultiToolbarConfig CollectionConfig => (MultiToolbarConfig)config;

    public override bool DrawSettings()
    {
        MigrateMultiDockPlugins();
        var changed = DrawConfigTransfer();
        SyncAuxiliaryBars();
        var rowHeight = MathF.Max(OmniTheme.CheckboxSize(), OmniControls.CompactButtonSize(
            OmniLoc.Get("Feature.MultiToolbar.EditWidgets")).Y);
        var rowSpacing = ImGui.GetStyle().ItemSpacing.Y;
        using var rowStyle = ImRaii.PushStyle(ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, (rowHeight - ImGui.GetTextLineHeight()) * 0.5f));
        var dividerTop = ImGui.GetCursorScreenPos().Y;
        var dividerX = 0f;
        using (var table = ImRaii.Table("##multiToolbarBars", 2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            if (!table)
            {
                return changed;
            }

            ImGui.TableSetupColumn("##bars", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(180f));
            ImGui.TableSetupColumn("##settings", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawBarListRow("main", OmniLoc.Get("Feature.MultiToolbar.MainBar"), null, rowHeight, rowSpacing, ref changed);

            MultiToolbarAuxiliaryBarConfig? removed = null;
            foreach (var bar in CollectionConfig.AuxiliaryBars)
            {
                if (DrawBarListRow(bar.ID, bar.Name, bar, rowHeight, rowSpacing, ref changed))
                {
                    removed = bar;
                }
            }
            if (removed is not null)
            {
                CollectionConfig.AuxiliaryBars.Remove(removed);
                if (selectedBarID == removed.ID)
                {
                    selectedBarID = "main";
                }
                changed = true;
            }

            if (OmniControls.IconButton("addToolbar", FontAwesomeIcon.Plus, false, OmniLoc.Get("Feature.MultiToolbar.AddBar")))
            {
                var number = 1;
                string name;
                do
                {
                    name = string.Format(OmniLoc.Get("Feature.MultiToolbar.AuxiliaryBar"), number++);
                } while (CollectionConfig.AuxiliaryBars.Any(bar => bar.Name == name));
                var bar = new MultiToolbarAuxiliaryBarConfig
                {
                    Name = name,
                    Widgets = [],
                    BarVerticalOffset = config.BarVerticalOffset + 40f * (CollectionConfig.AuxiliaryBars.Count + 1),
                    MultiDockMigrated = true,
                    MultiDockPluginsMigrated = true,
                    TextEntryDefaultsApplied = true
                };
                CollectionConfig.AuxiliaryBars.Add(bar);
                selectedBarID = bar.ID;
                changed = true;
            }

            SyncAuxiliaryBars();
            var selected = auxiliaryToolbars.FirstOrDefault(bar => bar.barID == selectedBarID) ?? this;
            ImGui.TableNextColumn();
            dividerX = ImGui.GetCursorScreenPos().X - ImGui.GetStyle().CellPadding.X;
            using var settingsID = ImRaii.PushId(selected.barID);
            changed |= selected.DrawBarSettings(rowHeight, rowSpacing);
        }
        var dividerColor = OmniTheme.Tokens.Text;
        dividerColor.W = 0.6f;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(dividerX, dividerTop),
            new Vector2(dividerX, ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y),
            ImGui.GetColorU32(dividerColor), OmniTheme.Scale(2f));
        return changed;
    }

    private bool DrawBarListRow(string id, string label, MultiToolbarAuxiliaryBarConfig? bar,
        float height, float spacing, ref bool changed)
    {
        using var rowID = ImRaii.PushId(id);
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var labelWidth = MathF.Max(1f, width - (bar is null ? 0f : height + ImGui.GetStyle().ItemSpacing.X));
        if (ImGui.InvisibleButton("##selectBar", new Vector2(labelWidth, height)))
        {
            selectedBarID = id;
        }
        var drawList = ImGui.GetWindowDrawList();
        if (selectedBarID == id || ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(position, position + new Vector2(width, height),
                ImGui.GetColorU32(selectedBarID == id ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered));
        }
        var textPadding = ImGui.GetStyle().FramePadding.X;
        drawList.AddText(position + new Vector2(textPadding, (height - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(ImGuiCol.Text), EllipsizeToolbarText(label, MathF.Max(1f, labelWidth - textPadding * 2f)));
        var remove = false;
        if (bar is not null)
        {
            using (var menu = ImRaii.ContextPopupItem("##barMenu"))
            {
                if (menu)
                {
                    var name = bar.Name;
                    if (ImGui.InputText(OmniLoc.Get("Feature.MultiToolbar.BarName"), ref name, 80) &&
                        !string.IsNullOrWhiteSpace(name))
                    {
                        bar.Name = name;
                    }
                    changed |= ImGui.IsItemDeactivatedAfterEdit();
                }
            }
            ImGui.SetCursorScreenPos(position + new Vector2(width - height, 0f));
            remove = OmniControls.IconButton("deleteToolbar", FontAwesomeIcon.Trash, false,
                new Vector2(height), OmniLoc.Get("Feature.MultiToolbar.DeleteBar"));
        }
        ImGui.SetCursorScreenPos(position + new Vector2(0f, height + spacing));
        return remove;
    }

    private void SyncAuxiliaryBars()
    {
        var bars = CollectionConfig.AuxiliaryBars;
        var synchronized = bars.Count == auxiliaryToolbars.Count;
        for (var index = 0; synchronized && index < bars.Count; index++)
        {
            synchronized = ReferenceEquals(bars[index], auxiliaryToolbars[index].config);
        }
        if (synchronized)
        {
            return;
        }

        ReleaseAuxiliaryBars();
        var ids = new HashSet<string>(StringComparer.Ordinal) { "main" };
        var changed = false;
        foreach (var bar in bars)
        {
            if (string.IsNullOrWhiteSpace(bar.ID) || !ids.Add(bar.ID))
            {
                bar.ID = Guid.NewGuid().ToString("N");
                ids.Add(bar.ID);
                changed = true;
            }
            auxiliaryToolbars.Add(new MultiToolbar(bar, saveConfig, iconBrowser, teleportService, bar.ID));
        }
        if (changed)
        {
            saveConfig();
        }
    }

    private void Draw()
    {
        SyncAuxiliaryBars();
        UpdateNativeDtrVisibility(UsesNativeDtr());
        var worldMarkersDrawn = HasWorldMarkerOverlay();
        DrawBar(worldMarkersDrawn);
        foreach (var bar in auxiliaryToolbars)
        {
            var drawWorldMarkers = !worldMarkersDrawn && bar.HasWorldMarkerOverlay();
            bar.DrawBar(drawWorldMarkers);
            worldMarkersDrawn |= drawWorldMarkers;
        }
    }

    private bool HasWorldMarkerOverlay() =>
        config.ShowWorldMarkerOverlay &&
        config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.MarkerControl);

    private bool UsesNativeDtr() =>
        config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList) ||
        CollectionConfig.AuxiliaryBars.Any(bar =>
            bar.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.DtrList));

    private void ReleaseBarResources()
    {
        onlineStatusDetailTasks?.Dispose();
        onlineStatusDetailTasks = null;
        toolbarFont?.Dispose();
        toolbarFont = null;
        ClosePopup();
        RestoreDtrEntries();
        autoHideOffset = 0f;
    }

    private void ReleaseAuxiliaryBars()
    {
        foreach (var bar in auxiliaryToolbars)
        {
            bar.Dispose();
        }
        auxiliaryToolbars.Clear();
    }

    protected override void OnDispose()
    {
        ReleaseBarResources();
        ReleaseAuxiliaryBars();
    }
}
