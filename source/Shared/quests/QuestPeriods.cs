using System;

namespace Shared.quests
{
    // When quests reset, all in UTC: daily ones at midnight, weekly ones at midnight starting
    // Monday, monthly ones at midnight on the 1st. Each period has a key (a number that changes
    // when it resets) and the time of its next reset.
    public static class QuestPeriods
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // days since 1970-01-01 (a Thursday)
        public static int DayKey(DateTime utc) => (int)Math.Floor((utc - Epoch).TotalDays);

        // weeks since Monday 1970-01-05 (day 4)
        public static int WeekKey(DateTime utc) => FloorDiv(DayKey(utc) - 4, 7);

        public static int MonthKey(DateTime utc) => utc.Year * 12 + utc.Month - 1;

        public static int Key(QuestPeriod p, DateTime utc) => p switch
        {
            QuestPeriod.Daily => DayKey(utc),
            QuestPeriod.Weekly => WeekKey(utc),
            _ => MonthKey(utc),
        };

        public static DateTime NextReset(QuestPeriod p, DateTime utc)
        {
            var day = utc.Date;
            return p switch
            {
                QuestPeriod.Daily => day.AddDays(1),
                QuestPeriod.Weekly => day.AddDays(7 - ((int)day.DayOfWeek + 6) % 7),
                _ => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
            };
        }

        public static int Unix(DateTime utc) => (int)(utc - Epoch).TotalSeconds;

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
