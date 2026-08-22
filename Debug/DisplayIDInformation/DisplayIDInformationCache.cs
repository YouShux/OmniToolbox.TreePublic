namespace OmniToolbox.TreePublic;

internal sealed class DisplayIDInformationCache
{
    private readonly List<ActionEntry> actions = [];
    private readonly List<StatusEntry> statuses = [];

    public int ActionCount => actions.Count;

    public int StatusCount => statuses.Count;

    public void RememberAction(uint actionID, uint value, long timestamp)
    {
        if (actionID == 0 || value == 0)
        {
            return;
        }

        actions.Add(new(actionID, value, timestamp));
        Trim(actions, 512);
    }

    public bool TryTakeAction(uint value, long now, out uint actionID)
    {
        PruneActions(now);
        for (var index = actions.Count - 1; index >= 0; index--)
        {
            if (actions[index].Value != value)
            {
                continue;
            }

            actionID = actions[index].ActionID;
            actions.RemoveAt(index);
            return true;
        }

        actionID = 0;
        return false;
    }

    public void RememberStatus(uint iconID, uint statusID, string name, long timestamp)
    {
        if (iconID == 0 || statusID == 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            var status = statuses[index];
            if (status.IconID != iconID || status.StatusID != statusID || !string.Equals(status.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            statuses[index] = new(iconID, statusID, name, timestamp);
            return;
        }

        statuses.Add(new(iconID, statusID, name, timestamp));
        Trim(statuses, 128);
    }

    public bool TryGetStatus(uint iconID, string text, long now, out uint statusID, out string name)
    {
        PruneStatuses(now);
        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            var status = statuses[index];
            if (status.IconID != iconID ||
                !text.Contains(status.Name, StringComparison.Ordinal))
            {
                continue;
            }

            statusID = status.StatusID;
            name = status.Name;
            return true;
        }

        statusID = 0;
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
            if (now - actions[index].Timestamp > 3_000)
            {
                actions.RemoveAt(index);
            }
        }
    }

    private void PruneStatuses(long now)
    {
        for (var index = statuses.Count - 1; index >= 0; index--)
        {
            if (now - statuses[index].Timestamp > 30_000)
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

    private readonly record struct ActionEntry(uint ActionID, uint Value, long Timestamp);

    private readonly record struct StatusEntry(uint IconID, uint StatusID, string Name, long Timestamp);
}
