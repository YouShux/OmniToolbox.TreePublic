using System.Numerics;

namespace OmniToolbox.TreePublic;

public sealed partial class AutoIshgardRestoration
{
    public sealed class Config
    {
        public uint SelectedRecipeID;
        public int TicketThreshold = 5;
        public bool AutoBuyScrips;
        public int ScripStopThreshold = 9000;
        public int ScripThreshold = 9000;
        public uint ScripShopID;
        public uint ScripItemID;
        public int ScripQuantity = 1;
        public Vector2 ExpandedWindowSize = new(720f, 480f);
    }
}
