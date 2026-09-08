using System.Linq;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly (uint Id, uint Icon)[] QuickCommandCategories =
    [
        (7, 14),
        (6, 20),
        (5, 17),
        (4, 7),
        (3, 21),
        (2, 5),
        (1, 1),
    ];

    private void DrawQuickCommandsPopup()
    {
        var categories = DService.Instance().Data.GetExcelSheet<MainCommandCategory>();
        var commands = DService.Instance().Data.GetExcelSheet<MainCommand>();
        if (categories is null || commands is null)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.QuickCommandsUnavailable"));
            return;
        }

        using var table = ImRaii.Table(
            "##multiToolbarQuickCommands",
            2,
            ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.BordersInnerV,
            new Vector2(0f, MathF.Max(1f, ImGui.GetContentRegionAvail().Y)));
        if (!table)
        {
            return;
        }

        ImGui.TableSetupColumn("##multiToolbarQuickCommandCategories", ImGuiTableColumnFlags.WidthFixed, OmniTheme.Scale(190f));
        ImGui.TableSetupColumn("##multiToolbarQuickCommandItems", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        using (var categoryChild = ImRaii.Child("##quickCommandCategories", new Vector2(0f, 0f), false))
        {
            if (categoryChild)
            {
                foreach (var (categoryID, iconID) in QuickCommandCategories)
                {
                    var category = categories.GetRowOrDefault(categoryID);
                    if (category is not { } categoryRow)
                    {
                        continue;
                    }

                    var name = categoryRow.Name.ExtractText();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var selected = quickCommandCategory == categoryID;
                    if (DrawQuickCommandEntry($"##quickCategory{categoryID}", name, iconID, selected, true))
                    {
                        quickCommandCategory = (int)categoryID;
                    }
                }
            }
        }

        ImGui.TableNextColumn();
        using var itemChild = ImRaii.Child("##quickCommandItems", new Vector2(0f, 0f), false);
        if (!itemChild)
        {
            return;
        }

        var itemCount = 0;
        foreach (var command in commands
                     .Where(command => command.MainCommandCategory.RowId == (uint)quickCommandCategory)
                     .OrderBy(command => command.SortID))
        {
            var name = command.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            itemCount++;
            var iconID = command.RowId switch
            {
                35 => 111u,
                36 => 112u,
                _ => command.Icon > 0 ? (uint)command.Icon : 0u,
            };
            var enabled = IsQuickCommandAvailable(command.RowId);
            if (DrawQuickCommandEntry($"##quickCommand{command.RowId}", name, iconID, false, enabled))
            {
                ExecuteQuickCommand(command.RowId);
                ClosePopup();
                break;
            }
        }

        if (itemCount == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.QuickCommandsEmpty"));
        }
    }

    private bool DrawQuickCommandEntry(string id, string label, uint iconID, bool selected, bool enabled)
    {
        var rowHeight = MathF.Max(ImGui.GetFrameHeight(), GetPopupRowIconSize() + OmniTheme.Scale(4f));
        var origin = ImGui.GetCursorScreenPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;
        using var disabled = ImRaii.Disabled(!enabled);
        var clicked = ImGui.Selectable(id, selected, ImGuiSelectableFlags.None, new Vector2(rowWidth, rowHeight));
        if (ImGui.IsItemHovered())
        {
            ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(rowWidth, rowHeight),
                ImGui.GetColorU32(ImGui.IsItemActive() ? ImGuiCol.HeaderActive : ImGuiCol.HeaderHovered),
                OmniTheme.Scale(OmniTheme.Tokens.ButtonRadius));
        }

        var iconSize = GetPopupRowIconSize();
        var iconPosition = origin + new Vector2(ImGui.GetStyle().FramePadding.X, (rowHeight - iconSize) * 0.5f);
        if (iconID > 0 && ImageHelper.GetGameIcon(iconID) is { } texture)
        {
            ImGui.GetWindowDrawList().AddImage(texture.Handle, iconPosition, iconPosition + new Vector2(iconSize));
        }

        ImGui.GetWindowDrawList().AddText(
            origin + new Vector2(ImGui.GetStyle().FramePadding.X + iconSize + OmniTheme.Scale(8f),
                (rowHeight - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(ImGuiCol.Text), label);
        return clicked;
    }

    private static unsafe void ExecuteQuickCommand(uint commandID)
    {
        var ui = UIModule.Instance();
        if (ui is not null)
        {
            ui->ExecuteMainCommand(commandID);
        }
    }

    private static unsafe bool IsQuickCommandAvailable(uint commandID)
    {
        var ui = UIModule.Instance();
        var hud = AgentHUD.Instance();
        return ui is not null && hud is not null && ui->IsMainCommandUnlocked(commandID) && hud->IsMainCommandEnabled(commandID);
    }
}
