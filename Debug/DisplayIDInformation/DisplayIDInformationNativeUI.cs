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

namespace OmniToolbox.TreePublic;

internal sealed unsafe class DisplayIDInformationNativeUI : IDisposable
{
    private static readonly CompSig StatusTooltipSig = new(
        "40 55 41 54 41 55 41 56 41 57 48 8D 6C 24 90 48 81 EC 70 01 00 00");
    private static readonly CompSig TooltipSig = new("E8 ?? ?? ?? ?? 49 63 47 ?? BB");

    private readonly DisplayIDInformationConfig config;
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
    private uint lastMapID;
    private uint lastTerritoryID;
    private uint lastWeatherID;
    private bool lastShowZone;
    private bool lastShowWeather;
    private nint lastTargetAddress;
    private uint lastTargetID;
    private bool lastTargetShow;
    private string? lastTargetText;
    private string? lastTargetName;

    private delegate CStringPointer GetStatusTooltipTextDelegate(
        AgentHUD* agent,
        Utf8String* output,
        uint statusID,
        uint param);

    private delegate void ShowTooltipDelegate(
        AtkTooltipManager* manager,
        AtkTooltipType type,
        ushort parentID,
        AtkResNode* targetNode,
        AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
        long unkDelegate,
        byte unk7,
        byte unk8);

    public DisplayIDInformationNativeUI(DisplayIDInformationConfig config)
    {
        this.config = config;
        tooltipManager = TooltipManager.Instance();
        addonEvents = new(DalamudServices.AddonLifecycle);
        itemTooltipHandler = (_, itemID, ref modifications) => OnItemTooltip(itemID, ref modifications);
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
        var mapID = GetCurrentMapID();
        var territoryID = DService.Instance().ClientState.TerritoryType;
        var weatherManager = WeatherManager.Instance();
        var weatherID = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (mapID == lastMapID &&
            territoryID == lastTerritoryID &&
            weatherID == lastWeatherID &&
            config.DisplayZoneInfo == lastShowZone &&
            config.DisplayWeatherID == lastShowWeather)
        {
            return;
        }

        lastMapID = mapID;
        lastTerritoryID = territoryID;
        lastWeatherID = weatherID;
        lastShowZone = config.DisplayZoneInfo;
        lastShowWeather = config.DisplayWeatherID;
        if (zoneInfoEntry is null)
        {
            return;
        }

        var text = DisplayIDInformationFormatter.FormatDtr(
            config.DisplayZoneInfo,
            config.DisplayWeatherID,
            mapID,
            territoryID,
            weatherID);
        zoneInfoEntry.Shown = text is not null;
        if (text is not null)
        {
            zoneInfoEntry.Text = text;
        }
    }

    public void Dispose() => lifetime.Dispose();

    private void OnItemTooltip(uint itemID, ref List<TooltipItemModification> modifications)
    {
        if (!config.DisplayItemID || itemID == 0)
        {
            return;
        }

        var iconID = config.DisplayIconID && LuminaGetter.TryGetRow<LuminaItem>(itemID, out var item)
            ? (uint)item.Icon
            : 0;
        using var builder = new RentedSeStringBuilder();
        builder.Builder
            .Append(tooltipManager.GetOriginalItemTooltipText(TooltipItemType.UICategory))
            .PushColorType(3)
            .Append(DisplayIDInformationFormatter.FormatIDSuffix(itemID, iconID, config.DisplayIconID))
            .PopColorType();
        modifications.Add(new()
        {
            Target = TooltipItemType.UICategory,
            Type = TooltipModificationType.Contribute,
            Text = builder.Builder.ToReadOnlySeString()
        });
    }

