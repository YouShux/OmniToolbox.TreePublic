// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using OmenTools.Interop.Game.Helpers;
using OmniToolbox.Items;
using CharaMakeCustomize = Lumina.Excel.Sheets.CharaMakeCustomize;
using Glasses = Lumina.Excel.Sheets.Glasses;
using Item = Lumina.Excel.Sheets.Item;
using KernelTexture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal sealed unsafe class BetterGlamourPortraitPreview(FeatureLifetime lifetime) : IAsyncDisposable
    {
        private OmniPreviewContext? context;
        private bool currentTextureValid;
        private Task nativeStop = Task.CompletedTask;
        private readonly RemotePreviewFrames phoneFrames = new(lifetime);
        private string cachedPhoneStatus = "{\"ready\":false}";
        private volatile bool enabled;
        private Func<IReadOnlyList<BetterGlamourPreset>> getPresets = null!;
        private Func<CustomizeData?>? getBaseHairAppearance;
        private CustomizeData? baselineHairAppearance;
        private readonly object commandLock = new();
        private Plan? pendingPreset;
        private long due;
        private int generation;
        private int selected;
        private readonly ConcurrentQueue<Action> commands = new();
        private Plan[] plans = [];
        private nint ownedTryon;
        private bool ownsTryon;
        private bool modelSubmitted;
        private bool outfitVerified;
        private bool previousSaveOutfit;
        private bool previousDisplayGear;
        private AgentTryon.TryOnItem[]? savedTryonItems;
        private nint player;
        private nint texture;
        private nint image;
        private nint hiddenTryonRoot;
        private bool savedDisableShowOnOpen;
        private bool savedDisableFocusOnShow;
        private bool savedDisableFocusability;
        private bool savedTryonClickThrough;
        private bool closingTryon;
        private byte savedTryonAlpha;
        private nint guardedTryonAddon;
        private int suppressedDraws;
        private uint renderIndex;
        private bool shown;
        private int frameWidth;
        private int frameHeight;
        private int verifiedFrames;
        private double loadingSeconds;
        private double customizationRetryAt;
        private int customizationAttempts;
        private float ratio = 0.75f;
        private float yaw;
        private float pitch;
        private float zoom;
        private float panX;
        private float panY;
        private CharaViewModelData desired;
        private readonly bool[] mask = new bool[10];
        private string phase = "idle";
        private string message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Hint");
        private string currentPlan = string.Empty;
        private long requestID;
        private ICallGateProvider<string>? listIpc;
        private ICallGateProvider<string>? statusIpc;
        private ICallGateProvider<string, string>? requestIpc;
        private ICallGateProvider<nint, int, int, long, object>? frameIpc;
        private static readonly JsonSerializerOptions JSON = new()
        {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private AgentTryon* Tryon
        {
            get
            {
                if (!ownsTryon)
                    return null;
                var agent = AgentTryon.Instance();
                return (nint)agent == ownedTryon ? agent : null;
            }
        }

        private CharaView* View
        {
            get
            {
                var agent = Tryon;
                return agent == null ? null : &agent->CharaView.CharaView;
            }
        }

        public nint Texture => currentTextureValid && pendingPreset == null && shown ? texture : 0;
        public float AspectRatio => ratio > 0 ? ratio : 0.75f;
        public string Message => pendingPreset == null ? message : OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Loading");

        public void Initialize(OmniPreviewContext previewContext, Func<IReadOnlyList<BetterGlamourPreset>> presets, Func<CustomizeData?>? baseHairAppearance = null)
        {
            ArgumentNullException.ThrowIfNull(previewContext);
            ArgumentNullException.ThrowIfNull(previewContext.PluginInterface);
            ArgumentNullException.ThrowIfNull(previewContext.Framework);
            ArgumentNullException.ThrowIfNull(previewContext.AddonLifecycle);
            ArgumentNullException.ThrowIfNull(presets);
            context = previewContext;
            getPresets = presets;
            getBaseHairAppearance = baseHairAppearance;
        }

        public void Enable()
        {
            if (enabled || context == null)
                return;
            enabled = true;
            generation++;
            try
            {
                listIpc = context.PluginInterface.GetIpcProvider<string>("Omni.BetterGlamourPreview.List.V1");
                statusIpc = context.PluginInterface.GetIpcProvider<string>("Omni.BetterGlamourPreview.Status.V1");
                requestIpc = context.PluginInterface.GetIpcProvider<string, string>("Omni.BetterGlamourPreview.Request.V1");
                frameIpc = context.PluginInterface.GetIpcProvider<nint, int, int, long, object>("Omni.BetterGlamourPreview.Frame.V1");
                listIpc.RegisterFunc(() => JsonSerializer.Serialize(plans, JSON));
                statusIpc.RegisterFunc(Status);
                requestIpc.RegisterFunc(Submit);
                context.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, ["Tryon", "FittingRoom"], OnTryonLifecycle);
                context.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, ["Tryon", "FittingRoom"], OnTryonLifecycle);
                context.Framework.Update += OnFrame;
                ReadPlans();
            }
            catch
            {
                try
                {
                    Disable();
                }
                catch (Exception)
                {
                }
                throw;
            }
        }

        public void Disable()
        {
            lock (commandLock)
            {
                if (!enabled)
                    return;
                enabled = false;
                generation++;
                pendingPreset = null;
                commands.Clear();
            }

            currentTextureValid = false;
            phoneFrames.Reset();
            Volatile.Write(ref cachedPhoneStatus, Status());
            if (context == null)
                return;
            context.Framework.Update -= OnFrame;
            listIpc?.UnregisterFunc();
            statusIpc?.UnregisterFunc();
            requestIpc?.UnregisterFunc();
            listIpc = null;
            statusIpc = null;
            requestIpc = null;
            frameIpc = null;
            if (context.Framework.IsInFrameworkUpdateThread)
                StopAndUnregister();
            else
                nativeStop = context.Framework.RunOnFrameworkThread(StopAndUnregister);
        }

        public ValueTask DisposeAsync()
        {
            Disable();
            return new(Task.WhenAll(nativeStop, phoneFrames.DisposeAsync().AsTask()));
        }
        public string PhoneFrame() => phoneFrames.Read(Status());
        public string CachedPhoneFrame() => phoneFrames.Read(Volatile.Read(ref cachedPhoneStatus));
        public string Status() => JsonSerializer.Serialize(new
        {
            version = "1.4.4",
            phase = pendingPreset == null ? phase : "loading",
            message = Message,
            currentPlan,
            requestId = requestID,
            ready = enabled && currentTextureValid && pendingPreset == null && shown && image != 0 && outfitVerified,
            displayReady = enabled && currentTextureValid && pendingPreset == null && shown && image != 0,
            outfitVerified,
            modelSubmitted,
            renderIndex,
            width = frameWidth,
            height = frameHeight,
            appliesToPlayer = false,
            usesNativeTryOn = true,
            closingTryon,
            suppressedDraws
        }, JSON);

        public string Submit(string json)
        {
            if (!enabled)
                return "{\"accepted\":false,\"error\":\"disabled\"}";
            try
            {
                if (json == null || json.Length > 64 * 1024)
                    throw new InvalidDataException("请求过大或为空。");
                var request = JsonSerializer.Deserialize<Request>(json, JSON) ?? throw new InvalidDataException("空请求。");
                ValidateRequest(request);
                lock (commandLock)
                {
                    if (!enabled)
                        return "{\"accepted\":false,\"error\":\"disabled\"}";
                    if (commands.Count >= 32)
                        throw new InvalidDataException("请求队列已满。");
                    var ticket = generation;
                    commands.Enqueue(() =>
                    {
                        if (!enabled || ticket != generation)
                            return;
                        Execute(request);
                    });
                }

                return "{\"accepted\":true,\"completed\":false}";
            }
            catch (Exception error)
            {
                return JsonSerializer.Serialize(new
                {
                    accepted = false,
                    error = error.Message
                }, JSON);
            }
        }

        public void QueuePreset(BetterGlamourPreset preset)
        {
            if (!enabled)
                return;
            pendingPreset = ToPlan(preset);
            currentTextureValid = false;
            phoneFrames.Reset();
            due = Environment.TickCount64 + 150;
            Volatile.Write(ref cachedPhoneStatus, Status());
        }

        internal bool TryGetHairAppearance(out CustomizeData customize)
        {
            customize = default;
            if (!enabled || !Safe())
                return false;

            if (ownsTryon && context?.ObjectTable.LocalPlayer?.Address == player && baselineHairAppearance is { } baseline)
                customize = pendingPreset == null ? desired.CustomizeData : baseline;
            else if (getBaseHairAppearance?.Invoke() is { } current)
                customize = current;
            else
                return false;

            if (pendingPreset is { } plan)
            {
                customize.HairColor = plan.HairColor ?? customize.HairColor;
                customize.HighlightsColor = plan.HighlightColor ?? customize.HighlightsColor;
                customize.Highlights = plan.HighlightsEnabled ?? customize.Highlights;
            }

            return true;
        }

        public void CancelPreview()
        {
            pendingPreset = null;
            lock (commandLock)
            {
                generation++;
                commands.Clear();
            }

            Stop();
        }

        public void CameraPixels(float rotation, float x, float y, float wheel)
        {
            if (!shown || View == null)
                return;
            yaw += Math.Clamp(rotation, -100, 100);
            panX += Math.Clamp(x, -100, 100);
            panY += Math.Clamp(y, -100, 100);
            zoom -= wheel * 2.5f;
        }

        public bool NativeButtonChecked(uint nodeID)
        {
            var button = GetNativeButton(nodeID);
            return button != null && button->GetComponentType() == ComponentType.CheckBox && button->IsChecked;
        }

        public void ClickNativeButton(uint nodeID)
        {
            var button = GetNativeButton(nodeID);
            if (button == null || !button->IsEnabled)
                return;
            var owner = &button->OwnerNode->AtkResNode;
            if (!owner->IsEventRegistered(AtkEventType.ButtonClick))
            {
                message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.ButtonUnavailable");
                return;
            }

            if (button->GetComponentType() == ComponentType.CheckBox)
                button->SetChecked(!button->IsChecked);
            if (nodeID == 39)
                yaw = pitch = zoom = panX = panY = 0;
            AtkEventDispatcher.Event nativeEvent = new()
            {
                State = new()
                {
                    EventType = AtkEventType.ButtonClick
                }
            };
            customizationAttempts = 0;
            customizationRetryAt = loadingSeconds + 0.25;
            owner->DispatchEvent(&nativeEvent);
        }

        public void ResetCamera()
        {
            yaw = pitch = zoom = panX = panY = 0;
            var view = View;
            if (view != null && shown)
                view->ResetPositions();
        }

        private void OnFrame(IFramework framework)
        {
            if (!enabled)
                return;
            currentTextureValid = false;
            Update(framework.UpdateDelta);
            if (pendingPreset is { } plan && Environment.TickCount64 >= due)
            {
                pendingPreset = null;
                try
                {
                    Begin(plan);
                }
                catch (Exception error)
                {
                    Stop();
                    phase = "error";
                    message = error.Message;
                }
            }

            Volatile.Write(ref cachedPhoneStatus, Status());
            if (context != null && currentTextureValid)
                phoneFrames.Update(context.TextureReadback, image, frameWidth, frameHeight, requestID, pendingPreset == null && shown && outfitVerified);
            if (currentTextureValid && pendingPreset == null && shown && image != 0 && outfitVerified)
            {
                try
                {
                    frameIpc?.SendMessage(image, frameWidth, frameHeight, requestID);
                }
                catch (Exception)
                {
                }
            }
        }

        private void OnTryonLifecycle(AddonEvent type, AddonArgs args)
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (addon == null || (nint)addon != guardedTryonAddon || hiddenTryonRoot == 0 || addon->RootNode == null || (nint)addon->RootNode != hiddenTryonRoot)
                return;
            if (type == AddonEvent.PreFinalize)
            {
                addon->DisableShowOnOpen = savedDisableShowOnOpen;
                addon->DisableFocusOnShow = savedDisableFocusOnShow;
                addon->DisableFocusability = savedDisableFocusability;
                FlagHelper.UpdateFlag(ref addon->Flags1A3, TRYON_CLICK_THROUGH_FLAG, savedTryonClickThrough);
                addon->SetAlpha(savedTryonAlpha);
                hiddenTryonRoot = guardedTryonAddon = 0;
                closingTryon = false;
                return;
            }

            if (type != AddonEvent.PreDraw || (!ownsTryon && !closingTryon))
                return;
            addon->RootNode->ToggleVisibility(false);
            if (closingTryon)
                addon->SetAlpha(0);
            suppressedDraws++;
        }

        public void Update(TimeSpan elapsed)
        {
            if (!enabled || context == null)
                return;
            try
            {
                int batch = 0;
                while (batch++ < 8 && commands.TryDequeue(out var command))
                    command();
                var tryon = Tryon;
                if (tryon == null)
                {
                    if (ownsTryon) Stop();
                    return;
                }
                var view = &tryon->CharaView.CharaView;
                if (!Safe() || context.ObjectTable.LocalPlayer is not { } localPlayer || localPlayer.Address != player || PortraitUIOpen())
                {
                    Stop();
                    message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.EnvironmentChanged");
                    return;
                }

                if (tryon == null || !ownsTryon || !tryon->IsAgentActive())
                {
                    Stop();
                    message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.NativeClosed");
                    return;
                }

                HideTryonChrome();
                if (view->State != 0 && !IsExpectedTryonMapping(view->ClientObjectIndex, view->ClientObjectId))
                    throw new InvalidOperationException("试衣间槽位已变化，停止预览。");
                var requestProcessed = !tryon->TryOnItemsChanged && !tryon->GearItemsChanged && !tryon->CharaView.DoUpdate;
                modelSubmitted = requestProcessed;
                loadingSeconds += Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
                var character = view->GetCharacter();
                var draw = character == null ? null : (CharacterBase*)character->DrawObject;
                var humanReady = character != null && (nint)character != player && draw != null && draw->GetModelType() == CharacterBase.ModelType.Human;
                if (requestProcessed && humanReady && view->State == 6)
                    ApplyPreviewCustomization(character, (Human*)draw);
                bool extraMatched = humanReady && ItemPreviewService.HairAppearanceMatches(((Human*)draw)->Customize, desired.CustomizeData) && character->DrawData.GlassesIds[0] == desired.GlassesIds[0];
                outfitVerified = requestProcessed && humanReady && extraMatched && Matches(ref desired, (Human*)draw, mask, tryon->CharaView.HeadgearHidden);
                var target = RenderTargetManager.Instance();
                var nativeTexture = tryon->Texture;
                if (nativeTexture == null && target != null)
                    nativeTexture = target->GetCharaViewTexture(renderIndex);
                var hasTexture = nativeTexture != null && nativeTexture->D3D11ShaderResourceView != null && nativeTexture->ActualWidth > 0 && nativeTexture->ActualHeight > 0;
                if (CanPresentNativeFrame(view->State, hasTexture, humanReady))
                {
                    verifiedFrames++;
                    if (verifiedFrames >= 2)
                    {
                        var previous = (KernelTexture*)texture;
                        if (previous != nativeTexture)
                            nativeTexture->IncRef();
                        texture = (nint)nativeTexture;
                        image = (nint)nativeTexture->D3D11ShaderResourceView;
                        frameWidth = (int)nativeTexture->ActualWidth;
                        frameHeight = (int)nativeTexture->ActualHeight;
                        ratio = (float)frameWidth / frameHeight;
                        if (previous != null && previous != nativeTexture)
                            previous->DecRef();
                        currentTextureValid = true;
                        shown = true;
                    }
                }
                else
                    verifiedFrames = 0;
                if (shown)
                {
                    phase = outfitVerified ? "ready" : requestProcessed ? "mismatch" : "loading";
                    if (outfitVerified)
                        message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Ready");
                    else if (!requestProcessed || !humanReady)
                        message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Loading");
                    else if (!extraMatched)
                        message = OmniLoc.Get(customizationAttempts >= 3 ? "Feature.BetterGlamourManagement.Preview.CustomizationMismatch" : "Feature.BetterGlamourManagement.Preview.Loading");
                    else
                        message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.OutfitMismatch");
                }
                else
                {
                    message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Loading");
                    if (loadingSeconds > 12)
                        throw new InvalidOperationException($"试衣间预览超时：状态 {view->State}，缓存处理 {requestProcessed}，纹理 {hasTexture}，人物 {humanReady}。");
                }

                if (shown)
                {
                    if (yaw != 0 || pitch != 0)
                        view->SetCameraYawAndPitch(Math.Clamp(yaw, -100, 100), Math.Clamp(pitch, -100, 100));
                    if (zoom != 0)
                        view->SetCameraDistance(Math.Clamp(zoom, -20, 20));
                    if (panX != 0 || panY != 0)
                        view->SetCameraXAndY(Math.Clamp(panX, -100, 100), Math.Clamp(panY, -100, 100));
                    yaw = pitch = zoom = panX = panY = 0;
                }
            }
            catch (Exception error)
            {
                try
                {
                    Stop();
                }
                catch (Exception)
                {
                }

                phase = "error";
                message = error.Message;
            }
        }

        private void Execute(Request request)
        {
            switch (request.Command)
            {
                case "reload":
                    ReadPlans();
                    break;
                case "preview":
                    pendingPreset = null;
                    if (request.Plan != null)
                        Begin(request.Plan);
                    else
                    {
                        selected = request.PresetIndex;
                        PreviewSelected();
                    }

                    break;
                case "stop":
                    pendingPreset = null;
                    Stop();
                    break;
                case "camera":
                    yaw += RotationPixels(request.Yaw);
                    pitch += RotationPixels(request.Pitch);
                    zoom += request.Zoom * 5;
                    panX += request.PanX * 100;
                    panY += request.PanY * 100;
                    break;
                case "resetCamera":
                    ResetCamera();
                    break;
            }
        }

        private void ReadPlans()
        {
            plans = getPresets().Select(ToPlan).ToArray();
            selected = 0;
            message = string.Format(OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Loaded"), plans.Length);
        }

        private void PreviewSelected()
        {
            if (selected < 0 || selected >= plans.Length)
            {
                message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.SelectPreset");
                return;
            }

            Begin(plans[selected]);
        }

        private void Begin(Plan plan)
        {
            ValidatePlan(plan);
            currentTextureValid = false;
            phoneFrames.Reset();
            var tryon = Tryon;
            var reuse = ownsTryon && tryon != null && tryon->IsAgentActive() && Safe() && context?.ObjectTable.LocalPlayer is { } currentPlayer && currentPlayer.Address == player;
            if (!reuse)
                Stop();
            else
            {
                shown = false;
                image = 0;
                var oldTexture = (KernelTexture*)texture;
                texture = 0;
                if (oldTexture != null)
                    oldTexture->DecRef();
                yaw = pitch = zoom = panX = panY = 0;
                HideTryonChrome();
            }

            if (!Safe())
                throw new InvalidOperationException("请登录并等待切换场景结束。");
            if (PortraitUIOpen())
                throw new InvalidOperationException("请先关闭游戏中的职业套装预览。");
            if (context?.ObjectTable.LocalPlayer is not { } localPlayer)
                throw new InvalidOperationException("预览角色不可用。");
            player = localPlayer.Address;
            desired = default;
            desired.CopyFromCharacter((Character*)player);
            baselineHairAppearance ??= getBaseHairAppearance == null
                ? desired.CustomizeData
                : getBaseHairAppearance() ?? throw new InvalidOperationException(OmniLoc.Get("Feature.BetterGlamourManagement.Preview.HairAppearanceUnavailable"));
            var hairAppearance = baselineHairAppearance.Value;
            desired.CustomizeData.Hairstyle = hairAppearance.Hairstyle;
            desired.CustomizeData.HairColor = plan.HairColor ?? hairAppearance.HairColor;
            desired.CustomizeData.HighlightsColor = plan.HighlightColor ?? hairAppearance.HighlightsColor;
            desired.CustomizeData.Highlights = plan.HighlightsEnabled ?? hairAppearance.Highlights;
            Array.Fill(mask, false);
            foreach (var part in plan.Items)
            {
                var slot = ModelSlot(part.Slot);
                if (part.ItemID == 0)
                    continue;
                var id = Normalize(part.ItemID);
                var row = context.DataManager.GetExcelSheet<Item>().GetRowOrDefault(id);
                if (!row.HasValue)
                    throw new InvalidDataException($"不存在物品 {id}");
                SetPackedModel(ref desired, part, row.Value.ModelMain);
                if (slot >= 0)
                    mask[slot] = true;
            }

            desired.GlassesIds[0] = plan.Glasses;
            if (plan.Hairstyle != 0)
            {
                var hair = context.DataManager.GetExcelSheet<CharaMakeCustomize>().GetRowOrDefault(plan.Hairstyle);
                if (!hair.HasValue || hair.Value.FeatureID > 255)
                    throw new InvalidDataException("发型数据无效。");
                desired.CustomizeData.Hairstyle = (byte)hair.Value.FeatureID;
            }

            tryon = AgentTryon.Instance();
            if (tryon == null)
                throw new InvalidOperationException("原生试衣间不可用。");
            if (!reuse && tryon->IsAgentActive())
                throw new InvalidOperationException("请先关闭游戏中已打开的试衣间，再选择方案预览。");
            if (!reuse)
            {
                var seed = plan.Items.FirstOrDefault(part => part.ItemID != 0);
                if (seed == null)
                {
                    foreach (var slot in InspectSlots)
                    {
                        if (!InventorySlotAccess.TryGet(InventoryType.EquippedItems, (ushort)slot, out var item) || item->ItemId == 0)
                            continue;
                        seed = new Part
                        {
                            Slot = slot,
                            ItemID = item->GlamourId != 0 ? item->GlamourId : item->ItemId,
                            Dye0 = item->Stains[0],
                            Dye1 = item->Stains[1]
                        };
                        break;
                    }
                }

                if (seed == null)
                    throw new InvalidOperationException(OmniLoc.Get("Feature.BetterGlamourManagement.Preview.EquipmentUnavailable"));
                savedTryonItems = tryon->TryOnItems.ToArray();
                previousSaveOutfit = tryon->SaveDeleteOutfit;
                previousDisplayGear = tryon->DisplayGear;
                ownedTryon = (nint)tryon;
                ownsTryon = true;
                if (!AgentTryon.TryOn(0, Normalize(seed.ItemID), seed.Dye0, seed.Dye1))
                    throw new InvalidOperationException("游戏拒绝打开此装备的试衣间。");
                HideTryonChrome();
            }

            ResetNativeTryonItems(tryon->TryOnItems);
            tryon->SaveDeleteOutfit = true;
            tryon->DisplayGear = false;
            foreach (var part in plan.Items.Where(x => x.ItemID != 0).OrderBy(x => x.Slot))
            {
                if (!AgentTryon.TryOn(0, Normalize(part.ItemID), part.Dye0, part.Dye1))
                    throw new InvalidOperationException($"试衣间拒绝装备：部位 {part.Slot} / 物品 {part.ItemID}。");
                HideTryonChrome();
            }

            if (plan.Glasses != 0)
            {
                var glasses = context.DataManager.GetExcelSheet<Glasses>().GetRowOrDefault(plan.Glasses);
                if (!glasses.HasValue)
                    throw new InvalidDataException("面部装饰不存在。");
                AddGlassesCache(tryon->TryOnItems, plan.Glasses, (uint)glasses.Value.Icon);
            }

            customizationAttempts = 0;
            customizationRetryAt = 0;
            tryon->TryOnItemsChanged = true;
            tryon->CharaView.HideOtherEquipment = false;
            tryon->CharaView.DoUpdate = true;
            HideTryonChrome();
            var view = &tryon->CharaView.CharaView;
            renderIndex = PREVIEW_RENDER_SLOT;
            if (view->State != 0 && !IsExpectedTryonMapping(view->ClientObjectIndex, view->ClientObjectId))
                throw new InvalidOperationException($"试衣间映射异常：槽 {view->ClientObjectIndex}/对象 {view->ClientObjectId}。");
            modelSubmitted = false;
            outfitVerified = false;
            verifiedFrames = 0;
            currentPlan = plan.Name;
            phase = "loading";
            loadingSeconds = 0;
            requestID++;
            message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Loading");
        }

        private void Stop()
        {
            phoneFrames.Reset();
            currentTextureValid = false;
            image = 0;
            shown = false;
            yaw = pitch = zoom = panX = panY = 0;
            var oldAgent = Tryon;
            var closeOwned = ownsTryon;
            ownsTryon = false;
            ownedTryon = 0;
            if (closeOwned && oldAgent != null)
            {
                closingTryon = hiddenTryonRoot != 0;
                var beforeClose = GetTryonAddon();
                if (beforeClose != null && (nint)beforeClose == guardedTryonAddon)
                {
                    beforeClose->SetAlpha(0);
                    if (beforeClose->RootNode != null)
                        beforeClose->RootNode->ToggleVisibility(false);
                }

                if (oldAgent->IsAgentActive())
                    oldAgent->Hide();
                CloseOwnedTryonAddon();
                if (savedTryonItems != null)
                    savedTryonItems.AsSpan().CopyTo(oldAgent->TryOnItems);
                oldAgent->SaveDeleteOutfit = previousSaveOutfit;
                oldAgent->DisplayGear = previousDisplayGear;
            }

            savedTryonItems = null;
            modelSubmitted = false;
            outfitVerified = false;
            verifiedFrames = 0;
            var oldTexture = (KernelTexture*)texture;
            texture = 0;
            if (oldTexture != null)
                oldTexture->DecRef();
            player = 0;
            baselineHairAppearance = null;
            renderIndex = 0;
            frameWidth = frameHeight = 0;
            phase = "idle";
            currentPlan = string.Empty;
            message = OmniLoc.Get("Feature.BetterGlamourManagement.Preview.Hint");
            Volatile.Write(ref cachedPhoneStatus, Status());
        }

        private void StopAndUnregister()
        {
            try
            {
                Stop();
            }
            finally
            {
                context?.AddonLifecycle.UnregisterListener(OnTryonLifecycle);
            }
        }

        private void ApplyPreviewCustomization(Character* character, Human* human)
        {
            var view = View;
            if (view == null || !CanCustomizePreview(ownsTryon, view->ClientObjectIndex, view->ClientObjectId, (nint)character, player))
                return;
            var hairChanged = !ItemPreviewService.HairAppearanceMatches(human->Customize, desired.CustomizeData);
            var glassesChanged = character->DrawData.GlassesIds[0] != desired.GlassesIds[0];
            if ((!hairChanged && !glassesChanged) || customizationAttempts >= 3 || loadingSeconds < customizationRetryAt)
                return;
            customizationAttempts++;
            customizationRetryAt = loadingSeconds + 0.75;
            if (glassesChanged)
                character->DrawData.SetGlasses(0, desired.GlassesIds[0]);
            if (hairChanged)
            {
                var customize = character->DrawData.CustomizeData;
                customize.Hairstyle = desired.CustomizeData.Hairstyle;
                customize.HairColor = desired.CustomizeData.HairColor;
                customize.HighlightsColor = desired.CustomizeData.HighlightsColor;
                customize.Highlights = desired.CustomizeData.Highlights;
                var data = new Human.DrawData
                {
                    CustomizeData = customize
                };
                if (human->UpdateDrawData(&data, true))
                    character->DrawData.CustomizeData = customize;
            }
        }

        private void HideTryonChrome()
        {
            var tryon = Tryon;
            if (tryon == null || !tryon->IsAgentActive())
                return;
            var addon = GetTryonAddon();
            if (addon == null || addon->RootNode == null)
                return;
            var root = addon->RootNode;
            if (hiddenTryonRoot != (nint)root)
            {
                hiddenTryonRoot = (nint)root;
                guardedTryonAddon = (nint)addon;
                savedTryonAlpha = addon->Alpha;
                closingTryon = false;
                savedDisableShowOnOpen = addon->DisableShowOnOpen;
                savedDisableFocusOnShow = addon->DisableFocusOnShow;
                savedDisableFocusability = addon->DisableFocusability;
                savedTryonClickThrough = FlagHelper.ReadFlag(ref addon->Flags1A3, TRYON_CLICK_THROUGH_FLAG);
            }

            addon->DisableShowOnOpen = true;
            addon->DisableFocusOnShow = true;
            addon->DisableFocusability = true;
            FlagHelper.SetFlag(ref addon->Flags1A3, TRYON_CLICK_THROUGH_FLAG);
            if (root->IsVisible())
            {
                root->ToggleVisibility(false);
                addon->UpdateCollisionNodeList(true);
            }
        }

        private void CloseOwnedTryonAddon()
        {
            if (hiddenTryonRoot == 0)
                return;
            var addon = GetTryonAddon();
            if (addon != null && addon->RootNode != null && (nint)addon->RootNode == hiddenTryonRoot)
            {
                addon->RootNode->ToggleVisibility(false);
                addon->UpdateCollisionNodeList(true);
                addon->DisableShowOnOpen = savedDisableShowOnOpen;
                addon->DisableFocusOnShow = savedDisableFocusOnShow;
                addon->Hide(true, false, 0);
                addon->DisableFocusability = savedDisableFocusability;
                FlagHelper.UpdateFlag(ref addon->Flags1A3, TRYON_CLICK_THROUGH_FLAG, savedTryonClickThrough);
                addon->Close(false);
            }
        }

        private AtkComponentButton* GetNativeButton(uint nodeID)
        {
            var tryon = Tryon;
            if (tryon == null || !tryon->IsAgentActive())
                return null;
            var addon = GetTryonAddon();
            if (addon == null || addon->Id != tryon->GetAddonId())
                return null;
            var button = addon->GetComponentButtonById(nodeID);
            return button != null && button->OwnerNode != null && button->OwnerNode->AtkResNode.NodeId == nodeID ? button : null;
        }

        private static AtkUnitBase* GetTryonAddon()
        {
            AtkUnitBase* addon;
            if (!AddonHelper.TryGetByName("Tryon", out addon) && !AddonHelper.TryGetByName("FittingRoom", out addon))
                return null;
            return addon;
        }

        private bool Safe() => context != null && context.ClientState.IsLoggedIn && context.ObjectTable.LocalPlayer != null && !context.Condition[ConditionFlag.BetweenAreas] && !context.Condition[ConditionFlag.BetweenAreas51] && !context.Condition[ConditionFlag.OccupiedInCutSceneEvent];
        private bool PortraitUIOpen() => context != null && context.GameGUI.GetAddonByName("GearSetPreview").IsVisible;
        private static Plan ToPlan(BetterGlamourPreset preset) => new()
        {
            Name = preset.Name,
            Glasses = preset.GlassesID,
            Hairstyle = preset.HairstyleID,
            HairColor = preset.HairColor,
            HighlightColor = preset.HighlightColor,
            HighlightsEnabled = preset.HighlightsEnabled,
            Items = preset.Items.Select(item => new Part
            {
                Slot = item.Slot,
                ItemID = item.ItemID,
                Dye0 = item.Stain0,
                Dye1 = item.Stain1
            }).ToArray()
        };
        private static uint Normalize(uint id) => ItemUtil.GetBaseId(id).ItemId;

        internal static void ValidateRequest(Request request)
        {
            if (request.Command is not ("reload" or "preview" or "stop" or "camera" or "resetCamera"))
                throw new InvalidDataException("未知命令。");
            ReadOnlySpan<float> deltas = [request.Yaw, request.Pitch, request.Zoom, request.PanX, request.PanY];
            foreach (var delta in deltas)
                if (!float.IsFinite(delta) || Math.Abs(delta) > 2)
                    throw new InvalidDataException("相机增量超出范围。");
            if (request.PresetIndex < 0)
                throw new InvalidDataException("方案索引无效。");
            if (request.Plan != null)
                ValidatePlan(request.Plan);
        }

        internal static void ValidatePlan(Plan plan)
        {
            if (plan.Name == null || plan.Name.Length > 256 || plan.Items == null || plan.Items.Length > 12)
                throw new InvalidDataException("方案大小不合法。");
            var slots = new HashSet<int>();
            foreach (var part in plan.Items)
                if (part == null || !ValidSlot(part.Slot) || !slots.Add(part.Slot) || part.ItemID >= 2_000_000)
                    throw new InvalidDataException("方案包含无效或重复部位/物品 ID。");
            if (!plan.Items.Any(part => part.ItemID != 0) && plan.Glasses == 0 && plan.Hairstyle == 0 &&
                !plan.HairColor.HasValue && !plan.HighlightColor.HasValue && !plan.HighlightsEnabled.HasValue)
                throw new InvalidDataException("方案没有外观数据。");
        }

        internal static float RotationPixels(float radians) => radians * 100f;
        internal static bool Matches(ref CharaViewModelData expected, Human* actual, bool[] slots, bool hiddenHead)
        {
            for (var i = 0; i < 10; i++)
            {
                if (!slots[i] || (i == 0 && hiddenHead))
                    continue;
                var expectedModel = expected.EquipmentModelIds[i];
                var actualModel = actual->EquipmentModels[i];
                if (expectedModel.Id != actualModel.Id || expectedModel.Variant != actualModel.Variant || expectedModel.Stain0 != actualModel.Stain0 || expectedModel.Stain1 != actualModel.Stain1)
                    return false;
            }

            return true;
        }

        internal static void SetPackedModel(ref CharaViewModelData model, Part part, ulong packed)
        {
            if (part.Slot is 0 or 1)
            {
                model.WeaponModelIds[part.Slot] = *(WeaponModelId*)&packed;
                model.WeaponModelIds[part.Slot].Stain0 = part.Dye0;
                model.WeaponModelIds[part.Slot].Stain1 = part.Dye1;
            }

            var slot = ModelSlot(part.Slot);
            if (slot >= 0)
                model.EquipmentModelIds[slot] = new EquipmentModelId
                {
                    Id = (ushort)packed,
                    Variant = (byte)(packed >> 16),
                    Stain0 = part.Dye0,
                    Stain1 = part.Dye1
                };
        }

        internal static void ResetNativeTryonItems(Span<AgentTryon.TryOnItem> items)
        {
            items.Clear();
            for (var i = 0; i < items.Length; i++)
                items[i].EquipSlotCategory = 14;
        }

        internal static void AddGlassesCache(Span<AgentTryon.TryOnItem> items, ushort id, uint icon)
        {
            if (id == 0)
                return;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].Id != 0)
                    continue;
                items[i] = new AgentTryon.TryOnItem
                {
                    EquipSlotCategory = 13,
                    Id = id,
                    IconId = icon
                };
                return;
            }

            throw new InvalidOperationException("试衣间面部装饰缓存已满。");
        }

        internal static bool CanCustomizePreview(bool owned, uint slot, uint id, nint actor, nint localPlayer) => owned && IsExpectedTryonMapping(slot, id) && actor != 0 && localPlayer != 0 && actor != localPlayer;
        internal static bool CanPresentNativeFrame(uint state, bool hasTexture, bool nativeActorReady) => state == 6 && hasTexture && nativeActorReady;
        internal static bool IsExpectedTryonMapping(uint slot, uint id) => slot == PREVIEW_RENDER_SLOT && id == PREVIEW_OBJECT_ID;
        internal static bool ValidSlot(int slot) => slot is 0 or 1 or 2 or 3 or 4 or 6 or 7 or 8 or 9 or 10 or 11 or 12;
        internal static int ModelSlot(int slot) => slot switch
        {
            2 => 0,
            3 => 1,
            4 => 2,
            >= 6 and <= 12 => slot - 3,
            _ => -1
        };

        public sealed class Part
        {
            public int Slot;
            [JsonPropertyName("itemId")]
            public uint ItemID;

            public byte Dye0;
            public byte Dye1;
        }

        public sealed class Plan
        {
            public string Name = string.Empty;
            public ushort Glasses;
            public uint Hairstyle;
            public byte? HairColor;
            public byte? HighlightColor;
            public bool? HighlightsEnabled;
            public Part[] Items = [];
        }

        public sealed class Request
        {
            public string Command = string.Empty;
            public int PresetIndex;
            public Plan? Plan;
            public float Yaw;
            public float Pitch;
            public float Zoom;
            public float PanX;
            public float PanY;
        }

        #region 常量

        internal const uint PREVIEW_RENDER_SLOT = 2;
        internal const ushort PREVIEW_OBJECT_ID = 242;
        private const int TRYON_CLICK_THROUGH_FLAG = 0x40;

        #endregion
    }
}
