using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using OmniToolbox.Config;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.UI;
using OmniToolbox.UI.Controls;
using OmniToolbox.UI.Theme;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmniToolbox.Teleport;
using OmenTools;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed class CoordinateNearestAetheryte(
    CoordinateNearestAetheryteConfig config,
    TeleportService teleportService,
    AetheryteRouteResolver routeResolver) : ModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("CoordinateNearestAetheryteTitle"),
        Description = OmniLoc.Get("CoordinateNearestAetheryteDescription"),
        Category = ModuleCategory.Daily,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Daily/CoordinateNearestAetheryte-1.png"
    };

    private FeatureLifetime? runtimeLifetime;
    private DalamudLinkPayload? aetheryteLinkPayload;
    private uint aetheryteLinkCommandID;
    private static string search = string.Empty;

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var aetherytes = routeResolver.Destinations;
        OmniControls.InputTextWithHint(
            "##coordinateNearestAetheryteSearch",
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.SearchHint"),
            ref search,
            128,
            ImGui.GetContentRegionAvail().X);

        var query = search.Trim();
        var filteredRowCount = 0;
        foreach (var aetheryte in aetherytes)
        {
            if (MatchesSearch(aetheryte, query))
            {
                filteredRowCount++;
            }
        }

        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(30f));
        using var table = ImRaii.Table(
            "##coordinateNearestAetheryteTable",
            4,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * (Math.Min(filteredRowCount, 5) + 1) + ImGui.GetStyle().ItemSpacing.Y));
        if (!table)
        {
            return changed;
        }

        ImGui.TableSetupColumn(
            "##coordinateNearestAetheryteVisible",
            ImGuiTableColumnFlags.WidthFixed,
            ImGui.GetFrameHeight());
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"),
            ImGuiTableColumnFlags.WidthFixed,
            OmniTheme.Scale(92f));
        ImGui.TableSetupScrollFreeze(0, 1);
        OmniControls.BeginTableHeaderRow();
        ImGui.TableNextColumn();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
        var selectAll = config.IgnoredAetheryteIds.Count == 0;
        if (OmniControls.Checkbox("##coordinateNearestAetheryteSelectAll", ref selectAll))
        {
            if (selectAll)
            {
                config.IgnoredAetheryteIds.Clear();
            }
            else
            {
                foreach (var aetheryte in aetherytes)
                {
                    config.IgnoredAetheryteIds.Add(aetheryte.ID);
                }
            }

            changed = true;
        }

        OmniControls.TableHeader(
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name"),
            OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Name.Help"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Map"));
        OmniControls.TableHeader(OmniLoc.Get("Feature.CoordinateNearestAetheryte.Column.Id"));

        foreach (var aetheryte in aetherytes)
        {
            if (!MatchesSearch(aetheryte, query))
            {
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), OmniTheme.SmallButtonSize().Y);
            var visible = !config.IgnoredAetheryteIds.Contains(aetheryte.ID);
            if (OmniControls.Checkbox($"##coordinateNearestAetheryte{aetheryte.ID}", ref visible))
            {
                if (visible)
                {
                    config.IgnoredAetheryteIds.Remove(aetheryte.ID);
                }
                else
                {
                    config.IgnoredAetheryteIds.Add(aetheryte.ID);
                }

                changed = true;
            }

            var textColor = visible
                ? (Vector4?)null
                : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.Name, OmniTheme.SmallButtonSize().Y, textColor);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.MapName, OmniTheme.SmallButtonSize().Y, textColor);
            ImGui.TableNextColumn();
            OmniControls.TableTextCentered(aetheryte.ID.ToString(), OmniTheme.SmallButtonSize().Y, textColor);
        }

        return changed;
    }

    protected override void OnEnable()
    {
        _ = routeResolver.Destinations;

        var lifetime = new FeatureLifetime();
        try
        {
            var linkManager = LinkPayloadManager.Instance();
            aetheryteLinkPayload = linkManager.Reg(OnAetheryteLinkClicked, out aetheryteLinkCommandID);
            var commandID = aetheryteLinkCommandID;
            lifetime.Add(() => linkManager.Unreg(commandID));

            DalamudServices.ChatGUI.ChatMessage += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGUI.ChatMessage -= OnChatMessage);
            DalamudServices.ChatGUI.CheckMessageHandled += OnChatMessage;
            lifetime.Add(() => DalamudServices.ChatGUI.CheckMessageHandled -= OnChatMessage);
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
            if (payloads[index] is DalamudLinkPayload link && link.CommandId == aetheryteLinkCommandID)
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
        AetheryteRouteResolver.AppendRoutePayloads(newPayloads, route, aetheryteLinkPayload!);

        for (var index = insertIndex; index < payloads.Count; index++)
        {
            newPayloads.Add(payloads[index]);
        }

        modified = new SeString(newPayloads);
        return true;
    }

    private void OnAetheryteLinkClicked(uint commandID, SeString link)
    {
        if (commandID != aetheryteLinkCommandID || string.IsNullOrWhiteSpace(link.TextValue))
        {
            return;
        }

        _ = DalamudServices.Framework.RunOnFrameworkThread(() => teleportService.TryTeleport(link.TextValue));
    }

    private void ClearRuntimeReferences()
    {
        runtimeLifetime = null;
        aetheryteLinkPayload = null;
        aetheryteLinkCommandID = 0;
    }

    private static bool MatchesSearch(AetheryteDestination aetheryte, string query) =>
        query.Length == 0 ||
        aetheryte.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        aetheryte.MapName.Contains(query, StringComparison.OrdinalIgnoreCase);
}

[Serializable]
public sealed class CoordinateNearestAetheryteConfig
{
    public HashSet<uint> IgnoredAetheryteIds { get; set; } = [];
}
