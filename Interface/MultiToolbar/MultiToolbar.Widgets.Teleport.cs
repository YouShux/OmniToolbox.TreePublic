using System.Globalization;
using System.Linq;
using Dalamud.Game.Text;
using Lumina.Data.Files;
using Lumina.Text;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Host;
using OmniToolbox.Notifications;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private readonly List<TeleportPanelColumn> teleportPanelColumns = [];
    private uint teleportPanelTerritory;
    private UldFile? teleportPanelUld;

    private sealed record TeleportPanelDestination(
        uint AetheryteID, byte SubIndex, string Name, uint Cost, uint Order,
        uint ExpansionID, string ExpansionName, uint RegionID, string RegionName, uint IconPart);
    private sealed record TeleportPanelRegion(string Name, List<TeleportPanelDestination> Destinations);
    private sealed record TeleportPanelColumn(string Name, List<TeleportPanelRegion> Regions);

    private void RefreshTeleportPanel()
    {
        teleportPanelColumns.Clear();
        teleportPanelUld ??= DalamudServices.DataManager.GetFile<UldFile>("ui/uld/Teleport.uld");
        var services = DService.Instance();
        teleportPanelTerritory = services.ClientState.TerritoryType;
        if (!services.ClientState.IsLoggedIn)
        {
            return;
        }

        // 打开时读取已解锁目的地及当前费用，绘制时只遍历分组快照。
        var destinations = new List<TeleportPanelDestination>();
        foreach (var entry in services.AetheryteList)
        {
            if (entry.AetheryteData.ValueNullable is not { } aetheryte ||
                aetheryte.Territory.ValueNullable is not { } territory ||
                territory.Map.ValueNullable is not { } map)
            {
                continue;
            }

            var housing = entry.IsSharedHouse || entry.IsApartment || entry.Ward > 0 || entry.Plot > 0;
            if (!housing && (aetheryte.Invisible || !aetheryte.IsAetheryte))
            {
                continue;
            }

            var name = aetheryte.PlaceName.Value.Name.ExtractText();
            if (housing)
            {
                name = AetheryteRecordManager.Instance().AllRecords.FirstOrDefault(record =>
                    record.RowID == entry.AetheryteID && record.SubIndex == entry.SubIndex)?.Name ?? name;
            }

            destinations.Add(new(
                entry.AetheryteID, entry.SubIndex, name, entry.GilCost, aetheryte.Order,
                housing ? uint.MaxValue : territory.ExVersion.RowId,
                housing ? OmniLoc.Get("Feature.MultiToolbar.TeleportPanel.Housing") : territory.ExVersion.Value.Name.ExtractText(),
                map.PlaceNameRegion.RowId, map.PlaceNameRegion.Value.Name.ExtractText(),
                GetTeleportIconPart(map.PlaceNameRegion.RowId, territory.RowId)));
        }

        foreach (var expansion in destinations.OrderBy(static destination => destination.ExpansionID)
                     .ThenBy(static destination => destination.Order)
                     .ThenBy(static destination => destination.AetheryteID)
                     .ThenBy(static destination => destination.SubIndex)
                     .GroupBy(static destination => destination.ExpansionID))
        {
            teleportPanelColumns.Add(new(expansion.First().ExpansionName,
                expansion.GroupBy(static destination => destination.RegionID)
                    .Select(static region => new TeleportPanelRegion(region.First().RegionName, region.ToList()))
                    .ToList()));
        }
    }

    private static float TeleportPanelRowHeight => MathF.Floor(
        ImGui.GetFont().FontSize * ImGui.GetIO().FontGlobalScale + OmniTheme.Scale(4f));

    private static float TeleportPanelGap => MathF.Floor(OmniTheme.Scale(8f));

    private static Vector2 TeleportPanelPadding => new(
        OmniTheme.Scale(OmniTheme.Tokens.WindowPadding).X, MathF.Floor(OmniTheme.Scale(8f)));

    private float GetTeleportPanelContentHeight()
    {
        var rowHeight = TeleportPanelRowHeight;
        var height = rowHeight;
        foreach (var column in teleportPanelColumns)
        {
            var columnHeight = rowHeight + Math.Max(0, column.Regions.Count - 1) * TeleportPanelGap;
            foreach (var region in column.Regions)
            {
                columnHeight += (region.Destinations.Count + 1) * rowHeight;
            }
            height = MathF.Max(height, columnHeight);
        }
        return height;
    }

    private float GetTeleportPanelHeight() => GetTeleportPanelContentHeight() + TeleportPanelPadding.Y * 2f;

    private void DrawTeleportPanel()
    {
        if (!DService.Instance().ClientState.IsLoggedIn ||
            teleportPanelTerritory != DService.Instance().ClientState.TerritoryType)
        {
            RefreshTeleportPanel();
        }

        if (teleportPanelColumns.Count == 0)
        {
            ImGui.TextDisabled(OmniLoc.Get("Feature.MultiToolbar.TeleportPanel.Empty"));
            return;
        }

        var columnWidth = OmniTheme.Scale(200f);
        var gap = TeleportPanelGap;
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero)
            .Push(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        // 行高和间距使用整数像素，保持窗口估算与逐行布局一致。
        var rowHeight = TeleportPanelRowHeight;
        var contentHeight = GetTeleportPanelContentHeight();
        ImGui.SetNextWindowContentSize(new Vector2(
            teleportPanelColumns.Count * (columnWidth + gap) - gap, contentHeight));
        using var child = ImRaii.Child("##teleportPanel", ImGui.GetContentRegionAvail(), false,
            ImGuiWindowFlags.NoScrollbar);
        if (!child)
        {
            return;
        }

        var start = ImGui.GetCursorPos();
        for (var index = 0; index < teleportPanelColumns.Count; index++)
        {
            ImGui.SetCursorPos(start + new Vector2(index * (columnWidth + gap), 0f));
            using var id = ImRaii.PushId(index);
            using var group = ImRaii.Group();
            var column = teleportPanelColumns[index];
            DrawTeleportPanelHeading(column.Name, false, columnWidth, rowHeight);

            for (var regionIndex = 0; regionIndex < column.Regions.Count; regionIndex++)
            {
                var region = column.Regions[regionIndex];
                if (regionIndex > 0)
                {
                    ImGui.Dummy(new Vector2(columnWidth, gap));
                }
                var origin = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(origin,
                    origin + new Vector2(columnWidth, (region.Destinations.Count + 1) * rowHeight),
                    ImGui.GetColorU32(Vector4.Lerp(ToolbarTheme.Surface, ToolbarTheme.Text, 0.04f)));
                DrawTeleportPanelHeading(region.Name, true, columnWidth, rowHeight);
                foreach (var destination in region.Destinations)
                {
                    if (!DrawTeleportDestination(destination, columnWidth, rowHeight))
                    {
                        continue;
                    }

                    if (teleportService.TryTeleport(destination.AetheryteID, destination.SubIndex))
                    {
                        ClosePopup();
                        return;
                    }

                    OmniNotifier.Chat(OmniLoc.Get("Feature.MultiToolbar.TeleportPanel.Unavailable"));
                }
            }
        }
    }

    private void DrawTeleportPanelHeading(string text, bool region, float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var label = EllipsizeToolbarText(text, MathF.Max(1f, width - OmniTheme.Scale(8f)));
        var drawList = ImGui.GetWindowDrawList();
        if (region)
        {
            drawList.AddRectFilled(origin, origin + new Vector2(width, height),
                ImGui.GetColorU32(Vector4.Lerp(ToolbarTheme.Surface, ToolbarTheme.Text, 0.12f)));
        }
        else
        {
            drawList.AddLine(origin + new Vector2(0f, height - 1f),
                origin + new Vector2(width, height - 1f), ImGui.GetColorU32(ToolbarTheme.Primary));
        }
        drawList.AddText(origin + new Vector2(MathF.Max(0f, (width - ImGui.CalcTextSize(label).X) * 0.5f),
            (height - ImGui.GetTextLineHeight()) * 0.5f), ImGui.GetColorU32(ImGuiCol.Text), label);
        ImGui.Dummy(new Vector2(width, height));
    }

    private bool DrawTeleportDestination(TeleportPanelDestination destination, float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var iconSize = ImGui.GetTextLineHeight();
        var clicked = ImGui.Selectable($"##teleport_{destination.AetheryteID}_{destination.SubIndex}",
            false, ImGuiSelectableFlags.None, new Vector2(width, height));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            OmniControls.HelpTooltip(destination.Name);
        }

        var padding = OmniTheme.Scale(3f);
        var cost = $"{destination.Cost.ToString("N0", CultureInfo.CurrentCulture)} {SeIconChar.Gil.ToIconString()}";
        var costWidth = ImGui.CalcTextSize(cost).X;
        var textX = iconSize + padding * 2f;
        var nameWidth = MathF.Max(1f, width - textX - costWidth - padding * 2f);
        var textY = (height - ImGui.GetTextLineHeight()) * 0.5f;
        DrawTeleportIcon(destination.IconPart, origin + new Vector2(padding, (height - iconSize) * 0.5f), iconSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(origin, origin + new Vector2(width, height), true);
        drawList.AddText(origin + new Vector2(textX, textY), ImGui.GetColorU32(ImGuiCol.Text),
            EllipsizeToolbarText(destination.Name, nameWidth));
        drawList.AddText(origin + new Vector2(width - costWidth - padding, textY), ImGui.GetColorU32(ImGuiCol.TextDisabled), cost);
        drawList.PopClipRect();
        return clicked;
    }

    private static uint GetTeleportIconPart(uint region, uint territory) => territory switch
    {
        819 => 8,
        820 => 9,
        958 => 11,
        1186 or 1191 => 14,
        _ => region switch
        {
            22 => 0,
            23 => 1,
            24 => 2,
            25 => 4,
            2400 => 6,
            2402 => 7,
            3703 => 10,
            3702 => 12,
            4500 or 4501 => 13,
            _ => 3,
        },
    };

    private void DrawTeleportIcon(uint partID, Vector2 position, float size)
    {
        if (teleportPanelUld is not { } uld)
        {
            return;
        }

        foreach (var parts in uld.Parts)
        {
            if (parts.Id != 16 || partID >= parts.Parts.Length)
            {
                continue;
            }

            var part = parts.Parts[partID];
            foreach (var asset in uld.AssetData)
            {
                if (asset.Id != part.TextureId)
                {
                    continue;
                }

                var texture = DalamudServices.TextureProvider.GetFromGame(new string(asset.Path).TrimEnd('\0')).GetWrapOrDefault();
                if (texture is not null)
                {
                    var textureSize = new Vector2(texture.Width, texture.Height);
                    ImGui.GetWindowDrawList().AddImage(texture.Handle, position, position + new Vector2(size),
                        new Vector2(part.U, part.V) / textureSize,
                        new Vector2(part.U + part.W, part.V + part.H) / textureSize,
                        ImGui.GetColorU32(Vector4.One));
                }
                return;
            }
            return;
        }
    }
}
