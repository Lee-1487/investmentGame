using System;
using System.Collections.Generic;
using System.IO;
using BlackWhale.Rules;
using BlackWhale.UI.Dev;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using UnityEditor;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    public sealed partial class PatternLabWindow
    {
        private const string LibraryPath = "Assets/Resources/StockPatternLibrary.json";
        [SerializeField] private StockPatternLibrary authoringLibrary = new StockPatternLibrary();
        [SerializeField] private CustomStockPattern draft = new CustomStockPattern();
        [SerializeField] private List<Vector2> drawingPoints = new List<Vector2>();
        [SerializeField] private List<int> connectedPoints = new List<int>();
        [SerializeField] private List<int> pointAdditionOrder = new List<int>();
        [SerializeField] private int authorCompany;
        private int dragPoint = -1;
        private Vector2 authorScroll, libraryScroll;
        private string authorMessage = "";
        [SerializeField] private bool showVolatility = true;
        private string volatilityMessage = "";
        private int previewCandleCount;
        private string probabilityMessage = "";
        private int CanvasDays => Math.Max(30, draft.startDay + draft.TotalDays);
        private int DrawingDay(Vector2 point) => Mathf.RoundToInt(point.x * CanvasDays);
        private Vector2 SnapDrawingPoint(Vector2 point)
            => new Vector2(Mathf.Round(Mathf.Clamp01(point.x) * CanvasDays) / CanvasDays, Mathf.Clamp(point.y, -1, 1));

        private void LoadAuthoringLibrary()
        {
            authoringLibrary = StockPatternLibraryResource.Load();
            if (draft.vertices.Count >= 2 && connectedPoints.Count == drawingPoints.Count) RestoreDrawing();
            Undo.undoRedoPerformed -= AuthoringUndo;
            Undo.undoRedoPerformed += AuthoringUndo;
        }
        private void OnDisable() { Undo.undoRedoPerformed -= AuthoringUndo; }
        private void AuthoringUndo() { dragPoint = -1; Repaint(); }

        private void DrawVolatilitySettings()
        {
            showVolatility = EditorGUILayout.Foldout(showVolatility, "기업별 하루 변동 한도 · 모든 턴 동일", true);
            if (!showVolatility) return;
            Undo.RecordObject(this, "기업별 하루 변동 한도 편집");
            if (authoringLibrary.volatility == null) authoringLibrary.volatility = new StockVolatilitySettings();
            string[] companies = { "BANK", "FOOD", "AUTO", "AI", "BIO", "SPACE" };
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 92;
            EditorGUI.BeginChangeCheck();
            try
            {
                for (int row = 0; row < 2; row++)
                    using (new EditorGUILayout.HorizontalScope())
                        for (int column = 0; column < 3; column++)
                        {
                            string company = companies[row * 3 + column];
                            float value = EditorGUILayout.FloatField(company + " ± (%)", authoringLibrary.volatility.DailyLimitBps(company) / 100f);
                            if (!float.IsNaN(value) && !float.IsInfinity(value))
                                authoringLibrary.volatility.SetDailyLimitBps(company, Mathf.RoundToInt(Mathf.Clamp(value, .01f, 100) * 100));
                        }
            }
            finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
            if (EditorGUI.EndChangeCheck())
            { Rebuild(); Repaint(); volatilityMessage = "변경한 한도로 미리보기 중입니다. 저장하면 새 게임부터 적용됩니다."; GUIUtility.ExitGUI(); }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("하루 시가→종가 최대 변화폭 · 턴 배율/노이즈/추가 이벤트 없음", EditorStyles.miniLabel);
                if (GUILayout.Button("변동 한도 저장", GUILayout.Width(115)))
                {
                    volatilityMessage = WriteLibrary() ? "변동 한도 저장 완료 · 새 게임부터 적용됩니다." : authorMessage;
                    Repaint(); GUIUtility.ExitGUI();
                }
            }
            if (!string.IsNullOrEmpty(volatilityMessage)) EditorGUILayout.LabelField(volatilityMessage, EditorStyles.miniLabel);
        }

        private void NewDrawing(int preset = -1)
        {
            string company = InvestmentRules.CompanyIds[authorCompany];
            pointAdditionOrder.Clear();
            draft = preset < 0 ? new CustomStockPattern { companyId = company } : CustomStockPattern.Preset(company, preset);
            draft.isBasePattern = preset >= 0;
            RestoreDrawing();
            authorMessage = "";
        }

        private void SelectAuthorCompany(int company)
        {
            authorCompany = Math.Max(0, Math.Min(InvestmentRules.CompanyIds.Length - 1, company));
            draft.companyId = InvestmentRules.CompanyIds[authorCompany];
            probabilityMessage = "";
        }

        private void ShowBasePatternMenu()
        {
            var menu = new GenericMenu();
            string[] names = { "M", "W", "FLAG", "박스권" };
            for (int i = 0; i < names.Length; i++)
            {
                int preset = i;
                menu.AddItem(new GUIContent(names[i]), false, () =>
                {
                    Undo.RecordObject(this, "기본 패턴 추가");
                    NewDrawing(preset); Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void DrawPatternProbabilities()
        {
            string company = InvestmentRules.CompanyIds[authorCompany];
            var available = authoringLibrary.AvailablePatterns(company);
            var probabilities = authoringLibrary.Probabilities(company);
            int baseCount = authoringLibrary.BasePatternCount(company);
            EditorGUILayout.LabelField("기본 패턴 (" + baseCount + "개)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("모든 기업이 공유하는 등록 패턴", EditorStyles.miniLabel);
            DrawSavedPatternGroup(true, company, available, probabilities);
            EditorGUILayout.LabelField(baseCount > 0 ? "각 기본 확률 = (100 − 사용자 확률 합계) ÷ " + baseCount : "등록된 기본 패턴 없음", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("사용자 패턴", EditorStyles.boldLabel);
            if (!authoringLibrary.patterns.Exists(p => p.companyId == company && !p.isBasePattern))
                EditorGUILayout.LabelField("저장된 사용자 패턴 없음", EditorStyles.miniLabel);
            DrawSavedPatternGroup(false, company, available, probabilities);
            EditorGUILayout.LabelField("사용자 확률을 지정하면 나머지는 기본 패턴과 ‘자동’ 사용자 패턴에 균등 배분합니다.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("기업별 확률 저장"))
            {
                probabilityMessage = WriteLibrary() ? "확률 저장 완료 · 새 게임부터 적용됩니다." : authorMessage;
                Repaint(); GUIUtility.ExitGUI();
            }
            if (!string.IsNullOrEmpty(probabilityMessage)) EditorGUILayout.LabelField(probabilityMessage, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSavedPatternGroup(bool basic, string company, List<CustomStockPattern> available, double[] probabilities)
        {
            foreach (var pattern in authoringLibrary.patterns)
            {
                if (pattern.isBasePattern != basic || (!basic && pattern.companyId != company)) continue;
                int index = available.IndexOf(pattern);
                double effective = index >= 0 ? probabilities[index] : 0;
                if (GUILayout.Button(pattern.name + " · " + effective.ToString("0.##") + "%", GUILayout.Height(28)))
                { pointAdditionOrder.Clear(); draft = pattern.Copy(); RestoreDrawing(); authorMessage = "저장된 패턴을 불러왔습니다."; Repaint(); GUIUtility.ExitGUI(); }
                if (index < 0)
                { EditorGUILayout.LabelField("후보 제외 (비활성 또는 30일 초과)", EditorStyles.miniLabel); continue; }
                if (basic) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool specified = EditorGUILayout.ToggleLeft("확률 지정", pattern.useCustomProbability, GUILayout.Width(85));
                    if (specified != pattern.useCustomProbability)
                    {
                        pattern.useCustomProbability = specified;
                        if (specified) pattern.probabilityPercent = effective;
                        Repaint(); GUIUtility.ExitGUI();
                    }
                    if (specified)
                    {
                        double other = 0;
                        foreach (var candidate in available)
                            if (candidate != pattern && !candidate.isBasePattern && candidate.useCustomProbability) other += candidate.probabilityPercent;
                        double value = EditorGUILayout.DoubleField(pattern.probabilityPercent);
                        if (!double.IsNaN(value) && !double.IsInfinity(value))
                        {
                            value = Math.Max(0, Math.Min(Math.Max(0, 100 - other), value));
                            if (value != pattern.probabilityPercent)
                            { pattern.probabilityPercent = value; Repaint(); GUIUtility.ExitGUI(); }
                        }
                        GUILayout.Label("%", GUILayout.Width(16));
                    }
                    else GUILayout.Label("자동 " + effective.ToString("0.##") + "%", EditorStyles.miniLabel);
                }
            }
        }

        private void RestoreDrawing()
        {
            var restoredOrder = new List<int>();
            foreach (int index in pointAdditionOrder)
            {
                int order = connectedPoints.IndexOf(index);
                if (order >= 0 && order < draft.vertices.Count) restoredOrder.Add(order);
            }
            drawingPoints.Clear(); connectedPoints.Clear(); pointAdditionOrder.Clear(); dragPoint = -1;
            if (draft.vertices.Count == 0) return;
            double low = draft.vertices[0], high = low;
            foreach (double value in draft.vertices) { low = Math.Min(low, value); high = Math.Max(high, value); }
            int day = 0;
            for (int i = 0; i < draft.vertices.Count; i++)
            {
                if (i > 0) day += draft.segmentDays[i - 1];
                float x = (float)(draft.startDay + day) / CanvasDays;
                float y = high > low ? (float)((draft.vertices[i] - low) / (high - low) * 1.6 - .8) : 0;
                drawingPoints.Add(new Vector2(x, y)); connectedPoints.Add(i);
            }
            if (restoredOrder.Count == drawingPoints.Count) pointAdditionOrder.AddRange(restoredOrder);
            else for (int i = 0; i < drawingPoints.Count; i++) pointAdditionOrder.Add(i);
        }

        private void ResetGraph()
        {
            drawingPoints.Clear(); connectedPoints.Clear(); pointAdditionOrder.Clear();
            draft.vertices.Clear(); draft.segmentDays.Clear(); draft.startDay = 0;
            dragPoint = -1; authorMessage = "";
        }

        private void DeleteLastPoint()
        {
            if (connectedPoints.Count > 0) RemoveDrawingPoint(connectedPoints[connectedPoints.Count - 1]);
        }

        private void DeleteLastAddedPoint()
        {
            if (pointAdditionOrder.Count > 0) RemoveDrawingPoint(pointAdditionOrder[pointAdditionOrder.Count - 1]);
            else if (drawingPoints.Count > 0) RemoveDrawingPoint(drawingPoints.Count - 1);
        }

        private void RemoveDrawingPoint(int index)
        {
            connectedPoints.Remove(index); pointAdditionOrder.Remove(index); drawingPoints.RemoveAt(index);
            for (int i = 0; i < connectedPoints.Count; i++) if (connectedPoints[i] > index) connectedPoints[i]--;
            for (int i = 0; i < pointAdditionOrder.Count; i++) if (pointAdditionOrder[i] > index) pointAdditionOrder[i]--;
            dragPoint = -1; authorMessage = "";
            SynchronizeCurve();
            if (connectedPoints.Count == 0) draft.startDay = 0;
        }

        private void SynchronizeCurve()
        {
            int axisDays = CanvasDays;
            draft.vertices.Clear(); draft.segmentDays.Clear();
            if (connectedPoints.Count > 0) draft.startDay = Mathf.RoundToInt(drawingPoints[connectedPoints[0]].x * axisDays);
            foreach (int index in connectedPoints) draft.vertices.Add(drawingPoints[index].y);
            for (int i = 1; i < connectedPoints.Count; i++)
                draft.segmentDays.Add(Math.Max(1, Mathf.RoundToInt(drawingPoints[connectedPoints[i]].x * axisDays) - Mathf.RoundToInt(drawingPoints[connectedPoints[i - 1]].x * axisDays)));
            if (CanvasDays != axisDays)
                for (int i = 0; i < drawingPoints.Count; i++)
                    drawingPoints[i] = new Vector2(drawingPoints[i].x * axisDays / CanvasDays, drawingPoints[i].y);
        }

        private void DrawAuthoring()
        {
            Undo.RecordObject(this, "주가 패턴 편집");
            using (new EditorGUILayout.HorizontalScope())
            {
                int company = EditorGUILayout.Popup("기업", authorCompany, InvestmentRules.CompanyIds, GUILayout.Width(270));
                if (company != authorCompany) { SelectAuthorCompany(company); Repaint(); GUIUtility.ExitGUI(); }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.HelpBox("1칸 = 1일. 빈 곳을 왼쪽 클릭하면 날짜 순서로 점을 추가·연결합니다. 구간 사이를 클릭하면 기존 선분이 새 점을 거치도록 나뉩니다. 기존 점은 드래그로 이동합니다. 기업을 바꿔도 그림은 유지됩니다. 기본 패턴은 모든 기업에, 사용자 패턴은 선택 기업에 반영됩니다.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
                {
                    EditorGUILayout.LabelField("추가하기", EditorStyles.boldLabel);
                    if (GUILayout.Button("빈 그래프 · 사용자 패턴 추가", GUILayout.Height(30)))
                    { NewDrawing(); Repaint(); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button("기본 패턴 추가", GUILayout.Height(28))) ShowBasePatternMenu();
                    EditorGUILayout.Space();
                    libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
                    DrawPatternProbabilities();
                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.LabelField("기본 패턴은 모든 기업이 공유합니다. 사용자 패턴은 선택 기업에만 저장됩니다. 새 게임부터 적용됩니다.", EditorStyles.wordWrappedMiniLabel);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    authorScroll = EditorGUILayout.BeginScrollView(authorScroll);
                    draft.companyId = InvestmentRules.CompanyIds[authorCompany];
                    EditorGUILayout.LabelField("패턴 종류", draft.isBasePattern ? "기본 패턴 · 모든 기업 공유" : "사용자 패턴 · 선택 기업 전용");
                    draft.name = EditorGUILayout.TextField("패턴 이름", draft.name);
                    draft.enabled = EditorGUILayout.Toggle("게임 후보에 포함", draft.enabled);
                    Rect canvas = GUILayoutUtility.GetRect(200, 280, GUILayout.ExpandWidth(true));
                    DrawAuthorCanvas(canvas);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("그래프 초기화")) { ResetGraph(); Repaint(); GUIUtility.ExitGUI(); }
                        using (new EditorGUI.DisabledScope(drawingPoints.Count == 0))
                        {
                            if (GUILayout.Button("마지막 점 삭제")) { DeleteLastPoint(); Repaint(); GUIUtility.ExitGUI(); }
                            if (GUILayout.Button("방금 추가한 점 삭제")) { DeleteLastAddedPoint(); Repaint(); GUIUtility.ExitGUI(); }
                        }
                    }
                    string firstDay = connectedPoints.Count > 0 ? draft.startDay + "일" : "—";
                    string lastDay = connectedPoints.Count > 0 ? (draft.startDay + draft.TotalDays) + "일" : "—";
                    EditorGUILayout.LabelField("점 " + drawingPoints.Count + "개 · 시작 " + firstDay + " · 종료 " + lastDay);
                    Rect preview = GUILayoutUtility.GetRect(100, 160, GUILayout.ExpandWidth(true));
                    DrawAuthoredPreview(preview);
                    if (draft.startDay + draft.TotalDays > 30)
                        EditorGUILayout.HelpBox("마지막 점이 30일을 초과하면 저장·미리보기만 가능하며 게임 후보에서 제외됩니다.", MessageType.Warning);
                    bool connected = draft.vertices.Count >= 2 && connectedPoints.Count == drawingPoints.Count;
                    using (new EditorGUI.DisabledScope(!connected || string.IsNullOrWhiteSpace(draft.name)))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(draft.isBasePattern ? "저장 · 모든 기업 반영" : "저장 · 게임 후보 반영", GUILayout.Height(30))) { SaveDraft(false); Repaint(); GUIUtility.ExitGUI(); }
                        if (GUILayout.Button("복사본으로 저장", GUILayout.Height(30))) { SaveDraft(true); Repaint(); GUIUtility.ExitGUI(); }
                    }
                    using (new EditorGUI.DisabledScope(!authoringLibrary.patterns.Exists(p => p.id == draft.id && (p.isBasePattern || p.companyId == InvestmentRules.CompanyIds[authorCompany]))))
                        if (GUILayout.Button("저장 목록에서 삭제"))
                        {
                            var previous = authoringLibrary.Copy();
                            authoringLibrary.patterns.RemoveAll(p => p.id == draft.id && (p.isBasePattern || p.companyId == InvestmentRules.CompanyIds[authorCompany]));
                            if (WriteLibrary()) { draft.id = ""; authorMessage = "저장 목록에서 삭제했습니다. 현재 그림은 유지됩니다."; }
                            else authoringLibrary = previous;
                            Repaint(); GUIUtility.ExitGUI();
                        }
                    if (!connected) EditorGUILayout.HelpBox("저장하려면 모든 점을 하나의 꺾은선으로 연결하세요 (최소 2점).", MessageType.None);
                    if (!string.IsNullOrEmpty(authorMessage)) EditorGUILayout.HelpBox(authorMessage, MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void SaveDraft(bool duplicate)
        {
            var previous = authoringLibrary.Copy();
            try
            {
                var saved = PrepareSavedDraft(duplicate);
                int index = authoringLibrary.patterns.FindIndex(p => p.id == saved.id);
                if (index < 0) authoringLibrary.patterns.Add(saved); else authoringLibrary.patterns[index] = saved;
                if (WriteLibrary()) { draft = saved.Copy(); authorMessage = "저장 완료 · " + LibraryPath; }
                else authoringLibrary = previous;
            }
            catch (Exception exception) { authoringLibrary = previous; authorMessage = exception.Message; }
        }

        private CustomStockPattern PrepareSavedDraft(bool duplicate)
        {
            if (string.IsNullOrWhiteSpace(draft.name)) throw new ArgumentException("패턴 이름을 입력하세요.");
            var saved = draft.Copy();
            saved.companyId = saved.isBasePattern ? "AI" : InvestmentRules.CompanyIds[authorCompany];
            saved.name = saved.name.Trim(); saved.Validate();
            var existing = authoringLibrary.patterns.Find(p => p.id == saved.id && (p.isBasePattern || p.companyId == saved.companyId));
            if (duplicate || existing == null)
            { saved.id = Guid.NewGuid().ToString("N"); saved.useCustomProbability = false; saved.probabilityPercent = 0; }
            else
            { saved.useCustomProbability = existing.useCustomProbability; saved.probabilityPercent = existing.probabilityPercent; }
            if (saved.isBasePattern) { saved.useCustomProbability = false; saved.probabilityPercent = 0; }
            return saved;
        }

        private bool WriteLibrary()
        {
            try
            {
                authoringLibrary.Copy(); // Validate every company before persisting probability changes.
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                string json = JsonUtility.ToJson(authoringLibrary, true);
                File.WriteAllText(LibraryPath, json, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(LibraryPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception) { authorMessage = "저장 실패: " + exception.Message; return false; }
        }

        private void DrawAuthorCanvas(Rect outer)
        {
            EditorGUI.DrawRect(outer, new Color(.06f, .08f, .12f));
            Rect rect = new Rect(outer.x + 24, outer.y + 24, outer.width - 48, outer.height - 70);
            Vector2 Screen(Vector2 p) => new Vector2(rect.x + p.x * rect.width, rect.center.y - p.y * rect.height / 2);
            Vector2 Local(Vector2 p) => SnapDrawingPoint(new Vector2((p.x - rect.x) / rect.width, (rect.center.y - p.y) * 2 / rect.height));
            Handles.BeginGUI();
            Handles.color = new Color(.25f, .3f, .4f);
            int tickStep = Math.Max(1, Mathf.CeilToInt(CanvasDays / 120f));
            for (int day = 0; day <= CanvasDays; day += tickStep)
            {
                Handles.color = day % 5 == 0 ? new Color(.4f, .48f, .6f) : new Color(.22f, .27f, .35f);
                float x = rect.x + rect.width * day / CanvasDays;
                Handles.DrawLine(new Vector2(x, rect.y), new Vector2(x, rect.yMax));
            }
            Handles.DrawLine(new Vector2(rect.x, rect.center.y), new Vector2(rect.xMax, rect.center.y));
            Handles.color = new Color(.25f, .85f, .95f);
            for (int i = 1; i < connectedPoints.Count; i++) Handles.DrawAAPolyLine(3, Screen(drawingPoints[connectedPoints[i - 1]]), Screen(drawingPoints[connectedPoints[i]]));
            Handles.EndGUI();
            int labelStep = CanvasDays <= 30 ? 1 : Math.Max(5, Mathf.CeilToInt(CanvasDays / 30f));
            for (int day = 0; day <= CanvasDays; day += labelStep)
            {
                float x = rect.x + rect.width * day / CanvasDays;
                GUI.Label(new Rect(x - 12, rect.yMax + 3, 30, 18), day.ToString(), EditorStyles.whiteMiniLabel);
            }
            for (int i = 1; i < connectedPoints.Count; i++)
            {
                Vector2 a = Screen(drawingPoints[connectedPoints[i - 1]]), b = Screen(drawingPoints[connectedPoints[i]]);
                int days = DrawingDay(drawingPoints[connectedPoints[i]]) - DrawingDay(drawingPoints[connectedPoints[i - 1]]);
                GUI.Label(new Rect((a.x + b.x) / 2 - 20, (a.y + b.y) / 2 + 5, 55, 18), days + "일 간격", EditorStyles.whiteMiniLabel);
            }
            for (int i = 0; i < drawingPoints.Count; i++)
            {
                Vector2 p = Screen(drawingPoints[i]);
                EditorGUI.DrawRect(new Rect(p.x - 4, p.y - 4, 8, 8), Color.cyan);
                int order = connectedPoints.IndexOf(i);
                string label = (order >= 0 ? "점 " + (order + 1) : "미연결") + " · " + DrawingDay(drawingPoints[i]) + "일";
                GUI.Label(new Rect(Mathf.Clamp(p.x - 20, outer.x, outer.xMax - 95), p.y - 21, 95, 20), label, EditorStyles.whiteMiniLabel);
            }
            GUI.Label(new Rect(rect.x, outer.yMax - 20, rect.width, 20), "날짜 →  1칸=1일 · 왼쪽 클릭: 점 추가·자동 연결 · Ctrl+Z: 실행 취소", EditorStyles.whiteMiniLabel);
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape || e.type == EventType.MouseDown && e.button == 1 && outer.Contains(e.mousePosition))
            { dragPoint = -1; e.Use(); Repaint(); return; }
            int hit = -1;
            for (int i = 0; i < drawingPoints.Count; i++) if (Vector2.Distance(Screen(drawingPoints[i]), e.mousePosition) <= 10) { hit = i; break; }
            if (e.type == EventType.MouseDown && e.button == 0 && (rect.Contains(e.mousePosition) || hit >= 0 && outer.Contains(e.mousePosition)))
            {
                int previousVertexCount = draft.vertices.Count;
                GUIUtility.keyboardControl = 0;
                if (hit >= 0) dragPoint = hit;
                else CompleteDrawingClick(hit, Local(e.mousePosition));
                e.Use(); Repaint();
                if (draft.vertices.Count != previousVertexCount) GUIUtility.ExitGUI();
            }
            else if (e.type == EventType.MouseDrag && dragPoint >= 0)
            {
                Vector2 point = Local(e.mousePosition);
                int order = connectedPoints.IndexOf(dragPoint);
                float min = order > 0 ? drawingPoints[connectedPoints[order - 1]].x + 1f / CanvasDays : 0;
                float max = order >= 0 && order < connectedPoints.Count - 1 ? drawingPoints[connectedPoints[order + 1]].x - 1f / CanvasDays : 1;
                point.x = Mathf.Clamp(point.x, min, max);
                drawingPoints[dragPoint] = point; SynchronizeCurve(); e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && dragPoint >= 0)
            {
                dragPoint = -1; e.Use(); Repaint();
            }
            if (e.type == EventType.MouseMove) Repaint();
        }

        private void CompleteDrawingClick(int hit, Vector2 point)
        {
            if (hit >= 0) return; // Existing vertices are moved by dragging; clicks never duplicate them.
            point = SnapDrawingPoint(point);
            int insertion = 0;
            while (insertion < connectedPoints.Count && DrawingDay(drawingPoints[connectedPoints[insertion]]) < DrawingDay(point)) insertion++;
            if (insertion < connectedPoints.Count && DrawingDay(drawingPoints[connectedPoints[insertion]]) == DrawingDay(point))
            { authorMessage = "해당 날짜에는 이미 점이 있습니다. 기존 점을 드래그로 이동하세요."; return; }
            int index = drawingPoints.Count;
            drawingPoints.Add(point); connectedPoints.Insert(insertion, index); pointAdditionOrder.Add(index);
            SynchronizeCurve();
            authorMessage = connectedPoints.Count == 1 ? "첫 점 추가 · 다른 날짜를 클릭하면 자동으로 연결됩니다." : "날짜 순서로 점을 추가하고 선분을 연결했습니다.";
        }

        private void DrawAuthoredPreview(Rect rect)
        {
            if (draft.vertices.Count < 2)
            {
                if (Event.current.type == EventType.Repaint) previewCandleCount = 0;
                EditorGUI.DrawRect(rect, new Color(.06f, .08f, .12f));
                GUI.Label(new Rect(rect.x + 12, rect.y + 12, rect.width - 24, 22), "점을 2개 이상 추가하면 일봉 미리보기가 표시됩니다.", EditorStyles.whiteMiniLabel);
                return;
            }
            var values = draft.GenerateTimeline(InvestmentRules.CompanyIds[authorCompany], turn, authoringLibrary.volatility, out double scale);
            var bars = DailyBarModel.FromDailyChanges(values);
            if (Event.current.type == EventType.Repaint) previewCandleCount = bars.Length;
            var range = ChartVerticalRange.FromVisibleValues(values);
            EditorGUI.DrawRect(rect, new Color(.06f, .08f, .12f));
            Rect plot = new Rect(rect.x + 50, rect.y + 8, rect.width - 58, rect.height - 32);
            float Y(int value) => plot.yMax - (float)range.Fraction(value) * plot.height;
            float slot = plot.width / bars.Length;
            float width = Math.Max(1, slot * .62f);
            Handles.BeginGUI();
            Handles.color = new Color(.35f, .4f, .47f);
            Handles.DrawDottedLine(new Vector2(plot.x, Y(0)), new Vector2(plot.xMax, Y(0)), 4);
            Handles.EndGUI();
            foreach (var bar in bars)
            {
                float x = plot.x + (bar.Day + .5f) * slot;
                float openY = Y(bar.OpenBps.Value), closeY = Y(bar.Bps.Value);
                Color color = bar.Move == CandleMove.Rise ? BarChartGraphic.UpColor : bar.Move == CandleMove.Fall ? BarChartGraphic.DownColor : BarChartGraphic.FlatColor;
                float height = Math.Max(2, Math.Abs(openY - closeY));
                EditorGUI.DrawRect(new Rect(x - width / 2, (openY + closeY) / 2 - height / 2, width, height), color);
            }
            foreach (float fraction in new[] { 0f, .5f, 1f })
            {
                double value = range.MinBps + range.Span * fraction;
                GUI.Label(new Rect(rect.x + 3, plot.yMax - fraction * plot.height - 8, 48, 18), (value / 100).ToString("0.##") + "%", EditorStyles.whiteMiniLabel);
            }
            for (int day = 0; day <= CanvasDays; day += Math.Max(1, Mathf.CeilToInt(CanvasDays / 15f)))
                GUI.Label(new Rect(plot.x + (day + .5f) * slot - 10, plot.yMax + 3, 40, 18), day + "일", EditorStyles.whiteMiniLabel);
            EditorGUILayout.LabelField("실제 게임과 동일한 가격 데이터 · 빨강: 양봉 · 파랑: 음봉 · 적용 배율 " + scale.ToString("P1"), EditorStyles.miniLabel);
        }
    }
}
