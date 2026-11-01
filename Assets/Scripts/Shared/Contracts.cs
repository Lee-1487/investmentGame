using System;

namespace BlackWhale.Shared
{
    public static class Protocol
    {
        public const string Version = "prototype-v2";
        public const string RulesVersion = "rules-v2";
        public const string Info = "INFO", Selection = "SELECTION", Investment = "INVESTMENT";
        public const string Reveal = "MARKET_REVEAL", Result = "RESULT", Lobby = "LOBBY", Ended = "ENDED";
    }

    // Field names and integer units intentionally match the frozen fixture format.
    [Serializable] public sealed class Interval { public int entryDay, exitDay; }
    [Serializable] public sealed class Investment
    {
        public string companyId;
        public int weightBps;
        public Interval[] intervals;
    }
    [Serializable] public sealed class CommandPayload
    {
        public string companyId;
        public int bundleIndex;
        public Investment[] investments;
        public bool ready;
    }
    [Serializable] public sealed class Command
    {
        public string type, requestId;
        public int turn;
        public CommandPayload payload;
    }
    [Serializable] public sealed class ResultPayload
    {
        public string companyId;
        public int remainingCoins, bundleIndex, startDay;
        public int[] dailyChangesBps;
        public bool locked, automatic;
        public Investment[] investments;
    }
    [Serializable] public sealed class CommandResult
    {
        public string type, requestId, errorCode = "";
        public int turn;
        public bool success;
        public ResultPayload payload;
        public static CommandResult Fail(Command command, string code)
        {
            return new CommandResult { type = (command.type ?? "UNKNOWN") + "_RESULT",
                requestId = command.requestId, turn = command.turn, errorCode = code };
        }
    }
    [Serializable] public sealed class Player
    {
        public string playerId, displayName;
        public bool connected, ready;
    }
    [Serializable] public sealed class CompanySeat { public string companyId; public int remainingSeats; }
    [Serializable] public sealed class PublicState
    {
        public string schemaVersion = "1.0", roomId, phase, currentPlayerId = "";
        public int turn, selectionIndex;
        public double serverElapsedSeconds, phaseEndsAtServerSeconds, selectionDeadlineAtServerSeconds;
        public Player[] players;
        public CompanySeat[] companySeats;
    }
    [Serializable] public sealed class VisibleBundle
    {
        public int bundleIndex, startDay;
        public string source;
        public int[] dailyChangesBps;
    }
    [Serializable] public sealed class VisibleCompany
    {
        public string companyId;
        public VisibleBundle[] visibleBundles;
    }
    [Serializable] public sealed class PlayerView
    {
        public string schemaVersion = "1.0", recipientPlayerId, phase;
        public int turn, coins;
        public long assets;
        public string[] selectedCompanies;
        public Investment[] ownInvestments;
        public VisibleCompany[] companies;
    }
    [Serializable] public sealed class ReturnPoint { public int turn, returnBps; }
    [Serializable] public sealed class ScoreRow
    {
        public int rank, turnReturnBps, cumulativeReturnBps;
        public string playerId;
        public long totalAssets, cumulativeProfit;
        public ReturnPoint[] returnHistory;
    }
    [Serializable] public sealed class Scoreboard
    {
        public string schemaVersion = "1.0", phase = Protocol.Result;
        public int turn;
        public bool rankingFinalForTurn = true;
        public ScoreRow[] rows;
    }
    [Serializable] public sealed class RevealedCompany { public string companyId; public int[] dailyChangesBps; }
    [Serializable] public sealed class Session
    {
        public string playerId, reconnectToken;
    }
    [Serializable] public sealed class ServerMessage
    {
        public string type, errorCode;
        public Session session;
        public PublicState state;
        public CommandResult response;
        public PlayerView playerView;
        public Scoreboard scoreboard;
        public RevealedCompany[] revealedMarket;
    }
    [Serializable] public sealed class ClientMessage
    {
        public string type, displayName, reconnectToken;
        public Command command;
    }
}
