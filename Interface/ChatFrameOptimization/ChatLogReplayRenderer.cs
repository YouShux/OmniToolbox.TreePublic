using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Lumina.Text.ReadOnly;
using OmniToolbox.Host;
using OmniToolbox.UI.Theme;

namespace OmniToolbox.TreePublic;

internal sealed class ChatLogReplayRenderer
{
    private readonly ChatLogReplayAnonymizer anonymizer = new();
    private readonly Dictionary<ChatLogReplayEntry, byte[]?> nativePayloads = [];
    private readonly Dictionary<ChatLogReplayEntry, byte[]?> anonymousPayloads = [];
    private IReadOnlyList<ChatLogReplayEntry> entries = [];

    public void Reset(IReadOnlyList<ChatLogReplayEntry> value, string anonymousPrefix)
    {
        entries = value;
        nativePayloads.Clear();
        RebuildAnonymous(anonymousPrefix);
    }

    public void RebuildAnonymous(string anonymousPrefix)
    {
        anonymizer.Build(entries, anonymousPrefix);
        anonymousPayloads.Clear();
    }

    public void Draw(IReadOnlyList<ChatLogReplayEntry> value, bool showTime, bool anonymousMode)
    {
        foreach (var entry in value)
        {
            DrawEntry(entry, showTime, anonymousMode);
        }
    }

    private void DrawEntry(ChatLogReplayEntry entry, bool showTime, bool anonymousMode)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ChatLogReplayPresentation.GetChannelColor(entry.ChannelName));
        try
        {
            if (showTime)
            {
                ImGui.TextUnformatted($"[{entry.TimeText}] ");
                ImGui.SameLine(0f, 0f);
            }

            DrawJobIcon(entry);
            if (TryDrawNativePayload(entry, anonymousMode))
            {
                return;
            }

            ImGui.PushTextWrapPos();
            ImGui.TextWrapped(GetFallbackLine(entry, anonymousMode));
            ImGui.PopTextWrapPos();
        }
        finally
        {
            ImGui.PopStyleColor();
        }
    }

    private static void DrawJobIcon(ChatLogReplayEntry entry)
    {
        if (entry.SenderJobIconID == 0 ||
            DalamudServices.TextureProvider
                .GetFromGameIcon(new GameIconLookup(entry.SenderJobIconID))
                .GetWrapOrDefault() is not { } texture)
        {
            return;
        }

        ImGui.Image(texture.Handle, new(MathF.Max(OmniTheme.Scale(18f), ImGui.GetTextLineHeight())));
        if (!string.IsNullOrWhiteSpace(entry.SenderJobName) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(entry.SenderJobName);
        }

        ImGui.SameLine(0f, OmniTheme.Scale(3f));
    }

    private bool TryDrawNativePayload(ChatLogReplayEntry entry, bool anonymousMode)
    {
        var cache = anonymousMode ? anonymousPayloads : nativePayloads;
        if (!cache.TryGetValue(entry, out var payload))
        {
            payload = CreatePayload(entry, anonymousMode);
            cache[entry] = payload;
        }

        if (payload is null)
        {
            return false;
        }

        ImGuiHelpers.SeStringWrapped(new ReadOnlySeString(payload));
        return true;
    }

    private byte[]? CreatePayload(ChatLogReplayEntry entry, bool anonymousMode)
    {
        if (string.IsNullOrWhiteSpace(entry.NativeFormattedPayloadBase64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(entry.NativeFormattedPayloadBase64);
            if (!anonymousMode)
            {
                return bytes;
            }

            var original = SeString.Parse(bytes);
            var payloads = new List<Payload>(original.Payloads.Count);
            var changed = false;
            foreach (var payload in original.Payloads)
            {
                if (payload is not TextPayload textPayload)
                {
                    payloads.Add(payload);
                    continue;
                }

                var text = textPayload.Text ?? string.Empty;
                var replaced = anonymizer.Replace(text);
                changed |= !string.Equals(text, replaced, StringComparison.Ordinal);
                payloads.Add(new TextPayload(replaced));
            }

            return changed ? new SeString(payloads).Encode() : bytes;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Chat log replay payload is invalid.");
            return null;
        }
    }

    private string GetFallbackLine(ChatLogReplayEntry entry, bool anonymousMode)
    {
        var text = !string.IsNullOrWhiteSpace(entry.NativeFormattedText)
            ? entry.NativeFormattedText
            : string.IsNullOrWhiteSpace(entry.SenderDisplayName) && string.IsNullOrWhiteSpace(entry.SenderText)
                ? entry.MessageText
                : $"{(string.IsNullOrWhiteSpace(entry.SenderDisplayName) ? entry.SenderText : entry.SenderDisplayName)}: {entry.MessageText}";
        return anonymousMode ? anonymizer.Replace(text) : text;
    }
}
