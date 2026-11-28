using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Bots;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;

namespace BlackWhale.UI.Dev
{
    [Serializable] public sealed class BotExperimentProfile
    {
        public BotPersonality personality = BotPersonality.Strategic;
        public BotDifficulty difficulty = BotDifficulty.Normal;
        public BotExperimentProfile Copy() => new BotExperimentProfile { personality = personality, difficulty = difficulty };
    }
    [Serializable] public sealed class BotExperimentSettings
    {
        public int repetitions = 100, firstSeed = 1;
        public bool rotateSeats = true;
        public LobbySettings lobby = new LobbySettings();
        public BotExperimentProfile[] bots = {
            new BotExperimentProfile { personality = BotPersonality.Adventurous },
            new BotExperimentProfile { personality = BotPersonality.Stable },
            new BotExperimentProfile(), new BotExperimentProfile()
        };
        public void Validate()
        {
            if (repetitions < 1 || repetitions > 10000 || (long)firstSeed + repetitions - 1 > int.MaxValue)
                throw new ArgumentException("반복 횟수는 1~10000, 마지막 시드는 int 범위여야 합니다.");
            if (bots == null || bots.Length != 4 || bots.Any(b => b == null ||
                !Enum.IsDefined(typeof(BotPersonality), b.personality) || !Enum.IsDefined(typeof(BotDifficulty), b.difficulty)))
                throw new ArgumentException("유효한 BOT 4개의 성향과 난이도가 필요합니다.");
            if (lobby == null || !lobby.IsValid) throw new ArgumentException("게임 설정이 올바르지 않습니다.");
        }
        public BotExperimentSettings Copy() => new BotExperimentSettings { repetitions = repetitions, firstSeed = firstSeed,
            rotateSeats = rotateSeats, bots = bots.Select(b => b.Copy()).ToArray(), lobby = lobby.Copy() };
    }
    [Serializable] public sealed class BotExperimentTurn
    {
        public int turn;
        public int[] returnWinners, assetLeaders;
        public int[] returnsBps = new int[4];
        public long[] assets = new long[4];
        // Indexed by stable BOT identity, independent of its seat in this run.
        public string[] selectedAugments = new string[4];
    }
    [Serializable] public sealed class BotExperimentRun
    {
        public int number, seed, finalWinner;
        public int commandFailures;
        public int[] seatBots;
        public List<BotExperimentTurn> turns = new List<BotExperimentTurn>();
    }
    [Serializable] public sealed class BotExperimentResults
    {
        public string rulesVersion = Protocol.RulesVersion;
        public float stepSeconds = .1f, thinkSeconds = 1.2f;
        public BotExperimentSettings settings;
        public StockPatternLibrary patternLibrary;
        public List<BotExperimentRun> runs = new List<BotExperimentRun>();
    }
    public sealed class BotExperimentSummary
    {
        public readonly int[,] returnWins = new int[4, StockRules.Turns], assetLeads = new int[4, StockRules.Turns];
        public readonly int[] finalWins = new int[4];
        public readonly Dictionary<string, int[]> augmentCounts = new Dictionary<string, int[]>();
        public void Add(BotExperimentRun run)
        {
            finalWins[run.finalWinner]++;
            foreach (var turn in run.turns) {
                foreach (int bot in turn.returnWinners) returnWins[bot, turn.turn - 1]++;
                foreach (int bot in turn.assetLeaders) assetLeads[bot, turn.turn - 1]++;
                for (int bot = 0; bot < 4; bot++) {
                    string id = turn.selectedAugments[bot];
                    if (string.IsNullOrEmpty(id)) continue;
                    string key = turn.turn + ":" + id;
                    if (!augmentCounts.TryGetValue(key, out var counts)) augmentCounts.Add(key, counts = new int[4]);
                    counts[bot]++;
                }
            }
        }
    }
    // Headless execution of the production room and DevBot. Virtual time preserves
    // reaction delays and trading deadlines; no scene, socket, or Unity API is used.
    public sealed class BotExperimentBatch : IDisposable
    {
        public BotExperimentResults Results { get; }
        public BotExperimentSummary Summary { get; } = new BotExperimentSummary();
        public bool Running { get; private set; } = true;
        public int Completed => Results.runs.Count;
        public int CurrentTurn => current?.CurrentTurn ?? 0;
        public string CurrentPhase => current?.CurrentPhase ?? "";
        private Simulation current;
        public BotExperimentBatch(BotExperimentSettings settings, StockPatternLibrary library)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            Results = new BotExperimentResults { settings = settings.Copy(), patternLibrary = (library ?? StockPatternLibrary.CreateDefault()).Copy() };
        }
        public void Step()
        {
            if (!Running) return;
            try {
                if (current == null) current = new Simulation(Results, Completed);
                if (!current.Step()) return;
                var run = current.Result;
                Summary.Add(run); Results.runs.Add(run);
                current.Dispose(); current = null;
                if (Completed == Results.settings.repetitions) Running = false;
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { Running = false; current?.Dispose(); current = null; }

        private sealed class SimClock : IServerClock { public double Seconds { get; set; } }
        private sealed class SimClient : IGameClient
        {
            private readonly GameRoom room;
            public readonly string Connection;
            public readonly Queue<ServerMessage> Inbox = new Queue<ServerMessage>();
            public event Action<ServerMessage> MessageReceived;
            public SimClient(GameRoom room, string connection) { this.room = room; Connection = connection; }
            public void SendCommand(Command command) => room.ProcessCommand(Connection, command);
            public void Pump() { while (Inbox.Count > 0) MessageReceived?.Invoke(Inbox.Dequeue()); }
            public void Dispose() { Inbox.Clear(); }
        }
        private sealed class Simulation : IDisposable
        {
            private readonly SimClock clock = new SimClock();
            private readonly GameRoom room;
            private readonly MarketService market;
            private readonly SimClient[] clients = new SimClient[4];
            private readonly DevBot[] bots = new DevBot[4];
            private readonly BotExperimentResults experiment;
            private readonly string[][] previousAugments = Enumerable.Range(0, 4).Select(_ => Array.Empty<string>()).ToArray();
            private int steps;
            public BotExperimentRun Result { get; }
            public int CurrentTurn => bots[0]?.Controller.Store.Public?.turn ?? 0;
            public string CurrentPhase => bots[0]?.Controller.Store.Public?.phase ?? Protocol.Lobby;
            public Simulation(BotExperimentResults experiment, int index)
            {
                this.experiment = experiment;
                int seed = experiment.settings.firstSeed + index;
                // Rotate profile identities through P1..P4, keeping bot labels stable in reports.
                int offset = experiment.settings.rotateSeats ? index % 4 : 0;
                Result = new BotExperimentRun { number = index + 1, seed = seed,
                    seatBots = Enumerable.Range(0, 4).Select(seat => (seat + offset) % 4).ToArray() };
                market = MarketService.CreateStockSystem(seed, experiment.patternLibrary);
                room = new GameRoom("BOT-LAB-" + seed, market, new SharedInformationService(market, seed ^ 397), clock,
                    TimerMode.Realtime, false, GameSettings.StockGame, seed ^ 7919);
                try {
                    for (int seat = 0; seat < 4; seat++) {
                        int botIndex = Result.seatBots[seat]; var profile = experiment.settings.bots[botIndex];
                        clients[seat] = new SimClient(room, "lab-" + seat);
                        bots[seat] = new DevBot(clients[seat], experiment.thinkSeconds, unchecked(seed * 31 + botIndex * 397), profile.difficulty, profile.personality);
                        if (seat == 0) clients[seat].MessageReceived += Record;
                        string error = room.Join(clients[seat].Connection, "BOT " + (botIndex + 1));
                        if (error != "") throw new InvalidOperationException(error);
                        if (seat > 0) {
                            string player = "P" + (seat + 1);
                            if (!room.RegisterLocalBot(player) || !room.SetBotProfile(player, profile.difficulty.ToString(), profile.personality.ToString()))
                                throw new InvalidOperationException("BOT 프로필 등록 실패");
                        }
                    }
                    RouteAndPump();
                    bots[0].Controller.SetLobbySettings(experiment.settings.lobby.Copy());
                    RouteAndPump();
                }
                catch { Dispose(); throw; }
            }
            private void Record(ServerMessage message)
            {
                var board = message.type == "SCOREBOARD" ? message.scoreboard : null;
                if (board == null || Result.turns.Any(t => t.turn == board.turn)) return;
                var turn = new BotExperimentTurn { turn = board.turn };
                for (int seat = 0; seat < 4; seat++) {
                    string player = "P" + (seat + 1); int bot = Result.seatBots[seat];
                    var row = board.rows.Single(r => r.playerId == player);
                    turn.returnsBps[bot] = row.turnReturnBps; turn.assets[bot] = row.totalAssets;
                    var owned = market.GetPortfolio(player).augments ?? Array.Empty<string>();
                    turn.selectedAugments[bot] = string.Join(",", owned.Except(previousAugments[bot]));
                    previousAugments[bot] = (string[])owned.Clone();
                }
                turn.returnWinners = Enumerable.Range(0, 4).Where(b => turn.returnsBps[b] == turn.returnsBps.Max()).ToArray();
                turn.assetLeaders = Enumerable.Range(0, 4).Where(b => turn.assets[b] == turn.assets.Max()).ToArray();
                Result.turns.Add(turn);
                if (board.gameComplete) Result.finalWinner = Result.seatBots[int.Parse(board.winnerPlayerId.Substring(1)) - 1];
            }
            private void RouteAndPump()
            {
                foreach (var delivery in room.DrainMessages()) {
                    int seat = int.Parse(delivery.PlayerId.Substring(1)) - 1;
                    clients[seat]?.Inbox.Enqueue(delivery.Message);
                    if (delivery.Message.type == "COMMAND_RESULT" && delivery.Message.response?.success == false) Result.commandFailures++;
                }
                foreach (var client in clients) client?.Pump();
            }
            public bool Step()
            {
                if (++steps > 60000) throw new InvalidOperationException("시뮬레이션 진행 제한 초과: " + CurrentTurn + " / " + CurrentPhase);
                clock.Seconds = steps * (double)experiment.stepSeconds;
                room.Tick(); RouteAndPump();
                // Rotate update priority so simultaneous ticks do not always favor P1.
                for (int i = 0; i < 4; i++) {
                    bots[(steps + i) % 4].Update((float)clock.Seconds);
                    // Deliver accepted exchanges before the next client decides to finish information sharing.
                    RouteAndPump();
                }
                if (Result.turns.Count == StockRules.Turns) return true;
                if (CurrentPhase == Protocol.Ended) throw new InvalidOperationException("정산을 완료하기 전에 게임이 종료됐습니다.");
                return false;
            }
            public void Dispose() { foreach (var bot in bots) bot?.Dispose(); room?.Shutdown(); }
        }
    }
}
