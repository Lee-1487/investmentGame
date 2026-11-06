using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.IO;
using BlackWhale.Network;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using BlackWhale.UI.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlackWhale.UI.Tests
{
    public class NetworkGameUiTests
    {
        private sealed class PlanningClient : IGameClient
        {
            public readonly List<Command> Sent = new List<Command>();
            public event Action<ServerMessage> MessageReceived;
            public void SendCommand(Command command) => Sent.Add(command);
            public void Receive(ServerMessage message) => MessageReceived?.Invoke(message);
            public void Pump() { }
            public void Dispose() { }
        }
        [UnitySetUp] public IEnumerator Enter() { yield return new EnterPlayMode(); }

        [UnityTearDown] public IEnumerator Exit()
        {
            foreach (var root in UnityEngine.Object.FindObjectsByType<DevTabletBootstrap>(FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(root.gameObject);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator HostAndGuestUi_BotSeats_PersonalInformation_FullSettlement_LeaveAndRehost()
        {
            int number = FreePort();
            var host = Create("Host UI");
            yield return null;
            Port(host, number);
            Click(host, "Host");
            yield return Until(() => C(host)?.Store.Public?.players.Length == 1, "host lobby");
            Assert.AreEqual("P1", C(host).LocalPlayerId);
            Assert.IsFalse(C(host).Store.Public.players[0].isBot);
            Click(host, "OpenDifficulty");
            Click(host, "BotDifficulty_Hard");
            yield return Until(() => C(host).Store.Public.botDifficulty == "Hard", "D difficulty screen updates actual room setting");
            Click(host, "CloseGameScreen");
            Click(host, "FillBots");
            yield return Until(() => C(host).Store.Public.players.Length == 4 && C(host).Store.Public.players.Skip(1).All(p => p.isBot && p.ready), "three bot clients ready");
            Assert.AreEqual(Protocol.Lobby, C(host).Store.Public.phase, "host chooses when to start");
            Click(host, "RemoveBots");
            yield return Until(() => C(host).Store.Public.players.Length == 1, "remove all bots");

            var guest = Create("Guest UI");
            yield return null;
            Port(guest, number);
            Click(guest, "Join");
            yield return Until(() => C(guest)?.Store.Public?.players.Length == 2, "human reuses released P2 slot");
            Assert.AreEqual("P2", C(guest).LocalPlayerId);
            Click(guest, "OpenDifficulty");
            Assert.IsTrue(guest.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("BotDifficulty_")).All(b => !b.interactable), "guest cannot edit host difficulty");
            Click(guest, "CloseGameScreen");
            Assert.IsFalse(guest.GetComponentsInChildren<Button>().Any(b => b.name == "FillBots" || b.name == "RemoveBots"), "guest has no host controls");
            Click(host, "FillBots");
            yield return Until(() => C(host).Store.Public.players.Length == 4 && C(host).Store.Public.players.Count(p => p.isBot && p.ready) == 2, "two humans and two bots");
            CollectionAssert.AreEqual(new[] { "P1", "P2", "P3", "P4" }, C(host).Store.Public.players.Select(p => p.playerId));

            var extra = Create("Full room UI");
            yield return null;
            Port(extra, number);
            Click(extra, "Join");
            yield return Until(() => extra.GetComponentsInChildren<Text>().Any(t => t.text.Contains("방이 가득")), "full-room error shown in UI");
            Assert.IsNull(Field<NetworkSession>(extra, "session"));
            UnityEngine.Object.Destroy(extra.gameObject);

            Click(host, "Ready");
            Click(guest, "Ready");
            yield return Until(() => C(host).AllPlayersReady, "all players ready");
            Click(host, "StartGame");
            yield return Until(() => C(host).Store.Public.phase == Protocol.AbilitySelection && C(host).Store.View?.ability?.candidates?.Length == 3,
                "three ability candidates before first turn");
            yield return null;
            string chosenAbility = C(host).Store.View.ability.candidates[0];
            Click(host, "ChooseAbility_" + chosenAbility);
            Click(host, "ConfirmAbility");
            yield return Until(() => C(host).Store.View.ability.selectedId == chosenAbility, "D ability confirmation reaches actual C service");
            Click(host, "DefaultAbilities");
            yield return Until(() => C(guest).Store.View?.turn == 1 && C(host).Store.Public.phase == Protocol.Info, "start synchronized");
            var hostSession = Field<NetworkSession>(host, "session");
            var guestSession = Field<NetworkSession>(guest, "session");
            var hostedRoom = Field<BlackWhale.Flow.GameRoom>(hostSession, "hostedRoom");
            Assert.IsFalse(guestSession.SetGamePaused(true), "guest cannot pause the host's room");
            Assert.IsFalse(guest.GetComponentsInChildren<Button>().Any(b => b.name == "PauseGame"));
            Click(host, "PauseGame");
            yield return Until(() => C(host).Store.Public.paused && C(guest).Store.Public.paused, "pause broadcast to both actual TCP clients");
            Assert.IsTrue(guest.GetComponentsInChildren<RectTransform>().Any(r => r.name == "GamePausedOverlay"));
            double pausedTime = hostedRoom.Snapshot().serverElapsedSeconds;
            int pausedCoins = C(guest).Store.View.coins;
            Assert.AreEqual(CommandStatus.BlockedLocally, C(guest).BuyInformation("AI", 0));
            guestSession.Client.SendCommand(new Command { type = "COMPLETE_PHASE", requestId = "while-paused", turn = 1, payload = new CommandPayload() });
            yield return Until(() => C(guest).Store.LastError?.RequestId == "while-paused", "paused server rejects raw TCP command");
            Assert.AreEqual(ErrorCodes.GamePaused, C(guest).Store.LastError.ErrorCode);
            C(guest).Store.ClearError();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(pausedTime, hostedRoom.Snapshot().serverElapsedSeconds, "real clock stays frozen");
            Assert.AreEqual(pausedCoins, C(guest).Store.View.coins);
            CapturePlanningScreen(host.GetComponentInChildren<Canvas>(), "game-paused");
            var editorPause = typeof(NetworkSession).GetMethod("OnEditorPauseChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            editorPause.Invoke(hostSession, new object[] { UnityEditor.PauseState.Paused });
            editorPause.Invoke(hostSession, new object[] { UnityEditor.PauseState.Unpaused });
            Assert.IsTrue(hostedRoom.Snapshot().paused, "editor unpause preserves manual game pause");
            Click(host, "ResumeGame");
            yield return Until(() => !C(host).Store.Public.paused && !C(guest).Store.Public.paused, "resume broadcast");
            editorPause.Invoke(hostSession, new object[] { UnityEditor.PauseState.Paused });
            pausedTime = hostedRoom.Snapshot().serverElapsedSeconds;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(pausedTime, hostedRoom.Snapshot().serverElapsedSeconds, "editor pause hook freezes server time");
            editorPause.Invoke(hostSession, new object[] { UnityEditor.PauseState.Unpaused });
            Assert.Less(hostedRoom.Snapshot().serverElapsedSeconds - pausedTime, .1, "editor resume does not catch up elapsed wall time");
            yield return Until(() => !C(guest).Store.Public.paused, "guest sees editor resume");
            Click(host, "OpenShop");
            Assert.AreEqual(8, host.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Buy_")));
            Assert.IsTrue(host.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Buy_")).All(b => !b.interactable), "unsupported purchases disabled in actual game");
            Click(host, "CloseGameScreen");
            Assert.IsFalse(host.GetComponentsInChildren<Button>().Any(b => b.name == "RemoveBots"), "cannot remove bots mid-game");
            int bundle = Enumerable.Range(0, 6).First(b => !C(guest).IsVisible("AI", b));
            int guestBefore = C(guest).Store.View.coins;
            bool free = C(guest).Store.View.ability.freeInformationCredits > 0;
            Click(guest, "Expand_AI");
            Click(guest, "ZoomBuyBundle_" + bundle);
            yield return Until(() => C(guest).IsVisible("AI", bundle), "private C purchase result");
            Assert.IsTrue(guest.GetComponentsInChildren<ChartPointerLayer>().Any(p => p.name == "ZoomChart_AI"), "purchase keeps graph enlarged");
            Assert.IsFalse(guest.GetComponentsInChildren<Button>().Single(b => b.name == "ZoomBuyBundle_" + bundle).interactable, "purchased bundle cannot be bought twice");
            CapturePlanningScreen(guest.GetComponentInChildren<Canvas>(), "zoom-purchase");
            Click(guest, "ShrinkChart");
            Assert.AreEqual(guestBefore - (free ? 0 : 1), C(guest).Store.View.coins);
            Assert.IsFalse(C(host).IsVisible("AI", bundle));
            Assert.AreEqual(3, C(host).Store.View.coins);

            var deadline = Time.realtimeSinceStartup + 40;
            while (C(host).Store.Scoreboard == null || C(guest).Store.Scoreboard == null)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "B settlement reaches both UIs");
                DriveHuman(C(host)); DriveHuman(C(guest));
                yield return null;
            }
            Assert.AreEqual(4, C(host).Store.Scoreboard.rows.Length);
            CollectionAssert.AreEqual(C(host).Store.Scoreboard.rows.Select(r => r.totalAssets), C(guest).Store.Scoreboard.rows.Select(r => r.totalAssets));
            Assert.AreEqual(2, C(guest).Store.View.ownInvestments.Length);
            Assert.AreEqual(2, C(host).Store.View.ownInvestments.Length);
            Click(host, "LeaveRoom");
            yield return Until(() => !string.IsNullOrEmpty(C(guest).Store.EndReason), "host leaving closes the room");
            Assert.IsNotNull(C(guest).Store.Scoreboard, "settled history remains visible after disconnect");
            Click(guest, "LeaveRoom");
            Assert.IsNull(C(guest));
            Click(host, "Host");
            yield return Until(() => C(host)?.Store.Public?.players.Length == 1, "same port can host again");
        }

        [UnityTest]
        public IEnumerator ChartZoom_PersonalPlans_PersistIntoOrder_AndConfirmedRangesSurviveReveal()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var canvas = new GameObject("Planning UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Info, phaseEndsAtServerSeconds = 90,
                    players = new[] { new Player { playerId = "P1", displayName = "Planner", connected = true } },
                    companySeats = UiRules.CompanyIds.Select(id => new CompanySeat { companyId = id, remainingSeats = 2 }).ToArray() };
                var privateView = new PlayerView { turn = 1, phase = Protocol.Info, recipientPlayerId = "P1", assets = 10000, coins = 3,
                    selectedCompanies = new string[0], ownInvestments = new Investment[0],
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id, visibleBundles = new[] {
                        new VisibleBundle { bundleIndex = 1, startDay = 6, source = "PUBLIC", dailyChangesBps = new[] { 60, 70, 80, 90, 100 } },
                        new VisibleBundle { bundleIndex = 2, startDay = 11, source = "PURCHASED", dailyChangesBps = new[] { 110, 120, 130, 140, 150 } } } }).ToArray() };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = privateView });
                var tablet = TabletView.Create((RectTransform)canvas.transform, controller); tablet.ShowPage(1);
                yield return null; Canvas.ForceUpdateCanvases();
                float smallWidth = canvas.GetComponentsInChildren<BarChartGraphic>().First(c => c.name == "Chart").rectTransform.rect.width;
                ClickPlanning(canvas, "Expand_AI"); Canvas.ForceUpdateCanvases();
                var chart = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(c => c.name == "ZoomChart_AI");
                Assert.Greater(chart.GetComponent<BarChartGraphic>().rectTransform.rect.width, smallWidth * 2, "enlarged chart fills central tablet outside the pager mask");
                Assert.IsFalse(chart.GetComponentsInParent<RectMask2D>().Any(), "large graph is not clipped to a company card or page");
                Assert.IsNull(Field<DayBar[]>(chart.GetComponent<BarChartGraphic>(), "_bars")[20].Bps, "zoom never reveals hidden prices");
                ClickPlanning(canvas, "FavoriteCompany"); ClickPlanning(canvas, "PlanRank_1");
                chart.Clicked(10, false); chart.Clicked(15, false);
                Assert.AreEqual(10, controller.Store.TradePlans.Get("AI").Intervals.Single().entryDay);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "zoom-plan");
                ClickPlanning(canvas, "ShrinkChart");
                Assert.AreEqual(6, canvas.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Expand_")));
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "six-companies");
                ClickPlanning(canvas, "Expand_SPACE");
                chart = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(c => c.name == "ZoomChart_SPACE");
                chart.Clicked(0, false); chart.Clicked(5, false); ClickPlanning(canvas, "ShrinkChart");
                Assert.AreEqual(0, client.Sent.Count, "favorite, priority, zoom and planned dates send no server commands");
                Assert.AreEqual(3, controller.Store.View.coins);
                tablet.ShowBanner("이전 정보 탐색 안내");
                state.phase = Protocol.Selection; state.currentPlayerId = "P1";
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.IsFalse(Field<Queue<string>>(tablet, "_bannerQueue").Any(m => m.Contains("이전 정보 탐색")), "queued messages expire on phase change");
                Assert.AreEqual("", Field<Text>(tablet, "_bannerText").text, "visible old-phase message disappears immediately");
                yield return null;
                StringAssert.Contains("내 차례", Field<Text>(tablet, "_bannerText").text);
                var selectionCandidate = Field<SelectionCandidate>(Field<InvestmentPage>(tablet, "_investmentPage"), "_candidate");
                selectionCandidate.Toggle("AI");
                state.paused = true; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.AreEqual("AI", selectionCandidate.CompanyId, "pause preserves unfinished company candidate");
                state.paused = false; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.AreEqual("AI", selectionCandidate.CompanyId, "resume retains candidate before its original deadline");
                tablet.ShowBanner("지난 선택 차례 안내");
                state.currentPlayerId = "P2"; state.selectionIndex = 1;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.AreEqual("", Field<Text>(tablet, "_bannerText").text, "my-turn notice disappears when next player selects");
                Assert.AreEqual(0, Field<Queue<string>>(tablet, "_bannerQueue").Count);
                StringAssert.Contains("1순위", canvas.GetComponentsInChildren<Text>().Single(t => t.name == "SelectionPlan_AI").text);
                privateView.selectedCompanies = new[] { "AI", "SPACE" };
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = privateView });
                state.phase = Protocol.Investment; state.currentPlayerId = "";
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                var investmentPage = Field<InvestmentPage>(tablet, "_investmentPage");
                var draft = Field<InvestmentDraft>(investmentPage, "_draft");
                Assert.AreEqual(10, draft.FirstIntervals.Single().entryDay, "selected-company plan prepopulates real investment form");
                ClickPlanning(canvas, "RemoveInterval_AI_0");
                var orderChart = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.GetComponentsInParent<RectTransform>().Any(r => r.name == "Col_AI"));
                orderChart.Clicked(6, false);
                orderChart = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.GetComponentsInParent<RectTransform>().Any(r => r.name == "Col_AI"));
                orderChart.Clicked(11, false);
                ClickPlanning(canvas, "SubmitOrder");
                Assert.AreEqual(1, client.Sent.Count);
                var command = client.Sent.Single();
                Assert.AreEqual(6, command.payload.investments.Single(i => i.companyId == "AI").intervals.Single().entryDay);
                client.Receive(new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult {
                    type = "SUBMIT_INVESTMENT_RESULT", requestId = command.requestId, turn = 1, success = true,
                    payload = new ResultPayload { locked = true, investments = command.payload.investments } } });
                state.phase = Protocol.Reveal;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = UiRules.CompanyIds.Select(id => new RevealedCompany {
                    companyId = id, dailyChangesBps = Enumerable.Range(0, 31).Select(d => d * 10).ToArray() }).ToArray() });
                ClickPlanning(canvas, "Expand_AI");
                chart = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(c => c.name == "ZoomChart_AI");
                StringAssert.Contains("6", chart.GetComponentsInChildren<Text>().Single(t => t.name == "EntryMarker_0").text);
                StringAssert.Contains("11", chart.GetComponentsInChildren<Text>().Single(t => t.name == "ExitMarker_0").text);
                Assert.IsTrue(Field<DayBar[]>(chart.GetComponent<BarChartGraphic>(), "_bars")[20].Bps.HasValue, "full prices appear only after reveal");
                Assert.IsFalse(canvas.GetComponentsInChildren<Button>().Single(b => b.name == "PlanRank_1").interactable);
                Assert.AreEqual(10, controller.Store.TradePlans.Get("AI").Intervals.Single().entryDay, "confirmed markers supersede the original plan without overwriting it");
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "reveal-confirmed-order");
                client.Receive(new ServerMessage { type = "SCOREBOARD", scoreboard = new Scoreboard { turn = 1,
                    rows = new[] { new ScoreRow { playerId = "P1", rank = 1, totalAssets = 10000, returnHistory = new ReturnPoint[0] } } } });
                state.phase = Protocol.Result;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.IsTrue(Field<Queue<string>>(tablet, "_bannerQueue").Any(m => m.Contains("정산 완료")), "scoreboard received before result phase is announced when that phase starts");
                yield return null;
                state.turn = 2; state.phase = Protocol.Info;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.AreEqual("", Field<Text>(tablet, "_bannerText").text, "previous turn result banner vanishes immediately");
                Assert.IsFalse(Field<Queue<string>>(tablet, "_bannerQueue").Any(m => m.Contains("정산 완료") || m.Contains("1턴")), "no prior turn result remains queued");
            }
            finally { UnityEngine.Object.Destroy(canvas); controller.Dispose(); }
            yield return null;
        }
        private static void ClickPlanning(GameObject root, string name)
        {
            var button = root.GetComponentsInChildren<Button>().Single(b => b.name == name);
            Assert.IsTrue(button.interactable, name + " enabled"); button.onClick.Invoke();
        }
        [UnityTest]
        public IEnumerator ZoomPurchase_FreeCredit_Pending_Response_AndPhaseGuard()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var canvas = new GameObject("Zoom purchase UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Info, phaseEndsAtServerSeconds = 90,
                    players = new[] { new Player { playerId = "P1", connected = true } } };
                var view = new PlayerView { turn = 1, phase = Protocol.Info, recipientPlayerId = "P1", coins = 0,
                    ability = new AbilityState { freeInformationCredits = 1 },
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id, visibleBundles = new VisibleBundle[0] }).ToArray() };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                TabletView.Create((RectTransform)canvas.transform, controller);
                ClickPlanning(canvas, "Expand_AI");
                var buy = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "ZoomBuyBundle_0");
                Assert.IsTrue(buy.interactable, "free credit enables enlarged purchase at zero coins");
                ClickPlanning(canvas, "ZoomBuyBundle_0");
                Assert.IsFalse(buy.interactable, "pending purchase immediately disables duplicate input");
                Assert.AreEqual(0, controller.Store.View.coins, "no optimistic charge");
                Assert.AreEqual(CommandStatus.AlreadyPending, controller.BuyInformation("AI", 0));
                Assert.AreEqual(1, client.Sent.Count);
                var command = client.Sent.Single();
                Assert.AreEqual("AI", command.payload.companyId); Assert.AreEqual(0, command.payload.bundleIndex);
                client.Receive(new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult {
                    type = "BUY_INFORMATION_RESULT", requestId = command.requestId, turn = 1, success = true,
                    payload = new ResultPayload { companyId = "AI", bundleIndex = 0, startDay = 1,
                        remainingCoins = 0, source = BundleSources.Purchased, dailyChangesBps = new[] { 100, 200, 300, 400, 500 } } } });
                var chart = canvas.GetComponentsInChildren<BarChartGraphic>().Single(c => c.name == "ZoomChart_AI");
                Assert.IsTrue(Field<DayBar[]>(chart, "_bars")[5].Bps.HasValue, "purchased prices update without collapsing");
                StringAssert.Contains("구매함", buy.GetComponentInChildren<Text>().text);
                view = DtoCopy.Copy(controller.Store.View); view.ability.freeInformationCredits = 0;
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                Assert.IsTrue(canvas.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("ZoomBuyBundle_")).All(b => !b.interactable), "no coins or credits disables every remaining bundle");
                state.phase = Protocol.Reveal; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                ClickPlanning(canvas, "Expand_AI");
                view.coins = 3; client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                Assert.IsTrue(canvas.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("ZoomBuyBundle_")).All(b => !b.interactable), "coins cannot enable purchases outside information phase");
                yield return null;
            }
            finally { controller.Dispose(); UnityEngine.Object.Destroy(canvas); }
        }

        private static void CapturePlanningScreen(Canvas canvas, string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var cameraObject = new GameObject("Planning capture", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Palette.Background;
            var target = new RenderTexture(1280, 720, 24); target.Create(); camera.targetTexture = target;
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; float previousDistance = canvas.planeDistance;
            var previousTarget = RenderTexture.active; Texture2D image = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null) camera.Render();
                else
                {
                    var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
                    Assert.IsTrue(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request));
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                }
                RenderTexture.active = target; image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "PlanningScreens");
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousDistance;
                Canvas.ForceUpdateCanvases(); RenderTexture.active = previousTarget; camera.targetTexture = null;
                if (image != null) UnityEngine.Object.Destroy(image);
                target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator InvalidPort_ConnectionCancellation_CanHostAfterCancelledHandshake()
        {
            var ui = Create("Cancel UI");
            yield return null;
            Port(ui, 0);
            Click(ui, "Host");
            Assert.IsNull(Field<NetworkSession>(ui, "session"));
            Assert.IsTrue(ui.GetComponentsInChildren<Text>().Any(t => t.text.Contains("1~65535")));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Port(ui, ((IPEndPoint)listener.LocalEndpoint).Port);
                Click(ui, "Join");
                yield return Until(() => Field<NetworkSession>(ui, "session")?.Client != null, "TCP connected, waiting for WELCOME");
                Click(ui, "CancelConnection");
                Port(ui, FreePort());
                Click(ui, "Host");
                yield return Until(() => C(ui)?.Store.Public?.players.Length == 1, "cancelled operation cannot tear down new host");
                Assert.AreEqual("P1", C(ui).LocalPlayerId);
            }
            finally { listener?.Stop(); }
        }

        private static void DriveHuman(GameClientController c)
        {
            var state = c.Store.Public;
            if (state.phase == Protocol.Info || state.phase == Protocol.Reveal)
            { if (c.CanFinishPhase()) c.FinishPhase(); }
            else if (state.phase == Protocol.Selection && c.IsMyTurnToSelect() && !c.Store.HasPending(CommandTypes.SelectCompany))
            {
                var pick = new[] { "BANK", "FOOD", "AUTO", "AI", "SPACE", "BIO" }.FirstOrDefault(c.CanSelect);
                if (pick != null) c.SelectCompany(pick);
            }
            else if (state.phase == Protocol.Investment && !c.Store.OrderLocked && !c.Store.HasPending(CommandTypes.SubmitInvestment))
            {
                var draft = InvestmentDraft.FromSelections(c.Store.OwnSelections);
                if (draft == null) return;
                draft.TryAddInterval(draft.FirstCompanyId, 10, 15);
                draft.TryAddInterval(draft.SecondCompanyId, 5, 10);
                c.SubmitInvestment(draft.ToInvestments());
            }
        }

        private static DevTabletBootstrap Create(string name) => new GameObject(name).AddComponent<DevTabletBootstrap>();
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static GameClientController C(DevTabletBootstrap root) => Field<GameClientController>(root, "controller");
        private static void Port(DevTabletBootstrap root, int number) => root.GetComponentsInChildren<InputField>().Single(f => f.name == "Port").text = number.ToString();
        private static void Click(DevTabletBootstrap root, string name)
        {
            var button = root.GetComponentsInChildren<Button>().Single(b => b.name == name);
            Assert.IsTrue(button.interactable, name + " enabled");
            button.onClick.Invoke();
        }
        private static IEnumerator Until(Func<bool> predicate, string message)
        {
            var deadline = Time.realtimeSinceStartup + 15;
            while (!predicate())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, message);
                yield return null;
            }
            // One frame for a just-created view to consume the remaining state messages.
            yield return null;
        }
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int number = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return number;
        }
    }
}
