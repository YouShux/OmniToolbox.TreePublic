using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using OmniToolbox.Common.Module.Abstractions;
using OmniToolbox.Common.Module.Enums;
using OmniToolbox.Common.Module.Models;
using OmniToolbox.Host;

namespace OmniToolbox.TreePublic;

public sealed unsafe class AdventurerPlateInspector(AdventurerPlateInspectorConfig config) : ModuleBase
{
    private const string AddonName = "CharaCard";
    private AdventurerPlateSnapshot? snapshot;
    private bool showWindow;

    public override ModuleInfo Info { get; } = new()
    {
        Title = "冒险者铭牌查看器",
        Description = "查看当前打开的冒险者铭牌，导出铭牌文本并复制肖像预设。",
        Category = ModuleCategory.Daily,
        Author = "keita",
        SupportUrls = ["https://afdian.com/a/keita"]
    };

    public override bool HasSettings => true;

    protected override void OnEnable()
    {
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnPlateEvent);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, OnPlateEvent);
        DalamudServices.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnPlateClosed);
        DalamudServices.PluginInterface.UiBuilder.Draw += Draw;
    }

    protected override void OnDisable()
    {
        DalamudServices.AddonLifecycle.UnregisterListener(OnPlateEvent);
        DalamudServices.AddonLifecycle.UnregisterListener(OnPlateClosed);
        DalamudServices.PluginInterface.UiBuilder.Draw -= Draw;
        snapshot = null;
        showWindow = false;
    }

    public override bool DrawSettings()
    {
        var ignoreOwnPlate = config.IgnoreOwnPlate;
        var changed = ImGui.Checkbox("忽略自己的铭牌", ref ignoreOwnPlate);
        if (changed) config.IgnoreOwnPlate = ignoreOwnPlate;
        ImGui.TextWrapped("在游戏中打开冒险者铭牌后，模块会自动读取并显示当前铭牌数据。可在查看窗口复制文本或肖像预设。保存肖像预设后，可在支持该格式的肖像工具中导入。 ");
        return changed;
    }

    private void OnPlateEvent(AddonEvent _, AddonArgs __)
    {
        var captured = Capture();
        if (captured == null) return;
        snapshot = captured;
        if (!(config.IgnoreOwnPlate && captured.IsSelf)) showWindow = true;
    }

    private void OnPlateClosed(AddonEvent _, AddonArgs __) => showWindow = false;

    private void Draw()
    {
        if (!showWindow || snapshot == null) return;
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(560, 460), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("冒险者铭牌查看器", ref showWindow)) { ImGui.End(); return; }
        var s = snapshot;
        ImGui.TextWrapped($"{s.Name}  ·  世界 {s.WorldId}  ·  Lv.{s.Level}");
        ImGui.TextWrapped($"职业 ID: {s.ClassJobId}  标题 ID: {s.TitleId}  内容 ID: {s.ContentId}");
        ImGui.Separator();
        if (ImGui.BeginTabBar("###AdventurerPlateInspectorTabs"))
        {
            if (ImGui.BeginTabItem("铭牌"))
            {
                DrawBullet("底色：", DesignName(s.BasePlate));
                DrawBullet("花纹：", DecorationAt(s, 1));
                DrawBullet("背衬：", DecorationAt(s, 0));
                DrawBullet("顶部装饰：", HeaderName(s.TopBorder));
                DrawBullet("底部装饰：", HeaderName(s.BottomBorder));
                DrawBullet("肖像外框：", DecorationAt(s, 2));
                DrawBullet("铭牌外框：", DecorationAt(s, 3));
                DrawBullet("铭牌装饰物：", DecorationAt(s, 4));
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("肖像"))
            {
                DrawBullet("肖像职业：", JobName(s.GearClassJobId));
                DrawBullet("肖像背景：", BannerBackgroundName(s.Portrait.BannerBg));
                DrawBullet("肖像装饰框：", BannerFrameName(s.Portrait.BannerFrame));
                DrawBullet("肖像装饰物：", BannerDecorationName(s.Portrait.BannerDecoration));
                DrawBullet("动作：", PoseName(s.Portrait.BannerTimeline));
                DrawBullet("表情：", ExpressionName(s.Portrait.Expression));
                DrawBullet("灯光：环境光：", LightingColor(s.Portrait.AmbientLightingColorRed, s.Portrait.AmbientLightingColorGreen, s.Portrait.AmbientLightingColorBlue, s.Portrait.AmbientLightingBrightness));
                DrawBullet("灯光：方向性灯光：", DirectionalLighting(s.Portrait));
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("资料"))
            {
                ImGui.TextUnformatted("铭牌文本");
                ImGui.TextWrapped(string.IsNullOrWhiteSpace(s.SearchComment) ? "无" : s.SearchComment);
                ImGui.TextUnformatted("肖像装备");
                if (s.Gear.Count == 0)
                    ImGui.TextWrapped("无");
                else
                    foreach (var gear in s.Gear)
                        DrawBullet("", GearName(gear));
                ImGui.TextWrapped($"眼镜：{GlassesName(s.Glasses0)} / {GlassesName(s.Glasses1)}");
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.Separator();
        if (ImGui.Button("复制铭牌文本"))
        {
            ImGui.SetClipboardText(FormatText(s));
            DalamudServices.ChatGUI.Print("[冒险者铭牌查看器] 铭牌文本已复制到剪贴板。");
        }
        ImGui.SameLine();
        if (ImGui.Button("复制肖像预设"))
        {
            ImGui.SetClipboardText(PortraitPreset.Encode(s.Portrait));
            DalamudServices.ChatGUI.Print("[冒险者铭牌查看器] 肖像预设已复制到剪贴板。");
        }
        ImGui.SameLine();
        if (ImGui.Button("刷新")) snapshot = Capture();
        ImGui.TextWrapped("肖像预设复制后可粘贴至兼容工具使用。铭牌数据仅在游戏窗口打开期间读取。 ");
        ImGui.End();
    }

    private static string DesignName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<CharaCardBase>(); CharaCardBase row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string HeaderName(byte id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<CharaCardHeader>(); CharaCardHeader row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string DecorationName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<CharaCardDecoration>(); CharaCardDecoration row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string DecorationAt(AdventurerPlateSnapshot snapshot, int index) =>
        index < snapshot.Decorations.Count ? DecorationName(snapshot.Decorations[index]) : "无 (ID 0)";

    private static string BannerBackgroundName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<BannerBg>(); BannerBg row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string BannerFrameName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<BannerFrame>(); BannerFrame row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string BannerDecorationName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<BannerDecoration>(); BannerDecoration row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string JobName(byte id)
    {
        var sheet = DalamudServices.DataManager.GetExcelSheet<ClassJob>(); ClassJob row;
        return sheet.TryGetRow(id, out row) ? Display(row.Name.ExtractText(), id) : $"#{id}";
    }

    private static string ExpressionName(byte id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<BannerFacial>(); BannerFacial row;
        return sheet.TryGetRow(id, out row)
            ? Display(row.Emote.Value.Name.ExtractText(), id)
            : $"未知表情 (ID {id})";
    }

    private static string PoseName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<BannerTimeline>(); BannerTimeline row;
        return sheet.TryGetRow(id, out row)
            ? Display(PoseDisplayName(row), id)
            : $"未知动作 (ID {id})";
    }

    private static string PoseDisplayName(BannerTimeline row)
    {
        if (row.AdditionalData.TryGetValue<Emote>(out var emote))
            return emote.Name.ExtractText();
        return row.Name.ExtractText();
    }

    private static void DrawBullet(string label, string value)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped($"{label}{value}");
    }

    private static string Display(string name, uint id) => string.IsNullOrWhiteSpace(name) ? $"#{id}" : $"{name} (ID {id})";

    private static string LightingColor(byte red, byte green, byte blue, byte brightness) =>
        $"亮度 {brightness}，颜色 R{red} G{green} B{blue}";

    private static string DirectionalLighting(PortraitSnapshot p) =>
        $"亮度 {p.DirectionalLightingBrightness}，颜色 R{p.DirectionalLightingColorRed} G{p.DirectionalLightingColorGreen} B{p.DirectionalLightingColorBlue}，垂直角 {p.DirectionalLightingVerticalAngle}，水平角 {p.DirectionalLightingHorizontalAngle}";

    private static string GearName(GearEntry gear)
    {
        var sheet = DalamudServices.DataManager.GetExcelSheet<Item>();
        var name = sheet.TryGetRow(gear.ItemId, out var row) ? row.Name.ExtractText() : "未知物品";
        var stains = gear.Stain0 == 0 && gear.Stain1 == 0
            ? ""
            : $"，染色 {StainName(gear.Stain0)} / {StainName(gear.Stain1)}";
        return $"{name} (ID {gear.ItemId}{stains})";
    }

    private static string StainName(byte id)
    {
        if (id == 0) return "无";
        var sheet = DalamudServices.DataManager.GetExcelSheet<Stain>();
        return sheet.TryGetRow(id, out var row)
            ? $"{row.Name.ExtractText()} (ID {id})"
            : $"未知染色 (ID {id})";
    }

    private static string GlassesName(ushort id)
    {
        if (id == 0) return "无 (ID 0)";
        var sheet = DalamudServices.DataManager.GetExcelSheet<GlassesStyle>();
        return sheet.TryGetRow(id, out var row)
            ? $"{row.Name.ExtractText()} (ID {id})"
            : $"未知眼镜 (ID {id})";
    }

    private static string FormatText(AdventurerPlateSnapshot s)
    {
        var b = new StringBuilder();
        b.AppendLine($"Adventurer Plate: {s.Name} @ World {s.WorldId}");
        b.AppendLine($"Level {s.Level}  Job {s.ClassJobId}  Title {s.TitleId}");
        b.AppendLine();
        b.AppendLine("[Design]");
        b.AppendLine($"Base Plate: {DesignName(s.BasePlate)}");
        b.AppendLine($"Pattern: {DecorationAt(s, 1)}");
        b.AppendLine($"Backing: {DecorationAt(s, 0)}");
        b.AppendLine($"Top Decoration: {HeaderName(s.TopBorder)}");
        b.AppendLine($"Bottom Decoration: {HeaderName(s.BottomBorder)}");
        b.AppendLine($"Portrait Frame: {DecorationAt(s, 2)}");
        b.AppendLine($"Plate Frame: {DecorationAt(s, 3)}");
        b.AppendLine($"Plate Accent: {DecorationAt(s, 4)}");
        b.AppendLine();
        b.AppendLine("[Portrait]");
        b.AppendLine($"Background: {BannerBackgroundName(s.Portrait.BannerBg)}");
        b.AppendLine($"Frame: {BannerFrameName(s.Portrait.BannerFrame)}");
        b.AppendLine($"Accent: {BannerDecorationName(s.Portrait.BannerDecoration)}");
        b.AppendLine($"Portrait Job: {JobName(s.GearClassJobId)}");
        b.AppendLine($"Pose: {PoseName(s.Portrait.BannerTimeline)}  Expression: {ExpressionName(s.Portrait.Expression)}");
        b.AppendLine($"Ambient Light: {LightingColor(s.Portrait.AmbientLightingColorRed, s.Portrait.AmbientLightingColorGreen, s.Portrait.AmbientLightingColorBlue, s.Portrait.AmbientLightingBrightness)}");
        b.AppendLine($"Directional Light: {DirectionalLighting(s.Portrait)}");
        b.AppendLine($"Camera Zoom: {s.Portrait.CameraZoom}  Rotation: {s.Portrait.ImageRotation}");
        b.AppendLine();
        b.AppendLine("[Text]");
        b.AppendLine(string.IsNullOrWhiteSpace(s.SearchComment) ? "None" : s.SearchComment);
        b.AppendLine();
        b.AppendLine("[Portrait Gear]");
        foreach (var gear in s.Gear) b.AppendLine(GearName(gear));
        b.AppendLine($"Glasses: {GlassesName(s.Glasses0)} / {GlassesName(s.Glasses1)}");
        return b.ToString();
    }

    private static AdventurerPlateSnapshot? Capture()
    {
        var agent = AgentCharaCard.Instance();
        if (agent == null || agent->Data == null || agent->Data->IsNotCreated) return null;
        var data = agent->Data;
        var pd = data->PortraitData;
        var decorations = new List<ushort>();
        foreach (var id in data->PlateDesign.Decorations) decorations.Add(id);
        var gear = new List<GearEntry>();
        var character = data->CharaView.PortraitCharacterData;
        for (var i = 0; i < 14; i++) if (character.ItemIds[i] != 0)
            gear.Add(new GearEntry(character.ItemIds[i], character.ItemStain0Ids[i], character.ItemStain1Ids[i]));
        var local = Control.Instance()->LocalPlayer;
        return new AdventurerPlateSnapshot
        {
            Name = data->Name.ToString(), SearchComment = data->SearchComment.ToString(), WorldId = data->WorldId, Level = data->Level,
            ClassJobId = data->ClassJobId, TitleId = data->TitleId, BasePlate = data->PlateDesign.BasePlate,
            TopBorder = data->PlateDesign.TopBorder, BottomBorder = data->PlateDesign.BottomBorder,
            Decorations = decorations, Gear = gear, Glasses0 = character.GlassesIds[0], Glasses1 = character.GlassesIds[1], GearClassJobId = character.ClassJobId,
            IsSelf = local != null && local->ContentId == data->ContentId, ContentId = data->ContentId,
            Portrait = new PortraitSnapshot
            {
                CameraPositionX = pd.CameraPosition.X, CameraPositionY = pd.CameraPosition.Y, CameraPositionZ = pd.CameraPosition.Z, CameraPositionW = pd.CameraPosition.W,
                CameraTargetX = pd.CameraTarget.X, CameraTargetY = pd.CameraTarget.Y, CameraTargetZ = pd.CameraTarget.Z, CameraTargetW = pd.CameraTarget.W,
                ImageRotation = pd.ImageRotation, CameraZoom = pd.CameraZoom, BannerTimeline = pd.BannerTimeline, AnimationProgress = pd.AnimationProgress,
                Expression = pd.Expression, HeadDirectionX = pd.HeadDirection.X, HeadDirectionY = pd.HeadDirection.Y, EyeDirectionX = pd.EyeDirection.X, EyeDirectionY = pd.EyeDirection.Y,
                DirectionalLightingColorRed = pd.DirectionalLightingColorRed, DirectionalLightingColorGreen = pd.DirectionalLightingColorGreen, DirectionalLightingColorBlue = pd.DirectionalLightingColorBlue,
                DirectionalLightingBrightness = pd.DirectionalLightingBrightness, DirectionalLightingVerticalAngle = pd.DirectionalLightingVerticalAngle, DirectionalLightingHorizontalAngle = pd.DirectionalLightingHorizontalAngle,
                AmbientLightingColorRed = pd.AmbientLightingColorRed, AmbientLightingColorGreen = pd.AmbientLightingColorGreen, AmbientLightingColorBlue = pd.AmbientLightingColorBlue,
                AmbientLightingBrightness = pd.AmbientLightingBrightness, BannerBg = pd.BannerBg, BannerFrame = data->BannerFrame, BannerDecoration = data->BannerDecoration
            }
        };
    }
}

