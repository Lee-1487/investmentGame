using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
            yield return Until(() => C(guest).Store.View?.turn == 1 && C(host).Store.Public.phase == Protocol.Info, "start synchronized");
            Assert.IsFalse(host.GetComponentsInChildren<Button>().Any(b => b.name == "RemoveBots"), "cannot remove bots mid-game");
            C(guest).BuyInformation("AI", 1);
            yield return Until(() => C(guest).Store.View.coins == 2, "private C purchase result");
            Assert.IsTrue(C(guest).IsVisible("AI", 1));
            Assert.IsFalse(C(host).IsVisible("AI", 1));
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
