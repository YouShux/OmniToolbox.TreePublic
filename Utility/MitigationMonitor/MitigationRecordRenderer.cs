using System.Drawing;
using System.Globalization;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmniToolbox.UI.Theme;
using OmenTools.Extensions;

namespace OmniToolbox.TreePublic;

internal sealed class MitigationRecordRenderer(MitigationMonitorConfig config)
{
    private const uint PhysicalDamageIconID = 60011;
    private const uint MagicalDamageIconID = 60012;
    private const uint SpecialDamageIconID = 60013;
    private const float StatusIconTextGap = -6f;

    private static readonly uint[] JobIconV3ByRowID =
    [
        0, 62301, 62302, 62303, 62304, 62305, 62306, 62307, 62310, 62311, 62312,
        62313, 62314, 62315, 62316, 62317, 62318, 62319, 62320, 62401, 62402, 62403,
        62404, 62405, 62406, 62407, 62308, 62408, 62409, 62309, 62410, 62411, 62412,
        62413, 62414, 62415, 62416, 62417, 62418, 62419, 62420, 62421, 62422
    ];

    private readonly Dictionary<uint, ISharedImmediateTexture> iconTextures = [];

    public float CalculateRowHeight(MitigationRecord record, float statusWidth) =>
        MathF.Max(OmniTheme.Scale(40f), CalculateStatusHeight(record, statusWidth));

    public void Draw(
        MitigationRecord record,
        int index,
        Vector2 rowMin,
        float width,
        float rowHeight,
        MitigationTableLayout layout,
        bool hovered,
        bool pressed)
    {
        var rowColor = record.Kind == MitigationRecordKind.Wipe
            ? KnownColor.DarkGoldenrod.ToVector4() with { W = 0.30f }
            : KnownColor.White.ToVector4() with { W = index % 2 == 0 ? 0.035f : 0.075f };
        if (hovered)
        {
            rowColor.W += 0.07f;
        }

        if (pressed)
        {
            rowColor = KnownColor.SteelBlue.ToVector4() with { W = 0.24f };
        }

        ImGui.GetWindowDrawList().AddRectFilled(
            rowMin,
            rowMin + new Vector2(width, rowHeight),
            OmniTheme.Color(rowColor));

        var offset = pressed ? new Vector2(0f, OmniTheme.Scale(1f)) : Vector2.Zero;
        var x = rowMin.X;
        DrawText(MitigationText.FormatElapsed(record.Elapsed), new(x, rowMin.Y), layout.Time, rowHeight, null, true, offset);
        x += layout.Time;
        DrawAction(record, new(x, rowMin.Y), layout.Action, rowHeight, offset);
        x += layout.Action;
        DrawTarget(record, new(x, rowMin.Y), layout.Target, rowHeight, offset);
        x += layout.Target;
        DrawDamage(record, new(x, rowMin.Y), layout.Damage, rowHeight, offset);
        x += layout.Damage;
        DrawText(
            record.Kind == MitigationRecordKind.Wipe ? "—" : $"{record.MitigationPercent:0.#}%",
            new(x, rowMin.Y),
            layout.Mitigation,
            rowHeight,
            KnownColor.LightSkyBlue.ToVector4(),
            true,
            offset);
        x += layout.Mitigation;
        DrawStatuses(record, new(x, rowMin.Y), layout.Status, rowHeight, offset);
    }

    public void ResetRuntime() => iconTextures.Clear();

    private static void DrawText(
        string text,
        Vector2 min,
        float width,
        float rowHeight,
        Vector4? color = null,
        bool centered = false,
        Vector2 offset = default)
    {
        var display = FitText(text, MathF.Max(OmniTheme.Scale(4f), width - OmniTheme.Scale(8f)));
        var textSize = ImGui.CalcTextSize(display);
        ImGui.GetWindowDrawList().AddText(
            min + offset + new Vector2(
                centered ? MathF.Max(0f, (width - textSize.X) * 0.5f) : OmniTheme.Scale(4f),
                MathF.Max(0f, (rowHeight - textSize.Y) * 0.5f)),
            OmniTheme.Color(color ?? KnownColor.White.ToVector4()),
            display);
    }

