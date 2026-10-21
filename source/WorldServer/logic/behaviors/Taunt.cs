using System;
using System.Linq;
using WorldServer.core.objects;
using WorldServer.utils;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;
using WorldServer.core.worlds;

namespace WorldServer.logic.behaviors
{
    internal class Taunt : Behavior
    {
        private readonly bool broadcast = false;
        private readonly float probability = 1;
        private Cooldown cooldown = new Cooldown(0, 0);
        private int? ordered;
        private string[] text;

        // false: the line goes to chat only, without a speech bubble over the speaker (a boss's
        // bubbles hide his attacks)
        public bool Bubble { get; init; } = true;

        public Taunt(params string[] text) => this.text = text;

        public Taunt(double probability, params string[] text)
        {
            this.text = text;
            this.probability = (float)probability;
        }

        public Taunt(bool broadcast, params string[] text)
        {
            this.text = text;
            this.broadcast = broadcast;
        }

        public Taunt(Cooldown cooldown, params string[] text)
        {
            this.text = text;
            this.cooldown = cooldown;
        }

        public Taunt(double probability, bool broadcast, params string[] text)
        {
            this.text = text;
            this.probability = (float)probability;
            this.broadcast = broadcast;
        }

        public Taunt(double probability, Cooldown cooldown, params string[] text)
        {
            this.text = text;
            this.probability = (float)probability;
            this.cooldown = cooldown;
        }

        public Taunt(bool broadcast, Cooldown cooldown, params string[] text)
        {
            this.text = text;
            this.broadcast = broadcast;
            this.cooldown = cooldown;
        }

        public Taunt(double probability, bool broadcast, Cooldown cooldown, params string[] text)
        {
            this.text = text;
            this.probability = (float)probability;
            this.broadcast = broadcast;
            this.cooldown = cooldown;
        }

        protected override void OnStateEntry(Entity host, TickTime time, ref object state) => state = null;

        protected override void TickCore(Entity host, TickTime time, ref object state)
        {
            if (state != null && cooldown.CoolDown == 0)
                return;    //cooldown = 0 -> once per state entry

            var c = state == null ? cooldown.Next(Random) : (int)state;
            c -= time.ElapsedMsDelta;

            state = c;

            if (c > 0)
                return;

            c = cooldown.Next(Random);

            state = c;

            if (Random.NextDouble() >= probability)
                return;

            string taunt;

            if (ordered != null)
            {
                taunt = text[ordered.Value];
                ordered = (ordered.Value + 1) % text.Length;
            }
            else
                taunt = text[Random.Next(text.Length)];

            if (taunt.Contains("{PLAYER}"))
            {
                var player = host.GetNearestEntity(10, null);

                if (player == null)
                    return;

                taunt = taunt.Replace("{PLAYER}", player.Name);
            }

            // game objects (spawners, controllers) can taunt too; only enemies have HP and a rarity
            var enemy = host as Enemy;

            if (enemy != null)
                taunt = taunt.Replace("{HP}", enemy.Health.ToString());

            var name = host.ObjectDesc.DisplayId ?? host.ObjectDesc.IdName;
            var displayenemy =
                  enemy == null ? name :
                  enemy.IsLegendary ? $"Legendary {name}" :
                  enemy.IsEpic ? $"Epic {name}" :
                  enemy.IsRare ? $"Rare {name}" :
                  name;

            var packet = new Text() { Name = "#" + displayenemy, ObjectId = host.Id, NumStars = -1, BubbleTime = (byte)(Bubble ? 3 : 0), Recipient = "", Txt = taunt };

            if (broadcast)
                host.World.Broadcast(packet);
            else
                foreach (var i in host.World.PlayersCollision.HitTest(host.X, host.Y, 15).Where(e => e is Player))
                    if (i is Player && host.DistTo(i) < 15)
                        (i as Player).Client.SendPacket(packet);
        }
    }
}
