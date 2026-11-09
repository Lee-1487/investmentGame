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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BlackWhale.UI.Tests
{
    public class NetworkGameUiTests
    {
        [UnityTest]
        public IEnumerator MarketRevealShowsEveryPlayersColoredRangesAndScrollableReturns()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var canvas = new GameObject("Public trade UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Reveal, windowInformation = true, phaseEndsAtServerSeconds = 10,
                    players = new[] { "P1", "P2", "P3", "P4" }.Select(id => new Player { playerId = id, displayName = id, connected = true }).ToArray(), companySeats = new CompanySeat[0] };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = new PlayerView { turn = 1, phase = Protocol.Reveal, entryAtOpen = true,
                    recipientPlayerId = "P1", assets = 10000, selectedCompanies = new[] { "AI", "BANK" },
                    ownInvestments = new[] { new Investment { companyId = "AI", weightBps = 5000, intervals = new[] { new Interval { entryDay = 1, exitDay = 3 } } } },
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id }).ToArray() } });
                var tablet = TabletView.Create((RectTransform)canvas.transform, controller); tablet.ShowPage(1);
                var positions = new[] {
                    new RevealedInvestment { playerId = "P1", returnBps = 1800, intervals = new[] {
                        new RevealedTrade { entryDay = 1, exitDay = 3, returnBps = 300 },
                        new RevealedTrade { entryDay = 9, exitDay = 9, returnBps = 100 },
                        new RevealedTrade { entryDay = 17, exitDay = 30, returnBps = 1400 } } },
                    new RevealedInvestment { playerId = "P2", returnBps = 1000, intervals = new[] {
                        new RevealedTrade { entryDay = 2, exitDay = 8, returnBps = 700 },
                        new RevealedTrade { entryDay = 20, exitDay = 22, returnBps = 300 } } }
                };
                client.Receive(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = UiRules.CompanyIds.Select(id => new RevealedCompany {
                    companyId = id, dailyChangesBps = Enumerable.Range(0, 31).Select(d => d * 100).ToArray(),
                    investments = id == "AI" ? positions : new RevealedInvestment[0] }).ToArray() });
                yield return new WaitForSecondsRealtime(.4f); Canvas.ForceUpdateCanvases();
                var badge = canvas.GetComponentsInChildren<Text>().Single(t => t.name == "PlanBadge_AI").text;
                StringAssert.Contains("P1", badge); StringAssert.Contains("P2", badge);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "trading-reveal-all-companies");
                ClickPlanning(canvas, "Expand_AI"); Canvas.ForceUpdateCanvases();
                var chart = canvas.GetComponentsInChildren<BarChartGraphic>().Single(c => c.name == "ZoomChart_AI");
                Assert.AreEqual(2, Field<RevealedInvestment[]>(chart, "_revealedInvestments").Length);
                Assert.AreEqual(Palette.Player("P2"), chart.GetComponentsInChildren<Text>().Single(t => t.name == "RevealedPlayer_P2").color);
                var rows = canvas.GetComponentsInChildren<RectTransform>().Single(r => r.name == "PlannedIntervals");
                var labels = rows.GetComponentsInChildren<Text>();
                Assert.AreEqual(7, labels.Length, "both player totals and all five trades are accessible");
                Assert.IsTrue(labels.Any(t => t.text.Contains("9일 시가 → 9일 종가") && t.text.Contains("+1%")));
                Assert.IsTrue(labels.Any(t => t.text.Contains("P1") && t.text.Contains("+18%")));
                Assert.Greater(rows.sizeDelta.y, ((RectTransform)rows.parent).rect.height, "returns scroll instead of being clipped away");
                Assert.IsFalse(canvas.GetComponentsInChildren<Button>().Where(b => b.name == "ReserveRange").Any(b => b.interactable));
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "trading-reveal-colored-intervals");
            }
            finally { controller.Dispose(); UnityEngine.Object.DestroyImmediate(canvas); }
        }

        [UnityTest]
        public IEnumerator MouseRanges_BuyPerDay_ReserveMoveResize_DeleteWithKeyboard_AndOpenPrice()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var events = new GameObject("Range test events", typeof(EventSystem));
            var previousInputMode = InputSystem.settings.updateMode;
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.EnableDevice(keyboard);
            var canvas = new GameObject("Mouse Range UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Info, windowInformation = true, phaseEndsAtServerSeconds = 90,
                    players = new[] { new Player { playerId = "P1", displayName = "Range", connected = true } }, companySeats = new CompanySeat[0] };
                var view = new PlayerView { turn = 1, phase = Protocol.Info, entryAtOpen = true, recipientPlayerId = "P1", coins = 15, assets = 10000,
                    selectedCompanies = new string[0], ownInvestments = new Investment[0],
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id, visibleWindows = new VisibleInformationWindow[0] }).ToArray() };
                view.companies[0].visibleWindows = new[] { new VisibleInformationWindow { recordId = "public", source = "PUBLIC", startDay = 1,
                    dailyChangesBps = new[] { 100, 200, 300, 400, 500 }, dailyOpenBps = new[] { 0, 100, 200, 300, 400 } } };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state }); client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                var tablet = TabletView.Create((RectTransform)canvas.transform, controller); tablet.ShowPage(1);
                yield return null; ClickPlanning(canvas, "Expand_AI"); Canvas.ForceUpdateCanvases();
                var pointer = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.name == "ZoomChart_AI");
                Assert.AreEqual(0, canvas.GetComponentsInChildren<Slider>().Length, "no range sliders");
                DragChart(pointer, 3, 9);
                Assert.AreEqual(0, client.Sent.Count); Assert.AreEqual(0, controller.Store.TradePlans.Get("AI").Intervals.Length);
                StringAssert.Contains("4코인", canvas.GetComponentsInChildren<Text>().Single(t => t.name == "InformationRangeCost").text);
                ClickPlanning(canvas, "ReserveRange");
                Assert.AreEqual(3, controller.Store.TradePlans.Get("AI").Intervals[0].entryDay);
                Assert.AreEqual(CommandTypes.SaveTradePlan, client.Sent.Single().type, "reservation is saved privately for automatic execution, without buying information or locking an order");
                DragChart(pointer, 6, 8);
                Assert.AreEqual(5, controller.Store.TradePlans.Get("AI").Intervals[0].entryDay);
                Assert.AreEqual(11, controller.Store.TradePlans.Get("AI").Intervals[0].exitDay);
                DragChart(pointer, 11, 13);
                Assert.AreEqual(13, controller.Store.TradePlans.Get("AI").Intervals[0].exitDay);
                DragChart(pointer, 13, 20);
                Assert.AreEqual(20, controller.Store.TradePlans.Get("AI").Intervals[0].exitDay, "holding longer than ten days is allowed");
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "mouse-range-reservation");
                EventSystem.current.SetSelectedGameObject(pointer.gameObject);
                Assert.AreSame(pointer.gameObject, EventSystem.current.currentSelectedGameObject);
                Assert.AreEqual(0, pointer.RangeSelection.SelectedIndex);
                Assert.IsTrue(Field<InputAction>(pointer, "_deleteAction").enabled);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Delete)); InputSystem.Update();
                var deleteAction = Field<InputAction>(pointer, "_deleteAction");
                Assert.IsTrue(deleteAction.triggered, "Delete invokes the chart input action");
                Assert.IsTrue(keyboard.deleteKey.isPressed, "queued Delete reaches the test keyboard"); yield return null;
                Assert.AreEqual(0, controller.Store.TradePlans.Get("AI").Intervals.Length, "Delete removes selected reservation");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); yield return null;
                DragChart(pointer, 3, 9); ClickPlanning(canvas, "ReserveRange");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Backspace)); InputSystem.Update(); yield return null;
                Assert.AreEqual(0, controller.Store.TradePlans.Get("AI").Intervals.Length, "Backspace removes selected reservation");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); yield return null;
                DragChart(pointer, 30, 30); ClickPlanning(canvas, "ConfirmInformationWindow");
                var buy = client.Sent.Single(c => c.type == CommandTypes.BuyInformationWindow); Assert.AreEqual(30, buy.payload.startDay); Assert.AreEqual(30, buy.payload.endDay);
                client.Receive(new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult { type = "BUY_INFORMATION_WINDOW_RESULT", turn = 1,
                    requestId = buy.requestId, success = true, payload = new ResultPayload { recordId = "day30", companyId = "AI", startDay = 30,
                        dailyChangesBps = new[] { 1000 }, dailyOpenBps = new[] { 950 }, paidCoins = 1, remainingCoins = 14, source = "PURCHASED" } } });
                Assert.AreEqual(14, controller.Store.View.coins);
                var bars = Field<DayBar[]>(pointer.GetComponent<BarChartGraphic>(), "_bars");
                Assert.IsNull(bars[29].Bps); Assert.AreEqual(950, bars[30].OpenBps); Assert.AreEqual(1000, bars[30].Bps);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "mouse-range-day-purchase");
                state.phase = Protocol.Selection; state.currentPlayerId = "P1"; state.phaseEndsAtServerSeconds = 122; state.selectionDeadlineAtServerSeconds = 94;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                view.selectedCompanies = new[] { "AI", "BANK" }; client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                state.phase = Protocol.Investment; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Canvas.ForceUpdateCanvases();
                pointer = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.GetComponentsInParent<RectTransform>().Any(r => r.name == "Col_AI"));
                DragChart(pointer, 3, 9); ClickPlanning(canvas, "ReserveOrderRange_AI");
                pointer = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.GetComponentsInParent<RectTransform>().Any(r => r.name == "Col_AI"));
                DragChart(pointer, 9, 10);
                var draft = Field<InvestmentDraft>(Field<InvestmentPage>(tablet, "_investmentPage"), "_draft");
                Assert.AreEqual(10, draft.FirstIntervals[0].exitDay, "order intervals can be resized by mouse too");
                pointer = canvas.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.GetComponentsInParent<RectTransform>().Any(r => r.name == "Col_AI"));
                DragChart(pointer, 6, 6); EventSystem.current.SetSelectedGameObject(pointer.gameObject);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Delete)); InputSystem.Update(); yield return null;
                Assert.AreEqual(0, draft.FirstIntervals.Count, "keyboard deletion works in the investment draft");
            }
            finally {
                InputSystem.RemoveDevice(keyboard); InputSystem.settings.updateMode = previousInputMode; InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
                controller.Dispose(); UnityEngine.Object.DestroyImmediate(canvas); UnityEngine.Object.DestroyImmediate(events);
            }
        }
        [UnityTest]
        public IEnumerator CompanyCardClick_SelectsImmediately_AndBlocksPendingFullExpiredAndOtherTurns()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var canvas = new GameObject("Direct Selection UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Selection, currentPlayerId = "P1", serverElapsedSeconds = 90,
                    phaseEndsAtServerSeconds = 122, selectionDeadlineAtServerSeconds = 94,
                    players = new[] { new Player { playerId = "P1", displayName = "Selector", connected = true } },
                    companySeats = UiRules.CompanyIds.Select(id => new CompanySeat { companyId = id, remainingSeats = id == "BANK" ? 0 : 2 }).ToArray() };
                var view = new PlayerView { turn = 1, phase = Protocol.Selection, recipientPlayerId = "P1", coins = 3,
                    selectedCompanies = new string[0], ownInvestments = new Investment[0],
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id, visibleWindows = new VisibleInformationWindow[0] }).ToArray() };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                var tablet = TabletView.Create((RectTransform)canvas.transform, controller);
                yield return null; Canvas.ForceUpdateCanvases();
                Assert.IsFalse(canvas.GetComponentsInChildren<Button>().Any(b => b.name == "ConfirmSelection" || b.name == "CancelCandidate"));
                var full = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_BANK");
                Assert.IsFalse(full.interactable); full.onClick.Invoke(); Assert.AreEqual(0, client.Sent.Count);
                var ai = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_AI");
                Assert.IsTrue(ai.interactable); ai.onClick.Invoke(); ai.onClick.Invoke();
                Assert.AreEqual(1, client.Sent.Count, "one card click sends one command immediately, pending click cannot duplicate it");
                Assert.AreEqual(CommandTypes.SelectCompany, client.Sent[0].type); Assert.AreEqual("AI", client.Sent[0].payload.companyId);
                Assert.AreEqual(0, controller.Store.OwnSelections.Count, "confirmed selections still come from the server");
                client.Receive(new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult { type = "SELECT_COMPANY_RESULT",
                    turn = 1, requestId = client.Sent[0].requestId, success = true, payload = new ResultPayload { companyId = "AI" } } });
                CollectionAssert.AreEqual(new[] { "AI" }, controller.Store.OwnSelections);
                state.currentPlayerId = "P2"; state.selectionIndex = 1;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                var space = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_SPACE");
                Assert.IsFalse(space.interactable); space.onClick.Invoke(); Assert.AreEqual(1, client.Sent.Count);
                state.currentPlayerId = "P1"; state.selectionIndex = 7; state.serverElapsedSeconds = 120; state.selectionDeadlineAtServerSeconds = 119;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                space = canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_SPACE");
                Assert.IsFalse(space.interactable); space.onClick.Invoke(); Assert.AreEqual(1, client.Sent.Count);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "company-click-selection");
            }
            finally { controller.Dispose(); UnityEngine.Object.DestroyImmediate(canvas); }
        }
        [UnityTest]
        public IEnumerator InformationWindowZoom_KeepsTimerVisible_AndConfirmsOnlyAfterClick()
        {
            var client = new PlanningClient(); var controller = new GameClientController(client);
            var canvas = new GameObject("Window UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            try
            {
                client.Receive(new ServerMessage { type = "WELCOME", session = new Session { playerId = "P1" } });
                var state = new PublicState { turn = 1, phase = Protocol.Info, windowInformation = true, publicSelectionActive = true,
                    publicSelectionPlayerId = "P1", publicSelectionDeadlineAtServerSeconds = 10, phaseEndsAtServerSeconds = 90,
                    players = new[] { "P1", "P2", "P3", "P4" }.Select(id => new Player { playerId = id, displayName = id, connected = true }).ToArray(),
                    publicInformation = new PublicInformationChoice[0], companySeats = new CompanySeat[0] };
                var view = new PlayerView { turn = 1, phase = Protocol.Info, recipientPlayerId = "P1", coins = 3, assets = 10000,
                    selectedCompanies = new string[0], ownInvestments = new Investment[0],
                    companies = UiRules.CompanyIds.Select(id => new VisibleCompany { companyId = id, visibleWindows = new VisibleInformationWindow[0] }).ToArray() };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                var tablet = TabletView.Create((RectTransform)canvas.transform, controller); tablet.ShowPage(1);
                yield return new WaitForSecondsRealtime(.35f); Canvas.ForceUpdateCanvases();
                Assert.IsTrue(Field<RectTransform>(tablet, "_turnHighlight").gameObject.activeInHierarchy);
                StringAssert.Contains("지금 내 차례", Field<Text>(tablet, "_turnNoticeText").text);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "stock-window-six-companies");
                yield return new WaitForSecondsRealtime(5.1f);
                Assert.IsTrue(Field<RectTransform>(tablet, "_turnNotice").gameObject.activeInHierarchy, "turn notice persists after the transient banner expires");
                Assert.IsTrue(Field<RectTransform>(tablet, "_turnHighlight").gameObject.activeInHierarchy);
                ClickPlanning(canvas, "Expand_AI"); yield return null; Canvas.ForceUpdateCanvases();
                var zoom = canvas.GetComponentsInChildren<RectTransform>().Single(t => t.name == "ZoomCompanyPanel");
                Assert.IsFalse(canvas.GetComponentsInChildren<Text>().Any(t => t.text == DisplayFormat.Percent(5000) || t.text == DisplayFormat.Percent(-5000)), "stock charts have no upper/lower percentage labels");
                var timer = Field<Text>(tablet, "_timerText");
                var zoomCorners = new Vector3[4]; var timerCorners = new Vector3[4];
                zoom.GetWorldCorners(zoomCorners); timer.rectTransform.GetWorldCorners(timerCorners);
                Assert.Less(zoomCorners[2].y, timerCorners[0].y, "the timer stays above the enlarged chart");
                Assert.AreSame(tablet.ScreenRect, timer.transform.parent.parent, "timer belongs to the top HUD, outside the right column");
                Assert.IsTrue(timer.gameObject.activeInHierarchy);
                StringAssert.Contains("공용 공개", timer.text); StringAssert.Contains("P1", timer.text);
                Assert.AreEqual(0, zoom.GetComponentsInChildren<Slider>().Length);
                var mouseChart = zoom.GetComponentInChildren<ChartPointerLayer>();
                DragChart(mouseChart, 26, 26);
                Assert.AreEqual(0, client.Sent.Count, "moving the window never spends or reveals");
                ClickPlanning(canvas, "ConfirmInformationWindow");
                Assert.AreEqual(CommandTypes.RevealInformationWindow, client.Sent.Single().type);
                Assert.AreEqual(26, client.Sent.Single().payload.startDay);
                Assert.IsFalse(controller.CanFinishPhase(), "cannot finish before all public picks");
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "public-my-turn-highlight-zoom");
                state.publicSelectionIndex = 1; state.publicSelectionPlayerId = "P2"; state.publicSelectionDeadlineAtServerSeconds = 20;
                state.publicInformation = new[] { new PublicInformationChoice { playerId = "P1", companyId = "AI", startDay = 26 } };
                view.companies.Single(c => c.companyId == "AI").visibleWindows = new[] { new VisibleInformationWindow {
                    recordId = "public-1", source = "PUBLIC", startDay = 26, dailyChangesBps = new[] { 100, 200, 300, 250, 350 } } };
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                yield return null;
                StringAssert.Contains("P2", timer.text);
                Assert.IsFalse(Field<RectTransform>(tablet, "_turnHighlight").gameObject.activeInHierarchy);
                StringAssert.Contains("P2", Field<Text>(tablet, "_turnNoticeText").text);
                Assert.IsTrue(zoom.gameObject.activeInHierarchy, "public turns preserve the enlarged chart");
                var bars = Field<DayBar[]>(zoom.GetComponentInChildren<BarChartGraphic>(), "_bars");
                Assert.IsNull(bars[25].Bps); Assert.AreEqual(350, bars[30].Bps);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "stock-window-public-timer");
                state.publicSelectionActive = false; state.publicSelectionIndex = 4;
                state.publicSelectionPlayerId = ""; state.publicSelectionDeadlineAtServerSeconds = 0;
                client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                client.Receive(new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult { type = "REVEAL_INFORMATION_WINDOW_RESULT",
                    requestId = client.Sent[0].requestId, turn = 1, success = true } });
                yield return null;
                StringAssert.DoesNotContain("공용 공개", timer.text);
                Assert.IsFalse(Field<RectTransform>(tablet, "_turnNotice").gameObject.activeInHierarchy);
                DragChart(mouseChart, 24, 28);
                ClickPlanning(canvas, "ConfirmInformationWindow");
                Assert.AreEqual(CommandTypes.BuyInformationWindow, client.Sent.Last().type);
                Assert.AreEqual(24, client.Sent.Last().payload.startDay);
                CapturePlanningScreen(canvas.GetComponent<Canvas>(), "stock-window-private-timer");
            }
            finally { controller.Dispose(); UnityEngine.Object.DestroyImmediate(canvas); }
        }
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
            Assert.IsFalse(host.GetComponentsInChildren<Text>().Any(t => t.text.Contains("사람이 입장한 뒤") || t.text.Contains("혼자 테스트할 때는")), "lobby has no small setup instructions");
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
            float publicDeadline = Time.realtimeSinceStartup + 20;
            while (C(guest).Store.Public.publicSelectionActive || C(host).Store.Public.publicSelectionActive)
            {
                RevealPublic(C(host)); RevealPublic(C(guest));
                Assert.Less(Time.realtimeSinceStartup, publicDeadline, "public selections finish in INFO");
                yield return null;
            }
            int startDay = Enumerable.Range(1, 26).First(d => InformationWindowRules.NewDays(C(guest).Store.View.companies.Single(c => c.companyId == "AI"), d) > 0);
            int guestBefore = C(guest).Store.View.coins;
            int cost = Math.Max(0, InformationWindowRules.NewDays(C(guest).Store.View.companies.Single(c => c.companyId == "AI"), startDay) - C(guest).Store.View.ability.freeInformationCredits);
            Click(guest, "Expand_AI");
            DragChart(guest.GetComponentsInChildren<ChartPointerLayer>().Single(p => p.name == "ZoomChart_AI"), startDay, startDay + 4);
            Click(guest, "ConfirmInformationWindow");
            yield return Until(() => InformationWindowRules.NewDays(C(guest).Store.View.companies.Single(c => c.companyId == "AI"), startDay) == 0, "private C purchase result");
            Assert.IsTrue(guest.GetComponentsInChildren<ChartPointerLayer>().Any(p => p.name == "ZoomChart_AI"), "purchase keeps graph enlarged");
            Assert.IsFalse(guest.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmInformationWindow").interactable, "purchased window cannot be bought twice");
            CapturePlanningScreen(guest.GetComponentInChildren<Canvas>(), "zoom-purchase");
            Click(guest, "ShrinkChart");
            Assert.AreEqual(guestBefore - cost, C(guest).Store.View.coins);
            Assert.Greater(InformationWindowRules.NewDays(C(host).Store.View.companies.Single(c => c.companyId == "AI"), startDay), 0);
            Assert.AreEqual(15, C(host).Store.View.coins);

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
                state.paused = true; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.IsFalse(canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_AI").interactable, "pause blocks immediate selection");
                state.paused = false; client.Receive(new ServerMessage { type = "PUBLIC_STATE", state = state });
                Assert.IsTrue(canvas.GetComponentsInChildren<Button>().Single(b => b.name == "SelectCompany_AI").interactable, "resume allows selection before its original deadline");
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

        private static void DragChart(ChartPointerLayer pointer, int start, int end)
        {
            var rect = (RectTransform)pointer.transform;
            Vector2 Point(int day) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(DayAxis.CenterX(day, rect.rect.xMin, rect.rect.width), rect.rect.center.y)));
            var e = new PointerEventData(EventSystem.current) { position = Point(start), button = PointerEventData.InputButton.Left };
            pointer.OnPointerDown(e); e.position = Point(end); pointer.OnDrag(e); pointer.OnPointerUp(e);
        }
        private static void RevealPublic(GameClientController c)
        {
            var state = c.Store.Public;
            if (state?.publicSelectionActive != true || state.publicSelectionPlayerId != c.LocalPlayerId || c.Store.HasPending(CommandTypes.RevealInformationWindow)) return;
            foreach (var company in c.Store.View.companies)
            {
                int start = Enumerable.Range(1, 26).FirstOrDefault(d => !InformationWindowRules.OverlapsPublic(state, company.companyId, d));
                if (start > 0) { c.SelectInformationWindow(company.companyId, start); return; }
            }
        }
        private static void DriveHuman(GameClientController c)
        {
            if (c.Store.Public?.publicSelectionActive == true) { RevealPublic(c); return; }
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
