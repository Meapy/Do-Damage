using System.Collections.Generic;
using System.Linq;

namespace Shared
{
    // Players' /visit and /summon requests (Command.Visit, SummonCommand, VisitAnswerHandler):
    // one waiting per player asking, except for a help call which may ask several players, and
    // one per player asked. Each is answered Yes or No or runs out after TimeoutMs (a No). A
    // visitor waits CooldownMs between requests, and DeclinedMs after a No before asking that
    // player again. One per world server, used from every world thread, so it locks; times are
    // ms on any steady clock (Environment.TickCount64).
    public sealed class VisitBook
    {
        public const int TimeoutMs = 30000;
        public const int CooldownMs = 10000;
        public const int DeclinedMs = 60000;
        // a timed-out request waits this long for the side that hasn't heard yet to collect it
        private const int KeepTimedOutMs = 120000;

        // Visitor is the player who asked, Target the one asked; a summon brings the target to
        // the visitor instead of the visitor to the target.
        public sealed class Request
        {
            public int Id;
            public bool Summon;
            public int Visitor;
            public string VisitorName;
            public int Target;
            public string TargetName;
            public int WorldId;
            public long Expires;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, List<Request>> _byVisitor = new Dictionary<int, List<Request>>();
        private readonly Dictionary<int, Request> _byTarget = new Dictionary<int, Request>();
        private readonly Dictionary<int, long> _lastAsked = new Dictionary<int, long>();
        private readonly Dictionary<(int Visitor, int Target), long> _declinedUntil = new Dictionary<(int, int), long>();
        // timed out, for the side still to be told: (request, whether the visitor has been, the target has been)
        private readonly List<(Request Request, bool VisitorTold, bool TargetTold)> _timedOut = new List<(Request, bool, bool)>();
        private int _nextId;
        private volatile int _open;

        // Whether anything waits (requests or timeouts to tell), so ticks can skip the lock.
        public bool Any => _open > 0;

        private void Count()
        {
            var requests = 0;
            foreach (var list in _byVisitor.Values)
                requests += list.Count;
            _open = requests + _timedOut.Count;
        }

        private static int Seconds(long ms) => (int)((ms + 999) / 1000);

        // A new request, or why there can't be one (and request null).
        public string Ask(int visitor, string visitorName, int target, string targetName, int worldId, long now, out Request request, bool summon = false)
        {
            request = null;
            lock (_lock)
            {
                if (_byVisitor.TryGetValue(visitor, out var mine) && mine.Count > 0)
                {
                    var waiting = mine.FirstOrDefault(r => r.Expires > now);
                    if (waiting != null)
                        return $"You're already waiting for {waiting.TargetName} to answer.";
                    foreach (var expired in mine.ToArray())
                        TimeOut(expired);
                    Count();
                }
                if (_lastAsked.TryGetValue(visitor, out var last) && now - last < CooldownMs)
                    return $"Wait {Seconds(CooldownMs - (now - last))} s before asking again.";
                if (_declinedUntil.TryGetValue((visitor, target), out var until))
                {
                    if (now < until)
                        return $"{targetName} said no: try again in {Seconds(until - now)} s.";
                    _declinedUntil.Remove((visitor, target));
                }
                if (_byTarget.TryGetValue(target, out var incoming))
                {
                    if (incoming.Expires > now)
                        return $"{targetName} is answering another request: try again in a moment.";
                    TimeOut(incoming);
                }

                request = NewRequest(visitor, visitorName, target, targetName, worldId, now, summon);
                Add(request);
                _lastAsked[visitor] = now;
                Count();
                return null;
            }
        }

        // A help call asks every eligible player at once. Busy or recently declined targets are
        // skipped; the requester's cooldown and outstanding-call limit still apply to the batch.
        public string AskMany(int visitor, string visitorName, IEnumerable<(int Target, string TargetName)> targets, int worldId, long now, out List<Request> requests, bool summon = false)
        {
            requests = new List<Request>();
            lock (_lock)
            {
                if (_byVisitor.TryGetValue(visitor, out var mine))
                {
                    var waiting = mine.FirstOrDefault(r => r.Expires > now);
                    if (waiting != null)
                        return $"You're already waiting for {waiting.TargetName} to answer.";
                    foreach (var expired in mine.ToArray())
                        TimeOut(expired);
                    Count();
                }
                if (_lastAsked.TryGetValue(visitor, out var last) && now - last < CooldownMs)
                    return $"Wait {Seconds(CooldownMs - (now - last))} s before asking again.";

                var seen = new HashSet<int>();
                foreach (var (target, targetName) in targets)
                {
                    if (!seen.Add(target) || target == visitor)
                        continue;
                    if (_declinedUntil.TryGetValue((visitor, target), out var until))
                    {
                        if (now < until)
                            continue;
                        _declinedUntil.Remove((visitor, target));
                    }
                    if (_byTarget.TryGetValue(target, out var incoming))
                    {
                        if (incoming.Expires > now)
                            continue;
                        TimeOut(incoming);
                    }
                    requests.Add(NewRequest(visitor, visitorName, target, targetName, worldId, now, summon));
                }
                if (requests.Count == 0)
                    return "No available players could be asked right now.";

                foreach (var request in requests)
                    Add(request);
                _lastAsked[visitor] = now;
                Count();
                return null;
            }
        }

