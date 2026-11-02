using System.Collections.Generic;

namespace Shared
{
    // Players' /visit requests (Command.Visit, VisitAnswerHandler): one waiting per visitor and
    // one per player asked, each answered Yes or No or running out after TimeoutMs (a No). A
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

        public sealed class Request
        {
            public int Id;
            public int Visitor;
            public string VisitorName;
            public int Target;
            public string TargetName;
            public int WorldId;
            public long Expires;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, Request> _byVisitor = new Dictionary<int, Request>();
        private readonly Dictionary<int, long> _lastAsked = new Dictionary<int, long>();
        private readonly Dictionary<(int Visitor, int Target), long> _declinedUntil = new Dictionary<(int, int), long>();
        // timed out, for the side still to be told: (request, whether the visitor has been, the target has been)
        private readonly List<(Request Request, bool VisitorTold, bool TargetTold)> _timedOut = new List<(Request, bool, bool)>();
        private int _nextId;
        private volatile int _open;

        // Whether anything waits (requests or timeouts to tell), so ticks can skip the lock.
        public bool Any => _open > 0;

        private void Count() => _open = _byVisitor.Count + _timedOut.Count;

        private static int Seconds(long ms) => (int)((ms + 999) / 1000);

        // A new request, or why there can't be one (and request null).
        public string Ask(int visitor, string visitorName, int target, string targetName, int worldId, long now, out Request request)
        {
            request = null;
            lock (_lock)
            {
                if (_byVisitor.TryGetValue(visitor, out var mine))
                {
                    if (mine.Expires > now)
                        return $"You're already waiting for {mine.TargetName} to answer.";
                    TimeOut(mine);
                }
                if (_lastAsked.TryGetValue(visitor, out var last) && now - last < CooldownMs)
                    return $"Wait {Seconds(CooldownMs - (now - last))} s before asking again.";
                if (_declinedUntil.TryGetValue((visitor, target), out var until))
                {
                    if (now < until)
                        return $"{targetName} didn't let you in: try again in {Seconds(until - now)} s.";
                    _declinedUntil.Remove((visitor, target));
                }
                foreach (var r in _byVisitor.Values)
                    if (r.Target == target && r.Expires > now)
                        return $"{targetName} is answering another visit request: try again in a moment.";

                request = new Request
                {
                    Id = ++_nextId,
                    Visitor = visitor,
                    VisitorName = visitorName,
                    Target = target,
                    TargetName = targetName,
                    WorldId = worldId,
                    Expires = now + TimeoutMs,
                };
                _byVisitor[visitor] = request;
                _lastAsked[visitor] = now;
                Count();
                return null;
            }
        }

        // The target's answer to request `id`: the request, taken off the book, or null when it
        // isn't theirs or is gone (answered, timed out). A No keeps the visitor off them for a while.
        public Request Answer(int target, int id, bool yes, long now)
        {
            lock (_lock)
            {
                Request found = null;
                foreach (var r in _byVisitor.Values)
                    if (r.Id == id && r.Target == target)
                        found = r;
                if (found == null)
                    return null;
                if (found.Expires <= now)
                {
                    TimeOut(found);
                    return null;
                }
                _byVisitor.Remove(found.Visitor);
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
                foreach (var r in _byVisitor.Values)
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

        // /visit cancel: drop the request this visitor has waiting, returning it so the target's
        // panel can be cleared.
        public Request Withdraw(int visitor)
        {
            lock (_lock)
            {
                if (!_byVisitor.Remove(visitor, out var r))
                    return null;
                Count();
                return r;
            }
        }

        // A timed-out request counts as a No.
        private void TimeOut(Request r)
        {
            _byVisitor.Remove(r.Visitor);
            _declinedUntil[(r.Visitor, r.Target)] = r.Expires + DeclinedMs;
            _timedOut.Add((r, false, false));
            Count();
        }
    }
}
