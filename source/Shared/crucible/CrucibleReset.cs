using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace Shared.crucible
{
    /// Gives every account its Crucible climbs back for the day (the admins' /resetcrucible): the
    /// `crucibleClimbs` of each account that began a climb today (its `crucibleDay`) goes back to 0,
    /// straight in Redis, online or not. The world servers then reload it for the players they
    /// hold (WorldServer CrucibleNews), whose accounts keep their own copy.
    public static class CrucibleReset
    {
        public const string DayField = "crucibleDay";
        public const string ClimbsField = "crucibleClimbs";

        // accounts read at once
        private const int Batch = 500;

        /// Whether an account has climbs to give back: it began one or more on the day given.
        public static bool ClimbedToday(int today, int lastDay, int climbs) => lastDay == today && climbs > 0;

        /// Resets every account that climbed on the UTC day of `utcNow`; returns their ids.
        public static List<int> ResetAll(IDatabase db, DateTime utcNow)
        {
            var today = CrucibleRules.Day(utcNow);
            var last = (int)db.StringGet("nextAccId") - 1;
            var reset = new List<int>();
            var fields = new RedisValue[] { DayField, ClimbsField };
            for (var from = 1; from <= last; from += Batch)
            {
                var ids = Enumerable.Range(from, Math.Min(Batch, last - from + 1)).ToArray();
                var reads = ids.Select(id => db.HashGetAsync($"account.{id}", fields)).ToArray();
                db.WaitAll(reads);
                var writes = new List<System.Threading.Tasks.Task>();
                for (var i = 0; i < ids.Length; i++)
                {
                    var v = reads[i].Result;
                    var day = v[0].TryParse(out int d) ? d : 0;
                    var climbs = v[1].TryParse(out int c) ? c : 0;
                    if (!ClimbedToday(today, day, climbs))
                        continue;
                    writes.Add(db.HashSetAsync($"account.{ids[i]}", ClimbsField, 0));
                    reset.Add(ids[i]);
                }
                if (writes.Count > 0)
                    db.WaitAll(writes.ToArray());
            }
            return reset;
        }
    }
}