    private static void DrawAction(
        MitigationRecord record,
        Vector2 min,
        float width,
        float rowHeight,
        Vector2 offset)
    {
        var color = record.Kind switch
        {
            MitigationRecordKind.Defeated => KnownColor.LightPink.ToVector4(),
            MitigationRecordKind.Wipe => OmniTheme.Orange,
            _ when record.SourceKind == DamageSourceKind.Dot => KnownColor.Coral.ToVector4(),
            _ when record.SourceKind == DamageSourceKind.AutoAttack => KnownColor.Wheat.ToVector4(),
            _ => KnownColor.White.ToVector4()
        };
        if (record.Kind == MitigationRecordKind.Damage)
        {
            DrawText(record.ActionName, min, width, rowHeight, color, true, offset);
            return;
        }

        var icon = record.Kind == MitigationRecordKind.Defeated ? FontAwesomeIcon.Skull : FontAwesomeIcon.HandPaper;
        var iconWidth = OmniTheme.Scale(17f);
        var spacing = OmniTheme.Scale(5f);
        var display = FitText(record.ActionName, MathF.Max(OmniTheme.Scale(4f), width - iconWidth - OmniTheme.Scale(12f)));
        var textSize = ImGui.CalcTextSize(display);
        var startX = min.X + MathF.Max(0f, (width - iconWidth - spacing - textSize.X) * 0.5f);
        var iconText = icon.ToIconString();
        var iconSize = ImGui.CalcTextSize(iconText);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(startX + MathF.Max(0f, (iconWidth - iconSize.X) * 0.5f), min.Y + MathF.Max(0f, (rowHeight - iconSize.Y) * 0.5f)) + offset,
            OmniTheme.Color(color),
            iconText);

