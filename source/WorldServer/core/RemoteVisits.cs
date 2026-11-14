using System;
using System.Linq;
using NLog;
using Shared;
using Shared.isc;
using Shared.isc.data;
using WorldServer.core.objects;
using WorldServer.core.worlds;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.core
{
    // /visit and /summon between world servers (Channel.Visit, VisitMsg): above all the old and the
    // new server of a hand-over behind the world proxy, so friends split by an update or the
    // scheduled restart can still join each other, and Testing and the main server for admins.
    //
    // Each side keeps the request in its own VisitBook: the asker's server for the asker (their
    // cooldown, "didn't answer"), the target's for the target (the Yes/No box, "ran out"), each
    // Request naming the other server (Server) and its id there (RemoteId). The server whose world
    // the player goes into invites them and makes a reconnect ticket (ConnectionManager.AddTicket),
    // sent to the player's own server in a Go: their client reconnects with it through the proxy,
    // which routes the world id to the server that owns it (Host ""), or to the other machine's
    // address. Old servers without this (ServerInfo.crossVisits false) are told apart.
    public sealed class RemoteVisits
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        // the asker's side waits this much longer than the target's for the answer to come across
        private const int GraceMs = 3000;

        private readonly GameServer _server;

        public RemoteVisits(GameServer server)
        {
            _server = server;
            server.Configuration.serverInfo.crossVisits = true;
            server.InterServerManager.AddHandler<VisitMsg>(Channel.Visit, OnMessage);
        }

        private static long Clock => Environment.TickCount64;
        private VisitBook Book => _server.Visits;
        private ConnectionManager Clients => _server.ConnectionManager;

        private void Send(string instance, VisitMsg m) => _server.InterServerManager.Publish(Channel.Visit, m, instance);

        // A player of that name on another world server (not hidden), and the server; an
        // admin-only server's players only for admins.
        public (ServerInfo Server, PlayerInfo Player)? Find(string name, Player viewer)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;
            var admin = viewer.Client.Account.Admin;
            foreach (var s in _server.InterServerManager.GetServerList())
            {
                if (s.type != ServerType.World || s.instanceId == _server.InstanceId || s.adminOnly && !admin || s.playerList == null)
                    continue;
                foreach (PlayerInfo p in s.playerList)
                    if (!p.Hidden && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                        return (s, p);
            }
            return null;
        }

        // `me` (here) asks `them` (on `server`) to let them in where they are, or with `summon` to
        // come here; admins go (or bring them) straight away. False, with the reason told, if not.
        public bool Ask(Player me, ServerInfo server, PlayerInfo them, bool summon)
        {
            if (!server.crossVisits)
            {
                me.SendError($"{them.Name} is on a world server that can't take {(summon ? "summons" : "visits")} from here yet: try again once they've moved over.");
                return false;
            }
            var acc = me.Client.Account;
            if (acc.Admin || acc.Rank >= (int)RankingType.Admin)
            {
                if (!summon)
                {
                    Send(server.instanceId, new VisitMsg { Kind = VisitMsgKind.AdminVisit, Visitor = acc.AccountId, VisitorName = me.Name, Target = them.AccountId, TargetName = them.Name });
                    me.SendInfo($"Going to {them.Name} on {server.name}...");
                    return true;
                }
                var here = me.World;
                here.Invite(them.AccountId); // even into an arena game that has started
                Player.ShieldArrival(them.AccountId);
                Send(server.instanceId, Ticket(them.AccountId, here, $"You are being summoned by {me.Name}."));
                me.SendInfo($"Summoned {them.Name}.");
                return true;
            }

            var world = summon ? me.World : null;
            var refusal = summon ? VisitRules.Refusal(Player.VisitPlace(world, them.AccountId), them.Name, summon: true) : null;
            VisitBook.Request r = null;
            if (refusal == null)
                refusal = Book.Ask(acc.AccountId, me.Name, them.AccountId, them.Name, world?.Id ?? 0, Clock, out r, summon, GraceMs);
            if (refusal != null)
            {
                me.SendError(refusal);
                return false;
            }
            r.Server = server.instanceId;
            Send(server.instanceId, new VisitMsg
            {
                Kind = VisitMsgKind.Ask,
                Summon = summon,
                RequestId = r.Id,
                Visitor = acc.AccountId,
                VisitorName = me.Name,
                Target = them.AccountId,
                TargetName = them.Name,
                WorldId = world?.Id ?? 0,
                WorldName = world?.GetDisplayName(),
            });
            return true;
        }

        // /visit cancel or /summon cancel took these back here: take them back on their servers too.
        public void Withdrawn(int visitor, System.Collections.Generic.IEnumerable<VisitBook.Request> requests)
        {
            var told = new System.Collections.Generic.HashSet<string>();
            foreach (var r in requests)
                if (r.Server != null && told.Add(r.Server))
                    Send(r.Server, new VisitMsg { Kind = VisitMsgKind.Withdraw, Visitor = visitor });
        }

        // `me` (the target, here) answered a request from a player on another server.
        public void Answered(Player me, VisitBook.Request r, bool yes)
        {
            var answer = new VisitMsg { RequestId = r.RemoteId, Summon = r.Summon, Visitor = r.Visitor, VisitorName = r.VisitorName, Target = me.AccountId, TargetName = me.Name };
            if (!yes)
            {
                answer.Kind = VisitMsgKind.No;
                Send(r.Server, answer);
                return;
            }
            if (r.Summon)
            {
                // their server checks its world still takes us, and sends the ticket
                answer.Kind = VisitMsgKind.Accept;
                Send(r.Server, answer);
                me.SendInfo($"Going to {r.VisitorName}.");
                return;
            }

            var world = me.World;
            var refusal = VisitRules.Refusal(Player.VisitPlace(world, r.Visitor), me.Name);
            if (refusal != null)
            {
                me.SendError($"{r.VisitorName} can't join you here.");
                answer.Kind = VisitMsgKind.Refused;
                answer.Text = refusal;
                Send(r.Server, answer);
                return;
            }
            world.Invite(r.Visitor);        // even into an Arena game that has started
            world.MarkBeenIn(r.Visitor);    // once only, even if they never arrive
            Player.ShieldArrival(r.Visitor);
            var go = Ticket(r.Visitor, world, $"{me.Name} let you in.");
            go.RequestId = r.RemoteId;
            go.Target = me.AccountId;
            Send(r.Server, go);
            me.SendInfo($"{r.VisitorName} is on their way.");
        }

        // A Go for player `mover` into `world` here: they're expected, with this key.
        private VisitMsg Ticket(int mover, World world, string text)
        {
            var key = Clients.AddTicket(mover, world.Id);
            return new VisitMsg
            {
                Kind = VisitMsgKind.Go,
                Mover = mover,
                WorldId = world.Id,
                WorldName = world.GetDisplayName(),
                Key = Convert.ToBase64String(key),
                Text = text,
            };
        }

        // The Reconnect for a Go from server `instance`: through this machine's world proxy
        // (Host "") when that server shares our public address, else to its own.
        private Reconnect ReconnectTo(string instance, VisitMsg m)
        {
            var there = _server.InterServerManager.GetServerInfo(instance);
            var mine = _server.Configuration.serverInfo;
            var local = there == null || there.address == mine.address && there.port == mine.port;
            var reconnect = new Reconnect
            {
                Name = m.WorldName ?? "",
                Host = local ? "" : there.address,
                Port = local ? 0 : there.port,
                GameId = m.WorldId,
            };
            reconnect.SetKey(Convert.FromBase64String(m.Key ?? ""));
            return reconnect;
        }

        private void Tell(int account, string text, bool error) =>
            Player.OnPlayersThread(Clients.FindClient(account), p =>
            {
                if (error)
                    p.SendError(text);
                else
                    p.SendInfo(text);
            });

        private void OnMessage(object sender, InterServerEventArgs<VisitMsg> e)
        {
            if (e.InstanceId == _server.InstanceId)
                return;
            try
            {
                Handle(e.InstanceId, e.Content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Visit message {0} from {1}", e.Content.Kind, e.InstanceId);
            }
        }

        private void Handle(string from, VisitMsg m)
        {
            switch (m.Kind)
            {
                case VisitMsgKind.Ask:
                    {
                        // the target is here
                        var target = Clients.FindClient(m.Target);
                        var them = target?.Player;
                        string refusal = null;
                        World world = null;
                        if (them?.World == null || target.Account.IgnoreList.Contains(m.Visitor) || them.IsHidden)
                            refusal = "Player not found!";
                        else if (!m.Summon)
                        {
                            world = them.World;
                            refusal = VisitRules.Refusal(Player.VisitPlace(world, m.Visitor), them.Name);
                        }
                        VisitBook.Request r = null;
                        if (refusal == null)
                            refusal = Book.Ask(m.Visitor, m.VisitorName, m.Target, them.Name, world?.Id ?? m.WorldId, Clock, out r, m.Summon);
                        if (refusal != null)
                        {
                            Send(from, new VisitMsg { Kind = VisitMsgKind.Refused, RequestId = m.RequestId, Visitor = m.Visitor, Target = m.Target, Text = refusal });
                            return;
                        }
                        r.Server = from;
                        r.RemoteId = m.RequestId;
                        var place = m.Summon ? m.WorldName : world.GetDisplayName();
                        // told before the box shows, so it comes ahead of a quick answer
                        Send(from, new VisitMsg { Kind = VisitMsgKind.Asked, Summon = m.Summon, RequestId = m.RequestId, Visitor = m.Visitor, Target = m.Target, TargetName = them.Name, WorldName = place });
                        Player.OnPlayersThread(target, p => p.ShowVisitRequest(r, place, (byte)(VisitBook.TimeoutMs / 1000)));
                        return;
                    }

                case VisitMsgKind.Asked:
                    {
                        var secs = VisitBook.TimeoutMs / 1000;
                        Tell(m.Visitor, m.Summon
                            ? $"Asked {m.TargetName} to come to your {m.WorldName}: they have {secs} s to answer. (/summon cancel to take it back)"
                            : $"Asked {m.TargetName} to let you into their {m.WorldName}: they have {secs} s to answer. (/visit cancel to take it back)", false);
                        return;
                    }

                case VisitMsgKind.Refused:
                    Book.Answer(m.Target, m.RequestId, true, Clock); // off the book, no wait after it
                    Tell(m.Visitor, m.Text, true);
                    return;

                case VisitMsgKind.No:
                    Book.Answer(m.Target, m.RequestId, false, Clock);
                    Tell(m.Visitor, m.Summon ? $"{m.TargetName} didn't come." : $"{m.TargetName} didn't let you in.", true);
                    return;

                case VisitMsgKind.Withdraw:
                    foreach (var r in Book.WithdrawAll(m.Visitor))
                        Player.OnPlayersThread(Clients.FindClient(r.Target), p =>
                        {
                            p.ShowVisitRequest(r, "", 0);
                            p.SendInfo($"{r.VisitorName} took back their {(r.Summon ? "summon" : "visit")} request.");
                        });
                    return;

                case VisitMsgKind.Accept:
                    {
                        // the summoner is here
                        Book.Answer(m.Target, m.RequestId, true, Clock);
                        var summoner = Clients.FindClient(m.Visitor);
                        if (summoner?.Player?.World == null)
                        {
                            Send(from, new VisitMsg { Kind = VisitMsgKind.Tell, Mover = m.Target, Error = true, Text = $"{m.VisitorName} has gone." });
                            return;
                        }
                        Player.OnPlayersThread(summoner, p =>
                        {
                            var world = p.World;
                            var refusal = VisitRules.Refusal(Player.VisitPlace(world, m.Target), m.TargetName, summon: true);
                            if (refusal != null)
                            {
                                p.SendError(refusal);
                                Send(from, new VisitMsg { Kind = VisitMsgKind.Tell, Mover = m.Target, Error = true, Text = $"You can't go to {p.Name} there." });
                                return;
                            }
                            world.Invite(m.Target);
                            world.MarkBeenIn(m.Target);
                            Player.ShieldArrival(m.Target);
                            Send(from, Ticket(m.Target, world, null));
                            p.SendInfo($"{m.TargetName} is on their way.");
                        });
                        return;
                    }

                case VisitMsgKind.Go:
                    {
                        if (m.RequestId != 0)
                            Book.Answer(m.Target, m.RequestId, true, Clock);
                        var client = Clients.FindClient(m.Mover);
                        if (client?.Player == null)
                            return;
                        var reconnect = ReconnectTo(from, m);
                        Player.OnPlayersThread(client, p =>
                        {
                            if (!string.IsNullOrEmpty(m.Text))
                                p.SendInfo(m.Text);
                            p.Client.SendPacket(reconnect);
                        });
                        return;
                    }

                case VisitMsgKind.Tell:
                    Tell(m.Mover, m.Text, m.Error);
                    return;

                case VisitMsgKind.AdminVisit:
                    {
                        var them = Clients.FindClient(m.Target)?.Player;
                        if (them?.World == null)
                        {
                            Send(from, new VisitMsg { Kind = VisitMsgKind.Tell, Mover = m.Visitor, Error = true, Text = "Player not found!" });
                            return;
                        }
                        var world = them.World;
                        world.Invite(m.Visitor); // even into an arena game that has started
                        Send(from, Ticket(m.Visitor, world, null));
                        return;
                    }
            }
        }
    }
}