public sealed class AdventurerPlateInspectorConfig { public bool IgnoreOwnPlate { get; set; } }

public sealed record GearEntry(uint ItemId, byte Stain0, byte Stain1);
public sealed record AdventurerPlateSnapshot
{
    public string Name { get; init; } = ""; public string SearchComment { get; init; } = ""; public ushort WorldId { get; init; } public ushort Level { get; init; }
    public byte ClassJobId { get; init; } public ushort TitleId { get; init; } public ushort BasePlate { get; init; }
    public byte TopBorder { get; init; } public byte BottomBorder { get; init; } public IReadOnlyList<ushort> Decorations { get; init; } = [];
    public PortraitSnapshot Portrait { get; init; } = new(); public IReadOnlyList<GearEntry> Gear { get; init; } = [];
    public ushort Glasses0 { get; init; } public ushort Glasses1 { get; init; } public byte GearClassJobId { get; init; } public bool IsSelf { get; init; } public ulong ContentId { get; init; }
}

public sealed record PortraitSnapshot
{
    public Half CameraPositionX { get; init; } public Half CameraPositionY { get; init; } public Half CameraPositionZ { get; init; } public Half CameraPositionW { get; init; }
    public Half CameraTargetX { get; init; } public Half CameraTargetY { get; init; } public Half CameraTargetZ { get; init; } public Half CameraTargetW { get; init; }
    public short ImageRotation { get; init; } public byte CameraZoom { get; init; } public ushort BannerTimeline { get; init; } public float AnimationProgress { get; init; } public byte Expression { get; init; }
    public Half HeadDirectionX { get; init; } public Half HeadDirectionY { get; init; } public Half EyeDirectionX { get; init; } public Half EyeDirectionY { get; init; }
    public byte DirectionalLightingColorRed { get; init; } public byte DirectionalLightingColorGreen { get; init; } public byte DirectionalLightingColorBlue { get; init; } public byte DirectionalLightingBrightness { get; init; }
    public short DirectionalLightingVerticalAngle { get; init; } public short DirectionalLightingHorizontalAngle { get; init; } public byte AmbientLightingColorRed { get; init; } public byte AmbientLightingColorGreen { get; init; } public byte AmbientLightingColorBlue { get; init; } public byte AmbientLightingBrightness { get; init; }
    public ushort BannerBg { get; init; } public ushort BannerFrame { get; init; } public ushort BannerDecoration { get; init; }
}

