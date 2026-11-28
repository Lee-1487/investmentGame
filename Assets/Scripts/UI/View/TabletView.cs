// 태블릿형 게임 화면(REQ-018). 배치는 팀 스케치 기준:
//  - 위 가장자리: 없음(아이템이 기획에서 빠져 상점 열기를 제거)
//  - 왼쪽 가장자리: 매수 창으로 넘김 / 오른쪽 가장자리: 정보 구매 창으로 넘김
//  - 아래 가장자리: 없음(아이템이 기획에서 빠져 보유 아이템 창 열기를 제거)
//  - 상단 중앙: 전광판 주요 알림(5초 표시 후 사라짐)
//  - 왼쪽 열: 1~4등 칸. 마우스를 올리면 그 플레이어의 이번 턴 수익률·누적 손익·추이(주변 암전)
//  - 상단 오른쪽: 남은 시간 / 오른쪽: 완료 버튼 / 오른쪽 아래: 내 능력 상태 카드
// 화면은 저장소(ClientStateStore) 값만 표시하고, 버튼은 GameClientController로 의도만 보낸다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class TabletView : MonoBehaviour
    {
        private const float EdgeSize = 34f;
        private const float BannerSeconds = 5f;
        private const float LeftEdge = 164f; // 왼쪽 플레이어 열 오른쪽 끝(창 내용이 시작하는 x)

        private GameClientController _c;
        private LobbyActions _lobby;
        private ProgressionPanel _features;
        private Text _abilityLabel, _abilityTitle;
        private ClientStateStore Store => _c.Store;

        public RectTransform Root { get; private set; }
        public RectTransform ScreenRect { get; private set; }

        // 페이지
        private RectTransform _pager;
        private RectTransform _pageViewport, _rightColumn, _timerBox;
        private float _pageT, _pageTarget;
        private InvestmentPage _investmentPage;
        private InfoPurchasePage _infoPage;
        private Text _pageHint;

        // 전광판
        private CanvasGroup _bannerGroup;
        private Text _bannerText;
        private Image _bannerLine, _turnNoticeLine;
        private RectTransform _augmentList;
        private string _augmentKey;

        /// <summary>글자 폭만큼의 밑줄을 영역 아래 가운데에 맞춘다.</summary>
        private static void FitUnderline(Image line, Text text)
        {
            if (line == null || text == null) return;
            var rt = line.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            float max = ((RectTransform)line.transform.parent).rect.width - 24f;
            float w = text.preferredWidth;
            rt.sizeDelta = new Vector2(Mathf.Clamp(w, 8f, max > 8f ? max : w), 1.5f);
            rt.anchoredPosition = new Vector2(0, 2f);
        }
        /// <summary>도트 테마: 알림 글자 크기에 맞춰 자주색 상자(테두리 포함)를 가운데에 맞춘다.</summary>
        private static void FitBox(RectTransform box, Text text, float minWidth = 120f)
        {
            if (box == null || text == null) return;
            float max = ((RectTransform)box.parent).rect.width;
            float w = Mathf.Clamp(text.preferredWidth + 48f, minWidth, max > minWidth ? max : minWidth);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(w, Mathf.Max(40f, text.preferredHeight + 18f));
        }

        /// <summary>도트 테마 패널: 황토 테두리 + 갈색 바탕(테두리 두께 2px). 반환 = 안쪽.</summary>
        internal static RectTransform PixelPanel(Transform parent, string name, Color border, Color fill, out Image frame, bool raycast = false)
        {
            frame = UiKit.Panel(parent, name, border, rounded: true, raycast: raycast);
            var body = UiKit.Panel(frame.transform, "Fill", fill, rounded: true);
            UiKit.Stretch(body.rectTransform, 2, 2, 2, 2);
            return body.rectTransform;
        }
        private RectTransform _bannerBox, _turnNoticeBox;
        private Image _timerBar;
        private float _timerMax; private string _timerKey;
        private readonly Queue<string> _bannerQueue = new Queue<string>();
        private float _bannerLeft;

        // 순위
        private readonly List<RankSlot> _slots = new List<RankSlot>();
        private Text _turnLabel;

        // 타이머
        private readonly PhaseClock _clock = new PhaseClock();
        private UrgencyEdges _urgency;
        private readonly Dictionary<string, long> _fxAssets = new Dictionary<string, long>(); // 정산 연출용 직전 총자산
        private readonly Dictionary<string, int> _fxRanks = new Dictionary<string, int>();    // 역전 판단용 직전 순위
        private string _clockKey = "";
        private PublicState _renderedPublic;
        private PlayerView _renderedView;
        private int _viewRefreshTurn;
        private float _missingViewSince = -1;
        private Text _timerText;
        private RectTransform _turnHighlight, _turnNotice;
        private Text _turnNoticeText;
        private Image _turnNoticeBackground;
        private string _lastPublicSelector = "";
        private Button _doneButton;
        private Button _developerTimer, _developerPrevious, _developerReset;
        private Button _pauseButton;
        private RectTransform _pauseOverlay;

        // 오버레이
        private Image _dim;
        private RectTransform _popup;

        // 알림 감지용 이전 값
        private string _lastPhaseKey = "";
        private int? _lastDay;
        private int _lastCoins = -1;
        private int _lastActiveShares = -1;
        private int _lastMoneyDay = -1;     // 실시간 매매: 마지막으로 돈 효과를 계산한 일차
        private RectTransform _edgeFxLayer; // 화면 가장자리까지 덮는 효과층(손실 시 빨간 가장자리)
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
            PixelBackdrop.Create(bg.transform); // 도트 글리치 배경 효과
            PixelClickSparks.Attach(Root);      // 클릭할 때 도트 파편

            BuildEdges();

            var bezel = UiKit.Panel(Root, "Bezel", Color.clear, rounded: true); // 도트 테마: 바탕 효과가 보이게 투명
            UiKit.Stretch(bezel.rectTransform, EdgeSize + 8, EdgeSize + 4, EdgeSize + 8, EdgeSize + 4);
            var screen = UiKit.Panel(bezel.transform, "Screen", Color.clear, rounded: true);
            UiKit.Stretch(screen.rectTransform, 12, 12, 12, 12);
            ScreenRect = screen.rectTransform;

            BuildRankColumn();
            BuildPages();
            BuildRightColumn();
            BuildTimer();
            BuildBanner();
            _urgency = null; // 주변 테두리 효과(마감 10초 전 붉은 가장자리)는 쓰지 않음
            BuildOverlays();
            BuildTurnIndicator();
            _features = ProgressionPanel.Create(Root, _c, _lobby);
            BuildPauseControls();
            if (_lobby?.Leave != null)
            {
                var leave = UiKit.Button(Root, "방 나가기", 12, Palette.Card, Palette.Text, _lobby.Leave);
                leave.name = "LeaveRoom";
                UiKit.Place((RectTransform)leave.transform, Vector2.zero, Vector2.zero, new Vector2(42, 2), new Vector2(100, 30));
            }
            // 결과 발표·정산 카드·역전 연출 전용 맨 위 레이어.
            // 결과 단계에서는 '전광판' 창(ProgressionPanel)이 자동으로 열려 화면 전체를 덮기 때문에, 연출을 화면(Screen) 안에 그리면 그 창 뒤에 가려진다.
            _fxLayer = UiKit.Rect(Root, "ResultFxLayer");
            UiKit.Stretch(_fxLayer, EdgeSize + 20, EdgeSize + 16, EdgeSize + 20, EdgeSize + 16);
        }

        private RectTransform _fxLayer;
        /// <summary>연출을 그릴 곳: 항상 모든 창보다 위(맨 마지막 자식).</summary>
        private RectTransform FxLayer
        {
            get
            {
                if (_fxLayer == null) return ScreenRect;
                _fxLayer.SetAsLastSibling();
                return _fxLayer;
            }
        }

        /// <summary>
        /// 실시간 매매 중 하루가 지날 때마다, 그날 내가 들고 있던 기업들의 하루 등락(시가→종가)으로
        /// 돈이 늘었는지 줄었는지 보고 효과를 낸다(값 계산은 화면용 추정이며 실제 정산은 서버).
        /// </summary>
        private void DetectMoneyChange(PublicState s, PlayerView v)
        {
            if (s == null || !s.stockSystem || s.phase != Protocol.Investment || v.portfolio?.holdings == null) { _lastMoneyDay = -1; return; }
            int day = v.portfolio.processedDay;
            if (_lastMoneyDay < 0 || day <= _lastMoneyDay) { _lastMoneyDay = day; return; }
            long delta = 0;
            foreach (var h in v.portfolio.holdings)
            {
                if (h == null || h.activeShares <= 0 || h.companyId == "BANK") continue;
                var visible = v.companies?.FirstOrDefault(c => c.companyId == h.companyId);
                if (visible == null) continue;
                var bars = DailyBarModel.Build(visible);
                for (int d = _lastMoneyDay + 1; d <= day && d < bars.Length; d++)
                    if (bars[d].Bps.HasValue && bars[d].OpenBps.HasValue)
                        delta += (long)h.activeShares * (bars[d].Bps.Value - bars[d].OpenBps.Value);
            }
            _lastMoneyDay = day;
            if (delta > 0) MoneyFx.Gain(FxLayer);
            else if (delta < 0)
            {
                if (_edgeFxLayer == null) { _edgeFxLayer = UiKit.Rect(Root, "EdgeFxLayer"); UiKit.Stretch(_edgeFxLayer); }
                _edgeFxLayer.SetAsLastSibling();
                MoneyFx.Loss(_edgeFxLayer, ScreenRect); // 바탕은 두고 화면 내용만 흔들림(가장자리에 기본 배경이 비치지 않게)
            }
        }

        private void BuildEdges()
        {
            Edge("Edge_Left", 0, 0, 0, 1, new Vector2(0, EdgeSize), new Vector2(EdgeSize, -EdgeSize), "매수 창으로", () => ShowPage(0));
            Edge("Edge_Right", 1, 0, 1, 1, new Vector2(-EdgeSize, EdgeSize), new Vector2(0, -EdgeSize), "정보 구매 창으로", () => ShowPage(1));
        }

        private void BuildPauseControls()
        {
            _pauseOverlay = UiKit.Panel(Root, "GamePausedOverlay", new Color(0f, 0f, 0f, .85f), raycast: true).rectTransform;
            UiKit.Stretch(_pauseOverlay, 42, 40, 42, 40);
            var boxFrame = UiKit.Panel(_pauseOverlay, "PauseMessage", Palette.CardBorder, rounded: true).rectTransform;
            UiKit.Place(boxFrame, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(480, 180));
            var box = UiKit.Panel(boxFrame, "Fill", Palette.Card, rounded: true).rectTransform; UiKit.Stretch(box, 2, 2, 2, 2);
            var message = UiKit.Label(box, "<b>게임 일시정지</b>\n\n모든 참가자의 타이머와 게임 진행이 멈췄습니다.\n방장이 재개하면 이어서 진행합니다.", 17, Palette.Text);
            UiKit.Stretch(message.rectTransform, 16, 12, 16, 60);
            if (_c.IsHost && _lobby?.SetPaused != null)
            {
                var resume = UiKit.Button(box, "게임 재개", 15, Palette.Accent, Color.white, () => _lobby.SetPaused(false));
                resume.name = "ResumeGame";
                UiKit.Place((RectTransform)resume.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(160, 34));
                _pauseButton = UiKit.Button(Root, "일시정지", 12, Palette.Card, Palette.Text, () => _lobby.SetPaused(!Store.Public.paused));
                _pauseButton.name = "PauseGame";
                UiKit.Place((RectTransform)_pauseButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(42, -3), new Vector2(100, 30));
            }
            _pauseOverlay.gameObject.SetActive(false);
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
            col.offsetMax = new Vector2(152, -12); // 도트 테마: 레퍼런스처럼 넓은 플레이어 칸

            // 턴 표시: 베이지 상자 + 진한 갈색 굵은 글자(도트 테마)
            var turnInner = PixelPanel(col, "TurnBox", new Color32(0x5A, 0x43, 0x26, 0xFF), Palette.Accent, out var turnFrame);
            UiKit.Place(turnFrame.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(140, 56));
            turnInner.GetComponent<Image>().sprite = UiKit.PixelBevel;
            _turnLabel = UiKit.Label(turnInner, "", 15, Palette.OnAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_turnLabel.rectTransform, 2, 2, 2, 4);

            for (int i = 0; i < 4; i++)
            {
                var slot = new RankSlot(col, i, this);
                _slots.Add(slot);
            }

            var sep = UiKit.Panel(ScreenRect, "Separator", Color.clear); // 도트 테마: 구분선 없음(칸마다 테두리)
            sep.rectTransform.anchorMin = new Vector2(0, 0);
            sep.rectTransform.anchorMax = new Vector2(0, 1);
            sep.rectTransform.offsetMin = new Vector2(158, 16);
            sep.rectTransform.offsetMax = new Vector2(159, -16);
        }

        private void BuildPages()
        {
            var viewport = UiKit.Rect(ScreenRect, "PageViewport");
            _pageViewport = viewport;
            UiKit.Stretch(viewport, LeftEdge, 76, 168, 10);
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
            // 기업 선택 차례의 서버 마감으로 남은 시간을 표시한다. 진행 판정은 서버.
            _investmentPage.RemainingSeconds = () => _clock.RemainingSeconds(Store.Public, Time.realtimeSinceStartup);
            _investmentPage.RemainingExact = () => _clock.RemainingExact(Store.Public, Time.realtimeSinceStartup);
            _infoPage = new InfoPurchasePage(info, _c, ScreenRect);
            // 진입·청산(매매) 화면에서 확대했으면, 축소할 때 다시 매매 화면으로 돌아간다.
            _investmentPage.OpenStockPlan = id => { _zoomFromTrade = true; ShowPage(1); _infoPage.OpenCompany(id); };
            _infoPage.Shrunk = () => { if (!_zoomFromTrade) return; _zoomFromTrade = false; ShowPage(0); };

            _pageHint = UiKit.Label(ScreenRect, "", 11, Palette.SubText, TextAnchor.LowerCenter);
            _pageHint.rectTransform.anchorMin = new Vector2(0, 0);
            _pageHint.rectTransform.anchorMax = new Vector2(1, 0);
            _pageHint.rectTransform.offsetMin = new Vector2(LeftEdge, -2);
            _pageHint.rectTransform.offsetMax = new Vector2(-168, 12);
        }

        private void BuildRightColumn()
        {
            var col = UiKit.Rect(ScreenRect, "RightColumn");
            _rightColumn = col;
            col.anchorMin = new Vector2(1, 0);
            col.anchorMax = new Vector2(1, 1);
            col.pivot = new Vector2(1, 0.5f);
            col.offsetMin = new Vector2(-160, 12);
            col.offsetMax = new Vector2(-12, -76);

            _doneButton = UiKit.Button(col, "완료", 16, Palette.Accent, Color.white, OnDoneClicked);
            UiKit.Place((RectTransform)_doneButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(140, 44));
            _developerTimer = UiKit.Button(col, "현재 일차부터 타이머 적용", 11, Palette.AccentSoft, Palette.Accent, () => _c.DeveloperControl("DEV_TIMER"));
            _developerPrevious = UiKit.Button(col, "이전 단계", 13, Palette.AccentSoft, Palette.Accent, () => _c.DeveloperControl("DEV_PREVIOUS"));
            _developerReset = UiKit.Button(col, "현재 단계 초기화", 13, Palette.AccentSoft, Palette.Accent, () => _c.DeveloperControl("DEV_RESET"));
            _developerTimer.name = "DeveloperTimer"; _developerPrevious.name = "DeveloperPrevious"; _developerReset.name = "DeveloperReset";
            UiKit.Place((RectTransform)_developerTimer.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -100), new Vector2(146, 38));
            UiKit.Place((RectTransform)_developerPrevious.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -146), new Vector2(140, 36));
            UiKit.Place((RectTransform)_developerReset.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -190), new Vector2(140, 36));

            // 본인 직업 카드
            // 네모 칸 대신 흰 밑줄만(방송 화면 스타일). 투명 바탕은 클릭·마우스 판정용.
            // 도트 테마: 황토 테두리 카드
            var jobFill = PixelPanel(col, "OwnJob", Palette.CardBorder, Palette.Card, out var job, raycast: true);
            UiKit.Place(job.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(140, 124));
            var inner = UiKit.Rect(job.transform, "Inner");
            UiKit.Stretch(inner, 2, 2, 2, 4);
            // 제목은 위에 고정, 목록은 아래부터 쌓인다(새 증강이 생기면 기존 것 위에 하나씩 추가).
            _abilityTitle = UiKit.Label(inner, "", 14, Palette.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.Stretch(_abilityTitle.rectTransform, 10, 6, 4, 4);
            _abilityLabel = UiKit.Label(inner, "", 13, Palette.Text, TextAnchor.LowerLeft);
            UiKit.Stretch(_abilityLabel.rectTransform, 10, 26, 6, 4);
            // 주식 모드 증강 목록: 새 증강이 맨 아래, 기존은 위로 밀림.
            _augmentList = UiKit.Rect(inner, "AugmentList");
            UiKit.Stretch(_augmentList, 12, 26, 6, 6);
            UiKit.Hover(job.gameObject, () => ShowAllPlayersPopup(job.rectTransform), HidePopup);
            UiKit.Click(job.gameObject, () => { HidePopup(); _features?.OpenAbility(); });
        }

        private void BuildTimer()
        {
            var timerInner = PixelPanel(ScreenRect, "Timer", Palette.CardBorder, Palette.Card, out var timerBox);
            _timerBox = timerBox.rectTransform;
            // 남은 시간 막대(빨강)
            var track = UiKit.Panel(timerInner, "TimerTrack", new Color32(0x1A, 0x12, 0x0C, 0xFF));
            track.rectTransform.anchorMin = new Vector2(0, 0); track.rectTransform.anchorMax = new Vector2(1, 0);
            track.rectTransform.offsetMin = new Vector2(10, 6); track.rectTransform.offsetMax = new Vector2(-10, 11);
            _timerBar = UiKit.Panel(track.transform, "TimerBar", Palette.TimerRed);
            _timerBar.rectTransform.anchorMin = Vector2.zero; _timerBar.rectTransform.anchorMax = new Vector2(1, 1);
            _timerBar.rectTransform.offsetMin = new Vector2(1, 1); _timerBar.rectTransform.offsetMax = new Vector2(-1, -1);
            // 가운데를 오른쪽 열(완료 버튼)의 가운데에 맞춘다(오른쪽 열 폭은 모드마다 달라서 갱신 때 다시 맞춤)
            UiKit.Place(_timerBox, Vector2.one, new Vector2(0.5f, 1f), new Vector2(-86, -6), new Vector2(148, 64));
            _timerText = UiKit.Label(timerInner, "", 14, Palette.Text);
            UiKit.Stretch(_timerText.rectTransform, 6, 2, 6, 12);
        }

        private void BuildBanner()
        {
            var banner = UiKit.Panel(ScreenRect, "Banner", Color.clear); // 도트 테마: 글자 크기에 맞춘 자주색 상자
            var rt = banner.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(LeftEdge, -64);
            rt.offsetMax = new Vector2(-168, -12);
            var boxFill = PixelPanel(rt, "BannerBox", Palette.MaroonBorder, Palette.Maroon, out var boxFrame);
            _bannerBox = boxFrame.rectTransform;
            _bannerText = UiKit.Label(rt, "", 16, Palette.MaroonText);
            UiKit.Stretch(_bannerText.rectTransform, 12, 4, 12, 4);
            _bannerLine = UiKit.Panel(rt, "Underline", Color.clear);
            _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;
            _bannerGroup.blocksRaycasts = false;
        }

        private void BuildOverlays()
        {
            // 암전 + 팝업
            // 어둡게 덮는 층은 화면 전체(위쪽 탭·일시정지·방 나가기까지)를 덮도록 Root에 두고,
            // 정렬 순서를 따로 줘서 다른 요소보다 위에, 팝업은 그보다 한 단계 더 위에 그린다.
            _dim = UiKit.Panel(Root, "Dim", Palette.Dim);
            UiKit.Stretch(_dim.rectTransform);
            var dimCanvas = _dim.gameObject.AddComponent<Canvas>();
            dimCanvas.overrideSorting = true; dimCanvas.sortingOrder = 50;
            _dim.gameObject.SetActive(false);

            _popup = UiKit.Rect(ScreenRect, "Popup");
            var popupCanvas = _popup.gameObject.AddComponent<Canvas>();
            popupCanvas.overrideSorting = true; popupCanvas.sortingOrder = 51;
            _popup.gameObject.AddComponent<GraphicRaycaster>(); // 따로 정렬되는 캔버스도 마우스·클릭을 받게
            _popup.gameObject.SetActive(false);
        }

        private void BuildTurnIndicator()
        {
            _turnHighlight = UiKit.Rect(ScreenRect, "MyTurnHighlight"); UiKit.Stretch(_turnHighlight);
            foreach (bool horizontal in new[] { true, false })
                foreach (int side in new[] { 0, 1 })
                {
                    var edge = UiKit.Panel(_turnHighlight, "TurnEdge", Palette.Accent).rectTransform;
                    edge.anchorMin = horizontal ? new Vector2(0, side) : new Vector2(side, 0);
                    edge.anchorMax = horizontal ? new Vector2(1, side) : new Vector2(side, 1);
                    edge.pivot = horizontal ? new Vector2(.5f, side) : new Vector2(side, .5f);
                    edge.sizeDelta = horizontal ? new Vector2(0, 5) : new Vector2(5, 0);
                    edge.anchoredPosition = Vector2.zero;
                }
            _turnNoticeBackground = UiKit.Panel(ScreenRect, "PublicTurnNotice", Color.clear); // 네모 없이 글자 + 밑줄
            _turnNotice = _turnNoticeBackground.rectTransform;
            _turnNotice.anchorMin = new Vector2(0, 1); _turnNotice.anchorMax = Vector2.one;
            _turnNotice.offsetMin = new Vector2(LeftEdge, -68); _turnNotice.offsetMax = new Vector2(-168, -8);
            PixelPanel(_turnNotice, "NoticeBox", Palette.MaroonBorder, Palette.Maroon, out var noticeFrame);
            _turnNoticeBox = noticeFrame.rectTransform;
            _turnNoticeText = UiKit.Label(_turnNotice, "", 20, Palette.MaroonText, TextAnchor.MiddleCenter, FontStyle.Bold);
            _turnNoticeText.name = "PublicTurnNoticeText";
            UiKit.Stretch(_turnNoticeText.rectTransform, 12, 4, 12, 8);
            _turnNoticeLine = UiKit.Panel(_turnNotice, "Underline", Color.clear);
            _turnHighlight.gameObject.SetActive(false); _turnNotice.gameObject.SetActive(false);
        }

        private void RefreshTurnIndicator()
        {
            var state = Store.Public;
            bool active = state?.publicSelectionActive == true && !state.paused && string.IsNullOrEmpty(Store.EndReason);
            bool mine = active && state.publicSelectionPlayerId == _c.LocalPlayerId;
            _turnHighlight.gameObject.SetActive(false); _turnNotice.gameObject.SetActive(active); // 화면 테두리 강조는 쓰지 않음
            if (!active) return;
            _turnNoticeBackground.color = Color.clear;
            _turnNoticeText.color = mine ? Palette.MaroonText : Palette.Text;
            _turnNoticeText.text = mine
                ? "<size=23>지금 내 차례입니다!</size>  공용 정보 선택\n<size=13>공개할 기업을 선택하세요.</size>"
                : "<size=20>" + state.publicSelectionPlayerId + " 님의 공용 정보 선택 차례</size>\n<size=13>다음 차례까지 공개되는 정보를 확인하세요.</size>";
            FitBox(_turnNoticeBox, _turnNoticeText);
            _turnHighlight.SetAsLastSibling(); _turnNotice.SetAsLastSibling();
        }

        // ------------------------------------------------------------------
        // 갱신
        // ------------------------------------------------------------------

        public void Refresh()
        {
            var s = Store.Public;
            bool stock = s?.stockSystem == true && s.phase != Protocol.Lobby;
            bool stockDraft = stock && s.phase == Protocol.Selection;
            UiKit.Stretch(_pageViewport, LeftEdge, stock ? 70 : 76, stockDraft ? 12 : stock ? 136 : 168, 10);
            bool lobby = s?.phase == Protocol.Lobby; // 대기실: 대기실 상자와 '방 나가기' 외 버튼은 숨김
            _rightColumn.gameObject.SetActive(!stockDraft && !lobby);
            _pageHint.gameObject.SetActive(!stockDraft);
            _rightColumn.offsetMin = new Vector2(stock ? -140 : -160, 12);
            _rightColumn.offsetMax = new Vector2(stock ? -2 : -12, -76);
            ((RectTransform)_doneButton.transform).sizeDelta = new Vector2(stock ? 130 : 140, 44);
            // 타이머를 완료 버튼(오른쪽 열) 가운데에 맞춤
            if (_timerBox != null) _timerBox.anchoredPosition = new Vector2((_rightColumn.offsetMin.x + _rightColumn.offsetMax.x) * 0.5f, -8);
            // Sync before rebuilding clickable cards so a new turn uses its current deadline.
            if (s != null)
            {
                string key = s.turn + "|" + s.phase + "|" + s.selectionIndex + "|" + s.publicSelectionIndex + "|" + s.serverElapsedSeconds + "|" + s.paused;
                if (key != _clockKey)
                {
                    _clockKey = key;
                    _clock.Sync(s.serverElapsedSeconds, Time.realtimeSinceStartup);
                }
            }
            DetectNotifications();
            bool paused = s?.paused == true && string.IsNullOrEmpty(Store.EndReason) && s.phase != Protocol.Ended;
            _pauseOverlay.gameObject.SetActive(paused);
            if (_pauseButton != null)
            {
                _pauseButton.gameObject.SetActive(s?.phase != Protocol.Lobby);
                bool can = s != null && s.phase != Protocol.Lobby && s.phase != Protocol.Ended && string.IsNullOrEmpty(Store.EndReason);
                UiKit.SetButton(_pauseButton, paused ? "게임 재개" : "일시정지", can, paused ? Palette.AccentSoft : Palette.Card, Palette.Text);
            }

            _turnLabel.text = s == null ? "" : "<size=19>TURN " + s.turn + (s.stockSystem ? "/" + s.totalTurns : "") + "</size>\n<size=12>" + PhaseNames.ForState(s) + "</size>";
            RefreshRanks();
            _investmentPage.Refresh();
            _infoPage.Refresh();
            RefreshTurnIndicator();
            var ability = LiveScreenModels.AbilityId(Store.View?.ability?.selectedId);
            _abilityTitle.text = s?.stockSystem == true ? "내 증강" : "내 고유능력";
            _abilityLabel.text = (AbilityCatalog.Get(ability)?.Name ?? "선택 대기") + "\n<size=11>클릭하여 상태 보기</size>";
            // 새로 얻은 증강이 맨 아래에 붙고, 기존 증강은 위로 밀려 올라간다. 왼쪽 정렬 + 앞에 '-'.
            if (s?.stockSystem == true) { _abilityLabel.text = ""; RefreshAugmentList(Store.View?.portfolio?.augments ?? new string[0]); }
            else if (_augmentList != null && _augmentList.childCount > 0) { UiKit.Clear(_augmentList); _augmentKey = null; }
            UpdateTimer();
            _renderedPublic = Store.Public; _renderedView = Store.View;
        }

        private void DetectNotifications()
        {
            var s = Store.Public;
            if (s != null)
            {
                string phaseKey = s.turn + "|" + s.phase + "|" + s.developerRevision;
                var day = _c.HasCurrentPlayerView ? StockRules.CurrentDay(s, Store.View?.portfolio) : null;
                bool contextChanged = phaseKey != _lastPhaseKey || day != _lastDay;
                if (contextChanged) ClearBanners();
                if (phaseKey != _lastPhaseKey)
                {
                    _lastCoins = -1;
                    _lastActiveShares = -1;
                    _lastMoneyDay = -1;
                    _lastSelector = "";
                    _lastPhaseKey = phaseKey;
                    if (s.phase == Protocol.Lobby) ShowBanner("대기실에 입장했습니다. 준비를 누르세요.");
                    else if (s.phase == Protocol.Ended && !string.IsNullOrEmpty(Store.EndReason)) ShowBanner(Palette.BadText(ErrorMessages.ToKorean(Store.EndReason)));
                    else if (s.phase != Protocol.Ended && !day.HasValue) ShowBanner("<b>" + s.turn + "턴 · " + PhaseNames.ForState(s) + "</b> 단계가 시작되었습니다.");
                    if (s.phase != Protocol.Lobby && s.phase != Protocol.Ended) UiSound.Play(UiSound.Kind.Turn); // 새 단계 시작음
                    // 화면 전환 효과: 결과 발표처럼 화면 가로 띠(대기실·종료·정산 결과는 제외: 정산은 '결과 발표'가 따로 나옴)
                    if (s.phase != Protocol.Lobby && s.phase != Protocol.Ended && s.phase != Protocol.Result)
                        PhaseTransitionFx.Play(FxLayer, (s.stockSystem ? s.turn + "/" + s.totalTurns : s.turn.ToString()) + "턴", PhaseNames.ForState(s));
                    if (s.phase == Protocol.Info || s.phase == Protocol.Reveal) ShowPage(1);
                    if (s.phase == Protocol.Lobby || s.phase == Protocol.Selection || s.phase == StockRules.AugmentPhase || s.phase == Protocol.Investment || s.phase == Protocol.Result) ShowPage(0);
                }
                if (s.phase == Protocol.Selection && s.currentPlayerId != _lastSelector)
                {
                    ClearBanners();
                    _lastSelector = s.currentPlayerId;
                    if (s.currentPlayerId == _c.LocalPlayerId) UiSound.Play(UiSound.Kind.Turn); // 내 차례 알림음
                    if (s.currentPlayerId == _c.LocalPlayerId) ShowBanner(Palette.GoodText("<b>내 차례입니다.</b>") + " " + (s.stockSystem ? "주식 보유량을 정하세요." : "기업을 선택하세요."));
                }
                string publicSelector = s.publicSelectionActive ? s.turn + "|" + s.publicSelectionIndex + "|" + s.publicSelectionPlayerId : "";
                if (publicSelector != _lastPublicSelector)
                {
                    ClearBanners();
                    _lastPublicSelector = publicSelector;
                    if (s.publicSelectionActive)
                    {
                        if (s.publicSelectionPlayerId == _c.LocalPlayerId) ShowPage(1);
                    }
                }
                if (contextChanged && day.HasValue)
                    ShowBanner("<b>" + s.turn + "턴 · 현재 " + day.Value + "일차</b> 주가 공개");
                _lastDay = day;
            }

            var v = Store.View;
            if (v != null)
            {
                // 코인으로 정보를 살 때마다 뜨던 알림은 너무 자주 나와서 띄우지 않는다(남은 코인은 정보 구매 창 위쪽에 항상 표시됨)
                // 효과음: 같은 단계 안에서 코인이 줄면(정보 구매) 짤랑, 보유 주식이 늘면 매수음·줄면 매도음
                if (_lastCoins >= 0 && v.coins < _lastCoins) UiSound.Play(UiSound.Kind.Coin);
                _lastCoins = v.coins;
                int activeShares = v.portfolio?.holdings?.Sum(h => h.activeShares) ?? 0;
                if (_lastActiveShares >= 0 && activeShares != _lastActiveShares)
                    UiSound.Play(activeShares > _lastActiveShares ? UiSound.Kind.Buy : UiSound.Kind.Sell);
                _lastActiveShares = activeShares;
                DetectMoneyChange(s, v);
            }

            if (Store.EndReason != _lastEndReason)
            {
                _lastEndReason = Store.EndReason;
                if (!string.IsNullOrEmpty(_lastEndReason)) { ClearBanners(); ShowBanner(Palette.BadText(ErrorMessages.ToKorean(_lastEndReason))); }
            }

            if (Store.OrderLocked && !_lastLocked && (s?.phase == Protocol.Investment || s?.phase == Protocol.Reveal)) ShowBanner(Palette.GoodText("주문이 확정되었습니다."));
            _lastLocked = Store.OrderLocked;

            var board = Store.Scoreboard;
            if (board != null && board.turn != _lastBoardTurn && board.rows != null && board.rows.Length > 0
                && board.turn == s?.turn && (s.phase == Protocol.Result || s.phase == Protocol.Ended))
            {
                _lastBoardTurn = board.turn;
                var top = board.rows.OrderBy(r => r.rank).First();
                var mine = board.rows.FirstOrDefault(r => r.playerId == _c.LocalPlayerId);
                // 차트(시장 공개)를 다 본 뒤 최종 결과가 나오는 순간: 매 턴 크게 '결과 발표' → 이어서 내 정산 카드·역전 표시
                var fx = FxLayer;
                ResultAnnouncement.Play(fx, board.turn, board.gameComplete);
                const float afterAnnouncement = 1.8f;
                if (mine != null)
                {
                    long before = _fxAssets.TryGetValue(mine.playerId, out var b) ? b : mine.totalAssets - mine.cumulativeProfit;
                    int turnNo = board.turn, rank = mine.rank, ret = mine.turnReturnBps; long now = mine.totalAssets; bool complete = board.gameComplete;
                    DelayedAction.Run(fx, afterAnnouncement, () => SettlementFx.Play(FxLayer, turnNo, complete, rank, ret, before, now));
                }
                // 역전: 직전 순위와 비교해 누가 누구를 제쳤는지
                var reversal = ReversalFx.Describe(_fxRanks, board.rows.Where(r => r != null).Select(r => new KeyValuePair<string, int>(r.playerId, r.rank)),
                    id => id == _c.LocalPlayerId ? id + "(나)" : id, out bool reversalMine, _c.LocalPlayerId);
                if (reversal != null) ReversalFx.Play(fx, reversal, reversalMine, afterAnnouncement + 0.8f);
                foreach (var row in board.rows) if (row != null) { _fxAssets[row.playerId] = row.totalAssets; _fxRanks[row.playerId] = row.rank; }
                ShowBanner(board.gameComplete ? (s.totalTurns > 0 ? s.totalTurns : 10) + "턴 최종 승자 · " + board.winnerPlayerId :
                    board.turn + "턴 정산 완료 · 1위 " + top.playerId + " (" + DisplayFormat.Percent(top.turnReturnBps) + ")");
            }
        }

        private string _lastEndReason = "";

        private void OnAutomatic(string resultType)
        {
            if (resultType == CommandTypes.ResultOf(CommandTypes.SelectCompany) && Store.Public?.phase == Protocol.Selection)
                ShowBanner("<color=#E0D2B4>시간이 초과되어 기업이 자동 선택되었습니다.</color>");
            else if (resultType == CommandTypes.ResultOf(CommandTypes.SubmitInvestment) && (Store.Public?.phase == Protocol.Investment || Store.Public?.phase == Protocol.Reveal))
                ShowBanner("<color=#E0D2B4>투자 시간이 끝나 주문이 자동 확정되었습니다. 적용된 구간과 비중은 확정 주문에서 확인하세요.</color>");
        }

        private void OnError(ClientError error)
        {
            ShowBanner(Palette.BadText(ErrorMessages.ToKorean(error.ErrorCode)));
        }

        // 순위 칸 애니메이션용: 직전에 보여 준 순서와 총자산
        private List<string> _shownOrder = new List<string>();
        private readonly Dictionary<string, long> _shownAssets = new Dictionary<string, long>();

        /// <summary>정산 결과로 순서가 바뀌면 각 칸이 이전 자리에서 새 자리로 미끄러지듯 이동하고,
        /// 총자산은 이전 값에서 새 값으로 세어 올라간다(표시 효과만, 값은 서버 결과 그대로).</summary>
        private void RefreshRanks()
        {
            var rows = RankRows();
            var order = rows.Select(r => r.PlayerId).ToList();
            bool reordered = _shownOrder.Count > 0 && rows.Any(r => r.Rank > 0) && !order.SequenceEqual(_shownOrder);
            for (int i = 0; i < _slots.Count; i++)
            {
                var row = i < rows.Count ? rows[i] : null;
                int from = reordered && row != null ? _shownOrder.IndexOf(row.PlayerId) : -1;
                long? prevAssets = null;
                if (row?.Score != null && _shownAssets.TryGetValue(row.PlayerId, out var before)) prevAssets = before;
                _slots[i].Set(row, _c.LocalPlayerId, from, prevAssets, i * 0.08f);
            }
            _shownOrder = order;
            foreach (var r in rows)
                if (r.Score != null) _shownAssets[r.PlayerId] = r.Score.totalAssets;
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

        // ------------------------------------------------------------------
        // 매 프레임: 타이머·전광판·슬라이드
        // ------------------------------------------------------------------

        private void Update()
        {
            if (_c == null) return;
            // A missed change callback must not leave the new turn using an old timer/UI.
            if (!ReferenceEquals(_renderedPublic, Store.Public) || !ReferenceEquals(_renderedView, Store.View)) Refresh();
            if (Store.Public?.turn > 0 && !_c.HasCurrentPlayerView && string.IsNullOrEmpty(Store.EndReason))
            {
                if (_missingViewSince < 0) _missingViewSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _missingViewSince >= .5f && _viewRefreshTurn != Store.Public.turn)
                { _viewRefreshTurn = Store.Public.turn; _c.RefreshPlayerView(); }
            }
            else _missingViewSince = -1;
            UpdateTimer();
            UpdateUrgency();
            ClearDraftsOnOutsideClick();
            if (Store.Public?.phase == Protocol.Selection) _investmentPage.Tick();
            UpdateBanner();

            _pageT = Mathf.MoveTowards(_pageT, _pageTarget, Time.unscaledDeltaTime * 4f);
            float e = Mathf.SmoothStep(0, 1, _pageT);
            _pager.anchorMin = new Vector2(-e, 0);
            _pager.anchorMax = new Vector2(2 - e, 1);
            _pageHint.text = ""; // 화면 아래 안내 문구는 표시하지 않음(가장자리 클릭 이동은 그대로 동작)


        }

        private readonly List<RaycastResult> _clickHits = new List<RaycastResult>();

        /// <summary>차트·버튼이 아닌 곳을 누르면 확정(예약·구매)하지 않은 구간 선택 표시를 지운다.</summary>
        private void ClearDraftsOnOutsideClick()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (_infoPage == null || mouse == null || !mouse.leftButton.wasPressedThisFrame || EventSystem.current == null) return;
            var data = new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() };
            _clickHits.Clear();
            EventSystem.current.RaycastAll(data, _clickHits);
            if (_clickHits.Count > 0 && _clickHits[0].gameObject != null)
            {
                var hit = _clickHits[0].gameObject;
                if (hit.GetComponentInParent<Selectable>() != null || hit.GetComponentInParent<ChartPointerLayer>() != null
                    || hit.GetComponentInParent<BarChartGraphic>() != null) return; // 차트·버튼·입력칸을 누른 것
            }
            _infoPage.ClearUnconfirmed();
        }

        /// <summary>단계 마감 10초 전부터 가장자리를 붉게(남을수록 진하게). 시장 공개·정산·대기실·종료·일시정지는 제외.</summary>
        private void UpdateUrgency()
        {
            if (_urgency == null) return;
            var s = Store.Public;
            float level = 0f;
            if (s != null && !s.paused && s.phaseEndsAtServerSeconds > 0
                && s.phase != Protocol.Lobby && s.phase != Protocol.Ended && s.phase != Protocol.Reveal && s.phase != Protocol.Result)
            {
                double remain = s.phaseEndsAtServerSeconds - _clock.EstimatedServerSeconds(Time.realtimeSinceStartup);
                if (remain > 0 && remain <= 10) level = 0.45f + 0.55f * (float)(1 - remain / 10);
            }
            _urgency.SetLevel(level);
            _urgency.Update();
        }

        private void UpdateTimer()
        {
            var s = Store.Public;
            bool developer = s?.developerMode == true && s.stockSystem && _c.IsHost && s.turn > 0 && s.phase != Protocol.Ended;
            _developerTimer.gameObject.SetActive(developer && s.phase == Protocol.Investment);
            _developerPrevious.gameObject.SetActive(developer); _developerReset.gameObject.SetActive(developer);
            UiKit.SetButton(_developerTimer, s?.developerTimerRunning == true ? "타이머 중지 · 수동 전환" : "현재 일차부터 타이머 적용",
                developer && !s.paused, Palette.AccentSoft, Palette.Accent);
            _developerPrevious.interactable = developer && !s.paused && s.developerCanGoBack;
            _developerReset.interactable = developer && !s.paused;
            UiKit.Place((RectTransform)_doneButton.transform, new Vector2(.5f, developer ? 1 : .5f), new Vector2(.5f, developer ? 1 : .5f),
                new Vector2(0, developer ? -48 : -12), new Vector2(140, 44));
            if (s == null)
            {
                _timerText.text = "접속 대기";
                UiKit.SetButton(_doneButton, "상태 갱신 중", false, Palette.Disabled, Palette.DisabledText);
                return;
            }

            if (!PhaseClock.HasDeadline(s) && !s.developerMode)
            {
                _timerText.text = ""; // '시간 제한 없음' 문구는 표시하지 않음
                _timerText.color = Palette.Text;
                UiKit.SetButton(_doneButton, "완료", false, Palette.Disabled, Palette.DisabledText);
                return;
            }

            if (s.paused)
            {
                _timerText.text = "<size=11>일시정지</size>\n<size=28><b>" + DisplayFormat.Clock(_clock.RemainingSeconds(s, Time.realtimeSinceStartup)) + "</b></size>";
                _timerText.color = Palette.Text;
                UiKit.SetButton(_doneButton, "일시정지 중", false, Palette.Disabled, Palette.DisabledText);
                return;
            }

            int remain = _clock.RemainingSeconds(s, Time.realtimeSinceStartup);
            string what = s.publicSelectionActive ? "공용 공개 " + (s.publicSelectionIndex + 1) + "/4 · " + s.publicSelectionPlayerId
                : s.phase == Protocol.Selection
                ? (string.IsNullOrEmpty(s.currentPlayerId) ? "선택" : s.currentPlayerId + " 차례")
                : PhaseNames.Korean(s.phase);
            _timerText.text = "<size=11>" + (remain == 0 ? "서버 진행 대기" : what) + "</size>\n<size=26><color=#" + ColorUtility.ToHtmlStringRGB(Palette.TimerRed) + ">" + DisplayFormat.Clock(remain) + "</color></size>";
            UpdateTimerBar(s);
            if (s.developerMode && !PhaseClock.HasDeadline(s)) _timerText.text = "<size=11>개발자 모드 · " + what + "</size>\n<size=22><b>수동 진행</b></size>";
            _timerText.color = Palette.Text; // 숫자는 도트 테마의 빨간 숫자

            bool can = _c.CanCompleteStep();
            bool done = _c.IsDoneForCurrentPhase;
            string label = done ? "완료함 · 대기 중"
                : s.phase == Protocol.Investment ? (Store.OrderLocked ? "다른 참가자 대기" : s.stockSystem ? "실시간 매매 중" : "주문 확정 시 진행")
                : s.phase == Protocol.Selection ? (s.stockSystem ? "매수 완료 시 진행" : "선택 시 자동 진행")
                : "완료";
            if (s.developerMode && can) label = s.stockSystem && s.phase == Protocol.Investment ? "확인 · 다음 일차" : "확인 · 다음 단계";
            _doneButton.name = developer ? "DeveloperNext" : s.stockSystem && s.phase == Protocol.Investment ? "LiveTradingStatus" : "Button";
            UiKit.SetButton(_doneButton, label, can, can ? Palette.Accent : Palette.Disabled, can ? Color.white : Palette.DisabledText);
        }

        /// <summary>남은 시간 막대: 이번 단계에서 본 가장 긴 남은 시간을 100%로 본다.</summary>
        private void UpdateTimerBar(PublicState s)
        {
            if (_timerBar == null || s == null) return;
            string key = s.turn + "|" + s.phase + "|" + s.selectionIndex + "|" + s.publicSelectionIndex + "|" + s.developerRevision;
            float left = (float)_clock.RemainingExact(s, Time.realtimeSinceStartup);
            if (key != _timerKey) { _timerKey = key; _timerMax = 0f; }
            _timerMax = Mathf.Max(_timerMax, left);
            float f = _timerMax > 0f ? Mathf.Clamp01(left / _timerMax) : 0f;
            _timerBar.rectTransform.anchorMax = new Vector2(f, 1);
        }

        private void RefreshAugmentList(string[] augments)
        {
            string key = string.Join(",", augments);
            if (key == _augmentKey) return;
            _augmentKey = key; UiKit.Clear(_augmentList);
            const float rowHeight = 20f;
            for (int i = 0; i < augments.Length; i++)
            {
                float y = (augments.Length - 1 - i) * rowHeight; // 마지막(새) 증강이 맨 아래
                var row = UiKit.Rect(_augmentList, "Augment_" + augments[i]);
                row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(0, 0);
                row.offsetMin = new Vector2(0, y); row.offsetMax = new Vector2(0, y + rowHeight);
                var label = UiKit.Label(row, "- " + StockRules.Name(augments[i]), 13, Palette.Text, TextAnchor.MiddleLeft);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        private void UpdateBanner()
        {
            // 공용 정보 차례 안내가 같은 자리에 떠 있으면 알림은 잠시 기다린다(바탕이 없어 글자가 겹치지 않게)
            if (_turnNotice != null && _turnNotice.gameObject.activeSelf) { _bannerGroup.alpha = 0f; return; }
            if (_bannerLeft <= 0f && _bannerQueue.Count > 0)
            {
                _bannerText.text = _bannerQueue.Dequeue();
                FitBox(_bannerBox, _bannerText);
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

        private void ClearBanners()
        {
            _bannerQueue.Clear(); _bannerLeft = 0f;
            _bannerText.text = ""; _bannerGroup.alpha = 0f;
        }

        /// <summary>0 = 매수 창, 1 = 정보 구매 창.</summary>
        private bool _zoomFromTrade;
        public void ShowPage(int index)
        {
            _pageTarget = Mathf.Clamp01(index);
            if (index == 0) _zoomFromTrade = false;
            if (_pageTarget < .5f) _infoPage?.Collapse();
        }

        private void OnDoneClicked()
        {
            var status = _c.CompleteStep();
            if (status == CommandStatus.Sent && Store.Public?.developerMode != true)
                ShowBanner(Store.Public?.phase == Protocol.Investment ? Palette.GoodText("주문을 확정했습니다.") : Palette.GoodText("완료했습니다.") + " 다른 참가자를 기다립니다.");
        }

        // ------------------------------------------------------------------
        // 팝업(마우스 올림)
        // ------------------------------------------------------------------

        internal void ShowPlayerPopup(RankRow row, RectTransform slot)
        {
            if (row == null) return;
            bool stock = Store.Public?.stockSystem == true;
            var popup = OpenPopup(stock ? new Vector2(460, 540) : new Vector2(320, 230));
            PositionNextTo(popup, slot, rightSide: true);

            var r = row.Score;
            if (stock)
            {
                ShowStockPlayerPopup(popup, row, r);
                return;
            }
            string title = "<b>" + row.PlayerId + (row.PlayerId == _c.LocalPlayerId ? " (나)" : "") + "</b>";
            string body = r == null
                ? "\n<color=#B39C74>아직 정산 결과가 없습니다.</color>"
                : "\n이번 턴 수익률  <b><color=#" + ColorUtility.ToHtmlStringRGB(Palette.ForBps(r.turnReturnBps)) + ">" + DisplayFormat.Percent(r.turnReturnBps) + "</color></b>"
                  + "\n누적 손익  <b>" + DisplayFormat.SignedMoney(r.cumulativeProfit) + "</b>   ·   누적 수익률  <b>" + DisplayFormat.Percent(r.cumulativeReturnBps) + "</b>"
                  + "\n총자산  <b>" + DisplayFormat.Money(r.totalAssets) + "</b>";
            if (stock)
            {
                var augments = Store.Public.players?.FirstOrDefault(p => p.playerId == row.PlayerId)?.augments ?? System.Array.Empty<string>();
                body += "\n\n<b>보유 증강 (" + augments.Length + "/2)</b>\n" + (augments.Length == 0 ? "선택한 증강이 없습니다." :
                    string.Join("\n\n", augments.Select(id => "<b>" + StockRules.Name(id) + "</b> · " + StockRules.Description(id))));
            }
            var text = UiKit.Label(popup, title + body, 14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 14, 10, 14, stock ? 112 : (r?.returnHistory?.Length > 0 ? 112 : 10));

            if (stock)
            {
                var augmentIds = Store.Public.players?.FirstOrDefault(p => p.playerId == row.PlayerId)?.augments ?? System.Array.Empty<string>();
                float gap = 84f;
                float start = -(augmentIds.Length - 1) * gap * 0.5f;
                for (int i = 0; i < augmentIds.Length; i++)
                {
                    var artwork = AugmentArtwork.Create(popup, augmentIds[i], 72);
                    UiKit.Place(artwork, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(start + i * gap, 20), new Vector2(72, 72));
                }
            }

            if (r?.returnHistory != null && r.returnHistory.Length > 0)
                AddTrendChart(popup, new[] { r }, new Vector2(14, stock ? 108 : 12), 92);
        }

        private void ShowStockPlayerPopup(RectTransform popup, RankRow row, ScoreRow score)
        {
            string title = "<b>" + row.PlayerId + (row.PlayerId == _c.LocalPlayerId ? " (나)" : "") + "</b>";
            string summary = score == null
                ? "\n<color=#7A6748>아직 정산 결과가 없습니다.</color>"
                : "\n이번 턴 수익률 <b><color=#" + ColorUtility.ToHtmlStringRGB(Palette.ForBps(score.turnReturnBps)) + ">" + DisplayFormat.Percent(score.turnReturnBps) + "</color></b>"
                  + "\n누적 손익 <b>" + DisplayFormat.SignedMoney(score.cumulativeProfit) + "</b> · 누적 수익률 <b>" + DisplayFormat.Percent(score.cumulativeReturnBps) + "</b>"
                  + "\n총자산 <b>" + DisplayFormat.Money(score.totalAssets) + "</b>";
            var summaryText = UiKit.Label(popup, title + summary, 14, Palette.Text, TextAnchor.UpperLeft);
            // 총자산 아래에 바로 증강 영역이 이어지도록 요약 영역의 하단 여백을 줄입니다.
            UiKit.Stretch(summaryText.rectTransform, 14, 10, 14, 130);

            var augmentIds = Store.Public.players?.FirstOrDefault(p => p.playerId == row.PlayerId)?.augments ?? System.Array.Empty<string>();
            var heading = UiKit.Label(popup, "<b>보유 증강 (" + augmentIds.Length + "/2)</b>", 14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Place(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -134), new Vector2(300, 24));

            for (int i = 0; i < augmentIds.Length; i++)
            {
                string id = augmentIds[i];
                var item = UiKit.Panel(popup, "PlayerAugment_" + id, Palette.Card, rounded: true);
                var itemTransform = item.rectTransform;
                itemTransform.anchorMin = new Vector2(0, 1);
                itemTransform.anchorMax = new Vector2(1, 1);
                itemTransform.pivot = new Vector2(.5f, 1);
                float top = 162 + i * 112;
                itemTransform.offsetMin = new Vector2(14, -top - 96);
                itemTransform.offsetMax = new Vector2(-14, -top);

                var artwork = AugmentArtwork.Create(item.transform, id, 82);
                UiKit.Place(artwork, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(8, 0), new Vector2(82, 82));
                var detail = UiKit.Label(item.transform,
                    "<b>" + StockRules.Name(id) + "</b>\n" + StockRules.Description(id),
                    13, Palette.Text, TextAnchor.MiddleLeft);
                UiKit.Stretch(detail.rectTransform, 104, 8, 8, 8);
            }

            if (augmentIds.Length == 0)
            {
                var none = UiKit.Label(popup, "선택한 증강이 없습니다.", 13, Palette.SubText, TextAnchor.UpperLeft);
                UiKit.Place(none.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -162), new Vector2(300, 24));
            }

            if (score?.returnHistory != null && score.returnHistory.Length > 0)
                AddTrendChart(popup, new[] { score }, new Vector2(14, 14), 92);
        }

        private void ShowAllPlayersPopup(RectTransform anchor)
        {
            var popup = OpenPopup(new Vector2(380, 250));
            PositionNextTo(popup, anchor, rightSide: false);
            var board = Store.Scoreboard;

            var title = UiKit.Label(popup, "<b>플레이어별 수익률 추이</b>   <size=12>클릭: 내 고유능력 상태 보기</size>", 14, Palette.Text, TextAnchor.UpperLeft);
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
                var pts = (r.returnHistory ?? new ReturnPoint[0]).Where(p => p != null && p.turn > 0).OrderBy(p => p.turn).Select(p => new Vector2(p.turn, p.returnBps)).ToList();
                pts.Insert(0, Vector2.zero); // 시작 지점: 0턴 0%
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
        private readonly int _index;
        private readonly Image _border, _icon;
        private readonly Text _text, _assets, _rank;
        private readonly RectTransform _rt;
        private RankRow _row;
        private string _delta = "";

        // 도트 테마(레퍼런스): 가로로 긴 상자 = 왼쪽 플레이어 얼굴 아이콘 + 이름(굵게) + 아래 작은 줄(총자산), 오른쪽 위 순위
        public static Vector2 PositionOf(int index) => new Vector2(0, -66 - index * 74);

        public RankSlot(RectTransform column, int index, TabletView owner)
        {
            _index = index;
            var bg = UiKit.Panel(column, "RankSlot" + (index + 1), Palette.CardBorder, rounded: true, raycast: true);
            _rt = bg.rectTransform;
            UiKit.Place(_rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), PositionOf(index), new Vector2(140, 66));
            _border = UiKit.Panel(_rt, "Fill", Palette.Card, rounded: true);
            UiKit.Stretch(_border.rectTransform, 2, 2, 2, 2);
            var inner = UiKit.Panel(_rt, "Inner", Color.clear);
            UiKit.Stretch(inner.rectTransform, 2, 2, 2, 2);

            // 얼굴 아이콘(플레이어 색으로 물든 도트 얼굴) + 어두운 받침
            var iconBack = UiKit.Panel(inner.transform, "IconBack", new Color32(0x1A, 0x12, 0x0C, 0xFF), rounded: true);
            UiKit.Place(iconBack.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(8, 0), new Vector2(36, 36));
            _icon = UiKit.Panel(iconBack.transform, "PlayerFace", Color.white);
            _icon.sprite = UiKit.PixelFace; _icon.preserveAspect = true;
            UiKit.Stretch(_icon.rectTransform, 3, 3, 3, 3);

            // 이름(굵게) + 연결 끊김
            _text = UiKit.Label(inner.transform, "", 15, Palette.Text, TextAnchor.LowerLeft, FontStyle.Bold);
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.rectTransform.anchorMin = new Vector2(0, .5f); _text.rectTransform.anchorMax = new Vector2(1, 1);
            _text.rectTransform.offsetMin = new Vector2(52, 0); _text.rectTransform.offsetMax = new Vector2(-4, -6);
            // 순위(오른쪽 위, 작게)
            _rank = UiKit.Label(inner.transform, "", 11, Palette.SubText, TextAnchor.UpperRight);
            _rank.horizontalOverflow = HorizontalWrapMode.Overflow;
            _rank.rectTransform.anchorMin = new Vector2(1, 1); _rank.rectTransform.anchorMax = new Vector2(1, 1);
            _rank.rectTransform.pivot = new Vector2(1, 1);
            _rank.rectTransform.anchoredPosition = new Vector2(-6, -4); _rank.rectTransform.sizeDelta = new Vector2(60, 14);
            // 아래 작은 줄: 총자산
            _assets = UiKit.Label(inner.transform, "", 11, Palette.SubText, TextAnchor.UpperLeft);
            _assets.name = "RankAssets";
            _assets.horizontalOverflow = HorizontalWrapMode.Overflow;
            _assets.rectTransform.anchorMin = new Vector2(0, 0); _assets.rectTransform.anchorMax = new Vector2(1, .5f);
            _assets.rectTransform.offsetMin = new Vector2(52, 4); _assets.rectTransform.offsetMax = new Vector2(-4, -2);

            // 플레이어 칸을 클릭해서 팝업을 고정하지 않고, 마우스가 칸 위에 있을 때만 표시합니다.
            // 칸에서 마우스가 벗어나면 즉시 숨겨서 이전처럼 팝업이 화면에 남지 않게 합니다.
            UiKit.Hover(bg.gameObject, () => owner.ShowPlayerPopup(_row, _rt), owner.HidePopup);
        }

        /// <param name="fromIndex">이번에 순서가 바뀌었으면 이 플레이어가 있던 칸 번호, 아니면 -1</param>
        /// <param name="prevAssets">직전에 보여 준 총자산(없으면 null → 세는 효과 없음)</param>
        public void Set(RankRow row, string localPlayerId, int fromIndex = -1, long? prevAssets = null, float delay = 0f)
        {
            bool samePlayer = _row != null && row != null && _row.PlayerId == row.PlayerId;
            _row = row;
            _rt.gameObject.SetActive(row != null);
            if (row == null) return;

            if (fromIndex >= 0 && fromIndex != _index)
            {
                bool up = fromIndex > _index;
                _delta = up ? " <color=#FF4D4F>▲" + (fromIndex - _index) + "</color>" : " <color=#3D8BFF>▼" + (_index - fromIndex) + "</color>";
                if (up) _rt.SetAsLastSibling(); // 올라오는 칸이 위에 보이게
                UiAnim.Slide(_rt, PositionOf(fromIndex), PositionOf(_index), UiAnim.SlideSeconds, delay);
                if (up) UiAnim.Pulse(_rt, delay + UiAnim.SlideSeconds * 0.8f, 1.1f);
            }
            else if (fromIndex >= 0 || !samePlayer) _delta = "";

            // 누구인지는 왼쪽 얼굴 아이콘 색으로 구분
            _icon.color = Palette.Player(row.PlayerId);
            _rank.text = (row.Rank > 0 ? row.Rank + "등" : "") + _delta;
            string me = row.PlayerId == localPlayerId ? " (나)" : "";
            _text.text = row.PlayerId + me + (row.Connected ? "" : " " + Palette.BadText("<size=10>끊김</size>"));
            bool mine = row.PlayerId == localPlayerId;
            _border.color = mine ? new Color32(0x33, 0x24, 0x16, 0xFF) : Palette.Card; // 내 칸은 살짝 밝게

            if (row.Score == null) { _assets.text = row.Connected ? "대기 중" : Palette.BadText("연결 끊김"); return; }
            long now = row.Score.totalAssets;
            UiAnim.Count(_assets, prevAssets ?? now, now, v => DisplayFormat.Money(v), UiAnim.CountSeconds, delay);
        }
    }
}
