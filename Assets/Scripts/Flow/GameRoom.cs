using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BlackWhale.Shared;

namespace BlackWhale.Flow
{
    public sealed class MonotonicClock : IServerClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public double Seconds { get { return watch.Elapsed.TotalSeconds; } }
    }
    public enum TimerMode { Realtime, ManualForFixture }
    public sealed class Delivery
    {
        public readonly string PlayerId;
        public readonly ServerMessage Message;
        public Delivery(string playerId, ServerMessage message) { PlayerId = playerId; Message = message; }
    }

    // One room, one synchronization boundary. B/C calls occur inside this boundary.
    // Transport never executes callbacks while the room lock is held.
    public sealed class GameRoom
    {
        private sealed class Member
        {
            public string Id, Name, Connection, Token;
            public bool Ready;
            public double DisconnectedAt;
        }
        private sealed class Cached
        {
            public string Fingerprint;
            public CommandResult Result;
        }
        private readonly object gate = new object();
        private readonly IMarketService market;
        private readonly IInformationService information;
        private readonly IServerClock clock;
        private readonly TimerMode mode;
        private readonly bool fixedOrder;
        private readonly List<Member> members = new List<Member>();
        private readonly Queue<Delivery> outbox = new Queue<Delivery>();
        private readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private readonly HashSet<string> completed = new HashSet<string>();
        private readonly HashSet<int> begun = new HashSet<int>(), settled = new HashSet<int>();
        private string[] order = new string[0];
        private readonly string roomId;
        private string phase = Protocol.Lobby;
        private int turn, selectionIndex;
        private double origin, phaseEnd, pickEnd;
        private bool ordersLocked;
        private static readonly string[] DefaultCompanies = { "BANK", "FOOD", "AUTO", "AI", "SPACE", "BIO" };

        public GameRoom(string roomId, IMarketService market, IInformationService information,
            IServerClock clock, TimerMode mode = TimerMode.Realtime, bool fixedOrderForFixture = false)
        {
            if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("Room ID is required.");
            this.roomId = roomId;
            this.market = market ?? throw new ArgumentNullException(nameof(market));
            this.information = information ?? throw new ArgumentNullException(nameof(information));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.mode = mode;
            fixedOrder = fixedOrderForFixture;
        }
        private double Now { get { return clock.Seconds - origin; } }
        private RequestContext Context(string player = "", string request = "")
        { return new RequestContext(roomId, turn, phase, player, request); }
        private string CurrentPlayer { get { return phase == Protocol.Selection && selectionIndex < 8 ? order[selectionIndex] : ""; } }

        // Tokens are generated on the server and are never included in a public roster.
        public string Join(string connectionId, string displayName, string reconnectToken = null)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(connectionId) || members.Any(m => m.Connection == connectionId)) return "INVALID_CONNECTION";
                if (phase == Protocol.Ended) return "ROOM_ENDED";
                if (!string.IsNullOrEmpty(reconnectToken))
                {
                    Member returning = members.Find(m => m.Token == reconnectToken);
                    if (returning == null || returning.Connection != null || clock.Seconds >= returning.DisconnectedAt + 30)
                        return "INVALID_RECONNECT_TOKEN";
                    returning.Connection = connectionId;
                    Welcome(returning);
                    PublishState();
                    if (turn > 0) SendView(returning.Id);
                    return "";
                }
                if (phase != Protocol.Lobby) return "GAME_STARTED";
                if (members.Count == 4) return "ROOM_FULL";
                if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 32) return "INVALID_NAME";
                var member = new Member { Id = "P" + (members.Count + 1), Name = displayName,
                    Connection = connectionId, Token = NewToken() };
                members.Add(member);
                Welcome(member);
                PublishState();
                return "";
            }
        }
        private static string NewToken()
        {
            byte[] bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
        private void Welcome(Member member)
        {
            Send(member.Id, new ServerMessage { type = "WELCOME", session = new Session {
                playerId = member.Id, reconnectToken = member.Token } });
        }
        public string PlayerForConnection(string connectionId)
        { lock (gate) return members.Find(m => m.Connection == connectionId)?.Id; }
        public string ConnectionForPlayer(string playerId)
        { lock (gate) return members.Find(m => m.Id == playerId)?.Connection; }

        public void Disconnect(string connectionId)
        {
            lock (gate)
            {
                Member member = members.Find(m => m.Connection == connectionId);
                if (member == null) return;
                member.Connection = null;
                member.DisconnectedAt = clock.Seconds;
                if (member.Id == "P1") End("HOST_DISCONNECTED");
                else PublishState();
            }
        }
        public void Shutdown()
        { lock (gate) if (phase != Protocol.Ended) End("HOST_DISCONNECTED"); }

        public CommandResult ProcessCommand(string connectionId, Command input)
        { return ProcessCommandAt(connectionId, input, clock.Seconds); }

        // Only the server transport supplies receipt time. This value is absent from the wire DTO.
        public CommandResult ProcessCommandAt(string connectionId, Command input, double receivedAtServerSeconds)
        {
            lock (gate)
            {
                Member member = members.Find(m => m.Connection == connectionId);
                Command command = Copy(input);
                if (member == null) return CommandResult.Fail(command, "NOT_CONNECTED");
                if (string.IsNullOrEmpty(command.requestId) || command.requestId.Length > 64 ||
                    string.IsNullOrEmpty(command.type) || command.type.Length > 40)
                    return Reply(member.Id, CommandResult.Fail(command, "INVALID_REQUEST"));
                string key = member.Id + ":" + command.turn + ":" + command.requestId;
                string fingerprint = Fingerprint(command);
                Cached previous;
                // Replay is checked before stage, deadline, quota or ended-state checks.
                if (cache.TryGetValue(key, out previous))
                    return Reply(member.Id, previous.Fingerprint == fingerprint ? Copy(previous.Result)
                        : CommandResult.Fail(command, "REQUEST_ID_CONFLICT"));
                string quotaKey = member.Id + ":" + turn;
                int count;
                counts.TryGetValue(quotaKey, out count);
                if (count >= 256) return Reply(member.Id, CommandResult.Fail(command, "RATE_LIMITED"));
                counts[quotaKey] = count + 1;
                double received = receivedAtServerSeconds - origin;
                bool expired = turn > 0 && phase != Protocol.Ended &&
                    received >= (phase == Protocol.Selection ? pickEnd : phaseEnd);
                if (mode == TimerMode.Realtime) AdvanceTo(received);
                CommandResult result;
                if (command.turn != turn) result = CommandResult.Fail(command, "STALE_TURN");
                else if (phase == Protocol.Ended) result = CommandResult.Fail(command, "ROOM_ENDED");
                else if (expired) result = CommandResult.Fail(command, "DEADLINE_EXCEEDED");
                else result = Execute(member, command, received);
                // Snapshot before delivery so consumers cannot mutate the cached response.
                cache[key] = new Cached { Fingerprint = fingerprint, Result = Copy(result) };
                return Reply(member.Id, result);
            }
        }
        private CommandResult Execute(Member member, Command command, double now)
        {
            if (command.type == "SET_READY")
            {
                if (phase != Protocol.Lobby) return CommandResult.Fail(command, "WRONG_PHASE");
                if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
                member.Ready = command.payload.ready;
                PublishState();
                return Success(command);
            }
            if (command.type == "START_GAME")
            {
                if (phase != Protocol.Lobby) return CommandResult.Fail(command, "WRONG_PHASE");
                if (member.Id != "P1") return CommandResult.Fail(command, "HOST_ONLY");
                if (members.Count != 4 || members.Any(m => m.Connection == null || !m.Ready))
                    return CommandResult.Fail(command, "NOT_ALL_READY");
                origin = clock.Seconds;
                BeginTurn();
                return Success(command);
            }
            if (command.type == "COMPLETE_PHASE")
            {
                if (phase != Protocol.Info && phase != Protocol.Reveal && phase != Protocol.Result)
                    return CommandResult.Fail(command, "WRONG_PHASE");
                completed.Add(member.Id);
                if (completed.Count == 4) NextPhase(now);
                return Success(command);
            }
            if (command.type == "GET_PLAYER_VIEW")
            {
                if (turn == 0) return CommandResult.Fail(command, "WRONG_PHASE");
                SendView(member.Id);
                return Success(command);
            }
            string required = command.type == "BUY_INFORMATION" ? Protocol.Info :
                command.type == "SELECT_COMPANY" ? Protocol.Selection :
                command.type == "SUBMIT_INVESTMENT" ? Protocol.Investment : null;
            if (required == null) return CommandResult.Fail(command, "INVALID_REQUEST");
            if (phase != required) return CommandResult.Fail(command, "WRONG_PHASE");
            if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
            if (phase == Protocol.Selection && member.Id != CurrentPlayer)
                return CommandResult.Fail(command, "NOT_YOUR_TURN");
            RequestContext context = Context(member.Id, command.requestId);
            RuleResult rule;
            if (command.type == "BUY_INFORMATION")
                rule = information.BuyInformation(context, command.payload.companyId, command.payload.bundleIndex);
            else if (command.type == "SELECT_COMPANY")
                rule = market.ValidateAndSelectCompany(context, command.payload.companyId);
            else rule = market.SubmitInvestment(context, command.payload.investments);
            if (!rule.Success) return CommandResult.Fail(command, rule.ErrorCode);
            if (command.type == "SELECT_COMPANY") Selected(now);
            if (command.type == "SUBMIT_INVESTMENT" && members.All(m => market.IsOrderLocked(m.Id))) NextPhase(now);
            SendView(member.Id);
            return Success(command, rule.Payload);
        }
        private static CommandResult Success(Command command, ResultPayload payload = null)
        {
            return new CommandResult { type = command.type + "_RESULT", requestId = command.requestId,
                turn = command.turn, success = true, payload = payload };
        }
        private void BeginTurn()
        {
            // Baseline prototype intentionally ends after one turn. Auctions/multi-turn are a separate increment.
            turn = 1;
            if (!begun.Add(turn)) return;
            string[] ids = members.Select(m => m.Id).ToArray();
            string[] seats = (string[])ids.Clone();
            if (!fixedOrder)
            {
                var random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
                for (int i = seats.Length - 1; i > 0; i--) { int j = random.Next(i + 1); string s = seats[i]; seats[i] = seats[j]; seats[j] = s; }
            }
            order = seats.Concat(seats.Reverse()).ToArray();
            phase = Protocol.Info;
            market.BeginTurn(Context(), ids);
            information.GrantTurnCoins(Context(), ids);
            Enter(Protocol.Info, 0, 50);
            foreach (Member member in members) SendView(member.Id);
        }
        private void Enter(string next, double now, double duration)
        {
            phase = next;
            phaseEnd = now + duration;
            pickEnd = next == Protocol.Selection ? now + 4 : 0;
            completed.Clear();
            PublishState();
        }
        private void Selected(double now)
        {
            selectionIndex++;
            if (selectionIndex == 8) Enter(Protocol.Investment, now, 23);
            else { pickEnd = Math.Min(now + 4, phaseEnd); PublishState(); }
        }
        public void Tick()
        { lock (gate) if (mode == TimerMode.Realtime) AdvanceTo(Now); }
        private void AdvanceTo(double now)
        {
            while (phase != Protocol.Lobby && phase != Protocol.Ended &&
                now >= (phase == Protocol.Selection ? pickEnd : phaseEnd))
            {
                if (phase == Protocol.Selection) AutoSelect(pickEnd);
                else NextPhase(phaseEnd);
            }
        }
        // Explicit server-side fixture control; never exposed as a network command.
        public void AdvanceManualPhase()
        {
            lock (gate)
            {
                if (mode != TimerMode.ManualForFixture) throw new InvalidOperationException("Not in manual fixture mode.");
                if (phase == Protocol.Selection) AutoSelect(Now);
                else if (phase != Protocol.Lobby && phase != Protocol.Ended) NextPhase(Now);
            }
        }
        private void AutoSelect(double now)
        {
            string player = CurrentPlayer;
            string[] selected = market.GetSelectedCompanies(player);
            CompanySeat[] seats = market.GetCompanySeats();
            foreach (string company in DefaultCompanies)
            {
                if (selected.Contains(company) || !seats.Any(s => s.companyId == company && s.remainingSeats > 0)) continue;
                RuleResult result = market.ValidateAndSelectCompany(Context(player, "auto-pick-" + selectionIndex), company);
                if (!result.Success) continue;
                ResultPayload payload = result.Payload ?? new ResultPayload { companyId = company };
                payload.automatic = true;
                Send(player, new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult {
                    type = "SELECT_COMPANY_RESULT", requestId = "auto-pick-" + selectionIndex,
                    turn = turn, success = true, payload = payload } });
                Selected(now);
                SendView(player);
                return;
            }
            throw new InvalidOperationException("B service cannot commit a legal automatic selection.");
        }
        private void NextPhase(double now)
        {
            switch (phase)
            {
                case Protocol.Info: Enter(Protocol.Selection, now, 32); break;
                case Protocol.Investment:
                    foreach (Member member in members.Where(m => !market.IsOrderLocked(m.Id)))
                    {
                        string[] companies = market.GetSelectedCompanies(member.Id);
                        var investments = companies.Select(c => new Investment { companyId = c, weightBps = 5000,
                            intervals = new[] { new Interval { entryDay = 0, exitDay = 5 } } }).ToArray();
                        RuleResult result = market.SubmitInvestment(Context(member.Id, "auto-order"), investments);
                        if (!result.Success) throw new InvalidOperationException("B service rejected default order: " + result.ErrorCode);
                        ResultPayload payload = result.Payload ?? new ResultPayload { locked = true, investments = investments };
                        payload.automatic = true;
                        Send(member.Id, new ServerMessage { type = "COMMAND_RESULT", response = new CommandResult {
                            type = "SUBMIT_INVESTMENT_RESULT", requestId = "auto-order", turn = turn, success = true, payload = payload } });
                        SendView(member.Id);
                    }
                    if (!ordersLocked) { market.LockAllOrders(Context()); ordersLocked = true; }
                    Enter(Protocol.Reveal, now, 10);
                    Broadcast(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = market.GetRevealedMarket(Context()) });
                    break;
                case Protocol.Reveal:
                    Scoreboard scoreboard = null;
                    if (!settled.Contains(turn)) { scoreboard = market.SettleTurn(Context()); settled.Add(turn); }
                    Enter(Protocol.Result, now, 5);
                    if (scoreboard != null) Broadcast(new ServerMessage { type = "SCOREBOARD", scoreboard = scoreboard });
                    foreach (Member member in members) SendView(member.Id);
                    break;
                case Protocol.Result: End("GAME_COMPLETE"); break;
            }
        }
        private void End(string reason)
        {
            phase = Protocol.Ended;
            phaseEnd = pickEnd = 0;
            Broadcast(new ServerMessage { type = "ROOM_ENDED", errorCode = reason });
            PublishState();
        }
        public PublicState Snapshot()
        {
            lock (gate) return new PublicState { roomId = roomId, turn = turn, phase = phase,
                selectionIndex = selectionIndex, currentPlayerId = CurrentPlayer,
                serverElapsedSeconds = turn > 0 ? Now : 0, phaseEndsAtServerSeconds = phaseEnd,
                selectionDeadlineAtServerSeconds = pickEnd,
                players = members.Select(m => new Player { playerId = m.Id, displayName = m.Name,
                    connected = m.Connection != null, ready = m.Ready }).ToArray(),
                companySeats = turn > 0 ? market.GetCompanySeats() : new CompanySeat[0] };
        }
        private void SendView(string player)
        {
            Send(player, new ServerMessage { type = "PLAYER_VIEW", playerView = information.BuildPlayerView(
                Context(player), market.GetOwnInvestmentState(player)) });
        }
        private void PublishState() { Broadcast(new ServerMessage { type = "PUBLIC_STATE", state = Snapshot() }); }
        private void Broadcast(ServerMessage message)
        { foreach (Member member in members) if (member.Connection != null) Send(member.Id, message); }
        private void Send(string player, ServerMessage message) { outbox.Enqueue(new Delivery(player, message)); }
        private CommandResult Reply(string player, CommandResult result)
        { Send(player, new ServerMessage { type = "COMMAND_RESULT", response = Copy(result) }); return result; }
        public Delivery[] DrainMessages()
        { lock (gate) { Delivery[] result = outbox.ToArray(); outbox.Clear(); return result; } }

        private static Investment[] Copy(Investment[] input)
        {
            return input?.Select(i => i == null ? null : new Investment { companyId = i.companyId, weightBps = i.weightBps,
                intervals = i.intervals?.Select(t => t == null ? null : new Interval { entryDay = t.entryDay, exitDay = t.exitDay }).ToArray() }).ToArray();
        }
        private static Command Copy(Command input)
        {
            return new Command { type = input?.type, requestId = input?.requestId, turn = input?.turn ?? 0,
                payload = input?.payload == null ? null : new CommandPayload { companyId = input.payload.companyId,
                    bundleIndex = input.payload.bundleIndex, ready = input.payload.ready, investments = Copy(input.payload.investments) } };
        }
        private static CommandResult Copy(CommandResult input)
        {
            ResultPayload p = input.payload;
            return new CommandResult { type = input.type, requestId = input.requestId, turn = input.turn,
                success = input.success, errorCode = input.errorCode, payload = p == null ? null : new ResultPayload {
                    companyId = p.companyId, remainingCoins = p.remainingCoins, bundleIndex = p.bundleIndex,
                    startDay = p.startDay, dailyChangesBps = (int[])p.dailyChangesBps?.Clone(), locked = p.locked,
                    automatic = p.automatic, investments = Copy(p.investments) } };
        }
        private static string Fingerprint(Command c)
        {
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8))
            {
                writer.Write(c.type ?? ""); writer.Write(c.payload != null);
                if (c.payload != null)
                {
                    writer.Write(c.payload.companyId ?? ""); writer.Write(c.payload.bundleIndex); writer.Write(c.payload.ready);
                    writer.Write(c.payload.investments?.Length ?? -1);
                    if (c.payload.investments != null) foreach (Investment i in c.payload.investments)
                    {
                        writer.Write(i != null); if (i == null) continue;
                        writer.Write(i.companyId ?? ""); writer.Write(i.weightBps); writer.Write(i.intervals?.Length ?? -1);
                        if (i.intervals != null) foreach (Interval t in i.intervals)
                        { writer.Write(t != null); if (t != null) { writer.Write(t.entryDay); writer.Write(t.exitDay); } }
                    }
                }
                writer.Flush();
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes.ToArray()));
            }
        }
    }
}
