using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Network;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    public static class RealInformationValidation
    {
        // Real A transport + real C. Only B's market/settlement remain fixture doubles.
        public static async Task<string[]> Run(RoomValidation suite, IWireCodec codec)
        {
            var fixture = suite.NewRoom(start: false);
            var room = new GameRoom("REAL-C", fixture.Market, new SharedInformationService(fixture.Market),
                fixture.Clock, TimerMode.ManualForFixture, true);
            var clients = new List<TcpGameClient>();
            var logs = new List<List<ServerMessage>>();
            var server = new TcpGameServer(room, codec, fixture.Clock, 0, IPAddress.Loopback);
            Func<Func<bool>, Task> until = async predicate =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!predicate())
                {
                    server.Pump();
                    foreach (var client in clients) client.Pump();
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Real C TCP integration timed out.");
                    await Task.Delay(3);
                }
            };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var log = new List<ServerMessage>(); logs.Add(log);
                    var client = await TcpGameClient.Connect("127.0.0.1", server.Port, "Player " + (i + 1), codec);
                    client.MessageReceived += log.Add; clients.Add(client);
                    await until(() => client.PlayerId != null);
                }
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("SET_READY", "ready", 0, new CommandPayload { ready = true }));
                await until(() => room.Snapshot().players.All(p => p.ready));
                clients[0].SendCommand(RoomValidation.Command("START_GAME", "start", 0));
                await until(() => logs.All(l => l.Any(m => m.type == "PLAYER_VIEW")));
                RoomValidation.Check(logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 3), "real C initial grants");
                var buy = RoomValidation.Command("BUY_INFORMATION", "info-001", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 });
                clients[0].SendCommand(buy);
                await until(() => logs[0].Any(m => m.response?.requestId == "info-001"));
                clients[0].SendCommand(buy);
                await until(() => logs[0].Count(m => m.response?.requestId == "info-001") == 2);
                for (int i = 0; i < 4; i++) clients[i].SendCommand(RoomValidation.Command("GET_PLAYER_VIEW", "refresh", 1));
                await until(() => logs.All(l => l.Any(m => m.response?.requestId == "refresh")));
                var host = logs[0].Last(m => m.type == "PLAYER_VIEW").playerView;
                RoomValidation.Check(host.coins == 2 && RoomValidation.Visible(host), "real C host purchase and replay");
                RoomValidation.Check(host.assets == 10000 && host.recipientPlayerId == "P1", "common view identity and assets");
                RoomValidation.Check(logs.Skip(1).All(l => !l.Any(m => m.response?.requestId == "info-001") &&
                    l.Last(m => m.type == "PLAYER_VIEW").playerView.coins == 3 &&
                    !RoomValidation.Visible(l.Last(m => m.type == "PLAYER_VIEW").playerView)), "real C private routing");
                var values = host.companies.Single(c => c.companyId == "AI").visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps;
                RoomValidation.Check(values.SequenceEqual(new[] { -1500, -2000, -2500, -3000, -4000 }), "real C fixture values");
                // P2 buys, disconnects and restores its own purchases using the same C service state.
                clients[1].SendCommand(RoomValidation.Command("BUY_INFORMATION", "p2-buy", 1, new CommandPayload { companyId = "AI", bundleIndex = 1 }));
                await until(() => logs[1].Any(m => m.response?.requestId == "p2-buy"));
                string token = clients[1].ReconnectToken;
                clients[1].Dispose();
                await until(() => !room.Snapshot().players[1].connected);
                var restored = new List<ServerMessage>();
                var returning = await TcpGameClient.Connect("127.0.0.1", server.Port, "Return", codec, token);
                returning.MessageReceived += restored.Add; clients.Add(returning);
                await until(() => restored.Any(m => m.type == "PLAYER_VIEW"));
                var view = restored.Last(m => m.type == "PLAYER_VIEW").playerView;
                RoomValidation.Check(view.recipientPlayerId == "P2" && view.coins == 2 && RoomValidation.Visible(view), "real C reconnect restores purchases");
                return new[] { "TCP/real-C-grants", "TCP/real-C-private-purchase-and-replay", "TCP/real-C-reconnect" };
            }
            finally { foreach (var client in clients) client.Dispose(); server.Dispose(); }
        }
    }
}
