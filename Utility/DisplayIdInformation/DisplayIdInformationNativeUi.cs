using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using InteropGenerator.Runtime;
using OmniToolbox.Config;
using OmniToolbox.Host;
using OmniToolbox.Lifecycle;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using LuminaWeather = Lumina.Excel.Sheets.Weather;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed unsafe class DisplayIdInformationNativeUi : IDisposable
{
    private static readonly CompSig StatusTooltipSig = new(
        "40 55 41 54 41 55 41 56 41 57 48 8D 6C 24 90 48 81 EC 70 01 00 00");
    private static readonly CompSig TooltipSig = new("E8 ?? ?? ?? ?? 49 63 47 ?? BB");

    private readonly DisplayIdInformationConfig config;
    private readonly TooltipManager tooltipManager;
    private readonly AddonEventRegistry addonEvents;
    private readonly FeatureLifetime lifetime = new();
    private readonly TooltipManager.ItemTooltipUpdateDelegate itemTooltipHandler;
    private readonly TooltipManager.ActionTooltipUpdateDelegate actionTooltipHandler;
    private readonly IAddonLifecycle.AddonEventDelegate naviMapHandler;
    private Hook<GetStatusTooltipTextDelegate>? statusTooltipHook;
    private Hook<ShowTooltipDelegate>? tooltipHook;
    private IDtrBarEntry? zoneInfoEntry;
    private AtkEventWrapper? weatherMouseOver;
    private AtkEventWrapper? weatherMouseOut;
    private AtkResNode* weatherCollisionNode;
    private uint lastMapId;
    private uint lastTerritoryId;
    private uint lastWeatherId;
    private bool lastShowZone;
    private bool lastShowWeather;
    private nint lastTargetAddress;
    private uint lastTargetId;
    private bool lastTargetShow;
    private string? lastTargetText;
    private string? lastTargetName;

    private delegate CStringPointer GetStatusTooltipTextDelegate(
        AgentHUD* agent,
        Utf8String* output,
        uint statusId,
        uint param);

    private delegate void ShowTooltipDelegate(
        AtkTooltipManager* manager,
        AtkTooltipType type,
        ushort parentId,
        AtkResNode* targetNode,
        AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
        long unkDelegate,
        byte unk7,
        byte unk8);

    public DisplayIdInformationNativeUi(DisplayIdInformationConfig config)
    {
        this.config = config;
        tooltipManager = TooltipManager.Instance();
        addonEvents = new(DalamudServices.AddonLifecycle);
        itemTooltipHandler = (_, itemId, ref modifications) => OnItemTooltip(itemId, ref modifications);
        actionTooltipHandler = OnActionTooltip;
        naviMapHandler = OnNaviMapAddon;

        try
        {
            zoneInfoEntry = DService.Instance().DTRBar.Get("OmniToolbox-DisplayIdInformation-ZoneInfo");
            lifetime.Add(RemoveDtrEntry);

            statusTooltipHook = StatusTooltipSig.GetHook<GetStatusTooltipTextDelegate>(OnStatusTooltip);
            lifetime.Add(DisposeStatusHook);
            statusTooltipHook.Enable();

            try
            {
                tooltipHook = TooltipSig.GetHook<ShowTooltipDelegate>(OnTooltip);
                tooltipHook.Enable();
            }
            catch (Exception ex)
            {
                tooltipHook?.Dispose();
                tooltipHook = null;
                DalamudServices.PluginLog.Warning(ex, "Display ID generic tooltip hook initialization failed.");
            }

            if (tooltipHook is not null)
            {
                lifetime.Add(DisposeTooltipHook);
            }

            tooltipManager.RegItem(itemTooltipHandler);
            lifetime.Add(() => tooltipManager.Unreg(itemTooltipHandler));
            tooltipManager.RegAction(actionTooltipHandler);
            lifetime.Add(() => tooltipManager.Unreg(actionTooltipHandler));

            lifetime.Add(RestoreTargetText);
            lifetime.Add(addonEvents.Dispose);
            addonEvents.Register(AddonEvent.PreDraw, "_TargetInfo", OnTargetAddon);
            addonEvents.Register(AddonEvent.PreDraw, "_TargetInfoMainTarget", OnTargetAddon);
            addonEvents.Register(AddonEvent.PostDraw, "_NaviMap", naviMapHandler);
            addonEvents.Register(AddonEvent.PreFinalize, "_NaviMap", naviMapHandler);
            lifetime.Add(ReleaseWeatherEvents);
            UpdateDtr();
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    public void UpdateDtr()
    {
        var mapId = GetCurrentMapId();
        var territoryId = DService.Instance().ClientState.TerritoryType;
        var weatherManager = WeatherManager.Instance();
        var weatherId = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (mapId == lastMapId &&
            territoryId == lastTerritoryId &&
            weatherId == lastWeatherId &&
            config.DisplayZoneInfo == lastShowZone &&
            config.DisplayWeatherId == lastShowWeather)
        {
            return;
        }

        lastMapId = mapId;
        lastTerritoryId = territoryId;
        lastWeatherId = weatherId;
        lastShowZone = config.DisplayZoneInfo;
        lastShowWeather = config.DisplayWeatherId;
        if (zoneInfoEntry is null)
        {
            return;
        }

        var text = DisplayIdInformationFormatter.FormatDtr(
            config.DisplayZoneInfo,
            config.DisplayWeatherId,
            mapId,
            territoryId,
            weatherId);
        zoneInfoEntry.Shown = text is not null;
        if (text is not null)
        {
            zoneInfoEntry.Text = text;
        }
    }

    public void Dispose() => lifetime.Dispose();

    private void OnItemTooltip(uint itemId, ref List<TooltipItemModification> modifications)
    {
        if (!config.DisplayItemId || itemId == 0)
        {
            return;
        }

        var iconId = config.DisplayIconId && LuminaGetter.TryGetRow<LuminaItem>(itemId, out var item)
            ? (uint)item.Icon
            : 0;
        using var builder = new RentedSeStringBuilder();
        builder.Builder
            .Append(tooltipManager.GetOriginalItemTooltipText(TooltipItemType.UICategory))
            .PushColorType(3)
            .Append(DisplayIdInformationFormatter.FormatIdSuffix(itemId, iconId, config.DisplayIconId))
            .PopColorType();
        modifications.Add(new()
        {
            Target = TooltipItemType.UICategory,
            Type = TooltipModificationType.Contribute,
            Text = builder.Builder.ToReadOnlySeString()
        });
    }

    private void OnActionTooltip(DetailKind _, uint actionId, ref List<TooltipActionModification> modifications)
    {
        if (!config.DisplayActionId || actionId == 0)
        {
            return;
        }

        var iconId = config.DisplayIconId && LuminaGetter.TryGetRow<LuminaAction>(actionId, out var action)
            ? (uint)action.Icon
            : 0;
        using var builder = new RentedSeStringBuilder();
        builder.Builder
            .Append(tooltipManager.GetOriginalActionTooltipText(TooltipActionType.Category))
            .PushColorType(3)
            .Append(DisplayIdInformationFormatter.FormatActionSuffix(
                AgentActionDetail.Instance()->OriginalId,
                actionId,
                config.DisplayActionIdResolved,
                config.DisplayActionIdOriginal,
                iconId,
                config.DisplayIconId))
            .PopColorType();
        modifications.Add(new()
        {
            Target = TooltipActionType.Category,
            Type = TooltipModificationType.Contribute,
            Text = builder.Builder.ToReadOnlySeString()
        });
    }

    private void OnTargetAddon(AddonEvent _, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible || addon->RootNode == null || !addon->RootNode->IsVisible())
        {
            return;
        }

        var targetPointer = TargetSystem.Instance()->Target;
        if (targetPointer == null ||
            DService.Instance().ObjectTable.CreateObjectReference((nint)targetPointer) is not { } target)
        {
            RestoreTargetText();
            return;
        }

        var baseId = target.ToStruct()->BaseId;
        if (baseId == 0)
        {
            RestoreTargetText();
            return;
        }

        var targetName = target.Name.ToString();
        var show = config.DisplayTargetId && DisplayIdInformationFormatter.ShouldDisplayTarget(target.ObjectKind, config);
        if (lastTargetAddress != target.Address ||
            lastTargetId != baseId ||
            lastTargetShow != show ||
            !string.Equals(lastTargetName, targetName, StringComparison.Ordinal) ||
            lastTargetText is null)
        {
            lastTargetAddress = target.Address;
            lastTargetId = baseId;
            lastTargetShow = show;
            lastTargetName = targetName;
            lastTargetText = show ? $"{targetName}  [{baseId}]" : targetName;
        }

        var stringArray = AtkStage.Instance()->GetStringArrayData(StringArrayType.Hud2);
        if (stringArray != null)
        {
            stringArray->SetValueAndUpdate(0, lastTargetText);
        }
    }

    private void OnTooltip(
        AtkTooltipManager* manager,
        AtkTooltipType type,
        ushort parentId,
        AtkResNode* targetNode,
        AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
        long unkDelegate,
        byte unk7,
        byte unk8)
    {
        if (!config.DisplayWeatherId ||
            tooltipArgs == null ||
            tooltipArgs->TextArgs.Text.Value == null)
        {
            tooltipHook!.Original(manager, type, parentId, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            return;
        }

        var text = Marshal.PtrToStringUTF8((nint)tooltipArgs->TextArgs.Text.Value);
        var weatherManager = WeatherManager.Instance();
        var weatherId = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (string.IsNullOrWhiteSpace(text) ||
            weatherId == 0 ||
            !LuminaGetter.TryGetRow<LuminaWeather>(weatherId, out var weather) ||
            !TryAppendWeatherId(text, weather.Name.ToString(), weatherId, out var modified))
        {
            tooltipHook!.Original(manager, type, parentId, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            return;
        }

        var originalText = tooltipArgs->TextArgs.Text;
        var bytes = Encoding.UTF8.GetBytes(modified + '\0');
        fixed (byte* pointer = bytes)
        {
            tooltipArgs->TextArgs.Text = pointer;
            try
            {
                tooltipHook!.Original(manager, type, parentId, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            }
            finally
            {
                tooltipArgs->TextArgs.Text = originalText;
            }
        }
    }

    private CStringPointer OnStatusTooltip(AgentHUD* agent, Utf8String* output, uint statusId, uint param)
    {
        var original = statusTooltipHook!.Original(agent, output, statusId, param);
        if (!config.DisplayStatusId || statusId is 0 or uint.MaxValue || !original.HasValue)
        {
            return original;
        }

        var text = original.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return original;
        }

        var iconId = config.DisplayIconId && LuminaGetter.TryGetRow<LuminaStatus>(statusId, out var status)
            ? status.Icon
            : 0;
        var modified = DisplayIdInformationFormatter.AppendStatusTooltipId(text, statusId, iconId, config.DisplayIconId);
        if (string.Equals(text, modified, StringComparison.Ordinal))
        {
            return original;
        }

        using var utf8String = new Utf8String(modified);
        output->Copy(&utf8String);
        return output->StringPtr;
    }

    private void OnNaviMapAddon(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PreFinalize)
        {
            DisposeWeatherWrappers();
            return;
        }

        if (!config.DisplayWeatherId)
        {
            DisposeWeatherWrappers();
            return;
        }

        if (weatherMouseOver is not null && weatherMouseOut is not null)
        {
            return;
        }

        var addon = (AtkUnitBase*)args.Addon.Address;
        var component = addon == null ? null : addon->GetComponentByNodeId(14);
        var collisionNode = component == null ? null : component->UldManager.SearchNodeById(5);
        if (collisionNode == null)
        {
            return;
        }

        AtkEventWrapper? mouseOver = null;
        AtkEventWrapper? mouseOut = null;
        try
        {
            mouseOver = new(OnWeatherMouseOver);
            mouseOver.Add(addon, collisionNode, AtkEventType.MouseOver);
            mouseOut = new(OnWeatherMouseOut);
            mouseOut.Add(addon, collisionNode, AtkEventType.MouseOut);
            weatherCollisionNode = collisionNode;
            weatherMouseOver = mouseOver;
            weatherMouseOut = mouseOut;
        }
        catch
        {
            mouseOver?.Dispose();
            mouseOut?.Dispose();
            throw;
        }
    }

    private void OnWeatherMouseOver(AtkEventType _, AtkUnitBase* addon, AtkEvent* atkEvent, AtkEventData* data)
    {
        if (!config.DisplayWeatherId)
        {
            return;
        }

        var weatherManager = WeatherManager.Instance();
        var weatherId = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (weatherId == 0 || !LuminaGetter.TryGetRow<LuminaWeather>(weatherId, out var weather))
        {
            return;
        }

        using var builder = new RentedSeStringBuilder();
        using var values = new RentedAtkValues(1);
        values[0].SetManagedString(builder.Builder.Append($"{weather.Name} [{weather.RowId}]").GetViewAsSpan());
        var tooltipArgs = new AtkTooltipManager.AtkTooltipArgs();
        tooltipArgs.TextArgs.AtkArrayType = 0;
        tooltipArgs.TextArgs.Text = values[0].String;
        AtkStage.Instance()->TooltipManager.ShowTooltip(
            AtkTooltipType.Text,
            addon->Id,
            weatherCollisionNode,
            &tooltipArgs);
    }

    private static void OnWeatherMouseOut(AtkEventType _, AtkUnitBase* addon, AtkEvent* __, AtkEventData* ___) =>
        AtkStage.Instance()->TooltipManager.HideTooltip(addon->Id);

    private void ReleaseWeatherEvents()
    {
        List<Exception>? errors = null;
        try
        {
            addonEvents.Release(AddonEvent.PostDraw, "_NaviMap", naviMapHandler);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            addonEvents.Release(AddonEvent.PreFinalize, "_NaviMap", naviMapHandler);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        DisposeWeatherWrappers();
        if (errors is not null)
        {
            throw new AggregateException("Display ID weather events failed to unregister.", errors);
        }
    }

    private void DisposeWeatherWrappers()
    {
        weatherMouseOver?.Dispose();
        weatherMouseOver = null;
        weatherMouseOut?.Dispose();
        weatherMouseOut = null;
        weatherCollisionNode = null;
    }

    private void RestoreTargetText()
    {
        var targetPointer = TargetSystem.Instance()->Target;
        var targetName = targetPointer != null &&
                         DService.Instance().ObjectTable.CreateObjectReference((nint)targetPointer) is { } target
            ? target.Name.ToString()
            : string.Empty;

        var stringArray = AtkStage.Instance()->GetStringArrayData(StringArrayType.Hud2);
        if (stringArray != null)
        {
            stringArray->SetValueAndUpdate(0, targetName);
        }

        lastTargetAddress = nint.Zero;
        lastTargetId = 0;
        lastTargetShow = false;
        lastTargetText = null;
        lastTargetName = null;
    }

    private void DisposeStatusHook()
    {
        statusTooltipHook?.Dispose();
        statusTooltipHook = null;
    }

    private void DisposeTooltipHook()
    {
        tooltipHook?.Dispose();
        tooltipHook = null;
    }

    private void RemoveDtrEntry()
    {
        zoneInfoEntry?.Remove();
        zoneInfoEntry = null;
    }

    private static uint GetCurrentMapId()
    {
        var mapId = AgentMap.Instance()->CurrentMapId;
        if (mapId == 0)
        {
            mapId = AgentMap.Instance()->SelectedMapId;
        }

        return mapId == 0 ? GameMain.Instance()->CurrentMapId : mapId;
    }

    private static bool TryAppendWeatherId(string text, string weatherName, uint weatherId, out string modified)
    {
        modified = text;
        var newlineIndex = text.IndexOf('\n');
        var firstLine = newlineIndex < 0 ? text : text[..newlineIndex];
        if (!string.Equals(firstLine.Trim(), weatherName, StringComparison.Ordinal))
        {
            return false;
        }

        modified = $"{weatherName}[{weatherId}]" + (newlineIndex < 0 ? string.Empty : text[newlineIndex..]);
        return true;
    }
}
