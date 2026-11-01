using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using InteropGenerator.Runtime;
using Lumina.Text.ReadOnly;
using OmenTools;
using OmenTools.Interop.Game.Lumina;
using OmenTools.Interop.Game.Models;
using OmenTools.OmenService;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaStatus = Lumina.Excel.Sheets.Status;
using LuminaWeather = Lumina.Excel.Sheets.Weather;

namespace OmniToolbox.TreePublic;

public sealed partial class DisplayIDInformation
{
    private sealed unsafe class DisplayIDInformationNativeUI : IDisposable
    {
        private static readonly CompSig StatusTooltipSig = new(
            "40 55 41 54 41 55 41 56 41 57 48 8D 6C 24 90 48 81 EC 70 01 00 00");
        private delegate CStringPointer GetStatusTooltipTextDelegate
        (
            AgentHUD*   agent,
            Utf8String* output,
            uint        statusID,
            uint        param
        );
        private Hook<GetStatusTooltipTextDelegate>? GetStatusTooltipTextHook;

        private static readonly CompSig TooltipSig = new("E8 ?? ?? ?? ?? 49 63 47 ?? BB");
        private delegate void ShowTooltipDelegate
        (
            AtkTooltipManager*                manager,
            AtkTooltipType                    type,
            ushort                            parentID,
            AtkResNode*                       targetNode,
            AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
            long                              unkDelegate,
            byte                              unk7,
            byte                              unk8
        );
        private Hook<ShowTooltipDelegate>? ShowTooltipHook;

        private readonly DisplayIDInformationConfig                 config;
        private readonly TooltipManager                             tooltipManager;
        private readonly AddonEventRegistry                         addonEvents;
        private readonly FeatureLifetime                            lifetime             = new();
        private readonly TooltipManager.ItemTooltipUpdateDelegate   itemTooltipHandler;
        private readonly TooltipManager.ActionTooltipUpdateDelegate actionTooltipHandler;
        private readonly Dictionary<ReadOnlySeString, byte[]>        statusTooltipTexts    = [];
        private IDtrBarEntry?                                       zoneInfoEntry;
        private uint                                                lastMapID;
        private uint                                                lastTerritoryID;
        private uint                                                lastWeatherID;
        private bool                                                lastShowZone;
        private bool                                                lastShowWeather;
        private nint                                                lastTargetAddress;
        private uint                                                lastTargetID;
        private bool                                                lastTargetShow;
        private string?                                             lastTargetText;
        private string?                                             lastTargetName;

        public DisplayIDInformationNativeUI(DisplayIDInformationConfig config)
        {
            this.config          = config;
            tooltipManager       = TooltipManager.Instance();
            addonEvents          = new(DalamudServices.AddonLifecycle);
            itemTooltipHandler   = (_, itemID, ref modifications) => OnItemTooltip(itemID, ref modifications);
            actionTooltipHandler = OnActionTooltip;

            try
            {
                zoneInfoEntry = DService.Instance().DTRBar.Get("OmniToolbox-DisplayIdInformation-ZoneInfo");
                lifetime.Add(RemoveDtrEntry);
                lifetime.Add(statusTooltipTexts.Clear);

                GetStatusTooltipTextHook = StatusTooltipSig.GetHook<GetStatusTooltipTextDelegate>(OnStatusTooltip);
                lifetime.Add(DisposeStatusHook);
                GetStatusTooltipTextHook.Enable();

                try
                {
                    ShowTooltipHook = TooltipSig.GetHook<ShowTooltipDelegate>(OnTooltip);
                    ShowTooltipHook.Enable();
                }
                catch (Exception exception)
                {
                    ShowTooltipHook?.Dispose();
                    ShowTooltipHook = null;
                    DalamudServices.PluginLog.Warning(exception,
                        "[DisplayIDInformation] Weather tooltip hook initialization failed.");
                }

                if (ShowTooltipHook is not null)
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

            lastMapID       = mapID;
            lastTerritoryID = territoryID;
            lastWeatherID   = weatherID;
            lastShowZone    = config.DisplayZoneInfo;
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

        private void OnItemTooltip
        (
            uint                              itemID,
            ref List<TooltipItemModification> modifications
        )
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

        private void OnActionTooltip
        (
            DetailKind                          _,
            uint                                actionID,
            ref List<TooltipActionModification> modifications
        )
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

        private void OnTargetAddon
        (
            AddonEvent _,
            AddonArgs  args
        )
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
                lastTargetID      = baseID;
                lastTargetShow    = show;
                lastTargetName    = targetName;
                lastTargetText    = show ? $"{targetName}  [{baseID}]" : targetName;
            }

            var stringArray = AtkStage.Instance()->GetStringArrayData(StringArrayType.Hud2);
            if (stringArray != null)
            {
                stringArray->SetValueAndUpdate(0, lastTargetText);
            }
        }

