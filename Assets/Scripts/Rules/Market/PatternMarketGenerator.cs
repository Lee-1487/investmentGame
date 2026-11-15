using System;
using System.Collections.Generic;

namespace BlackWhale.Rules
{
    // Diagnostic metadata stays in the server/editor assembly, never in Shared/network DTOs.
    public enum StockPatternKind { Rectangle, DoubleTop, DoubleBottom, Flag }
    public enum StockPatternOutcome { Completed, Damaged, Unstructured }
    public sealed class StockPatternSample
    {
        public int Seed, Turn, StartDay, EndDay;
        public string CompanyId, CustomPatternName, PatternId;
        public bool IsBasePattern;
        public StockPatternKind Pattern;
        public StockPatternOutcome Outcome;
        public int[] DailyChangesBps;
    }
    // Server only. Metadata and the room seed never belong in a player snapshot.
    // RandomMarketGenerator remains the v2 comparison generator for frozen fixtures.
    public sealed class PatternMarketGenerator
    {
        private readonly int seed;
        private readonly InvestmentRules rules;
        private readonly StockPatternLibrary library;
        private readonly Dictionary<int, MarketData> cache = new Dictionary<int, MarketData>();
        private readonly object gate = new object();
        public PatternMarketGenerator(int seed, InvestmentRules rules = null, StockPatternLibrary library = null)
        { this.seed = seed; this.rules = (rules ?? new InvestmentRules()).Copy(); this.library = (library ?? StockPatternLibrary.CreateDefault()).Copy(); }

        public MarketData ForTurn(int turn)
        {
            lock (gate)
            {
                if (!cache.TryGetValue(turn, out var data)) cache.Add(turn, data = Generate(seed, turn, rules, library));
                var companies = new CompanyMarketData[data.companies.Length];
                for (int c = 0; c < companies.Length; c++) companies[c] = new CompanyMarketData {
                    companyId = data.companies[c].companyId,
                    dailyChangesBps = (int[])data.companies[c].dailyChangesBps.Clone(), publicBundleIndices = new int[0] };
                return new MarketData { turn = turn, companies = companies };
            }
        }

        public static MarketData Generate(int seed, int turn, InvestmentRules rules = null, StockPatternLibrary library = null)
        {
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            rules = rules ?? new InvestmentRules();
            if (rules.daysPerTurn != 30) throw new ArgumentException("Pattern markets require 30 days.");
            var companies = new CompanyMarketData[InvestmentRules.CompanyIds.Length];
            for (int c = 0; c < companies.Length; c++)
            {
                string id = InvestmentRules.CompanyIds[c];
                companies[c] = new CompanyMarketData { companyId = id, publicBundleIndices = new int[0],
                    dailyChangesBps = Inspect(seed, turn, id, library: library).DailyChangesBps };
            }
            return new MarketData { turn = turn, companies = companies };
        }

        public static StockPatternSample Inspect(int seed, int turn, string companyId,
            StockPatternKind? pattern = null, StockPatternOutcome? outcome = null, StockVolatilitySettings volatility = null, StockPatternLibrary library = null)
        {
            int company = Array.IndexOf(InvestmentRules.CompanyIds, companyId);
            if (company < 0) throw new ArgumentException("Unknown company.", nameof(companyId));
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            if (pattern.HasValue && !Enum.IsDefined(typeof(StockPatternKind), pattern.Value)) throw new ArgumentOutOfRangeException(nameof(pattern));
            if (outcome.HasValue && !Enum.IsDefined(typeof(StockPatternOutcome), outcome.Value)) throw new ArgumentOutOfRangeException(nameof(outcome));
            var choices = library ?? StockPatternLibrary.CreateDefault();
            var sample = new StockPatternSample { Seed = seed, Turn = turn, CompanyId = companyId };
            if (pattern.HasValue)
            {
                // Legacy diagnostic calls can select a registered default, but cannot synthesize another shape.
                string[] ids = { "builtin-rectangle", "builtin-m", "builtin-w", "builtin-flag" };
                var selected = choices.AvailablePatterns(companyId).Find(p => p.id == ids[(int)pattern.Value]);
                if (selected == null) throw new InvalidOperationException("요청한 기본 패턴이 실험실에 등록되어 있지 않습니다.");
                sample.Pattern = pattern.Value; sample.PatternId = selected.id; sample.CustomPatternName = selected.name;
                sample.IsBasePattern = true; sample.Outcome = StockPatternOutcome.Completed;
                sample.StartDay = selected.startDay; sample.EndDay = selected.startDay + selected.TotalDays;
                sample.DailyChangesBps = selected.GenerateTimeline(companyId, turn, volatility ?? choices.volatility, out _);
            }
            else
            {
                var companyData = new CompanyMarketData { companyId = companyId };
                choices.Apply(companyData, seed, turn, sample);
                sample.DailyChangesBps = companyData.dailyChangesBps;
            }
            return sample;
        }
    }
}
