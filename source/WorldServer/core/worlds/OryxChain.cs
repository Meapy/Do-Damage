using System.Collections.Generic;

namespace WorldServer.core.worlds
{
    // The Oryx chain's stages, each world with the boss that ends it: the castle (Janus), Oryx 1 to 4
    // and Oryx the Ascended God. Each stage's boss leaves the portal to the next, and the last leads
    // home to the Nexus, so no stage gets a Realm Portal (World.BossDied). The Herald's week counts
    // a chain completed when Oryx 5 falls, and failed when a stage someone entered closes with its
    // boss alive (WeeklyStats).
    public static class OryxChain
    {
        public const string LastBoss = "Oryx the Ascended God";

        public static readonly Dictionary<string, string> StageBoss = new Dictionary<string, string>
        {
            ["Oryx's Castle"] = "md Janus the Doorwarden",
            ["Oryx's Chamber"] = "Oryx the Mad God 1",
            ["Christmas Oryxs Chamber"] = "Oryx the Mad God 1",
            ["Wine Cellar"] = "Oryx the Mad God 2",
            ["Oryx's Sanctuary"] = "Oryx the Mad God 3",
            ["Oryx's Inferno"] = "Oryx the Mad God 4",
            ["Oryx's Elysium"] = LastBoss,
        };

        public static bool IsStage(World world) => world != null && StageBoss.ContainsKey(world.IdName);
    }
}
