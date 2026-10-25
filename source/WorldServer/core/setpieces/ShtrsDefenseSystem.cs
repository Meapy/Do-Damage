namespace WorldServer.core.setpieces
{
    // The Avatar of the Forgotten King's arena (Aurusenth's map): the Avatar at its middle, its four
    // Killer Pillars and the eight blobomb makers its "Blobs, attack!" phase sets off. Every realm
    // has one, and the realm event places another.
    internal class ShtrsDefenseSystem : ISetPiece
    {
        // the map's size, 32 by 32: realm events centre the set piece on their spot by it
        public override int Size => 32;
        public override string Map => "Setpieces/Shtrs Defense System.jm";
    }
}
