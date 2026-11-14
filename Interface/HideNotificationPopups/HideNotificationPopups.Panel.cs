using OmniToolbox.UI.Widgets;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Data;
using Lumina.Data.Files;
using OmenTools.OmenService;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class HideNotificationPopups
{
    private readonly ConcurrentDictionary<string, (Vector2 Minimum, Vector2 Maximum)> previewBounds = new(StringComparer.Ordinal);
    private readonly Dictionary<(uint IconID, IconSubFolder? Folder, Language Language), string?> previewPaths = [];

    public override bool DrawSettings()
    {
        if (displayBannerIDs is null || displayBannerIDs.Length != config.Banners.Count)
            displayBannerIDs = [.. config.Banners.Keys.Order()];

        using var cellPadding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(OmniTheme.Scale(5f)));
        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var minimumCardWidth = MathF.Max(OmniTheme.Scale(220f),
            OmniTextMeasurements.Measure(OmniLoc.Get(this, "Guildhest")).X +
            OmniTextMeasurements.Measure(OmniLoc.Get(this, "Visible")).X + OmniTheme.Scale(36f));
        var columns = Math.Clamp((int)((availableWidth - style.ScrollbarSize) /
            (minimumCardWidth + style.CellPadding.X * 2f)), 1, 3);
        var rows = (displayBannerIDs.Length + columns) / columns;
        var cardHeight = MathF.Ceiling(MathF.Max(OmniTheme.Scale(88f), ImGui.GetTextLineHeightWithSpacing() * 3f));
        var rowHeight = cardHeight + style.CellPadding.Y * 2f;
        var tableHeight = Math.Min(rows, 6) * rowHeight;
        using var table = ImRaii.Table("##notificationPopups", columns,
            ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings,
            new Vector2(availableWidth, tableHeight));
        if (!table)
            return false;

        var changed = false;
        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(rows, rowHeight);
        while (clipper.Step())
        {
            for (var row = clipper.DisplayStart; row < Math.Min(rows, clipper.DisplayEnd); row++)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, cardHeight);
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    if (index > displayBannerIDs.Length)
                        break;

                    ImGui.TableNextColumn();
                    var iconID = index == 0 ? 0 : displayBannerIDs[index - 1];
                    if (!DrawPopupCard(iconID, new(ImGui.GetContentRegionAvail().X, cardHeight)))
                        continue;

                    if (iconID == 0)
                        config.HideGuildhest ^= true;
                    else
                        config.Banners[iconID].Hidden ^= true;
                    changed = true;
                }
            }
        }
        clipper.End();
        if (changed && IsEnabled)
            ApplyCurrentPopups();
        return changed;
    }

    private bool DrawPopupCard(uint iconID, Vector2 size)
    {
        var selected = iconID == 0 ? config.HideGuildhest : config.Banners[iconID].Hidden;
        var position = ImGui.GetCursorScreenPos();
        var clicked = OmniControls.RoundedSelectable($"##notificationPopup{iconID}", selected, ImGuiSelectableFlags.None, size);
        if (!ImGui.IsItemVisible())
            return clicked;

        var padding = new Vector2(OmniTheme.Scale(6f));
        var lineHeight = ImGui.GetTextLineHeight();
        var contentSize = Vector2.Max(Vector2.One, size - padding * 2f - new Vector2(0f, lineHeight + padding.Y));
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(position, position + size, true);
        var borderWidth = OmniTheme.BorderThickness();
        var borderInset = new Vector2(borderWidth * 0.5f);
        drawList.AddRect(position + borderInset, position + size - borderInset,
            OmniTheme.Color(OmniTheme.Tokens.Border), OmniTheme.Scale(OmniTheme.Tokens.ButtonRadius),
            ImDrawFlags.None, borderWidth);

        IDalamudTextureWrap? texture = null;
        var path = GetPreviewPath(iconID);
        if (path is not null)
            texture = DalamudServices.TextureProvider.GetFromGame(path).GetWrapOrDefault();

        var textColor = OmniTheme.Color(OmniTheme.Tokens.Text);
        if (texture is { Width: > 0, Height: > 0 } && path is not null)
        {
            var bounds = iconID == 0 ? (Minimum: Vector2.Zero, Maximum: Vector2.One) : GetPreviewBounds(path);
            var croppedSize = new Vector2(texture.Width, texture.Height) * (bounds.Maximum - bounds.Minimum);
            var factor = MathF.Min(contentSize.X / croppedSize.X, contentSize.Y / croppedSize.Y);
            var imageSize = croppedSize * factor;
            var imagePosition = position + padding + (contentSize - imageSize) * 0.5f;
            drawList.AddImage(texture.Handle, imagePosition, imagePosition + imageSize, bounds.Minimum, bounds.Maximum);
        }
        else
        {
            var label = iconID == 0 ? OmniLoc.Get(this, "Guildhest") :
                $"{string.Format(OmniLoc.Get(this, "Banner"), iconID)}\n{OmniLoc.Get(this, "PreviewUnavailable")}";
            var textSize = OmniTextMeasurements.Measure(label, false, contentSize.X);
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(),
                position + padding + Vector2.Max(Vector2.Zero, contentSize - textSize) * 0.5f,
                textColor, label, contentSize.X);
        }

        var status = OmniLoc.Get(this, selected ? "Hidden" : "Visible");
        var statusPadding = OmniTheme.Scale(new Vector2(6f, 2f));
        var statusTextSize = OmniTextMeasurements.Measure(status);
        var statusSize = Vector2.Max(OmniTextMeasurements.Measure(OmniLoc.Get(this, "Hidden")),
            OmniTextMeasurements.Measure(OmniLoc.Get(this, "Visible"))) + statusPadding * 2f;
        var statusPosition = position + new Vector2(MathF.Max(padding.X, size.X - padding.X - statusSize.X),
            size.Y - padding.Y - statusSize.Y);
        if (iconID == 0 && texture is not null)
        {
            var labelPosition = new Vector2(position.X + padding.X, statusPosition.Y + statusPadding.Y);
            drawList.PushClipRect(labelPosition, new Vector2(statusPosition.X - padding.X, position.Y + size.Y), true);
            drawList.AddText(labelPosition, textColor, OmniLoc.Get(this, "Guildhest"));
            drawList.PopClipRect();
        }
        var statusFill = selected ? OmniTheme.Tokens.Accent : OmniTheme.Tokens.Surface;
        var statusRadius = OmniTheme.Scale(OmniTheme.Tokens.ButtonRadius) *
            MathF.Min(1f, statusSize.Y / OmniControls.NavButtonSize(status).Y);
        OmniControls.DrawControlShadow(statusPosition, statusSize, statusRadius);
        drawList.AddRectFilled(statusPosition, statusPosition + statusSize, OmniTheme.Color(statusFill), statusRadius);
        OmniControls.DrawControlFrame(drawList, statusPosition, statusSize, statusRadius, OmniTheme.BorderThickness());
        OmniControls.DrawControlHighlight(drawList, statusPosition, statusSize, statusRadius);
        drawList.AddText(statusPosition + (statusSize - statusTextSize) * 0.5f, textColor, status);
        drawList.PopClipRect();
        return clicked;
    }

    private string? GetPreviewPath(uint iconID)
    {
        var folder = iconID == 0 ? IconSubFolder.None : config.Banners[iconID].Folder;
        var language = GameState.ClientLanguage;
        var key = (iconID, folder, language);
        if (previewPaths.TryGetValue(key, out var path))
            return path;

        if (iconID != 0 && folder != IconSubFolder.HighQuality)
        {
            var variant = language switch
            {
                Language.Japanese => "ja",
                Language.English => "en",
                Language.German => "de",
                Language.French => "fr",
                Language.ChineseSimplified => "chs",
                Language.ChineseTraditional => "cht",
                Language.Korean => "ko",
                Language.TraditionalChinese => "tc",
                _ => string.Empty
            };
            if (variant.Length > 0)
            {
                var prefix = $"ui/icon/{iconID / 1000 * 1000:D6}/{variant}/{iconID:D6}";
                if (DalamudServices.DataManager.FileExists($"{prefix}_hr1.tex"))
                    path = $"{prefix}_hr1.tex";
                else if (DalamudServices.DataManager.FileExists($"{prefix}.tex"))
                    path = $"{prefix}.tex";
            }
        }

        if (path is null && !DalamudServices.TextureProvider.TryGetIconPath(
                new(iconID == 0 ? 111006 : iconID, folder == IconSubFolder.HighQuality), out path))
            path = null;
        previewPaths[key] = path;
        return path;
    }

    private (Vector2 Minimum, Vector2 Maximum) GetPreviewBounds(string path)
    {
        if (previewBounds.TryGetValue(path, out var bounds))
            return bounds;

        bounds = (Vector2.Zero, Vector2.One);
        previewBounds[path] = bounds;
        InstanceLifetime.RunBackground(token =>
        {
            try
            {
                previewBounds[path] = ReadPreviewBounds(path, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                DalamudServices.PluginLog.Warning(exception, "Unable to inspect banner preview texture {Path}.", path);
            }
            return Task.CompletedTask;
        });
        return bounds;
    }

    private static (Vector2 Minimum, Vector2 Maximum) ReadPreviewBounds(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var texture = DalamudServices.DataManager.GetFile<TexFile>(path);
        if (texture == null)
            return (Vector2.Zero, Vector2.One);

        var pixels = texture.ImageData;
        var width = (int)texture.Header.Width;
        var height = (int)texture.Header.Height;
        if (width == 0 || height == 0 || (long)width * height * 4 > pixels.Length)
            return (Vector2.Zero, Vector2.One);

        var minimumX = width;
        var minimumY = height;
        var maximumX = -1;
        var maximumY = -1;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] < 16)
                    continue;

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }
        if (maximumX < minimumX)
            return (Vector2.Zero, Vector2.One);

        var padding = Math.Max(2, Math.Min(width, height) / 64);
        return (
            new Vector2(Math.Max(0, minimumX - padding) / (float)width, Math.Max(0, minimumY - padding) / (float)height),
            new Vector2(Math.Min(width, maximumX + padding + 1) / (float)width, Math.Min(height, maximumY + padding + 1) / (float)height));
    }
}
