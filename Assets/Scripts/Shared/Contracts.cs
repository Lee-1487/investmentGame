using System;

namespace BlackWhale.Shared
{
    public static class Protocol
    {
        public const string Version = "prototype-v2";
        public const string RulesVersion = "rules-v2";
        public const string Info = "INFO", Selection = "SELECTION", Investment = "INVESTMENT";
        public const string Reveal = "MARKET_REVEAL", Result = "RESULT", Lobby = "LOBBY", Ended = "ENDED";
        public const string AbilitySelection = "ABILITY_SELECTION", Auction = "AUCTION";
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
        public int historyTurn;
        public Investment[] investments;
        public bool ready;
        public string abilityId;
        public long bidAmount;
        public bool useFreePass;
        public string[] seatPreferences;
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
        public string source;
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
        public bool isBot;
    }
    [Serializable] public sealed class CompanySeat { public string companyId; public int remainingSeats; }
    [Serializable] public sealed class PublicState
    {
        public string schemaVersion = "1.0", roomId, phase, currentPlayerId = "";
        public int turn, selectionIndex;
        public bool paused;
        public double serverElapsedSeconds, phaseEndsAtServerSeconds, selectionDeadlineAtServerSeconds;
        public Player[] players;
        public CompanySeat[] companySeats;
        public string[] initialPlayerOrder, seatPlayerIds;
        public string botDifficulty;
        public PublicEvent[] events;
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
        public AbilityState ability;
        public AuctionState auction;
        public string[] inventory;
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
        public bool gameComplete;
        public string winnerPlayerId;
    }
    [Serializable] public sealed class AbilityState
    {
        public string[] candidates;
        public string selectedId;
        public int reusableAtTurn, freeInformationCredits;
    }
    [Serializable] public sealed class AuctionState
    {
        public long maximumBid, bidAmount;
        public bool confirmed, useFreePass;
        public string[] seatPreferences;
    }
    // Deliberately excludes bid amounts, private coins, orders and information values.
    [Serializable] public sealed class PublicEvent
    {
        public int turn;
        public string eventId, kind, playerId, targetPlayerId, detail;
    }
    [Serializable] public sealed class RevealedCompany { public string companyId; public int[] dailyChangesBps; }
    [Serializable] public sealed class HistorySnapshot
    {
        public int turn;
        public PlayerView playerView;
        public Scoreboard scoreboard;
    }
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
        public HistorySnapshot history;
    }
    [Serializable] public sealed class ClientMessage
    {
        public string type, displayName, reconnectToken;
        public Command command;
    }
}
