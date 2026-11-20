namespace Shared.isc.data
{
    // The gold store paid gold into this account (or a refund took it back): the world server it
    // plays on shows the gold Redis now holds and tells the player (WorldServer GoldNews).
    public struct GoldMsg
    {
        public int AccountId;
        // what changed: positive bought, negative taken back by a refund
        public int Gold;
    }
}
