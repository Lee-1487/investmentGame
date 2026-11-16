using System.Collections;
using System.Linq;
using System.Reflection;
using BlackWhale.Rules;
using BlackWhale.Shared;
using BlackWhale.UI.EditorTools;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using System.Collections.Generic;

namespace BlackWhale.UI.Tests
{
    public sealed class PatternLabEditorTests
    {
        [TestCase(-20, 1)]
        [TestCase(9123, 7)]
        public void MultipleSeedsComparisonKeepsPatternHiddenAndCompanyFixed(int firstSeed, int step)
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(firstSeed, 4);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var library = StockPatternLibrary.CreateDefault();
                type.GetField("authoringLibrary", flags).SetValue(window, library);
                type.GetField("authorCompany", flags).SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, "AI"));
                type.GetField("comparisonMode", flags).SetValue(window, 3);
                type.GetField("combinationPattern", flags).SetValue(window, 2);
                type.GetField("combinationHidden", flags).SetValue(window, 4); // Lightning.
                type.GetField("comparisonSeedCount", flags).SetValue(window, 6);
                type.GetField("comparisonSeedStep", flags).SetValue(window, step);
                string before = JsonUtility.ToJson(library);
                var generate = type.GetMethod("GenerateComparisonSamples", flags);
                var comparison = (List<StockPatternSample>)generate.Invoke(window, null);
                Assert.AreEqual(6, comparison.Count);
                CollectionAssert.AreEqual(Enumerable.Range(0, 6).Select(i => firstSeed + i * step), comparison.Select(s => s.Seed));
                Assert.AreEqual(1, comparison.Select(s => s.PatternId).Distinct().Count());
                foreach (var sample in comparison)
                {
                    Assert.AreEqual("AI", sample.CompanyId);
                    Assert.AreEqual(4, sample.Turn);
                    Assert.AreEqual(HiddenPatternKind.Lightning, sample.Hidden);
                    var pattern = library.patterns.Single(p => p.id == sample.PatternId);
                    CollectionAssert.AreEqual(library.GenerateSelected(pattern, sample.Seed, 4, "AI", hiddenOverride: HiddenPatternKind.Lightning), sample.DailyChangesBps);
                }
                Assert.IsTrue(comparison.Skip(1).Any(s => !s.DailyChangesBps.SequenceEqual(comparison[0].DailyChangesBps)), "different seeds produce variations");
                var replay = (List<StockPatternSample>)generate.Invoke(window, null);
                for (int i = 0; i < comparison.Count; i++) CollectionAssert.AreEqual(comparison[i].DailyChangesBps, replay[i].DailyChangesBps);
                Assert.AreEqual(firstSeed, type.GetField("seed", flags).GetValue(window), "comparison does not mutate the starting seed");
                Assert.AreEqual(before, JsonUtility.ToJson(library));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MultipleSeedsComparisonBoundsCountAndRejectsOverflowWithoutWrappingSeeds()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("authoringLibrary", flags).SetValue(window, StockPatternLibrary.CreateDefault());
                type.GetField("comparisonMode", flags).SetValue(window, 3);
                type.GetField("comparisonSeedCount", flags).SetValue(window, 1000);
                type.GetField("comparisonSeedStep", flags).SetValue(window, 0);
                window.Configure(0, 1);
                var generate = type.GetMethod("GenerateComparisonSamples", flags);
                var comparison = (List<StockPatternSample>)generate.Invoke(window, null);
                Assert.AreEqual(24, comparison.Count);
                Assert.AreEqual(23, comparison.Last().Seed);
                window.Configure(int.MaxValue - 22, 1);
                Assert.AreEqual(0, ((List<StockPatternSample>)generate.Invoke(window, null)).Count);
                window.Configure(int.MaxValue - 23, 1);
                Assert.AreEqual(int.MaxValue, ((List<StockPatternSample>)generate.Invoke(window, null)).Last().Seed);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void SameSeedComparisonCoversEveryBaseHiddenPairAndMatchesIndividualPreviews()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(9123, 4);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var library = StockPatternLibrary.CreateDefault();
                type.GetField("authoringLibrary", flags).SetValue(window, library);
                type.GetField("authorCompany", flags).SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, "AI"));
                type.GetField("comparisonMode", flags).SetValue(window, 2);
                string before = JsonUtility.ToJson(library);
                var generate = type.GetMethod("GenerateComparisonSamples", flags);
                var comparison = (List<StockPatternSample>)generate.Invoke(window, null);
                Assert.AreEqual(12, comparison.Count);
                Assert.AreEqual(12, comparison.Select(s => s.PatternId + ":" + s.Hidden).Distinct().Count());
                foreach (var sample in comparison)
                {
                    Assert.AreEqual("AI", sample.CompanyId);
                    Assert.AreEqual(9123, sample.Seed);
                    Assert.AreEqual(4, sample.Turn);
                    var pattern = library.patterns.Single(p => p.id == sample.PatternId);
                    CollectionAssert.AreEqual(library.GenerateSelected(pattern, 9123, 4, "AI", hiddenOverride: sample.Hidden), sample.DailyChangesBps);
                }
                type.GetMethod("FitComparisonRange", flags).Invoke(window, new object[] { comparison });
                var range = (ChartVerticalRange)type.GetMethod("MarketRange", flags).Invoke(window, null);
                foreach (int value in comparison.SelectMany(s => s.DailyChangesBps))
                { Assert.Greater(range.MaxBps, value); Assert.Less(range.MinBps, value); }
                var replay = (List<StockPatternSample>)generate.Invoke(window, null);
                for (int i = 0; i < comparison.Count; i++) CollectionAssert.AreEqual(comparison[i].DailyChangesBps, replay[i].DailyChangesBps);
                Assert.AreEqual(before, JsonUtility.ToJson(library));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void SameSeedComparisonCanHoldHiddenOrBaseFixedAndHandlesEmptyLibrary()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var library = StockPatternLibrary.CreateDefault();
                type.GetField("authoringLibrary", flags).SetValue(window, library);
                type.GetField("authorCompany", flags).SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, "FOOD"));
                type.GetField("comparisonMode", flags).SetValue(window, 0);
                type.GetField("combinationHidden", flags).SetValue(window, 4); // Invalid Lightning resets to company rules.
                var generate = type.GetMethod("GenerateComparisonSamples", flags);
                var comparison = (List<StockPatternSample>)generate.Invoke(window, null);
                Assert.AreEqual(4, comparison.Count);
                Assert.IsTrue(comparison.All(s => (s.Hidden == HiddenPatternKind.UpDown || s.Hidden == HiddenPatternKind.DownUp) && s.CompanyId == "FOOD"));
                Assert.AreEqual(0, type.GetField("combinationHidden", flags).GetValue(window));
                type.GetField("comparisonMode", flags).SetValue(window, 1);
                type.GetField("combinationPattern", flags).SetValue(window, 2);
                comparison = (List<StockPatternSample>)generate.Invoke(window, null);
                Assert.AreEqual(2, comparison.Count);
                Assert.AreEqual(1, comparison.Select(s => s.PatternId).Distinct().Count());
                Assert.AreEqual(2, comparison.Select(s => s.Hidden).Distinct().Count());
                library.patterns.Clear();
                Assert.AreEqual(0, ((List<StockPatternSample>)generate.Invoke(window, null)).Count);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [TestCase("AI")]
        [TestCase("BIO")]
        [TestCase("SPACE")]
        [TestCase("FOOD")]
        [TestCase("AUTO")]
        [TestCase("BANK")]
        public void EveryComparisonModeAndPreviewUseCompanyHiddenChoices(string company)
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var library = StockPatternLibrary.CreateDefault();
                type.GetField("authoringLibrary", flags).SetValue(window, library);
                var companyField = type.GetField("authorCompany", flags);
                companyField.SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, company));
                var allowed = company == "BANK" ? new[] { HiddenPatternKind.None }
                    : company == "FOOD" || company == "AUTO" ? new[] { HiddenPatternKind.UpDown, HiddenPatternKind.DownUp }
                    : new[] { HiddenPatternKind.Decline, HiddenPatternKind.Lightning, HiddenPatternKind.Crash };
                var choices = (HiddenPatternKind?[])type.GetField("HiddenChoices", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var indices = (int[])type.GetProperty("CompanyHiddenIndices", flags).GetValue(window);
                CollectionAssert.AreEquivalent(allowed, indices.Skip(1).Select(i => choices[i].Value), "dropdown offers only company-specific types plus automatic");
                var selection = type.GetField("combinationHidden", flags);
                var generate = type.GetMethod("GenerateComparisonSamples", flags);
                for (int mode = 0; mode < 4; mode++)
                {
                    type.GetField("comparisonMode", flags).SetValue(window, mode);
                    for (int hidden = 0; hidden < choices.Length; hidden++)
                    {
                        selection.SetValue(window, hidden); // Includes a stale choice from another company.
                        var samples = (List<StockPatternSample>)generate.Invoke(window, null);
                        int expected = mode == 0 ? 4 : mode == 1 ? allowed.Length : mode == 2 ? 4 * allowed.Length : 6;
                        Assert.AreEqual(expected, samples.Count);
                        Assert.IsTrue(samples.All(s => s.CompanyId == company && allowed.Contains(s.Hidden)));
                        Assert.IsTrue(indices.Contains((int)selection.GetValue(window)), "switching companies discards incompatible saved selections");
                        if (mode == 1 || mode == 2) CollectionAssert.AreEquivalent(allowed, samples.Select(s => s.Hidden).Distinct());
                    }
                }
                foreach (var hidden in choices)
                {
                    var preview = (StockPatternSample)type.GetMethod("GenerateCombinationPreview", flags).Invoke(window, new object[] { library.patterns[0], hidden });
                    Assert.Contains(preview.Hidden, allowed, "individual previews also reject incompatible hidden types");
                }
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void FogTargetSelectsOneCompanyOrOneDayPerCompanyAndSkipsExhaustedCompanies()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("SetMarketFog", flags).Invoke(window, new object[] { true });
                type.GetField("randomRevealStartDay", flags).SetValue(window, 24);
                type.GetField("randomRevealEndDay", flags).SetValue(window, 25);
                var target = type.GetField("randomRevealCompany", flags);
                var reveal = type.GetMethod("RevealRandomMarketFog", flags);
                var revealed = (HashSet<string>)type.GetField("revealedMarketDays", flags).GetValue(window);
                target.SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, "BIO") + 1);
                Assert.AreEqual(true, reveal.Invoke(window, null));
                Assert.AreEqual(true, reveal.Invoke(window, null));
                Assert.AreEqual(false, reveal.Invoke(window, null));
                CollectionAssert.AreEquivalent(new[] { "BIO:24", "BIO:25" }, revealed);
                target.SetValue(window, InvestmentRules.CompanyIds.Length + 1);
                Assert.AreEqual(true, reveal.Invoke(window, null));
                Assert.AreEqual(7, revealed.Count, "exhausted BIO is skipped while each other company reveals one day");
                foreach (string company in InvestmentRules.CompanyIds.Where(c => c != "BIO"))
                    Assert.AreEqual(1, revealed.Count(key => key.StartsWith(company + ":")));
                Assert.AreEqual(true, reveal.Invoke(window, null));
                Assert.AreEqual(12, revealed.Count);
                Assert.AreEqual(false, reveal.Invoke(window, null));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MarketVerticalDragMovesCenterWithoutChangingSpanOrRevealingFog()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("SetMarketFog", flags).Invoke(window, new object[] { true });
                var range = type.GetMethod("MarketRange", flags);
                var before = (ChartVerticalRange)range.Invoke(window, null);
                var input = type.GetMethod("HandleMarketPlotInput", flags);
                var rect = new Rect(0, 0, 310, 100);
                input.Invoke(window, new object[] { rect, "BANK", new Event { type = EventType.MouseDown, button = 2, mousePosition = new Vector2(100, 50) } });
                input.Invoke(window, new object[] { rect, "BANK", new Event { type = EventType.MouseDrag, button = 2, mousePosition = new Vector2(100, 0) } });
                var after = (ChartVerticalRange)range.Invoke(window, null);
                Assert.AreEqual(before.Span, after.Span);
                Assert.AreEqual(-before.Span / 2, (after.MinBps + after.MaxBps) / 2, .001, "dragging up centers lower prices");
                Assert.AreEqual(0, ((HashSet<string>)type.GetField("revealedMarketDays", flags).GetValue(window)).Count);
                input.Invoke(window, new object[] { rect, "BANK", new Event { type = EventType.MouseUp, button = 2, mousePosition = new Vector2(100, -10) } });
                Assert.IsNull(type.GetField("panningCompany", flags).GetValue(window));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void CompanyCombinationPreviewHonorsSelectedBaseAndHiddenWithoutEditingLibrary()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var library = (StockPatternLibrary)type.GetField("authoringLibrary", flags).GetValue(window);
                var pattern = library.patterns.First(p => BaseStockPatternGenerator.Shape(p) != BasePatternShape.Authored);
                string before = JsonUtility.ToJson(library);
                type.GetField("authorCompany", flags).SetValue(window, System.Array.IndexOf(InvestmentRules.CompanyIds, "FOOD"));
                var sample = (StockPatternSample)type.GetMethod("GenerateCombinationPreview", flags).Invoke(window, new object[] { pattern, HiddenPatternKind.DownUp });
                Assert.AreEqual("FOOD", sample.CompanyId);
                Assert.AreEqual(pattern.id, sample.PatternId);
                Assert.AreEqual(HiddenPatternKind.DownUp, sample.Hidden);
                Assert.LessOrEqual(sample.EndDay, 25);
                Assert.AreEqual(before, JsonUtility.ToJson(library));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void LegacyLibraryUpgradesRulesAndRoundTripsEditableSurgeMultiplier()
        {
            var legacy = StockPatternLibrary.CreateDefault();
            legacy.registeredVersion = 1;
            foreach (var pattern in legacy.patterns) pattern.baseShape = BasePatternShape.Authored;
            string json = JsonUtility.ToJson(legacy);
            var restored = JsonUtility.FromJson<StockPatternLibrary>(json);
            restored.UpgradeLegacy();
            Assert.AreEqual(2, restored.registeredVersion);
            Assert.AreEqual(4, restored.patterns.Count);
            Assert.AreEqual(BasePatternShape.M, restored.patterns.Find(p => p.id == "builtin-m").baseShape);
            restored.baseSettings.surgeMultiplier = 4.5;
            restored = JsonUtility.FromJson<StockPatternLibrary>(JsonUtility.ToJson(restored));
            restored.UpgradeLegacy();
            Assert.AreEqual(4.5, restored.Copy().baseSettings.surgeMultiplier);
            foreach (string company in InvestmentRules.CompanyIds)
                CollectionAssert.AreEqual(PatternMarketGenerator.Inspect(81, 1, company, library: restored).DailyChangesBps,
                    PatternMarketGenerator.Inspect(81, 1, company, library: restored.Copy()).DailyChangesBps);
            restored.patterns.RemoveAt(0);
            restored.UpgradeLegacy();
            Assert.AreEqual(3, restored.patterns.Count, "upgrading never restores a deliberately deleted candidate");
        }

        [Test]
        public void RandomFogRevealStaysInInclusiveRangeNeverRepeatsAndStopsWhenExhausted()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Assert.AreEqual(0, type.GetField("randomRevealStartDay", flags).GetValue(window));
                Assert.AreEqual(25, type.GetField("randomRevealEndDay", flags).GetValue(window));
                var reveal = type.GetMethod("RevealRandomMarketFog", flags);
                Assert.AreEqual(false, reveal.Invoke(window, null), "no fog means nothing to reveal");
                type.GetMethod("SetMarketFog", flags).Invoke(window, new object[] { true });
                type.GetField("randomRevealStartDay", flags).SetValue(window, 24);
                type.GetField("randomRevealEndDay", flags).SetValue(window, 25);
                var revealed = (HashSet<string>)type.GetField("revealedMarketDays", flags).GetValue(window);
                // Include a manually opened date: random picks must also skip this one.
                revealed.Add("BANK:24");
                for (int count = 2; count <= 12; count++)
                {
                    Assert.AreEqual(true, reveal.Invoke(window, null));
                    Assert.AreEqual(count, revealed.Count, "every click reveals exactly one new company/day");
                    Assert.IsTrue(revealed.All(key => key.EndsWith(":24") || key.EndsWith(":25")));
                }
                Assert.AreEqual(false, reveal.Invoke(window, null));
                Assert.AreEqual(12, revealed.Count);
                type.GetField("randomRevealStartDay", flags).SetValue(window, 0);
                type.GetField("randomRevealEndDay", flags).SetValue(window, 0);
                Assert.AreEqual(false, reveal.Invoke(window, null), "day zero is always visible");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MarketFogRevealsOnlyClickedCompanyDayAndKeepsBaselineVisible()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var setFog = type.GetMethod("SetMarketFog", flags);
                var hidden = type.GetMethod("IsMarketDayHidden", flags);
                setFog.Invoke(window, new object[] { true });
                foreach (var sample in window.Samples)
                    for (int day = 0; day <= 30; day++)
                        Assert.AreEqual(day > 0, hidden.Invoke(window, new object[] { sample.CompanyId, day }));
                var click = new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(155, 50) };
                var input = type.GetMethod("HandleMarketPlotInput", flags);
                Assert.IsTrue((bool)input.Invoke(window, new object[] { new Rect(0, 0, 310, 100), "BANK", click }));
                Assert.AreEqual(EventType.Used, click.type, "opening fog must not select or close the card");
                Assert.AreEqual(false, hidden.Invoke(window, new object[] { "BANK", 15 }));
                Assert.AreEqual(true, hidden.Invoke(window, new object[] { "BANK", 14 }));
                Assert.AreEqual(true, hidden.Invoke(window, new object[] { "BIO", 15 }));
                setFog.Invoke(window, new object[] { true });
                Assert.AreEqual(true, hidden.Invoke(window, new object[] { "BANK", 15 }));
                setFog.Invoke(window, new object[] { false });
                foreach (var sample in window.Samples)
                    for (int day = 0; day <= 30; day++)
                        Assert.AreEqual(false, hidden.Invoke(window, new object[] { sample.CompanyId, day }));
                setFog.Invoke(window, new object[] { true });
                input.Invoke(window, new object[] { new Rect(0, 0, 310, 100), "BANK", new Event { type = EventType.MouseDown, mousePosition = new Vector2(305, 50) } });
                Assert.AreEqual(false, hidden.Invoke(window, new object[] { "BANK", 30 }));
                window.Configure(43, 1, true);
                Assert.AreEqual(true, hidden.Invoke(window, new object[] { "BANK", 30 }), "new samples reset individual reveals");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MarketPercentageRangeFitsAllCompaniesAndWheelZoomSurvivesFogChanges()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var getRange = type.GetMethod("MarketRange", flags);
                var original = (ChartVerticalRange)getRange.Invoke(window, null);
                Assert.AreEqual(-original.MinBps, original.MaxBps);
                foreach (int value in window.Samples.SelectMany(s => s.DailyChangesBps))
                { Assert.Greater(original.MaxBps, value); Assert.Less(original.MinBps, value); }
                var input = type.GetMethod("HandleMarketPlotInput", flags);
                var wheel = new Event { type = EventType.ScrollWheel, mousePosition = new Vector2(150, 50), delta = new Vector2(0, -6) };
                Assert.IsTrue((bool)input.Invoke(window, new object[] { new Rect(0, 0, 310, 100), "BANK", wheel }));
                Assert.AreEqual(EventType.Used, wheel.type, "chart zoom consumes scrolling before the outer scroll view");
                var zoomed = (ChartVerticalRange)getRange.Invoke(window, null);
                Assert.Less(zoomed.Span, original.Span);
                type.GetMethod("SetMarketFog", flags).Invoke(window, new object[] { true });
                Assert.AreEqual(zoomed.Span, ((ChartVerticalRange)getRange.Invoke(window, null)).Span);
                wheel = new Event { type = EventType.ScrollWheel, mousePosition = new Vector2(150, 50), delta = new Vector2(0, 6) };
                input.Invoke(window, new object[] { new Rect(0, 0, 310, 100), "BIO", wheel });
                Assert.AreEqual(original.Span, ((ChartVerticalRange)getRange.Invoke(window, null)).Span);
                wheel = new Event { type = EventType.ScrollWheel, mousePosition = new Vector2(500, 50), delta = new Vector2(0, -6) };
                Assert.IsFalse((bool)input.Invoke(window, new object[] { new Rect(0, 0, 310, 100), "BANK", wheel }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void PreviewUsesGameCandleOpensDirectionsAndResourceMigrationDefaults()
        {
            int[] values = Enumerable.Range(0, 31).Select(day => day % 3 == 0 ? 0 : day % 3 == 1 ? 300 : -100).ToArray();
            var preview = DailyBarModel.FromDailyChanges(values);
            var game = DailyBarModel.Build(null, new RevealedCompany { dailyChangesBps = values });
            for (int day = 0; day < 31; day++)
            {
                Assert.AreEqual(game[day].OpenBps, preview[day].OpenBps);
                Assert.AreEqual(game[day].Bps, preview[day].Bps);
                Assert.AreEqual(game[day].Move, preview[day].Move);
            }
            var migrated = JsonUtility.FromJson<StockPatternLibrary>("{\"patterns\":[]}").Copy();
            Assert.AreEqual(200, migrated.volatility.bankBps);
            Assert.AreEqual(2500, migrated.volatility.spaceBps);
            var roundTrip = JsonUtility.FromJson<StockPatternLibrary>(JsonUtility.ToJson(migrated));
            Assert.AreEqual(1500, roundTrip.volatility.aiBps);
            migrated.UpgradeLegacy();
            Assert.AreEqual(4, migrated.patterns.Count);
            migrated.UpgradeLegacy();
            Assert.AreEqual(4, migrated.patterns.Count, "migration registers the original templates only once");
        }
        [Test]
        public void DrawingClicksConnectForwardVerticesAndSurviveJsonRoundTrip()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("NewDrawing", flags).Invoke(window, new object[] { -1 });
                var click = type.GetMethod("CompleteDrawingClick", flags);
                // Each empty-space click immediately appends a connected vertex.
                click.Invoke(window, new object[] { -1, new Vector2(.1f, 0) });
                click.Invoke(window, new object[] { -1, new Vector2(.5f, .8f) });
                click.Invoke(window, new object[] { -1, new Vector2(.9f, -.3f) });
                var draft = (CustomStockPattern)type.GetField("draft", flags).GetValue(window);
                Assert.AreEqual(3, draft.vertices.Count);
                Assert.AreEqual(2, draft.segmentDays.Count);
                Assert.AreEqual(3, draft.startDay);
                Assert.AreEqual(24, draft.TotalDays);
                CollectionAssert.AreEqual(new[] { 12, 12 }, draft.segmentDays);
                type.GetMethod("RestoreDrawing", flags).Invoke(window, null);
                var points = (List<Vector2>)type.GetField("drawingPoints", flags).GetValue(window);
                Assert.AreEqual(.1f, points[0].x, .0001);
                Assert.AreEqual(.9f, points[2].x, .0001);
                click.Invoke(window, new object[] { 2, new Vector2(.9f, -.3f) });
                click.Invoke(window, new object[] { 0, new Vector2(.1f, 0) });
                click.Invoke(window, new object[] { -1, new Vector2(.5f, .3f) });
                click.Invoke(window, new object[] { -1, new Vector2(.9f, .3f) });
                Assert.AreEqual(3, draft.vertices.Count, "clicks on occupied dates cannot add duplicates");
                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, (List<int>)type.GetField("connectedPoints", flags).GetValue(window));
                var library = new StockPatternLibrary(); library.patterns.Add(draft);
                var restored = JsonUtility.FromJson<StockPatternLibrary>(JsonUtility.ToJson(library));
                CollectionAssert.AreEqual(draft.GenerateRaw(), restored.patterns[0].GenerateRaw());
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void InsertingDayThreeSplitsDayOneToFiveAndCompanySwitchKeepsTheDrawing()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("NewDrawing", flags).Invoke(window, new object[] { -1 });
                var click = type.GetMethod("CompleteDrawingClick", flags);
                click.Invoke(window, new object[] { -1, new Vector2(1f / 30, 0) });
                click.Invoke(window, new object[] { -1, new Vector2(5f / 30, .2f) });
                click.Invoke(window, new object[] { -1, new Vector2(3f / 30, .8f) });
                var draft = (CustomStockPattern)type.GetField("draft", flags).GetValue(window);
                Assert.AreEqual(1, draft.startDay);
                CollectionAssert.AreEqual(new[] { 2, 2 }, draft.segmentDays);
                CollectionAssert.AreEqual(new[] { 0.0, (double).8f, (double).2f }, draft.vertices);
                Assert.AreEqual(1000, draft.GenerateRaw()[2], .001, "the inserted date is sampled as a real vertex");
                var before = draft.GenerateRaw();
                type.GetMethod("SelectAuthorCompany", flags).Invoke(window, new object[] { 3 });
                CollectionAssert.AreEqual(before, draft.GenerateRaw(), "switching the save destination preserves the draft");
                var saved = (CustomStockPattern)type.GetMethod("PrepareSavedDraft", flags).Invoke(window, new object[] { false });
                Assert.AreEqual("BANK", saved.companyId);
                var library = (StockPatternLibrary)type.GetField("authoringLibrary", flags).GetValue(window);
                library.patterns.Add(saved);
                draft.id = saved.id;
                type.GetMethod("SelectAuthorCompany", flags).Invoke(window, new object[] { 0 });
                var other = (CustomStockPattern)type.GetMethod("PrepareSavedDraft", flags).Invoke(window, new object[] { false });
                Assert.AreEqual("AI", other.companyId);
                Assert.AreNotEqual(saved.id, other.id, "saving to another company cannot move the original asset");
                Assert.AreEqual("BANK", library.patterns.Find(p => p.id == saved.id).companyId);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator AuthoringTabPaintsPresetsAndDailyPreviewWithoutErrors()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                window.position = new Rect(40, 40, 1200, 1000); window.Show();
                typeof(PatternLabWindow).GetField("mode", flags).SetValue(window, 2);
                typeof(PatternLabWindow).GetMethod("NewDrawing", flags).Invoke(window, new object[] { 0 });
                var candles = typeof(PatternLabWindow).GetField("previewCandleCount", flags);
                for (int frame = 0; frame < 60 && (int)candles.GetValue(window) == 0; frame++) { window.Repaint(); yield return null; }
                Assert.AreEqual(31, candles.GetValue(window));
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.Close(); }
        }

        [Test]
        public void BasePresetAndBlankUserDraftSaveIntoTheirRespectiveCategories()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("authoringLibrary", flags).SetValue(window, StockPatternLibrary.CreateDefault());
                type.GetMethod("NewDrawing", flags).Invoke(window, new object[] { 0 });
                var saved = (CustomStockPattern)type.GetMethod("PrepareSavedDraft", flags).Invoke(window, new object[] { false });
                Assert.IsTrue(saved.isBasePattern);
                Assert.IsFalse(saved.useCustomProbability);
                var library = (StockPatternLibrary)type.GetField("authoringLibrary", flags).GetValue(window);
                library.patterns.Add(saved);
                Assert.AreEqual(5, library.BasePatternCount("AI"));
                CollectionAssert.AreEqual(new double[] { 20, 20, 20, 20, 20 }, library.Probabilities("AI"));
                type.GetMethod("NewDrawing", flags).Invoke(window, new object[] { -1 });
                var draft = (CustomStockPattern)type.GetField("draft", flags).GetValue(window);
                Assert.IsFalse(draft.isBasePattern);
                Assert.IsEmpty(draft.vertices, "the left-side blank graph action starts a user pattern");
                var click = type.GetMethod("CompleteDrawingClick", flags);
                click.Invoke(window, new object[] { -1, new Vector2(.1f, 0) });
                click.Invoke(window, new object[] { -1, new Vector2(.5f, .6f) });
                var user = (CustomStockPattern)type.GetMethod("PrepareSavedDraft", flags).Invoke(window, new object[] { false });
                Assert.IsFalse(user.isBasePattern);
                library.patterns.Add(user);
                var restored = JsonUtility.FromJson<StockPatternLibrary>(JsonUtility.ToJson(library));
                Assert.IsTrue(restored.patterns.Find(p => p.id == saved.id).isBasePattern);
                Assert.IsFalse(restored.patterns.Find(p => p.id == user.id).isBasePattern);
                Assert.AreEqual(5, restored.BasePatternCount("AI"));
                Assert.AreEqual(5, restored.BasePatternCount("BANK"), "saving a base pattern affects every company");
                type.GetMethod("SelectAuthorCompany", flags).Invoke(window, new object[] { 3 });
                type.GetField("draft", flags).SetValue(window, saved.Copy());
                var updated = (CustomStockPattern)type.GetMethod("PrepareSavedDraft", flags).Invoke(window, new object[] { false });
                Assert.AreEqual(saved.id, updated.id, "a shared base can be edited from another company's tab without duplication");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void DeleteButtonsDistinguishLatestDateFromLatestInsertionAndResetOnlyTheGraph()
        {
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                var type = typeof(PatternLabWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("NewDrawing", flags).Invoke(window, new object[] { -1 });
                var click = type.GetMethod("CompleteDrawingClick", flags);
                click.Invoke(window, new object[] { -1, new Vector2(1f / 30, 0) });
                click.Invoke(window, new object[] { -1, new Vector2(5f / 30, .2f) });
                click.Invoke(window, new object[] { -1, new Vector2(3f / 30, .8f) });
                // Rebuilding screen coordinates must retain the insertion history as well.
                type.GetMethod("RestoreDrawing", flags).Invoke(window, null);
                type.GetMethod("DeleteLastAddedPoint", flags).Invoke(window, null);
                var draft = (CustomStockPattern)type.GetField("draft", flags).GetValue(window);
                Assert.AreEqual(2, draft.vertices.Count);
                CollectionAssert.AreEqual(new[] { 4 }, draft.segmentDays, "delete recent removes day three and rejoins days one and five");
                click.Invoke(window, new object[] { -1, new Vector2(3f / 30, .8f) });
                type.GetMethod("DeleteLastPoint", flags).Invoke(window, null);
                CollectionAssert.AreEqual(new[] { 2 }, draft.segmentDays, "delete last removes day five, leaving days one and three");
                type.GetMethod("DeleteLastAddedPoint", flags).Invoke(window, null);
                Assert.AreEqual(1, draft.vertices.Count);
                type.GetMethod("DeleteLastAddedPoint", flags).Invoke(window, null);
                Assert.AreEqual(0, draft.vertices.Count);
                Assert.AreEqual(0, draft.startDay);
                click.Invoke(window, new object[] { -1, new Vector2(2f / 30, .5f) });
                draft.name = "유지할 이름";
                type.GetMethod("ResetGraph", flags).Invoke(window, null);
                Assert.AreEqual("유지할 이름", draft.name);
                Assert.IsEmpty(draft.vertices); Assert.IsEmpty(draft.segmentDays);
                Assert.IsEmpty((List<Vector2>)type.GetField("drawingPoints", flags).GetValue(window));
                Assert.IsEmpty((List<int>)type.GetField("pointAdditionOrder", flags).GetValue(window));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ReservedBandIncludesEntryOpenAndExitCloseSlotsIncludingSingleDay()
        {
            var go = new GameObject("Trade boundaries", typeof(RectTransform), typeof(CanvasRenderer), typeof(BarChartGraphic));
            try
            {
                var chart = go.GetComponent<BarChartGraphic>(); chart.rectTransform.sizeDelta = new Vector2(310, 160);
                chart.SetData(DailyBarModel.Build(null));
                foreach (var interval in new[] { new Interval { entryDay = 3, exitDay = 5 }, new Interval { entryDay = 30, exitDay = 30 } })
                {
                    chart.SetTradeIntervals(new[] { interval }, true);
                    using (var mesh = new VertexHelper())
                    {
                        typeof(BarChartGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new[] { typeof(VertexHelper) }, null).Invoke(chart, new object[] { mesh });
                        var vertices = new List<UIVertex>(); mesh.GetUIVertexStream(vertices);
                        var band = vertices.Where(v => v.color.Equals(BarChartGraphic.BandColor)).ToArray();
                        Assert.AreEqual(DayAxis.LeftX(interval.entryDay, chart.rectTransform.rect.xMin, 310), band.Min(v => v.position.x), .001);
                        Assert.AreEqual(DayAxis.LeftX(interval.exitDay + 1, chart.rectTransform.rect.xMin, 310), band.Max(v => v.position.x), .001);
                        Assert.Greater(band.Max(v => v.position.x) - band.Min(v => v.position.x), 0);
                    }
                    var entry = chart.GetComponentsInChildren<Text>().Single(t => t.name == "EntryMarker_0");
                    var exit = chart.GetComponentsInChildren<Text>().Single(t => t.name == "ExitMarker_0");
                    StringAssert.Contains("시가", entry.text); StringAssert.Contains("종가", exit.text);
                    Assert.AreEqual(interval.entryDay / 31f, entry.rectTransform.anchorMin.x, .0001);
                    Assert.AreEqual((interval.exitDay + 1) / 31f, exit.rectTransform.anchorMin.x, .0001);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
        [UnityTest]
        public IEnumerator LaboratoryShowsOnlyMarketSamplesAndAuthoringWithoutChangingScene()
        {
            var scene = SceneManager.GetActiveScene();
            var window = ScriptableObject.CreateInstance<PatternLabWindow>();
            try
            {
                window.Configure(42, 1, true);
                Assert.AreEqual(6, window.Samples.Count);
                window.position = new Rect(40, 40, 1200, 900); window.Show(); window.Repaint();
                yield return null; yield return null;
                var paintedCards = typeof(PatternLabWindow).GetField("renderedCardCount", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int frame = 0; frame < 60 && (int)paintedCards.GetValue(window) == 0; frame++)
                { window.Repaint(); yield return null; }
                Assert.Greater((int)paintedCards.GetValue(window), 0, "the editor window must actually paint its charts");
                Assert.Greater((int)typeof(PatternLabWindow).GetField("renderedMarketCandleCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window), 0);
                Assert.Greater((int)typeof(PatternLabWindow).GetField("renderedCloseLinePointCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window), 0, "candles and close lines are rendered together");
                Assert.AreEqual(scene.handle, SceneManager.GetActiveScene().handle, "opening a lab does not replace the user's scene");
                window.Configure(42, 7, true);
                var library = (StockPatternLibrary)typeof(PatternLabWindow).GetField("authoringLibrary", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                var market = PatternMarketGenerator.Generate(42, 7, library: library);
                Assert.AreEqual(6, window.Samples.Count);
                foreach (var sample in window.Samples)
                    CollectionAssert.AreEqual(market.companies.Single(c => c.companyId == sample.CompanyId).dailyChangesBps, sample.DailyChangesBps);
                window.Configure(42, 7);
                Assert.AreEqual(0, window.Samples.Count, "authoring never creates the removed comparison samples");
            }
            finally { if (window != null) window.Close(); }
        }
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(4, 2)]
        [TestCase(30, 28)]
        public void PriceTrendStopsAtPublishedDayAndBreaksAtUnknownDays(int publishedDay, int segments)
        {
            var go = new GameObject("Published price trend", typeof(RectTransform), typeof(BarChartGraphic));
            try
            {
                var chart = go.GetComponent<BarChartGraphic>();
                chart.rectTransform.sizeDelta = new Vector2(620, 200);
                var bars = Enumerable.Range(0, 31).Select(day => new DayBar { Day = day, Bps = day * 100, Source = "PUBLIC" }).ToArray();
                bars[2].Bps = null;
                chart.SetData(bars);
                chart.SetTrendThroughDay(publishedDay);
                var populate = typeof(BarChartGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                using (var mesh = new VertexHelper())
                {
                    populate.Invoke(chart, new object[] { mesh });
                    var vertices = new List<UIVertex>();
                    for (int i = 0; i < mesh.currentVertCount; i++)
                    {
                        var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                        if (vertex.color.Equals(BarChartGraphic.TrendColor)) vertices.Add(vertex);
                    }
                    Assert.AreEqual(segments * 4, vertices.Count, "unknown days break the line and future known prices are excluded");
                    Assert.IsTrue(vertices.All(v => v.position.x <= DayAxis.CenterX(publishedDay, chart.rectTransform.rect.xMin, 620) + 1), "line ends at the last published close");
                    chart.SetTrendThroughDay(null);
                    populate.Invoke(chart, new object[] { mesh });
                    for (int i = 0; i < mesh.currentVertCount; i++)
                    {
                        var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                        Assert.IsFalse(vertex.color.Equals(BarChartGraphic.TrendColor), "leaving price publication removes the trend");
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ChartFitsOnlyVisibleOpensAndClosesAndIgnoresHiddenBounds()
        {
            var go = new GameObject("Adaptive chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(BarChartGraphic));
            try
            {
                var graphic = go.GetComponent<BarChartGraphic>();
                var bars = new[] { new DayBar { Day = 0, Bps = 0, OpenBps = 0 }, new DayBar { Day = 1, Bps = 100, OpenBps = 0 }, new DayBar { Day = 2 } };
                graphic.SetData(bars, 5000);
                var field = typeof(BarChartGraphic).GetField("_verticalRange", BindingFlags.NonPublic | BindingFlags.Instance);
                var first = (ChartVerticalRange)field.GetValue(graphic);
                Assert.Less(first.Span, 200);
                bars[2].OpenBps = 999999; // a hidden bar must not contribute even if an accidental open is present
                graphic.SetData(bars, 8000);
                var same = (ChartVerticalRange)field.GetValue(graphic);
                Assert.AreEqual(first.MinBps, same.MinBps); Assert.AreEqual(first.MaxBps, same.MaxBps);
                bars[2].Bps = 1000; bars[2].OpenBps = 900;
                graphic.SetData(bars);
                var revealed = (ChartVerticalRange)field.GetValue(graphic);
                Assert.Greater(revealed.MaxBps, 1000);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
