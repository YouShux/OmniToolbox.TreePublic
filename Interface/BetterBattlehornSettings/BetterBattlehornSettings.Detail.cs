using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using OmniToolbox.UI;

namespace OmniToolbox.TreePublic;

public sealed unsafe partial class BetterBattlehornSettings
{
    private const string DetailAddonName = "XBMMonsterBookDetail";
    private BattlehornDetailProgressNode? detailProgress;
    private nint detailAddon;

    private void OnDetailAddon(AddonEvent type, AddonArgs args)
    {
        if (type == AddonEvent.PreFinalize)
        {
            DisposeDetailProgress();
            return;
        }
        if (!IsRunning)
        {
            return;
        }
        var addon = (AddonXBMMonsterBookDetail*)args.Addon.Address;
        if (addon == null || addon->EntryName == null)
        {
            return;
        }
        if (detailAddon != args.Addon.Address)
        {
            DisposeDetailProgress();
            detailAddon = args.Addon.Address;
        }
        if (detailProgress != null)
        {
            detailProgress.IsVisible = false;
        }
        // 原生属性面板通过父节点隐藏，需检查整条父节点链。
        if (Visible((AtkResNode*)addon->EntryExpCount))
        {
            return;
        }
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId(AgentId.XBMMonsterNotebook);
        if (agent == null || !agent->IsAgentActive() || *((byte*)agent + 0x5C) == 0 || isCaptured == null)
        {
            return;
        }
        // 0x9C 保存原生详情页当前选中的 XBMPet RowId。
        var pet = *(uint*)((byte*)agent + 0x9C);
        if (pet is 0 or > 50 || !isCaptured(agent, pet) ||
            !TryGetPetProgress((byte)pet, out var level, out var experience))
        {
            return;
        }
        var title = addon->EntryName;
        var parent = title->ParentNode;
        if (parent == null || !Visible((AtkResNode*)title))
        {
            return;
        }
        ushort titleWidth = 0, titleHeight = 0;
        title->GetTextDrawSize(&titleWidth, &titleHeight);
        var right = parent->Width - 24f;
        var left = MathF.Max(title->X + titleWidth + 12f, right - 230f);
        var width = right - left;
        if (width < 180f)
        {
            return;
        }
        if (detailProgress == null)
        {
            detailProgress = new();
            detailProgress.AttachNode(parent);
        }
        detailProgress.Position = new(left, title->Y);
        detailProgress.Size = new(width, 30f);
        detailProgress.SetProgress(level, experience);
        detailProgress.IsVisible = true;
    }

    private static bool Visible(AtkResNode* node)
    {
        if (node == null)
        {
            return false;
        }
        for (; node != null; node = node->ParentNode)
        {
            if (!node->IsVisible())
            {
                return false;
            }
        }
        return true;
    }

    private void DisposeDetailProgress()
    {
        var node = detailProgress;
        detailProgress = null;
        detailAddon = 0;
        node?.DetachNode();
        node?.Dispose();
    }
}

internal sealed class BattlehornDetailProgressNode : ResNode
{
    private readonly TextNode levelText;
    private readonly TextNode experienceText;
    private readonly ProgressBarNode bar;

    public BattlehornDetailProgressNode()
    {
        levelText = new()
        {
            TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero,
            TextFlags = TextFlags.Ellipsis, FontSize = 12
        };
        levelText.AttachNode(this);
        experienceText = new()
        {
            TextColor = BattlehornWindowNode.Ink, TextOutlineColor = Vector4.Zero,
            TextFlags = TextFlags.Ellipsis, FontSize = 12, AlignmentType = AlignmentType.Right
        };
        experienceText.AttachNode(this);
        bar = new()
        {
            BackgroundColor = new(0.85f, 0.83f, 0.88f, 1f),
            BarColor = new(0.93f, 0.65f, 0.06f, 1f)
        };
        bar.AttachNode(this);
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        levelText.Size = new(72f, 20f);
        experienceText.Position = new(76f, 0f);
        experienceText.Size = new(MathF.Max(0f, Width - 76f), 20f);
        bar.Position = new(0f, 22f);
        bar.Size = new(Width, 6f);
    }

    public void SetProgress(byte level, byte experience)
    {
        var levelLabel = $"{OmniLoc.Get("Feature.Battlehorn.Level")} {level}";
        var experienceLabel = $"{OmniLoc.Get("Feature.Battlehorn.Experience")} {(level == 25 ? "--/--" : $"{experience}/100")}";
        if (levelText.String.ExtractText() != levelLabel)
        {
            levelText.String = levelLabel;
        }
        if (experienceText.String.ExtractText() != experienceLabel)
        {
            experienceText.String = experienceLabel;
        }
        bar.Progress = level == 25 ? 1f : experience / 100f;
    }
}