        ImGui.GetWindowDrawList().AddText(
            new Vector2(startX + iconWidth + spacing, min.Y + MathF.Max(0f, (rowHeight - textSize.Y) * 0.5f)) + offset,
            OmniTheme.Color(color),
            display);
    }

    private void DrawTarget(
        MitigationRecord record,
        Vector2 min,
        float width,
        float rowHeight,
        Vector2 offset)
    {
        if (record.Kind == MitigationRecordKind.Wipe)
        {
            return;
        }

        var iconID = GetTargetIconID(record.TargetJobRowID, config.TargetDisplayMode);
        if (iconID == 0)
        {
            DrawText(
                config.TargetDisplayMode == MitigationTargetDisplayMode.JobName && !string.IsNullOrWhiteSpace(record.TargetJobName)
                    ? record.TargetJobName
                    : MitigationText.BuildTargetName(string.Empty, record.TargetName),
                min,
                width,
                rowHeight,
                null,
                true,
                offset);
        }
        else if (GetIconHandle(iconID) is { } handle)
        {
            var size = OmniTheme.Scale(new Vector2(24f));
            var iconMin = min + offset + new Vector2(
                MathF.Max(0f, (width - size.X) * 0.5f),
                MathF.Max(0f, (rowHeight - size.Y) * 0.5f));
            ImGui.GetWindowDrawList().AddImage(handle, iconMin, iconMin + size);
        }

        if (!string.IsNullOrWhiteSpace(record.TargetName) &&
            ImGui.IsMouseHoveringRect(min, min + new Vector2(width, rowHeight)))
        {
            ImGui.SetTooltip(record.TargetName);
        }
    }

    private void DrawDamage(
        MitigationRecord record,
        Vector2 min,
        float width,
        float rowHeight,
        Vector2 offset)
    {
        if (record.Kind == MitigationRecordKind.Wipe)
        {
            DrawText("—", min, width, rowHeight, null, true, offset);
            return;
        }

        var iconID = record.DamageKind switch
        {
            DamageKind.Physical => PhysicalDamageIconID,
            DamageKind.Magical => MagicalDamageIconID,
            _ => SpecialDamageIconID
        };
        var text = record.Missed
            ? OmniLoc.Get("Feature.MitigationMonitor.Damage.Miss")
            : record.Invulnerable
                ? OmniLoc.Get("Feature.MitigationMonitor.Damage.Invulnerable")
                : OmniNumberFormatter.Format(record.Damage);
        var iconSize = OmniTheme.Scale(new Vector2(18f));
        var spacing = OmniTheme.Scale(4f);
        var display = FitText(text, MathF.Max(OmniTheme.Scale(10f), width - iconSize.X - OmniTheme.Scale(8f)));
        var textSize = ImGui.CalcTextSize(display);
        var startX = min.X + MathF.Max(0f, (width - iconSize.X - spacing - textSize.X) * 0.5f);
        if (GetIconHandle(iconID) is { } handle)
        {
            var iconMin = new Vector2(startX, min.Y + MathF.Max(0f, (rowHeight - iconSize.Y) * 0.5f)) + offset;
            ImGui.GetWindowDrawList().AddImage(handle, iconMin, iconMin + iconSize);
        }

        ImGui.GetWindowDrawList().AddText(
            new Vector2(startX + iconSize.X + spacing, min.Y + MathF.Max(0f, (rowHeight - textSize.Y) * 0.5f)) + offset,
            OmniTheme.Color(record.DamageKind switch
            {
                DamageKind.Physical => KnownColor.SandyBrown.ToVector4(),
                DamageKind.Magical => KnownColor.CornflowerBlue.ToVector4(),
                _ => KnownColor.MediumPurple.ToVector4()
            }),
            display);
    }

    private float CalculateStatusHeight(MitigationRecord record, float statusWidth)
    {
        if (record.Statuses.Length == 0)
        {
            return 0f;
        }

        var iconWidth = OmniTheme.Scale(26f);
        var lineHeight = OmniTheme.Scale(28f + StatusIconTextGap) + ImGui.GetFontSize() * 0.82f;
        var availableWidth = MathF.Max(iconWidth, statusWidth - OmniTheme.Scale(8f));
        var lineCount = 1;
        var usedWidth = 0f;
        var gap = OmniTheme.Scale(-3f);
        foreach (var status in record.Statuses)
        {
            var slotWidth = GetStatusSlotWidth(status, iconWidth);
            var nextWidth = usedWidth == 0f ? slotWidth : usedWidth + gap + slotWidth;
            if (usedWidth > 0f && nextWidth > availableWidth)
            {
                lineCount++;
                usedWidth = slotWidth;
                continue;
            }

            usedWidth = nextWidth;
        }

        return lineCount * lineHeight + (lineCount - 1) * OmniTheme.Scale(2f) + OmniTheme.Scale(4f);
    }

    private void DrawStatuses(
        MitigationRecord record,
        Vector2 min,
        float width,
        float rowHeight,
        Vector2 offset)
    {
        if (record.Statuses.Length == 0)
        {
            if (record.Kind != MitigationRecordKind.Wipe)
            {
                DrawText("—", min, width, rowHeight, null, true, offset);
            }

            return;
        }

        var iconSlotSize = OmniTheme.Scale(new Vector2(26f, 28f));
        var iconSize = OmniTheme.StatusIconSize(iconSlotSize.Y);
        var smallFontSize = ImGui.GetFontSize() * 0.76f;
        var lineHeight = iconSlotSize.Y + OmniTheme.Scale(StatusIconTextGap) + smallFontSize;
        var y = min.Y + MathF.Max(0f, (rowHeight - CalculateStatusHeight(record, width)) * 0.5f);
        var availableWidth = MathF.Max(iconSlotSize.X, width - OmniTheme.Scale(8f));
        var gap = OmniTheme.Scale(-3f);
        var statusIndex = 0;
        while (statusIndex < record.Statuses.Length)
        {
            var lineStart = statusIndex;
            var lineWidth = 0f;
            while (statusIndex < record.Statuses.Length)
            {
                var slotWidth = GetStatusSlotWidth(record.Statuses[statusIndex], iconSlotSize.X);
                var nextWidth = lineWidth == 0f ? slotWidth : lineWidth + gap + slotWidth;
                if (lineWidth > 0f && nextWidth > availableWidth)
                {
                    break;
                }

                lineWidth = nextWidth;
                statusIndex++;
            }

            var x = min.X + OmniTheme.Scale(4f);
            for (var index = lineStart; index < statusIndex; index++)
            {
                var status = record.Statuses[index];
                var seconds = MathF.Ceiling(status.RemainingSeconds).ToString("0", CultureInfo.InvariantCulture);
                var textSize = ImGui.CalcTextSize(seconds) * 0.76f;
                var slotWidth = GetStatusSlotWidth(status, iconSlotSize.X);
                var iconMin = new Vector2(x + (slotWidth - iconSize.X) * 0.5f, y) + offset;
                var iconMax = iconMin + iconSize;
                if (GetIconHandle(status.IconID) is { } handle)
                {
                    ImGui.GetWindowDrawList().AddImage(handle, iconMin, iconMax);
                }

                if (!status.Useful)
                {
                    ImGui.GetWindowDrawList().AddRectFilled(
                        iconMin,
                        iconMax,
                        OmniTheme.Color(KnownColor.Black.ToVector4() with { W = 0.56f }),
                        OmniTheme.Scale(2f));
                }

                ImGui.GetWindowDrawList().AddText(
                    ImGui.GetFont(),
                    smallFontSize,
                    new Vector2(x + (slotWidth - textSize.X) * 0.5f, iconMax.Y + OmniTheme.Scale(StatusIconTextGap)),
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

                x += slotWidth + gap;
            }

            y += lineHeight + OmniTheme.Scale(2f);
        }
    }

    private static float GetStatusSlotWidth(ActiveMitigation status, float iconWidth) =>
        MathF.Max(
            iconWidth,
            ImGui.CalcTextSize(MathF.Ceiling(status.RemainingSeconds).ToString(
                "0",
                CultureInfo.InvariantCulture)).X * 0.82f);

    private ImTextureID? GetIconHandle(uint iconID)
    {
        if (iconID == 0)
        {
            return null;
        }

        if (!iconTextures.TryGetValue(iconID, out var texture))
        {
            texture = DalamudServices.TextureProvider.GetFromGameIcon(new GameIconLookup(iconID));
            iconTextures[iconID] = texture;
        }

        return texture.GetWrapOrDefault()?.Handle;
    }

    private static uint GetTargetIconID(uint jobRowID, MitigationTargetDisplayMode mode)
    {
        if (jobRowID == 0)
        {
            return 0;
        }

        return mode switch
        {
            MitigationTargetDisplayMode.JobIcon => 62000u + jobRowID,
            MitigationTargetDisplayMode.JobIconV2 => 62100u + jobRowID,
            MitigationTargetDisplayMode.JobIconV3 when jobRowID < JobIconV3ByRowID.Length => JobIconV3ByRowID[jobRowID],
            _ => 0
        };
    }

    private static string BuildStatusTooltip(ActiveMitigation status)
    {
        var stackText = status.StackCount > 1
            ? string.Format(CultureInfo.CurrentCulture, OmniLoc.Get("Feature.MitigationMonitor.Status.Stack"), status.StackCount)
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

    internal static string FitText(string text, float width)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= width)
        {
            return text;
        }

        const string ellipsis = "...";
        var available = MathF.Max(0f, width - ImGui.CalcTextSize(ellipsis).X);
        var info = new StringInfo(text);
        for (var length = info.LengthInTextElements - 1; length > 0; length--)
        {
            var candidate = info.SubstringByTextElements(0, length);
            if (ImGui.CalcTextSize(candidate).X <= available)
            {
                return candidate + ellipsis;
            }
        }

        return ellipsis;
    }
}
