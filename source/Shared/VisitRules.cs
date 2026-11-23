namespace Shared
{
    // Where a player's /visit may take them (Command.Visit), and where /summon may bring a player
    // (SummonCommand): into the dungeons, the Arena, Oryx's chain and the other instances made from
    // a portal, once each, and never back into one they've been in.
    public static class VisitRules
    {
        // trips into one instance: once, or twice with the Gilded Realm Pass
        public const int Trips = 1, GildedTrips = 2;

        public enum Kind
        {
            Instance,
            Nexus,
            Realm,
            Vault,
            GuildHall,
            Test,
            // a world everyone shares that the Nexus leads to (the Daily Quest Room)
            Shared,
            // closing, or the player is between worlds
            Gone,
        }

        public struct Place
        {
            public Kind Kind;
            public string Name;
            // the visitor is there now
            public bool Same;
            // the visitor's account has had a character in this instance, or visited it before
            public bool BeenIn;
            // trips the visitor's account has used on this instance (a portal entry counts as one) and how
            // many it has (Allowed); when set, they replace BeenIn
            public int Trips;
            public int Allowed;
            public bool Full;
            public bool SinglePlayer;
            // a realm closing (Oryx is coming): no one new gets in
            public bool Closed;
            // an instance only joined at its start (a Crucible climb), now under way
            public bool Begun;
        }

        // Why the visitor can't join `target` there, or null when they may; with `summon`, why the
        // summoner can't bring `target` into the summoner's own world, `p`. With `realm` (the
        // Scrying Glass's Ask to join), a realm may be joined too: anyone may walk into one, so
        // only a closing or full one, or being there already, stops it.
        public static string Refusal(Place p, string target, bool summon = false, bool realm = false)
        {
            if (summon)
                return SummonRefusal(p, target);
            if (realm && p.Kind == Kind.Realm)
            {
                if (p.Same)
                    return $"You're already with {target}.";
                if (p.Closed)
                    return $"{target}'s realm is closing.";
                if (p.Full)
                    return $"{target}'s realm is full.";
                return null;
            }
            switch (p.Kind)
            {
                case Kind.Gone: return $"{target} is between worlds: try again in a moment.";
                case Kind.Nexus: return $"{target} is in the Nexus: go there the usual way.";
                case Kind.Realm: return $"{target} is in a realm, which /visit doesn't reach: take a realm portal from the Nexus (or /realm2).";
                case Kind.Vault: return $"{target} is in their vault, which only they can enter.";
                case Kind.GuildHall: return $"{target} is in their guild hall, which only its guild can enter.";
                case Kind.Test: return $"{target} can't be visited there.";
                case Kind.Shared: return $"{target} is in {p.Name}: go there the usual way.";
            }
            if (p.Same)
                return $"You're already with {target}.";
            if (p.Begun)
                return $"{target}'s climb of {p.Name} has begun: it can only be joined at the start.";
            if (p.BeenIn || (p.Allowed > 0 && p.Trips >= p.Allowed))
                return $"You've already been in that {p.Name}: /visit can't take you back in.";
            if (p.SinglePlayer)
                return $"{target} is in a {p.Name} made for one.";
            if (p.Full)
                return $"{target}'s {p.Name} is full.";
            return null;
        }

        private static string SummonRefusal(Place p, string target)
        {
            switch (p.Kind)
            {
                case Kind.Gone: return "You're between worlds: try again in a moment.";
                case Kind.Instance: break;
                default: return $"Players can't be summoned into {p.Name}: only into dungeons, the Arena and other instances from a portal.";
            }
            if (p.Same)
                return $"{target} is already with you.";
            if (p.Begun)
                return $"Your climb of {p.Name} has begun: nobody can join it now.";
            if (p.BeenIn || (p.Allowed > 0 && p.Trips >= p.Allowed))
                return $"{target} has already been in this {p.Name}: /summon can't bring them back in.";
            if (p.SinglePlayer)
                return $"Your {p.Name} is made for one.";
            if (p.Full)
                return $"Your {p.Name} is full.";
            return null;
        }
    }
}
