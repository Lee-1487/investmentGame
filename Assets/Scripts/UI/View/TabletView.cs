// 태블릿형 게임 화면(REQ-018). 배치는 팀 스케치 기준:
//  - 위 가장자리: 아이템 상점이 위에서 내려옴(후속 기능, 확정 가격만 표시)
//  - 왼쪽 가장자리: 매수 창으로 넘김 / 오른쪽 가장자리: 정보 구매 창으로 넘김
//  - 아래 가장자리: 보유 아이템(후속)
//  - 상단 중앙: 전광판 주요 알림(5초 표시 후 사라짐)
//  - 왼쪽 열: 1~4등 칸. 마우스를 올리면 그 플레이어의 이번 턴 수익률·누적 손익·추이(주변 암전)
//  - 오른쪽: 남은 시간 + 완료 버튼 / 오른쪽 아래: 본인 직업 카드(마우스: 전체 추이, 클릭: 고유 능력-후속)
// 화면은 저장소(ClientStateStore) 값만 표시하고, 버튼은 GameClientController로 의도만 보낸다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class TabletView : MonoBehaviour
    {
        private const float EdgeSize = 34f;
        private const float BannerSeconds = 5f;

        private GameClientController _c;
        private LobbyActions _lobby;
        private ClientStateStore Store => _c.Store;

        public RectTransform Root { get; private set; }
        public RectTransform ScreenRect { get; private set; }

        // 페이지
        private RectTransform _pager;
        private float _pageT, _pageTarget;
        private InvestmentPage _investmentPage;
        private InfoPurchasePage _infoPage;
        private Text _pageHint;

        // 전광판
        private CanvasGroup _bannerGroup;
        private Text _bannerText;
        private readonly Queue<string> _bannerQueue = new Queue<string>();
        private float _bannerLeft;

        // 순위
        private readonly List<RankSlot> _slots = new List<RankSlot>();
        private Text _turnLabel;

        // 타이머
        private readonly PhaseClock _clock = new PhaseClock();
        private string _clockKey = "";
        private Text _timerText;
        private Button _doneButton;

        // 오버레이
        private Image _dim;
        private RectTransform _popup;
        private RectTransform _shop, _inventory;
        private float _shopT, _shopTarget, _invT, _invTarget;
        private Text _shopBody;

        // 알림 감지용 이전 값
        private string _lastPhaseKey = "";
        private int _lastCoins = -1;
        private bool _lastLocked;
        private int _lastBoardTurn = -1;
        private string _lastSelector = "";

        public static TabletView Create(RectTransform parent, GameClientController controller, LobbyActions lobby = null)
        {
            var root = UiKit.Rect(parent, "TabletView");
            UiKit.Stretch(root);
            var view = root.gameObject.AddComponent<TabletView>();
            view._lobby = lobby;
            view.Init(root, controller);
            return view;
        }

        private void Init(RectTransform root, GameClientController controller)
        {
            _c = controller;
            Root = root;
            Build();
            Store.Changed += Refresh;
            Store.ErrorReceived += OnError;
            Store.AutomaticActionReceived += OnAutomatic;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_c == null) return;
            Store.Changed -= Refresh;
            Store.ErrorReceived -= OnError;
            Store.AutomaticActionReceived -= OnAutomatic;
        }

        // ------------------------------------------------------------------
        // 구성
        // ------------------------------------------------------------------

        private void Build()
        {
            var bg = UiKit.Panel(Root, "Background", Palette.Background);
            UiKit.Stretch(bg.rectTransform);

            BuildEdges();

            var bezel = UiKit.Panel(Root, "Bezel", Palette.Bezel, rounded: true);
            UiKit.Stretch(bezel.rectTransform, EdgeSize + 8, EdgeSize + 4, EdgeSize + 8, EdgeSize + 4);
            var screen = UiKit.Panel(bezel.transform, "Screen", Palette.Screen, rounded: true);
            UiKit.Stretch(screen.rectTransform, 12, 12, 12, 12);
            ScreenRect = screen.rectTransform;

            BuildRankColumn();
            BuildPages();
            BuildRightColumn();
            BuildBanner();
            BuildOverlays();
            if (_lobby?.Leave != null)
            {
                var leave = UiKit.Button(Root, "방 나가기", 12, Palette.Card, Palette.Text, _lobby.Leave);
                leave.name = "LeaveRoom";
                UiKit.Place((RectTransform)leave.transform, Vector2.zero, Vector2.zero, new Vector2(42, 2), new Vector2(100, 30));
            }
        }

        private void BuildEdges()
        {
            Edge("Edge_Top", 0, 1, 1, 1, new Vector2(0, -EdgeSize), Vector2.zero, "아이템 상점 열기", ToggleShop);
            Edge("Edge_Bottom", 0, 0, 1, 0, Vector2.zero, new Vector2(0, EdgeSize), "보유 아이템 열기", ToggleInventory);
            Edge("Edge_Left", 0, 0, 0, 1, new Vector2(0, EdgeSize), new Vector2(EdgeSize, -EdgeSize), "매수 창으로", () => ShowPage(0));
            Edge("Edge_Right", 1, 0, 1, 1, new Vector2(-EdgeSize, EdgeSize), new Vector2(0, -EdgeSize), "정보 구매 창으로", () => ShowPage(1));
        }

        private void Edge(string name, float aMinX, float aMinY, float aMaxX, float aMaxY,
            Vector2 offMin, Vector2 offMax, string purpose, System.Action onClick)
        {
            // 보이지 않는 클릭 영역. 색은 투명(알파 0)이지만 raycastTarget이 켜져 있어 클릭을 받는다.
            var img = UiKit.Panel(Root, name, Color.clear, raycast: true);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(aMinX, aMinY);
            rt.anchorMax = new Vector2(aMaxX, aMaxY);
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            UiKit.Click(img.gameObject, onClick);
        }

        private void BuildRankColumn()
        {
            var col = UiKit.Rect(ScreenRect, "RankColumn");
            col.anchorMin = new Vector2(0, 0);
            col.anchorMax = new Vector2(0, 1);
            col.pivot = new Vector2(0, 1);
            col.offsetMin = new Vector2(12, 12);
            col.offsetMax = new Vector2(100, -12);

            _turnLabel = UiKit.Label(col, "", 13, Palette.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.Place(_turnLabel.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(88, 50));

            for (int i = 0; i < 4; i++)
            {
                var slot = new RankSlot(col, i, this);
                _slots.Add(slot);
            }

            var sep = UiKit.Panel(ScreenRect, "Separator", Palette.CardBorder);
            sep.rectTransform.anchorMin = new Vector2(0, 0);
            sep.rectTransform.anchorMax = new Vector2(0, 1);
            sep.rectTransform.offsetMin = new Vector2(106, 16);
            sep.rectTransform.offsetMax = new Vector2(107, -16);
        }

        private void BuildPages()
        {
            var viewport = UiKit.Rect(ScreenRect, "PageViewport");
            UiKit.Stretch(viewport, 116, 76, 168, 10);
            viewport.gameObject.AddComponent<RectMask2D>();

            _pager = UiKit.Rect(viewport, "Pager");
            _pager.anchorMin = new Vector2(0, 0);
            _pager.anchorMax = new Vector2(2, 1);
            _pager.offsetMin = Vector2.zero;
            _pager.offsetMax = Vector2.zero;

            var invest = UiKit.Rect(_pager, "Page_Investment");
            UiKit.Anchor(invest, 0, 0, 0.5f, 1, 4, 0);
            var info = UiKit.Rect(_pager, "Page_Info");
            UiKit.Anchor(info, 0.5f, 0, 1, 1, 4, 0);

            _investmentPage = new InvestmentPage(invest, _c, _lobby);
            _infoPage = new InfoPurchasePage(info, _c);

            _pageHint = UiKit.Label(ScreenRect, "", 11, Palette.SubText, TextAnchor.LowerCenter);
            _pageHint.rectTransform.anchorMin = new Vector2(0, 0);
            _pageHint.rectTransform.anchorMax = new Vector2(1, 0);
            _pageHint.rectTransform.offsetMin = new Vector2(116, -2);
            _pageHint.rectTransform.offsetMax = new Vector2(-168, 12);
        }

        private void BuildRightColumn()
        {
            var col = UiKit.Rect(ScreenRect, "RightColumn");
            col.anchorMin = new Vector2(1, 0);
            col.anchorMax = new Vector2(1, 1);
            col.pivot = new Vector2(1, 0.5f);
            col.offsetMin = new Vector2(-160, 12);
            col.offsetMax = new Vector2(-12, -76);

            // 남은 시간
            var timerBox = UiKit.Panel(col, "Timer", Palette.Card, rounded: true);
            UiKit.Place(timerBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(140, 110));
            _timerText = UiKit.Label(timerBox.transform, "", 14, Palette.Text);
            UiKit.Stretch(_timerText.rectTransform, 6, 6, 6, 6);

            _doneButton = UiKit.Button(col, "완료", 16, Palette.Accent, Color.white, OnDoneClicked);
            UiKit.Place((RectTransform)_doneButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(140, 44));

            // 본인 직업 카드
            var job = UiKit.Panel(col, "OwnJob", Palette.Card, rounded: true, raycast: true);
            UiKit.Place(job.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(120, 120));
            var border = UiKit.Panel(job.transform, "Border", Palette.Player(_c.LocalPlayerId), rounded: true);
            UiKit.Stretch(border.rectTransform);
            var inner = UiKit.Panel(border.transform, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 3, 3, 3, 3);
            var t = UiKit.Label(inner.transform, "<b>본인의 직업</b>\n\n<size=12>" + _c.LocalPlayerId + "\n능력 미정</size>", 14, Palette.Text);
            UiKit.Stretch(t.rectTransform, 4, 4, 4, 4);
            UiKit.Hover(job.gameObject, () => ShowAllPlayersPopup(job.rectTransform), HidePopup);
            UiKit.Click(job.gameObject, () => ShowBanner("고유 능력은 후속 구현 예정입니다. (rules-v2 확정 효과로 추가)"));
        }

        private void BuildBanner()
        {
            var banner = UiKit.Panel(ScreenRect, "Banner", Palette.Card, rounded: true);
            var rt = banner.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(116, -64);
            rt.offsetMax = new Vector2(-168, -12);
            var border = UiKit.Panel(rt, "Border", Palette.CardBorder, rounded: true);
            UiKit.Stretch(border.rectTransform);
            border.transform.SetAsFirstSibling();
            var inner = UiKit.Panel(rt, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 1, 1, 1, 1);
            _bannerText = UiKit.Label(inner.transform, "", 16, Palette.Text);
            UiKit.Stretch(_bannerText.rectTransform, 12, 4, 12, 4);
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
        }

        private void BuildOverlays()
        {
            // 상점: 위에서 내려옴
            _shop = UiKit.Panel(ScreenRect, "ShopDrop", Palette.Card, rounded: true, raycast: true).rectTransform;
            _shop.anchorMin = new Vector2(0, 1);
            _shop.anchorMax = new Vector2(1, 1);
            _shop.pivot = new Vector2(0.5f, 0);
            _shop.sizeDelta = new Vector2(-232, 250);
            var shopTitle = UiKit.Label(_shop, "<b>아이템 상점</b>   <size=12>가격 = 턴 시작 자산 S × 비율 (0 방향 버림, 최소 1) · 턴당 1개 · 구매 기능은 후속 구현</size>",
                15, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(shopTitle.rectTransform, 16, 12, 16, 0);
            _shopBody = UiKit.Label(_shop, "", 14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(_shopBody.rectTransform, 16, 42, 16, 40);
            var closeShop = UiKit.Button(_shop, "닫기 ▲", 13, Palette.Disabled, Palette.Text, ToggleShop);
            UiKit.Place((RectTransform)closeShop.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-12, 8), new Vector2(90, 28));

            // 인벤토리: 아래에서 올라옴
            _inventory = UiKit.Panel(ScreenRect, "InventoryDrop", Palette.Card, rounded: true, raycast: true).rectTransform;
            _inventory.anchorMin = new Vector2(0, 0);
            _inventory.anchorMax = new Vector2(1, 0);
            _inventory.pivot = new Vector2(0.5f, 1);
            _inventory.sizeDelta = new Vector2(-232, 150);
            var invText = UiKit.Label(_inventory, "<b>보유 아이템</b> (최대 3개)\n\n<color=#6B7280>보유한 아이템이 없습니다. 아이템 사용은 후속 구현입니다.\n금융형은 투자 결정 단계, 정보·공격형은 정보 탐색 단계에서 사용합니다.</color>",
                14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(invText.rectTransform, 16, 12, 16, 12);
            var closeInv = UiKit.Button(_inventory, "닫기 ▼", 13, Palette.Disabled, Palette.Text, ToggleInventory);
            UiKit.Place((RectTransform)closeInv.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -8), new Vector2(90, 28));

            // 암전 + 팝업
            _dim = UiKit.Panel(ScreenRect, "Dim", Palette.Dim);
            UiKit.Stretch(_dim.rectTransform);
            _dim.gameObject.SetActive(false);

            _popup = UiKit.Rect(ScreenRect, "Popup");
            _popup.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 갱신
        // ------------------------------------------------------------------

        public void Refresh()
        {
            var s = Store.Public;
            DetectNotifications();

            _turnLabel.text = s == null ? "" : s.turn + "턴\n<size=11>" + PhaseNames.Korean(s.phase) + "</size>";
            RefreshRanks();
            _investmentPage.Refresh();
            _infoPage.Refresh();
            RefreshShop();

            if (s != null)
            {
                string key = s.turn + "|" + s.phase + "|" + s.selectionIndex + "|" + s.serverElapsedSeconds;
                if (key != _clockKey)
                {
                    _clockKey = key;
                    _clock.Sync(s.serverElapsedSeconds, Time.realtimeSinceStartup);
                }
            }
        }

        private void DetectNotifications()
        {
            var s = Store.Public;
            if (s != null)
            {
                string phaseKey = s.turn + "|" + s.phase;
                if (phaseKey != _lastPhaseKey)
                {
                    _lastPhaseKey = phaseKey;
                    if (s.phase == Protocol.Lobby) ShowBanner("대기실에 입장했습니다. 준비를 누르세요.");
                    else if (s.phase != Protocol.Ended) ShowBanner("<b>" + s.turn + "턴 · " + PhaseNames.Korean(s.phase) + "</b> 단계가 시작되었습니다.");
                    if (s.phase == Protocol.Info || s.phase == Protocol.Reveal) ShowPage(1);
                    if (s.phase == Protocol.Lobby || s.phase == Protocol.Selection || s.phase == Protocol.Investment || s.phase == Protocol.Result) ShowPage(0);
                }

                if (s.phase == Protocol.Selection && s.currentPlayerId != _lastSelector)
                {
                    _lastSelector = s.currentPlayerId;
                    if (s.currentPlayerId == _c.LocalPlayerId) ShowBanner("<color=#DC2626><b>내 차례입니다.</b></color> 기업을 선택하세요.");
                }
            }

            var v = Store.View;
            if (v != null)
            {
                if (_lastCoins >= 0 && v.coins < _lastCoins) ShowBanner("정보를 구매했습니다. 남은 코인 " + v.coins + "개");
                _lastCoins = v.coins;
            }

            if (Store.EndReason != _lastEndReason)
            {
                _lastEndReason = Store.EndReason;
                if (!string.IsNullOrEmpty(_lastEndReason)) ShowBanner(ErrorMessages.ToKorean(_lastEndReason));
            }

            if (Store.OrderLocked && !_lastLocked) ShowBanner("주문이 확정되었습니다.");
            _lastLocked = Store.OrderLocked;

            var board = Store.Scoreboard;
            if (board != null && board.turn != _lastBoardTurn && board.rows != null && board.rows.Length > 0)
            {
                _lastBoardTurn = board.turn;
                var top = board.rows.OrderBy(r => r.rank).First();
                ShowBanner(board.turn + "턴 정산 완료 · 1위 " + top.playerId + " (" + DisplayFormat.Percent(top.turnReturnBps) + ")");
            }
        }

        private string _lastEndReason = "";

        private void OnAutomatic(string resultType)
        {
            if (resultType == CommandTypes.ResultOf(CommandTypes.SelectCompany))
                ShowBanner("<color=#B45309>시간이 초과되어 기업이 자동 선택되었습니다.</color>");
            else if (resultType == CommandTypes.ResultOf(CommandTypes.SubmitInvestment))
                ShowBanner("<color=#B45309>시간이 초과되어 기본 주문(50:50 · 각 0→5일)으로 확정되었습니다.</color>");
        }

        private void OnError(ClientError error)
        {
            ShowBanner("<color=#DC2626>" + ErrorMessages.ToKorean(error.ErrorCode) + "</color>");
        }

        private void RefreshRanks()
        {
            var rows = RankRows();
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].Set(i < rows.Count ? rows[i] : null, _c.LocalPlayerId);
        }

        /// <summary>정산 결과가 있으면 순위대로, 없으면 참가자 순서대로.</summary>
        internal List<RankRow> RankRows()
        {
            var board = Store.Scoreboard;
            if (board?.rows != null && board.rows.Length > 0)
                return board.rows.OrderBy(r => r.rank).ThenBy(r => r.playerId)
                    .Select(r => new RankRow { PlayerId = r.playerId, Rank = r.rank, Score = r }).ToList();

            var players = Store.Public?.players;
            if (players == null) return new List<RankRow>();
            return players.Select(p => new RankRow { PlayerId = p.playerId, Rank = 0, Connected = p.connected }).ToList();
        }

        private void RefreshShop()
        {
            long s = Store.View?.assets ?? 0;
            (string name, string kind, int bps, string effect)[] items =
            {
                ("증폭기", "금융", 300, "선택한 본인 기업의 전체 구간 손익률 ×2"),
                ("하락 파쇄기", "금융", 400, "선택한 본인 기업의 손익률 부호 반전"),
                ("방패", "금융", 200, "-3~+3%면 +5%, -3% 미만이면 손실 절반"),
                ("대폭발 탐지기", "금융", 500, "지정 구간 변화폭 15% 이상이면 +50%p"),
                ("블랙박스 분석권", "정보", 200, "선택 기업의 미열람 1묶음 공개"),
                ("정보 왜곡", "공격", 200, "대상의 유료 묶음 1개를 10초간 가림"),
                ("정보 훔치기", "공격", 300, "대상의 유료 묶음 중 내 미열람 1개 복사"),
                ("수익률 감소", "공격", 400, "상위 2명 중 1명, 정산 시 투자금 10% 차감"),
            };
            var sb = new System.Text.StringBuilder();
            foreach (var it in items)
            {
                long price = s <= 0 ? 0 : System.Math.Max(1, s * it.bps / 10000);
                sb.Append("<b>").Append(it.name).Append("</b> <size=11>[").Append(it.kind).Append("]</size>  ")
                  .Append(it.bps / 100).Append("%")
                  .Append(s > 0 ? " (현재 " + DisplayFormat.Money(price) + ")" : " (구매 불가)")
                  .Append("  <color=#6B7280><size=12>").Append(it.effect).Append("</size></color>\n");
            }
            _shopBody.text = sb.ToString();
        }

        // ------------------------------------------------------------------
        // 매 프레임: 타이머·전광판·슬라이드
        // ------------------------------------------------------------------

        private void Update()
        {
            if (_c == null) return;
            UpdateTimer();
            UpdateBanner();

            _pageT = Mathf.MoveTowards(_pageT, _pageTarget, Time.unscaledDeltaTime * 4f);
            float e = Mathf.SmoothStep(0, 1, _pageT);
            _pager.anchorMin = new Vector2(-e, 0);
            _pager.anchorMax = new Vector2(2 - e, 1);
            _pageHint.text = _pageTarget < 0.5f
                ? "오른쪽 가장자리를 누르면 정보 구매 창으로 넘어갑니다"
                : "왼쪽 가장자리를 누르면 매수 창으로 넘어갑니다";

            _shopT = Mathf.MoveTowards(_shopT, _shopTarget, Time.unscaledDeltaTime * 5f);
            _shop.anchoredPosition = new Vector2(0, -Mathf.SmoothStep(0, 1, _shopT) * 262f);
            _shop.gameObject.SetActive(_shopT > 0.001f);

            _invT = Mathf.MoveTowards(_invT, _invTarget, Time.unscaledDeltaTime * 5f);
            _inventory.anchoredPosition = new Vector2(0, Mathf.SmoothStep(0, 1, _invT) * 162f);
            _inventory.gameObject.SetActive(_invT > 0.001f);
        }

        private void UpdateTimer()
        {
            var s = Store.Public;
            if (s == null)
            {
                _timerText.text = "접속 대기";
                return;
            }

            if (!PhaseClock.HasDeadline(s))
            {
                _timerText.text = "<size=12>" + PhaseNames.Korean(s.phase) + "</size>\n<size=26><b>-</b></size>\n<size=11>시간 제한 없음</size>";
                _timerText.color = Palette.Text;
                UiKit.SetButton(_doneButton, "완료", false, Palette.Disabled, Palette.DisabledText);
                return;
            }

            int remain = _clock.RemainingSeconds(s, Time.realtimeSinceStartup);
            string what = s.phase == Protocol.Selection
                ? (string.IsNullOrEmpty(s.currentPlayerId) ? "선택" : s.currentPlayerId + " 차례")
                : PhaseNames.Korean(s.phase);
            _timerText.text = "<size=12>" + what + "</size>\n<size=34><b>" + remain + "</b></size><size=13>초</size>\n<size=11>"
                              + (remain == 0 ? "서버 진행 대기" : "남은 시간") + "</size>";
            _timerText.color = remain <= 5 && remain > 0 ? Palette.Up : Palette.Text;

            bool can = _c.CanFinishPhase();
            bool done = _c.IsDoneForCurrentPhase;
            string label = done ? "완료함 · 대기 중"
                : s.phase == Protocol.Investment ? (Store.OrderLocked ? "다른 참가자 대기" : "주문 확정 시 진행")
                : s.phase == Protocol.Selection ? "선택 시 자동 진행"
                : "완료";
            UiKit.SetButton(_doneButton, label, can, can ? Palette.Accent : Palette.Disabled, can ? Color.white : Palette.DisabledText);
        }

        private void UpdateBanner()
        {
            if (_bannerLeft <= 0f && _bannerQueue.Count > 0)
            {
                _bannerText.text = _bannerQueue.Dequeue();
                _bannerLeft = _bannerQueue.Count > 2 ? 1.5f : BannerSeconds; // 밀려 있으면 빨리 넘김
            }
            if (_bannerLeft > 0f)
            {
                _bannerLeft -= Time.unscaledDeltaTime;
                _bannerGroup.alpha = Mathf.Clamp01(_bannerLeft / 0.4f);
            }
            else
            {
                _bannerGroup.alpha = 0f;
            }
        }

        // ------------------------------------------------------------------
        // 동작
        // ------------------------------------------------------------------

        public void ShowBanner(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _bannerQueue.Enqueue(message);
        }

        /// <summary>0 = 매수 창, 1 = 정보 구매 창.</summary>
        public void ShowPage(int index) => _pageTarget = Mathf.Clamp01(index);

        private void ToggleShop()
        {
            _shopTarget = _shopTarget > 0.5f ? 0f : 1f;
            if (_shopTarget > 0.5f) _invTarget = 0f;
        }

        private void ToggleInventory()
        {
            _invTarget = _invTarget > 0.5f ? 0f : 1f;
            if (_invTarget > 0.5f) _shopTarget = 0f;
        }

        private void OnDoneClicked()
        {
            var status = _c.FinishPhase();
            if (status == CommandStatus.Sent) ShowBanner("완료했습니다. 다른 참가자를 기다립니다.");
        }

        // ------------------------------------------------------------------
        // 팝업(마우스 올림)
        // ------------------------------------------------------------------

        internal void ShowPlayerPopup(RankRow row, RectTransform slot)
        {
            if (row == null) return;
            var popup = OpenPopup(new Vector2(320, 230));
            PositionNextTo(popup, slot, rightSide: true);

            var r = row.Score;
            string title = "<b>" + row.PlayerId + (row.PlayerId == _c.LocalPlayerId ? " (나)" : "") + "</b>  <size=12>직업 미정</size>";
            string body = r == null
                ? "\n<color=#6B7280>아직 정산 결과가 없습니다.</color>"
                : "\n이번 턴 수익률  <b><color=#" + ColorUtility.ToHtmlStringRGB(Palette.ForBps(r.turnReturnBps)) + ">" + DisplayFormat.Percent(r.turnReturnBps) + "</color></b>"
                  + "\n누적 손익  <b>" + DisplayFormat.SignedMoney(r.cumulativeProfit) + "</b>   ·   누적 수익률  <b>" + DisplayFormat.Percent(r.cumulativeReturnBps) + "</b>"
                  + "\n총자산  <b>" + DisplayFormat.Money(r.totalAssets) + "</b>";
            var text = UiKit.Label(popup, title + body, 14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 14, 10, 14, 10);

            if (r?.returnHistory != null && r.returnHistory.Length > 0)
                AddTrendChart(popup, new[] { r }, new Vector2(14, 12), 92);
        }

        private void ShowAllPlayersPopup(RectTransform anchor)
        {
            var popup = OpenPopup(new Vector2(380, 250));
            PositionNextTo(popup, anchor, rightSide: false);
            var board = Store.Scoreboard;

            var title = UiKit.Label(popup, "<b>플레이어별 수익률 추이</b>   <size=12>클릭: 고유 능력 사용(후속)</size>", 14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(title.rectTransform, 14, 10, 14, 10);

            if (board?.rows == null || board.rows.Length == 0)
            {
                var none = UiKit.Label(popup, "아직 정산 결과가 없습니다.", 14, Palette.SubText);
                UiKit.Stretch(none.rectTransform, 14, 40, 14, 14);
                return;
            }

            var legend = string.Join("    ", board.rows.OrderBy(r => r.playerId).Select(r =>
                "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(r.playerId)) + ">■</color> " + r.playerId + " " + DisplayFormat.Percent(r.cumulativeReturnBps)));
            var legendText = UiKit.Label(popup, legend, 12, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(legendText.rectTransform, 14, 34, 14, 10);
            AddTrendChart(popup, board.rows, new Vector2(14, 14), 160);
        }

        private void AddTrendChart(RectTransform popup, IEnumerable<ScoreRow> rows, Vector2 margin, float height)
        {
            var list = rows.ToList();
            int maxAbs = 1000;
            int maxTurn = 10;
            foreach (var r in list)
                foreach (var p in r.returnHistory ?? new ReturnPoint[0])
                {
                    maxAbs = Mathf.Max(maxAbs, Mathf.Abs(p.returnBps));
                    maxTurn = Mathf.Max(maxTurn, p.turn);
                }
            float range = maxAbs * 1.2f;

            var rt = UiKit.Rect(popup, "Trend");
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(margin.x + 30, margin.y + 12);
            rt.offsetMax = new Vector2(-margin.x, margin.y + height);
            var chart = rt.gameObject.AddComponent<LineChartGraphic>();
            chart.SetRange(0, maxTurn, -range, range);
            chart.AddHLine(0, Palette.Grid);
            foreach (var r in list)
            {
                var pts = (r.returnHistory ?? new ReturnPoint[0]).OrderBy(p => p.turn).Select(p => new Vector2(p.turn, p.returnBps)).ToList();
                chart.AddSeries(pts, Palette.Player(r.playerId), 2.5f, 5f);
            }

            var top = UiKit.Label(rt, DisplayFormat.Percent((int)range), 9, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(top.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(40, 12));
            var bottom = UiKit.Label(rt, DisplayFormat.Percent(-(int)range), 9, Palette.SubText, TextAnchor.LowerRight);
            UiKit.Place(bottom.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-2, 0), new Vector2(40, 12));
            var xLabel = UiKit.Label(rt, "0턴" + new string(' ', 40) + maxTurn + "턴", 9, Palette.SubText, TextAnchor.UpperCenter);
            UiKit.Place(xLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(300, 12));
        }

        private RectTransform OpenPopup(Vector2 size)
        {
            UiKit.Clear(_popup);
            _dim.gameObject.SetActive(true);
            _dim.transform.SetAsLastSibling();
            _popup.SetAsLastSibling();
            _popup.gameObject.SetActive(true);

            var shadow = UiKit.Panel(_popup, "Shadow", new Color(0, 0, 0, 0.25f), rounded: true);
            UiKit.Stretch(shadow.rectTransform, -3, -1, -3, -5);
            var panel = UiKit.Panel(_popup, "Panel", Palette.Card, rounded: true);
            UiKit.Stretch(panel.rectTransform);
            _popup.sizeDelta = size;
            return panel.rectTransform;
        }

        private void PositionNextTo(RectTransform popup, RectTransform target, bool rightSide)
        {
            // 대상의 화면 위치를 Screen 좌표계로 옮겨 옆에 붙인다.
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var local = ScreenRect.InverseTransformPoint(rightSide ? corners[2] : corners[1]); // 오른쪽 위 / 왼쪽 위
            _popup.anchorMin = _popup.anchorMax = new Vector2(0.5f, 0.5f);
            _popup.pivot = rightSide ? new Vector2(0, 1) : new Vector2(1, 0);
            var pos = new Vector2(local.x + (rightSide ? 10 : -10), local.y);
            if (!rightSide) pos.y = ScreenRect.InverseTransformPoint(corners[0]).y; // 왼쪽 아래 기준으로 위로 펼침
            // 화면 밖으로 나가지 않게 보정
            var half = ScreenRect.rect.size * 0.5f;
            if (rightSide) pos.y = Mathf.Clamp(pos.y, -half.y + _popup.sizeDelta.y + 8, half.y - 8);
            _popup.anchoredPosition = pos;
        }

        internal void HidePopup()
        {
            _dim.gameObject.SetActive(false);
            _popup.gameObject.SetActive(false);
        }
    }

    internal sealed class RankRow
    {
        public string PlayerId;
        public int Rank;
        public bool Connected = true;
        public ScoreRow Score;
    }

    internal sealed class RankSlot
    {
        private readonly Image _border;
        private readonly Text _text;
        private readonly RectTransform _rt;
        private RankRow _row;

        public RankSlot(RectTransform column, int index, TabletView owner)
        {
            var bg = UiKit.Panel(column, "RankSlot" + (index + 1), Palette.Card, rounded: true, raycast: true);
            _rt = bg.rectTransform;
            UiKit.Place(_rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -64 - index * 96), new Vector2(84, 86));
            _border = UiKit.Panel(_rt, "Border", Palette.CardBorder, rounded: true);
            UiKit.Stretch(_border.rectTransform);
            var inner = UiKit.Panel(_border.transform, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 3, 3, 3, 3);
            _text = UiKit.Label(inner.transform, "", 13, Palette.Text);
            UiKit.Stretch(_text.rectTransform, 2, 2, 2, 2);

            UiKit.Hover(bg.gameObject, () => owner.ShowPlayerPopup(_row, _rt), owner.HidePopup);
        }

        public void Set(RankRow row, string localPlayerId)
        {
            _row = row;
            _rt.gameObject.SetActive(row != null);
            if (row == null) return;
            _border.color = Palette.Player(row.PlayerId);
            string rank = row.Rank > 0 ? row.Rank + "등" : "-";
            string me = row.PlayerId == localPlayerId ? " (나)" : "";
            string conn = row.Connected ? "" : "\n<color=#DC2626>연결 끊김</color>";
            _text.text = "<b>" + rank + "</b>\n" + row.PlayerId + me + "\n<size=11>직업 미정</size>" + conn;
        }
    }
}
