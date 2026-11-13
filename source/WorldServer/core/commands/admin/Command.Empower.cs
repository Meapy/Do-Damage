using System.Linq;
using Shared;
using WorldServer.core.objects;
using WorldServer.core.resurrection;
using WorldServer.core.structures;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // /empower <boss> [health %]: raises that boss's Empowered form where the admin stands, as a
        // Necromantic Relic would, at that share of its health (to try its phases: every phase
        // above it opens as it rises; /smite takes it down further)
        internal class Empower : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "empower";

            protected override bool Process(Player player, TickTime time, string args)
            {
                args = args?.Trim() ?? "";
                var percent = 100;
                var space = args.LastIndexOf(' ');
                if (space > 0 && int.TryParse(args[(space + 1)..], out var p) && p >= 1 && p <= 100)
                {
                    percent = p;
                    args = args[..space].Trim();
                }
                var found = args.Length == 0 ? new System.Collections.Generic.List<BossInfo>() : BossResurrection.Find(args);
                var exact = found.FirstOrDefault(b => b.Boss.Equals(args, System.StringComparison.OrdinalIgnoreCase)
                    || b.Name.Equals(args, System.StringComparison.OrdinalIgnoreCase));
                if (exact == null && found.Count != 1)
                {
                    player.SendError(found.Count == 0
                        ? "Usage: /empower <boss> [health %] (part of an Elder boss's id or name, e.g. Thessal, Void Entity, Twilight)"
                        : "Which one? " + string.Join(", ", found.Select(b => b.Name)));
                    return false;
                }
                var info = exact ?? found[0];
                var boss = BossResurrection.RaiseAt(player.World, info, new Position(player.X, player.Y), 0, player.Name);
                if (boss == null)
                {
                    player.SendError($"The Empowered {info.Name} could not be made.");
                    return false;
                }
                if (percent < 100)
                    // once it has risen (its health is set as it enters the world)
                    player.World.StartNewTimer(100, (w, t) =>
                    {
                        if (boss.World == w && !boss.Dead)
                            boss.Health = System.Math.Max(1, boss.MaxHealth * percent / 100);
                    });
                player.SendInfo($"Raised {Empowered.DisplayName(boss)}" + (percent < 100 ? $" at {percent}% health." : "."));
                return true;
            }
        }
    }
}
