// [개발 전용] D 화면 확인용 B 대역과 실제 C의 오류 주입용 래퍼.
// - 시장: fixture(B_market_server.json)의 고정 주가·무료 공개 위치를 그대로 쓴다.
// - 주문 검사: D의 화면 사전검사(OrderInputValidator)를 재사용한다. 정식 검증은 B 담당.
// - 정산: 계산하지 않고 fixture 기대 전광판(D_scoreboard_expected.json)을 그대로 돌려준다.
// - 코인·구매·개인뷰: 실제 C 구현에 위임한다.
// B는 개발용 대역이며 C는 실제 SharedInformationService에 위임한다.
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

    // Development-only error injection around the real C service; no duplicate coin/purchase rules.
    public sealed class DevInformationService : IInformationService
    {
        private readonly IInformationService inner;
        public string ForceNextBuyError { get; set; }
        public DevInformationService(IMarketDataSource source)
            => inner = new BlackWhale.Rules.Information.SharedInformationService(source);

        public void GrantTurnCoins(RequestContext context, string[] playerIds)
            => inner.GrantTurnCoins(context, playerIds);

        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            if (!string.IsNullOrEmpty(ForceNextBuyError))
            {
                var forced = ForceNextBuyError;
                ForceNextBuyError = null;
                return new RuleResult(forced);
            }
            return inner.BuyInformation(context, companyId, bundleIndex);
        }

        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
            => inner.BuildPlayerView(context, ownState);
    }
}
