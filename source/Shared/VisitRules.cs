namespace Shared
{
    // Where a player's /visit may take them (Command.Visit): into the dungeons, the Arena, Oryx's
    // chain and the other instances made from a portal, once each, and never back into one
    // they've been in.
    public static class VisitRules
    {
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
            public bool Full;
            public bool SinglePlayer;
        }

        // Why the visitor can't join `target` there, or null when they may.
        public static string Refusal(Place p, string target)
        {
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
            if (p.BeenIn)
                return $"You've already been in that {p.Name}: /visit can't take you back in.";
            if (p.SinglePlayer)
                return $"{target} is in a {p.Name} made for one.";
            if (p.Full)
                return $"{target}'s {p.Name} is full.";
            return null;
        }
    }
}
