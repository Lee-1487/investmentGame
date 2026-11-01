// 화면 버튼 -> A의 Command 생성 -> IGameClient.SendCommand
// IGameClient.MessageReceived -> 상태 저장소 반영
// 방장도 다른 참가자와 같은 IGameClient 경로를 쓴다(A: 호스트도 동일한 TCP 클라이언트 사용).
// B·C를 화면에서 직접 호출하지 않는다.
using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;

namespace BlackWhale.UI.State
{
    public enum CommandStatus
    {
        Sent,
        AlreadyPending,    // 같은 요청이 응답 대기 중(재클릭 방지)
        BlockedLocally,    // 화면에서 미리 막음(최종 판정은 서버)
        NoState
    }

    public sealed class GameClientController : IDisposable
    {
        private readonly IGameClient _client;
        private int _requestCounter;

        public ClientStateStore Store { get; }
        public string LocalPlayerId => Store.LocalPlayerId;
        /// <summary>BlockedLocally일 때의 사유(공통 오류 코드).</summary>
        public string LastLocalBlockCode { get; private set; } = "";

        public GameClientController(IGameClient client, ClientStateStore store = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Store = store ?? new ClientStateStore();
            _client.MessageReceived += OnMessage;
        }

        public void Dispose() => _client.MessageReceived -= OnMessage;

        private void OnMessage(ServerMessage message) => Store.Apply(message);

        // ------------------------------------------------------------------
        // 대기실
        // ------------------------------------------------------------------

        public CommandStatus SetReady(bool ready)
        {
            if (Store.Public == null) return CommandStatus.NoState;
            if (Store.HasPending(CommandTypes.SetReady)) return CommandStatus.AlreadyPending;
            Send(CommandTypes.SetReady, new CommandPayload { ready = ready });
            return CommandStatus.Sent;
        }

        /// <summary>방장(P1)만 가능. 4명 모두 준비해야 서버가 받아 준다.</summary>
        public CommandStatus StartGame()
        {
            if (Store.Public == null) return CommandStatus.NoState;
            if (Store.HasPending(CommandTypes.StartGame)) return CommandStatus.AlreadyPending;
            Send(CommandTypes.StartGame, new CommandPayload());
            return CommandStatus.Sent;
        }

        public bool IsHost => LocalPlayerId == "P1";

        public bool AllPlayersReady
            => Store.Public?.players != null && Store.Public.players.Length == 4
               && Store.Public.players.All(p => p.connected && p.ready);

        public bool IsLocalReady
            => Store.Public?.players?.FirstOrDefault(p => p.playerId == LocalPlayerId)?.ready ?? false;

        // ------------------------------------------------------------------
        // 게임 명령
        // ------------------------------------------------------------------

        public CommandStatus BuyInformation(string companyId, int bundleIndex)
        {
            if (Store.View == null) return CommandStatus.NoState;
            if (Store.IsPurchasePending(companyId, bundleIndex)) return CommandStatus.AlreadyPending;
            if (IsVisible(companyId, bundleIndex)) return Block(ErrorCodes.AlreadyVisible);

            Send(CommandTypes.BuyInformation, new CommandPayload { companyId = companyId, bundleIndex = bundleIndex },
                ClientStateStore.PurchaseKey(companyId, bundleIndex));
            return CommandStatus.Sent;
        }

        public CommandStatus SelectCompany(string companyId)
        {
            if (Store.Public == null) return CommandStatus.NoState;
            if (Store.HasPending(CommandTypes.SelectCompany)) return CommandStatus.AlreadyPending;
            if (!IsMyTurnToSelect()) return Block(ErrorCodes.NotYourTurn);

            Send(CommandTypes.SelectCompany, new CommandPayload { companyId = companyId }, companyId);
            return CommandStatus.Sent;
        }

        public CommandStatus SubmitInvestment(Investment[] investments)
        {
            if (Store.View == null) return CommandStatus.NoState;
            if (Store.HasPending(CommandTypes.SubmitInvestment)) return CommandStatus.AlreadyPending;
            if (Store.OrderLocked) return Block(ErrorCodes.OrderLocked);

            Send(CommandTypes.SubmitInvestment, new CommandPayload { investments = DtoCopy.Copy(investments) });
            return CommandStatus.Sent;
        }

        /// <summary>완료 버튼(COMPLETE_PHASE). 의도만 보내고 단계 진행은 A가 결정한다.</summary>
        public CommandStatus FinishPhase()
        {
            var s = Store.Public;
            if (s == null) return CommandStatus.NoState;
            if (!CanFinishPhase()) return Block(ErrorCodes.WrongPhase);
            var requestId = Send(CommandTypes.CompletePhase, new CommandPayload());
            Store.MarkPhaseCompleted(requestId, s.turn, s.phase);
            return CommandStatus.Sent;
        }

        public bool IsDoneForCurrentPhase
            => Store.Public != null && Store.IsPhaseCompleted(Store.Public.turn, Store.Public.phase);

        /// <summary>A 규칙: COMPLETE_PHASE는 INFO·시장 공개·결과 단계에서만. 투자 단계는 4명 주문이 잠기면 자동 진행.</summary>
        public bool CanFinishPhase()
        {
            var s = Store.Public;
            if (s == null || IsDoneForCurrentPhase) return false;
            return s.phase == Protocol.Info || s.phase == Protocol.Reveal || s.phase == Protocol.Result;
        }

        // ------------------------------------------------------------------
        // 화면 조회 도우미
        // ------------------------------------------------------------------

        public bool IsMyTurnToSelect()
        {
            var s = Store.Public;
            return s != null && s.phase == Protocol.Selection && s.currentPlayerId == LocalPlayerId
                   && !string.IsNullOrEmpty(LocalPlayerId);
        }

        public int RemainingSeats(string companyId)
            => Store.Public?.companySeats?.FirstOrDefault(c => c.companyId == companyId)?.remainingSeats ?? 0;

        /// <summary>선택 버튼 활성 여부(화면 편의용). 서버가 최종 판정.</summary>
        public bool CanSelect(string companyId)
            => IsMyTurnToSelect() && RemainingSeats(companyId) > 0 && !Store.OwnSelections.Contains(companyId)
               && !Store.HasPending(CommandTypes.SelectCompany);

        public bool IsVisible(string companyId, int bundleIndex)
            => Store.View?.companies?.FirstOrDefault(c => c.companyId == companyId)?
                   .visibleBundles?.Any(b => b.bundleIndex == bundleIndex) ?? false;

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        private string Send(string type, CommandPayload payload, string pendingKey = "")
        {
            int turn = Store.CurrentTurn;
            var command = new Command
            {
                type = type,
                requestId = NextRequestId(type, turn),
                turn = turn,
                payload = payload
            };
            Store.MarkPending(command.requestId, type, turn, pendingKey);
            _client.SendCommand(command);
            return command.requestId;
        }

        private CommandStatus Block(string code)
        {
            LastLocalBlockCode = code;
            return CommandStatus.BlockedLocally;
        }

        private string NextRequestId(string type, int turn)
        {
            _requestCounter++;
            string prefix = type == CommandTypes.BuyInformation ? "info"
                : type == CommandTypes.SelectCompany ? "pick"
                : type == CommandTypes.SubmitInvestment ? "order"
                : type.ToLowerInvariant();
            // A 제한: requestId 64자 이하
            return prefix + "-t" + turn + "-" + _requestCounter.ToString("000");
        }
    }
}
