using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Game.Text;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Info.Game.AetheryteRecord;
using OmenTools.ImGuiOm;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{

    private string? RightWidgetLabel(MultiToolbarWidgetType type)
    {
        return type switch
        {
            MultiToolbarWidgetType.Flag => GetFlagLabel(),
            MultiToolbarWidgetType.Volume => GetVolumeLabel(),
            MultiToolbarWidgetType.MailIndicator => string.Empty,
            MultiToolbarWidgetType.WalkingIndicator => string.Empty,
            MultiToolbarWidgetType.StackedClock => $"{SeIconChar.LocalTimeEn.ToIconString()} {DateTime.Now:HH:mm}\n{SeIconChar.EorzeaTimeEn.ToIconString()} {GetEorzeaTime():HH:mm}",
            _ => null,
        };
    }

    private FontAwesomeIcon? RightWidgetIcon(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.Flag => null,
        MultiToolbarWidgetType.Volume => GetVolumeIcon(),
        MultiToolbarWidgetType.MailIndicator => FontAwesomeIcon.Envelope,
        MultiToolbarWidgetType.MarkerControl => FontAwesomeIcon.MapSigns,
        MultiToolbarWidgetType.WalkingIndicator => GetWalkingIcon(),
        MultiToolbarWidgetType.StackedClock => null,
        MultiToolbarWidgetType.ToolbarPin => config.IsLocked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
        _ => null,
    };

    private uint RightWidgetGameIcon(MultiToolbarWidgetType type) => type switch
    {
        MultiToolbarWidgetType.Flag => 60561,
        MultiToolbarWidgetType.MailIndicator => 60551,
        _ => 0,
    };

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
                    OmniControls.HelpTooltip(string.Format(OmniLoc.Get("Feature.MultiToolbar.UnreadMailCount"), GetUnreadMailCount()));
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
                OmniControls.HelpTooltip(aetheryte is null
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

    private readonly Dictionary<string, uint> playerVolumeBeforeMute = [];

    private void DrawVolumePopup()
    {
        DrawVolumePresets();
        DrawVolumeChannels();
        DrawVolumeOptions();
    }

    private void DrawVolumeChannels()
    {
        var system = DService.Instance().GameConfig.System;
        var buttonSize = OmniTheme.SmallButtonSize().Y;
        using var table = ImRaii.Table("##multiToolbarVolumeTable", 3, ImGuiTableFlags.SizingStretchProp);
        if (table)
        {
            ImGui.TableSetupColumn("##slider", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##mute", ImGuiTableColumnFlags.WidthFixed, buttonSize);
            ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("乐器演奏").X + OmniTheme.Scale(8f));
            foreach (var channel in VolumeChannels)
            {
                if (channel.Volume == "SoundPlayer")
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(OmniLoc.Get("Feature.MultiToolbar.PlayerAudio"));
                }
                var value = (int)Math.Clamp(system.GetUInt(channel.Volume), 0u, 100u);
                var muted = channel.Mute is { } muteKey ? system.GetBool(muteKey) : value == 0;
                ImGui.PushID(channel.Volume);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.SliderInt("##value", ref value, 0, 100, "%d%%"))
                {
                    system.Set(channel.Volume, (uint)value);
                    if (channel.Mute is { } key)
                    {
                        system.Set(key, false);
                    }
                    muted = channel.Mute is null && value == 0;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    SaveActiveVolumePreset();
                }
                ImGui.TableNextColumn();
                if (OmniControls.IconButton(
                        "##mute",
                        muted ? FontAwesomeIcon.VolumeMute : value switch
                        {
                            0 => FontAwesomeIcon.VolumeOff,
                            < 50 => FontAwesomeIcon.VolumeDown,
                            _ => FontAwesomeIcon.VolumeUp,
                        },
                        false,
                        new Vector2(buttonSize),
                        OmniLoc.Get(muted ? "Feature.MultiToolbar.Unmute" : "Feature.MultiToolbar.Mute")))
                {
                    if (channel.Mute is { } key)
                    {
                        system.Set(key, !muted);
                    }
                    else if (value > 0)
                    {
                        playerVolumeBeforeMute[channel.Volume] = (uint)value;
                        system.Set(channel.Volume, 0u);
                    }
                    else
                    {
                        system.Set(channel.Volume, playerVolumeBeforeMute.GetValueOrDefault(channel.Volume, 100u));
                    }
                    SaveActiveVolumePreset();
                }
                using (var menu = ImRaii.ContextPopupItem("##backgroundAudio"))
                {
                    if (menu)
                    {
                        if (channel.Background is not null)
                        {
                            var background = system.GetBool(channel.Background);
                            if (ImGui.Checkbox(OmniLoc.Get("Feature.MultiToolbar.BackgroundAudio"), ref background))
                            {
                                system.Set(channel.Background, background);
                                SaveActiveVolumePreset();
                            }
                        }
                    }
                }
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(OmniLoc.Get($"Feature.MultiToolbar.Audio{channel.Label}"));
                ImGui.PopID();
            }
        }
    }

    private void DrawVolumeOptions()
    {
        var system = DService.Instance().GameConfig.System;
        ImGui.Separator();
        foreach (var key in MusicOptions)
        {
            var enabled = system.GetBool(key);
            if (OmniControls.Checkbox(OmniLoc.Get($"Feature.MultiToolbar.{key}"), ref enabled))
            {
                system.Set(key, enabled);
                SaveActiveVolumePreset();
            }
            if (key == "SoundHousing")
            {
                ImGui.SameLine();
                OmniControls.HelpIcon(OmniLoc.Get("Feature.MultiToolbar.SoundHousingHint"));
            }
        }
        var muteInBackground = !system.GetBool("IsSoundAlways");
        if (OmniControls.Checkbox(OmniLoc.Get("Feature.MultiToolbar.MuteInBackground"), ref muteInBackground))
        {
            system.Set("IsSoundAlways", !muteInBackground);
            SaveActiveVolumePreset();
        }
    }

    private static readonly string[] MusicOptions = ["SoundChocobo", "SoundFieldBattle", "SoundHousing"];

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
            var size = new Vector2(OmniTheme.Scale(28f));
            using var color = ImRaii.PushColor(ImGuiCol.Button,
                selected ? ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive] : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
            var clicked = ImageHelper.GetGameIcon((uint)(66162 + i)) is { } icon
                ? ImGui.ImageButton(icon.Handle, size)
                : ImGui.Button($"{i + 1}##volumePreset", size);
            if (ImGui.IsItemHovered())
            {
                OmniControls.HelpTooltip(preset.Name);
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
                        if (channel.Mute is not null && preset.Toggles.TryGetValue(channel.Mute, out var muted))
                        {
                            system.Set(channel.Mute, muted);
                        }
                        if (channel.Background is not null && preset.Toggles.TryGetValue(channel.Background, out var background))
                        {
                            system.Set(channel.Background, background);
                        }
                    }
                    foreach (var key in MusicOptions)
                    {
                        if (preset.Toggles.TryGetValue(key, out var enabled))
                        {
                            system.Set(key, enabled);
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
            if (channel.Mute is not null)
            {
                preset.Toggles[channel.Mute] = system.GetBool(channel.Mute);
            }
            if (channel.Background is not null)
            {
                preset.Toggles[channel.Background] = system.GetBool(channel.Background);
            }
        }
        foreach (var key in MusicOptions)
        {
            preset.Toggles[key] = system.GetBool(key);
        }
        saveConfig();
    }

    private static void DrawClockPopup()
    {
        ImGui.TextUnformatted($"{SeIconChar.LocalTimeEn.ToIconString()} {DateTime.Now:HH:mm:ss}");
        ImGui.TextUnformatted($"{SeIconChar.ServerTimeEn.ToIconString()} {GetServerTime():HH:mm:ss}");
        ImGui.TextUnformatted($"{SeIconChar.EorzeaTimeEn.ToIconString()} {GetEorzeaTime():HH:mm:ss}");
    }



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
        return $"{map.PlaceName.Value.Name}\n<{position.X:F1}, {position.Y:F1}>";
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

    private static readonly (string Label, string Volume, string? Mute, string? Background)[] VolumeChannels =
    [
        ("Master", "SoundMaster", "IsSndMaster", "IsSoundAlways"),
        ("BGM", "SoundBgm", "IsSndBgm", "IsSoundBgmAlways"),
        ("SFX", "SoundSe", "IsSndSe", "IsSoundSeAlways"),
        ("VOC", "SoundVoice", "IsSndVoice", "IsSoundVoiceAlways"),
        ("SYS", "SoundSystem", "IsSndSystem", "IsSoundSystemAlways"),
        ("AMB", "SoundEnv", "IsSndEnv", "IsSoundEnvAlways"),
        ("PERF", "SoundPerform", "IsSndPerform", "IsSoundPerformAlways"),
        ("Self", "SoundPlayer", null, null),
        ("Party", "SoundParty", null, null),
        ("Other", "SoundOther", null, null),
    ];

    [StructLayout(LayoutKind.Explicit)]
    private struct InfoProxyLetterCount
    {
        [FieldOffset(0x26)] public byte NumLetters;
    }
}

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public sealed class MultiToolbarVolumePreset
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, uint> Volumes { get; set; } = [];
    public Dictionary<string, bool> Toggles { get; set; } = [];
}
