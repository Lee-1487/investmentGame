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
        private Vector2 scroll;
        private int selected = -1;
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
        private void OnEnable() { titleContent = new GUIContent("패턴 실험실"); if (mode != 2) mode = 1; LoadAuthoringLibrary(); Rebuild(); }
        public void Configure(int newSeed, int newTurn, bool normalMarket = false)
        {
            seed = newSeed; turn = Math.Max(1, Math.Min(10, newTurn));
            mode = normalMarket ? 1 : 2; selected = -1; Rebuild(); Repaint();
        }
        private void Rebuild()
        {
            samples.Clear();
            if (mode == 1)
                foreach (string id in InvestmentRules.CompanyIds)
                {
                    var sample = PatternMarketGenerator.Inspect(seed, turn, id, library: authoringLibrary);
                    samples.Add(sample);
                }
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
                GUILayout.Label("캔들 + 종가 연결선", GUILayout.Width(135));
                highlight = GUILayout.Toggle(highlight, "패턴 위치 강조", GUILayout.Width(115));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("새 시드", GUILayout.Width(75))) { seed = RandomMarketGenerator.NewSeed(); GUI.changed = true; }
                if (GUILayout.Button("다시 생성", GUILayout.Width(85))) GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck()) { selected = -1; Rebuild(); }
            EditorGUILayout.HelpBox("일반 게임과 같은 시드·턴으로 생성한 전체 시장입니다. 카드를 누르면 크게 봅니다.", MessageType.Info);
            EditorGUILayout.LabelField("등록 패턴 + 기업별 하루 변동 한도 · 노이즈/추가 이벤트 없음 · 실제 게임과 동일한 가격 생성", EditorStyles.miniLabel);
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
                    if (Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition))
                    { selected = index == selected ? -1 : index; Event.current.Use(); Repaint(); }
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.MouseMove) Repaint();
        }
        private void DrawCard(Rect card, StockPatternSample sample, bool active)
        {
            if (Event.current.type == EventType.Repaint) renderedCardCount++;
            EditorGUI.DrawRect(card, active ? new Color(.16f, .28f, .40f) : new Color(.16f, .17f, .20f));
            var header = new Rect(card.x + 10, card.y + 6, card.width - 20, 20);
            GUI.Label(header, sample.CompanyId + "   " + (sample.CustomPatternName ?? PatternNames[(int)sample.Pattern]), cardTitle);
            GUI.Label(new Rect(header.x, header.y + 20, header.width, 18), (sample.IsBasePattern ? "공유 기본 패턴" : "기업별 사용자 패턴"), cardNote);
            Rect plot = new Rect(card.x + 10, card.y + 48, card.width - 20, card.height - 75);
            DrawPlot(plot, sample);
            string footer = sample.Outcome == StockPatternOutcome.Unstructured ? "형태 없는 추세·횡보 경로"
                : "주 패턴 배치: " + sample.StartDay + "~" + sample.EndDay + "일";
            GUI.Label(new Rect(card.x + 10, card.yMax - 20, card.width - 20, 18), footer, cardNote);
        }
        private void DrawPlot(Rect rect, StockPatternSample sample)
        {
            EditorGUI.DrawRect(rect, new Color(.065f, .085f, .12f));
            var range = ChartVerticalRange.FromVisibleValues(sample.DailyChangesBps);
            float X(int day) => rect.x + (day + .5f) / 31 * rect.width;
            float Y(int bps) => rect.yMax - (float)range.Fraction(bps) * rect.height;
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
                    int open = sample.DailyChangesBps[Math.Max(0, day - 1)], close = sample.DailyChangesBps[day];
                    float height = Math.Max(2, Math.Abs(Y(open) - Y(close)));
                    EditorGUI.DrawRect(new Rect(X(day) - rect.width / 31 * .31f, (Y(open) + Y(close)) / 2 - height / 2, rect.width / 31 * .62f, height),
                        close > open ? (Color)BarChartGraphic.UpColor : close < open ? (Color)BarChartGraphic.DownColor : (Color)BarChartGraphic.FlatColor);
                    if (Event.current.type == EventType.Repaint) renderedMarketCandleCount++;
                }
            Handles.BeginGUI();
            Handles.color = new Color(.30f, .82f, .94f);
            Handles.DrawAAPolyLine(2, points);
            if (Event.current.type == EventType.Repaint) renderedCloseLinePointCount += points.Length;
            Handles.EndGUI();
            foreach (int day in new[] { 0, 10, 20, 30 })
                GUI.Label(new Rect(Mathf.Clamp(X(day) - 10, rect.x, rect.xMax - 30), rect.yMax - 17, 30, 16), day + "일", cardNote);
        }
    }
}
