using System.Drawing;
using System.Globalization;
using Dalamud.Interface.Textures;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;

namespace OmniToolbox.TreeHouse.General;

internal sealed class MitigationRecordRenderer(MitigationMonitorConfig config)
{
    private const uint PhysicalDamageIconId = 60011;
    private const uint MagicalDamageIconId = 60012;
    private const uint SpecialDamageIconId = 60013;
    private const float StatusMeasureTextScale = 0.82f;
    private const float StatusDrawTextScale = 0.76f;
    private const float StatusIconTextGap = -6f;
    private const float StatusWrappedRowGap = 2f;
    private const float StatusItemGap = -3f;

    private static readonly uint[] JobIconV3ByRowId =
    [
        0, 62301, 62302, 62303, 62304, 62305, 62306, 62307, 62310, 62311, 62312,
        62313, 62314, 62315, 62316, 62317, 62318, 62319, 62320, 62401, 62402, 62403,
        62404, 62405, 62406, 62407, 62308, 62408, 62409, 62309, 62410, 62411, 62412,
        62413, 62414, 62415, 62416, 62417, 62418, 62419, 62420, 62421, 62422
    ];

    private readonly Dictionary<uint, ISharedImmediateTexture> iconTextures = [];

    public float StatusColumnWidth { get; set; } = 92f;

    public void Draw(MitigationRecord record, int index)
    {
        var rowHeight = MathF.Max(OmniTheme.Scale(40f), CalculateStatusHeight(record));
        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        ImGui.TableSetColumnIndex(0);
        var rowStart = ImGui.GetCursorScreenPos();
        if (ImGui.Selectable(
                $"##MitigationRecord{index}",
                false,
                ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                new Vector2(0f, rowHeight)))
        {
            ImGui.SetClipboardText(MitigationRecordCopyText.Build(record));
        }

        ImGui.SetCursorScreenPos(rowStart);
        DrawCenteredText(MitigationText.FormatElapsed(record.Elapsed), rowHeight);
        ImGui.TableSetColumnIndex(1);
        DrawAction(record, rowHeight);
        ImGui.TableSetColumnIndex(2);
        DrawTarget(record, rowHeight);
        ImGui.TableSetColumnIndex(3);
        DrawDamage(record, rowHeight);
        ImGui.TableSetColumnIndex(4);
        DrawCenteredText(
            record.Kind == MitigationRecordKind.Wipe ? "-" : $"{record.MitigationPercent:0.#}%",
            rowHeight,
            KnownColor.LightSkyBlue.ToVector4());
        ImGui.TableSetColumnIndex(5);
        DrawStatuses(record, rowHeight);
    }

    public void ResetRuntime() => iconTextures.Clear();

