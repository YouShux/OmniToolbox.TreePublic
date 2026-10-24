using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using OmniToolbox.Host;
using OmniToolbox.Items;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterGlamourManagement
{
    private ICallGateProvider<string, string>? remoteGate;
    private ICallGateProvider<string, Task<string>>? asyncManagementGate;
    private ICallGateProvider<string, Task<string>>? asyncFrameGate;
    private ICallGateProvider<string, string>? frameCacheGate;
    private long remoteUntil;
    private volatile bool remoteEnabled;
    private string phoneSelectedID = string.Empty;
    private bool remoteActive;
    private string? nativeButtonAtlas;

    private void EnableRemote()
    {
        remoteGate = DalamudServices.PluginInterface.GetIpcProvider<string, string>("Omni.Glamour.Management.V1");
        remoteGate.RegisterFunc(RemoteRequest);
        asyncManagementGate = DalamudServices.PluginInterface.GetIpcProvider<string, Task<string>>("Omni.Glamour.Management.V2");
        asyncManagementGate.RegisterFunc(RemoteRequestAsync);
        asyncFrameGate = DalamudServices.PluginInterface.GetIpcProvider<string, Task<string>>("Omni.Glamour.FrameCache.V2");
        asyncFrameGate.RegisterFunc(request => Task.FromResult(Envelope(ReadFrameCache(request))));
        frameCacheGate = DalamudServices.PluginInterface.GetIpcProvider<string, string>("Omni.Glamour.FrameCache.V1");
        frameCacheGate.RegisterFunc(ReadFrameCache);
        remoteEnabled = true;
    }

    private void DisableRemote()
    {
        remoteEnabled = false;
        asyncManagementGate?.UnregisterFunc();
        asyncManagementGate = null;
        asyncFrameGate?.UnregisterFunc();
        asyncFrameGate = null;
        frameCacheGate?.UnregisterFunc();
        frameCacheGate = null;
        remoteGate?.UnregisterFunc();
        remoteGate = null;
        remoteActive = false;
        Volatile.Write(ref phoneSelectedID, string.Empty);
    }

    private Task<string> RemoteRequestAsync(string json)
    {
        if (!remoteEnabled)
            return Task.FromResult(Envelope("{\"ok\":false,\"error\":\"幻化管理模块未启用\"}"));

        var framework = IFramework.Instance();
        return framework.IsInFrameworkUpdateThread
            ? Task.FromResult(Envelope(RemoteRequest(json)))
            : framework.RunOnFrameworkThread(() => Envelope(RemoteRequest(json)));
    }

    private string Envelope(string reply)
    {
        using var document = JsonDocument.Parse(reply);
        var data = document.RootElement;
        var ok = !data.TryGetProperty("ok", out var flag) || flag.ValueKind != JsonValueKind.False;
        string? error = null;
        if (!ok)
            error = data.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : "请求失败";

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            available = remoteEnabled,
            error,
            data
        });
    }

    private string ReadFrameCache(string request)
    {
        if (!remoteEnabled)
            return "{\"ok\":false,\"error\":\"幻化管理模块未启用\"}";
        if (string.IsNullOrEmpty(request) || !request.StartsWith("frame:", StringComparison.Ordinal) ||
            request.Length == 6 || request[6..] != Volatile.Read(ref phoneSelectedID))
            return "{\"ok\":true,\"changed\":true}";

        Interlocked.Exchange(ref remoteUntil, Environment.TickCount64 + 5000);
        return portraitPreview?.CachedPhoneFrame() ?? "{\"ok\":false,\"error\":\"预览未就绪\"}";
    }

    private void RemoteTick()
    {
        Volatile.Write(ref phoneSelectedID, selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count
            ? config.Presets[selectedPresetIndex].ID
            : string.Empty);
        if (!remoteActive || Environment.TickCount64 <= Interlocked.Read(ref remoteUntil))
            return;

        remoteActive = false;
        if (managerNativeUI?.IsOpen != true)
            portraitPreview?.CancelPreview();
    }

    internal static string Revision(IEnumerable<BetterGlamourPreset> entries) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries)));

    private string RemoteRequest(string json)
    {
        try
        {
            if (!remoteEnabled)
                throw new InvalidOperationException("幻化管理模块未启用。");
            if (!IFramework.Instance().IsInFrameworkUpdateThread)
                throw new InvalidOperationException("必须通过游戏线程访问幻化管理。");
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
                throw new InvalidDataException("请求为空或过大。");

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 20
            });
            var request = document.RootElement;
            var action = request.GetProperty("action").GetString() ?? string.Empty;
            var id = request.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
            if (action is "save" or "delete" or "apply" or "preview-draft")
            {
                if (!request.TryGetProperty("revision", out var revision) || revision.GetString() != Revision(config.Presets))
                    throw new InvalidOperationException("方案已在另一端更改，请刷新后重试。");
            }

            if ((action is "camera" or "button") && !IsSelectedRemotePreset(id))
                throw new InvalidOperationException("另一端已切换预览，请重新选择方案。");

            switch (action)
            {
                case "frame":
                    Interlocked.Exchange(ref remoteUntil, Environment.TickCount64 + 5000);
                    if (id.Length > 0 && !IsSelectedRemotePreset(id))
                    {
                        return JsonSerializer.Serialize(new
                        {
                            ok = true,
                            image = string.Empty,
                            changed = true
                        });
                    }

                    return portraitPreview?.PhoneFrame() ?? "{\"ok\":false,\"error\":\"预览未就绪\"}";
                case "search":
                {
                    var kind = request.GetProperty("kind").GetString() ?? "equipment";
                    var query = request.GetProperty("query").GetString() ?? string.Empty;
                    if (query.Length > 100)
                        throw new InvalidDataException("搜索过长。");

                    var rows = kind switch
                    {
                        "hair" => SearchHairstyles(query),
                        "glasses" => SearchGlasses(query),
                        _ => SearchItems(query).Where(x => x.ItemID == 0 ||
                            LuminaGetter.TryGetRow<Item>(x.ItemID, out var item) && FitsSlot(item, request.GetProperty("slot").GetInt32())).ToList()
                    };
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        rows = rows.Take(60).Select(x => new
                        {
                            id = x.ItemID,
                            name = x.Name,
                            iconId = x.IconID
                        })
                    });
                }
                case "export":
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        text = GlamourClipboard.Export(GetRemotePreset(id))
                    });
                case "import":
                case "import-website":
                {
                    if (config.Presets.Count >= MAX_PRESET_COUNT)
                        throw new InvalidOperationException("幻化条目已满。");

                    var incoming = action == "import"
                        ? GlamourClipboard.Import(request.GetProperty("text").GetString() ?? string.Empty)
                        : WebsitePreset(request.GetProperty("data"));
                    ValidateRemotePreset(incoming);
                    config.Presets.Add(incoming);
                    try
                    {
                        saveConfig();
                    }
                    catch
                    {
                        config.Presets.Remove(incoming);
                        throw;
                    }

                    Volatile.Write(ref phoneSelectedID, string.Empty);
                    selectedPresetIndex = config.Presets.Count - 1;
                    managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
                    SelectPreset(incoming);
                    break;
                }
                case "select":
                {
                    var preset = GetRemotePreset(id);
                    Volatile.Write(ref phoneSelectedID, string.Empty);
                    SelectPreset(preset);
                    managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
                    break;
                }
                case "preview-draft":
                {
                    var preset = GetRemotePreset(id);
                    if (!IsSelectedRemotePreset(id))
                        throw new InvalidOperationException("另一端已切换预览，请重新选择方案。");

                    var incoming = GlamourClipboard.Import(request.GetProperty("preset").GetRawText(), preset);
                    ValidateRemotePreset(incoming);
                    Volatile.Write(ref phoneSelectedID, string.Empty);
                    QueuePortraitPreview(incoming);
                    break;
                }
                case "save":
                {
                    var preset = GetRemotePreset(id);
                    var incoming = GlamourClipboard.Import(request.GetProperty("preset").GetRawText(), preset);
                    ValidateRemotePreset(incoming);
                    var gearsetIndex = request.TryGetProperty("gearsetIndex", out var gearsetValue) ? gearsetValue.GetInt32() : preset.GearsetIndex;
                    if (gearsetIndex < -1 || gearsetIndex > 99 || gearsetIndex >= 0 && !TryGetGearsetName(gearsetIndex, out _))
                        throw new InvalidDataException("职业套装无效。");

                    var oldName = preset.Name;
                    var oldHair = preset.HairstyleID;
                    var oldHairColor = preset.HairColor;
                    var oldHighlightColor = preset.HighlightColor;
                    var oldHighlightsEnabled = preset.HighlightsEnabled;
                    var oldGlasses = preset.GlassesID;
                    var oldItems = preset.Items;
                    preset.Name = incoming.Name;
                    preset.HairstyleID = incoming.HairstyleID;
                    preset.HairColor = incoming.HairColor;
                    preset.HighlightColor = incoming.HighlightColor;
                    preset.HighlightsEnabled = incoming.HighlightsEnabled;
                    preset.GlassesID = incoming.GlassesID;
                    preset.Items = incoming.Items;
                    try
                    {
                        saveConfig();
                    }
                    catch
                    {
                        preset.Name = oldName;
                        preset.HairstyleID = oldHair;
                        preset.HairColor = oldHairColor;
                        preset.HighlightColor = oldHighlightColor;
                        preset.HighlightsEnabled = oldHighlightsEnabled;
                        preset.GlassesID = oldGlasses;
                        preset.Items = oldItems;
                        throw;
                    }

                    Volatile.Write(ref phoneSelectedID, string.Empty);
                    SelectPreset(preset);
                    managerNativeUI?.UpdateData(config.Presets, selectedPresetIndex);
                    if (gearsetIndex != preset.GearsetIndex)
                        ChangePresetGearset(preset, gearsetIndex);
                    break;
                }
                case "delete":
                    Volatile.Write(ref phoneSelectedID, string.Empty);
                    DeletePreset(GetRemotePreset(id));
                    break;
                case "apply":
                    StartTryOn(GetRemotePreset(id));
                    break;
                case "restore":
                    ClearTryOn();
                    break;
                case "camera":
                    portraitPreview?.CameraPixels(
                        ReadCameraDelta(request, "yaw"),
                        ReadCameraDelta(request, "x"),
                        ReadCameraDelta(request, "y"),
                        Math.Clamp(ReadCameraDelta(request, "wheel"), -3, 3));
                    Interlocked.Exchange(ref remoteUntil, Environment.TickCount64 + 5000);
                    return "{\"ok\":true}";
                case "button":
                {
                    var node = request.GetProperty("node").GetUInt32();
                    if (!NativePreviewButtonIDs.Contains(node))
                        throw new InvalidDataException("未知预览按钮。");
                    portraitPreview?.ClickNativeButton(node);
                    return "{\"ok\":true}";
                }
                case "stop":
                    remoteActive = false;
                    if (managerNativeUI?.IsOpen != true)
                        portraitPreview?.CancelPreview();
                    return "{\"ok\":true}";
                case "read":
                    break;
                default:
                    throw new InvalidDataException("未知幻化操作。");
            }

            if (action is "import" or "import-website" or "select" or "preview-draft" or "save")
            {
                remoteActive = true;
                Interlocked.Exchange(ref remoteUntil, Environment.TickCount64 + 5000);
                Volatile.Write(ref phoneSelectedID, selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count
                    ? config.Presets[selectedPresetIndex].ID
                    : string.Empty);
            }

            if (action == "preview-draft")
                return "{\"ok\":true}";
            return RemoteSnapshot(action == "read" && (!request.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.False));
        }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = exception.Message
            });
        }
    }

    private BetterGlamourPreset GetRemotePreset(string id) =>
        config.Presets.Find(preset => preset.ID == id) ?? throw new InvalidOperationException("方案已删除，请刷新列表。");

    private bool IsSelectedRemotePreset(string id) =>
        id.Length > 0 && selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count && config.Presets[selectedPresetIndex].ID == id;

    private static float ReadCameraDelta(JsonElement request, string key)
    {
        var value = request.TryGetProperty(key, out var property) ? property.GetSingle() : 0;
        if (!float.IsFinite(value) || Math.Abs(value) > 100)
            throw new InvalidDataException("相机增量无效。");
        return value;
    }

    private string RemoteSnapshot(bool includeAssets)
    {
        var gearsets = new List<object>();
        for (var i = 0; i < 100; i++)
        {
            if (TryGetGearsetName(i, out var name))
                gearsets.Add(new
                {
                    id = i,
                    name
                });
        }

        var preview = portraitPreview ?? throw new InvalidOperationException("预览未就绪");
        var selectedID = selectedPresetIndex >= 0 && selectedPresetIndex < config.Presets.Count
            ? config.Presets[selectedPresetIndex].ID
            : string.Empty;
        Volatile.Write(ref phoneSelectedID, selectedID);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            revision = Revision(config.Presets),
            selectedId = selectedID,
            presets = config.Presets.Select(preset => new
            {
                id = preset.ID,
                name = preset.Name,
                gearsetIndex = preset.GearsetIndex,
                glassesId = preset.GlassesID,
                glassesName = GetGlassesName(preset.GlassesID),
                glassesIcon = GetGlassesIcon(preset.GlassesID),
                hairstyleId = preset.HairstyleID,
                hairstyleName = GetHairstyleName(preset.HairstyleID),
                hairstyleIcon = GetHairstyleIcon(preset.HairstyleID),
                hairColor = preset.HairColor,
                highlightColor = preset.HighlightColor,
                highlightsEnabled = preset.HighlightsEnabled,
                items = InspectSlots.Select(slot =>
                {
                    var item = preset.Items.Find(item => item.Slot == slot);
                    var itemID = item?.ItemID ?? 0;
                    LuminaGetter.TryGetRow<Item>(itemID, out var row);
                    return new
                    {
                        slot,
                        itemId = itemID,
                        stain0 = item?.Stain0 ?? 0,
                        stain1 = item?.Stain1 ?? 0,
                        name = itemID == 0 ? "无" : row.Name.ToString(),
                        iconId = itemID == 0 ? 0 : row.Icon
                    };
                })
            }),
            dyes = DyeOptions.Select(dye => new
            {
                id = dye.ID,
                name = dye.Name,
                color = $"#{(int)(dye.Color.X * 255):x2}{(int)(dye.Color.Y * 255):x2}{(int)(dye.Color.Z * 255):x2}"
            }),
            gearsets,
            buttonAtlas = includeAssets ? NativeButtonAtlas() : string.Empty,
            preview = JsonSerializer.Deserialize<JsonElement>(preview.Status()),
            buttons = NativePreviewButtonIDs.Select(node => new
            {
                node,
                active = preview.NativeButtonChecked(node)
            })
        });
    }

    private string NativeButtonAtlas()
    {
        if (nativeButtonAtlas != null)
            return nativeButtonAtlas;
        try
        {
            var texture = DalamudServices.DataManager.GetFile<TexFile>("ui/uld/CircleButtons.tex");
            if (texture == null)
                return string.Empty;
            var pixels = texture.ImageData;
            fixed (byte* data = pixels)
            {
                using var bitmap = new Bitmap(texture.Header.Width, texture.Header.Height, texture.Header.Width * 4, PixelFormat.Format32bppArgb, (nint)data);
                using var output = new MemoryStream();
                bitmap.Save(output, ImageFormat.Png);
                return nativeButtonAtlas = $"data:image/png;base64,{Convert.ToBase64String(output.ToArray())}";
            }
        }
        catch (Exception)
        {
            return nativeButtonAtlas = string.Empty;
        }
    }

    private static void ValidateRemotePreset(BetterGlamourPreset preset)
    {
        foreach (var item in preset.Items)
        {
            if (item.ItemID != 0 && (!LuminaGetter.TryGetRow<Item>(ItemUtil.GetBaseId(item.ItemID).ItemId, out var row) || !FitsSlot(row, item.Slot)))
                throw new InvalidDataException($"装备与部位不匹配：{item.ItemID}");
            if (item.Stain0 != 0 && !LuminaGetter.TryGetRow<Stain>(item.Stain0, out _) ||
                item.Stain1 != 0 && !LuminaGetter.TryGetRow<Stain>(item.Stain1, out _))
                throw new InvalidDataException("染色不存在。");
        }

        if (preset.GlassesID != 0 && !LuminaGetter.TryGetRow<Glasses>(preset.GlassesID, out _))
            throw new InvalidDataException("面部配饰不存在。");
        if (preset.HairstyleID != 0 &&
            (!LuminaGetter.TryGetRow<CharaMakeCustomize>(preset.HairstyleID, out var hairstyle) ||
             hairstyle.FeatureID > byte.MaxValue ||
             !LuminaGetter.Get<HairMakeType>().Any(type => type.CharaMakeStruct[0].SubMenuParam.Contains(preset.HairstyleID))))
            throw new InvalidDataException("发型不存在。");
    }

    internal static bool FitsSlot(Item item, int slot)
    {
        if (item.EquipSlotCategory.RowId == 0)
            return false;
        var category = item.EquipSlotCategory.Value;
        return slot switch
        {
            0 => category.MainHand > 0,
            1 => category.OffHand > 0,
            2 => category.Head > 0,
            3 => category.Body > 0,
            4 => category.Gloves > 0,
            6 => category.Legs > 0,
            7 => category.Feet > 0,
            8 => category.Ears > 0,
            9 => category.Neck > 0,
            10 => category.Wrists > 0,
            11 => category.FingerR > 0,
            12 => category.FingerL > 0,
            _ => false
        };
    }

    private static BetterGlamourPreset WebsitePreset(JsonElement data)
    {
        var result = new BetterGlamourPreset
        {
            Name = data.GetProperty("name").GetString() ?? string.Empty
        };
        var parts = data.GetProperty("parts");
        if (parts.GetArrayLength() > 16)
            throw new InvalidDataException("网页部位过多。");

        var rings = 0;
        foreach (var part in parts.EnumerateArray())
        {
            var label = part.GetProperty("part").GetString() ?? string.Empty;
            var name = part.GetProperty("name").GetString() ?? string.Empty;
            if (name.Length > 150)
                throw new InvalidDataException("网页物品名过长。");
            if (label == "时尚配饰")
                continue;
            if (label == "面部配饰")
            {
                if (name.Length > 0)
                {
                    var matches = LuminaGetter.Get<Glasses>().Where(row => row.Name.ToString() == name).Take(2).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidDataException($"面饰名称不唯一或不存在：{name}");
                    result.GlassesID = (ushort)matches[0].RowId;
                }

                continue;
            }

            var slot = label switch
            {
                "主手" => 0,
                "副手" => 1,
                "头部" => 2,
                "上衣" or "身体" => 3,
                "手部" => 4,
                "腿部" => 6,
                "脚部" => 7,
                "耳坠" or "耳饰" => 8,
                "项链" => 9,
                "手镯" => 10,
                "戒指" => rings++ == 0 ? 12 : 11,
                _ => throw new InvalidDataException($"未知部位：{label}")
            };
            var item = new BetterGlamourItem
            {
                Slot = slot
            };
            if (name.Length > 0)
            {
                var matches = LuminaGetter.Get<Item>().Where(row => row.Name.ToString() == name && FitsSlot(row, slot)).Take(2).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"装备名称不唯一或不存在：{name}");
                item.ItemID = matches[0].RowId;
            }

            var dyes = part.GetProperty("dyes");
            if (dyes.GetArrayLength() > 2)
                throw new InvalidDataException("染色数量无效。");
            item.Stain0 = ReadWebsiteDye(dyes, 0);
            item.Stain1 = ReadWebsiteDye(dyes, 1);
            result.Items.Add(item);
        }

        if (result.Items.Count != 12 || rings != 2)
            throw new InvalidDataException("请等待幻化详情装备列表加载完整。");
        if (string.IsNullOrWhiteSpace(result.Name))
            result.Name = "导入幻化";
        return GlamourClipboard.PrepareImportedPreset(result);
    }

    private static byte ReadWebsiteDye(JsonElement dyes, int index)
    {
        if (index >= dyes.GetArrayLength())
            return 0;
        var text = (dyes[index].GetString() ?? string.Empty).Replace("染剂", string.Empty).Trim();
        if (text.Length == 0 || text is "无染色" or "未染色" or "不可染色")
            return 0;
        var matches = DyeOptions.Where(dye => dye.Name == text).Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"染色无法识别：{text}");
        return matches[0].ID;
    }

    private static readonly uint[] NativePreviewButtonIDs = [32, 33, 34, 35, 37, 39];
}
