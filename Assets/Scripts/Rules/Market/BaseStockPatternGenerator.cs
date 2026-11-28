using System;
using System.Collections.Generic;

namespace BlackWhale.Rules
{
    public enum BasePatternShape { Authored, M, W, Rectangle, Flag }
    public enum HiddenPatternKind { None, StableReversal, Decline, Lightning, Crash, UpDown, DownUp }

    [Serializable]
    public sealed class BasePatternSettings
    {
        public double surgeMultiplier = 3;

        public BasePatternSettings Copy()
        {
            if (double.IsNaN(surgeMultiplier) || double.IsInfinity(surgeMultiplier) || surgeMultiplier < 1 || surgeMultiplier > 10)
                throw new ArgumentException("급등락 배율은 1~10배입니다.");
            return new BasePatternSettings { surgeMultiplier = surgeMultiplier };
        }
    }

    // Server/editor only. All values are cumulative percentage points, never compounded returns.
    public static class BaseStockPatternGenerator
    {
        public static BasePatternShape Shape(CustomStockPattern pattern)
        {
            return pattern.isBasePattern ? pattern.baseShape : BasePatternShape.Authored;
        }

        public static int[] Generate(CustomStockPattern pattern, int seed, int turn, string company,
            StockVolatilitySettings volatility, BasePatternSettings settings, StockPatternSample sample,
            HiddenPatternKind? hiddenOverride = null)
        {
            settings = (settings ?? new BasePatternSettings()).Copy();
            var shape = Shape(pattern);
            if (shape == BasePatternShape.Authored) throw new ArgumentException("규칙 기반 기본 패턴이 아닙니다.");
            uint hash;
            unchecked
            {
                hash = (uint)seed * 2654435761U ^ (uint)turn * 2246822519U ^
                    (uint)(Array.IndexOf(InvestmentRules.CompanyIds, company) + 1) * 3266489917U;
                hash ^= hash >> 16;
            }
            var random = new Random((int)(hash & 0x7fffffff));
            int limit = volatility.DailyLimitBps(company);
            int surge = (int)Math.Round(limit * settings.surgeMultiplier, MidpointRounding.AwayFromZero);
            bool volatileHidden = company == "AI" || company == "BIO";
            int hiddenSurge = volatileHidden ? limit * 2 : surge;
            int quiet = Math.Max(1, (int)Math.Round(limit * .1));

            // Hidden is chosen before any timing or geometry: its prelude owns day 22 onward.
            HiddenPatternKind hidden = HiddenPatternKind.None;
            if (company == "FOOD" || company == "AUTO") hidden = HiddenPatternKind.StableReversal;
            else if (volatileHidden)
            {
                int draw = random.Next(100);
                hidden = draw < 60 ? HiddenPatternKind.Decline : draw < 70 ? HiddenPatternKind.Lightning : HiddenPatternKind.Crash;
            }
            if (hiddenOverride.HasValue)
            {
                if (!Enum.IsDefined(typeof(HiddenPatternKind), hiddenOverride.Value))
                    throw new ArgumentOutOfRangeException(nameof(hiddenOverride));
                hidden = hiddenOverride.Value;
            }
            int last = hidden == HiddenPatternKind.Lightning || hidden == HiddenPatternKind.Crash ? 21 : 25;
            int flags = shape == BasePatternShape.Flag ? random.Next(1, 4) : 0;
            int minimum = Math.Max(10, flags * 5 + 1);
            int start = random.Next(0, Math.Min(5, last - minimum) + 1);
            int maximum = Math.Min(25, last - start);
            if (flags > 0) maximum = Math.Min(maximum, flags * 8 + (flags + 1) * 2);
            int duration = random.Next(minimum, maximum + 1);
            var values = new int[31];
            for (int day = 1; day <= 30; day++) values[day] = values[day - 1] + SmallMove(random, quiet);
            var vertexDays = new List<int> { start };
            int[] plateaus = new int[0], surges = new int[0];
            bool converging = false;
            int direction = 0;
            bool reversed = false;

            if (shape == BasePatternShape.M || shape == BasePatternShape.W)
            {
                int[] gaps = Partition(random, duration, 4, 2, 8);
                // The first five authored vertices specify relative heights. Legacy sixth points are tails.
                double[] heights = shape == BasePatternShape.M ? new[] { 0d, 1, .25, 1, -.4 }
                    : new[] { 0d, -1, -.25, -1, .4 };
                if (pattern.vertices.Count >= 5) heights = pattern.vertices.GetRange(0, 5).ToArray();
                double largestStep = 0;
                for (int i = 0; i < gaps.Length; i++) largestStep = Math.Max(largestStep, Math.Abs(heights[i + 1] - heights[i]) / gaps[i]);
                double scale = largestStep > 0 ? limit / largestStep : 0;
                int day = start, baseline = values[start];
                for (int i = 0; i < gaps.Length; i++)
                {
                    DrawTo(values, day, gaps[i], baseline + (int)Math.Round((heights[i + 1] - heights[0]) * scale));
                    day += gaps[i]; vertexDays.Add(day);
                }
            }
            else if (shape == BasePatternShape.Rectangle)
            {
                converging = random.Next(2) == 0;
                direction = random.Next(-1, 2);
                int count = random.Next(6, 11);
                int reversalDays = direction != 0 && random.Next(100) <
                    (company == "AUTO" || company == "FOOD" ? 20 : 50) ? random.Next(1, 3) : 0;
                reversed = reversalDays > 0;
                int body = duration - reversalDays;
                int[] gaps = Partition(random, body, count - 1, 1, body);
                int day = start, baseline = values[start];
                double initial = limit * .65;
                for (int i = 1; i < count; i++)
                {
                    day += gaps[i - 1];
                    double t = (double)(day - start) / body;
                    double width = initial * (converging ? 1 - t : 1);
                    double center = direction * limit * 2 * t;
                    int target = baseline + (int)Math.Round(center + (i % 2 == 0 ? 1 : -1) * width - initial);
                    DrawTo(values, day - gaps[i - 1], gaps[i - 1], target);
                    vertexDays.Add(day);
                }
                for (int i = 0; i < reversalDays; i++) values[++day] = values[day - 1] - direction * surge;
            }
            else
            {
                plateaus = new int[flags]; surges = new int[flags + 1];
                for (int i = 0; i < surges.Length; i++) surges[i] = random.Next(1, 3);
                for (int i = 0; i < plateaus.Length; i++) plateaus[i] = random.Next(4, 9);
                FitFlagDurations(duration, plateaus, surges);
                int day = start, sign = random.Next(2) == 0 ? -1 : 1;
                for (int i = 0; i <= flags; i++)
                {
                    for (int j = 0; j < surges[i]; j++) { values[day + 1] = values[day] + sign * surge; day++; }
                    vertexDays.Add(day);
                    if (i < flags)
                        for (int j = 0; j < plateaus[i]; j++) { values[day + 1] = values[day] + (j % 2 == 0 ? quiet : -quiet); day++; }
                    sign = -sign; // One shared transition separates adjacent plateaus.
                }
            }
            int end = start + duration;
            for (int day = end + 1; day <= 30; day++) values[day] = values[day - 1] + SmallMove(random, quiet);
            int eventStart = 0, eventLength = 0;
            if (hidden == HiddenPatternKind.Lightning || hidden == HiddenPatternKind.Crash)
            {
                int sign = random.Next(2) == 0 ? -1 : 1;
                eventLength = hidden == HiddenPatternKind.Lightning ? 1 : random.Next(2, 4);
                eventStart = random.Next(26, 32 - eventLength);
                for (int day = 22; day <= 30; day++)
                {
                    int move = day >= eventStart && day < eventStart + eventLength
                        ? (hidden == HiddenPatternKind.Lightning ? hiddenSurge : -hiddenSurge)
                        : sign * ((day - 22) % 2 == 0 ? quiet : -quiet);
                    values[day] = values[day - 1] + move;
                }
            }
            else if (hidden == HiddenPatternKind.StableReversal || hidden == HiddenPatternKind.UpDown || hidden == HiddenPatternKind.DownUp)
            {
                int split = random.Next(1, 5), sign = random.Next(2) == 0 ? -1 : 1;
                if (hidden == HiddenPatternKind.UpDown) sign = 1;
                else if (hidden == HiddenPatternKind.DownUp) sign = -1;
                hidden = sign > 0 ? HiddenPatternKind.UpDown : HiddenPatternKind.DownUp;
                int reversalMove = company == "FOOD" || company == "AUTO"
                    ? (int)Math.Round(limit * 1.5, MidpointRounding.AwayFromZero) : 0;
                for (int i = 0; i < 5; i++)
                    values[26 + i] = values[25 + i] + (i < split ? sign : -sign)
                        * (reversalMove > 0 ? reversalMove : random.Next(1, quiet + 1));
            }
            else if (hidden == HiddenPatternKind.Decline)
            {
                // Two rises and three equal-sized falls retain a net decline.
                // Starting with a rise also keeps day 30 strictly below day 26.
                var moves = volatileHidden ? new[] { hiddenSurge, -hiddenSurge, -hiddenSurge, -hiddenSurge }
                    : new[] { limit, limit, -surge, -surge };
                for (int i = moves.Length - 1; i > 0; i--) { int j = random.Next(i + 1); int temp = moves[i]; moves[i] = moves[j]; moves[j] = temp; }
                values[26] = values[25] + (volatileHidden ? hiddenSurge : (random.Next(2) == 0 ? limit : -limit));
                for (int i = 0; i < moves.Length; i++) values[27 + i] = values[26 + i] + moves[i];
                if (values[30] >= values[26]) values[30] = values[26] - 1;
            }
            else for (int day = 26; day <= 30; day++) values[day] = values[25];

            if (sample != null)
            {
                sample.StartDay = start; sample.EndDay = end;
                sample.Pattern = shape == BasePatternShape.M ? StockPatternKind.DoubleTop : shape == BasePatternShape.W ? StockPatternKind.DoubleBottom
                    : shape == BasePatternShape.Flag ? StockPatternKind.Flag : StockPatternKind.Rectangle;
                sample.Hidden = hidden; sample.HiddenEventStartDay = eventStart; sample.HiddenEventDays = eventLength;
                sample.VertexDays = vertexDays.ToArray(); sample.FlagPlateauDays = plateaus; sample.FlagSurgeDays = surges;
                sample.BoxConverging = converging; sample.BoxDirection = direction; sample.BoxReversed = reversed;
            }
            return values;
        }

