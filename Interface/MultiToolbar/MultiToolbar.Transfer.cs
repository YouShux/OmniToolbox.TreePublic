using System.Linq;
using Dalamud.Interface.ImGuiNotification;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.Notifications;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private static readonly JsonSerializerSettings TransferSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 32
    };

    private void MigrateMultiDock()
    {
        if (config.MultiDockMigrated || getMultiDockConfig?.Invoke() is not { } source)
        {
            return;
        }

        var commands = new List<MultiToolbarWidgetConfig>(config.Commands);
        var seen = commands.Select(command => command.Command.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source["Items"]?.OfType<JObject>() ?? [])
        {
            var type = item["Type"]?.ToString();
            if (type is not ("2" or "Command") || !TryNormalizeCommand(item.Value<string>("Command"), out var command) || !seen.Add(command))
            {
                continue;
            }
            commands.Add(new()
            {
                Type = MultiToolbarWidgetType.CustomButton,
                Name = item.Value<string>("Name") ?? string.Empty,
                Command = command,
                GameIconID = item["Icon"]?.Value<uint?>("GameIconID") ?? 0,
                IconFilePath = item["Icon"]?.Value<string>("FilePath") ?? string.Empty,
                IconGlyph = item["Icon"]?.Value<string>("SeIconChar") ?? string.Empty
            });
        }
        var hidden = new HashSet<string>(config.HiddenWindows, StringComparer.OrdinalIgnoreCase);
        foreach (var token in source["HiddenWindows"]?.Values<string>() ?? [])
        {
            if (NormalizeHiddenWindowName(token) is { Length: > 0 } name && CanManageHiddenWindow(name))
            {
                hidden.Add(name);
            }
        }
        config.Commands = commands;
        KeepBuiltInCommandsFirst();
        config.HiddenWindows = hidden;
        config.MultiDockMigrated = true;
        saveConfig();
    }

    private void KeepBuiltInCommandsFirst()
    {
        var builtIns = new MultiToolbarConfig().Commands;
        for (var index = builtIns.Count - 1; index >= 0; index--)
        {
            var template = builtIns[index];
            var existing = config.Commands.FirstOrDefault(item => item.Command.Trim().Equals(template.Command, StringComparison.OrdinalIgnoreCase));
            config.Commands.RemoveAll(item => item.Command.Trim().Equals(template.Command, StringComparison.OrdinalIgnoreCase));
            var command = existing ?? template;
            command.Enabled = true;
            config.Commands.Insert(0, command);
        }
    }

    private void MigrateMultiDockPlugins()
    {
        if (config.MultiDockPluginsMigrated || getMultiDockConfig?.Invoke() is not { } source)
        {
            return;
        }
        var seen = new HashSet<string>(config.SelectedPlugins, StringComparer.OrdinalIgnoreCase);
        foreach (var item in source["Items"]?.OfType<JObject>() ?? [])
        {
            var name = item.Value<string>("PluginInternalName")?.Trim();
            if (item["Type"]?.ToString() is ("0" or "Plugin") &&
                !string.IsNullOrEmpty(name) && seen.Add(name))
            {
                config.SelectedPlugins.Add(name);
            }
        }
        config.MultiDockPluginsMigrated = true;
        saveConfig();
    }

    private bool DrawConfigTransfer()
    {
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiDock.Export"), false))
        {
            ImGui.SetClipboardText(new JObject
            {
                ["Format"] = "OmniToolbox.MultiToolbar",
                ["Version"] = 2,
                ["Config"] = JObject.FromObject(config, JsonSerializer.Create(TransferSettings))
            }.ToString(Formatting.Indented));
            OmniNotifier.Popup(OmniLoc.Get("MultiToolbarTitle"), OmniLoc.Get("Feature.MultiDock.ExportSuccess"), NotificationType.Success);
        }
        ImGui.SameLine();
        var changed = false;
        if (OmniControls.SmallButton(OmniLoc.Get("Feature.MultiDock.Import"), false))
        {
            try
            {
                var text = ImGui.GetClipboardText();
                if (text.Length > 1024 * 1024)
                {
                    throw new JsonSerializationException("Configuration exceeds the size limit.");
                }
                var snapshot = JsonConvert.DeserializeObject<JObject>(text, TransferSettings);
                if (snapshot?.Value<string>("Format") != "OmniToolbox.MultiToolbar" || snapshot.Value<int>("Version") is not (1 or 2) ||
                    snapshot["Config"] is not JObject payload)
                {
                    throw new JsonSerializationException("Unsupported configuration format.");
                }
                var imported = payload.ToObject<MultiToolbarConfig>(JsonSerializer.Create(TransferSettings));
                if (imported is null || !IsValidBarConfig(imported) || imported.AuxiliaryBars is null ||
                    imported.AuxiliaryBars.Any(bar => bar is null || !IsValidBarConfig(bar) ||
                        string.IsNullOrWhiteSpace(bar.Name) || !Guid.TryParseExact(bar.ID, "N", out _)) ||
                    imported.AuxiliaryBars.Select(bar => bar.ID).Distinct(StringComparer.Ordinal).Count() != imported.AuxiliaryBars.Count)
                {
                    throw new JsonSerializationException("Invalid toolbar configuration.");
                }
                imported.HiddenWindows = imported.HiddenWindows.Select(NormalizeHiddenWindowName)
                    .Where(CanManageHiddenWindow).ToHashSet(StringComparer.OrdinalIgnoreCase);
                imported.MultiDockMigrated = config.MultiDockMigrated;
                imported.SelectedPlugins = imported.SelectedPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                imported.MultiDockPluginsMigrated = payload.ContainsKey(nameof(MultiToolbarConfig.SelectedPlugins)) || config.MultiDockPluginsMigrated;
                RestoreHiddenWindows();
                ReleaseBarResources();
                ReleaseAuxiliaryBars();
                selectedBarID = "main";
                JsonConvert.PopulateObject(JsonConvert.SerializeObject(imported, TransferSettings), config, TransferSettings);
                KeepBuiltInCommandsFirst();
                autoHideOffset = 0f;
                nextMarkerRefresh = 0;
                changed = true;
                OmniNotifier.Popup(OmniLoc.Get("MultiToolbarTitle"), OmniLoc.Get("Feature.MultiDock.ImportSuccess"), NotificationType.Success);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
            {
                OmniNotifier.Popup(OmniLoc.Get("MultiToolbarTitle"), OmniLoc.Get("Feature.MultiToolbar.ImportFailed"), NotificationType.Error);
            }
        }
        return changed;
    }

    private static bool IsValidBarConfig(MultiToolbarBarConfig bar) =>
        bar.Widgets is not null && bar.Commands is not null &&
        bar.SelectedPlugins is not null && !bar.SelectedPlugins.Any(string.IsNullOrWhiteSpace) &&
        bar.HiddenWindows is not null && bar.DtrOrder is not null && bar.CollapsedDtrTitles is not null &&
        bar.EnabledWorldMarkers is not null && bar.VolumePresets is not null &&
        !bar.Widgets.Concat(bar.Commands).Any(item => item is null || item.Command is null ||
            item.RightCommand is null || item.Name is null || item.DtrTitle is null ||
            !Enum.IsDefined(item.Type) || !Enum.IsDefined(item.Side)) &&
        float.IsFinite(bar.ToolbarScale) && bar.ToolbarScale is >= 0.1f and <= 3f &&
        float.IsFinite(bar.ComponentSpacing) && bar.ComponentSpacing is >= 0f and <= 40f &&
        float.IsFinite(bar.ButtonCornerRadius) && bar.ButtonCornerRadius is >= 0f and <= 20f &&
        float.IsFinite(bar.RowIconSize) && bar.RowIconSize is >= 8f and <= 72f &&
        float.IsFinite(bar.BarVerticalOffset) && bar.BarVerticalOffset >= 0f &&
        float.IsFinite(bar.BarHorizontalOffset) && bar.BarHorizontalOffset >= 0f &&
        float.IsFinite(bar.BarLeftMargin) && bar.BarLeftMargin >= 0f &&
        float.IsFinite(bar.BarRightMargin) && bar.BarRightMargin >= 0f &&
        float.IsFinite(bar.BarBackgroundOpacity) && bar.BarBackgroundOpacity is >= 0f and <= 1f &&
        float.IsFinite(bar.ButtonBackgroundOpacity) && bar.ButtonBackgroundOpacity is >= 0f and <= 1f &&
        Enum.IsDefined(bar.Alignment);
}
