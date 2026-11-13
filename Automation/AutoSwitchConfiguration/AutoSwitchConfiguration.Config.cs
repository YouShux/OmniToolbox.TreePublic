namespace OmniToolbox.TreePublic;

public sealed partial class AutoSwitchConfiguration
{
    public sealed class Config
    {
        public List<Rule> Rules = [];
    }

    public sealed class Rule
    {
        public bool             Enabled     = true;
        public string           Name        = string.Empty;
        public uint             ClassJobID;
        public uint             TerritoryID;
        public int              GearsetID   = -1;
        public List<ActionStep> Actions     = [];

        internal Rule Copy() => new()
        {
            Enabled = Enabled,
            Name = Name,
            ClassJobID = ClassJobID,
            TerritoryID = TerritoryID,
            GearsetID = GearsetID,
            Actions = Actions.ConvertAll(step => new ActionStep
            {
                Type = step.Type,
                CollectionID = step.CollectionID,
                Command = step.Command
            })
        };
    }

    public sealed class ActionStep
    {
        public ActionKind Type;
        public Guid       CollectionID;
        public string     Command      = string.Empty;
    }

    public enum ActionKind
    {
        SendCommand,
        EnableCollection,
        DisableCollection
    }
}
