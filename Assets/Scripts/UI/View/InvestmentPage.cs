// 매수 창: 단계에 따라 내용이 바뀐다.
// - 정보 탐색: 안내 + 기업별 남은 자리
// - 기업 선택: 내 차례 안내, 기업 6개 선택 버튼(남은 자리·내 선택 표시)
// - 투자 결정: 비율(5:5/6:4/7:3)·순서·구간(정수 일, 5~10일, 최대 2개) 입력 후 주문 확정
// - 시장 공개/결과: 확정 주문과 결과 표시
// 타인의 투자 기업·금액·구간은 표시하지 않는다(남은 자리와 현재 차례만).
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class InvestmentPage
    {
        private readonly GameClientController _c;
        private readonly Text _status;
        private readonly RectTransform _body;

        private InvestmentDraft _draft;
        private int _draftTurn = -1;

        // 날짜 입력 중에는 화면 전체를 다시 그리지 않고 아래 요소만 바로 갱신한다
        // (전체를 다시 그리면 입력 직후 '주문 확정' 첫 클릭이 사라질 수 있음).
        private Text _orderMessage;
        private Button _submitButton;
        private readonly Dictionary<string, LineChartGraphic> _orderCharts = new Dictionary<string, LineChartGraphic>();
        private readonly List<KeyValuePair<Interval, Text>> _holdLabels = new List<KeyValuePair<Interval, Text>>();

        public InvestmentPage(RectTransform page, GameClientController controller)
        {
            _c = controller;

            var title = UiKit.Label(page, "매수 창", 18, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -2), new Vector2(200, 28));

            _status = UiKit.Label(page, "", 14, Palette.SubText, TextAnchor.MiddleRight);
            UiKit.Place(_status.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -2), new Vector2(640, 28));

            _body = UiKit.Rect(page, "Body");
            UiKit.Stretch(_body, 0, 34, 0, 0);
        }

        public void Refresh()
        {
            UiKit.Clear(_body);
            var s = _c.Store.Public;
            if (s == null)
            {
                Message(_body, "게임 상태를 기다리는 중입니다.");
                return;
            }

            string mine = _c.Store.OwnSelections.Count == 0 ? "없음" : string.Join(", ", _c.Store.OwnSelections);
            _status.text = s.turn + "턴 · " + PhaseNames.Korean(s.phase) + "   ·   내 선택: " + mine;

            if (!string.IsNullOrEmpty(_c.Store.EndReason) || s.phase == Protocol.Ended)
            {
                BuildEnded();
                return;
            }

            switch (s.phase)
            {
                case Protocol.Lobby:
                    BuildLobby(s);
                    break;
                case Protocol.Info:
                    BuildSelectionGrid(false, "지금은 정보 탐색 단계입니다. 오른쪽 가장자리를 눌러 정보 구매 창에서 정보를 확인하세요. 다음 단계에서 기업 2개를 고릅니다.");
                    break;
                case Protocol.Selection:
                    BuildSelectionGrid(true, SelectionHint(s));
                    break;
                case Protocol.Investment:
                    if (_c.Store.OrderLocked) BuildOrderSummary("주문이 확정되었습니다. 4명이 모두 확정하면 바로 시장 공개로 넘어갑니다.");
                    else BuildOrderForm();
                    break;
                case Protocol.Reveal:
                    BuildOrderSummary("시장 공개 중입니다. 주문은 잠겨 있습니다. 정보 구매 창에서 전체 그래프를 확인할 수 있습니다.");
                    break;
                case Protocol.Result:
                    BuildResult();
                    break;
                default:
                    Message(_body, PhaseNames.Korean(s.phase) + " 단계입니다.");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 대기실·종료
        // ------------------------------------------------------------------

        private void BuildLobby(PublicState s)
        {
            _status.text = "대기실 · 4명이 모두 준비하면 방장이 시작합니다";
            var card = UiKit.Panel(_body, "Lobby", Palette.Card, rounded: true);
            UiKit.Stretch(card.rectTransform, 120, 20, 120, 20);
            var root = card.rectTransform;

            var title = UiKit.Label(root, "<b>참가자</b>  <size=12>" + (s.players?.Length ?? 0) + " / 4명</size>", 18, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(title.rectTransform, 24, 18, 24, 0);

            var players = s.players ?? new Player[0];
            for (int i = 0; i < 4; i++)
            {
                var p = i < players.Length ? players[i] : null;
                var row = UiKit.Panel(root, "Player" + (i + 1), p == null ? Palette.Disabled : Palette.Screen, rounded: true);
                UiKit.Place(row.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60 - i * 54), new Vector2(520, 46));
                string text = p == null
                    ? "<color=#9CA3AF>빈 자리 · 접속 대기</color>"
                    : "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(p.playerId)) + ">■</color>  <b>" + p.playerId + "</b>  "
                      + (p.displayName ?? "") + (p.playerId == _c.LocalPlayerId ? " (나)" : "") + (p.playerId == "P1" ? "  <size=11>방장</size>" : "")
                      + "      " + (!p.connected ? "<color=#DC2626>연결 끊김</color>" : p.ready ? "<color=#047857><b>준비 완료</b></color>" : "<color=#6B7280>준비 전</color>");
                var label = UiKit.Label(row.transform, text, 15, Palette.Text, TextAnchor.MiddleLeft);
                UiKit.Stretch(label.rectTransform, 16, 0, 16, 0);
            }

            bool ready = _c.IsLocalReady;
            bool readyPending = _c.Store.HasPending(CommandTypes.SetReady);
            var readyButton = UiKit.Button(root, readyPending ? "처리 중…" : ready ? "준비 취소" : "준비", 16,
                ready ? Palette.Card : Palette.Accent, ready ? Palette.Text : Color.white, () => _c.SetReady(!ready));
            UiKit.Place((RectTransform)readyButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(_c.IsHost ? -100 : 0, 24), new Vector2(180, 46));
            readyButton.interactable = !readyPending;

            if (_c.IsHost)
            {
                bool can = _c.AllPlayersReady && !_c.Store.HasPending(CommandTypes.StartGame);
                var start = UiKit.Button(root, "게임 시작", 16, can ? Palette.Success : Palette.Disabled, can ? Color.white : Palette.DisabledText,
                    () => _c.StartGame());
                UiKit.Place((RectTransform)start.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(100, 24), new Vector2(180, 46));
                start.interactable = can;
            }
        }

        private void BuildEnded()
        {
            string reason = _c.Store.EndReason;
            string text = "<b>게임이 종료되었습니다.</b>\n\n" + (string.IsNullOrEmpty(reason) ? "" : ErrorMessages.ToKorean(reason));
            if (_c.Store.Scoreboard != null) text += "\n\n왼쪽 순위 칸에 마우스를 올려 결과를 확인할 수 있습니다.";
            Message(_body, text);
        }

        // ------------------------------------------------------------------
        // 기업 선택
        // ------------------------------------------------------------------

        private string SelectionHint(PublicState s)
        {
            if (_c.IsMyTurnToSelect())
                return "<color=#DC2626><b>내 차례입니다.</b></color> 기업을 하나 고르세요. (" + (_c.Store.OwnSelections.Count + 1) + "번째 / 2개)";
            if (string.IsNullOrEmpty(s.currentPlayerId)) return "기업 선택 진행 중";
            return s.currentPlayerId + " 님이 고르는 중입니다. 차례 순서: A→B→C→D→D→C→B→A";
        }

        private void BuildSelectionGrid(bool active, string hint)
        {
            var hintText = UiKit.Label(_body, hint, 15, Palette.Text, TextAnchor.MiddleLeft);
            UiKit.Place(hintText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -4), new Vector2(860, 40));
            hintText.rectTransform.anchorMax = new Vector2(1, 1);
            hintText.rectTransform.sizeDelta = new Vector2(-16, 40);

            var grid = UiKit.Rect(_body, "SelectGrid");
            UiKit.Stretch(grid, 0, 52, 0, 0);
            var ids = UiRules.CompanyIds;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                int col = i % 3, row = i / 3;
                var cell = UiKit.Rect(grid, "Pick_" + id);
                UiKit.Anchor(cell, col / 3f, row == 0 ? 0.5f : 0f, (col + 1) / 3f, row == 0 ? 1f : 0.5f, 6, 6);

                int seats = _c.RemainingSeats(id);
                bool own = _c.Store.OwnSelections.Contains(id);
                bool can = active && _c.CanSelect(id) && !_c.Store.HasPending(CommandTypes.SelectCompany);
                int known = _c.Store.View?.companies?.FirstOrDefault(c => c.companyId == id)?.visibleBundles?.Length ?? 0;

                string seatDots = new string('●', seats) + new string('○', UiRules.CompaniesPerOrder - seats);
                string state = own ? "내 선택" : seats <= 0 ? "자리 없음" : can ? "선택하기" : "남은 자리 " + seats;
                string label = "<size=22><b>" + id + "</b></size>\n<size=12>" + CompanyInfo.Volatility(id) + " · 열람 정보 " + known + "/6</size>\n"
                               + "<size=16>" + seatDots + "</size>\n<size=13>" + state + "</size>";

                Color bg = own ? Palette.Accent : can ? Palette.AccentSoft : seats <= 0 ? Palette.Disabled : Palette.Card;
                Color fg = own ? Color.white : seats <= 0 ? Palette.DisabledText : Palette.Text;
                var button = UiKit.Button(cell, label, 14, bg, fg, () => _c.SelectCompany(id));
                UiKit.Stretch((RectTransform)button.transform);
                button.interactable = can;
                if (!can) ((Image)button.targetGraphic).color = bg; // 비활성이어도 상태 색 유지
                var colors = button.colors;
                colors.disabledColor = Color.white;
                button.colors = colors;
            }
        }

        // ------------------------------------------------------------------
        // 주문 입력
        // ------------------------------------------------------------------

        private void EnsureDraft()
        {
            int turn = _c.Store.CurrentTurn;
            var sel = _c.Store.OwnSelections;
            bool same = _draft != null && _draftTurn == turn
                        && sel.Count == 2 && sel.Contains(_draft.FirstCompanyId) && sel.Contains(_draft.SecondCompanyId);
            if (same) return;

            _draft = InvestmentDraft.FromSelections(sel);
            _draftTurn = turn;
            if (_draft == null) return;
            _draft.IntervalsOf(_draft.FirstCompanyId).Add(new Interval { entryDay = 0, exitDay = 5 });
            _draft.IntervalsOf(_draft.SecondCompanyId).Add(new Interval { entryDay = 0, exitDay = 5 });
        }

        private void BuildOrderForm()
        {
            _orderCharts.Clear();
            _holdLabels.Clear();
            EnsureDraft();
            if (_draft == null)
            {
                Message(_body, "선택한 기업이 2개가 아닙니다. 서버의 자동 선택 결과를 기다리는 중입니다.");
                return;
            }

            // 비율·순서
            var top = UiKit.Rect(_body, "RatioRow");
            UiKit.Place(top, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -2), new Vector2(880, 36));
            var ratioLabel = UiKit.Label(top, "투자 비율 (첫 기업 : 둘째 기업)", 14, Palette.Text, TextAnchor.MiddleLeft);
            UiKit.Place(ratioLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(230, 32));
            int x = 236;
            foreach (int w in UiRules.FirstCompanyWeightOptionsBps)
            {
                int weight = w;
                bool on = _draft.FirstWeightBps == w;
                var b = UiKit.Button(top, (w / 1000) + ":" + ((10000 - w) / 1000), 15,
                    on ? Palette.Accent : Palette.Card, on ? Color.white : Palette.Text,
                    () => { _draft.SetFirstWeight(weight); Refresh(); });
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(64, 32));
                x += 70;
            }
            var swap = UiKit.Button(top, "순서 바꾸기", 13, Palette.Card, Palette.Text, () => { _draft.SwapCompanies(); Refresh(); });
            UiKit.Place((RectTransform)swap.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 10, 0), new Vector2(120, 32));

            // 두 기업 열
            var cols = UiKit.Rect(_body, "Columns");
            UiKit.Stretch(cols, 0, 44, 0, 56);
            BuildCompanyColumn(cols, 0, _draft.FirstCompanyId, _draft.FirstWeightBps);
            BuildCompanyColumn(cols, 1, _draft.SecondCompanyId, _draft.SecondWeightBps);

            // 하단: 오류 + 확정
            bool pending = _c.Store.HasPending(CommandTypes.SubmitInvestment);
            var bottom = UiKit.Rect(_body, "SubmitRow");
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.offsetMin = new Vector2(6, 4);
            bottom.offsetMax = new Vector2(-6, 48);

            _orderMessage = UiKit.Label(bottom, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Stretch(_orderMessage.rectTransform, 0, 0, 200, 0);

            _submitButton = UiKit.Button(bottom, pending ? "확정 중…" : "주문 확정", 16, Palette.Disabled, Palette.DisabledText,
                () => _c.SubmitInvestment(_draft.ToInvestments()));
            UiKit.Place((RectTransform)_submitButton.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(180, 42));
            UpdateOrderLive();
        }

        private void BuildCompanyColumn(RectTransform parent, int index, string companyId, int weightBps)
        {
            var cell = UiKit.Rect(parent, "Col_" + companyId);
            UiKit.Anchor(cell, index * 0.5f, 0, index * 0.5f + 0.5f, 1, 6, 0);
            var bg = UiKit.Panel(cell, "Bg", Palette.Card, rounded: true);
            UiKit.Stretch(bg.rectTransform);
            var root = bg.rectTransform;

            var header = UiKit.Label(root, (index + 1) + ". <b>" + companyId + "</b>   <color=#2563EB><b>" + DisplayFormat.Weight(weightBps) + "</b></color>"
                + "   <size=12>" + CompanyInfo.Volatility(companyId) + "</size>", 18, Palette.Text, TextAnchor.MiddleLeft);
            UiKit.Place(header.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -4), new Vector2(400, 30));

            // 내가 아는 값 + 고른 구간 표시
            var chartRt = UiKit.Rect(root, "Chart");
            chartRt.anchorMin = new Vector2(0, 1);
            chartRt.anchorMax = new Vector2(1, 1);
            chartRt.pivot = new Vector2(0.5f, 1);
            chartRt.offsetMin = new Vector2(14, -150);
            chartRt.offsetMax = new Vector2(-14, -38);
            var chart = chartRt.gameObject.AddComponent<LineChartGraphic>();
            _orderCharts[companyId] = chart;
            DrawOrderChart(chart, companyId);
            foreach (int day in new[] { 0, 10, 20, 30 })
            {
                var tick = UiKit.Label(chartRt, day + "일", 9, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2(day / 30f, 0), new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(40, 14));
            }

            // 구간 입력 행
            var intervals = _draft.IntervalsOf(companyId);
            float y = -176;
            for (int i = 0; i < intervals.Count; i++)
            {
                BuildIntervalRow(root, companyId, i, intervals[i], y);
                y -= 44;
            }

            if (intervals.Count < UiRules.MaxIntervalsPerCompany)
            {
                var add = UiKit.Button(root, "+ 구간 추가 (최대 2개)", 13, Palette.AccentSoft, Palette.Accent, () =>
                {
                    int entry = intervals.Count > 0 ? intervals[intervals.Count - 1].exitDay : 0;
                    if (entry > UiRules.MaxDay - UiRules.MinHoldingDays) entry = 0;
                    intervals.Add(new Interval { entryDay = entry, exitDay = entry + UiRules.MinHoldingDays });
                    Refresh();
                });
                UiKit.Place((RectTransform)add.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, y), new Vector2(200, 34));
            }
        }

        private void BuildIntervalRow(RectTransform root, string companyId, int index, Interval iv, float y)
        {
            var row = UiKit.Rect(root, "Interval" + index);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0.5f, 1);
            row.offsetMin = new Vector2(14, y - 36);
            row.offsetMax = new Vector2(-14, y);

            var label = UiKit.Label(row, "구간 " + (index + 1), 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(label.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(48, 34));

            DayInput(row, 56, "진입", () => iv.entryDay, v => iv.entryDay = v);
            DayInput(row, 190, "청산", () => iv.exitDay, v => iv.exitDay = v);

            var hold = UiKit.Label(row, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(hold.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(90, 34));
            _holdLabels.Add(new KeyValuePair<Interval, Text>(iv, hold));
            UpdateHoldLabel(iv, hold);

            var remove = UiKit.Button(row, "삭제", 12, Palette.Card, Palette.SubText, () =>
            {
                _draft.RemoveInterval(companyId, index);
                Refresh();
            });
            UiKit.Place((RectTransform)remove.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(48, 30));
        }

        /// <summary>날짜 직접 입력 칸(0~30 정수). 입력할 때마다 초안에 반영하고 검사 결과만 바로 갱신한다.
        /// 범위를 벗어난 값도 그대로 두고 오류 문구로 알려 준다(최종 판정은 서버).</summary>
        private void DayInput(RectTransform row, float x, string name, System.Func<int> get, System.Action<int> set)
        {
            var label = UiKit.Label(row, name, 13, Palette.Text, TextAnchor.MiddleLeft);
            UiKit.Place(label.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(34, 34));

            var field = UiKit.NumberInput(row, get().ToString(), 15);
            UiKit.Place((RectTransform)field.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 36, 0), new Vector2(54, 30));
            field.onValueChanged.AddListener(text =>
            {
                if (!int.TryParse(text, out var value)) return;
                set(value);
                UpdateOrderLive();
            });
            field.onEndEdit.AddListener(_ => field.text = get().ToString()); // 비우거나 잘못 입력하면 마지막 값으로 되돌림

            var unit = UiKit.Label(row, "일", 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(unit.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 94, 0), new Vector2(20, 34));
        }

        private void UpdateOrderLive()
        {
            if (_draft == null || _orderMessage == null || _submitButton == null) return;
            string error = _draft.Validate();
            bool pending = _c.Store.HasPending(CommandTypes.SubmitInvestment);
            bool can = error.Length == 0 && !pending;

            _orderMessage.text = error.Length > 0 ? ErrorMessages.ToKorean(error)
                : "날짜는 0~30일, 보유 5~10일. 확정 후에는 바꿀 수 없습니다. 시간 초과 시 50:50 · 각 0→5일로 자동 확정됩니다.";
            _orderMessage.color = error.Length > 0 ? Palette.Up : Palette.SubText;
            UiKit.SetButton(_submitButton, pending ? "확정 중…" : "주문 확정", can,
                can ? Palette.Accent : Palette.Disabled, can ? Color.white : Palette.DisabledText);

            foreach (var pair in _holdLabels) UpdateHoldLabel(pair.Key, pair.Value);
            foreach (var pair in _orderCharts) DrawOrderChart(pair.Value, pair.Key);
        }

        private static void UpdateHoldLabel(Interval iv, Text hold)
        {
            int holding = iv.exitDay - iv.entryDay;
            bool ok = holding >= UiRules.MinHoldingDays && holding <= UiRules.MaxHoldingDays;
            hold.text = "보유 " + holding + "일";
            hold.color = ok ? Palette.SubText : Palette.Up;
        }

        private void DrawOrderChart(LineChartGraphic chart, string companyId)
        {
            var company = _c.Store.View?.companies?.FirstOrDefault(c => c.companyId == companyId);
            int cap = GraphSeriesBuilder.AxisLimitBps(companyId);
            chart.Clear();
            chart.SetRange(0, UiRules.MaxDay, -cap, cap);
            foreach (var locked in GraphSeriesBuilder.LockedRanges(company))
                chart.AddBand(locked.StartDay - 0.5f, locked.EndDay + 0.5f, Palette.Locked);
            foreach (var iv in _draft.IntervalsOf(companyId))
                if (iv.exitDay > iv.entryDay)
                    chart.AddBand(Mathf.Clamp(iv.entryDay, 0, UiRules.MaxDay), Mathf.Clamp(iv.exitDay, 0, UiRules.MaxDay), new Color(0.15f, 0.39f, 0.92f, 0.18f));
            chart.AddHLine(0, Palette.Grid);
            foreach (var seg in GraphSeriesBuilder.Segments(company))
                chart.AddSeries(seg.Select(p => new Vector2(p.Day, p.Bps)).ToList(), Palette.Text, 2f, 3f);
        }

        // ------------------------------------------------------------------
        // 확정 주문·결과
        // ------------------------------------------------------------------

        private void BuildOrderSummary(string message)
        {
            var text = UiKit.Label(_body, message, 15, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 10, 6, 10, 0);

            var card = UiKit.Panel(_body, "Order", Palette.Card, rounded: true);
            UiKit.Stretch(card.rectTransform, 6, 44, 6, 20);
            var lines = OrderLines();
            var body = UiKit.Label(card.transform, lines, 17, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(body.rectTransform, 20, 16, 20, 16);
        }

        private string OrderLines()
        {
            var inv = _c.Store.View?.ownInvestments;
            if (inv == null || inv.Length == 0) return "확정된 주문이 없습니다.";
            var sb = new System.Text.StringBuilder("<b>내 확정 주문</b>\n\n");
            for (int i = 0; i < inv.Length; i++)
            {
                var o = inv[i];
                sb.Append(i + 1).Append(". <b>").Append(o.companyId).Append("</b>  ").Append(DisplayFormat.Weight(o.weightBps)).Append("   ");
                sb.Append(string.Join(",  ", (o.intervals ?? new Interval[0]).Select(v => v.entryDay + "일 → " + v.exitDay + "일")));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private void BuildResult()
        {
            var board = _c.Store.Scoreboard;
            if (board == null || board.turn != _c.Store.CurrentTurn)
            {
                Message(_body, "정산 결과를 기다리는 중입니다.");
                return;
            }

            var card = UiKit.Panel(_body, "Result", Palette.Card, rounded: true);
            UiKit.Stretch(card.rectTransform, 6, 6, 6, 6);
            var sb = new System.Text.StringBuilder("<b>" + board.turn + "턴 결과</b>\n\n");
            sb.Append("<size=13><color=#6B7280>순위      플레이어      총자산          이번 턴          누적 손익</color></size>\n");
            foreach (var r in board.rows.OrderBy(r => r.rank).ThenBy(r => r.playerId))
            {
                bool me = r.playerId == _c.LocalPlayerId;
                string color = ColorUtility.ToHtmlStringRGB(Palette.ForBps(r.turnReturnBps));
                sb.Append(me ? "<b>" : "")
                  .Append(r.rank).Append("등        ")
                  .Append(r.playerId).Append(me ? " (나)" : "      ").Append("        ")
                  .Append(DisplayFormat.Money(r.totalAssets)).Append("        ")
                  .Append("<color=#").Append(color).Append(">").Append(DisplayFormat.Percent(r.turnReturnBps)).Append("</color>        ")
                  .Append(DisplayFormat.SignedMoney(r.cumulativeProfit))
                  .Append(me ? "</b>" : "").Append('\n');
            }
            sb.Append("\n").Append(OrderLines());
            var text = UiKit.Label(card.transform, sb.ToString(), 17, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 24, 18, 24, 18);
        }

        private static void Message(RectTransform parent, string message)
        {
            var t = UiKit.Label(parent, message, 16, Palette.SubText);
            UiKit.Stretch(t.rectTransform, 20, 20, 20, 20);
        }
    }

    public static class PhaseNames
    {
        public static string Korean(string phase)
        {
            switch (phase)
            {
                case Protocol.Lobby: return "대기실";
                case Protocol.Ended: return "종료";
                case "AUCTION": return "입찰";
                case Protocol.Info: return "정보 탐색";
                case Protocol.Selection: return "기업 선택";
                case Protocol.Investment: return "투자 결정";
                case Protocol.Reveal: return "시장 공개";
                case Protocol.Result: return "결과";
                default: return phase ?? "";
            }
        }
    }
}
