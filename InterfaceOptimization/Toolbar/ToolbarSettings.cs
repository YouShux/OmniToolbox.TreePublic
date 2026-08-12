using Dalamud.Interface;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.ImGuiOm;

namespace OmniToolbox.TreePublic;

internal sealed class ToolbarSettings(
    ToolbarConfig config,
    IconBrowser iconBrowser,
    Action saveConfig)
{
    private const string PayloadType = "OMNI_TOOLBAR_COMPONENT";
    private ToolbarSection draggedSection;
    private int draggedIndex = -1;
    private ToolbarSection addSection;

    public bool Draw()
    {
        var changed = config.Normalize();
        changed |= DrawGeneralSettings();
        ImGui.Dummy(new(0f, OmniTheme.Scale(8f)));
        changed |= DrawSections(out var openAddPopup);
        if (openAddPopup)
        {
            ImGui.OpenPopup("##toolbarAddComponent");
        }

        changed |= DrawAddPopup();
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            draggedIndex = -1;
        }

        return changed;
    }

    private bool DrawGeneralSettings()
    {
        var changed = false;
        var top = config.Edge == ToolbarEdge.Top;
        if (ImGui.RadioButton($"{OmniLoc.Get("Feature.Toolbar.Edge.Top")}##toolbarTop", top))
        {
            config.Edge = ToolbarEdge.Top;
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton($"{OmniLoc.Get("Feature.Toolbar.Edge.Bottom")}##toolbarBottom", !top))
        {
            config.Edge = ToolbarEdge.Bottom;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(20f));
        var stretch = config.Stretch;
        if (OmniControls.Checkbox($"{OmniLoc.Get("Feature.Toolbar.Stretch")}##toolbarStretch", ref stretch))
        {
            config.Stretch = stretch;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(20f));
        var hideNativeDtr = config.HideNativeDtr;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.Toolbar.HideNativeDtr")}##toolbarHideNativeDtr",
                ref hideNativeDtr))
        {
            config.HideNativeDtr = hideNativeDtr;
            changed = true;
        }

        ImGui.SameLine(0f, OmniTheme.Scale(20f));
        var locked = config.Locked;
        if (OmniControls.Checkbox(
                $"{OmniLoc.Get("Feature.Toolbar.Locked")}##toolbarLocked",
                ref locked))
        {
            config.Locked = locked;
            changed = true;
        }

        var scale = config.Scale;
        if (DrawSlider("Scale", ref scale, 0.75f, 2f, "%.2f"))
        {
            config.Scale = scale;
            changed = true;
        }

        var opacity = config.Opacity;
        if (DrawSlider("Opacity", ref opacity, 0.15f, 1f, "%.2f"))
        {
            config.Opacity = opacity;
            changed = true;
        }

        var spacing = config.Spacing;
        if (DrawSlider("Spacing", ref spacing, 0f, 32f, "%.0f"))
        {
            config.Spacing = spacing;
            changed = true;
        }

        var height = config.Height;
        if (DrawSlider("Height", ref height, 24f, 64f, "%.0f"))
        {
            config.Height = height;
            changed = true;
        }

        return changed;
    }

    private static bool DrawSlider(string name, ref float value, float minimum, float maximum, string format)
    {
        var changed = OmniControls.SliderFloat(
            $"##toolbar{name}",
            ref value,
            minimum,
            maximum,
            format,
            MathF.Max(OmniTheme.Scale(120f), ImGui.GetContentRegionAvail().X * 0.3f));
        ImGui.SameLine(0f, OmniTheme.Scale(8f));
        ImGui.TextUnformatted(OmniLoc.Get($"Feature.Toolbar.{name}"));
        return changed;
    }

    private bool DrawSections(out bool openAddPopup)
    {
        var changed = false;
        openAddPopup = false;
        (ToolbarSection Section, int Index)? moveTarget = null;
        (ToolbarSection Section, int Index)? removeTarget = null;
        using var table = ImRaii.Table(
            "##toolbarSections",
            3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame,
            new(ImGui.GetContentRegionAvail().X, 0f));
        if (!table)
        {
            return false;
        }

        foreach (var section in Enum.GetValues<ToolbarSection>())
        {
            ImGui.TableSetupColumn(OmniLoc.Get($"Feature.Toolbar.Section.{section}"));
        }

        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        foreach (var section in Enum.GetValues<ToolbarSection>())
        {
            ImGui.TableNextColumn();
            var components = config.GetSection(section);
            for (var index = 0; index < components.Count; index++)
            {
                var label = OmniLoc.Get($"Feature.Toolbar.Component.{components[index]}");
                OmniControls.IconButton(
                    $"##toolbarDrag{section}{index}",
                    FontAwesomeIcon.Bars,
                    false,
                    OmniLoc.Get("Feature.Toolbar.Reorder"));
                using (var source = ImRaii.DragDropSource())
                {
                    if (source)
                    {
                        if (ImGui.SetDragDropPayload(PayloadType, []))
                        {
                            draggedSection = section;
                            draggedIndex = index;
                        }

                        ImGui.TextUnformatted(label);
                    }
                }

                using (var target = ImRaii.DragDropTarget())
                {
                    if (target)
                    {
                        var payload = ImGui.AcceptDragDropPayload(PayloadType);
                        if (!payload.IsNull && payload.IsDelivery())
                        {
                            moveTarget = (section, index);
                        }
                    }
                }

                ImGui.SameLine(0f, OmniTheme.Scale(6f));
                if (components[index] != ToolbarComponentType.Separator)
                {
                    changed |= DrawIconButton(section, index, components[index]);
                    ImGui.SameLine(0f, OmniTheme.Scale(6f));
                }

                ImGui.TextUnformatted(label);
                ImGui.SameLine();
                if (OmniControls.IconButton(
                        $"##toolbarRemove{section}{index}",
                        FontAwesomeIcon.Trash,
                        false,
                        OmniLoc.Get("Common.Delete")))
                {
                    removeTarget = (section, index);
                }
            }

            if (OmniControls.SmallButton(
                    $"{OmniLoc.Get("Common.Add")}##toolbarAdd{section}",
                    false))
            {
                addSection = section;
                openAddPopup = true;
            }

            using var endTarget = ImRaii.DragDropTarget();
            if (endTarget)
            {
                var payload = ImGui.AcceptDragDropPayload(PayloadType);
                if (!payload.IsNull && payload.IsDelivery())
                {
                    moveTarget = (section, components.Count);
                }
            }
        }

        if (removeTarget is { } remove)
        {
            config.GetSection(remove.Section).RemoveAt(remove.Index);
            return true;
        }

        if (moveTarget is { } move)
        {
            changed |= MoveComponent(move.Section, move.Index);
        }

        return changed;
    }

    private bool DrawIconButton(
        ToolbarSection section,
        int index,
        ToolbarComponentType component)
    {
        var iconID = config.GetIconID(component);
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(OmniTheme.CheckboxSize());
        if (OmniControls.IconButton(
                $"##toolbarIcon{section}{index}",
                FontAwesomeIcon.Image,
                false,
                size,
                OmniLoc.Get("Feature.Toolbar.Icon"),
                iconVisible: iconID == 0))
        {
            iconBrowser.OpenForSelection(
                false,
                value =>
                {
                    config.Icons[component] = value;
                    saveConfig();
                },
                null);
        }

        if (iconID != 0)
        {
            var padding = OmniTheme.Scale(3f);
            FramedGameIcon.Draw(
                iconID,
                position + new Vector2(padding),
                size - new Vector2(padding * 2f),
                drawFrame: false,
                preserveAspectRatio: true);
        }

        return ImGui.IsItemClicked(ImGuiMouseButton.Right) && config.Icons.Remove(component);
    }

    private bool DrawAddPopup()
    {
        var changed = false;
        if (!ImGui.BeginPopup("##toolbarAddComponent"))
        {
            return false;
        }

        foreach (var component in Enum.GetValues<ToolbarComponentType>())
        {
            if (component != ToolbarComponentType.Separator && Contains(component))
            {
                continue;
            }

            if (ImGui.Selectable(OmniLoc.Get($"Feature.Toolbar.Component.{component}")))
            {
                config.GetSection(addSection).Add(component);
                changed = true;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
        return changed;
    }

    private bool MoveComponent(ToolbarSection targetSection, int targetIndex)
    {
        if (draggedIndex < 0)
        {
            return false;
        }

        var source = config.GetSection(draggedSection);
        if (draggedIndex >= source.Count)
        {
            return false;
        }

        var component = source[draggedIndex];
        source.RemoveAt(draggedIndex);
        var target = config.GetSection(targetSection);
        if (source == target && draggedIndex < targetIndex)
        {
            targetIndex--;
        }

        target.Insert(Math.Clamp(targetIndex, 0, target.Count), component);
        draggedIndex = -1;
        return true;
    }

    private bool Contains(ToolbarComponentType component)
    {
        foreach (var section in Enum.GetValues<ToolbarSection>())
        {
            if (config.GetSection(section).Contains(component))
            {
                return true;
            }
        }

        return false;
    }
}
