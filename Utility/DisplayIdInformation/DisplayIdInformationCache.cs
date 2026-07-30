namespace OmniToolbox.TreeHouse.InterfaceOptimization;

internal sealed class DisplayIdInformationCache
{
    private const long ActionMaxAgeMs = 3_000;
    private const int ActionMaxCount = 512;
    private const long StatusMaxAgeMs = 30_000;
    private const int StatusMaxCount = 128;

    private readonly List<ActionEntry> actions = [];
    private readonly List<StatusEntry> statuses = [];

    public int ActionCount => actions.Count;

    public int StatusCount => statuses.Count;

    public void RememberAction(uint actionId, uint value, long timestamp)
    {
        if (actionId == 0 || value == 0)
        {
            return;
        }

        actions.Add(new(actionId, value, timestamp));
        Trim(actions, ActionMaxCount);
    }

    public bool TryTakeAction(uint value, long now, out uint actionId)
    {
        PruneActions(now);
        for (var index = actions.Count - 1; index >= 0; index--)
        {
            if (actions[index].Value != value)
            {
                continue;
            }

            actionId = actions[index].ActionId;
            actions.RemoveAt(index);
            return true;
        }

        actionId = 0;
        return false;
    }

    public void RememberStatus(uint iconId, uint statusId, string name, long timestamp)
    {
        if (iconId == 0 || statusId == 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            var status = statuses[index];
            if (status.IconId != iconId || status.StatusId != statusId || !string.Equals(status.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            statuses[index] = new(iconId, statusId, name, timestamp);
            return;
        }

        statuses.Add(new(iconId, statusId, name, timestamp));
        Trim(statuses, StatusMaxCount);
    }

    public bool TryGetStatus(uint iconId, string text, long now, out uint statusId, out string name)
    {
        PruneStatuses(now);
        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            var status = statuses[index];
            if (status.IconId != iconId ||
                !text.Contains(status.Name, StringComparison.Ordinal))
            {
                continue;
            }

            statusId = status.StatusId;
            name = status.Name;
            return true;
        }

        statusId = 0;
        name = string.Empty;
        return false;
    }

    public void Clear()
    {
        actions.Clear();
        statuses.Clear();
    }

    private void PruneActions(long now)
    {
        for (var index = actions.Count - 1; index >= 0; index--)
        {
            if (now - actions[index].Timestamp > ActionMaxAgeMs)
            {
                actions.RemoveAt(index);
            }
        }
    }

    private void PruneStatuses(long now)
    {
        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            if (now - statuses[index].Timestamp > StatusMaxAgeMs)
            {
                statuses.RemoveAt(index);
            }
        }
    }

    private static void Trim<T>(List<T> entries, int maxCount)
    {
        if (entries.Count > maxCount)
        {
            entries.RemoveRange(0, entries.Count - maxCount);
        }
    }

    private readonly record struct ActionEntry(uint ActionId, uint Value, long Timestamp);

    private readonly record struct StatusEntry(uint IconId, uint StatusId, string Name, long Timestamp);
}