        private static int SmallMove(Random random, int quiet) => (random.Next(2) == 0 ? -1 : 1) * random.Next(1, quiet + 1);

        private static void DrawTo(int[] values, int start, int days, int target)
        {
            int from = values[start];
            for (int i = 1; i <= days; i++) values[start + i] = from + (int)Math.Round((target - from) * (double)i / days);
        }

        private static int[] Partition(Random random, int total, int count, int min, int max)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int remaining = count - i - 1;
                int low = Math.Max(min, total - remaining * max), high = Math.Min(max, total - remaining * min);
                result[i] = random.Next(low, high + 1); total -= result[i];
            }
            return result;
        }

        // Reclaim earlier plateau days first so the last plateau always retains its four-day minimum.
        internal static void FitFlagDurations(int total, int[] plateaus, int[] surges)
        {
            int used = 0;
            foreach (int days in plateaus) used += days;
            foreach (int days in surges) used += days;
            for (int i = 0; i < plateaus.Length && used > total; i++)
                while (plateaus[i] > 4 && used > total) { plateaus[i]--; used--; }
            for (int i = 0; i < surges.Length && used > total; i++)
                while (surges[i] > 1 && used > total) { surges[i]--; used--; }
            for (int i = 0; i < plateaus.Length && used < total; i++)
                while (plateaus[i] < 8 && used < total) { plateaus[i]++; used++; }
            for (int i = 0; i < surges.Length && used < total; i++)
                while (surges[i] < 2 && used < total) { surges[i]++; used++; }
            if (used != total) throw new ArgumentException("FLAG 기간을 주어진 구간에 배치할 수 없습니다.");
        }
    }
}
