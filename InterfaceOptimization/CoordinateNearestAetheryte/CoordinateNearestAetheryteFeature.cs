using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Teleport;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

public sealed class CoordinateNearestAetheryteFeature(
    CoordinateNearestAetheryteConfig config,
    TeleportService teleportService,
    AetheryteRouteResolver routeResolver) : TreeHouseFeatureBase(new(
    "coordinate-nearest-aetheryte",
    "Feature.CoordinateNearestAetheryte.Title",
    "Feature.CoordinateNearestAetheryte.Description",
    TreeHouseCategory.InterfaceOptimization))
{
    private FeatureLifetime? runtimeLifetime;
    private DalamudLinkPayload? aetheryteLinkPayload;
    private uint aetheryteLinkCommandId;

    public override bool HasSettings => true;

    public override bool DrawSettings() =>
        CoordinateNearestAetherytePanel.Draw(config, routeResolver.Destinations);

    protected override void OnEnable()
    {
        _ = routeResolver.Destinations;

        var lifetime = new FeatureLifetime();
        try
        {
            var linkManager = LinkPayloadManager.Instance();
            aetheryteLinkPayload = linkManager.Reg(OnAetheryteLinkClicked, out aetheryteLinkCommandId);
            var commandId = aetheryteLinkCommandId;
            lifetime.Add(() => linkManager.Unreg(commandId));

            DalamudServices.ChatGui.ChatMessage += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGui.ChatMessage -= OnChatMessage);
            DalamudServices.ChatGui.CheckMessageHandled += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGui.CheckMessageHandled -= OnChatMessage);
            runtimeLifetime = lifetime;
        }
        catch
        {
            try
            {
                lifetime.Dispose();
            }
            finally
            {
                ClearRuntimeReferences();
            }

            throw;
        }
    }

    protected override void OnDisable()
    {
        try
        {
            runtimeLifetime?.Dispose();
        }
        finally
        {
            ClearRuntimeReferences();
        }
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        try
        {
            if (TryBuildMessage(chatMessage.Message, out var modified))
            {
                chatMessage.Message = modified;
            }
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Coordinate nearest aetheryte message processing failed.");
        }
    }

    private bool TryBuildMessage(SeString message, out SeString modified)
    {
        modified = message;
        var payloads = message.Payloads;
        MapLinkPayload? mapLink = null;
        var mapIndex = -1;
        for (var index = 0; index < payloads.Count; index++)
        {
            if (payloads[index] is DalamudLinkPayload link && link.CommandId == aetheryteLinkCommandId)
            {
                return false;
            }

            if (mapLink is null && payloads[index] is MapLinkPayload candidate)
            {
                mapLink = candidate;
                mapIndex = index;
            }
        }

        if (mapLink is null ||
            !DService.Instance().ClientState.IsLoggedIn ||
            !routeResolver.TryResolve(mapLink, config.IgnoredAetheryteIds, out var route))
        {
            return false;
        }

        var insertIndex = payloads.Count;
        var terminatorData = RawPayload.LinkTerminator.Data;
        for (var index = mapIndex + 1; index < payloads.Count; index++)
        {
            if (payloads[index] is not RawPayload raw || raw.Data.Length != terminatorData.Length)
            {
                continue;
            }

            var isTerminator = true;
            for (var dataIndex = 0; dataIndex < terminatorData.Length; dataIndex++)
            {
                if (raw.Data[dataIndex] == terminatorData[dataIndex])
                {
                    continue;
                }

                isTerminator = false;
                break;
            }

            if (isTerminator)
            {
                insertIndex = index + 1;
                break;
            }
        }

        var newPayloads = new List<Payload>(payloads.Count + route.Length * 8 + 1);
        for (var index = 0; index < insertIndex; index++)
        {
            newPayloads.Add(payloads[index]);
        }

        newPayloads.Add(new TextPayload(" \u2192 "));
        for (var index = 0; index < route.Length; index++)
        {
            if (index > 0)
            {
                newPayloads.Add(new TextPayload(" "));
                newPayloads.Add(new IconPayload((BitmapFontIcon)16));
                newPayloads.Add(new TextPayload(" "));
            }

            var segment = route[index];
            if (segment.Kind == AetheryteRouteSegmentKind.Boundary)
            {
                newPayloads.Add(new IconPayload((BitmapFontIcon)74));
                newPayloads.Add(new TextPayload(segment.Name));
                continue;
            }

            if (!segment.CanTeleportDirectly)
            {
                newPayloads.Add(new IconPayload((BitmapFontIcon)66));
                newPayloads.Add(new TextPayload(segment.Name));
                continue;
            }

            newPayloads.Add(aetheryteLinkPayload!);
            newPayloads.Add(new IconPayload((BitmapFontIcon)65));
            newPayloads.Add(new UIForegroundPayload(69));
            newPayloads.AddRange(SeString.TextArrowPayloads);
            newPayloads.Add(new TextPayload(segment.Name));
            newPayloads.Add(UIForegroundPayload.UIForegroundOff);
            newPayloads.Add(RawPayload.LinkTerminator);
        }

        for (var index = insertIndex; index < payloads.Count; index++)
        {
            newPayloads.Add(payloads[index]);
        }

        modified = new SeString(newPayloads);
        return true;
    }

    private void OnAetheryteLinkClicked(uint commandId, SeString link)
    {
        if (commandId != aetheryteLinkCommandId || string.IsNullOrWhiteSpace(link.TextValue))
        {
            return;
        }

        _ = DalamudServices.Framework.RunOnFrameworkThread(() => teleportService.TryTeleport(link.TextValue));
    }

    private void ClearRuntimeReferences()
    {
        runtimeLifetime = null;
        aetheryteLinkPayload = null;
        aetheryteLinkCommandId = 0;
    }
}
