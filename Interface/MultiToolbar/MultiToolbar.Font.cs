using System.IO;
using Dalamud.Interface.ManagedFontAtlas;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed partial class MultiToolbar
{
    private const string DefaultToolbarFontName = "思源黑体 CN Bold";
    private object? installedFontSnapshot;
    private string defaultToolbarFontPath = OmniTheme.DefaultFontPath;
    private IFontHandle? toolbarFont;
    private string toolbarFontPath = string.Empty;
    private float toolbarFontSize;
    private string toolbarFontSearch = string.Empty;

    private string GetDefaultToolbarFontPath()
    {
        var installed = FontManager.Instance().InstalledFonts;
        if (!ReferenceEquals(installedFontSnapshot, installed))
        {
            installedFontSnapshot = installed;
            defaultToolbarFontPath = OmniTheme.DefaultFontPath;
            foreach (var font in installed)
            {
                if (font.Value == DefaultToolbarFontName)
                {
                    defaultToolbarFontPath = font.Key;
                    break;
                }
            }
        }
        return defaultToolbarFontPath;
    }

    private bool DrawToolbarFontSelector()
    {
        var manager = FontManager.Instance();
        var path = config.FontFileName;
        var isDefault = string.IsNullOrWhiteSpace(path);
        var label = isDefault ? DefaultToolbarFontName
            : manager.InstalledFonts.GetValueOrDefault(path, Path.GetFileNameWithoutExtension(path));
        if (!OmniControls.BeginCombo("##multiToolbarFont", label, ImGui.GetContentRegionAvail().X, ImGuiComboFlags.HeightLarge))
        {
            return false;
        }

        var changed = false;
        OmniControls.InputTextWithHint("##fontSearch", OmniLoc.Get("Settings.Ui.Font.Search"),
            ref toolbarFontSearch, 128, ImGui.GetContentRegionAvail().X);
        if (ImGui.Selectable(DefaultToolbarFontName, isDefault))
        {
            config.FontFileName = string.Empty;
            changed = true;
        }
        foreach (var installed in manager.InstalledFonts)
        {
            if (installed.Value == DefaultToolbarFontName ||
                (!string.IsNullOrWhiteSpace(toolbarFontSearch) &&
                 !installed.Key.Contains(toolbarFontSearch, StringComparison.OrdinalIgnoreCase) &&
                 !installed.Value.Contains(toolbarFontSearch, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (ImGui.Selectable($"{installed.Value}##{installed.Key}", path == installed.Key))
            {
                config.FontFileName = installed.Key;
                changed = true;
            }
            OmniControls.HelpTooltip(installed.Key);
        }
        ImGui.EndCombo();
        return changed;
    }

    private unsafe IFontHandle GetToolbarFont()
    {
        var manager = FontManager.Instance();
        var path = string.IsNullOrWhiteSpace(config.FontFileName) ? GetDefaultToolbarFontPath() : config.FontFileName;
        var size = manager.GetActualFontSize(1f);
        if (string.Equals(path, manager.Config.FontFileName, StringComparison.OrdinalIgnoreCase))
        {
            toolbarFont?.Dispose();
            toolbarFont = null;
            return manager.UIFont;
        }
        if (toolbarFont is not null && toolbarFontPath == path && toolbarFontSize == size)
        {
            return toolbarFont;
        }

        toolbarFont?.Dispose();
        toolbarFontPath = path;
        toolbarFontSize = size;
        var builder = new ImFontGlyphRangesBuilderPtr(ImGuiNative.ImFontGlyphRangesBuilder());
        ushort[] ranges;
        try
        {
            builder.AddRanges(ImGui.GetIO().Fonts.GetGlyphRangesDefault());
            builder.AddRanges(ImGui.GetIO().Fonts.GetGlyphRangesChineseFull());
            builder.AddRanges(ImGui.GetIO().Fonts.GetGlyphRangesKorean());
            ranges = builder.BuildRangesToArray();
        }
        finally
        {
            builder.Destroy();
        }
        var exists = File.Exists(path);
        toolbarFont = DalamudServices.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var textFont = exists
                ? tk.AddFontFromFile(path, new() { SizePx = size, PixelSnapH = true, GlyphRanges = ranges })
                : tk.AddDalamudDefaultFont(size, ranges);
            var symbols = tk.AddGameSymbol(new() { SizePx = size, PixelSnapH = true, MergeFont = textFont });
            tk.AddFontAwesomeIconFont(new() { SizePx = size, PixelSnapH = true, MergeFont = symbols });
        }));
        return toolbarFont;
    }
}
