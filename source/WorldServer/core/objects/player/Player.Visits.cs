using System;
using System.Linq;
using Shared;
using Shared.resources;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // A player's /visit (Command.Visit): the player asked gets a Yes/No in a corner of their
        // screen (VISIT_REQUEST) for VisitBook.TimeoutMs, and a Yes sends the visitor into the
        // instance they're in, if VisitRules still allow it. The book (GameServer.Visits) keeps
        // the cooldowns; each world remembers who has been in it (World.HasBeenIn).

        // The client can show visit requests (VISIT_ANSWER 255): only the Rust client from 2.3.7.
        public bool WantsVisitRequests { get; set; }

        private static long VisitClock => Environment.TickCount64;

        // Where /visit would take `visitor` to join us.
        public static VisitRules.Place VisitPlace(World world, Player visitor)
        {
            var p = new VisitRules.Place { Name = world?.GetDisplayName() ?? "" };
            if (world == null || world.Deleted)
                p.Kind = VisitRules.Kind.Gone;
            else if (world is NexusWorld || world.InstanceType == WorldResourceInstanceType.Nexus)
                p.Kind = VisitRules.Kind.Nexus;
            else if (world.IsRealm || world is RealmWorld || world.InstanceType == WorldResourceInstanceType.Realm2)
                p.Kind = VisitRules.Kind.Realm;
            else if (world is VaultWorld || world.InstanceType == WorldResourceInstanceType.Vault)
                p.Kind = VisitRules.Kind.Vault;
            else if (world is GuildHallWorld || world.InstanceType == WorldResourceInstanceType.Guild)
                p.Kind = VisitRules.Kind.GuildHall;
            else if (world is TestWorld || world.InstanceType == WorldResourceInstanceType.Test)
                p.Kind = VisitRules.Kind.Test;
            else if (world.Persist)
                p.Kind = VisitRules.Kind.Shared;
            else if (OryxChain.IsStage(world))
                p.Kind = VisitRules.Kind.OryxChain;
            else
                p.Kind = VisitRules.Kind.Instance;
            p.Same = visitor.World == world;
            p.BeenIn = world != null && world.HasBeenIn(visitor.Client.Account.AccountId);
            p.SinglePlayer = world != null && world.MaxPlayers <= 1;
            p.Full = world != null && world.IsPlayersMax();
            return p;
        }

        // Runs `action` with the client's player on its world's thread, if it's still in a world.
        private static void OnPlayersThread(Client client, Action<Player> action)
        {
            var world = client?.Player?.World;
            world?.Post(() =>
            {
                if (client.Player != null)
                    action(client.Player);
            });
        }

        // This player asks `target` to let them in where they are; false (with the reason told)
        // when they can't.
        public bool AskToVisit(Client target)
        {
            var me = Client.Account;
            var them = target.Player;
            var world = them.World;
            var refusal = VisitRules.Refusal(VisitPlace(world, this), them.Name);
            if (refusal == null && !them.WantsVisitRequests)
                refusal = $"{them.Name}'s game can't show visit requests: they need to update it.";
            if (refusal == null && target.Account.IgnoreList.Contains(me.AccountId))
                refusal = "Player not found!";
            VisitBook.Request request = null;
            if (refusal == null)
                refusal = GameServer.Visits.Ask(me.AccountId, Name, target.Account.AccountId, them.Name, world.Id, VisitClock, out request);
            if (refusal != null)
            {
                SendError(refusal);
                return false;
            }

            var place = world.GetDisplayName();
            OnPlayersThread(target, p =>
            {
                if (p.WantsVisitRequests)
                    p.Client.SendPacket(new VisitRequest { Id = request.Id, Name = Name, World = place, Seconds = VisitBook.TimeoutMs / 1000 });
            });
            SendInfo($"Asked {them.Name} to let you into their {place}: they have {VisitBook.TimeoutMs / 1000} s to answer. (/visit cancel to take it back)");
            return true;
        }

        // /visit cancel: take back the request waiting.
        public void WithdrawVisit()
        {
            var r = GameServer.Visits.Withdraw(Client.Account.AccountId);
            if (r == null)
            {
                SendInfo("You have no visit request waiting.");
                return;
            }
            var target = GameServer.ConnectionManager.FindClient(r.Target);
            OnPlayersThread(target, p =>
            {
                if (p.WantsVisitRequests)
                    p.Client.SendPacket(new VisitRequest { Id = r.Id, Name = r.VisitorName });
                p.SendInfo($"{r.VisitorName} took back their visit request.");
            });
            SendInfo($"Took back your visit request to {r.TargetName}.");
        }

        // Our answer to request `id`.
        public void AnswerVisit(int id, bool yes)
        {
            var r = GameServer.Visits.Answer(Client.Account.AccountId, id, yes, VisitClock);
            if (r == null)
            {
                if (yes)
                    SendError("That visit request has run out.");
                return;
            }
            var visitor = GameServer.ConnectionManager.FindClient(r.Visitor);
            if (!yes)
            {
                OnPlayersThread(visitor, p => p.SendError($"{Name} didn't let you in."));
                return;
            }
            if (visitor?.Player?.World == null)
            {
                SendError($"{r.VisitorName} has gone.");
                return;
            }

            // where we are now, which may not be where we were asked
            var world = World;
            var refusal = VisitRules.Refusal(VisitPlace(world, visitor.Player), Name);
            if (refusal != null)
            {
                SendError($"{r.VisitorName} can't join you here.");
                OnPlayersThread(visitor, p => p.SendError(refusal));
                return;
            }
            var visitorId = visitor.Account.AccountId;
            world.Invite(visitorId);        // even into an Arena game that has started
            world.MarkBeenIn(visitorId);    // once only, even if they never arrive
            OnPlayersThread(visitor, p =>
            {
                if (world.Deleted)
                {
                    p.SendError($"{Name}'s {world.GetDisplayName()} has closed.");
                    return;
                }
                p.SendInfo($"{Name} let you in.");
                p.Reconnect(world);
            });
            SendInfo($"{r.VisitorName} is on their way.");
        }

        // Requests that ran out, told to both sides (a No).
        private void TickVisits()
        {
            var book = GameServer.Visits;
            if (!book.Any || Client?.Account == null)
                return;
            foreach (var (r, asVisitor) in book.TakeTimedOut(Client.Account.AccountId, VisitClock))
            {
                if (asVisitor)
                {
                    SendError($"{r.TargetName} didn't answer your visit request.");
                    continue;
                }
                if (WantsVisitRequests)
                    Client.SendPacket(new VisitRequest { Id = r.Id, Name = r.VisitorName });
                SendInfo($"{r.VisitorName}'s visit request ran out.");
            }
        }
    }
}