internal static class PortraitPreset
{
    public static string Encode(PortraitSnapshot p)
    {
        using var ms = new MemoryStream(64); using var w = new BinaryWriter(ms);
        w.Write(0x53505448); w.Write((ushort)1); w.Write(p.CameraPositionX); w.Write(p.CameraPositionY); w.Write(p.CameraPositionZ); w.Write(p.CameraPositionW); w.Write(p.CameraTargetX); w.Write(p.CameraTargetY); w.Write(p.CameraTargetZ); w.Write(p.CameraTargetW); w.Write(p.ImageRotation); w.Write(p.CameraZoom); w.Write(p.BannerTimeline); w.Write(p.AnimationProgress); w.Write(p.Expression); w.Write(p.HeadDirectionX); w.Write(p.HeadDirectionY); w.Write(p.EyeDirectionX); w.Write(p.EyeDirectionY); w.Write(p.DirectionalLightingColorRed); w.Write(p.DirectionalLightingColorGreen); w.Write(p.DirectionalLightingColorBlue); w.Write(p.DirectionalLightingBrightness); w.Write(p.DirectionalLightingVerticalAngle); w.Write(p.DirectionalLightingHorizontalAngle); w.Write(p.AmbientLightingColorRed); w.Write(p.AmbientLightingColorGreen); w.Write(p.AmbientLightingColorBlue); w.Write(p.AmbientLightingBrightness); w.Write(p.BannerBg); w.Write(p.BannerFrame); w.Write(p.BannerDecoration); w.Flush(); return Convert.ToBase64String(ms.ToArray());
    }
}
