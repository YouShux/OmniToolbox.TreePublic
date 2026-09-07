using Dalamud.Interface;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using WeatherRow = Lumina.Excel.Sheets.Weather;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private long nextInventorySpaceRefresh;
    private (int Used, int Total) inventorySpace;

    private unsafe string? InformationWidgetLabel(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.Clock:
                return config.ClockTimeSource switch
                {
                    1 => $"{SeIconChar.LocalTimeEn.ToIconString()} {DateTime.Now:HH:mm}",
                    2 => $"{SeIconChar.ServerTimeEn.ToIconString()} {GetServerTime():HH:mm}",
                    _ => $"{SeIconChar.EorzeaTimeEn.ToIconString()} {GetEorzeaTime():HH:mm}",
                };
            case MultiToolbarWidgetType.FpsCounter:
                return $"{(int)Framework.Instance()->FrameRate} FPS";
            case MultiToolbarWidgetType.Coordinates:
                var coords = GetPlayerMapCoordinates();
                return $"{coords.X:F1} / {coords.Y:F1}";
            case MultiToolbarWidgetType.Location:
                return GameState.MapData.PlaceName.Value.Name.ExtractText();
            case MultiToolbarWidgetType.WorldName:
                return DService.Instance().ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ExtractText() ?? string.Empty;
            case MultiToolbarWidgetType.InventorySpace:
                var (used, total) = GetToolbarInventorySpace();
                return $"{used} / {total}";
            case MultiToolbarWidgetType.ExperienceBar:
                var hud = AgentHUD.Instance();
                return hud == null || hud->ExpNeededExperience == 0
                    ? string.Empty
                    : $"Lv. {LocalPlayerState.GetClassJobLevel(LocalPlayerState.ClassJob, false)}    {100d * hud->ExpCurrentExperience / hud->ExpNeededExperience:0.0}%";
            case MultiToolbarWidgetType.Weather:
                var remaining = GetNextWeatherSeconds();
                return $"{GameState.WeatherData.Name.ExtractText()}\n{TimeSpan.FromSeconds(remaining):mm\\:ss}";
            case MultiToolbarWidgetType.SanctuaryIndicator:
                return string.Empty;
            default:
                return null;
        }
    }

    private static FontAwesomeIcon? InformationWidgetIcon(MultiToolbarWidgetType type) =>
        type == MultiToolbarWidgetType.SanctuaryIndicator ? FontAwesomeIcon.Moon : null;

    private uint InformationWidgetGameIcon(MultiToolbarWidgetType type)
    {
        if (type == MultiToolbarWidgetType.Weather)
        {
            return (uint)GameState.WeatherData.Icon;
        }
        if (type == MultiToolbarWidgetType.InventorySpace)
        {
            var (used, total) = GetToolbarInventorySpace();
            return total - used <= 1 ? 60074u : total - used <= 6 ? 60073u : 2u;
        }
        return 0;
    }

    private unsafe bool IsInformationWidgetVisible(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.SanctuaryIndicator => TerritoryInfo.Instance()->InSanctuary,
        MultiToolbarWidgetType.InventorySpace => GetToolbarInventorySpace().Total > 0,
        MultiToolbarWidgetType.ExperienceBar => AgentHUD.Instance() != null &&
            (AgentHUD.Instance()->ExpFlags & AgentHudExpFlag.MaxLevel) == 0 && AgentHUD.Instance()->ExpNeededExperience > 0,
        MultiToolbarWidgetType.Coordinates or MultiToolbarWidgetType.Location or MultiToolbarWidgetType.Weather =>
            DService.Instance().ObjectTable.LocalPlayer != null && GameState.Map != 0,
        MultiToolbarWidgetType.WorldName => DService.Instance().ObjectTable.LocalPlayer != null,
        MultiToolbarWidgetType.FpsCounter => Framework.Instance() != null && Framework.Instance()->FrameRate < 1000,
        _ => true,
    };

    private unsafe bool TryHandleInformationWidgetInteraction(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.Clock:
                if (ImGui.IsItemClicked())
                {
                    config.ClockTimeSource = (config.ClockTimeSource + 1) % 3;
                    saveConfig();
                }
                return true;
            case MultiToolbarWidgetType.Coordinates:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    var coords = GetPlayerMapCoordinates();
                    ImGui.SetClipboardText($"{coords.X:F1}, {coords.Y:F1}");
                }
                return true;
            case MultiToolbarWidgetType.Location:
                OmniControls.HelpTooltip(OmniLoc.Get("Feature.MultiToolbar.LocationInteractionHelp"));
                if (ImGui.IsItemClicked())
                {
                    UIModule.Instance()->ExecuteMainCommand(16);
                }
                else if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ExecuteCommand("/omni DisplayIDInformation");
                }
                return true;
            case MultiToolbarWidgetType.InventorySpace:
                if (ImGui.IsItemClicked())
                {
                    ExecuteCommand("/inventory");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ExecuteCommand("/keyitem");
                }
                return true;
            case MultiToolbarWidgetType.ExperienceBar:
                DrawToolbarExperience();
                return true;
            case MultiToolbarWidgetType.FpsCounter:
            case MultiToolbarWidgetType.SanctuaryIndicator:
            case MultiToolbarWidgetType.WorldName:
                return true;
            default:
                return false;
        }
    }

    private static unsafe void DrawToolbarExperience()
    {
        var hud = AgentHUD.Instance();
        if (hud == null || hud->ExpNeededExperience == 0)
        {
            return;
        }
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        min.X += 4;
        max.X -= 4;
        min.Y = max.Y - 4;
        max.Y -= 1;
        var width = max.X - min.X;
        var progress = Math.Clamp((float)hud->ExpCurrentExperience / hud->ExpNeededExperience, 0, 1);
        var rested = Math.Clamp((float)hud->ExpRestedExperience / hud->ExpNeededExperience, 0, 1 - progress);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));
        drawList.AddRectFilled(min, new(min.X + width * progress, max.Y), ImGui.GetColorU32(ImGuiCol.PlotHistogram));
        drawList.AddRectFilled(new(min.X + width * progress, min.Y), new(min.X + width * (progress + rested), max.Y), ImGui.GetColorU32(ImGuiCol.PlotHistogramHovered));
        if (hovered)
        {
            OmniControls.HelpTooltip($"{hud->ExpCurrentExperience:N0} / {hud->ExpNeededExperience:N0} (+{hud->ExpRestedExperience:N0})");
        }
    }

    private static unsafe bool DrawInformationWidgetPopup(MultiToolbarWidgetType type)
    {
        if (type != MultiToolbarWidgetType.Weather)
        {
            return false;
        }
        ImGui.TextUnformatted(GameState.WeatherData.Name.ExtractText());
        ImGui.TextDisabled(GameState.MapData.PlaceName.Value.Name.ExtractText());
        ImGui.Separator();
        var manager = WeatherManager.Instance();
        if (manager == null)
        {
            return true;
        }
        for (var i = 1; i <= 4; i++)
        {
            var weatherID = manager->GetWeatherForDaytime((ushort)GameState.TerritoryType, i);
            if (!LuminaGetter.TryGetRow<WeatherRow>(weatherID, out var weather))
            {
                continue;
            }
            if (ImageHelper.GetGameIcon((uint)weather.Icon) is { } icon)
            {
                ImGui.Image(icon.Handle, new Vector2(ImGui.GetTextLineHeightWithSpacing() * 2));
                ImGui.SameLine();
            }
            ImGui.BeginGroup();
            ImGui.TextUnformatted(weather.Name.ExtractText());
            ImGui.TextDisabled($"{TimeSpan.FromSeconds(GetNextWeatherSeconds() + (i - 1) * 1400):hh\\:mm\\:ss}");
            ImGui.EndGroup();
        }
        return true;
    }

    private static Vector2 GetPlayerMapCoordinates()
    {
        var player = DService.Instance().ObjectTable.LocalPlayer;
        return player == null ? default : PositionHelper.WorldToMap(new Vector2(player.Position.X, player.Position.Z), GameState.MapData);
    }

    private static unsafe long GetNextWeatherSeconds() => 1400 - Framework.GetServerTime() % 1400;

    private unsafe (int Used, int Total) GetToolbarInventorySpace()
    {
        var now = Environment.TickCount64;
        if (now < nextInventorySpaceRefresh)
        {
            return inventorySpace;
        }
        nextInventorySpaceRefresh = now + 500;
        inventorySpace = default;
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return default;
        }
        var total = 0;
        for (var i = 0; i < 4; i++)
        {
            var container = manager->GetInventoryContainer((InventoryType)i);
            if (container == null || !container->IsLoaded)
            {
                return default;
            }
            total += container->Size;
        }
        inventorySpace = (total - (int)manager->GetEmptySlotsInBag(), total);
        return inventorySpace;
    }
}
