using System.IO;
using System.Linq;
using System.Text.Json;

namespace OmniToolbox.TreePublic;

public sealed partial class BetterGlamourManagement
{
    internal static class GlamourClipboard
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            MaxDepth = 16
        };

        public static string Export(BetterGlamourPreset preset)
        {
            var copy = new BetterGlamourPreset
            {
                Name = string.IsNullOrWhiteSpace(preset.Name) ? "导入幻化" : preset.Name,
                GlassesID = preset.GlassesID,
                HairstyleID = preset.HairstyleID,
                HairColor = preset.HairColor,
                HighlightColor = preset.HighlightColor,
                HighlightsEnabled = preset.HighlightsEnabled,
                Items = preset.Items.Select(x => new BetterGlamourItem
                {
                    Slot = x.Slot,
                    ItemID = x.ItemID,
                    Stain0 = x.Stain0,
                    Stain1 = x.Stain1
                }).ToList()
            };
            return JsonSerializer.Serialize(new
            {
                Schema = "OmniGlamourPreset",
                Version = 1,
                Preset = copy
            }, Options);
        }

        public static BetterGlamourPreset Import(string text, BetterGlamourPreset? existing = null)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 65536)
                throw new InvalidDataException("剪贴板为空或超过 64KB。");
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                MaxDepth = 16
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("需要幻化条目的 JSON 数据。");
            if (root.TryGetProperty("Schema", out var schema))
            {
                if (schema.GetString() != "OmniGlamourPreset" || root.GetProperty("Version").GetInt32() != 1)
                    throw new InvalidDataException("不支持的幻化分享版本。");
                root = root.GetProperty("Preset");
            }

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Name", out _) || !root.TryGetProperty("Items", out _))
                throw new InvalidDataException("缺少条目名称或装备数据；网页链接、普通分享文字不能直接导入。");
            var preset = root.Deserialize<BetterGlamourPreset>(Options) ?? throw new InvalidDataException("条目为空。");
            if (existing != null)
            {
                if (!root.TryGetProperty(nameof(BetterGlamourPreset.HairColor), out _))
                    preset.HairColor = existing.HairColor;
                if (!root.TryGetProperty(nameof(BetterGlamourPreset.HighlightColor), out _))
                    preset.HighlightColor = existing.HighlightColor;
                if (!root.TryGetProperty(nameof(BetterGlamourPreset.HighlightsEnabled), out _))
                    preset.HighlightsEnabled = existing.HighlightsEnabled;
            }
            return PrepareImportedPreset(preset);
        }

        internal static BetterGlamourPreset PrepareImportedPreset(BetterGlamourPreset preset)
        {
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 256 || preset.Items == null || preset.Items.Count > 12)
                throw new InvalidDataException("名称或装备数量无效。");
            var slots = new HashSet<int>();
            foreach (var item in preset.Items)
            {
                if (item == null || !BetterGlamourPortraitPreview.ValidSlot(item.Slot) || !slots.Add(item.Slot) || item.ItemID >= 2000000)
                    throw new InvalidDataException("包含重复/未知装备部位或无效物品 ID。");
            }

            if (!preset.Items.Any(x => x.ItemID != 0) && preset.GlassesID == 0 && preset.HairstyleID == 0 &&
                !preset.HairColor.HasValue && !preset.HighlightColor.HasValue && !preset.HighlightsEnabled.HasValue)
                throw new InvalidDataException("条目没有任何外观数据。");
            preset.Name = preset.Name.Trim();
            preset.ID = Guid.NewGuid().ToString("N");
            preset.GearsetIndex = -1;
            preset.GearsetBindings = [];
            return preset;
        }
    }
}
