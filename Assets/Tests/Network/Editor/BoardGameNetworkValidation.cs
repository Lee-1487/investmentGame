using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using BlackWhale.Flow;
using BlackWhale.Network;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    public static partial class BoardGameValidation
    {
        public static async Task<string> RunNetwork(IWireCodec codec)
        {
            var clock = new Clock(); var market = MarketService.CreateStockSystem(301);
            var room = new GameRoom("BOARD-TCP", market, new SharedInformationService(market), clock);
            var clients = new List<TcpGameClient>(); var logs = new List<List<ServerMessage>>();
            using (var server = new TcpGameServer(room, codec, clock, 0, IPAddress.Loopback)) {
                async Task Until(Func<bool> ready) {
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!ready()) {
                        server.Pump(); foreach (var client in clients) client.Pump();
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("BOARD TCP delivery timed out");
                        await Task.Delay(3);
                    }
                }
                void Send(int player, string type, string id, CommandPayload payload = null) => clients[player - 1].SendCommand(new Command {
                    type = type, requestId = id, turn = room.Snapshot().turn, payload = payload });
                PlayerView View(int player) => logs[player - 1].Last(m => m.type == "PLAYER_VIEW").playerView;
                bool Result(int player, string id) => logs[player - 1].Any(m => m.type == "COMMAND_RESULT" && m.response.requestId == id);
                try {
                    for (int i = 0; i < 4; i++) {
                        var messages = new List<ServerMessage>(); logs.Add(messages);
                        var client = await TcpGameClient.Connect("127.0.0.1", server.Port, "Player " + (i + 1), codec);
                        client.MessageReceived += messages.Add; clients.Add(client);
                        await Until(() => messages.Any(m => m.type == "WELCOME"));
                    }
                    for (int p = 1; p <= 4; p++) Send(p, "SET_READY", "ready", new CommandPayload { ready = true });
                    await Until(() => room.Snapshot().players.All(p => p.ready));
                    Send(1, "START_GAME", "start");
                    await Until(() => logs.All(l => l.Any(m => m.type == "PLAYER_VIEW")));
                    Check(logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.portfolio.capacity == 3), "TCP portfolio capacity");
                    Check(logs.All(l => l.Last(m => m.type == "PLAYER_VIEW").playerView.assets == 100000000), "TCP starting assets are one hundred million");
                    Send(3, "SAVE_TRADE_PLAN", "save-reservation", new CommandPayload { investments = new[] {
                        new Investment { companyId = "AUTO", intervals = new[] { new Interval { entryDay = 3, exitDay = 9 } } } } });
                    await Until(() => Result(3, "save-reservation"));
                    Check(logs[2].Last(m => m.type == "COMMAND_RESULT").response.success, "TCP save daily reservation");
                    Send(1, StockRules.ShareInformation, "share", new CommandPayload { targetPlayerId = "P2", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 11 });
                    await Until(() => Result(1, "share") && View(2).informationExchanges.Any(e => e.status == "PENDING"));
                    string exId = View(2).informationExchanges.Single(e => e.status == "PENDING").id;
                    Send(2, StockRules.RespondExchange, "accept", new CommandPayload { exchangeId = exId, acceptExchange = true });
                    await Until(() => Result(2, "accept") && View(2).companies.Single(c => c.companyId == "FOOD").visibleWindows.Any(w => w.source == "EXCHANGED"));
                    Check(View(1).companies.Single(c => c.companyId == "FOOD").visibleWindows.Any(w => w.source == "EXCHANGED" && w.startDay == 11), "TCP sender receives requested card");
                    Check(View(1).companies.SelectMany(c => c.visibleWindows).All(w => Math.Abs(w.direction) == 1
                        && w.dailyChangesBps.Length == 0 && w.dailyOpenBps.Length == 0), "TCP cards carry direction without actual prices");
                    Check(View(3).companies.Single(c => c.companyId == "FOOD").visibleWindows.All(w => w.source != "EXCHANGED"), "TCP exchanged card private");
                    Send(1, StockRules.SendPlan, "intent", new CommandPayload { targetPlayerId = "P3", companyId = "BIO", startDay = 6, endDay = 15 });
                    await Until(() => Result(1, "intent") && View(3).receivedTradeIntents?.Length == 1);
                    Check(View(2).receivedTradeIntents.Length == 0, "TCP intent private");
                    for (int p = 1; p <= 4; p++) Send(p, "COMPLETE_PHASE", "info-ready");
                    await Until(() => room.Snapshot().phase == Protocol.Investment);
                    Send(2, StockRules.Position, "place", new CommandPayload { startDay = 1, shareOrders = new[] { new ShareOrder { companyId = "BIO", targetShares = 3 } } });
                    await Until(() => Result(2, "place"));
                    Check(market.GetPortfolio("P2").usedSlots == 3 && room.Snapshot().shareMarket.All(c => c.owners.All(o => o.shares == 0)), "TCP sealed placement");
                    clock.Seconds = room.Snapshot().phaseEndsAtServerSeconds;
                    await Until(() => logs.All(l => l.Any(m => m.type == "PUBLIC_STATE" && m.state.stockSegmentRevealing)));
                    Check(logs.All(l => l.Last(m => m.type == "PUBLIC_STATE").state.stockSegmentHistory[0].companies.Single(c => c.companyId == "BIO").owners.Single(o => o.playerId == "P2").shares == 3), "TCP reveal and segment history");
                    // Reconnect during reveal must restore own holdings and received messages without future prices.
                    string token = clients[2].ReconnectToken; clients[2].Dispose();
                    await Until(() => !room.Snapshot().players.Single(p => p.playerId == "P3").connected);
                    var restored = new List<ServerMessage>();
                    var returning = await TcpGameClient.Connect("127.0.0.1", server.Port, "Return", codec, token);
                    returning.MessageReceived += restored.Add; clients[2] = returning;
                    await Until(() => restored.Any(m => m.type == "PLAYER_VIEW"));
                    var restoredView = restored.Last(m => m.type == "PLAYER_VIEW").playerView;
                    Check(restoredView.companies.SelectMany(c => c.visibleWindows).All(w => Math.Abs(w.direction) == 1
                        && w.dailyChangesBps.Length == 0 && w.dailyOpenBps.Length == 0), "TCP reconnect preserves direction cards without prices");
                    Check(restoredView.companies.SelectMany(c => c.visibleWindows).All(w => w.startDay < 26), "TCP reconnect never reveals HIDDEN card");
                    Check(restoredView.recipientPlayerId == "P3" && restoredView.receivedTradeIntents.Single().companyId == "BIO", "TCP reconnect restores private inbox");
                    var reserved = restoredView.reservedInvestments.Single(i => i.companyId == "AUTO").intervals.Single();
                    Check(reserved.entryDay == 3 && reserved.exitDay == 9, "TCP reconnect preserves own daily reservation during investment");
                    Check(restoredView.companies.Where(c => c.companyId == "AI" || c.companyId == "BIO").All(c => c.visibleWindows.Length == 0), "TCP no future reactive prices during reveal");
                    Check(Encoding.UTF8.GetByteCount(codec.EncodeServer(new ServerMessage { type = "PUBLIC_STATE", state = room.Snapshot() })) < WireFrame.MaxBytes, "public state fits wire frame");
                }
                finally { foreach (var client in clients) client.Dispose(); }
            }
            return "BOARD four real TCP clients: private sharing/intents, sealed placements, reveal/history, reconnect";
        }
    }
}
