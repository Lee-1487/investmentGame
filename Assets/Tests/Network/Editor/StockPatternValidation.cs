using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using BlackWhale.UI.State;
using BlackWhale.UI.Presentation;

namespace BlackWhale.Tests
{
    public static class StockPatternValidation
    {
        private static void Check(bool value, string message) => RoomValidation.Check(value, "STOCK-v3: " + message);
        private sealed class Harness
        {
            public readonly TestClock Clock = new TestClock();
            public readonly MarketService Market = MarketService.CreateRandom(20261108);
            public readonly SharedInformationService Info;
            public readonly GameRoom Room;
            private int counter;
            public Harness(bool features = false, double seconds = 90)
            {
                Info = new SharedInformationService(Market);
                Room = new GameRoom("PATTERNS", Market, Info, Clock, fixedOrderForFixture: true,
                    settings: new GameSettings(infoSeconds: seconds, turns: 1, enableProgressionFeatures: features));
                for (int i = 1; i <= 4; i++)
                { Room.Join("c" + i, "Player " + i); Send(i, "SET_READY", new CommandPayload { ready = true }, 0); }
                Send(1, "START_GAME", turn: 0);
                if (features) Send(1, "COMPLETE_PHASE", turn: 0);
            }
            public CommandResult Send(int p, string type, CommandPayload payload = null, int turn = 1)
                => Room.ProcessCommand("c" + p, new Command { type = type, turn = turn, requestId = "test-" + (++counter), payload = payload });
            public void Public()
            {
                string[] companies = { "BANK", "AI", "SPACE", "FOOD" };
                for (int p = 1; p <= 4; p++) Check(Send(p, "REVEAL_INFORMATION_WINDOW", new CommandPayload { companyId = companies[p - 1], startDay = 1 }).success, "valid public pick");
            }
            public PlayerView View(int p) => Info.BuildPlayerView(new RequestContext("PATTERNS", 1, Protocol.Info, "P" + p), Market.GetOwnInvestmentState("P" + p));
            public CommandResult Buy(int p, string company, int start, int end = -1) => Send(p, "BUY_INFORMATION_WINDOW", new CommandPayload { companyId = company, startDay = start, endDay = end == -1 ? start + 4 : end });
            public void Feature(int p, string field, object value)
            {
                // Test setup for earned inventory/credits; no production-only testing endpoints.
                var all = (IDictionary)typeof(SharedInformationService).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Info);
                object room = all["PATTERNS"];
                var players = (IDictionary)room.GetType().GetField("Players").GetValue(room);
                object player = players["P" + p];
                if (field == "Inventory") ((List<string>)player.GetType().GetField(field).GetValue(player)).Add((string)value);
                else player.GetType().GetField(field).SetValue(player, value);
            }
        }
        public static string[] Run()
        {
            var passed = new List<string>();
            Action<string, Action> run = (name, action) => { action(); passed.Add("STOCK-v3/" + name); };
            run("pattern-reproducibility-limits-and-legal-trade-survey", Patterns);
            run("authored-pattern-timing-vertices-bounds-and-library", AuthoredPatterns);
            run("procedural-base-timing-flag-hidden-and-surge-rules", ProceduralRules);
            run("forced-base-hidden-combinations-and-reversal-directions", ForcedHiddenCombinations);
            run("food-auto-hidden-reversal-uses-configured-volatility-times-1.5", StableReversalVolatility);
            run("ai-bio-space-hidden-types-and-fixed-double-volatility", VolatileHiddenRules);
            run("fixed-daily-volatility-all-turns-and-settings-isolation", FixedVolatility);
            run("company-pattern-probabilities-and-weighted-generation", PatternProbabilities);
            run("registered-only-shared-base-and-exact-live-preview", RegisteredPatternsOnly);
            run("four-public-picks-order-overlap-private-gates-and-replay", Public);
            run("timeout-reconnect-pause-and-short-info-budget", Timeouts);
            run("window-boundaries-union-cost-replay-and-private-copy", Purchases);
            run("free-credit-no-op-and-item-proof-ledger", Items);
            run("daily-price-flexible-length-free-days-and-grants", DailyEconomy);
            run("entry-open-exit-close-and-hidden-previous-day", OpenEntry);
            run("mouse-move-resize-and-invalid-edit-isolation", RangeEditing);
            run("pattern-lab-reuses-generator-and-visible-axis-fit", PatternLabSamples);
            run("unlimited-reservations-auto-order-reconnect-and-public-trade-returns", Reservations);
            return passed.ToArray();
        }
        private static void AuthoredPatterns()
        {
            Check(Enum.GetValues(typeof(StockPatternKind)).Length == 4, "only M/W/FLAG/rectangle remain");
            foreach (string company in InvestmentRules.CompanyIds)
                for (int preset = 0; preset < 4; preset++)
                {
                    var pattern = CustomStockPattern.Preset(company, preset);
                    foreach (int requested in new[] { -10, 1, pattern.MinimumDays, 13, 30, 121, 3650, 10000 })
                    {
                        pattern.SetDuration(requested);
                        Check(pattern.TotalDays == Math.Max(pattern.MinimumDays, Math.Min(3650, requested)), "duration clamps to distinct vertex days");
                        Check(pattern.segmentDays.All(d => d >= 1), "every segment retains at least one day");
                        var raw = pattern.GenerateRaw();
                        double factor = pattern.amplitudeBps / (pattern.vertices.Max() - pattern.vertices.Min());
                        int day = 0;
                        for (int i = 1; i < pattern.vertices.Count; i++)
                        {
                            day += pattern.segmentDays[i - 1];
                            Check(Math.Abs(raw[day] - (pattern.vertices[i] - pattern.vertices[0]) * factor) < .0001, "every vertex is sampled exactly");
                        }
                        var bounded = pattern.GenerateForCompany(1, out double scale);
                        int limit = new StockVolatilitySettings().DailyLimitBps(company);
                        Check(bounded[0] == 0 && scale > 0, "baseline and uniform scale");
                        Check(Enumerable.Range(1, bounded.Length - 1).All(d => bounded[d] > -10000 && Math.Abs(bounded[d] - bounded[d - 1]) <= limit), "authored limits");
                    }
                }
            var asymmetric = CustomStockPattern.Preset("AI", 0);
            asymmetric.segmentDays = new List<int> { 1, 50, 1, 1, 50 };
            asymmetric.SetDuration(10);
            Check(asymmetric.TotalDays == 10 && asymmetric.segmentDays[1] >= 3 && asymmetric.segmentDays[4] >= 3, "asymmetric timing survives compression");
            var custom = CustomStockPattern.Preset("AI", 0); custom.id = "custom";
            var library = StockPatternLibrary.CreateDefault(); library.patterns.Add(custom);
            var originalLibrary = library.Copy();
            var frozen = new PatternMarketGenerator(42, library: library);
            var before = frozen.ForTurn(1);
            custom.vertices[1] = 999;
            CollectionAssertEquivalent(before.companies[0].dailyChangesBps, frozen.ForTurn(1).companies[0].dailyChangesBps);
            CollectionAssertEquivalent(PatternMarketGenerator.Generate(42, 2, library: originalLibrary).companies[0].dailyChangesBps,
                frozen.ForTurn(2).companies[0].dailyChangesBps);
            int selected = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var data = PatternMarketGenerator.Generate(seed, 1, library: library);
                new MarketBook(data, new InvestmentRules());
                var ai = data.companies.Single(c => c.companyId == "AI");
                Check(ai.dailyChangesBps.SequenceEqual(PatternMarketGenerator.Generate(seed, 1, library: library).companies.Single(c => c.companyId == "AI").dailyChangesBps), "custom selection is deterministic");
                var service = MarketService.CreateStockSystem(seed, library);
                service.BeginTurn(new RequestContext("CUSTOM", 1, Protocol.Info), new[] { "P1" });
                Check(service.GetWindow(1, "AI", 1, 30).SequenceEqual(ai.dailyChangesBps.Skip(1)), "stock system consumes the same authored curve");
                if (!ai.dailyChangesBps.SequenceEqual(PatternMarketGenerator.Generate(seed, 1).companies.Single(c => c.companyId == "AI").dailyChangesBps)) selected++;
                Check(data.companies.Single(c => c.companyId == "BANK").dailyChangesBps.SequenceEqual(PatternMarketGenerator.Generate(seed, 1).companies.Single(c => c.companyId == "BANK").dailyChangesBps), "library is scoped per company");
            }
            Check(selected > 0 && selected < 100, "custom and built-in patterns both occur");
            custom.SetDuration(31);
            Check(PatternMarketGenerator.Generate(42, 1, library: library).companies[0].dailyChangesBps.SequenceEqual(PatternMarketGenerator.Generate(42, 1).companies[0].dailyChangesBps), "long patterns are excluded from 30-day games");
        }
        private static void ProceduralRules()
        {
            var library = StockPatternLibrary.CreateDefault();
            var hiddenCounts = new int[5];
            var flagCounts = new HashSet<int>();
            var timings = new HashSet<string>();
            foreach (string company in InvestmentRules.CompanyIds)
                foreach (StockPatternKind kind in Enum.GetValues(typeof(StockPatternKind)))
                    for (int seed = 0; seed < 500; seed++)
                    {
                        var sample = PatternMarketGenerator.Inspect(seed, 1, company, kind, library: library);
                        var values = sample.DailyChangesBps;
                        int limit = library.volatility.DailyLimitBps(company), quiet = limit / 10;
                        Check(sample.StartDay >= 0 && sample.StartDay <= 5 && sample.EndDay - sample.StartDay >= 10 && sample.EndDay <= 25, "base has independent ten-day minimum and bounded random timing");
                        Check(values.Length == 31 && values[0] == 0, "baseline and thirty closes");
                        Check(values.SequenceEqual(PatternMarketGenerator.Inspect(seed, 1, company, kind, library: library).DailyChangesBps), "procedural preview replays seed");
                        timings.Add(sample.StartDay + ":" + sample.EndDay);
                        if (sample.Hidden == HiddenPatternKind.Lightning || sample.Hidden == HiddenPatternKind.Crash)
                        {
                            Check(sample.EndDay <= 21, "hidden prelude never overwrites main pattern");
                            Check(Enumerable.Range(22, sample.HiddenEventStartDay - 22).All(d => Math.Abs(values[d] - values[d - 1]) <= quiet), "prelude is quiet from day 22");
                            Check(sample.HiddenEventStartDay >= 26 && sample.HiddenEventStartDay + sample.HiddenEventDays <= 31, "hidden event fits days 26 through 30");
                            int sign = sample.Hidden == HiddenPatternKind.Lightning ? 1 : -1;
                            Check(Enumerable.Range(sample.HiddenEventStartDay, sample.HiddenEventDays).All(d => values[d] - values[d - 1] == sign * limit * 2), "hidden surge uses twice company volatility");
                            if (sample.Hidden == HiddenPatternKind.Lightning)
                            {
                                Check(sample.HiddenEventDays == 1, "only one lightning day");
                                int after = sample.HiddenEventStartDay + 1;
                                Check(Enumerable.Range(after, 31 - after).All(d => Math.Abs(values[d] - values[d - 1]) <= quiet), "lightning retains elevated level with only quiet movements afterward");
                                Check(values[30] > values[sample.HiddenEventStartDay - 1], "lightning never returns to its old price level");
                            }
                            else Check(sample.HiddenEventDays >= 2 && sample.HiddenEventDays <= 3, "crash spans two or three days");
                        }
                        if (sample.Hidden == HiddenPatternKind.UpDown || sample.Hidden == HiddenPatternKind.DownUp)
                        {
                            Check(Math.Sign(values[26] - values[25]) == (sample.Hidden == HiddenPatternKind.UpDown ? 1 : -1), "stable hidden name matches initial direction");
                            int changes = 0;
                            for (int day = 27; day <= 30; day++)
                                if (Math.Sign(values[day] - values[day - 1]) != Math.Sign(values[day - 1] - values[day - 2])) changes++;
                            int reversalMove = (int)Math.Round(limit * 1.5, MidpointRounding.AwayFromZero);
                            Check(changes == 1 && Enumerable.Range(26, 5).All(d => Math.Abs(values[d] - values[d - 1]) == reversalMove), "stable hidden has exactly one direction reversal at 1.5 times company volatility");
                        }
                        if (sample.Hidden == HiddenPatternKind.Decline)
                            Check(values[30] < values[26] && Enumerable.Range(26, 5).Count(d => values[d] > values[d - 1]) == 2, "decline mixes two rises and three falls but ends below day 26");
                        if (company == "BANK") Check(sample.Hidden == HiddenPatternKind.None && values.Skip(26).All(v => v == values[25]), "BANK has no hidden event");
                        for (int day = 1; day <= 25; day++)
                            if (day <= sample.StartDay || day > sample.EndDay)
                                Check(Math.Abs(values[day] - values[day - 1]) <= quiet, "outside base pattern is quiet");
                        if (kind == StockPatternKind.DoubleTop || kind == StockPatternKind.DoubleBottom)
                        {
                            Check(sample.VertexDays.Length == 5, "M/W have exactly five vertices");
                            for (int i = 1; i < 5; i++) Check(sample.VertexDays[i] - sample.VertexDays[i - 1] >= 2 && sample.VertexDays[i] - sample.VertexDays[i - 1] <= 8, "M/W segment timing is 2 to 8 days");
                        }
                        if (kind == StockPatternKind.Flag)
                        {
                            flagCounts.Add(sample.FlagPlateauDays.Length);
                            Check(sample.FlagPlateauDays.Length >= 1 && sample.FlagPlateauDays.Length <= 3, "one through three plateaus");
                            Check(sample.FlagPlateauDays.All(d => d >= 4 && d <= 8) && sample.FlagSurgeDays.All(d => d >= 1 && d <= 2), "flag allocation retains all local minima and maxima");
                            Check(sample.FlagSurgeDays.Length == sample.FlagPlateauDays.Length + 1 && sample.FlagPlateauDays.Sum() + sample.FlagSurgeDays.Sum() == sample.EndDay - sample.StartDay, "shared transitions fill the exact allotted duration");
                            int day = sample.StartDay;
                            for (int i = 0; i < sample.FlagSurgeDays.Length; i++)
                            {
                                for (int j = 0; j < sample.FlagSurgeDays[i]; j++, day++) Check(Math.Abs(values[day + 1] - values[day]) == limit * 3, "flag jumps use three times daily volatility");
                                if (i < sample.FlagPlateauDays.Length) day += sample.FlagPlateauDays[i];
                            }
                        }
                        if (kind == StockPatternKind.Rectangle) Check(sample.VertexDays.Length >= 6 && sample.VertexDays.Length <= 10, "box has six through ten vertices");
                        if (company == "AI" && kind == StockPatternKind.DoubleTop) hiddenCounts[(int)sample.Hidden]++;
                    }
            Check(timings.Count > 30 && flagCounts.Count == 3, "seeds vary timing and reach all flag counts");
            Check(hiddenCounts[(int)HiddenPatternKind.Decline] > 250 && hiddenCounts[(int)HiddenPatternKind.Lightning] > 25 && hiddenCounts[(int)HiddenPatternKind.Crash] > 100, "all weighted hidden types occur");
            var plateau = new[] { 8, 8, 4 }; var surgeDays = new[] { 2, 2, 2, 2 };
            typeof(BaseStockPatternGenerator).GetMethod("FitFlagDurations", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { 25, plateau, surgeDays });
            Check(plateau.SequenceEqual(new[] { 5, 8, 4 }) && surgeDays.All(d => d == 2), "overflow reclaims earlier plateaus before shortening final minimum or surges");
            var saved = library.Copy();
            library.baseSettings.surgeMultiplier = 4;
            var changed = PatternMarketGenerator.Inspect(15, 1, "AI", StockPatternKind.Flag, library: library);
            Check(Math.Abs(changed.DailyChangesBps[changed.StartDay + 1] - changed.DailyChangesBps[changed.StartDay]) == library.volatility.aiBps * 4, "editable multiplier changes actual flag candles");
            Check(saved.baseSettings.surgeMultiplier == 3, "library snapshots isolate multiplier settings");
        }

        private static void VolatileHiddenRules()
        {
            var library = StockPatternLibrary.CreateDefault();
            library.baseSettings.surgeMultiplier = 4.5; // Hidden stays at 2x independently of main-pattern surges.
            foreach (string company in new[] { "AI", "BIO", "SPACE" })
                foreach (int limit in new[] { 1, 501, 2000, 10000 })
                {
                    library.volatility.SetDailyLimitBps(company, limit);
                    var seen = new HashSet<HiddenPatternKind>();
                    foreach (var pattern in library.patterns)
                        for (int seed = 0; seed < 100; seed++)
                        {
                            var sample = new StockPatternSample();
                            var values = library.GenerateSelected(pattern, seed, 1, company, sample);
                            seen.Add(sample.Hidden);
                            Check(sample.Hidden == HiddenPatternKind.Decline || sample.Hidden == HiddenPatternKind.Crash || sample.Hidden == HiddenPatternKind.Lightning, "volatile companies only generate the three allowed hidden types");
                            if (sample.Hidden == HiddenPatternKind.Decline)
                            {
                                Check(Enumerable.Range(26, 5).All(d => Math.Abs(values[d] - values[d - 1]) == limit * 2), "every decline move uses twice configured volatility");
                                Check(values[30] < values[25] && values[30] < values[26], "decline ends below both hidden entry and day 26");
                            }
                            else
                                Check(Enumerable.Range(sample.HiddenEventStartDay, sample.HiddenEventDays).All(d => values[d] - values[d - 1] == (sample.Hidden == HiddenPatternKind.Lightning ? 2 : -2) * limit), "lightning and crash use fixed double volatility regardless of main-pattern multiplier");
                        }
                    Check(seen.Count == 3, "all three hidden types occur");
                }
        }

        private static void StableReversalVolatility()
        {
            var library = StockPatternLibrary.CreateDefault();
            foreach (string company in new[] { "FOOD", "AUTO" })
                foreach (int limit in new[] { 1, 501, 700, 10000 })
                    foreach (var pattern in library.patterns.Where(p => BaseStockPatternGenerator.Shape(p) != BasePatternShape.Authored))
                        foreach (var hidden in new[] { HiddenPatternKind.UpDown, HiddenPatternKind.DownUp })
                            for (int seed = 0; seed < 20; seed++)
                            {
                                var volatility = library.volatility.Copy();
                                volatility.SetDailyLimitBps(company, limit);
                                var values = library.GenerateSelected(pattern, seed, 1, company,
                                    volatilityOverride: volatility, hiddenOverride: hidden);
                                int expected = (int)Math.Round(limit * 1.5, MidpointRounding.AwayFromZero);
                                Check(Enumerable.Range(26, 5).All(d => Math.Abs(values[d] - values[d - 1]) == expected), "each hidden day follows configured volatility, including half-bps rounding");
                                Check(Math.Sign(values[26] - values[25]) == (hidden == HiddenPatternKind.UpDown ? 1 : -1), "configured reversal preserves initial direction");
                                Check(Enumerable.Range(27, 4).Count(d => Math.Sign(values[d] - values[d - 1]) != Math.Sign(values[d - 1] - values[d - 2])) == 1, "configured reversal changes direction exactly once");
                            }
        }

        private static void ForcedHiddenCombinations()
        {
            var library = StockPatternLibrary.CreateDefault();
            var kinds = new[] { HiddenPatternKind.UpDown, HiddenPatternKind.DownUp, HiddenPatternKind.Decline, HiddenPatternKind.Lightning, HiddenPatternKind.Crash };
            foreach (string company in InvestmentRules.CompanyIds)
                foreach (var pattern in library.patterns)
                    foreach (var hidden in kinds)
                        for (int seed = 0; seed < 40; seed++)
                        {
                            var sample = new StockPatternSample();
                            var values = library.GenerateSelected(pattern, seed, 1, company, sample, hiddenOverride: hidden);
                            Check(sample.Hidden == hidden, "forced hidden is honored on every company and base pattern");
                            Check(values.SequenceEqual(library.GenerateSelected(pattern, seed, 1, company, hiddenOverride: hidden)), "forced preview replays exactly");
                            Check(sample.EndDay <= (hidden == HiddenPatternKind.Lightning || hidden == HiddenPatternKind.Crash ? 21 : 25), "forced hidden reserves its prelude");
                            if (hidden == HiddenPatternKind.UpDown || hidden == HiddenPatternKind.DownUp)
                            {
                                int sign = hidden == HiddenPatternKind.UpDown ? 1 : -1;
                                Check(Math.Sign(values[26] - values[25]) == sign && Math.Sign(values[30] - values[29]) == -sign, "forced reversal starts and ends in requested directions");
                                Check(Enumerable.Range(27, 4).Count(d => Math.Sign(values[d] - values[d - 1]) != Math.Sign(values[d - 1] - values[d - 2])) == 1, "forced reversal changes direction exactly once");
                            }
                            if (hidden == HiddenPatternKind.Decline) Check(values[30] < values[26], "forced decline ends lower");
                            if (hidden == HiddenPatternKind.Lightning || hidden == HiddenPatternKind.Crash)
                            {
                                int surge = (int)(library.volatility.DailyLimitBps(company)
                                    * (company == "AI" || company == "BIO" || company == "SPACE" ? 2 : library.baseSettings.surgeMultiplier));
                                Check(Enumerable.Range(sample.HiddenEventStartDay, sample.HiddenEventDays).All(d => values[d] - values[d - 1] == (hidden == HiddenPatternKind.Lightning ? surge : -surge)), "forced event moves at configured surge magnitude");
                            }
                        }
        }
        private static void CollectionAssertEquivalent(int[] expected, int[] actual)
            => Check(expected.SequenceEqual(actual), "library snapshot is isolated");
        private static void RegisteredPatternsOnly()
        {
            var shared = CustomStockPattern.Preset("BANK", 0);
            shared.id = "shared-only"; shared.name = "공유 패턴"; shared.isBasePattern = true; shared.baseShape = BasePatternShape.Authored;
            var library = new StockPatternLibrary { registeredVersion = 1 };
            library.patterns.Add(shared);
            foreach (string company in InvestmentRules.CompanyIds)
            {
                Check(library.AvailablePatterns(company).Count == 1 && library.BasePatternCount(company) == 1, "global base is available once for every company");
                Check(library.Probabilities(company).SequenceEqual(new double[] { 100 }), "there are no implicit extra candidates");
            }
            foreach (int turn in new[] { 1, 6, 10 })
            {
                var live = MarketService.CreateStockSystem(123, library);
                live.BeginTurn(new RequestContext("EXACT", turn, Protocol.Info), new[] { "P1" });
                foreach (string company in InvestmentRules.CompanyIds)
                {
                    var expected = shared.GenerateTimeline(company, turn, library.volatility, out _);
                    var sample = PatternMarketGenerator.Inspect(123, turn, company, library: library);
                    Check(sample.PatternId == shared.id && sample.IsBasePattern, "only the registered global pattern is selected");
                    Check(sample.DailyChangesBps.SequenceEqual(expected), "market sample exactly matches the authored candle preview");
                    Check(live.GetWindow(turn, company, 1, 30).SequenceEqual(expected.Skip(1)), "actual stock game has no BANK/BIO overlay");
                    Check(expected.SequenceEqual(PatternMarketGenerator.Inspect(99999, turn, company, library: library).DailyChangesBps), "seed never changes the selected pattern geometry");
                }
            }
            var bank = shared.GenerateTimeline("BANK", 1, library.volatility, out _);
            var space = shared.GenerateTimeline("SPACE", 1, library.volatility, out _);
            Check(!bank.SequenceEqual(space), "company daily limits scale a shared shape differently");
            var user = CustomStockPattern.Preset("SPACE", 1); user.id = "space-only"; user.useCustomProbability = true; user.probabilityPercent = 100;
            library.patterns.Add(user);
            Check(PatternMarketGenerator.Inspect(123, 1, "SPACE", library: library).PatternId == user.id, "company user pattern overrides only its company's draw");
            Check(PatternMarketGenerator.Inspect(123, 1, "AI", library: library).PatternId == shared.id, "another company's user pattern is excluded");
            var empty = new StockPatternLibrary { registeredVersion = 1 };
            bool rejected = false;
            try { PatternMarketGenerator.Generate(123, 1, library: empty); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "an empty registered library never falls back to a random or hidden generator");
        }
        private static void PatternProbabilities()
        {
            var library = StockPatternLibrary.CreateDefault();
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 25, 25, 25, 25 }), "four defaults are exactly equal");
            var first = CustomStockPattern.Preset("AI", 0); first.id = "one"; first.name = "custom one";
            library.patterns.Add(first);
            Check(library.Probabilities("AI").All(p => p == 20), "one automatic addition makes five equal slots");
            first.useCustomProbability = true; first.probabilityPercent = 40;
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 15, 15, 15, 15, 40 }), "each base receives (100-p)/4");
            var second = CustomStockPattern.Preset("AI", 1); second.id = "two";
            library.patterns.Add(second);
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 12, 12, 12, 12, 40, 12 }), "automatic candidates share the unassigned remainder");
            second.useCustomProbability = true; second.probabilityPercent = 30;
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 7.5, 7.5, 7.5, 7.5, 40, 30 }), "multiple explicit custom percentages are summed");
            Check(library.Probabilities("BANK").All(p => p == 25), "probabilities are scoped per company");
            second.probabilityPercent = 70;
            bool invalid = false;
            try { library.Copy(); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "totals over 100 are rejected before persistence or room creation");
            second.enabled = false; first.probabilityPercent = 100;
            var frozen = new PatternMarketGenerator(111, library: library);
            for (int seed = 0; seed < 100; seed++)
            {
                var sample = PatternMarketGenerator.Inspect(seed, 1, "AI", library: library);
                Check(sample.CustomPatternName == first.name, "100 percent always selects the specified authored graph");
                Check(sample.DailyChangesBps.SequenceEqual(PatternMarketGenerator.Generate(seed, 1, library: library).companies[0].dailyChangesBps), "editor and generator use the same weighted draw");
            }
            var expected = first.GenerateForCompany(2, out _, library.volatility);
            first.probabilityPercent = 0;
            Check(frozen.ForTurn(2).companies[0].dailyChangesBps.Take(expected.Length).SequenceEqual(expected), "room freezes its probabilities");
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 25, 25, 25, 25, 0 }), "zero percent leaves only the defaults");
            library = StockPatternLibrary.CreateDefault();
            foreach (string company in InvestmentRules.CompanyIds)
            {
                int[] counts = new int[4];
                for (int seed = 0; seed < 20000; seed++) counts[library.PickPatternIndex(seed, 1, company)]++;
                Check(counts.All(count => count > 4500 && count < 5500), "default draw is uniform for " + company);
            }
            var extraBase = CustomStockPattern.Preset("AI", 0); extraBase.isBasePattern = true;
            // Base candidates always share the remainder, even if older data contains an override.
            extraBase.useCustomProbability = true; extraBase.probabilityPercent = 95;
            var user = CustomStockPattern.Preset("AI", 1); user.useCustomProbability = true; user.probabilityPercent = 40;
            library.patterns.Add(extraBase); library.patterns.Add(user);
            Check(library.BasePatternCount("AI") == 5, "added base patterns increase N");
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 12, 12, 12, 12, 12, 40 }), "each base receives (100 minus user sum) divided by N");
            Check(library.Copy().patterns[0].isBasePattern, "base/user classification survives a room snapshot");
            extraBase.enabled = false;
            Check(library.BasePatternCount("AI") == 4, "excluded base patterns do not affect N");
            Check(library.Probabilities("AI").SequenceEqual(new double[] { 15, 15, 15, 15, 40 }), "remaining active base candidates rebalance automatically");
        }
        private static void FixedVolatility()
        {
            var defaults = new StockVolatilitySettings();
            string[] companies = { "BANK", "FOOD", "AUTO", "AI", "BIO", "SPACE" };
            int[] expected = { 200, 500, 700, 1500, 2000, 2500 };
            for (int i = 0; i < companies.Length; i++) Check(defaults.DailyLimitBps(companies[i]) == expected[i], "requested daily limit " + companies[i]);
            var library = StockPatternLibrary.CreateDefault();
            foreach (string company in companies) library.volatility.SetDailyLimitBps(company, 25);
            library.patterns.Add(CustomStockPattern.Preset("AI", 0));
            var custom = library.patterns[library.patterns.Count - 1]; custom.startDay = 5; custom.id = "timing";
            CollectionAssertEquivalent(custom.GenerateForCompany(1, out _, library.volatility), custom.GenerateForCompany(10, out _, library.volatility));
            var original = library.Copy();
            var frozen = new PatternMarketGenerator(123, library: library);
            library.volatility.aiBps = 9999;
            CollectionAssertEquivalent(frozen.ForTurn(10).companies[0].dailyChangesBps,
                PatternMarketGenerator.Generate(123, 10, library: original).companies[0].dailyChangesBps);
            for (int seed = 0; seed < 12; seed++)
                for (int turn = 1; turn <= 10; turn++)
                {
                    var provider = new PatternMarketGenerator(seed, library: original);
                    var market = provider.ForTurn(turn);
                    foreach (var company in market.companies)
                    {
                        Check(Enumerable.Range(1, 30).All(d => Math.Abs(company.dailyChangesBps[d] - company.dailyChangesBps[d - 1]) <= 75), "base/custom limit is fixed through turn ten");
                        var single = new CompanyMarketData { companyId = company.companyId };
                        var metadata = new StockPatternSample();
                        original.Apply(single, seed, turn, metadata);
                        if (metadata.PatternId == custom.id)
                        {
                            Check(single.dailyChangesBps.Take(custom.startDay + 1).All(v => v == 0), "authored start date is preserved");
                            Check(single.dailyChangesBps[custom.startDay + custom.segmentDays[0]] == custom.GenerateForCompany(turn, out _, original.volatility)[custom.segmentDays[0]], "authored vertex dates match the game");
                        }
                    }
                    var stock = MarketService.CreateStockSystem(seed, original);
                    stock.BeginTurn(new RequestContext("VOL", turn, Protocol.Info), new[] { "P1" });
                    foreach (string company in companies)
                    {
                        var closes = new[] { 0 }.Concat(stock.GetWindow(turn, company, 1, 30)).ToArray();
                        Check(Enumerable.Range(1, 30).All(d => Math.Abs(closes[d] - closes[d - 1]) <= 75), "stock events respect the saved limit on every turn");
                    }
                }
        }
        private static void Reservations()
        {
            var h = new Harness(); h.Public(); h.Room.DrainMessages();
            var singles = new[] { 1, 3, 5, 7 }.Select(d => new Interval { entryDay = d, exitDay = d }).ToArray();
            Check(OrderInputValidator.ValidateIntervals(singles, true) == "", "single-day trades and more than two intervals are valid");
            Check(OrderInputValidator.ValidateIntervals(new[] { new Interval { entryDay = 0, exitDay = 30 } }, true) == "", "whole turn holding is valid");
            Check(OrderInputValidator.ValidateIntervals(new[] { new Interval { entryDay = 0, exitDay = 0 } }, true) == ErrorCodes.InvalidDay, "zero-day baseline is not a trade");
            var plan = new[] {
                new Investment { companyId = "AI", weightBps = 6000, intervals = singles },
                new Investment { companyId = "BANK", weightBps = 4000, intervals = new[] { new Interval { entryDay = 0, exitDay = 30 } } },
                new Investment { companyId = "BIO", intervals = new[] { new Interval { entryDay = 12, exitDay = 12 } } }
            };
            var command = new Command { type = "SAVE_TRADE_PLAN", turn = 1, requestId = "reservation", payload = new CommandPayload { investments = plan } };
            Check(h.Room.ProcessCommand("c1", command).success && h.Room.ProcessCommand("c1", command).success, "reservation save and replay");
            plan[0].intervals[0].exitDay = 29;
            Check(h.Market.GetReservations("P1")[0].intervals[0].exitDay == 1, "server reservation copy is isolated");
            Check(!h.Market.IsOrderLocked("P1") && h.Market.GetReservations("P2").Length == 0, "saving neither locks nor shares a plan");
            var invalid = h.Market.GetReservations("P1"); invalid[0].intervals[0].exitDay = 4;
            Check(h.Send(1, "SAVE_TRADE_PLAN", new CommandPayload { investments = invalid }).errorCode == ErrorCodes.OverlappingIntervals,
                "overlap is rejected atomically");
            h.Send(1, "GET_PLAYER_VIEW"); h.Send(2, "GET_PLAYER_VIEW");
            var privateViews = h.Room.DrainMessages();
            Check(privateViews.Single(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Message.playerView.reservedInvestments.Length == 3 &&
                privateViews.Single(d => d.PlayerId == "P2" && d.Message.type == "PLAYER_VIEW").Message.playerView.reservedInvestments.Length == 0, "no cross-player reservation leak");
            string[] choices = { "AI", "BANK", "SPACE", "FOOD", "BIO", "AUTO", "FOOD", "BANK" };
            int[] pickers = { 1, 2, 3, 4, 4, 3, 2, 1 };
            var remote = new Harness(); remote.Public();
            string token = remote.Room.DrainMessages().First(d => d.PlayerId == "P2" && d.Message.type == "WELCOME").Message.session.reconnectToken;
            Check(remote.Send(2, "SAVE_TRADE_PLAN", new CommandPayload { investments = h.Market.GetReservations("P1") }).success, "remote reservation");
            remote.Room.DrainMessages(); remote.Room.Disconnect("c2");
            Check(remote.Room.Join("c2-return", "ignored", token) == "", "remote reconnect succeeds");
            Check(remote.Room.DrainMessages().Single(d => d.PlayerId == "P2" && d.Message.type == "PLAYER_VIEW").Message.playerView.reservedInvestments[0].intervals.Length == 4, "reconnect restores own reservations");
            // A fresh connected room exercises the complete timeout -> reveal -> settlement path.
            var live = new Harness(); live.Public();
            Check(live.Send(1, "SAVE_TRADE_PLAN", new CommandPayload { investments = h.Market.GetReservations("P1") }).success, "save live plan");
            for (int p = 1; p <= 4; p++) Check(live.Send(p, "COMPLETE_PHASE").success, "finish info");
            for (int n = 0; n < choices.Length; n++) Check(live.Send(pickers[n], "SELECT_COMPANY", new CommandPayload { companyId = choices[n] }).success, "company pick");
            bool guarded = false;
            try { live.Market.GetRevealedMarket(new RequestContext("PATTERNS", 1, Protocol.Info)); } catch (InvalidOperationException) { guarded = true; }
            Check(guarded, "other players' orders and market remain unavailable before lock");
            live.Room.DrainMessages();
            live.Clock.Seconds = live.Room.Snapshot().phaseEndsAtServerSeconds; live.Room.Tick();
            Check(live.Room.Snapshot().phase == Protocol.Reveal, "deadline reveals without clicking submit");
            var own = live.Market.GetOwnInvestmentState("P1").Investments;
            Check(own[0].companyId == "AI" && own[0].weightBps == 6000 && own[0].intervals.Length == 4 && own[1].intervals[0].exitDay == 30,
                "automatic order preserves chosen firms, ratio and every reserved interval");
            Check(own.All(i => i.companyId != "BIO"), "unselected company's reservation is not traded");
            var deliveries = live.Room.DrainMessages();
            Check(deliveries.Where(d => d.Message.type == "MARKET_REVEAL").Count() == 4, "all four receive public trades");
            var reveal = deliveries.First(d => d.Message.type == "MARKET_REVEAL").Message.revealedMarket;
            var ai = reveal.Single(c => c.companyId == "AI"); var p1 = ai.investments.Single(p => p.playerId == "P1");
            Check(p1.intervals.All(v => v.returnBps == ai.dailyChangesBps[v.exitDay] - ai.dailyChangesBps[Math.Max(0, v.entryDay - 1)]), "public interval return uses entry open and exit close");
            var copy = DtoCopy.Copy(ai); copy.investments[0].intervals[0].returnBps++;
            Check(copy.investments[0].intervals[0].returnBps != ai.investments[0].intervals[0].returnBps, "public trade DTO copy isolation");
            live.Clock.Seconds = live.Room.Snapshot().phaseEndsAtServerSeconds; live.Room.Tick();
            Check(live.Market.GetSettlementDetail(1).Single(p => p.playerId == "P1").companyProfits.Single(c => c.companyId == "AI").returnBps == p1.returnBps,
                "revealed company total exactly matches settlement");
            Check(live.Send(1, "SAVE_TRADE_PLAN", new CommandPayload { investments = own }).errorCode == ErrorCodes.WrongPhase, "cannot change reservations after reveal");
        }
        private static void DailyEconomy()
        {
            var h = new Harness(); h.Public();
            Check(h.Buy(1, "BIO", 30, 30).success && h.View(1).coins == 14, "one day costs one coin");
            Check(h.Buy(1, "BIO", 3, 9).success && h.View(1).coins == 7, "arbitrary seven-day range costs seven coins");
            Check(h.Buy(1, "BIO", 8, 12).success && h.View(1).coins == 4, "only new overlapping days cost coins");
            Check(h.Buy(1, "BIO", 13, 29).errorCode == "NOT_ENOUGH_COINS" && h.View(1).coins == 4, "unaffordable range is atomic");
            Check(h.Buy(1, "BIO", 7, 6).errorCode == "INVALID_WINDOW" && h.Buy(1, "BIO", 30, 31).errorCode == "INVALID_WINDOW", "range boundaries");
            var grant = new Harness(); string[] players = { "P1", "P2", "P3", "P4" };
            for (int turn = 1; turn <= 10; turn++)
            {
                var c = new RequestContext("PATTERNS", turn, Protocol.Info);
                grant.Market.BeginTurn(c, players); grant.Info.GrantTurnCoins(c, players); grant.Info.GrantTurnCoins(c, players);
                Check(grant.Info.GetCoins("PATTERNS", "P1") == (turn <= 5 ? turn * 15 : 75 + (turn - 5) * 25), "15/25 idempotent daily grants");
            }
            var free = new Harness(features: true); free.Public(); free.Feature(1, "FreeCredits", 2);
            Check(free.Buy(1, "BIO", 1, 5).success && free.View(1).coins == 12, "free days cover only two of five new days");
        }
        private static void OpenEntry()
        {
            var h = new Harness(); h.Public(); Check(h.Buy(1, "BIO", 20, 20).success, "buy isolated day");
            var company = h.View(1).companies.Single(c => c.companyId == "BIO");
            var days = h.Market.GetWindow(1, "BIO", 19, 7);
            Check(!InformationWindowRules.KnownDays(company)[19].HasValue && InformationWindowRules.KnownOpens(company)[20] == days[0], "only bought day exposes its open");
            var data = PatternMarketGenerator.Generate(20261108, 1); var rules = new InvestmentRules { entryAtOpen = true };
            var investment = new Investment { companyId = "BIO", intervals = new[] { new Interval { entryDay = 20, exitDay = 25 } } };
            Check(Settlement.CompanyReturnBps(new MarketBook(data, rules), investment, rules) == days[6] - days[0], "exit close minus entry open");
            Check(h.View(1).entryAtOpen && h.View(2).companies.Single(c => c.companyId == "BIO").visibleWindows.Length == 0, "private opens do not leak to other players");
            Check(OrderInputValidator.ValidateIntervals(new[] { new Interval { entryDay = 3, exitDay = 7 } }, true) == "", "entry and exit dates count as five holding days");
            var touching = new[] { new Interval { entryDay = 1, exitDay = 5 }, new Interval { entryDay = 5, exitDay = 9 } };
            Check(OrderInputValidator.ValidateIntervals(touching, true) == ErrorCodes.OverlappingIntervals, "same-day exit close and re-entry open overlap");
        }
        private static void RangeEditing()
        {
            var book = new TradePlanBook(); book.BeginTurn(1); book.TryAddInterval("AI", 3, 9);
            var range = new ChartRangeSelection(); range.Begin(6, book.Get("AI").Intervals); range.Finish(8);
            Check(range.Start == 5 && range.End == 11 && book.TryReplaceInterval("AI", 0, range.Start, range.End) == "", "dragging body moves both ends");
            range.Begin(11, book.Get("AI").Intervals); range.Finish(13);
            Check(book.TryReplaceInterval("AI", 0, range.Start, range.End) == "" && book.Get("AI").Intervals[0].exitDay == 13, "dragging endpoint resizes");
            range.Begin(13, book.Get("AI").Intervals); range.Finish(30);
            Check(book.TryReplaceInterval("AI", 0, range.Start, range.End) != "" && book.Get("AI").Intervals[0].exitDay == 13, "invalid edit cannot mutate reservation");
        }
        private static void PatternLabSamples()
        {
            foreach (int seed in new[] { 0, 42, -9, 20261108 })
                foreach (int turn in new[] { 1, 5, 10 })
                {
                    var market = PatternMarketGenerator.Generate(seed, turn);
                    foreach (var company in market.companies)
                    {
                        var normal = PatternMarketGenerator.Inspect(seed, turn, company.companyId);
                        Check(normal.DailyChangesBps.SequenceEqual(company.dailyChangesBps), "lab normal mode matches the live generator exactly");
                        foreach (StockPatternKind pattern in Enum.GetValues(typeof(StockPatternKind)))
                            foreach (StockPatternOutcome outcome in Enum.GetValues(typeof(StockPatternOutcome)))
                            {
                                var sample = PatternMarketGenerator.Inspect(seed, turn, company.companyId, pattern, outcome);
                                Check(sample.Pattern == pattern && sample.Outcome == StockPatternOutcome.Completed && sample.StartDay >= 0 && sample.EndDay <= 30, "forced diagnostic metadata");
                                int limit = new StockVolatilitySettings().DailyLimitBps(company.companyId), cap = RandomMarketGenerator.AbsoluteCapBps[company.companyId];
                                Check(Enumerable.Range(1, 30).All(d => Math.Abs(sample.DailyChangesBps[d] - sample.DailyChangesBps[d - 1]) <= limit * 3), "lab constraints match normal play");
                            }
                    }
                }
            var flat = ChartVerticalRange.FromVisibleValues(new[] { 0, 0 });
            Check(flat.Span > 0 && flat.Fraction(0) == .5, "flat/unopened chart has a valid fallback");
            var fit = ChartVerticalRange.FromVisibleValues(new[] { 0, 30, -20 });
            Check(fit.MinBps < -20 && fit.MaxBps > 30 && fit.Span < 100, "small movements fill the chart with padding");
            var positive = ChartVerticalRange.FromVisibleValues(new[] { 0, 1000 });
            Check(positive.Fraction(0) < .2 && positive.Fraction(1000) > .8, "unused negative half no longer flattens a positive pattern");
            // 세로축 % 눈금: 0% 포함, 깔끔한 간격, 범위 안, 개수 제한
            foreach (var range in new[] { flat, fit, positive, ChartVerticalRange.FromVisibleValues(new[] { 0, -3700, 2200 }), ChartVerticalRange.FromVisibleValues(new[] { 0, 15000 }) })
            {
                int[] ticks = range.AxisTicks(5);
                Check(ticks.Length >= 2 && ticks.Length <= 5, "axis tick count stays readable: " + ticks.Length);
                Check(ticks.Contains(0), "axis ticks always include the 0% day-zero baseline");
                Check(ticks.All(t => t >= range.MinBps && t <= range.MaxBps), "axis ticks stay inside the visible range");
                Check(ticks.Zip(ticks.Skip(1), (a, b) => b - a).Distinct().Count() == 1, "axis ticks are evenly spaced");
            }
            Check(positive.AxisTicks(5).SequenceEqual(new[] { 0, 500, 1000 }), "0~10% chart shows 0%, +5%, +10%: " + string.Join(",", positive.AxisTicks(5)));
        }
        private static void Patterns()
        {
            var generator = new PatternMarketGenerator(42);
            int profitable = 0, lossOnly = 0;
            for (int seed = 0; seed < 200; seed++)
                for (int turn = 1; turn <= 10; turn += 3)
                {
                    var data = PatternMarketGenerator.Generate(seed, turn);
                    new MarketBook(data, new InvestmentRules());
                    var repeat = PatternMarketGenerator.Generate(seed, turn);
                    foreach (var c in data.companies)
                    {
                        Check(c.publicBundleIndices.Length == 0 && c.dailyChangesBps[0] == 0, "no preselected public dates");
                        Check(c.dailyChangesBps.SequenceEqual(repeat.companies.Single(r => r.companyId == c.companyId).dailyChangesBps), "deterministic path");
                        int limit = new StockVolatilitySettings().DailyLimitBps(c.companyId), cap = RandomMarketGenerator.AbsoluteCapBps[c.companyId];
                        Check(Enumerable.Range(1, 30).All(d => Math.Abs(c.dailyChangesBps[d] - c.dailyChangesBps[d - 1]) <= limit * 3), "daily and absolute constraints");
                        int best = int.MinValue;
                        for (int a = 0; a <= 25; a++) for (int b = a + 5; b <= Math.Min(a + 10, 30); b++)
                            best = Math.Max(best, c.dailyChangesBps[b] - c.dailyChangesBps[a]);
                        if (best > 0) profitable++; else lossOnly++;
                    }
                }
            Check(profitable > 0, "registered templates retain their authored trading opportunities without random paths");
            var copy = generator.ForTurn(3); copy.companies[0].dailyChangesBps[10] = int.MaxValue;
            Check(generator.ForTurn(3).companies[0].dailyChangesBps[10] != int.MaxValue, "cache isolated");
            Check(generator.ForTurn(3).companies.Any(c => !c.dailyChangesBps.SequenceEqual(generator.ForTurn(4).companies.Single(n => n.companyId == c.companyId).dailyChangesBps)), "a turn selects candidates independently");
        }
        private static void Public()
        {
            var h = new Harness(); var first = h.Room.Snapshot();
            Check(first.publicSelectionActive && first.publicSelectionPlayerId == "P1" && first.phaseEndsAtServerSeconds == 90 && first.publicSelectionDeadlineAtServerSeconds == 10, "INFO includes ten-second pick");
            Check(h.View(1).companies.All(c => InformationWindowRules.KnownDays(c).Count(v => v.HasValue) == 1), "only day zero initially");
            Check(h.Send(1, "COMPLETE_PHASE").errorCode == "PUBLIC_SELECTION_PENDING" && !h.Buy(1, "AI", 1).success, "cannot skip public picks");
            Check(h.Send(2, "REVEAL_INFORMATION_WINDOW", new CommandPayload { companyId = "AI", startDay = 1 }).errorCode == "NOT_YOUR_TURN", "seat order");
            var request = new Command { type = "REVEAL_INFORMATION_WINDOW", requestId = "public-replay", turn = 1, payload = new CommandPayload { companyId = "AI", startDay = 3 } };
            Check(h.Room.ProcessCommand("c1", request).success && h.Room.ProcessCommand("c1", request).success && h.Room.Snapshot().publicSelectionIndex == 1, "replay publishes once");
            Check(h.Send(2, request.type, new CommandPayload { companyId = "AI", startDay = 7 }).errorCode == "PUBLIC_WINDOW_OVERLAP", "overlap rejected");
            for (int p = 2; p <= 4; p++) Check(h.Send(p, request.type, new CommandPayload { companyId = "AI", startDay = 3 + 5 * (p - 1) }).success, "same company allowed");
            Check(!h.Room.Snapshot().publicSelectionActive && h.Room.Snapshot().publicInformation.Length == 4, "four choices completed");
            for (int p = 1; p <= 4; p++) Check(h.View(p).companies.Sum(c => InformationWindowRules.KnownDays(c).Skip(1).Count(v => v.HasValue)) == 20 && h.Info.GetCoins("PATTERNS", "P" + p) == 15, "twenty identical free dates");
        }
        private static void Timeouts()
        {
            var h = new Harness(); var welcomes = h.Room.DrainMessages().Where(d => d.Message.type == "WELCOME").ToArray();
            h.Clock.Seconds = 9.999; h.Room.Tick();
            Check(h.Room.Snapshot().publicSelectionIndex == 0, "public turn stays available for ten seconds");
            h.Clock.Seconds = 10; h.Room.Tick();
            Check(h.Room.Snapshot().publicSelectionIndex == 1 && h.Room.Snapshot().publicInformation[0].automatic, "exact timeout auto pick");
            h.Room.Disconnect("c2"); h.Clock.Seconds = 30; h.Room.Tick();
            Check(h.Room.Snapshot().publicInformation.Length == 4 && h.Room.Snapshot().phaseEndsAtServerSeconds == 90, "timeout and disconnect preserve INFO budget");
            Check(h.Room.Join("return", "Player 2", welcomes.Single(d => d.PlayerId == "P2").Message.session.reconnectToken) == "", "reconnect allowed");
            Check(h.Room.DrainMessages().Any(d => d.PlayerId == "P2" && d.Message.playerView?.companies.Sum(c => InformationWindowRules.KnownDays(c).Skip(1).Count(v => v.HasValue)) == 20), "reconnect restores public mask");
            var full = new Harness(); full.Clock.Seconds = 39.999; full.Room.Tick();
            Check(full.Room.Snapshot().publicSelectionIndex == 3 && full.Room.Snapshot().publicSelectionActive, "four ten-second public choices fit inside INFO");
            full.Clock.Seconds = 40; full.Room.Tick();
            Check(!full.Room.Snapshot().publicSelectionActive && full.Room.Snapshot().phaseEndsAtServerSeconds == 90, "fifty seconds remain for personal exploration");
            var shortRoom = new Harness(seconds: 5); shortRoom.Clock.Seconds = 4; shortRoom.Room.Tick();
            Check(!shortRoom.Room.Snapshot().publicSelectionActive && shortRoom.Room.Snapshot().phase == Protocol.Info, "short configuration reserves personal time");
        }
        private static void Purchases()
        {
            var h = new Harness(); h.Public();
            Check(h.Buy(1, "BIO", 26).success && h.View(1).coins == 10, "26 through 30 costs five coins");
            Check(h.Buy(1, "BIO", 27).errorCode == "INVALID_WINDOW" && h.Buy(1, "BIO", 0).errorCode == "INVALID_WINDOW", "invalid endpoints");
            var request = new Command { type = "BUY_INFORMATION_WINDOW", requestId = "private-replay", turn = 1, payload = new CommandPayload { companyId = "BIO", startDay = 24, endDay = 28 } };
            Check(h.Room.ProcessCommand("c1", request).success && h.Room.ProcessCommand("c1", request).success && h.View(1).coins == 8, "partial overlap charges two new days once");
            request.payload.startDay = 23; Check(h.Room.ProcessCommand("c1", request).errorCode == "REQUEST_ID_CONFLICT", "start date belongs to fingerprint");
            Check(h.Buy(1, "BIO", 25).errorCode == "ALREADY_VISIBLE" && h.View(1).coins == 8, "union rejects entirely known window");
            Check(InformationWindowRules.KnownDays(h.View(1).companies.Single(c => c.companyId == "BIO")).Skip(1).Count(d => d.HasValue) == 7, "daily mask union");
            Check(h.View(2).companies.Single(c => c.companyId == "BIO").visibleWindows.Length == 0, "personal purchases stay private");
            var privateCopy = DtoCopy.Copy(h.View(1)); privateCopy.companies.Single(c => c.companyId == "BIO").visibleWindows[0].dailyChangesBps[0] = int.MaxValue;
            Check(h.View(1).companies.Single(c => c.companyId == "BIO").visibleWindows[0].dailyChangesBps[0] != int.MaxValue, "deep copied windows");
            Check(h.Market.GetWindow(1, "BIO", 26).SequenceEqual(PatternMarketGenerator.Generate(20261108, 1).companies.Single(c => c.companyId == "BIO").dailyChangesBps.Skip(26)), "purchases never modify market");
        }
        private static void Items()
        {
            var h = new Harness(features: true); h.Public();
            for (int p = 1; p <= 4; p++) h.Feature(p, "FreeCredits", 0);
            h.Feature(1, "FreeCredits", 5);
            Check(h.Buy(1, "AI", 1).errorCode == "ALREADY_VISIBLE", "fully public free purchase rejected");
            var view = new PlayerView(); h.Info.PopulateView(new RequestContext("PATTERNS", 1, Protocol.Info, "P1"), view);
            Check(view.ability.freeInformationCredits == 5, "no-op preserves credit");
            Check(h.Buy(1, "BIO", 26).success && h.View(1).coins == 15, "five free day credits consumed on new information only");
            Check(h.Buy(1, "AI", 3).success && h.Buy(1, "AI", 6).success, "overlapping paid proofs");
            h.Feature(2, "Inventory", "STEAL");
            Check(h.Send(2, "USE_INFORMATION_ITEM", new CommandPayload { itemId = "STEAL", targetPlayerId = "P1" }).success, "copy paid record");
            h.Feature(3, "Inventory", "DISTORT");
            Check(h.Send(3, "USE_INFORMATION_ITEM", new CommandPayload { itemId = "DISTORT", targetPlayerId = "P1" }).success, "hide record with exclusive dates");
            var known = InformationWindowRules.KnownDays(h.View(1).companies.Single(c => c.companyId == "AI"));
            Check(known[1].HasValue && known[5].HasValue && known[6].HasValue && known[7].HasValue && !known[8].HasValue, "public and overlapping private proof survives distortion");
            h.Clock.Seconds = 10; h.Room.Tick();
            Check(InformationWindowRules.KnownDays(h.View(1).companies.Single(c => c.companyId == "AI"))[10].HasValue, "distortion expires on server clock");
            h.Feature(4, "Inventory", "STEAL");
            Check(h.Send(4, "USE_INFORMATION_ITEM", new CommandPayload { itemId = "STEAL", targetPlayerId = "P2" }).errorCode == "NO_INFORMATION_TARGET", "stolen record cannot be stolen again");
            h.Feature(4, "Inventory", "BLACKBOX"); h.Feature(4, "Ability", "INFORMATION_BROKER");
            Check(h.Send(4, "USE_INFORMATION_ITEM", new CommandPayload { itemId = "BLACKBOX", companyId = "BIO", startDay = 1 }).success, "blackbox uses chosen window");
            Check(h.View(4).companies.Single(c => c.companyId == "BIO").visibleWindows.Count(w => w.source == "ITEM") == 2, "broker bonus uses remaining unseen dates");
        }
    }
}
