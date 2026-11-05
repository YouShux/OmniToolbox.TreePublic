using OmniToolbox.UI.Widgets;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using OmenTools;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Config;
using OmniToolbox.Teleport;
using OmniToolbox.UI.Theme;
using TreeTools.Text;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed class CoordinateNearestAetheryte
(
    CoordinateNearestAetheryteConfig config,
    ITeleportService                teleportService,
    IAetheryteRouteResolver          routeResolver
) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Daily/CoordinateNearestAetheryte-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "CoordinateNearestAetheryteTitle",
            ["Description"] = "CoordinateNearestAetheryteDescription",
            ["Feature.CoordinateNearestAetheryte.Column.Id"] = "Feature.CoordinateNearestAetheryte.Column.Id",
            ["Feature.CoordinateNearestAetheryte.Column.Map"] = "Feature.CoordinateNearestAetheryte.Column.Map",
            ["Feature.CoordinateNearestAetheryte.Column.Name"] = "Feature.CoordinateNearestAetheryte.Column.Name",
            ["Feature.CoordinateNearestAetheryte.Column.Name.Help"] = "Feature.CoordinateNearestAetheryte.Column.Name.Help",
            ["Feature.CoordinateNearestAetheryte.SearchHint"] = "Feature.CoordinateNearestAetheryte.SearchHint"
        });

    public override string ModuleName => nameof(CoordinateNearestAetheryte);

    private readonly Dictionary<uint, (uint CommandID, DalamudLinkPayload Payload)> aetheryteLinks          = [];
    private readonly HashSet<uint>                                                  aetheryteLinkCommandIDs = [];
    private string                                                                  SearchText              = string.Empty;

    protected override void OnStart(FeatureLifetime activation)
    {
        _ = routeResolver.Destinations;

        var lifetime = Activation;
        lifetime.Add(() =>
        {
            ClearRuntimeReferences();
        });
        lifetime.OnStopping(() => DalamudServices.ChatGUI.ChatMessage -= OnChatMessage);
        DalamudServices.ChatGUI.ChatMessage += OnChatMessage;
        lifetime.OnStopping(() => DalamudServices.ChatGUI.CheckMessageHandled -= OnChatMessage);
        DalamudServices.ChatGUI.CheckMessageHandled += OnChatMessage;
    }

    public override bool HasSettings => true;

    public override bool DrawSettings()
    {
        var changed = false;
        var aetherytes = routeResolver.Destinations;
        OmniControls.InputTextWithHint(
            "##coordinateNearestAetheryteSearch",
            OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.SearchHint"),
            ref SearchText,
            128,
            ImGui.GetContentRegionAvail().X);

        var query = SearchText.Trim();
        var filteredRowCount = 0;
        foreach (var aetheryte in aetherytes)
        {
            if (MatchesSearch(aetheryte, query))
            {
                filteredRowCount++;
            }
        }

        var rowHeight = MathF.Max(ImGui.GetFrameHeightWithSpacing(), OmniTheme.Scale(30f));
        string[] labels = [string.Empty, OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.Column.Name"),
            OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.Column.Map"), OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.Column.Id")];
        var idWidth = OmniTextMeasurements.Measure(uint.MaxValue.ToString()).X;
        using var table = OmniControls.DataTable(
            "##coordinateNearestAetheryteTable",
            labels, [OmniControls.MeasureCheckbox(string.Empty).X, OmniControls.MeasureTableHeader(labels[1], true).X, ImGui.GetFontSize() * 4f, idWidth],
            [OmniControls.MeasureCheckbox(string.Empty).X, OmniTheme.Scale(240f), OmniTheme.Scale(180f), idWidth],
            out var detailLayout,
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY,
            new Vector2(
                ImGui.GetContentRegionAvail().X,
                rowHeight * (Math.Min(filteredRowCount, 5) + 1) + ImGui.GetStyle().ItemSpacing.Y), stretchColumn: 1);
        if (!table)
        {
            return changed;
        }

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

        if (detailLayout)
        {
            OmniControls.SameLineOrWrap(OmniTextMeasurements.Measure(labels[1]).X);
            ImGui.TextUnformatted(labels[1]);
            OmniControls.HelpTooltip(OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.Column.Name.Help"));
        }
        else
        {
            OmniControls.TableHeader(labels[1], OmniLoc.Get(this, "Feature.CoordinateNearestAetheryte.Column.Name.Help"));
            OmniControls.TableHeader(labels[2]);
            OmniControls.TableHeader(labels[3]);
        }
        ImGui.TableSetColumnIndex(detailLayout ? 0 : 1);
        var nameWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.TableSetColumnIndex(detailLayout ? 0 : 2);
        var mapWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);

        foreach (var aetheryte in aetherytes)
        {
            if (!MatchesSearch(aetheryte, query))
            {
                continue;
            }

            var rowContentHeight = MathF.Max(ImGui.GetFrameHeight(),
                MathF.Max(OmniTextMeasurements.Measure(aetheryte.Name, false, nameWidth).Y,
                    OmniTextMeasurements.Measure(aetheryte.MapName, false, mapWidth).Y));
            ImGui.TableNextRow(ImGuiTableRowFlags.None, detailLayout ? 0f : rowContentHeight);
            ImGui.TableNextColumn();
            if (!detailLayout)
                OmniControls.CenterTableItem(new Vector2(OmniTheme.CheckboxSize()), rowContentHeight);
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
            OmniControls.NextTableField(labels[1], detailLayout);
            OmniControls.TableTextCentered(aetheryte.Name, detailLayout ? 0f : rowContentHeight, textColor);
            OmniControls.NextTableField(labels[2], detailLayout);
            OmniControls.TableTextCentered(aetheryte.MapName, detailLayout ? 0f : rowContentHeight, textColor);
            OmniControls.NextTableField(labels[3], detailLayout);
            OmniControls.TableTextCentered(aetheryte.ID.ToString(), detailLayout ? 0f : rowContentHeight, textColor);
        }

        return changed;
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        if (Activation.IsStopping)
            return;

        try
        {
            if (TryBuildMessage(chatMessage.Message, out var modified))
            {
                chatMessage.Message = modified;
            }
        }
        catch (Exception)
        {
        }
    }

    private bool TryBuildMessage
    (
        SeString     message,
        out SeString modified
    )
    {
        modified = message;
        var payloads = message.Payloads;
        MapLinkPayload? mapLink = null;
        var mapIndex = -1;
        for (var index = 0; index < payloads.Count; index++)
        {
            if (payloads[index] is DalamudLinkPayload link && aetheryteLinkCommandIDs.Contains(link.CommandId))
            {
                return false;
            }

            if (mapLink is null && payloads[index] is MapLinkPayload candidate)
            {
                mapLink  = candidate;
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
            if (payloads[index] is not RawPayload raw || !raw.Data.AsSpan().SequenceEqual(terminatorData))
            {
                continue;
            }

            insertIndex = index + 1;
            break;
        }

        var newPayloads = new List<Payload>(payloads.Count + route.Length * 8 + 1);
        for (var index = 0; index < insertIndex; index++)
        {
            newPayloads.Add(payloads[index]);
        }

        newPayloads.Add(new TextPayload(" \u2192 "));
        routeResolver.AppendRoutePayloads(
            newPayloads,
            route,
            GetAetheryteLinkPayload(route[0].AetheryteID));

        for (var index = insertIndex; index < payloads.Count; index++)
        {
            newPayloads.Add(payloads[index]);
        }

        modified = new SeString(newPayloads);
        return true;
    }

    private DalamudLinkPayload GetAetheryteLinkPayload(uint aetheryteID)
    {
        if (aetheryteLinks.TryGetValue(aetheryteID, out var link))
        {
            return link.Payload;
        }

        var payload = LinkPayloadManager.Instance().Reg(
            (_, _) => OnAetheryteLinkClicked(aetheryteID),
            out var commandID);
        aetheryteLinks.Add(aetheryteID, (commandID, payload));
        aetheryteLinkCommandIDs.Add(commandID);
        return payload;
    }

    private void OnAetheryteLinkClicked(uint aetheryteID)
    {
        if (!IsEnabled || Activation.IsStopping)
            return;

        var lifetime = Activation;
        lifetime.Track(DalamudServices.Framework.Run(() =>
        {
            if (!lifetime.IsStopping)
                teleportService.TryTeleport(aetheryteID);
        }, cancellationToken: lifetime.CancellationToken));
    }

    private void ClearRuntimeReferences()
    {
        foreach (var link in aetheryteLinks.Values)
        {
            LinkPayloadManager.Instance().Unreg(link.CommandID);
        }

        aetheryteLinks.Clear();
        aetheryteLinkCommandIDs.Clear();
    }

    private static bool MatchesSearch
    (
        AetheryteDestination aetheryte,
        string               query
    ) =>
        query.Length == 0 ||
        aetheryte.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        aetheryte.MapName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
