using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using LuminaInstanceContent = Lumina.Excel.Sheets.InstanceContent;

namespace OmniToolbox.TreePublic;

internal readonly record struct LockedDutyPreviewDuty(
    uint ContentFinderConditionID,
    string Name,
    bool IsUnlocked);

internal readonly record struct LockedDutyPreviewDefinition(
    uint ContentFinderConditionID,
    uint InstanceContentID,
    string Name);

internal sealed unsafe class LockedDutyPreviewResolver
{
    private const uint BeginnerTrainingInstanceContentType = 8;
    private const uint PvpContentType = 3;
    private const uint GoldSaucerContentType = 4;

    private readonly List<LockedDutyPreviewDefinition> definitions = [];
    private readonly List<LockedDutyPreviewDuty> duties = [];

    public IReadOnlyList<LockedDutyPreviewDuty> Duties => duties;

    public int TotalCount => duties.Count;

    public int UnlockedCount { get; private set; }

    public void Refresh()
    {
        if (definitions.Count == 0)
        {
            BuildDefinitions();
        }

        duties.Clear();
        UnlockedCount = 0;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            var isUnlocked = UIState.IsInstanceContentUnlocked(definition.InstanceContentID);
            if (isUnlocked)
            {
                UnlockedCount++;
            }

            duties.Add(new(definition.ContentFinderConditionID, definition.Name, isUnlocked));
        }
    }

    private void BuildDefinitions()
    {
        var instanceContentIds = new Dictionary<uint, uint>();

        foreach (var instanceContent in LuminaGetter.Get<LuminaInstanceContent>())
        {
        if (instanceContent.RowId == 0 ||
            instanceContent.InstanceContentType.RowId == BeginnerTrainingInstanceContentType)
            {
                continue;
            }

        var contentFinderConditionID = instanceContent.ContentFinderCondition.RowId;
            if (contentFinderConditionID == 0)
            {
                continue;
            }

        instanceContentIds.TryAdd(contentFinderConditionID, instanceContent.RowId);
        }

        foreach (var condition in LuminaGetter.Get<ContentFinderCondition>())
        {
        if (condition.RowId == 0 ||
            !instanceContentIds.TryGetValue(condition.RowId, out var instanceContentId))
            {
                continue;
            }

            if ((condition.ContentType.ValueNullable?.RowId ?? 0) is PvpContentType or GoldSaucerContentType)
            {
                continue;
            }

        definitions.Add(new(condition.RowId, instanceContentId, condition.Name.ExtractText()));
        }

        definitions.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.CurrentCulture));
    }
}