    private static void DrawCenteredText(string text, float rowHeight, Vector4? color = null)
    {
        var cursor = ImGui.GetCursorPos();
        var textSize = ImGui.CalcTextSize(text);
        ImGui.SetCursorPos(new(
            cursor.X + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - textSize.X) * 0.5f),
            cursor.Y + MathF.Max(0f, (rowHeight - textSize.Y) * 0.5f)));
        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }

    private static void DrawAction(MitigationRecord record, float rowHeight)
    {
        DrawCenteredText(record.ActionName, rowHeight, record.Kind switch
        {
            MitigationRecordKind.Defeated => KnownColor.LightPink.ToVector4(),
            MitigationRecordKind.Wipe => KnownColor.Orange.ToVector4(),
            _ when record.SourceKind == DamageSourceKind.Dot => KnownColor.Coral.ToVector4(),
            _ when record.SourceKind == DamageSourceKind.AutoAttack => KnownColor.Wheat.ToVector4(),
            _ => KnownColor.White.ToVector4()
        });
    }

    private void DrawTarget(MitigationRecord record, float rowHeight)
    {
        if (record.Kind == MitigationRecordKind.Wipe)
        {
            return;
        }

        var iconId = GetTargetIconId(record.TargetJobRowId, config.TargetDisplayMode);
        if (iconId == 0)
        {
            DrawCenteredText(config.TargetDisplayMode == MitigationTargetDisplayMode.JobName &&
                             !string.IsNullOrWhiteSpace(record.TargetJobName)
                ? record.TargetJobName
                : MitigationText.BuildTargetName(string.Empty, record.TargetName), rowHeight);
        }
        else if (GetIconHandle(iconId) is { } handle)
        {
            var size = OmniTheme.Scale(new Vector2(24f));
            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(new(
                cursor.X + MathF.Max(0f, (ImGui.GetContentRegionAvail().X - size.X) * 0.5f),
                cursor.Y + MathF.Max(0f, (rowHeight - size.Y) * 0.5f)));
            ImGui.Image(handle, size);
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrWhiteSpace(record.TargetName))
        {
            ImGui.SetTooltip(record.TargetName);
        }
    }

    private void DrawDamage(MitigationRecord record, float rowHeight)
    {
        if (record.Kind == MitigationRecordKind.Wipe)
        {
            DrawCenteredText("-", rowHeight);
            return;
        }

        var iconId = record.DamageKind switch
        {
            DamageKind.Physical => PhysicalDamageIconId,
            DamageKind.Magical => MagicalDamageIconId,
            _ => SpecialDamageIconId
        };
        var text = record.Missed
            ? OmniLoc.Get("Feature.MitigationMonitor.Damage.Miss")
            : record.Invulnerable
                ? OmniLoc.Get("Feature.MitigationMonitor.Damage.Invulnerable")
                : record.Damage.ToString("N0", CultureInfo.InvariantCulture);
        var textSize = ImGui.CalcTextSize(text);
        var iconSize = OmniTheme.Scale(new Vector2(18f));
        var iconHandle = GetIconHandle(iconId);
        var spacing = iconHandle.HasValue ? OmniTheme.Scale(4f) : 0f;
        var contentWidth = textSize.X + (iconHandle.HasValue ? iconSize.X + spacing : 0f);
        var start = ImGui.GetCursorScreenPos() + new Vector2(
            MathF.Max(0f, (ImGui.GetContentRegionAvail().X - contentWidth) * 0.5f),
            0f);
        if (iconHandle.HasValue)
        {
            var iconMin = start + new Vector2(0f, MathF.Max(0f, (rowHeight - iconSize.Y) * 0.5f));
            ImGui.GetWindowDrawList().AddImage(iconHandle.Value, iconMin, iconMin + iconSize);
        }

        ImGui.GetWindowDrawList().AddText(
            start + new Vector2(
                iconHandle.HasValue ? iconSize.X + spacing : 0f,
                MathF.Max(0f, (rowHeight - textSize.Y) * 0.5f)),
            OmniTheme.Color(record.DamageKind switch
            {
                DamageKind.Physical => KnownColor.SandyBrown.ToVector4(),
                DamageKind.Magical => KnownColor.CornflowerBlue.ToVector4(),
                _ => KnownColor.MediumPurple.ToVector4()
            }),
            text);
    }

    private float CalculateStatusHeight(MitigationRecord record)
    {
        if (record.Statuses.Length == 0)
        {
            return 0f;
        }

        var iconWidth = OmniTheme.Scale(26f);
        var lineHeight = OmniTheme.Scale(28f + StatusIconTextGap) + ImGui.GetFontSize() * StatusMeasureTextScale;
        var availableWidth = MathF.Max(iconWidth, StatusColumnWidth - OmniTheme.Scale(8f));
        var lineCount = 1;
        var usedWidth = 0f;
        foreach (var status in record.Statuses)
        {
            var slotWidth = MathF.Max(iconWidth, ImGui.CalcTextSize(MathF.Ceiling(status.RemainingSeconds).ToString(
                "0", CultureInfo.InvariantCulture)).X * StatusMeasureTextScale);
            if (usedWidth > 0f && usedWidth + slotWidth > availableWidth)
            {
                lineCount++;
                usedWidth = 0f;
            }

            usedWidth += slotWidth + OmniTheme.Scale(StatusItemGap);
        }

        return lineCount == 1
            ? lineHeight
            : lineCount * lineHeight +
              (lineCount - 1) * OmniTheme.Scale(StatusWrappedRowGap) +
              OmniTheme.Scale(4f);
    }

    private void DrawStatuses(MitigationRecord record, float rowHeight)
    {
        if (record.Statuses.Length == 0)
        {
            if (record.Kind != MitigationRecordKind.Wipe)
            {
                DrawCenteredText("-", rowHeight);
            }

            return;
        }

        var iconSize = OmniTheme.Scale(new Vector2(26f, 28f));
        var smallFontSize = ImGui.GetFontSize() * StatusDrawTextScale;
        var lineHeight = iconSize.Y + OmniTheme.Scale(StatusIconTextGap) + smallFontSize;
        var start = ImGui.GetCursorScreenPos();
        var x = start.X + OmniTheme.Scale(4f);
        var y = start.Y + MathF.Max(0f, (rowHeight - CalculateStatusHeight(record)) * 0.5f);
        var right = start.X + MathF.Max(iconSize.X, StatusColumnWidth - OmniTheme.Scale(4f));
        foreach (var status in record.Statuses)
        {
            var seconds = MathF.Ceiling(status.RemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
            var textSize = ImGui.CalcTextSize(seconds) * StatusMeasureTextScale;
            var slotWidth = MathF.Max(iconSize.X, textSize.X);
            if (x > start.X + OmniTheme.Scale(4f) && x + slotWidth > right)
            {
                x = start.X + OmniTheme.Scale(4f);
                y += lineHeight + OmniTheme.Scale(StatusWrappedRowGap);
            }

            var iconMin = new Vector2(x + (slotWidth - iconSize.X) * 0.5f, y);
            var iconMax = iconMin + iconSize;
            if (GetIconHandle(status.IconId) is { } handle)
            {
                ImGui.GetWindowDrawList().AddImage(handle, iconMin, iconMax);
            }

            if (!status.Useful)
            {
                ImGui.GetWindowDrawList().AddRectFilled(
                    iconMin,
                    iconMax,
                    OmniTheme.Color(OmniTheme.Tokens.Shadow with { W = 0.56f }),
                    OmniTheme.Scale(2f));
            }

            ImGui.GetWindowDrawList().AddText(
                ImGui.GetFont(),
                smallFontSize,
                new Vector2(
                    x + (slotWidth - textSize.X) * 0.5f,
                    iconMax.Y + OmniTheme.Scale(StatusIconTextGap)),
                OmniTheme.Color(status.Category switch
                {
                    MitigationStatusCategory.Mitigation => KnownColor.LightSkyBlue.ToVector4(),
                    MitigationStatusCategory.Shield => KnownColor.Gold.ToVector4(),
                    _ => KnownColor.White.ToVector4()
                }),
                seconds);
            if (ImGui.IsMouseHoveringRect(iconMin, iconMax))
            {
                ImGui.SetTooltip(BuildStatusTooltip(status));
            }

            x += slotWidth + OmniTheme.Scale(StatusItemGap);
        }

        ImGui.Dummy(new Vector2(StatusColumnWidth, rowHeight));
    }

    private ImTextureID? GetIconHandle(uint iconId)
    {
        if (iconId == 0)
        {
            return null;
        }

        if (!iconTextures.TryGetValue(iconId, out var texture))
        {
            texture = DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            iconTextures[iconId] = texture;
        }

        var wrap = texture.GetWrapOrDefault();
        return wrap == null ? null : wrap.Handle;
    }

    private static uint GetTargetIconId(uint jobRowId, MitigationTargetDisplayMode mode)
    {
        if (jobRowId == 0)
        {
            return 0;
        }

        return mode switch
        {
            MitigationTargetDisplayMode.JobIcon => 62000u + jobRowId,
            MitigationTargetDisplayMode.JobIconV2 => 62100u + jobRowId,
            MitigationTargetDisplayMode.JobIconV3 when jobRowId < JobIconV3ByRowId.Length => JobIconV3ByRowId[jobRowId],
            _ => 0
        };
    }

    private static string BuildStatusTooltip(ActiveMitigation status)
    {
        var stackText = status.StackCount > 1
            ? string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Status.Stack"),
                status.StackCount)
            : string.Empty;
        return status.Category switch
        {
            MitigationStatusCategory.Vulnerability => string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Status.Vulnerability"),
                status.Name,
                stackText),
            MitigationStatusCategory.Mitigation => string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Status.Mitigation"),
                status.Name,
                status.AffectsPercent ? status.Value : 0),
            _ => string.Format(
                CultureInfo.CurrentCulture,
                OmniLoc.Get("Feature.MitigationMonitor.Status.Shield"),
                status.Name,
                stackText)
        };
    }

}
