using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using OmenTools.Extensions;
using OmenTools.Interop.Game.Lumina;
using LuminaInstanceContent = Lumina.Excel.Sheets.InstanceContent;

namespace OmniToolbox.TreePublic;

internal readonly record struct LockedDutyPreviewDuty(
    uint ContentFinderConditionID,
    string Name,
    bool IsUnlocked,
    bool IsCompleted);

internal readonly record struct LockedDutyPreviewDefinition(
    uint ContentFinderConditionID,
    uint InstanceContentID,
    string Name);

internal sealed unsafe class LockedDutyPreviewResolver
{
    private const uint BeginnerTrainingInstanceContentType = 8;
    private const uint PvpContentType = 6;
    private const uint GoldSaucerContentType = 19;
    private const ushort InternalDutySortKey = 9997;

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
            var isCompleted = isUnlocked &&
                              UIState.IsInstanceContentCompleted(definition.InstanceContentID);
            if (isUnlocked)
            {
                UnlockedCount++;
            }

            duties.Add(new(
                definition.ContentFinderConditionID,
                definition.Name,
                isUnlocked,
                isCompleted));
        }
    }

    private void BuildDefinitions()
    {
        var instanceContentIds = new HashSet<uint>();

        foreach (var instanceContent in LuminaGetter.Get<LuminaInstanceContent>())
        {
            if (instanceContent.RowId == 0 ||
                instanceContent.InstanceContentType.RowId == BeginnerTrainingInstanceContentType)
            {
                continue;
            }

            instanceContentIds.Add(instanceContent.RowId);
        }

        foreach (var condition in LuminaGetter.Get<ContentFinderCondition>())
        {
            var instanceContentID = condition.Content.RowId;
            if (condition.RowId == 0 ||
                !instanceContentIds.Contains(instanceContentID))
            {
                continue;
            }

            if (!condition.IsInDutyFinder ||
                condition.SortKey >= InternalDutySortKey ||
                condition.ContentType.RowId is PvpContentType or GoldSaucerContentType)
            {
                continue;
            }

            var name = condition.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            definitions.Add(new(condition.RowId, instanceContentID, name));
        }

        definitions.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.CurrentCulture));
    }
}
