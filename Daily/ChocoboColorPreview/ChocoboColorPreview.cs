using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.Interop.SpanExtensions;
using Lumina.Excel.Sheets;
using OmenTools;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Helpers;
using OmenTools.Interop.Game.Lumina;
using OmenTools.OmenService;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;

namespace OmniToolbox.TreePublic;

[ModuleApiVersion(1)]
public sealed unsafe partial class ChocoboColorPreview(
    ChocoboColorPreview.Settings config,
    System.Action saveConfig) : OmniModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Category = ModuleCategory.Daily,
        RequiresPrivateProvider = true,
        PreviewImageURL =
            "https://raw.githubusercontent.com/YouShux/OmniToolbox.Common/main/Assets/previews/Daily/ChocoboColorPreview-1.png"
    };

    public override ModuleLocalization Localization { get; } = ModuleLocalization.FromResources(
        new Dictionary<string, string>
        {
            ["Title"] = "ChocoboColorPreviewTitle",
            ["Description"] = "ChocoboColorPreviewDescription",
            ["Feature.ChocoboColorPreview.Clear"] = "Feature.ChocoboColorPreview.Clear",
            ["Feature.ChocoboColorPreview.CurrentColor"] = "Feature.ChocoboColorPreview.CurrentColor",
            ["Feature.ChocoboColorPreview.DefaultColor"] = "Feature.ChocoboColorPreview.DefaultColor",
            ["Feature.ChocoboColorPreview.FeedOrder"] = "Feature.ChocoboColorPreview.FeedOrder",
            ["Feature.ChocoboColorPreview.Fruit.CieldalaesPineapple"] = "Feature.ChocoboColorPreview.Fruit.CieldalaesPineapple",
            ["Feature.ChocoboColorPreview.Fruit.DomanPlum"] = "Feature.ChocoboColorPreview.Fruit.DomanPlum",
            ["Feature.ChocoboColorPreview.Fruit.HanLemon"] = "Feature.ChocoboColorPreview.Fruit.HanLemon",
            ["Feature.ChocoboColorPreview.Fruit.MamookPear"] = "Feature.ChocoboColorPreview.Fruit.MamookPear",
            ["Feature.ChocoboColorPreview.Fruit.OGhomoroBerries"] = "Feature.ChocoboColorPreview.Fruit.OGhomoroBerries",
            ["Feature.ChocoboColorPreview.Fruit.Valfruit"] = "Feature.ChocoboColorPreview.Fruit.Valfruit",
            ["Feature.ChocoboColorPreview.Fruit.XelphatolApple"] = "Feature.ChocoboColorPreview.Fruit.XelphatolApple",
            ["Feature.ChocoboColorPreview.FruitRequirements"] = "Feature.ChocoboColorPreview.FruitRequirements",
            ["Feature.ChocoboColorPreview.NoFruit"] = "Feature.ChocoboColorPreview.NoFruit",
            ["Feature.ChocoboColorPreview.Preview"] = "Feature.ChocoboColorPreview.Preview",
            ["Feature.ChocoboColorPreview.TargetColor"] = "Feature.ChocoboColorPreview.TargetColor",
            ["Feature.ChocoboColorPreview.UnknownColor"] = "Feature.ChocoboColorPreview.UnknownColor"
        });

    private static readonly Vector3 DefaultChocoboColor = new(219f, 180f, 87f);
    private static readonly ChocoboFruit[] Fruits =
    [
        new("Feature.ChocoboColorPreview.Fruit.XelphatolApple", new(1, -1, -1), 8157),
        new("Feature.ChocoboColorPreview.Fruit.MamookPear", new(-1, 1, -1), 8159),
        new("Feature.ChocoboColorPreview.Fruit.OGhomoroBerries", new(-1, -1, 1), 8161),
        new("Feature.ChocoboColorPreview.Fruit.DomanPlum", new(-1, 1, 1), 8158),
        new("Feature.ChocoboColorPreview.Fruit.Valfruit", new(1, -1, 1), 8160),
        new("Feature.ChocoboColorPreview.Fruit.CieldalaesPineapple", new(1, 1, -1), 8162),
        new("Feature.ChocoboColorPreview.Fruit.HanLemon", Vector3.Zero, 8163)
    ];

    private readonly List<ChocoboColor> colors = [];
    private readonly Dictionary<string, ChocoboFruitInfo> fruitInfo = new(StringComparer.Ordinal);
    private ChocoboColorPreviewNativeUI? nativeUI;
    private bool colorsLoaded;
    private Demihuman* appliedDrawObject;
    private byte appliedStainID;
    private byte displayedCurrentStainID = byte.MaxValue;
    private byte displayedTargetStainID = byte.MaxValue;

    protected override void OnStart(FeatureLifetime activation)
    {
        nativeUI = new(OnTargetColorSelected, PreviewTargetColor, ClearPreview);
        activation.Add(nativeUI);
        Activation.OnStopping(() => DalamudServices.PluginInterface.UiBuilder.Draw -= DrawOverlay);
        DalamudServices.PluginInterface.UiBuilder.Draw += DrawOverlay;
    }

    protected override void OnStop()
    {
        nativeUI?.Close();
        nativeUI = null;
        displayedCurrentStainID = byte.MaxValue;
        displayedTargetStainID = byte.MaxValue;
    }

    private void DrawOverlay()
    {
        AtkUnitBase* addon = null;
        if (!DService.Instance().ClientState.IsLoggedIn ||
            !AddonHelper.TryGetByName(BUDDY_ADDON_NAME, out addon) ||
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
        var buddyScale = buddyWindow->GetScale().X;
        if (MathF.Abs(previewAddon->WindowNode->GetScale().X - buddyScale) > 0.001f)
        {
            previewAddon->SetScale(buddyScale / AtkUnitBase.GetGlobalUIScale(), true);
        }
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

        foreach (var fruit in Fruits)
        {
            fruitInfo[fruit.LocalizationKey] = LuminaGetter.TryGetRow<Item>(fruit.ItemID, out var item)
                ? new(item.Name.ExtractText(), item.Icon)
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
                OmniLoc.Get(this, "Feature.ChocoboColorPreview.DefaultColor"),
                new(DefaultChocoboColor / 255f, 1f));
            return true;
        }

        color = new(
            rowID,
            string.Format(OmniLoc.Get(this, "Feature.ChocoboColorPreview.UnknownColor"), rowID),
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
            MathF.Round((target.X - source.X) / COLOR_STEP),
            MathF.Round((target.Y - source.Y) / COLOR_STEP),
            MathF.Round((target.Z - source.Z) / COLOR_STEP));
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

                    var result = source + new Vector3(r, g, b) * COLOR_STEP;
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

                var next = current + Fruits[index].Delta * COLOR_STEP;
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
            current += Fruits[selected].Delta * COLOR_STEP;
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
        uint ItemID);

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

    #region 常量

    private const string BUDDY_ADDON_NAME = "Buddy";

    private const int COLOR_STEP = 5;

    #endregion

    public sealed class Settings
    {
        public byte TargetStainID { get; set; }

        public byte PreviewStainID { get; set; }
    }
}
