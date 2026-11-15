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
