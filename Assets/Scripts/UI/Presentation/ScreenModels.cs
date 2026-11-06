// NEXT-D05·D06·IMP-07 화면이 그리는 값(화면 객체 의존 없음).
// - 아직 서버 계약이 없는 화면(입찰·능력·상점·인벤토리)은 이 모델만 보고 그린다.
//   계약이 생기면 'DTO → 이 모델' 변환만 추가하면 되고 화면 코드는 그대로 둔다.
// - 전광판은 현재 계약(Scoreboard·PublicState)에서 바로 만들 수 있다(FromScoreboard).
// - 타인의 코인·구매 정보·투자 기업·금액은 모델에 넣지 않는다(공개 범위 유지).
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    // ------------------------------------------------------------------
    // 전광판(D05)
    // ------------------------------------------------------------------

    public enum BoardEventKind { Auction, Ability, Attack, Bonus, System }

    public sealed class BoardEvent
    {
        public int Turn;
        public BoardEventKind Kind;
        public string Text;

        public static string KindName(BoardEventKind k)
        {
            switch (k)
            {
                case BoardEventKind.Auction: return "입찰";
                case BoardEventKind.Ability: return "능력";
                case BoardEventKind.Attack: return "공격";
                case BoardEventKind.Bonus: return "보상";
                default: return "알림";
            }
        }
    }

    public sealed class BoardPlayer
    {
        public string Id, Name;
        public int Rank, PrevRank;              // PrevRank 0 = 이전 순위 모름
        public long TotalAssets;
        public long? PrevTotalAssets;            // 직전 턴 총자산(연출용 숫자 세기). 모르면 null
        public int TurnReturnBps, CumulativeReturnBps;
        public long CumulativeProfit;
        public List<KeyValuePair<int, int>> History = new List<KeyValuePair<int, int>>(); // (턴, 턴 수익률 bps)
        public bool IsMe;

        public int RankDelta => PrevRank <= 0 ? 0 : PrevRank - Rank; // +면 상승

        public int? BestTurnBps => History.Count == 0 ? (int?)null : History.Max(h => h.Value);
    }

    public sealed class ScoreboardModel
    {
        public const int TotalTurns = 10;
        public int Turn;
        public bool Final;
        public List<BoardPlayer> Players = new List<BoardPlayer>();
        public List<BoardEvent> Events = new List<BoardEvent>();
        public string FinalNote = "";   // 최종 동점 처리 근거 등(서버가 준 문구)

        public IEnumerable<BoardPlayer> Ranked => Players.OrderBy(p => p.Rank).ThenBy(p => p.Id);

        public static bool IsAuctionTurn(int turn) => turn == 3 || turn == 6 || turn == 9;

        /// <summary>현재 계약의 SCOREBOARD로 만든다. prevRanks·prevAssets는 화면이 기억해 둔 직전 턴 순위·총자산(정산 연출용, 없으면 null).</summary>
        public static ScoreboardModel FromScoreboard(Scoreboard board, PublicState state, string localPlayerId,
            IDictionary<string, int> prevRanks = null, IDictionary<string, long> prevAssets = null)
        {
            var model = new ScoreboardModel { Turn = board?.turn ?? state?.turn ?? 0 };
            if (board?.rows == null) return model;
            foreach (var r in board.rows)
            {
                if (r == null) continue;
                var name = state?.players?.FirstOrDefault(p => p.playerId == r.playerId)?.displayName;
                int prev = 0;
                if (prevRanks != null && prevRanks.TryGetValue(r.playerId, out var pr)) prev = pr;
                long? prevAsset = null;
                if (prevAssets != null && prevAssets.TryGetValue(r.playerId, out var pa)) prevAsset = pa;
                model.Players.Add(new BoardPlayer
                {
                    Id = r.playerId,
                    Name = string.IsNullOrEmpty(name) ? r.playerId : name,
                    Rank = r.rank,
                    PrevRank = prev,
                    TotalAssets = r.totalAssets,
                    PrevTotalAssets = prevAsset,
                    TurnReturnBps = r.turnReturnBps,
                    CumulativeReturnBps = r.cumulativeReturnBps,
                    CumulativeProfit = r.cumulativeProfit,
                    History = (r.returnHistory ?? new ReturnPoint[0]).Where(h => h != null)
                        .OrderBy(h => h.turn).Select(h => new KeyValuePair<int, int>(h.turn, h.returnBps)).ToList(),
                    IsMe = r.playerId == localPlayerId
                });
            }
            model.Final = state?.phase == Protocol.Ended && model.Turn >= TotalTurns;
            return model;
        }
    }

    // ------------------------------------------------------------------
    // 입찰(D06)
    // ------------------------------------------------------------------

    public sealed class AuctionSeatResult
    {
        public string Seat, PlayerId, Name;
        public long Paid;
        public bool UsedFreePass;
    }

    public sealed class AuctionResultModel
    {
        public bool NoWinner;                 // 3·6턴 유찰(모두 0)
        public string WinnerId = "", WinnerName = "";
        public long WinningBid;
        public List<AuctionSeatResult> Seats = new List<AuctionSeatResult>(); // 9턴
    }

    public sealed class AuctionModel
    {
        public AuctionKind Kind;
        public long TurnStartAssets;           // S
        public long Bid;                       // 입력 중/확정한 금액
        public bool Submitted;                 // 확정(변경 불가)
        public int SubmittedCount, PlayerCount = 4;   // 제출 인원(금액은 비공개)
        public int SecondsLeft = 15;
        public bool HasFreePass, UseFreePass;
        public string[] SeatPreference = AuctionRules.Seats.ToArray();
        public AuctionResultModel Result;      // null = 아직 마감 전

        public long MaxBid => AuctionRules.MaxBid(TurnStartAssets);
        public int Turn => AuctionCatalog.TurnOf(Kind);
    }

    // ------------------------------------------------------------------
    // 고유능력 선택(D06)
    // ------------------------------------------------------------------

    public sealed class AbilityPickModel
    {
        public string[] Candidates = new string[0];   // 무작위 3개(서버 제공)
        public string Selected = "";                  // 로컬 후보
        public bool Confirmed;
        public int SecondsLeft = 20;

        /// <summary>미선택 시 서버가 적용하는 기본값: 제시된 첫 후보.</summary>
        public string DefaultPick => Candidates.Length > 0 ? Candidates[0] : "";
    }

    public sealed class AbilityStatusModel
    {
        public string AbilityId = "";
        public string State = "";      // 예: "방어 가능", "재사용 대기"
        public string Detail = "";     // 예: "7턴 시작부터 다시 방어"
    }

    // ------------------------------------------------------------------
    // 상점·인벤토리(D06)
    // ------------------------------------------------------------------

    public sealed class ShopModel
    {
        public long TurnStartAssets;   // S(가격 기준)
        public long Money;             // 지금 쓸 수 있는 돈
        public int BoughtThisTurn;
        public List<string> Inventory = new List<string>();
        public string Phase = Protocol.Info;
    }

    public sealed class TargetOption
    {
        public string Key, Label;
        public bool Enabled = true;
        public string Reason = "";
    }

    public sealed class InventoryModel
    {
        public List<string> Items = new List<string>();
        public string Phase = Protocol.Info;
        public int Turn = 1;
        public HashSet<ItemCategory> UsedThisTurn = new HashSet<ItemCategory>();
        public int SelectedSlot = -1;
        public string SelectedTarget = "";
        public string LastMessage = "";

        // 대상 후보(화면이 그대로 버튼으로 만든다). 서버 계약이 생기면 개인뷰에서 채운다.
        public List<TargetOption> OwnCompanies = new List<TargetOption>();
        public List<TargetOption> OwnIntervals = new List<TargetOption>();
        public List<TargetOption> Companies = new List<TargetOption>();
        public List<TargetOption> Players = new List<TargetOption>();
        public List<TargetOption> TopPlayers = new List<TargetOption>();

        public List<TargetOption> TargetsFor(ItemDef item)
        {
            if (item == null) return new List<TargetOption>();
            switch (item.Target)
            {
                case ItemTarget.OwnCompany: return OwnCompanies;
                case ItemTarget.OwnInterval: return OwnIntervals;
                case ItemTarget.Company: return Companies;
                case ItemTarget.Player: return Players;
                case ItemTarget.TopPlayer: return TopPlayers;
                default: return new List<TargetOption>();
            }
        }
    }
}
