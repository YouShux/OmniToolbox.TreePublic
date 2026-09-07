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
                ["Version"] = 1,
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
                if (snapshot?.Value<string>("Format") != "OmniToolbox.MultiToolbar" || snapshot.Value<int>("Version") != 1 ||
                    snapshot["Config"] is not JObject payload)
                {
                    throw new JsonSerializationException("Unsupported configuration format.");
                }
                var imported = payload.ToObject<MultiToolbarConfig>(JsonSerializer.Create(TransferSettings));
                if (imported is null || imported.Widgets is null || imported.Commands is null ||
                    imported.SelectedPlugins is null || imported.SelectedPlugins.Any(string.IsNullOrWhiteSpace) ||
                    imported.HiddenWindows is null || imported.DtrOrder is null || imported.CollapsedDtrTitles is null ||
                    imported.EnabledWorldMarkers is null || imported.VolumePresets is null ||
                    imported.Widgets.Concat(imported.Commands).Any(item => item is null || item.Command is null || item.RightCommand is null || item.Name is null ||
                        !Enum.IsDefined(item.Type) || !Enum.IsDefined(item.Side)) ||
                    !float.IsFinite(imported.ToolbarScale) || imported.ToolbarScale is < 0.1f or > 3f ||
                    !float.IsFinite(imported.ComponentSpacing) || imported.ComponentSpacing is < 0f or > 40f ||
                    !float.IsFinite(imported.RowIconSize) || imported.RowIconSize is < 8f or > 72f ||
                    !float.IsFinite(imported.BarVerticalOffset) || imported.BarVerticalOffset is < 0f or > 60f ||
                    !float.IsFinite(imported.BarBackgroundOpacity) || imported.BarBackgroundOpacity is < 0f or > 1f ||
                    !float.IsFinite(imported.ButtonBackgroundOpacity) || imported.ButtonBackgroundOpacity is < 0f or > 1f ||
                    !Enum.IsDefined(imported.Alignment))
                {
                    throw new JsonSerializationException("Invalid toolbar configuration.");
                }
                imported.HiddenWindows = imported.HiddenWindows.Select(NormalizeHiddenWindowName)
                    .Where(CanManageHiddenWindow).ToHashSet(StringComparer.OrdinalIgnoreCase);
                imported.MultiDockMigrated = config.MultiDockMigrated;
                imported.SelectedPlugins = imported.SelectedPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                imported.MultiDockPluginsMigrated = payload.ContainsKey(nameof(MultiToolbarConfig.SelectedPlugins)) || config.MultiDockPluginsMigrated;
                RestoreHiddenWindows();
                ClosePopup();
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
}
