using System;
using System.Collections.Generic;
using WorldServer.core.guide;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.core.worlds.impl.doomed;
using WorldServer.networking.packets.outgoing.guide;

namespace WorldServer.core.objects
{
    // The Gilded Realm Pass's dungeon guide: in a dungeon, a client that asked for it (GUIDE_SUBSCRIBE)
    // is sent the shortest walk to the dungeon's boss (its quest, `Quest`), again each time the
    // player has moved a few squares or the boss has, and now and then while they stand still.
    // The client draws it as a trail of arrows on the ground (guide.rs).
    public partial class Player
    {
        // the client can show the guide (GUIDE_SUBSCRIBE)
        public bool GuideWanted;
        // what the last path was made for: the player's and the boss's squares, the boss, when
        // (Environment.TickCount64) and whether the client holds a path now
        private int _guideX = int.MinValue, _guideY, _guideBossX, _guideBossY, _guideBoss;
        private long _guideAt;
        private bool _guideShown;

        // squares the player or the boss must move before the path is made again, and the
        // longest wait with neither moving (ms), and after a failed search
        private const int GuideMoved = 3;
        private const long GuideEveryMs = 6000, GuideRetryMs = 5000;

        // A world with a dungeon's boss to be led to: not a realm (its events have their own
        // arrow), the Nexus, a vault, a minigame, a guild hall or stacked floors.
        private bool InDungeon => World != null && World.Quests.Count > 0 && World.Levels == null
            && World is not (RealmWorld or NexusWorld or VaultWorld or TestWorld or DoomedMinigame or GuildHallWorld or DailyQuestWorld);

        public void HandleGuide(TickTime time)
        {
            if (!GuideWanted || time.TickCount % 5 != 0)
                return;
            if (Quest is not Enemy boss || boss.Dead || Pass?.IsGilded != true || !InDungeon)
            {
                ClearGuide();
                return;
            }
            int x = (int)X, y = (int)Y, bx = (int)boss.X, by = (int)boss.Y;
            var now = Environment.TickCount64;
            var moved = Math.Max(Math.Abs(x - _guideX), Math.Abs(y - _guideY)) >= GuideMoved
                || Math.Max(Math.Abs(bx - _guideBossX), Math.Abs(by - _guideBossY)) >= GuideMoved;
            if (boss.Id == _guideBoss && !moved && now - _guideAt < (_guideShown ? GuideEveryMs : GuideRetryMs))
                return;
            _guideX = x;
            _guideY = y;
            _guideBossX = bx;
            _guideBossY = by;
            _guideBoss = boss.Id;
            _guideAt = now;
            var path = GuidePath.Find(World, x, y, bx, by);
            if (path == null || path.Count < 2)
            {
                // no way known (or we're there): nothing to show
                ClearGuide();
                return;
            }
            var msg = new DungeonGuide { ObjectType = boss.ObjectType, X = boss.X, Y = boss.Y, Length = path.Count - 1 };
            // every second square, the last always: a trail's dots, each on ground that can be walked
            for (var i = 2; i < path.Count; i += 2)
                msg.Points.Add(((ushort)path[i].X, (ushort)path[i].Y));
            if (path.Count % 2 == 0 || msg.Points.Count == 0)
                msg.Points.Add(((ushort)path[^1].X, (ushort)path[^1].Y));
            Client.SendPacket(msg);
            _guideShown = true;
        }

        // Takes the guide off the client's view (once).
        private void ClearGuide()
        {
            _guideBoss = 0;
            _guideX = int.MinValue;
            if (!_guideShown)
                return;
            _guideShown = false;
            Client.SendPacket(new DungeonGuide());
        }

        public void SetGuide(bool on)
        {
            GuideWanted = on;
            if (!on)
                ClearGuide();
        }
    }
}
