// [개발 전용] D 화면 확인용 B·C 대역(stand-in). 실제 B·C 구현이 아니다.
// - 시장: fixture(B_market_server.json)의 고정 주가·무료 공개 위치를 그대로 쓴다.
// - 주문 검사: D의 화면 사전검사(OrderInputValidator)를 재사용한다. 정식 검증은 B 담당.
// - 정산: 계산하지 않고 fixture 기대 전광판(D_scoreboard_expected.json)을 그대로 돌려준다.
// - 코인: 1~5턴 3개, 6~10턴 5개 지급(멱등). 정식 원장은 C 담당.
// 실제 B·C가 IMarketService / IInformationService를 구현하면 이 파일 대신 그것을 넣는다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Dev
{
    [Serializable] public sealed class DevMarketCompany { public string companyId; public int[] dailyChangesBps; public int[] publicBundleIndices; }
    [Serializable] public sealed class DevMarketFixture { public DevMarketCompany[] companies; }

    public sealed class DevMarketService : IMarketService
    {
        private readonly DevMarketFixture _market;
        private readonly Scoreboard _scriptedScoreboard;
        private readonly Dictionary<string, List<string>> _selected = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, Investment[]> _orders = new Dictionary<string, Investment[]>();
        private bool _locked, _settled;

        public DevMarketService(DevMarketFixture market, Scoreboard scriptedScoreboard)
        {
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _scriptedScoreboard = scriptedScoreboard;
        }

        public string[] GetCompanyIds() => _market.companies.Select(c => c.companyId).ToArray();

        public ServerInformationBundle GetBundle(int turn, string companyId, int bundleIndex)
        {
            var c = _market.companies.FirstOrDefault(x => x.companyId == companyId);
            if (c == null || bundleIndex < 0 || bundleIndex >= UiRules.BundleCount) throw new ArgumentOutOfRangeException();
            int start = GraphSeriesBuilder.BundleStartDay(bundleIndex);
            return new ServerInformationBundle
            {
                CompanyId = companyId,
                BundleIndex = bundleIndex,
                StartDay = start,
                IsPublic = c.publicBundleIndices != null && c.publicBundleIndices.Contains(bundleIndex),
                DailyChangesBps = c.dailyChangesBps.Skip(start).Take(UiRules.BundleSizeDays).ToArray()
            };
        }

        public void BeginTurn(RequestContext context, string[] playerIds)
        {
            _selected.Clear();
            _orders.Clear();
            _locked = _settled = false;
            foreach (var id in playerIds) _selected[id] = new List<string>();
        }

        public RuleResult ValidateAndSelectCompany(RequestContext context, string companyId)
        {
            if (!UiRules.IsKnownCompany(companyId)) return new RuleResult(ErrorCodes.InvalidCompany);
            var mine = Selected(context.PlayerId);
            if (mine.Contains(companyId)) return new RuleResult(ErrorCodes.DuplicateCompany);
            if (_selected.Values.Count(s => s.Contains(companyId)) >= UiRules.CompaniesPerOrder)
                return new RuleResult(ErrorCodes.CompanyFull);
            mine.Add(companyId);
            return new RuleResult(payload: new ResultPayload { companyId = companyId });
        }

        public CompanySeat[] GetCompanySeats()
            => GetCompanyIds().Select(id => new CompanySeat
            {
                companyId = id,
                remainingSeats = UiRules.CompaniesPerOrder - _selected.Values.Count(s => s.Contains(id))
            }).ToArray();

        public string[] GetSelectedCompanies(string playerId) => Selected(playerId).ToArray();

        public RuleResult SubmitInvestment(RequestContext context, Investment[] investments)
        {
            if (_locked || _orders.ContainsKey(context.PlayerId)) return new RuleResult(ErrorCodes.OrderLocked);
            string error = OrderInputValidator.Validate(investments, Selected(context.PlayerId));
            if (error.Length > 0) return new RuleResult(error);
            _orders[context.PlayerId] = DtoCopy.Copy(investments);
            return new RuleResult(payload: new ResultPayload { locked = true, investments = DtoCopy.Copy(investments) });
        }

        public bool IsOrderLocked(string playerId) => _orders.ContainsKey(playerId);

        public void LockAllOrders(RequestContext context) => _locked = true;

        public Scoreboard SettleTurn(RequestContext context)
        {
            _settled = true;
            return _scriptedScoreboard; // 대역: 고정 전광판 전달만 한다(손익 계산은 B 담당)
        }

        public OwnInvestmentState GetOwnInvestmentState(string playerId)
        {
            var row = _settled ? _scriptedScoreboard?.rows?.FirstOrDefault(r => r.playerId == playerId) : null;
            return new OwnInvestmentState
            {
                TotalAssets = row?.totalAssets ?? 10000,
                SelectedCompanies = GetSelectedCompanies(playerId),
                Investments = _orders.TryGetValue(playerId, out var own) ? DtoCopy.Copy(own) : new Investment[0]
            };
        }

        public RevealedCompany[] GetRevealedMarket(RequestContext context)
        {
            if (!_locked) throw new InvalidOperationException("주문 잠금 전에는 시장 원본을 공개하지 않는다.");
            return _market.companies.Select(c => new RevealedCompany
            {
                companyId = c.companyId,
                dailyChangesBps = (int[])c.dailyChangesBps.Clone()
            }).ToArray();
        }

        private List<string> Selected(string playerId)
        {
            if (!_selected.TryGetValue(playerId, out var list))
            {
                list = new List<string>();
                _selected[playerId] = list;
            }
            return list;
        }
    }

    public sealed class DevInformationService : IInformationService
    {
        private readonly IMarketDataSource _source;
        private readonly Dictionary<string, int> _coins = new Dictionary<string, int>();
        private readonly Dictionary<string, HashSet<string>> _purchases = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _grants = new HashSet<string>();

        /// <summary>개발 도구: 다음 구매 1회를 이 오류로 거절(UI 오류 표시 확인용).</summary>
        public string ForceNextBuyError { get; set; }

        public DevInformationService(IMarketDataSource source) => _source = source;

        public void GrantTurnCoins(RequestContext context, string[] playerIds)
        {
            if (!_grants.Add(context.RoomId + ":" + context.Turn)) return; // 같은 턴 중복 지급 방지
            foreach (var id in playerIds)
            {
                if (!_coins.ContainsKey(id)) _coins[id] = 0;
                _purchases[id] = new HashSet<string>(); // 이전 턴 구매권으로 새 턴 정보를 열람하지 못함
                _coins[id] += context.Turn < 6 ? 3 : 5;
            }
        }

        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            if (!string.IsNullOrEmpty(ForceNextBuyError))
            {
                var forced = ForceNextBuyError;
                ForceNextBuyError = null;
                return new RuleResult(forced);
            }
            if (!_source.GetCompanyIds().Contains(companyId)) return new RuleResult(ErrorCodes.InvalidCompany);
            if (bundleIndex < 0 || bundleIndex >= UiRules.BundleCount) return new RuleResult(ErrorCodes.InvalidBundle);

            var bundle = _source.GetBundle(context.Turn, companyId, bundleIndex);
            var key = companyId + ":" + bundleIndex;
            if (bundle.IsPublic || _purchases[context.PlayerId].Contains(key)) return new RuleResult(ErrorCodes.AlreadyVisible);
            if (_coins[context.PlayerId] < 1) return new RuleResult(ErrorCodes.NotEnoughCoins);

            _coins[context.PlayerId]--;
            _purchases[context.PlayerId].Add(key);
            return new RuleResult(payload: new ResultPayload
            {
                companyId = companyId,
                bundleIndex = bundleIndex,
                startDay = bundle.StartDay,
                dailyChangesBps = (int[])bundle.DailyChangesBps.Clone(),
                remainingCoins = _coins[context.PlayerId]
            });
        }

        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
        {
            var id = context.PlayerId;
            return new PlayerView
            {
                recipientPlayerId = id,
                turn = context.Turn,
                phase = context.Phase,
                assets = ownState.TotalAssets,
                coins = _coins.TryGetValue(id, out var c) ? c : 0,
                selectedCompanies = ownState.SelectedCompanies,
                ownInvestments = ownState.Investments,
                companies = _source.GetCompanyIds().Select(companyId => new VisibleCompany
                {
                    companyId = companyId,
                    visibleBundles = Enumerable.Range(0, UiRules.BundleCount)
                        .Select(i => _source.GetBundle(context.Turn, companyId, i))
                        .Where(b => b.IsPublic || (_purchases.TryGetValue(id, out var p) && p.Contains(companyId + ":" + b.BundleIndex)))
                        .Select(b => new VisibleBundle
                        {
                            bundleIndex = b.BundleIndex,
                            startDay = b.StartDay,
                            source = b.IsPublic ? BundleSources.Public : BundleSources.Purchased,
                            dailyChangesBps = (int[])b.DailyChangesBps.Clone()
                        }).ToArray()
                }).ToArray()
            };
        }
    }
}
