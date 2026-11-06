using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using InvestmentGame.Rules.Information;

namespace BlackWhale.Rules.Information
{
    // A and D use Shared DTOs. C's domain models remain inside this server adapter.
    public sealed partial class SharedInformationService : IInformationService, IProgressionService
    {
        private sealed class RoomState
        {
            public InformationService Service;
            public int CurrentTurn;
            public readonly HashSet<string> Players = new HashSet<string>();
        }

        private readonly object gate = new object();
        private readonly IMarketDataSource source;
        private readonly Dictionary<string, RoomState> rooms = new Dictionary<string, RoomState>();

        public SharedInformationService(IMarketDataSource source)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); }

        public void GrantTurnCoins(RequestContext context, string[] playerIds)
        {
            lock (gate)
            {
                if (!rooms.TryGetValue(context.RoomId, out var room))
                {
                    room = new RoomState { Service = new InformationService(new MarketReader(source), source.GetCompanyIds()) };
                    rooms.Add(context.RoomId, room);
                }
                if (context.Turn < room.CurrentTurn) return; // late turn-start replay must not change current coins
                foreach (var id in playerIds)
                    if (room.Players.Add(id)) room.Service.AddPlayer(new PlayerInformationSeed(id, 0));
                room.Service.GrantTurnCoins(context.Turn, playerIds);
                room.CurrentTurn = context.Turn;
            }
        }

        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            lock (gate)
            {
                var room = rooms[context.RoomId];
                var feature = FeaturePlayer(context.RoomId, context.PlayerId);
                bool free = feature != null && feature.FreeCredits > 0;
                int beforeCoins = room.Service.GetCoins(context.PlayerId);
                var result = room.Service.BuyInformation(context.PlayerId, room.CurrentTurn, context.Phase,
                    new InformationRequest(context.RequestId, context.Turn, companyId, bundleIndex), free);
                if (!result.Success) return new RuleResult(result.ErrorCode);
                // The domain cache can replay a previous response. Consume a credit only once.
                string purchaseKey = context.Turn + ":" + context.RequestId;
                if (free && result.RemainingCoins == beforeCoins && feature.FreePurchases.Add(purchaseKey)) feature.FreeCredits--;
                return new RuleResult(payload: new ResultPayload
                {
                    companyId = result.CompanyId, bundleIndex = result.BundleIndex,
                    startDay = result.StartDay, remainingCoins = result.RemainingCoins,
                    dailyChangesBps = result.DailyChangesBps.ToArray(),
                    source = feature != null && feature.FreePurchases.Contains(purchaseKey) ? "ABILITY" : "PURCHASED"
                });
            }
        }

        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
        {
            lock (gate)
            {
                var view = rooms[context.RoomId].Service.BuildPlayerView(context.PlayerId, context.Turn);
                return new PlayerView
                {
                    recipientPlayerId = context.PlayerId, turn = context.Turn, phase = context.Phase,
                    coins = view.Coins, assets = ownState.TotalAssets,
                    selectedCompanies = (ownState.SelectedCompanies ?? Array.Empty<string>()).ToArray(),
                    ownInvestments = (ownState.Investments ?? Array.Empty<Investment>()).Select(i => new Investment
                    {
                        companyId = i.companyId, weightBps = i.weightBps,
                        intervals = (i.intervals ?? Array.Empty<Interval>()).Select(v => new Interval
                        { entryDay = v.entryDay, exitDay = v.exitDay }).ToArray()
                    }).ToArray(),
                    companies = view.Companies.Select(c => new VisibleCompany
                    {
                        companyId = c.CompanyId,
                        visibleBundles = c.VisibleBundles.Select(b => new VisibleBundle
                        {
                            bundleIndex = b.BundleIndex, startDay = b.StartDay,
                            source = b.Source, dailyChangesBps = b.DailyChangesBps.ToArray()
                        }).ToArray()
                    }).ToArray()
                };
            }
        }

        private sealed class MarketReader : IMarketInformationReader
        {
            private readonly IMarketDataSource source;
            public MarketReader(IMarketDataSource source) { this.source = source; }

            public IReadOnlyList<int> GetPublicBundleIndices(int turn, string companyId)
            {
                var indices = new List<int>();
                for (int i = 0; i < InformationConstants.BundleCount; i++)
                    if (Read(turn, companyId, i) is ServerInformationBundle b && b.IsPublic) indices.Add(i);
                return indices;
            }

            public bool TryGetBundle(int turn, string companyId, int bundleIndex, out MarketInformationBundle bundle)
            {
                var value = Read(turn, companyId, bundleIndex);
                bundle = value == null ? null : new MarketInformationBundle(companyId, bundleIndex, value.DailyChangesBps);
                return bundle != null;
            }

            private ServerInformationBundle Read(int turn, string companyId, int index)
            {
                ServerInformationBundle value;
                try { value = source.GetBundle(turn, companyId, index); }
                catch (KeyNotFoundException) { return null; }
                catch (ArgumentException) { return null; }
                if (value == null || value.CompanyId != companyId || value.BundleIndex != index ||
                    value.StartDay != index * InformationConstants.BundleSizeDays + 1 ||
                    value.DailyChangesBps == null || value.DailyChangesBps.Length != InformationConstants.BundleSizeDays)
                    return null;
                return value;
            }
        }
    }
}
