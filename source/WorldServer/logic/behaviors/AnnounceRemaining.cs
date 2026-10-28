using System.Linq;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.logic.behaviors
{
    // Tells the world's players, as `speaker`, how many of something are left, each time it changes:
    // Oryx 5's Trees of Doom, which keep his Holy Gates shut. `perOther` counts each `other` (the Void
    // Entity, which drops three trees when it dies) as that many more. `first` is said when the
    // first player comes, `left` with {N} for the count (`one` when it's one), `done` (if any) when
    // none are left. It counts once a second.
    internal class AnnounceRemaining : Behavior
    {
        private readonly string _speaker;
        private readonly ushort _counted;
        private readonly ushort _other;
        private readonly int _perOther;
        private readonly string _first, _left, _one, _done;

        public AnnounceRemaining(string speaker, string counted, string other, int perOther, string first, string left, string one, string done = null)
        {
            _speaker = speaker;
            _counted = GetObjType(counted);
            _other = GetObjType(other);
            _perOther = perOther;
            _first = first;
            _left = left;
            _one = one;
            _done = done;
        }

        private sealed class Count
        {
            public bool Greeted;
            public int Last = -1;
            public int Cool;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = new Count();

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            var s = (Count)state;
            if ((s.Cool -= time.ElapsedMsDelta) > 0 || host.World.Players.Count == 0)
                return;
            s.Cool = 1000;

            var enemies = host.World.Enemies.Values;
            var left = enemies.Count(e => e.ObjectType == _counted && !e.Dead)
                + _perOther * enemies.Count(e => e.ObjectType == _other && !e.Dead);
            if (!s.Greeted)
            {
                s.Greeted = true;
                s.Last = left;
                Say(host, _first);
                if (left > 0)
                    Say(host, Left(left));
                return;
            }
            if (left == s.Last)
                return;
            s.Last = left;
            Say(host, left == 0 ? _done : Left(left));
        }

        private string Left(int n) => n == 1 ? _one : _left.Replace("{N}", n.ToString());

        private void Say(Entity host, string text)
        {
            if (text != null)
                host.World.Broadcast(new Text()
                {
                    BubbleTime = 0,
                    NumStars = -1,
                    Name = "#" + _speaker,
                    Txt = text
                });
        }
    }
}
