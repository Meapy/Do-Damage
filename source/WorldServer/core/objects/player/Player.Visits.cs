using System;
using System.Collections.Generic;
using System.Linq;
using Shared;
using Shared.resources;
using StackExchange.Redis;
using WorldServer.core.worlds;
using WorldServer.core.worlds.impl;
using WorldServer.networking;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core.objects
{
    public partial class Player
    {
        // A player's /visit (Command.Visit) or /summon (SummonCommand): the player asked gets a
        // Yes/No in a corner of their screen (VISIT_REQUEST) for VisitBook.TimeoutMs, or a chat line
        // on a client that can't show it, and answers with it or /yes and /no. A Yes to a visit
        // sends the visitor into the instance the player asked is in; a Yes to a summon sends the
        // player asked into the summoner's, if VisitRules still allow it. The book
        // (GameServer.Visits) keeps the cooldowns; each world remembers who has been in it
        // (World.HasBeenIn).

        // The client can show visit requests (VISIT_ANSWER 255): the Rust client from 2.3.7.
        public bool WantsVisitRequests { get; set; }
        // ... and summons, marked by VISIT_REQUEST's last byte (VISIT_ANSWER 254): from 2.3.8.
        public bool WantsSummonRequests { get; set; }

        private static long VisitClock => Environment.TickCount64;

        // Where /visit or /summon would take `mover` into `world`.
        public static VisitRules.Place VisitPlace(World world, Player mover)
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
            else
                p.Kind = VisitRules.Kind.Instance;
            p.Same = mover.World == world;
            p.BeenIn = world != null && world.HasBeenIn(mover.Client.Account.AccountId);
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

        private static string Asking(bool summon) => summon ? "summon" : "visit";

        // Show this player the request, or take it off their screen (seconds 0). A client that
        // can show visits but not summons (2.3.7) gets summons in chat.
        private void ShowVisitRequest(VisitBook.Request r, string place, byte seconds)
        {
            if (WantsVisitRequests && (!r.Summon || WantsSummonRequests))
                Client.SendPacket(new VisitRequest { Id = r.Id, Name = r.VisitorName, World = place, Seconds = seconds, Summon = WantsSummonRequests ? r.Summon : null });
            else if (seconds > 0)
                SendInfo(r.Summon
                    ? $"{r.VisitorName} wants to summon you to their {place}: type /yes or /no ({seconds} s)."
                    : $"{r.VisitorName} wants to visit you: type /yes or /no ({seconds} s).");
        }

        // This player asks `target` to let them in where they are (a visit), or to come to them
        // (a summon); false (with the reason told) when they can't.
        public bool AskToVisit(Client target, bool summon = false)
        {
            var me = Client.Account;
            var them = target.Player;
            var world = summon ? World : them.World;
            var refusal = VisitRules.Refusal(VisitPlace(world, summon ? them : this), them.Name, summon);
            if (refusal == null && target.Account.IgnoreList.Contains(me.AccountId))
                refusal = "Player not found!";
            VisitBook.Request request = null;
            if (refusal == null)
                refusal = GameServer.Visits.Ask(me.AccountId, Name, target.Account.AccountId, them.Name, world.Id, VisitClock, out request, summon);
            if (refusal != null)
            {
                SendError(refusal);
                return false;
            }

            var place = world.GetDisplayName();
            var secs = VisitBook.TimeoutMs / 1000;
            OnPlayersThread(target, p => p.ShowVisitRequest(request, place, (byte)secs));
            SendInfo(summon
                ? $"Asked {them.Name} to come to your {place}: they have {secs} s to answer. (/summon cancel to take it back)"
                : $"Asked {them.Name} to let you into their {place}: they have {secs} s to answer. (/visit cancel to take it back)");
            return true;
        }

        // A help call is a batch of ordinary summon requests to eligible players.
        public List<VisitBook.Request> AskForHelp(IEnumerable<Client> targets, bool global = false)
        {
            var world = World;
            if (VisitPlace(world, this).Kind != VisitRules.Kind.Instance)
            {
                SendError("You can only call for help from a dungeon or instance.");
                return new List<VisitBook.Request>();
            }

            var accountId = Client.Account.AccountId;
            var placeName = world.GetDisplayName();
            var now = VisitClock;
            if (global)
            {
                var cooldownKey = $"helpCallCooldown:{accountId}";
                if (!GameServer.Database.Conn.StringSet(cooldownKey, 1, TimeSpan.FromMilliseconds(VisitBook.HelpCooldownMs), When.NotExists))
                {
                    var remaining = GameServer.Database.Conn.KeyTimeToLive(cooldownKey);
                    var seconds = (int)Math.Ceiling((remaining ?? TimeSpan.FromMilliseconds(VisitBook.HelpCooldownMs)).TotalSeconds);
                    SendError($"Wait {seconds} s before calling for help again.");
                    return new List<VisitBook.Request>();
                }
            }

            var candidates = targets
                .Where(target => target?.Player?.World != null && target.Account != null && target.Account.AccountId != accountId)
                .Where(target =>
                {
                    var player = target.Player;
                    var place = VisitPlace(player.World, player);
                    return place.Kind is VisitRules.Kind.Nexus or VisitRules.Kind.Realm
                        && player.CanBeSeenBy(this)
                        && !target.Account.IgnoreList.Contains(accountId)
                        && VisitRules.Refusal(VisitPlace(world, player), player.Name, summon: true) == null;
                })
                .Select(target => (target.Account.AccountId, target.Player.Name))
                .ToArray();

            if (candidates.Length == 0 && !global)
            {
                SendInfo("No available players can join your world right now.");
                return new List<VisitBook.Request>();
            }

            var refusal = GameServer.Visits.AskMany(accountId, Name, candidates, world.Id, now, out var requests, summon: true, helpCall: global);
            if (refusal != null)
            {
                SendError(refusal);
                return requests;
            }

            foreach (var request in requests)
            {
                var target = GameServer.ConnectionManager.FindClient(request.Target);
                OnPlayersThread(target, player =>
                {
                    player.ShowVisitRequest(request, placeName, (byte)(VisitBook.TimeoutMs / 1000));
                    if (!global)
                        player.HelpCallReceived(Name, Stars, placeName);
                });
            }
            if (global)
                GameServer.ChatManager.HelpCall(this);
            SendInfo(requests.Count == 0
                ? "No available players can join your world right now."
                : $"Asked {requests.Count} players to come to your {placeName}.");
            return requests;
        }

        // /visit cancel or /summon cancel: take back every outgoing request.
        public void WithdrawVisit()
        {
            var requests = GameServer.Visits.WithdrawAll(Client.Account.AccountId);
            if (requests.Count == 0)
            {
                SendInfo("You have no request waiting.");
                return;
            }
            foreach (var r in requests)
            {
                var target = GameServer.ConnectionManager.FindClient(r.Target);
                OnPlayersThread(target, p =>
                {
                    p.ShowVisitRequest(r, "", 0);
                    p.SendInfo($"{r.VisitorName} took back their {Asking(r.Summon)} request.");
                });
            }
            SendInfo(requests.Count == 1
                ? $"Took back your {Asking(requests[0].Summon)} request to {requests[0].TargetName}."
                : $"Took back your {Asking(requests[0].Summon)} requests to {requests.Count} players.");
        }

        // /yes or /no: answer the request waiting, and hide its box.
        public void AnswerVisitByChat(bool yes)
        {
            var r = GameServer.Visits.WaitingFor(Client.Account.AccountId, VisitClock);
            if (r == null)
            {
                SendInfo("Nobody is asking to visit or summon you.");
                return;
            }
            ShowVisitRequest(r, "", 0);
            if (!yes)
                SendInfo(r.Summon ? $"You didn't go to {r.VisitorName}." : $"You didn't let {r.VisitorName} in.");
            AnswerVisit(r.Id, yes);
        }

        // Our answer to request `id`.
        public void AnswerVisit(int id, bool yes)
        {
            var r = GameServer.Visits.Answer(Client.Account.AccountId, id, yes, VisitClock);
            if (r == null)
            {
                if (yes)
                    SendError("That request has run out.");
                return;
            }
            var asker = GameServer.ConnectionManager.FindClient(r.Visitor);
            if (!yes)
            {
                OnPlayersThread(asker, p => p.SendError(r.Summon ? $"{Name} didn't come." : $"{Name} didn't let you in."));
                return;
            }
            if (asker?.Player?.World == null)
            {
                SendError($"{r.VisitorName} has gone.");
                return;
            }
            if (r.Summon)
                GoToSummoner(asker);
            else
                LetVisitorIn(asker, r.VisitorName);
        }

        // A Yes to a visit: the visitor comes to where we are now (which may not be where we were asked).
        private void LetVisitorIn(Client visitor, string visitorName)
        {
            var world = World;
            var refusal = VisitRules.Refusal(VisitPlace(world, visitor.Player), Name);
            if (refusal != null)
            {
                SendError($"{visitorName} can't join you here.");
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
            SendInfo($"{visitorName} is on their way.");
        }

        // A Yes to a summon: we go to where the summoner is now.
        private void GoToSummoner(Client summoner)
        {
            var world = summoner.Player.World;
            var name = summoner.Player.Name;
            var refusal = VisitRules.Refusal(VisitPlace(world, this), Name, summon: true);
            if (refusal != null)
            {
                SendError($"You can't go to {name} there.");
                OnPlayersThread(summoner, p => p.SendError(refusal));
                return;
            }
            var myId = Client.Account.AccountId;
            world.Invite(myId);
            world.MarkBeenIn(myId);
            OnPlayersThread(summoner, p => p.SendInfo($"{Name} is on their way."));
            SendInfo($"Going to {name}.");
            Reconnect(world);
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
                    SendError($"{r.TargetName} didn't answer your {Asking(r.Summon)} request.");
                    continue;
                }
                ShowVisitRequest(r, "", 0);
                SendInfo($"{r.VisitorName}'s {Asking(r.Summon)} request ran out.");
            }
        }
    }
}
