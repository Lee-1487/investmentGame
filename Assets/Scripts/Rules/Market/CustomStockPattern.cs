using System;
using System.Collections.Generic;

namespace BlackWhale.Rules
{
    // Plain serializable data: usable by the server without a Unity dependency.
    [Serializable]
    public sealed class CustomStockPattern
    {
        public string id = "", name = "새 패턴", companyId = "AI";
        public bool enabled = true;
        public bool isBasePattern;
        public BasePatternShape baseShape;
        public bool useCustomProbability;
        public double probabilityPercent;
        public int amplitudeBps = 1000;
        public int startDay;
        public List<double> vertices = new List<double>();
        public List<int> segmentDays = new List<int>();
        public const int MaximumDays = 3650;
        public int MinimumDays => Math.Max(0, vertices.Count - 1);
        public int TotalDays { get { int sum = 0; foreach (int days in segmentDays) sum += days; return sum; } }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(BasePatternShape), baseShape)) throw new ArgumentException("알 수 없는 기본 패턴 규칙입니다.");
            if (Array.IndexOf(InvestmentRules.CompanyIds, companyId) < 0) throw new ArgumentException("Unknown company.");
            if (useCustomProbability && (double.IsNaN(probabilityPercent) || double.IsInfinity(probabilityPercent) || probabilityPercent < 0 || probabilityPercent > 100))
                throw new ArgumentException("패턴 확률은 0~100%입니다.");
            if (vertices == null || segmentDays == null || vertices.Count < 2 || segmentDays.Count != vertices.Count - 1)
                throw new ArgumentException("두 개 이상의 점을 하나의 꺾은선으로 연결하세요.");
            if (isBasePattern && (baseShape == BasePatternShape.M || baseShape == BasePatternShape.W) && vertices.Count < 5)
                throw new ArgumentException("M/W 기본 패턴의 높이를 정의하려면 최소 5개의 점이 필요합니다.");
            if (vertices.Count > MaximumDays + 1 || amplitudeBps < 1 || amplitudeBps > 100000)
                throw new ArgumentException("가격 폭 또는 정점 수가 허용 범위를 벗어났습니다.");
            foreach (double value in vertices)
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Invalid vertex.");
            long sum = 0;
            foreach (int days in segmentDays) { if (days < 1) throw new ArgumentException("각 구간은 최소 1일입니다."); sum += days; }
            if (startDay < 0 || sum + startDay > MaximumDays) throw new ArgumentException("마지막 점은 최대 3650일입니다.");
        }

        // Constrained apportionment: every vertex lands on a distinct integer day.
        // Largest remainders distribute extra days while retaining segment proportions.
        public void SetDuration(int requestedDays)
        {
            Validate();
            int total = Math.Max(MinimumDays, Math.Min(MaximumDays - startDay, requestedDays));
            int count = segmentDays.Count, remaining = total;
            var result = new int[count];
            var fixedSegment = new bool[count];
            double weight = TotalDays;
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < count; i++)
                    if (!fixedSegment[i] && remaining * segmentDays[i] / weight < 1)
                    { fixedSegment[i] = true; result[i] = 1; remaining--; weight -= segmentDays[i]; changed = true; }
            } while (changed && weight > 0);
            var fractions = new double[count];
            int assigned = 0;
            for (int i = 0; i < count; i++)
                if (!fixedSegment[i])
                {
                    double exact = remaining * segmentDays[i] / weight;
                    result[i] = (int)Math.Floor(exact); fractions[i] = exact - result[i]; assigned += result[i];
                }
            for (int extra = remaining - assigned; extra > 0; extra--)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                    if (!fixedSegment[i] && (best < 0 || fractions[i] > fractions[best])) best = i;
                result[best]++; fractions[best] = -1;
            }
            segmentDays = new List<int>(result);
        }

        public double[] GenerateRaw()
        {
            Validate();
            double low = vertices[0], high = low;
            foreach (double value in vertices) { low = Math.Min(low, value); high = Math.Max(high, value); }
            double scale = high > low ? amplitudeBps / (high - low) : 0;
            var result = new double[TotalDays + 1];
            int day = 0;
            for (int segment = 0; segment < segmentDays.Count; segment++)
                for (int step = 1; step <= segmentDays[segment]; step++)
                    result[++day] = (vertices[segment] - vertices[0] +
                        (vertices[segment + 1] - vertices[segment]) * step / segmentDays[segment]) * scale;
            return result;
        }

        // Normalize the largest daily step to the company's fixed daily limit. No jitter, drift or event overlays.
        public int[] GenerateForCompany(int turn, out double appliedScale, StockVolatilitySettings volatility = null, string targetCompany = null)
        {
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            var raw = GenerateRaw();
            int limit = (volatility ?? new StockVolatilitySettings()).DailyLimitBps(targetCompany ?? companyId);
            double largestStep = 0;
            for (int day = 1; day < raw.Length; day++) largestStep = Math.Max(largestStep, Math.Abs(raw[day] - raw[day - 1]));
            appliedScale = largestStep > 0 ? Math.Max(.5, limit - 1.0) / largestStep : 1;
            // Keep the price positive while uniformly retaining the authored geometry.
            foreach (double value in raw) if (value < 0) appliedScale = Math.Min(appliedScale, 9999.0 / -value);
            var result = new int[raw.Length];
            for (int day = 1; day < raw.Length; day++) result[day] = (int)Math.Round(raw[day] * appliedScale, MidpointRounding.AwayFromZero);
            return result;
        }

        public int[] GenerateTimeline(string company, int turn, StockVolatilitySettings volatility, out double appliedScale)
        {
            var path = GenerateForCompany(turn, out appliedScale, volatility, company);
            var values = new int[Math.Max(30, startDay + TotalDays) + 1];
            for (int day = startDay; day < values.Length; day++) values[day] = path[Math.Min(day - startDay, path.Length - 1)];
            return values;
        }

        public CustomStockPattern Copy() => new CustomStockPattern { id = id, name = name, companyId = companyId,
            enabled = enabled, isBasePattern = isBasePattern, baseShape = baseShape, useCustomProbability = useCustomProbability, probabilityPercent = probabilityPercent,
            amplitudeBps = amplitudeBps, startDay = startDay, vertices = new List<double>(vertices), segmentDays = new List<int>(segmentDays) };

        public static CustomStockPattern Preset(string company, int preset)
        {
            double[][] shapes = { new[] { 0.0, 1, .25, 1, -.4, -.85 }, new[] { 0.0, -1, -.25, -1, .4, .85 },
                new[] { 0.0, 1, .7, .8, .55, 1.6 }, new[] { 0.0, .85, -.65, .95, -.8, .75, .1 } };
            string[] names = { "M", "W", "FLAG", "박스권" };
            var pattern = new CustomStockPattern { companyId = company, name = names[preset],
                baseShape = new[] { BasePatternShape.M, BasePatternShape.W, BasePatternShape.Flag, BasePatternShape.Rectangle }[preset],
                vertices = new List<double>(shapes[preset]) };
            for (int i = 1; i < pattern.vertices.Count; i++) pattern.segmentDays.Add(1);
            pattern.SetDuration(20);
            return pattern;
        }
    }

    [Serializable]
    public sealed class StockPatternLibrary
    {
        public int registeredVersion;
        public List<CustomStockPattern> patterns = new List<CustomStockPattern>();
        public static StockPatternLibrary CreateDefault()
        {
            var library = new StockPatternLibrary();
            library.UpgradeLegacy();
            return library;
        }

        public void UpgradeLegacy()
        {
            if (registeredVersion >= 2) return;
            int[] presets = { 3, 0, 1, 2 };
            string[] ids = { "builtin-rectangle", "builtin-m", "builtin-w", "builtin-flag" };
            var defaults = new List<CustomStockPattern>();
            for (int i = 0; i < presets.Length; i++)
                if (registeredVersion < 1 && !patterns.Exists(p => p.id == ids[i]))
                {
                    var pattern = CustomStockPattern.Preset("AI", presets[i]);
                    pattern.id = ids[i]; pattern.isBasePattern = true;
                    defaults.Add(pattern);
                }
            patterns.InsertRange(0, defaults);
            foreach (var pattern in patterns)
            {
                int index = Array.IndexOf(ids, pattern.id);
                if (pattern.isBasePattern && index >= 0)
                    pattern.baseShape = new[] { BasePatternShape.Rectangle, BasePatternShape.M, BasePatternShape.W, BasePatternShape.Flag }[index];
            }
            if (baseSettings == null) baseSettings = new BasePatternSettings();
            registeredVersion = 2;
        }
        public StockVolatilitySettings volatility = new StockVolatilitySettings();
        public BasePatternSettings baseSettings = new BasePatternSettings();
        public StockPatternLibrary Copy()
        {
            var copy = new StockPatternLibrary { registeredVersion = registeredVersion, volatility = (volatility ?? new StockVolatilitySettings()).Copy(),
                baseSettings = (baseSettings ?? new BasePatternSettings()).Copy() };
            foreach (var pattern in patterns) { pattern.Validate(); copy.patterns.Add(pattern.Copy()); }
            foreach (string company in InvestmentRules.CompanyIds) copy.Probabilities(company);
            return copy;
        }

        public List<CustomStockPattern> AvailablePatterns(string company)
            => patterns.FindAll(p => p.enabled && (p.isBasePattern || p.companyId == company) &&
                (BaseStockPatternGenerator.Shape(p) != BasePatternShape.Authored || p.startDay + p.TotalDays <= 30));

        public int BasePatternCount(string company) => AvailablePatterns(company).FindAll(p => p.isBasePattern).Count;

        // Explicit custom percentages reserve their share; all other candidates divide the remainder equally.
        public double[] Probabilities(string company)
        {
            var available = AvailablePatterns(company);
            double specified = 0;
            int automatic = 0;
            foreach (var pattern in available)
            {
                pattern.Validate();
                if (!pattern.isBasePattern && pattern.useCustomProbability) specified += pattern.probabilityPercent;
                else automatic++;
            }
            if (specified > 100 + .00000001) throw new ArgumentException("추가 패턴의 지정 확률 합계는 100% 이하여야 합니다.");
            var probabilities = new double[available.Count];
            if (available.Count == 0) return probabilities;
            if (automatic == 0 && Math.Abs(specified - 100) > .00000001) throw new ArgumentException("기본 패턴이 없으면 사용자 확률 합계는 100%여야 합니다.");
            double share = automatic > 0 ? Math.Max(0, 100 - specified) / automatic : 0;
            for (int i = 0; i < probabilities.Length; i++) probabilities[i] = share;
            for (int i = 0; i < available.Count; i++)
                if (!available[i].isBasePattern && available[i].useCustomProbability) probabilities[i] = available[i].probabilityPercent;
            return probabilities;
        }

        // One deterministic draw drives both built-in and authored patterns, in editor and game.
        public int PickPatternIndex(int seed, int turn, string company)
        {
            uint hash;
            unchecked { hash = (uint)seed * 2654435761U ^ (uint)turn * 2246822519U ^ (uint)(Array.IndexOf(InvestmentRules.CompanyIds, company) + 1) * 3266489917U;
                hash ^= hash >> 16; hash *= 2246822519U; hash ^= hash >> 13; }
            double draw = hash / 4294967296.0 * 100;
            var probabilities = Probabilities(company);
            if (probabilities.Length == 0) throw new InvalidOperationException(company + ": 패턴 실험실에 등록된 게임 후보가 없습니다.");
            int lastNonZero = 0;
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (probabilities[i] > 0) lastNonZero = i;
                draw -= probabilities[i];
                if (draw < 0) return i;
            }
            return lastNonZero;
        }

        public bool Apply(CompanyMarketData company, int seed, int turn, StockPatternSample sample = null)
        {
            var available = AvailablePatterns(company.companyId);
            var selected = available[PickPatternIndex(seed, turn, company.companyId)];
            company.dailyChangesBps = GenerateSelected(selected, seed, turn, company.companyId, sample);
            return true;
        }

        public int[] GenerateSelected(CustomStockPattern selected, int seed, int turn, string company, StockPatternSample sample = null,
            StockVolatilitySettings volatilityOverride = null, HiddenPatternKind? hiddenOverride = null)
        {
            selected.Validate();
            bool generated = BaseStockPatternGenerator.Shape(selected) != BasePatternShape.Authored;
            if (sample != null)
            {
                sample.PatternId = selected.id; sample.CustomPatternName = selected.name;
                sample.IsBasePattern = selected.isBasePattern;
                sample.StartDay = selected.startDay; sample.EndDay = selected.startDay + selected.TotalDays;
                sample.Outcome = StockPatternOutcome.Completed;
                int kind = Array.IndexOf(new[] { "builtin-rectangle", "builtin-m", "builtin-w", "builtin-flag" }, selected.id);
                sample.Pattern = (StockPatternKind)Math.Max(0, kind);
            }
            var settings = volatilityOverride ?? volatility ?? new StockVolatilitySettings();
            return generated ? BaseStockPatternGenerator.Generate(selected, seed, turn, company, settings, baseSettings, sample, hiddenOverride)
                : selected.GenerateTimeline(company, turn, settings, out _);
        }
    }
}
