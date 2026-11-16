using System;
using System.Collections.Generic;
using BlackWhale.Rules;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using UnityEditor;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    // Independent diagnostic samples. No room, transport, scene switch, or play-mode dependency.
    public sealed partial class PatternLabWindow : EditorWindow
    {
        [SerializeField] private int seed = 20261108, turn = 1, mode = 2;
        [SerializeField] private bool highlight = true;
        [SerializeField] private bool showMarketCloseLine = true;
        [SerializeField] private int randomRevealStartDay = 0, randomRevealEndDay = 25;
        private readonly System.Random fogRandom = new System.Random();
        private string randomRevealMessage = "";
        private Vector2 scroll;
        private int selected = -1;
        private readonly HashSet<string> revealedMarketDays = new HashSet<string>();
        private bool marketFog;
        private float marketZoom = 1f;
        private double marketExtentBps = 500;
        private int renderedCardCount;
        private int renderedMarketCandleCount, renderedCloseLinePointCount;
        private GUIStyle cardTitle, cardNote;
        private readonly List<StockPatternSample> samples = new List<StockPatternSample>();
        public IReadOnlyList<StockPatternSample> Samples => samples;
        private static readonly string[] PatternNames = { "박스권 · Rectangle", "M자 · Double Top", "W자 · Double Bottom", "깃발형 · Flag" };
        private static readonly string[] Turns = { "1턴", "2턴", "3턴", "4턴", "5턴", "6턴", "7턴", "8턴", "9턴", "10턴" };

        [MenuItem("BlackWhale/패턴 실험실", false, 120)]
        public static void Open()
        {
            var window = GetWindow<PatternLabWindow>("패턴 실험실", true, typeof(SceneView));
            window.minSize = new Vector2(1000, 640); window.Show();
        }
        private void OnEnable() { titleContent = new GUIContent("패턴 실험실"); wantsMouseMove = true; wantsMouseEnterLeaveWindow = true; if (mode != 2) mode = 1; LoadAuthoringLibrary(); Rebuild(); }
        public void Configure(int newSeed, int newTurn, bool normalMarket = false)
        {
            seed = newSeed; turn = Math.Max(1, Math.Min(10, newTurn));
            mode = normalMarket ? 1 : 2; selected = -1; Rebuild(); Repaint();
        }
        private void Rebuild()
        {
            samples.Clear();
            revealedMarketDays.Clear();
            randomRevealMessage = "";
            marketExtentBps = 50;
            if (mode == 1)
                foreach (string id in InvestmentRules.CompanyIds)
                {
                    var sample = PatternMarketGenerator.Inspect(seed, turn, id, library: authoringLibrary);
                    samples.Add(sample);
                    foreach (int value in sample.DailyChangesBps)
                        marketExtentBps = Math.Max(marketExtentBps, Math.Abs((double)value));
                }
            marketExtentBps *= 1.24;
        }
        private void SetMarketFog(bool covered)
        {
            marketFog = covered;
            revealedMarketDays.Clear();
            randomRevealMessage = "";
            Repaint();
        }
        private bool IsMarketDayHidden(string companyId, int day)
            => marketFog && day > 0 && !revealedMarketDays.Contains(companyId + ":" + day);

        private bool RevealRandomMarketFog()
        {
            int first = Mathf.Clamp(Math.Min(randomRevealStartDay, randomRevealEndDay), 0, 30);
            int last = Mathf.Clamp(Math.Max(randomRevealStartDay, randomRevealEndDay), 0, 30);
            var candidates = new List<KeyValuePair<string, int>>();
            foreach (var sample in samples)
                for (int day = first; day <= last; day++)
                    if (IsMarketDayHidden(sample.CompanyId, day))
                        candidates.Add(new KeyValuePair<string, int>(sample.CompanyId, day));
            // Sampling only remaining fog is equivalent to retrying revealed days, without an unbounded loop.
            if (candidates.Count == 0)
            {
                randomRevealMessage = $"{first}~{last}일 범위에 남은 안개가 없습니다.";
                Repaint(); return false;
            }
            var chosen = candidates[fogRandom.Next(candidates.Count)];
            revealedMarketDays.Add(chosen.Key + ":" + chosen.Value);
            randomRevealMessage = $"{chosen.Key} · {chosen.Value}일차 공개";
            Repaint(); return true;
        }

        private ChartVerticalRange MarketRange()
        {
            double extent = marketExtentBps / marketZoom;
            // Four equal intervals, with the same readable percentage ticks on every company.
            double roughStep = extent / 2;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
            double step = Math.Ceiling(roughStep / magnitude) * magnitude;
            return new ChartVerticalRange { MinBps = -step * 2, MaxBps = step * 2 };
        }
        private bool HandleMarketPlotInput(Rect rect, string companyId, Event input)
        {
            if (!rect.Contains(input.mousePosition)) return false;
            if (input.type == EventType.ScrollWheel)
            {
                marketZoom = Mathf.Clamp(marketZoom * Mathf.Pow(1.12f, -input.delta.y), .1f, 100f);
                input.Use(); Repaint(); return true;
            }
            if (input.type == EventType.MouseDown && input.button == 0)
            {
                int day = Mathf.Clamp(Mathf.FloorToInt((input.mousePosition.x - rect.x) / rect.width * 31), 0, 30);
                if (IsMarketDayHidden(companyId, day))
                {
                    revealedMarketDays.Add(companyId + ":" + day);
                    input.Use(); Repaint(); return true;
                }
            }
            return false;
        }
        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) renderedCardCount = renderedMarketCandleCount = renderedCloseLinePointCount = 0;
            if (cardTitle == null)
            {
                cardTitle = new GUIStyle(EditorStyles.boldLabel); cardTitle.normal.textColor = new Color(.92f, .95f, 1);
                cardNote = new GUIStyle(EditorStyles.miniLabel); cardNote.normal.textColor = new Color(.70f, .77f, .84f);
            }
            DrawVolatilitySettings();
            EditorGUI.BeginChangeCheck();
            int previousMode = mode;
            mode = GUILayout.Toolbar(mode == 2 ? 1 : 0, new[] { "일반 게임 생성 · 6종목", "직접 그리기 · 기업별 패턴" }, GUILayout.Height(28)) + 1;
            if (mode != previousMode)
            { EditorGUI.EndChangeCheck(); selected = -1; Rebuild(); Repaint(); GUIUtility.ExitGUI(); }
            if (mode == 2) { EditorGUI.EndChangeCheck(); DrawAuthoring(); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField("시드", seed, GUILayout.Width(230));
                turn = EditorGUILayout.Popup(turn - 1, Turns, GUILayout.Width(75)) + 1;
                GUILayout.Label("일봉 캔들", GUILayout.Width(80));
                highlight = GUILayout.Toggle(highlight, "패턴 위치 강조", GUILayout.Width(115));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("새 시드", GUILayout.Width(75))) { seed = RandomMarketGenerator.NewSeed(); GUI.changed = true; }
                if (GUILayout.Button("다시 생성", GUILayout.Width(85))) GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck()) { selected = -1; Rebuild(); }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("안개 모두 씌우기", GUILayout.Width(130))) SetMarketFog(true);
                if (GUILayout.Button("안개 모두 없애기", GUILayout.Width(130))) SetMarketFog(false);
                if (GUILayout.Button(showMarketCloseLine ? "꺾은선 끄기" : "꺾은선 켜기", GUILayout.Width(100)))
                { showMarketCloseLine = !showMarketCloseLine; Repaint(); }
                if (GUILayout.Button("배율 초기화", GUILayout.Width(100))) { marketZoom = 1f; Repaint(); }
                GUILayout.Label($"공통 % 축 · 세로 배율 {marketZoom:0.##}배", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("랜덤 공개 범위", GUILayout.Width(90));
                randomRevealStartDay = Mathf.Clamp(EditorGUILayout.IntField(randomRevealStartDay, GUILayout.Width(45)), 0, 30);
                GUILayout.Label("~", GUILayout.Width(12));
                randomRevealEndDay = Mathf.Clamp(EditorGUILayout.IntField(randomRevealEndDay, GUILayout.Width(45)), 0, 30);
                GUILayout.Label("일 (양 끝 포함)", GUILayout.Width(90));
                if (GUILayout.Button(new GUIContent("랜덤 안개 1개 없애기", "6기업 중 지정 범위에 남은 안개 한 칸을 무작위로 공개합니다. 이미 공개된 칸은 제외합니다."), GUILayout.Width(155)))
                    RevealRandomMarketFog();
                GUILayout.Label(randomRevealMessage, EditorStyles.miniLabel);
            }
            EditorGUILayout.HelpBox("카드를 누르면 크게 봅니다. 안개를 클릭하면 해당 기업의 하루만 공개됩니다(0일차는 항상 공개). 차트 위에서 휠 ↑ 확대 / ↓ 축소 · 모든 기업에 같은 % 축과 배율을 적용합니다.", MessageType.Info);
            EditorGUILayout.LabelField("기본 패턴: 규칙 기반 변형 + hidden · 사용자 패턴: 등록 모양 유지 · 실제 게임과 동일한 가격 생성", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (selected >= 0 && selected < samples.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                { GUILayout.Label("선택한 샘플", EditorStyles.boldLabel); if (GUILayout.Button("상세 닫기", GUILayout.Width(90))) selected = -1; }
                if (selected >= 0)
                {
                    Rect detail = GUILayoutUtility.GetRect(100, 300, GUILayout.ExpandWidth(true));
                    DrawCard(detail, samples[selected], true);
                }
            }
            const int columns = 3;
            int rows = (samples.Count + columns - 1) / columns;
            for (int row = 0; row < rows; row++)
            {
                Rect strip = GUILayoutUtility.GetRect(100, 190, GUILayout.ExpandWidth(true));
                float width = (strip.width - 16) / columns;
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column; if (index >= samples.Count) continue;
                    Rect card = new Rect(strip.x + column * (width + 8), strip.y, width, 184);
                    DrawCard(card, samples[index], index == selected);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && card.Contains(Event.current.mousePosition))
                    { selected = index == selected ? -1 : index; Event.current.Use(); Repaint(); }
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseLeaveWindow) Repaint();
        }
        private void DrawCard(Rect card, StockPatternSample sample, bool active)
        {
            if (Event.current.type == EventType.Repaint) renderedCardCount++;
            EditorGUI.DrawRect(card, active ? new Color(.16f, .28f, .40f) : new Color(.16f, .17f, .20f));
            var header = new Rect(card.x + 10, card.y + 6, card.width - 20, 20);
            GUI.Label(header, sample.CompanyId + "   " + (sample.CustomPatternName ?? PatternNames[(int)sample.Pattern]), cardTitle);
            GUI.Label(new Rect(header.x, header.y + 20, header.width, 18), (sample.IsBasePattern ? "공유 기본 패턴" : "기업별 사용자 패턴"), cardNote);
            Rect plot = new Rect(card.x + 57, card.y + 48, card.width - 67, card.height - 75);
            var range = MarketRange();
            for (int tick = 0; tick <= 4; tick++)
            {
                double value = range.MaxBps - range.Span * tick / 4;
                GUI.Label(new Rect(card.x + 3, plot.y + plot.height * tick / 4 - 8, 53, 16),
                    (value / 100).ToString("0.##") + "%", cardNote);
            }
            DrawPlot(plot, sample);
            string footer = sample.Outcome == StockPatternOutcome.Unstructured ? "형태 없는 추세·횡보 경로"
                : "주 패턴 배치: " + sample.StartDay + "~" + sample.EndDay + "일 · hidden " + sample.Hidden;
            GUI.Label(new Rect(card.x + 10, card.yMax - 20, card.width - 20, 18), footer, cardNote);
        }
        private void DrawPlot(Rect rect, StockPatternSample sample)
        {
            HandleMarketPlotInput(rect, sample.CompanyId, Event.current);
            GUI.BeginGroup(rect);
            rect = new Rect(0, 0, rect.width, rect.height);
            EditorGUI.DrawRect(rect, new Color(.065f, .085f, .12f));
            var range = MarketRange();
            float X(int day) => rect.x + (day + .5f) / 31 * rect.width;
            float Y(int bps) => rect.yMax - (float)((bps - range.MinBps) / range.Span) * rect.height;
            if (highlight && sample.Outcome != StockPatternOutcome.Unstructured)
                EditorGUI.DrawRect(new Rect(rect.x + sample.StartDay / 31f * rect.width, rect.y,
                    (sample.EndDay - sample.StartDay + 1) / 31f * rect.width, rect.height), new Color(.15f, .30f, .42f, .35f));
            Handles.BeginGUI();
            Handles.color = new Color(.35f, .40f, .47f, .45f);
            foreach (float fraction in new[] { .25f, .5f, .75f })
                Handles.DrawLine(new Vector3(rect.x, rect.y + fraction * rect.height), new Vector3(rect.xMax, rect.y + fraction * rect.height));
            Handles.color = new Color(.55f, .60f, .67f, .65f);
            Handles.DrawDottedLine(new Vector3(rect.x, Y(0)), new Vector3(rect.xMax, Y(0)), 4);
            var points = new Vector3[31];
            for (int day = 0; day <= 30; day++) points[day] = new Vector3(X(day), Y(sample.DailyChangesBps[day]));
            Handles.EndGUI();
            for (int day = 0; day <= 30; day++)
                {
                    if (IsMarketDayHidden(sample.CompanyId, day)) continue;
                    int open = sample.DailyChangesBps[Math.Max(0, day - 1)], close = sample.DailyChangesBps[day];
                    float height = Math.Max(2, Math.Abs(Y(open) - Y(close)));
                    EditorGUI.DrawRect(new Rect(X(day) - rect.width / 31 * .31f, (Y(open) + Y(close)) / 2 - height / 2, rect.width / 31 * .62f, height),
                        close > open ? (Color)BarChartGraphic.UpColor : close < open ? (Color)BarChartGraphic.DownColor : (Color)BarChartGraphic.FlatColor);
                    if (Event.current.type == EventType.Repaint) renderedMarketCandleCount++;
                }
            if (showMarketCloseLine)
            {
                Handles.BeginGUI();
                Handles.color = new Color(.30f, .82f, .94f);
                // Only join consecutive revealed days; a visible fragment must never point toward a hidden price.
                for (int day = 0; day <= 30; day++)
                {
                    if (IsMarketDayHidden(sample.CompanyId, day)) continue;
                    int start = day;
                    while (day < 30 && !IsMarketDayHidden(sample.CompanyId, day + 1)) day++;
                    int count = day - start + 1;
                    if (count < 2) continue;
                    var visiblePoints = new Vector3[count];
                    Array.Copy(points, start, visiblePoints, 0, count);
                    Handles.DrawAAPolyLine(2, visiblePoints);
                    if (Event.current.type == EventType.Repaint) renderedCloseLinePointCount += count;
                }
                Handles.EndGUI();
            }
            float DayEdge(int day) => Mathf.Round(day / 31f * rect.width * EditorGUIUtility.pixelsPerPoint) / EditorGUIUtility.pixelsPerPoint;
            for (int day = 1; day <= 30; day++)
            {
                if (!IsMarketDayHidden(sample.CompanyId, day)) continue;
                int start = day;
                while (day < 30 && IsMarketDayHidden(sample.CompanyId, day + 1)) day++;
                // Paint adjacent hidden days as one solid, pixel-aligned span to avoid fractional-width seams.
                var fog = new Rect(DayEdge(start), rect.y, DayEdge(day + 1) - DayEdge(start), rect.height);
                EditorGUI.DrawRect(fog, new Color(.22f, .25f, .30f, 1));
                EditorGUIUtility.AddCursorRect(fog, MouseCursor.Link);
            }
            // Keep the solid cover underneath; day boundaries are strokes, never gaps in the fog.
            float borderWidth = 1f / EditorGUIUtility.pixelsPerPoint;
            for (int day = 1; day <= 30; day++)
            {
                if (!IsMarketDayHidden(sample.CompanyId, day)) continue;
                var fog = new Rect(DayEdge(day), rect.y, DayEdge(day + 1) - DayEdge(day), rect.height);
                EditorGUI.DrawRect(new Rect(fog.x, fog.y, borderWidth, fog.height), new Color(.40f, .45f, .52f, 1));
                if (day == 30 || !IsMarketDayHidden(sample.CompanyId, day + 1))
                    EditorGUI.DrawRect(new Rect(fog.xMax - borderWidth, fog.y, borderWidth, fog.height), new Color(.40f, .45f, .52f, 1));
                if (fog.Contains(Event.current.mousePosition) && Event.current.type != EventType.MouseLeaveWindow)
                    EditorGUI.DrawRect(fog, new Color(.60f, .78f, 1f, .18f));
                GUI.Label(fog, new GUIContent("", $"{sample.CompanyId} · {day}일차 · 클릭하여 안개 없애기"), GUIStyle.none);
            }
            foreach (int day in new[] { 0, 10, 20, 30 })
                GUI.Label(new Rect(Mathf.Clamp(X(day) - 10, rect.x, rect.xMax - 30), rect.yMax - 17, 30, 16), day + "일", cardNote);
            GUI.EndGroup();
        }
    }
}
