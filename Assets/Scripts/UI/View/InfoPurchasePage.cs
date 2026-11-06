// 정보 구매 창: 기업 카드 6개(위 3개·아래 3개, REQ-005).
// 카드마다 일별 그래프(열람한 날짜만, 숨은 구간은 잇지 않고 음영)와 묶음 6개 버튼.
// 코인·열람 상태는 서버 응답으로만 바뀐다. 버튼은 요청 의도만 보낸다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class CompanyInfo
    {
        public static string Volatility(string companyId)
        {
            switch (companyId)
            {
                case "AI":
                case "SPACE": return "변동성 높음";
                case "FOOD":
                case "BANK": return "변동성 낮음";
                case "BIO": return "변동성 극단적";
                case "AUTO": return "변동성 중간";
                default: return "";
            }
        }

        public static string BundleLabel(int bundleIndex)
            => GraphSeriesBuilder.BundleStartDay(bundleIndex) + "~" + GraphSeriesBuilder.BundleEndDay(bundleIndex) + "일";
    }

    public sealed class InfoPurchasePage
    {
        private readonly GameClientController _c;
        private readonly Text _header;
        private readonly List<CompanyCard> _cards = new List<CompanyCard>();

        public InfoPurchasePage(RectTransform page, GameClientController controller)
        {
            _c = controller;

            var title = UiKit.Label(page, "정보 구매 창", 18, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -2), new Vector2(300, 28));

            _header = UiKit.Label(page, "", 14, Palette.SubText, TextAnchor.MiddleRight);
            UiKit.Place(_header.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -2), new Vector2(600, 28));

            var grid = UiKit.Rect(page, "CompanyGrid");
            UiKit.Stretch(grid, 0, 34, 0, 0);

            var ids = UiRules.CompanyIds;
            for (int i = 0; i < ids.Length; i++)
            {
                int col = i % 3;
                int row = i / 3; // 0 = 위, 1 = 아래
                var cell = UiKit.Rect(grid, "Card_" + ids[i]);
                UiKit.Anchor(cell, col / 3f, row == 0 ? 0.5f : 0f, (col + 1) / 3f, row == 0 ? 1f : 0.5f, 5, 5);
                _cards.Add(new CompanyCard(cell, ids[i], controller));
            }
        }

        public void Refresh()
        {
            var v = _c.Store.View;
            var phase = _c.Store.Public?.phase;
            _header.text = v == null
                ? "개인 정보 수신 대기"
                : "보유 코인 <b>" + v.coins + "개</b>   ·   자산 " + DisplayFormat.Money(v.assets)
                  + (phase == Protocol.Info ? "   ·   1코인 = 정보 1묶음" : "   ·   구매는 정보 탐색 단계에서만");
            foreach (var card in _cards) card.Refresh();
        }
    }

    internal sealed class CompanyCard
    {
        private readonly string _companyId;
        private readonly GameClientController _c;
        private readonly Text _seats;
        private readonly BarChartGraphic _chart;
        private readonly Text _tip;
        private DayBar[] _bars = new DayBar[0];
        private readonly Text _axisTop, _axisBottom;
        private readonly Button[] _bundleButtons = new Button[UiRules.BundleCount];

        public CompanyCard(RectTransform cell, string companyId, GameClientController controller)
        {
            _companyId = companyId;
            _c = controller;

            var bg = UiKit.Panel(cell, "Bg", Palette.CardBorder, rounded: true);
            UiKit.Stretch(bg.rectTransform);
            var inner = UiKit.Panel(bg.transform, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 1, 1, 1, 1);
            var root = inner.rectTransform;

            // NEXT-D03 기업 이미지(없으면 배지)와 표시명. 통신 ID는 그대로.
            var entry = CompanyCatalog.Get(companyId);
            var logo = CompanyBadge.Create(root, companyId, 24);
            UiKit.Place(logo, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -5), new Vector2(24, 24));
            var name = UiKit.Label(root, "<b>" + entry.DisplayName + "</b> <size=10>" + entry.Id + " · " + entry.Volatility + "</size>",
                14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(name.rectTransform, 38, 8, 10, 0);

            _seats = UiKit.Label(root, "", 12, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Stretch(_seats.rectTransform, 10, 8, 10, 0);

            // 그래프 영역
            var chartRt = UiKit.Rect(root, "Chart");
            UiKit.Stretch(chartRt, 40, 30, 10, 58);
            _chart = chartRt.gameObject.AddComponent<BarChartGraphic>();
            var pointer = chartRt.gameObject.AddComponent<ChartPointerLayer>(); // 마우스 위치 날짜의 값 툴팁
            pointer.Hovered = OnHover;

            _tip = UiKit.Label(root, "", 10, Palette.Text, TextAnchor.MiddleRight);
            UiKit.Place(_tip.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -24), new Vector2(220, 14));

            _axisTop = UiKit.Label(root, "", 10, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(_axisTop.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -28), new Vector2(38, 14));

            _axisBottom = UiKit.Label(root, "", 10, Palette.SubText, TextAnchor.LowerRight);
            UiKit.Place(_axisBottom.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 56), new Vector2(38, 14));

            foreach (int day in new[] { 0, 10, 20, 30 })
            {
                var tick = UiKit.Label(chartRt, day + "일", 9, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2((day + 0.5f) / DayAxis.Count, 0), new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(40, 14));
            }

            // 묶음 버튼 6개
            var row = UiKit.Rect(root, "Bundles");
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(0.5f, 0);
            row.offsetMin = new Vector2(6, 6);
            row.offsetMax = new Vector2(-6, 38);
            for (int k = 0; k < UiRules.BundleCount; k++)
            {
                int bundleIndex = k;
                var b = UiKit.Button(row, "", 10, Palette.Disabled, Palette.Text, () => OnBundleClicked(bundleIndex));
                UiKit.Anchor((RectTransform)b.transform, k / 6f, 0, (k + 1) / 6f, 1, 2, 0);
                _bundleButtons[k] = b;
            }
        }

        private void OnBundleClicked(int bundleIndex)
        {
            _c.BuyInformation(_companyId, bundleIndex);
        }

        public void Refresh()
        {
            // 남은 자리 수는 정보 구매 창에 표시하지 않는다(자리 선택 화면에서만 보임). 내가 고른 기업 표시만 남김.
            _seats.text = _c.Store.OwnSelections.Contains(_companyId) ? "<color=#2563EB><b>내 선택</b></color>" : "";

            var company = _c.Store.View?.companies?.FirstOrDefault(x => x.companyId == _companyId);
            var revealed = _c.Store.Revealed != null && _c.Store.RevealedTurn == _c.Store.CurrentTurn
                ? _c.Store.Revealed.FirstOrDefault(x => x.companyId == _companyId)
                : null;
            DrawChart(company, revealed);
            RefreshButtons(company);
        }

        private void DrawChart(VisibleCompany company, RevealedCompany revealed)
        {
            int cap = GraphSeriesBuilder.AxisLimitBps(_companyId); // 공개 설정값. 비공개 원본을 보지 않는다.
            _axisTop.text = DisplayFormat.Percent(cap);
            _axisBottom.text = DisplayFormat.Percent(-cap);
            // 시장 공개(주문 잠금) 이후에만 31일 전체, 그 전에는 열람한 날짜만.
            _bars = DailyBarModel.Build(company, revealed);
            _chart.SetData(_bars, cap);
        }

        private void OnHover(int? day)
        {
            _tip.text = day.HasValue && day.Value < _bars.Length ? DailyBarModel.Tooltip(_bars[day.Value]) : "";
        }

        private void RefreshButtons(VisibleCompany company)
        {
            bool infoPhase = _c.Store.Public?.phase == Protocol.Info;
            int coins = _c.Store.View?.coins ?? 0;

            for (int k = 0; k < _bundleButtons.Length; k++)
            {
                var b = _bundleButtons[k];
                string range = CompanyInfo.BundleLabel(k);
                if (GraphSeriesBuilder.TryGetSource(company, k, out var source))
                {
                    bool purchased = source == BundleSources.Purchased;
                    UiKit.SetButton(b, range, false,
                        purchased ? Palette.AccentSoft : Palette.Disabled,
                        purchased ? Palette.Accent : Palette.SubText);
                }
                else if (_c.Store.IsPurchasePending(_companyId, k))
                {
                    UiKit.SetButton(b, range, false, Palette.AccentSoft, Palette.Accent);
                }
                else
                {
                    bool can = infoPhase && coins > 0;
                    UiKit.SetButton(b, range, can,
                        can ? Palette.Accent : Palette.Disabled,
                        can ? Color.white : Palette.DisabledText);
                }
            }
        }
    }
}