        private void OnTooltip
        (
            AtkTooltipManager*                manager,
            AtkTooltipType                    type,
            ushort                            parentID,
            AtkResNode*                       targetNode,
            AtkTooltipManager.AtkTooltipArgs* tooltipArgs,
            long                              unkDelegate,
            byte                              unk7,
            byte                              unk8
        )
        {
            if (!config.DisplayWeatherID ||
                type != AtkTooltipType.Text ||
                tooltipArgs == null ||
                tooltipArgs->TextArgs.Text.Value == null)
            {
                ShowTooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
                return;
            }

            var text = new ReadOnlySeStringSpan(tooltipArgs->TextArgs.Text.Value);
            var weatherManager = WeatherManager.Instance();
            var weatherID = weatherManager == null ? 0u : weatherManager->WeatherId;
            if (weatherID == 0 || !LuminaGetter.TryGetRow<LuminaWeather>(weatherID, out var weather))
            {
                ShowTooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
                return;
            }

            if (!TryAppendWeatherID(text, weather.Name.ToString(), weatherID, out var modified))
            {
                ShowTooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
                return;
            }

            var originalText = tooltipArgs->TextArgs.Text;
            var bytes = new byte[modified.Length + 1];
            modified.CopyTo(bytes, 0);
            fixed (byte* pointer = bytes)
            {
                tooltipArgs->TextArgs.Text = pointer;
                try
                {
                    ShowTooltipHook!.Original(manager, type, parentID, targetNode, tooltipArgs, unkDelegate, unk7, unk8);
                }
                finally
                {
                    tooltipArgs->TextArgs.Text = originalText;
                }
            }
        }

        private CStringPointer OnStatusTooltip
        (
            AgentHUD*   agent,
            Utf8String* output,
            uint        statusID,
            uint        param
        )
        {
            if (agent == null)
            {
                return default;
            }

            var original = GetStatusTooltipTextHook!.Original(agent, output, statusID, param);
            if (!config.DisplayStatusID || statusID is 0 or uint.MaxValue || !original.HasValue)
            {
                return original;
            }

            var iconID = config.DisplayIconID && LuminaGetter.TryGetRow<LuminaStatus>(statusID, out var status)
                ? status.Icon
                : 0;
            if (!DisplayIDInformationFormatter.TryAppendTooltipID(
                    original.AsSpan(), statusID, iconID, config.DisplayIconID, out var modified))
            {
                return original;
            }

            if (output != null)
            {
                output->SetString(modified);
                return output->StringPtr;
            }

            var cacheKey = new ReadOnlySeString(modified);
            if (!statusTooltipTexts.TryGetValue(cacheKey, out var buffer))
            {
                buffer = GC.AllocateArray<byte>(modified.Length + 1, pinned: true);
                modified.CopyTo(buffer, 0);
                statusTooltipTexts.Add(new ReadOnlySeString(buffer.AsMemory(0, modified.Length)), buffer);
            }
            fixed (byte* pointer = buffer)
            {
                return pointer;
            }
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
            lastTargetID      = 0;
            lastTargetShow    = false;
            lastTargetText    = null;
            lastTargetName    = null;
        }

        private void DisposeStatusHook()
        {
            GetStatusTooltipTextHook?.Dispose();
            GetStatusTooltipTextHook = null;
        }

        private void DisposeTooltipHook()
        {
            ShowTooltipHook?.Dispose();
            ShowTooltipHook = null;
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

        private static bool TryAppendWeatherID
        (
            ReadOnlySeStringSpan text,
            string               weatherName,
            uint                 weatherID,
            out byte[]           modified
        )
        {
            modified = [];
            if (text.IsEmpty)
                return false;

            var plainText = text.ExtractText();
            var newlineIndex = plainText.IndexOf('\n');
            var firstLine = newlineIndex < 0 ? plainText : plainText[..newlineIndex];
            if (!string.Equals(firstLine.Trim(), weatherName, StringComparison.Ordinal))
            {
                return false;
            }

            return DisplayIDInformationFormatter.TryAppendTooltipID(text, weatherID, 0, false, out modified);
        }
    }

}
