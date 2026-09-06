using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.AetheryteRecord;
using OmenTools.ImGuiOm;
using OmenTools.Info.Game.Enums;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly uint[] WorldMarkerIconIds = [
        61341, 61342, 61343, 61347,
        61344, 61345, 61346, 61348,
    ];
    private long lastFlagBroadcast;

    // 状态直接读取游戏服务，避免工具栏维护重复数据。
    private string? RightWidgetLabel(MultiToolbarWidgetType type)
    {
        return type switch
        {
            MultiToolbarWidgetType.Flag => GetFlagLabel(),
            MultiToolbarWidgetType.Volume => GetVolumeLabel(),
            MultiToolbarWidgetType.MailIndicator => string.Empty,
            MultiToolbarWidgetType.WalkingIndicator => string.Empty,
            MultiToolbarWidgetType.StackedClock => $"LT {DateTime.Now:HH:mm}\nET {GetEorzeaTime():HH:mm}",
            _ => null,
        };
    }

    private FontAwesomeIcon? RightWidgetIcon(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.Flag => FontAwesomeIcon.Flag,
        MultiToolbarWidgetType.Volume => GetVolumeIcon(),
        MultiToolbarWidgetType.MailIndicator => FontAwesomeIcon.Envelope,
        MultiToolbarWidgetType.MarkerControl => FontAwesomeIcon.MapSigns,
        MultiToolbarWidgetType.WalkingIndicator => GetWalkingIcon(),
        MultiToolbarWidgetType.StackedClock => null,
        MultiToolbarWidgetType.ToolbarPin => config.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
        _ => null,
    };

    private uint RightWidgetGameIcon(MultiToolbarWidgetType type) => type == MultiToolbarWidgetType.Flag
        ? GetFlagIconID()
        : 0;

    private static bool IsRightWidgetVisible(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.Flag => IsFlagSet(),
        MultiToolbarWidgetType.MailIndicator => GetUnreadMailCount() > 0,
        MultiToolbarWidgetType.WalkingIndicator => IsWalking(),
        _ => true,
    };

    private unsafe bool TryHandleRightWidgetInteraction(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.MailIndicator:
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(string.Format(OmniLoc.Get("Feature.MultiToolbar.UnreadMailCount"), GetUnreadMailCount()));
                }
                return true;
            case MultiToolbarWidgetType.WalkingIndicator:
                var control = Control.Instance();
                if (ImGui.IsItemClicked() && control != null)
                {
                    control->IsWalking = !control->IsWalking;
                }
                return true;
            case MultiToolbarWidgetType.Flag:
                if (!IsFlagSet() || !ImGui.IsItemHovered())
                {
                    return true;
                }
                var aetheryte = GetFlagAetheryte();
                ImGui.SetTooltip(aetheryte is null
                    ? OmniLoc.Get("Feature.MultiToolbar.FlagUnavailable")
                    : string.Format(OmniLoc.Get("Feature.MultiToolbar.FlagTeleport"), aetheryte.Name, aetheryte.Cost));
                if (ImGui.IsItemClicked() && aetheryte is not null)
                {
                    teleportService.TryTeleport(aetheryte.RowID);
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    AgentMap.Instance()->FlagMarkerCount = 0;
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Middle) && !string.IsNullOrWhiteSpace(config.FlagChatPrefix) &&
                    Environment.TickCount64 - lastFlagBroadcast >= 3000)
                {
                    lastFlagBroadcast = Environment.TickCount64;
                    ExecuteCommand($"{config.FlagChatPrefix} <flag>");
                }
                return true;
            case MultiToolbarWidgetType.Volume:
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    var system = DService.Instance().GameConfig.System;
                    system.Set("IsSndMaster", !system.GetBool("IsSndMaster"));
                }
                return false;
            default:
                return false;
        }
    }

    // 返回 true 表示已绘制该组件弹窗。
    private bool DrawRightWidgetPopup(MultiToolbarWidgetType type)
    {
        switch (type)
        {
            case MultiToolbarWidgetType.Volume:
                DrawVolumePopup();
                return true;
            case MultiToolbarWidgetType.MarkerControl:
                DrawMarkerPopup();
                return true;
            case MultiToolbarWidgetType.StackedClock:
                DrawClockPopup();
                return true;
            case MultiToolbarWidgetType.ToolbarPin:
                ImGuiOm.TextIcon(
                    config.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
                    config.IsLocked ? "工具栏已固定" : "工具栏未固定");
                return true;
            default:
                return false;
        }
    }

    private void DrawVolumePopup()
    {
        var system = DService.Instance().GameConfig.System;
        DrawVolumePresets();
        using var table = ImRaii.Table("##multiToolbarVolumeTable", VolumeChannels.Length, ImGuiTableFlags.SizingStretchSame);
        if (table)
        {
            foreach (var channel in VolumeChannels)
            {
                var value = (int)Math.Clamp(system.GetUInt(channel.Volume), 0u, 100u);
                var muted = system.GetBool(channel.Mute);
                ImGui.PushID(channel.Volume);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(channel.Label);
                ImGui.TextUnformatted($"{value}%");
                if (ImGui.VSliderInt("##value", new Vector2(ImGui.GetContentRegionAvail().X, ScaleToolbar(140f)), ref value, 0, 100, string.Empty))
                {
                    system.Set(channel.Volume, (uint)value);
                    system.Set(channel.Mute, false);
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    SaveActiveVolumePreset();
                }
                if (OmniControls.IconButton(
                        "##mute",
                        muted ? FontAwesomeIcon.VolumeMute : value switch
                        {
                            0 => FontAwesomeIcon.VolumeOff,
                            < 50 => FontAwesomeIcon.VolumeDown,
                            _ => FontAwesomeIcon.VolumeUp,
                        },
                        false,
                        OmniLoc.Get(muted ? "Feature.MultiToolbar.Unmute" : "Feature.MultiToolbar.Mute")))
                {
                    system.Set(channel.Mute, !muted);
                    SaveActiveVolumePreset();
                }
                var background = system.GetBool(channel.Background);
                if (OmniControls.IconButton("##background",
                        background ? FontAwesomeIcon.PlusCircle : FontAwesomeIcon.Minus,
                        background, OmniLoc.Get("Feature.MultiToolbar.BackgroundAudio")))
                {
                    system.Set(channel.Background, !background);
                    SaveActiveVolumePreset();
                }
                ImGui.PopID();
            }
        }
    }

    private void DrawVolumePresets()
    {
        while (config.VolumePresets.Count < 5)
        {
            config.VolumePresets.Add(new MultiToolbarVolumePreset
            {
                Name = $"{OmniLoc.Get("Feature.MultiToolbar.VolumePreset")} #{config.VolumePresets.Count + 1}",
            });
        }
        for (var i = 0; i < 5; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
            }
            var preset = config.VolumePresets[i];
            using var presetID = ImRaii.PushId(i);
            var selected = config.ActiveVolumePreset == i;
            var size = new Vector2(ScaleToolbar(28f));
            using var color = ImRaii.PushColor(ImGuiCol.Button,
                selected ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
            var clicked = ImageHelper.GetGameIcon((uint)(66162 + i)) is { } icon
                ? ImGui.ImageButton(icon.Handle, size)
                : ImGui.Button($"{i + 1}##volumePreset", size);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(preset.Name);
            }
            if (clicked)
            {
                SaveActiveVolumePreset();
                config.ActiveVolumePreset = selected ? -1 : i;
                if (!selected)
                {
                    var system = DService.Instance().GameConfig.System;
                    foreach (var channel in VolumeChannels)
                    {
                        if (preset.Volumes.TryGetValue(channel.Volume, out var volume))
                        {
                            system.Set(channel.Volume, Math.Clamp(volume, 0u, 100u));
                        }
                        if (preset.Toggles.TryGetValue(channel.Mute, out var muted))
                        {
                            system.Set(channel.Mute, muted);
                        }
                        if (preset.Toggles.TryGetValue(channel.Background, out var background))
                        {
                            system.Set(channel.Background, background);
                        }
                    }
                    SaveActiveVolumePreset();
                }
                saveConfig();
            }
        }
        if (config.ActiveVolumePreset >= 0 && config.ActiveVolumePreset < config.VolumePresets.Count)
        {
            var preset = config.VolumePresets[config.ActiveVolumePreset];
            var name = preset.Name;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##volumePresetName", ref name, 128))
            {
                preset.Name = name;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                saveConfig();
            }
        }
        ImGui.Separator();
    }

    private void SaveActiveVolumePreset()
    {
        if (config.ActiveVolumePreset < 0 || config.ActiveVolumePreset >= config.VolumePresets.Count)
        {
            return;
        }
        var preset = config.VolumePresets[config.ActiveVolumePreset];
        var system = DService.Instance().GameConfig.System;
        foreach (var channel in VolumeChannels)
        {
            preset.Volumes[channel.Volume] = system.GetUInt(channel.Volume);
            preset.Toggles[channel.Mute] = system.GetBool(channel.Mute);
            preset.Toggles[channel.Background] = system.GetBool(channel.Background);
        }
        saveConfig();
    }

    private static void DrawClockPopup()
    {
        ImGuiOm.TextIcon(FontAwesomeIcon.Clock, $"本地时间  {DateTime.Now:HH:mm:ss}");
        ImGuiOm.TextIcon(FontAwesomeIcon.Clock, $"服务器时间  {GetServerTime():HH:mm:ss}");
        ImGuiOm.TextIcon(FontAwesomeIcon.Clock, $"艾欧泽亚时间  {GetEorzeaTime():HH:mm:ss}");
    }

    private void DrawMarkerPopup()
    {
        if (ImGui.Selectable(OmniLoc.Get("Feature.MultiToolbar.WorldMarkerOverlay"),
                config.ShowWorldMarkerOverlay, ImGuiSelectableFlags.DontClosePopups))
        {
            config.ShowWorldMarkerOverlay = !config.ShowWorldMarkerOverlay;
            saveConfig();
        }
    }

    private void DrawWorldMarkerOverlay()
    {
        if (!config.ShowWorldMarkerOverlay || !config.Widgets.Any(widget => widget.Enabled && widget.Type == MultiToolbarWidgetType.MarkerControl))
        {
            return;
        }

        var drawList = ImGui.GetForegroundDrawList();
        var iconSize = ScaleToolbar(30f);
        foreach (var point in Enum.GetValues<FieldMarkerPoint>())
        {
            if (!point.IsActive() || !DService.Instance().GameGUI.WorldToScreen(point.GetPosition(), out var screenPosition))
            {
                continue;
            }

            var icon = ImageHelper.GetGameIcon(WorldMarkerIconIds[(int)point]);
            if (icon is not null)
            {
                var halfSize = iconSize * 0.5f;
                drawList.AddImage(
                    icon.Handle,
                    screenPosition - new Vector2(halfSize),
                    screenPosition + new Vector2(halfSize));
            }

            var label = GetWorldMarkerName(point);
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(
                screenPosition + new Vector2(-textSize.X * 0.5f, iconSize * 0.5f + ScaleToolbar(2f)),
                ImGui.GetColorU32(OmniTheme.Tokens.Text),
                label);
        }
    }

    private static string GetWorldMarkerName(FieldMarkerPoint point) => (int)point switch
    {
        0 => "A",
        1 => "B",
        2 => "C",
        3 => "D",
        4 => "1",
        5 => "2",
        6 => "3",
        7 => "4",
        _ => point.ToString(),
    };


    private unsafe string GetFlagLabel()
    {
        if (!IsFlagSet())
        {
            return OmniLoc.Get("Feature.MultiToolbar.WidgetFlag");
        }

        var marker = GetFlagMarker();
        if (!LuminaGetter.TryGetRow<Lumina.Excel.Sheets.Map>(marker.MapId, out var map))
        {
            return OmniLoc.Get("Feature.MultiToolbar.WidgetFlag");
        }
        var position = PositionHelper.WorldToMap(new Vector2(marker.XFloat, marker.YFloat), map);
        return $"{map.PlaceName.Value.Name} <{position.X:F1}, {position.Y:F1}>";
    }

    private static unsafe AetheryteRecord? GetFlagAetheryte()
    {
        var marker = GetFlagMarker();
        AetheryteRecord? nearest = null;
        var distance = float.MaxValue;
        foreach (var record in AetheryteRecordManager.Instance().AllRecords)
        {
            if (!record.IsAetheryte || record.MapID != marker.MapId || !record.IsUnlocked())
            {
                continue;
            }
            var candidate = Vector2.DistanceSquared(new Vector2(record.Position.X, record.Position.Z),
                new Vector2(marker.XFloat, marker.YFloat));
            if (candidate < distance)
            {
                nearest = record;
                distance = candidate;
            }
        }
        return nearest;
    }

    private string GetVolumeLabel()
    {
        const string configName = "SoundMaster";
        var system = DService.Instance().GameConfig.System;
        return $"{system.GetUInt(configName)}%";
    }

    private FontAwesomeIcon GetVolumeIcon()
    {
        var system = DService.Instance().GameConfig.System;
        if (system.GetBool("IsSndMaster"))
        {
            return FontAwesomeIcon.VolumeMute;
        }

        return system.GetUInt("SoundMaster") switch
        {
            0 => FontAwesomeIcon.VolumeOff,
            < 50 => FontAwesomeIcon.VolumeDown,
            _ => FontAwesomeIcon.VolumeUp,
        };
    }

    private static unsafe FontAwesomeIcon GetWalkingIcon()
    {
        var control = Control.Instance();
        return control != null && control->IsWalking ? FontAwesomeIcon.Walking : FontAwesomeIcon.Running;
    }

    private static unsafe bool IsWalking()
    {
        var control = Control.Instance();
        return control != null && control->IsWalking;
    }

    private static unsafe uint GetFlagIconID()
    {
        if (!IsFlagSet())
        {
            return 0;
        }

        return GetFlagMarker().MapMarker.IconId;
    }

    private static unsafe bool IsFlagSet()
    {
        var map = AgentMap.Instance();
        return map != null && map->FlagMarkerCount != 0;
    }

    private static unsafe FlagMapMarker GetFlagMarker() => AgentMap.Instance()->FlagMapMarkers[0];

    private static unsafe uint GetUnreadMailCount()
    {
        var info = InfoModule.Instance();
        if (info is null)
        {
            return 0;
        }

        var proxy = (InfoProxyLetterCount*)info->GetInfoProxyById(InfoProxyId.Letter);
        return proxy is null ? 0u : proxy->NumLetters;
    }

    private static DateTime GetServerTime()
    {
        var serverTime = Framework.GetServerTime();
        return TimeFromSeconds(serverTime);
    }

    private static unsafe DateTime GetEorzeaTime()
    {
        var framework = Framework.Instance();
        return framework is null ? DateTime.MinValue : TimeFromSeconds(framework->ClientTime.EorzeaTime);
    }

    private static DateTime TimeFromSeconds(long seconds) => new(
        1,
        1,
        1,
        (int)(seconds / 3600 % 24),
        (int)(seconds / 60 % 60),
        (int)(seconds % 60));

    private static readonly (string Label, string Volume, string Mute, string Background)[] VolumeChannels =
    [
        ("Master", "SoundMaster", "IsSndMaster", "IsSoundAlways"),
        ("BGM", "SoundBgm", "IsSndBgm", "IsSoundBgmAlways"),
        ("SFX", "SoundSe", "IsSndSe", "IsSoundSeAlways"),
        ("VOC", "SoundVoice", "IsSndVoice", "IsSoundVoiceAlways"),
        ("AMB", "SoundEnv", "IsSndEnv", "IsSoundEnvAlways"),
        ("SYS", "SoundSystem", "IsSndSystem", "IsSoundSystemAlways"),
        ("PERF", "SoundPerform", "IsSndPerform", "IsSoundPerformAlways"),
    ];

    [StructLayout(LayoutKind.Explicit)]
    private struct InfoProxyLetterCount
    {
        [FieldOffset(0x26)] public byte NumLetters;
    }
}

public sealed class MultiToolbarVolumePreset
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, uint> Volumes { get; set; } = [];
    public Dictionary<string, bool> Toggles { get; set; } = [];
}
