using Newtonsoft.Json;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoSwitchConfiguration
{
    public sealed class Config
    {
        public List<Rule> Rules = [];

        internal bool MigrateLegacy()
        {
            var changed = false;
            foreach (var rule in Rules)
                changed |= rule.MigrateLegacy();
            return changed;
        }
    }

    public sealed class Rule
    {
        public bool Enabled = true;
        public string Name = string.Empty;
        public uint ClassJobID;
        public uint TerritoryID;
        public int GearsetID = -1;
        public List<ActionStep> Actions = [];

        [JsonProperty("Commands", NullValueHandling = NullValueHandling.Ignore)]
        private string? legacyCommands;

        [JsonProperty("FavoriteGroup", NullValueHandling = NullValueHandling.Ignore)]
        private string? legacyFavoriteGroup;

        internal bool MigrateLegacy()
        {
            var changed = legacyCommands is not null || legacyFavoriteGroup is not null;
            if (legacyCommands is not null)
            {
                foreach (var command in legacyCommands.Split(['\r', '\n'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Actions.Add(new() { Command = command });
            }
            legacyCommands = legacyFavoriteGroup = null;
            return changed;
        }

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
        public Guid CollectionID;
        public string Command = string.Empty;
    }

    public enum ActionKind
    {
        SendCommand,
        EnableCollection,
        DisableCollection
    }
}