        private Request NewRequest(int visitor, string visitorName, int target, string targetName, int worldId, long now, bool summon) =>
            new Request
            {
                Id = ++_nextId,
                Summon = summon,
                Visitor = visitor,
                VisitorName = visitorName,
                Target = target,
                TargetName = targetName,
                WorldId = worldId,
                Expires = now + TimeoutMs,
            };

        private void Add(Request request)
        {
            if (!_byVisitor.TryGetValue(request.Visitor, out var requests))
                _byVisitor.Add(request.Visitor, requests = new List<Request>());
            requests.Add(request);
            _byTarget.Add(request.Target, request);
        }

        private void Remove(Request request)
        {
            if (_byVisitor.TryGetValue(request.Visitor, out var requests))
            {
                requests.Remove(request);
                if (requests.Count == 0)
                    _byVisitor.Remove(request.Visitor);
            }
            if (_byTarget.TryGetValue(request.Target, out var incoming) && incoming.Id == request.Id)
                _byTarget.Remove(request.Target);
        }

        // The target's answer to request `id`: the request, taken off the book, or null when it
        // isn't theirs or is gone (answered, timed out). A No keeps the visitor off them for a while.
        public Request Answer(int target, int id, bool yes, long now)
        {
            lock (_lock)
            {
                if (!_byTarget.TryGetValue(target, out var found) || found.Id != id)
                    return null;
                if (found.Expires <= now)
                {
                    TimeOut(found);
                    return null;
                }
                Remove(found);
                if (!yes)
                    _declinedUntil[(found.Visitor, found.Target)] = now + DeclinedMs;
                Count();
                return found;
            }
        }

        // Requests that ran out with this account as the visitor (visitor true) or the one asked,
        // each told once to each side. Called from the players' ticks.
        public List<(Request Request, bool AsVisitor)> TakeTimedOut(int account, long now)
        {
            var told = new List<(Request, bool)>();
            if (!Any)
                return told;
            lock (_lock)
            {
                var due = new List<Request>();
                foreach (var r in _byVisitor.Values.SelectMany(requests => requests))
                    if (r.Expires <= now && (r.Visitor == account || r.Target == account))
                        due.Add(r);
                foreach (var r in due)
                    TimeOut(r);

                for (var i = _timedOut.Count - 1; i >= 0; i--)
                {
                    var (r, visitorTold, targetTold) = _timedOut[i];
                    if (r.Visitor == account && !visitorTold)
                    {
                        visitorTold = true;
                        told.Add((r, true));
                    }
                    if (r.Target == account && !targetTold)
                    {
                        targetTold = true;
                        told.Add((r, false));
                    }
                    if ((visitorTold && targetTold) || now - r.Expires > KeepTimedOutMs)
                        _timedOut.RemoveAt(i);
                    else
                        _timedOut[i] = (r, visitorTold, targetTold);
                }
                Count();
            }
            return told;
        }

        // The request waiting for this player's answer (/yes, /no), or null.
        public Request WaitingFor(int target, long now)
        {
            lock (_lock)
            {
                return _byTarget.TryGetValue(target, out var request) && request.Expires > now ? request : null;
            }
        }

        // Drops all outgoing requests; returns the first for callers that expect a single request.
        public Request Withdraw(int visitor)
        {
            var requests = WithdrawAll(visitor);
            return requests.FirstOrDefault();
        }

        // /visit cancel: drop every request this visitor has waiting so all target panels clear.
        public List<Request> WithdrawAll(int visitor)
        {
            lock (_lock)
            {
                if (!_byVisitor.Remove(visitor, out var requests))
                    return new List<Request>();
                foreach (var request in requests)
                    if (_byTarget.TryGetValue(request.Target, out var incoming) && incoming.Id == request.Id)
                        _byTarget.Remove(request.Target);
                Count();
                return requests;
            }
        }

        // A timed-out request counts as a No.
        private void TimeOut(Request r)
        {
            Remove(r);
            _declinedUntil[(r.Visitor, r.Target)] = r.Expires + DeclinedMs;
            _timedOut.Add((r, false, false));
            Count();
        }
    }
}
