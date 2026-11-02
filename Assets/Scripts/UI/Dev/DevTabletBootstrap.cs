// [개발 전용] 빈 씬의 GameObject에 붙이고 Play.
// A의 실제 GameRoom(진행·시간·자동 행동·재전송 캐시)을 같은 프로세스에서 돌리고,
// B 자리에 개발용 대역, C 자리에 실제 정보 서비스를, 다른 참가자 3명 자리에 봇을 넣어 D 화면을 한 턴 끝까지 확인한다.
// 실제 4인 접속은 A의 NetworkSession.Host / Connect + 실제 B·C 서비스로 한다.
using System.Collections.Generic;
using BlackWhale.Flow;
using BlackWhale.Shared;
using BlackWhale.UI.State;
using BlackWhale.UI.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlackWhale.UI.Dev
{
    public sealed class DevTabletBootstrap : MonoBehaviour
    {
        [Tooltip("내가 볼 참가자. P1 = 방장(방장도 같은 개인 필터 화면과 같은 요청 경로를 쓴다).")]
        [SerializeField] private string viewAsPlayer = "P1";
        [Tooltip("켜면 서버 시계가 자동으로 흐르지 않는다(A의 ManualForFixture). DEV 패널의 '다음 단계'로 진행.")]
        [SerializeField] private bool manualTimer;
        [Tooltip("봇이 행동하기 전 기다리는 시간(초)")]
        [SerializeField] private float botThinkSeconds = 1.2f;

        private RectTransform _canvasRoot;
        private LocalRoomHost _host;
        private DevInformationService _information;
        private LocalGameClient _myClient;
        private GameClientController _controller;
        private readonly List<DevBot> _bots = new List<DevBot>();
        private TabletView _view;
        private string _source;

        private RectTransform _devPanel;
        private Text _devStatus;

        private void Start()
        {
            EnsureEventSystem();
            _canvasRoot = CreateCanvas();
            Restart();
        }

        private void OnDestroy() => Teardown();

        // ------------------------------------------------------------------
        // 준비
        // ------------------------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private RectTransform CreateCanvas()
        {
            var go = new GameObject("TabletCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        private void Teardown()
        {
            if (_view != null) Destroy(_view.gameObject);
            _controller?.Dispose();
            foreach (var bot in _bots) bot.Controller.Dispose();
            _bots.Clear();
            _host?.Dispose();
            _host = null;
            _controller = null;
        }

        private void Restart()
        {
            Teardown();

            DevFixtures.Load(out var market, out var scoreboard, out _source);
            var marketService = new DevMarketService(market, scoreboard);
            _information = new DevInformationService(marketService);
            _host = new LocalRoomHost(marketService, _information,
                manualTimer ? TimerMode.ManualForFixture : TimerMode.Realtime, fixedSeatOrder: true);

            // 먼저 입장한 사람이 P1. 내가 P2로 보고 싶으면 봇 하나를 먼저 들여보낸다.
            int myIndex = Mathf.Clamp(viewAsPlayer.Length == 2 ? viewAsPlayer[1] - '1' : 0, 0, 3);
            for (int i = 0; i < 4; i++)
            {
                if (i == myIndex)
                {
                    _myClient = _host.Join("나");
                    _controller = new GameClientController(_myClient);
                }
                else
                {
                    _bots.Add(new DevBot(_host.Join("Bot " + (i + 1)), botThinkSeconds));
                }
            }

            _view = TabletView.Create(_canvasRoot, _controller);
            BuildDevPanel();
            _host.Pump(); // 입장 메시지 전달
            _view.ShowBanner("개발 모드 · A GameRoom + 실제 C + B 대역 · " + _source);
        }

        private void Update()
        {
            if (_host == null) return;
            _host.Pump();
            float now = Time.time;
            foreach (var bot in _bots) bot.Update(now);

            if (_devStatus != null)
            {
                var s = _controller.Store.Public;
                _devStatus.text = "나: " + _controller.LocalPlayerId + " · " + (s == null ? "-" : s.turn + "턴 " + s.phase)
                                  + " · 타이머 " + (manualTimer ? "수동" : "실시간")
                                  + (_myClient.Hold ? " · 응답 지연(" + _myClient.Waiting + "개 대기)" : "");
            }
        }

        // ------------------------------------------------------------------
        // 개발 도구 패널(왼쪽 아래 DEV 버튼)
        // ------------------------------------------------------------------

        private void BuildDevPanel()
        {
            var toggle = UiKit.Button(_view.Root, "DEV", 11, new Color(0.1f, 0.1f, 0.1f, 0.85f), Color.white, null);
            UiKit.Place((RectTransform)toggle.transform, Vector2.zero, Vector2.zero, new Vector2(2, 2), new Vector2(30, 30));

            _devPanel = UiKit.Panel(_view.Root, "DevPanel", new Color(0.08f, 0.09f, 0.12f, 0.95f), rounded: true, raycast: true).rectTransform;
            UiKit.Place(_devPanel, Vector2.zero, Vector2.zero, new Vector2(36, 2), new Vector2(560, 112));
            _devPanel.gameObject.SetActive(false);
            toggle.onClick.AddListener(() => _devPanel.gameObject.SetActive(!_devPanel.gameObject.activeSelf));

            _devStatus = UiKit.Label(_devPanel, "", 12, new Color(0.8f, 0.85f, 0.9f), TextAnchor.UpperLeft);
            UiKit.Stretch(_devStatus.rectTransform, 10, 6, 10, 0);

            string[] labels =
            {
                manualTimer ? "다음 단계(수동)" : "수동 타이머로 재시작", "응답 지연 켜기/끄기", "대기 응답 1개 받기", "마지막 결과 다시 받기",
                "다음 구매: 코인 부족", "P1 ↔ P2 화면", "처음부터", "패널 닫기"
            };
            System.Action[] actions =
            {
                () =>
                {
                    if (manualTimer) _host.AdvanceManualPhase();
                    else { manualTimer = true; Restart(); }
                },
                () => { _myClient.Hold = !_myClient.Hold; },
                () => _myClient.DeliverOne(),
                () => _myClient.RedeliverLastResult(),
                () => _information.ForceNextBuyError = ErrorCodes.NotEnoughCoins,
                () => { viewAsPlayer = viewAsPlayer == "P1" ? "P2" : "P1"; Restart(); },
                Restart,
                () => _devPanel.gameObject.SetActive(false)
            };
            for (int i = 0; i < labels.Length; i++)
            {
                int col = i % 4, row = i / 4;
                var action = actions[i];
                var b = UiKit.Button(_devPanel, labels[i], 11, new Color(0.22f, 0.25f, 0.32f), Color.white, () => action());
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(10 + col * 136, -28 - row * 40), new Vector2(130, 34));
            }
        }
    }
}
