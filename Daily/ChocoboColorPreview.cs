using System.Numerics;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.Interop.SpanExtensions;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;
using OmniToolbox.UI;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;

namespace OmniToolbox.TreePublic;

public sealed unsafe class ChocoboColorPreview(
    ChocoboColorPreviewConfig config,
    System.Action saveConfig) : ModuleBase
{
    private const string BuddyAddonName = "Buddy";
    private const int ColorStep = 5;
    private static readonly Vector3 DefaultChocoboColor = new(219f, 180f, 87f);
    private static readonly ChocoboFruit[] Fruits =
    [
        new("Feature.ChocoboColorPreview.Fruit.XelphatolApple", new(1, -1, -1), ["塞尔法特尔沙果", "Xelphatol Apple"]),
        new("Feature.ChocoboColorPreview.Fruit.MamookPear", new(-1, 1, -1), ["辉鳞油梨", "Mamook Pear"]),
        new("Feature.ChocoboColorPreview.Fruit.OGhomoroBerries", new(-1, -1, 1), ["奥·哥摩罗浆果", "O'Ghomoro Berries"]),
        new("Feature.ChocoboColorPreview.Fruit.DomanPlum", new(-1, 1, 1), ["多玛青梅", "Doman Plum"]),
        new("Feature.ChocoboColorPreview.Fruit.Valfruit", new(1, -1, 1), ["瓦尔醋栗", "Valfruit"]),
        new("Feature.ChocoboColorPreview.Fruit.CieldalaesPineapple", new(1, 1, -1), ["谢尔达莱凤梨", "Cieldalaes Pineapple"]),
        new("Feature.ChocoboColorPreview.Fruit.HanLemon", Vector3.Zero, ["拉札罕柠檬", "Han Lemon"])
    ];

    private readonly List<ChocoboColor> colors = [];
    private readonly Dictionary<string, ChocoboFruitInfo> fruitInfo = new(StringComparer.Ordinal);
    private ChocoboColorPreviewNativeUI? nativeUI;
    private bool colorsLoaded;
    private Demihuman* appliedDrawObject;
    private byte appliedStainID;
    private byte displayedCurrentStainID = byte.MaxValue;
    private byte displayedTargetStainID = byte.MaxValue;

    public override ModuleInfo Info { get; } = new()
    {
        Title = OmniLoc.Get("ChocoboColorPreviewTitle"),
        Description = OmniLoc.Get("ChocoboColorPreviewDescription"),
        Category = ModuleCategory.Daily,
        RequiresPrivateProvider = true,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Assets/main/previews/Daily/ChocoboColorPreview-1.png"
    };

    protected override void OnEnable()
    {
        nativeUI = new(OnTargetColorSelected, PreviewTargetColor, ClearPreview);
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
    }

    protected override void OnDisable()
    {
        DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay;
        nativeUI?.Close();
        nativeUI?.Dispose();
        nativeUI = null;
        displayedCurrentStainID = byte.MaxValue;
        displayedTargetStainID = byte.MaxValue;
    }

    private void DrawOverlay()
    {
        AtkUnitBase* addon = null;
        if (!DService.Instance().ClientState.IsLoggedIn ||
            !AddonHelper.TryGetByName(BuddyAddonName, out addon) ||
            addon == null ||
            !addon->IsVisible ||
            nativeUI is null)
        {
            nativeUI?.Close();
            return;
        }

        if (!TryGetCompanion(out var battleChara, out var drawObject, out var currentStainID))
        {
            nativeUI.Close();
            return;
        }

        EnsureData();
        ApplySavedPreview(battleChara, drawObject);
        var targetStainID = config.TargetStainID == 0 ? currentStainID : config.TargetStainID;
        if (!TryGetColor(currentStainID, out var currentColor) ||
            !TryGetColor(targetStainID, out var targetColor))
        {
            nativeUI.Close();
            return;
        }

        if (displayedCurrentStainID != currentStainID || displayedTargetStainID != targetStainID)
        {
            var calculation = Calculate(currentColor.Rgb, targetColor.Rgb);
            nativeUI.UpdateData(
                currentColor.Name,
                currentColor.DisplayColor,
                targetColor.Name,
                targetColor.DisplayColor,
                BuildFruitRequirements(calculation.Fruits),
                BuildFeedOrder(calculation.Order));
            displayedCurrentStainID = currentStainID;
            displayedTargetStainID = targetStainID;
        }

        PositionNativeUI(addon, nativeUI);
    }

    private List<ChocoboFruitRequirement> BuildFruitRequirements(IReadOnlyList<FruitCount> fruitCounts)
    {
        var requirements = new List<ChocoboFruitRequirement>(fruitCounts.Count);
        for (var index = 0; index < fruitCounts.Count; index++)
        {
            var fruit = fruitCounts[index];
            var fruitInfo = GetFruitInfo(fruit.Index);
            requirements.Add(new(fruitInfo.Name, fruitInfo.IconID, fruit.Count));
        }

        return requirements;
    }

    private List<ChocoboFeedOrder> BuildFeedOrder(IReadOnlyList<int> order)
    {
        var feedOrder = new List<ChocoboFeedOrder>(order.Count);
        for (var index = 0; index < order.Count; index++)
        {
            var fruit = GetFruitInfo(order[index]);
            feedOrder.Add(new(index + 1, fruit.Name, fruit.IconID));
        }

        return feedOrder;
    }

    private void OnTargetColorSelected(byte stainID)
    {
        if (config.TargetStainID == stainID)
        {
            return;
        }

        config.TargetStainID = stainID;
        saveConfig();
    }

    private void PreviewTargetColor()
    {
        if (!TryGetCompanion(out var battleChara, out var drawObject, out var currentStainID))
        {
            return;
        }

        var targetStainID = config.TargetStainID == 0 ? currentStainID : config.TargetStainID;
        config.PreviewStainID = targetStainID;
        ApplyStain(battleChara, drawObject, targetStainID);
        saveConfig();
    }

    private void ClearPreview()
    {
        if (!TryGetCompanion(out var battleChara, out var drawObject, out var currentStainID))
        {
            return;
        }

        config.PreviewStainID = 0;
        ApplyStain(battleChara, drawObject, currentStainID);
        saveConfig();
    }

    private static void PositionNativeUI(AtkUnitBase* buddyAddon, ChocoboColorPreviewNativeUI nativeUI)
    {
        var buddyWindow = buddyAddon->WindowNode;
        var stage = AtkStage.Instance();
        if (buddyWindow == null || stage == null)
        {
            nativeUI.Close();
            return;
        }

        if (!nativeUI.IsOpen)
        {
            nativeUI.Open();
        }

        AtkUnitBase* previewAddon = nativeUI;
        if (previewAddon == null || previewAddon->WindowNode == null)
        {
            return;
        }

        var buddyState = buddyWindow->GetNodeState();
        var previewState = previewAddon->WindowNode->GetNodeState();
        var screenSize = new Vector2(stage->ScreenSize.Width, stage->ScreenSize.Height);
        var position = new Vector2(
            Math.Clamp(
                buddyState.TopLeft.X + buddyState.Width + 4f,
                8f,
                MathF.Max(8f, screenSize.X - previewState.Width - 8f)),
            Math.Clamp(
                buddyState.TopLeft.Y,
                8f,
                MathF.Max(8f, screenSize.Y - previewState.Height - 8f)));
        nativeUI.SetWindowPosition(position);
    }

    private void EnsureData()
    {
        if (!colorsLoaded)
        {
            colorsLoaded = true;
            colors.Clear();
            foreach (var stain in LuminaGetter.Get<Stain>())
            {
                if (stain.RowId is 0 or > 85)
                {
                    continue;
                }

                var name = stain.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    colors.Add(new((byte)stain.RowId, name, stain.Color.ReverseToVector4()));
                }
            }
        }

        if (fruitInfo.Count == Fruits.Length)
        {
            return;
        }

        var itemInfo = new Dictionary<string, ChocoboFruitInfo>(StringComparer.Ordinal);
        foreach (var item in LuminaGetter.Get<Item>())
        {
            var name = item.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(name))
            {
                itemInfo.TryAdd(name, new(name, item.Icon));
            }
        }

        foreach (var fruit in Fruits)
        {
            var candidate = fruit.Candidates.FirstOrDefault(itemInfo.ContainsKey);
            fruitInfo[fruit.LocalizationKey] = candidate is not null
                ? itemInfo[candidate]
                : new(OmniLoc.Get(fruit.LocalizationKey), 0);
        }
    }

    private ChocoboFruitInfo GetFruitInfo(int index) => fruitInfo[Fruits[index].LocalizationKey];

    private bool TryGetColor(byte rowID, out ChocoboColor color)
    {
        for (var index = 0; index < colors.Count; index++)
        {
            if (colors[index].RowID == rowID)
            {
                color = colors[index];
                return true;
            }
        }

        if (rowID == 0)
        {
            color = new(
                0,
                OmniLoc.Get("Feature.ChocoboColorPreview.DefaultColor"),
                new(DefaultChocoboColor / 255f, 1f));
            return true;
        }

        color = new(
            rowID,
            string.Format(OmniLoc.Get("Feature.ChocoboColorPreview.UnknownColor"), rowID),
            Vector4.One);
        return false;
    }

    private static Calculation Calculate(Vector3 source, Vector3 target)
    {
        if (Vector3.DistanceSquared(source, target) > 0f &&
            Vector3.DistanceSquared(target, DefaultChocoboColor) <= 1f)
        {
            return new([new(Fruits.Length - 1, 1)], [Fruits.Length - 1]);
        }

        var desired = new Vector3(
            MathF.Round((target.X - source.X) / ColorStep),
            MathF.Round((target.Y - source.Y) / ColorStep),
            MathF.Round((target.Z - source.Z) / ColorStep));
        var best = default(CalculationCandidate);
        for (var r = (int)desired.X - 2; r <= desired.X + 2; r++)
        {
            for (var g = (int)desired.Y - 2; g <= desired.Y + 2; g++)
            {
                for (var b = (int)desired.Z - 2; b <= desired.Z + 2; b++)
                {
                    if ((r & 1) != (g & 1) || (r & 1) != (b & 1))
                    {
                        continue;
                    }

                    var result = source + new Vector3(r, g, b) * ColorStep;
                    if (result.X is < 0 or > 255 || result.Y is < 0 or > 255 || result.Z is < 0 or > 255)
                    {
                        continue;
                    }

                    var y1 = -(g + b) / 2;
                    var y2 = -(r + b) / 2;
                    var y3 = -(r + g) / 2;
                    var count = Math.Abs(y1) + Math.Abs(y2) + Math.Abs(y3);
                    var error = Vector3.DistanceSquared(result, target);
                    if (best.IsValid && (error > best.Error || error.Equals(best.Error) && count >= best.Count))
                    {
                        continue;
                    }

                    best = new(true, error, count, [y1, y2, y3]);
                }
            }
        }

        if (!best.IsValid)
        {
            return new([], []);
        }

        var counts = new List<FruitCount>();
        for (var index = 0; index < 3; index++)
        {
            var amount = best.Deltas[index];
            var fruitIndex = amount >= 0 ? index : index + 3;
            amount = Math.Abs(amount);
            if (amount == 0)
            {
                continue;
            }

            counts.Add(new(fruitIndex, amount));
        }

        return new(counts, BuildOrder(source, target, counts));
    }

    private static List<int> BuildOrder(Vector3 source, Vector3 target, IReadOnlyList<FruitCount> counts)
    {
        var remaining = new int[Fruits.Length];
        var total = 0;
        for (var index = 0; index < counts.Count; index++)
        {
            remaining[counts[index].Index] = counts[index].Count;
            total += counts[index].Count;
        }

        var order = new List<int>(total);
        var current = source;
        for (var step = 0; step < total; step++)
        {
            var selected = -1;
            var bestDistance = float.MaxValue;
            for (var index = 0; index < remaining.Length; index++)
            {
                if (remaining[index] == 0)
                {
                    continue;
                }

                var next = current + Fruits[index].Delta * ColorStep;
                if (next.X is < 0 or > 255 || next.Y is < 0 or > 255 || next.Z is < 0 or > 255)
                {
                    continue;
                }

                var distance = Vector3.DistanceSquared(next, target);
                if (distance < bestDistance)
                {
                    selected = index;
                    bestDistance = distance;
                }
            }

            if (selected < 0)
            {
                for (var index = 0; index < remaining.Length; index++)
                {
                    for (var count = remaining[index]; count > 0; count--)
                    {
                        order.Add(index);
                    }
                }

                return order;
            }

            remaining[selected]--;
            order.Add(selected);
            current += Fruits[selected].Delta * ColorStep;
        }

        return order;
    }

    private bool TryGetCompanion(
        out BattleChara* battleChara,
        out Demihuman* drawObject,
        out byte currentStainID)
    {
        battleChara = null;
        drawObject = null;
        currentStainID = UIState.Instance()->Buddy.CompanionInfo.CurrentColorStainId;
        var localPlayer = Control.GetLocalPlayer();
        if (localPlayer == null)
        {
            return false;
        }

        battleChara = CharacterManager.Instance()->LookupBuddyByOwnerObject(localPlayer);
        if (battleChara == null || battleChara->DrawObject == null)
        {
            return false;
        }

        drawObject = (Demihuman*)battleChara->DrawObject;
        return true;
    }

    private void ApplySavedPreview(BattleChara* battleChara, Demihuman* drawObject)
    {
        if (config.PreviewStainID == 0)
        {
            appliedDrawObject = null;
            return;
        }

        if (appliedDrawObject == drawObject && appliedStainID == config.PreviewStainID)
        {
            return;
        }

        ApplyStain(battleChara, drawObject, config.PreviewStainID);
    }

    private void ApplyStain(BattleChara* battleChara, Demihuman* drawObject, byte stainID)
    {
        var stainSlot = battleChara->DrawData.EquipmentModelIds.GetPointer((int)DrawDataContainer.EquipmentSlot.Legs);
        stainSlot->Stain0 = stainID;
        drawObject->SetEquipmentSlotModel((uint)DrawDataContainer.EquipmentSlot.Legs, stainSlot);
        appliedDrawObject = drawObject;
        appliedStainID = stainID;
    }

    private readonly record struct ChocoboFruit(
        string LocalizationKey,
        Vector3 Delta,
        string[] Candidates);

    private readonly record struct ChocoboFruitInfo(string Name, uint IconID);

    private readonly record struct ChocoboColor(
        byte RowID,
        string Name,
        Vector4 DisplayColor)
    {
        public Vector3 Rgb => new Vector3(DisplayColor.X, DisplayColor.Y, DisplayColor.Z) * 255f;
    }

    private readonly record struct FruitCount(int Index, int Count);

    private readonly record struct Calculation(
        List<FruitCount> Fruits,
        List<int> Order);

    private readonly record struct CalculationCandidate(
        bool IsValid,
        float Error,
        int Count,
        int[] Deltas);
}

internal readonly record struct ChocoboFruitRequirement(string Name, uint IconID, int Count);

internal readonly record struct ChocoboFeedOrder(int Index, string Name, uint IconID);

[Serializable]
public sealed class ChocoboColorPreviewConfig
{
    public byte TargetStainID { get; set; }
    public byte PreviewStainID { get; set; }
}
