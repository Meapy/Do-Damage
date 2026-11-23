namespace Shared.isc.data
{
    // An admin gave everyone their Crucible climbs back for this UTC day (CrucibleRules.Day), already
    // in Redis (Shared.crucible.CrucibleReset): each world server reloads them for the players it
    // holds and tells those who had climbed (WorldServer CrucibleAccess.OnReset).
    public struct CrucibleMsg
    {
        public int Day;
    }
}
