using System;

namespace BlackWhale.Shared
{
    // Server-created, immutable context. A connection's identity never comes from JSON.
    public sealed class RequestContext
    {
        public string RoomId { get; private set; }
        public int Turn { get; private set; }
        public string Phase { get; private set; }
        public string PlayerId { get; private set; }
        public string RequestId { get; private set; }
        public RequestContext(string roomId, int turn, string phase, string playerId = "", string requestId = "")
        { RoomId = roomId; Turn = turn; Phase = phase; PlayerId = playerId; RequestId = requestId; }
    }
    public sealed class RuleResult
    {
        public bool Success { get { return ErrorCode == ""; } }
        public string ErrorCode { get; private set; }
        public ResultPayload Payload { get; private set; }
        public RuleResult(string errorCode = "", ResultPayload payload = null)
        { ErrorCode = errorCode; Payload = payload; }
    }
    public sealed class OwnInvestmentState
    {
        public long TotalAssets;
        public string[] SelectedCompanies;
        public Investment[] Investments;
    }
    // Server-only B -> C source. This object is never a client message or a replicated state.
    public sealed class ServerInformationBundle
    {
        public string CompanyId;
        public int BundleIndex, StartDay;
        public bool IsPublic;
        public int[] DailyChangesBps;
    }
    public interface IMarketDataSource
    {
        string[] GetCompanyIds();
        // Server-only. Real B retains immutable bundles for completed turns for the room's lifetime.
        ServerInformationBundle GetBundle(int turn, string companyId, int bundleIndex);
    }
    // B owns all selections, capacity, orders, market and asset state.
    public interface IMarketService : IMarketDataSource
    {
        void BeginTurn(RequestContext context, string[] playerIds);
        RuleResult ValidateAndSelectCompany(RequestContext context, string companyId);
        CompanySeat[] GetCompanySeats();
        string[] GetSelectedCompanies(string playerId);
        RuleResult SubmitInvestment(RequestContext context, Investment[] investments);
        bool IsOrderLocked(string playerId);
        void LockAllOrders(RequestContext context);
        Scoreboard SettleTurn(RequestContext context);
        OwnInvestmentState GetOwnInvestmentState(string playerId);
        RevealedCompany[] GetRevealedMarket(RequestContext context);
    }
    // C must implement its own idempotent grant ledger and atomic purchase operation.
    public interface IInformationService
    {
        void GrantTurnCoins(RequestContext context, string[] playerIds);
        RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex);
        PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState);
    }
    public interface IServerClock { double Seconds { get; } }
    public interface IWireCodec
    {
        string EncodeClient(ClientMessage message);
        ClientMessage DecodeClient(string json);
        string EncodeServer(ServerMessage message);
        ServerMessage DecodeServer(string json);
    }
    // D subscribes to messages; both the host and remote players use this client.
    public interface IGameClient : IDisposable
    {
        event Action<ServerMessage> MessageReceived;
        void SendCommand(Command command);
        void Pump();
    }
}
