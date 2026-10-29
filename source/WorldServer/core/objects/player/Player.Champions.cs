using Shared.champions;
using WorldServer.core.champions;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // The account's places on the Hall of Champions' boards (ChampionCache) and so its rewards:
        // read on entering a world, and again within a few seconds of the boards changing.
        public ChampionPlaces Champion { get; private set; }
        // this week's fame and dungeons, for the weekly boards
        public ChampionCounter ChampionCounter { get; } = new ChampionCounter();
        private int _championVersion;
        private float _championCheckIn;

        private void InitChampion()
        {
            _championVersion = ChampionCache.Version;
            Champion = ChampionCache.PlacesOf(AccountId);
            UpdateGlow();
            UpdateNameColor();
        }

        private void TickChampion(float dt)
        {
            if (Client?.Account == null)
                return;
            ChampionCounter.Tick(dt, GameServer.Database, AccountId);
            _championCheckIn -= dt;
            if (_championCheckIn > 0)
                return;
            _championCheckIn = 5f;
            var version = ChampionCache.Version;
            if (version == _championVersion)
                return;
            _championVersion = version;
            var now = ChampionCache.PlacesOf(AccountId);
            if (now.Equals(Champion))
                return;
            var before = Champion;
            Champion = now;
            UpdateGlow();
            UpdateNameColor();
            foreach (var def in ChampionBoards.All)
            {
                int was = before.RankOn(def.Tab), rank = now.RankOn(def.Tab);
                int wasTier = ChampionBoards.TierOf(was), tier = ChampionBoards.TierOf(rank);
                if (rank == was)
                    continue;
                if (rank == 0)
                    SendInfo($"You've left the top {ChampionBoards.Places} of the {def.Name} board: its reward ({def.RewardText(wasTier)}) has ended.");
                else if (was == 0)
                    SendHelp($"You're #{rank} on the {def.Name} board in the Hall of Champions! While you hold the place: {def.RewardText(tier)}.");
                else if (tier != wasTier)
                    SendInfo($"You're now #{rank} on the {def.Name} board: {def.RewardText(tier)}.");
            }
        }

        // The glow: one the player chose (/glow) first, then a champion's, then a legend's red.
        public void UpdateGlow()
        {
            var acc = Client?.Account;
            if (acc == null)
                return;
            Glow = acc.GlowColor != 0 ? acc.GlowColor : Champion.Glow != 0 ? Champion.Glow : IsLegend ? 0xFF0000 : 0;
        }

        // The name's colour in chat: a champion's over the one chosen (the quest log's rank colour,
        // /namecolor), which stays on the account for when the place is lost.
        public void UpdateNameColor()
        {
            var acc = Client?.Account;
            if (acc == null)
                return;
            ColorNameChat = Champion.NameColor != 0 ? Champion.NameColor : acc.ColorNameChat;
        }
    }
}