    private void OnActionTooltip(DetailKind _, uint actionID, ref List<TooltipActionModification> modifications)
    {
        if (!config.DisplayActionID || actionID == 0)
        {
            return;
        }

        var iconID = config.DisplayIconID && LuminaGetter.TryGetRow<LuminaAction>(actionID, out var action)
            ? (uint)action.Icon
            : 0;
        using var builder = new RentedSeStringBuilder();
        builder.Builder
            .Append(tooltipManager.GetOriginalActionTooltipText(TooltipActionType.Category))
            .PushColorType(3)
            .Append(DisplayIDInformationFormatter.FormatActionSuffix(
                AgentActionDetail.Instance()->OriginalId,
                actionID,
                config.DisplayActionIDResolved,
                config.DisplayActionIDOriginal,
                iconID,
                config.DisplayIconID))
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

        var baseID = target.ToStruct()->BaseId;
        if (baseID == 0)
        {
            RestoreTargetText();
            return;
        }

        var targetName = target.Name.ToString();
        var show = config.DisplayTargetID && DisplayIDInformationFormatter.ShouldDisplayTarget(target.ObjectKind, config);
        if (lastTargetAddress != target.Address ||
            lastTargetID != baseID ||
            lastTargetShow != show ||
            !string.Equals(lastTargetName, targetName, StringComparison.Ordinal) ||
            lastTargetText is null)
        {
            lastTargetAddress = target.Address;
            lastTargetID = baseID;
            lastTargetShow = show;
            lastTargetName = targetName;
            lastTargetText = show ? $"{targetName}  [{baseID}]" : targetName;
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
        ushort parentID,
        AtkResNode* targetNode,
        AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
        long unkDelegate,
        byte unk7,
        byte unk8)
    {
        if (!config.DisplayWeatherID ||
            tooltipArgs == null ||
            tooltipArgs->TextArgs.Text.Value == null)
        {
            tooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            return;
        }

        var text = Marshal.PtrToStringUTF8((nint)tooltipArgs->TextArgs.Text.Value);
        var weatherManager = WeatherManager.Instance();
        var weatherID = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (string.IsNullOrWhiteSpace(text) ||
            weatherID == 0 ||
            !LuminaGetter.TryGetRow<LuminaWeather>(weatherID, out var weather) ||
            !TryAppendWeatherID(text, weather.Name.ToString(), weatherID, out var modified))
        {
            tooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            return;
        }

        var originalText = tooltipArgs->TextArgs.Text;
        var bytes = Encoding.UTF8.GetBytes(modified + '\0');
        fixed (byte* pointer = bytes)
        {
            tooltipArgs->TextArgs.Text = pointer;
            try
            {
                tooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
            }
            finally
            {
                tooltipArgs->TextArgs.Text = originalText;
            }
        }
    }

    private CStringPointer OnStatusTooltip(AgentHUD* agent, Utf8String* output, uint statusID, uint param)
    {
        // 小队列表路径可能在状态提示请求中传入空的 AgentHUD 或输出缓冲区；原函数会写入 this + 0x20。
        if (DisplayIDInformationFormatter.ShouldBypassStatusTooltip((nint)agent, (nint)output))
        {
            return default;
        }

        var original = statusTooltipHook!.Original(agent, output, statusID, param);
        if (!config.DisplayStatusID || statusID is 0 or uint.MaxValue || !original.HasValue)
        {
            return original;
        }

        var text = original.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return original;
        }

        var iconID = config.DisplayIconID && LuminaGetter.TryGetRow<LuminaStatus>(statusID, out var status)
            ? status.Icon
            : 0;
        var modified = DisplayIDInformationFormatter.AppendStatusTooltipID(text, statusID, iconID, config.DisplayIconID);
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

        if (!config.DisplayWeatherID)
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
        if (!config.DisplayWeatherID)
        {
            return;
        }

        var weatherManager = WeatherManager.Instance();
        var weatherID = weatherManager == null ? 0u : weatherManager->WeatherId;
        if (weatherID == 0 || !LuminaGetter.TryGetRow<LuminaWeather>(weatherID, out var weather))
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

    private static void OnWeatherMouseOut(AtkEventType _, AtkUnitBase* addon, AtkEvent* unusedEvent, AtkEventData* unusedEventData) =>
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
        lastTargetID = 0;
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

    private static uint GetCurrentMapID()
    {
        var mapID = AgentMap.Instance()->CurrentMapId;
        if (mapID == 0)
        {
            mapID = AgentMap.Instance()->SelectedMapId;
        }

        return mapID == 0 ? GameMain.Instance()->CurrentMapId : mapID;
    }

    private static bool TryAppendWeatherID(string text, string weatherName, uint weatherID, out string modified)
    {
        modified = text;
        var newlineIndex = text.IndexOf('\n');
        var firstLine = newlineIndex < 0 ? text : text[..newlineIndex];
        if (!string.Equals(firstLine.Trim(), weatherName, StringComparison.Ordinal))
        {
            return false;
        }

        modified = $"{weatherName}[{weatherID}]" + (newlineIndex < 0 ? string.Empty : text[newlineIndex..]);
        return true;
    }
}
