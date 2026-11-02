using System.Linq;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using NUnit.Framework;

namespace BlackWhale.Tests.Information
{
    public sealed class SharedInformationServiceTests
    {
        private sealed class Market : IMarketDataSource
        {
            public bool Missing, Malformed;
            public string[] GetCompanyIds() => new[] { "AI" };
            public ServerInformationBundle GetBundle(int turn, string companyId, int bundleIndex)
                => Missing && bundleIndex == 1 ? null : new ServerInformationBundle
                {
                    CompanyId = companyId, BundleIndex = bundleIndex, StartDay = bundleIndex * 5 + 1,
                    IsPublic = bundleIndex == 0 || bundleIndex == 2,
                    DailyChangesBps = Malformed && bundleIndex == 1 ? new[] { 1 } : new[] { 10, 20, 30, 40, 50 }
                };
        }

        private static RequestContext Context(string room = "room", int turn = 1, string player = "P1", string request = "buy")
            => new RequestContext(room, turn, Protocol.Info, player, request);
        private static OwnInvestmentState Own() => new OwnInvestmentState
        {
            TotalAssets = 12345, SelectedCompanies = new[] { "AI", "FOOD" },
            Investments = new[] { new Investment { companyId = "AI", weightBps = 6000,
                intervals = new[] { new Interval { entryDay = 3, exitDay = 8 } } } }
        };

        [Test]
        public void RegistersPlayers_GrantsOnce_AndScopesRoomAndTurn()
        {
            IInformationService service = new SharedInformationService(new Market());
            var ids = new[] { "P1", "P2" };
            service.GrantTurnCoins(Context(), ids);
            service.GrantTurnCoins(Context(), ids);
            Assert.That(service.BuildPlayerView(Context(), Own()).coins, Is.EqualTo(3));
            Assert.That(service.BuyInformation(Context(), "AI", 1).Success, Is.True);
            Assert.That(service.BuyInformation(Context(), "AI", 1).Payload.remainingCoins, Is.EqualTo(2));
            service.GrantTurnCoins(Context(turn: 2), ids);
            Assert.That(service.BuyInformation(Context(turn: 2), "AI", 1).Success, Is.True);
            Assert.That(service.BuildPlayerView(Context(turn: 2), Own()).coins, Is.EqualTo(4));
            service.GrantTurnCoins(Context(room: "other"), ids);
            var other = service.BuildPlayerView(Context(room: "other"), Own());
            Assert.That(other.coins, Is.EqualTo(3));
            Assert.That(other.companies[0].visibleBundles.Length, Is.EqualTo(2));
        }

        [Test]
        public void CommonViewPreservesOwnState_AndKeepsOtherPlayersPrivate()
        {
            IInformationService service = new SharedInformationService(new Market());
            service.GrantTurnCoins(Context(), new[] { "P1", "P2" });
            var result = service.BuyInformation(Context(), "AI", 1);
            Assert.That(result.Payload.startDay, Is.EqualTo(6));
            var own = Own();
            var view = service.BuildPlayerView(Context(), own);
            Assert.That(view.recipientPlayerId, Is.EqualTo("P1"));
            Assert.That(view.phase, Is.EqualTo(Protocol.Info));
            Assert.That(view.assets, Is.EqualTo(12345));
            CollectionAssert.AreEqual(new[] { "AI", "FOOD" }, view.selectedCompanies);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, view.companies[0].visibleBundles.Select(b => b.bundleIndex));
            Assert.That(view.ownInvestments[0].intervals[0].entryDay, Is.EqualTo(3));
            var p2 = service.BuildPlayerView(Context(player: "P2"), Own());
            Assert.That(p2.coins, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { 0, 2 }, p2.companies[0].visibleBundles.Select(b => b.bundleIndex));
            view.selectedCompanies[0] = "mutated";
            view.ownInvestments[0].intervals[0].entryDay = 99;
            view.companies[0].visibleBundles[1].dailyChangesBps[0] = 99;
            Assert.That(own.SelectedCompanies[0], Is.EqualTo("AI"));
            Assert.That(own.Investments[0].intervals[0].entryDay, Is.EqualTo(3));
            Assert.That(service.BuildPlayerView(Context(), own).companies[0].visibleBundles[1].dailyChangesBps[0], Is.EqualTo(10));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrMalformedMarketBundleDoesNotCharge(bool malformed)
        {
            var market = new Market { Missing = !malformed, Malformed = malformed };
            IInformationService service = new SharedInformationService(market);
            service.GrantTurnCoins(Context(), new[] { "P1" });
            var result = service.BuyInformation(Context(), "AI", 1);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("MARKET_UNAVAILABLE"));
            Assert.That(result.Payload, Is.Null);
            Assert.That(service.BuildPlayerView(Context(), Own()).coins, Is.EqualTo(3));
        }

        [Test]
        public void PastInformationAndCoinsSurviveNextTurn_AndPastPurchasesAreReadOnly()
        {
            var market = new BlackWhale.Rules.MarketService(t => new BlackWhale.Rules.MarketData
            {
                turn = t,
                companies = BlackWhale.Rules.InvestmentRules.CompanyIds.Select(id => new BlackWhale.Rules.CompanyMarketData
                {
                    companyId = id, publicBundleIndices = new[] { 0, 2 },
                    dailyChangesBps = Enumerable.Range(0, 31).Select(day => day * 100 * t).ToArray()
                }).ToArray()
            });
            IInformationService service = new SharedInformationService(market);
            var ids = new[] { "P1", "P2" };
            market.BeginTurn(Context(), ids);
            service.GrantTurnCoins(Context(), ids);
            service.BuyInformation(Context(), "AI", 1);
            market.BeginTurn(Context(turn: 2), ids);
            service.GrantTurnCoins(Context(turn: 2), ids);
            service.BuyInformation(Context(turn: 2), "AI", 1);
            var old = service.BuildPlayerView(Context(), market.GetOwnInvestmentState("P1", 1));
            Assert.AreEqual(2, old.coins);
            Assert.AreEqual(600, old.companies[0].visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps[0]);
            Assert.AreEqual(1200, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).companies[0].visibleBundles.Single(b => b.bundleIndex == 1).dailyChangesBps[0]);
            Assert.AreEqual(4, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).coins);
            Assert.AreEqual(ErrorCodes.StaleTurn, service.BuyInformation(Context(request: "new-old-buy"), "AI", 3).ErrorCode);
            Assert.IsTrue(service.BuyInformation(Context(), "AI", 1).Success, "기존 요청의 재전송은 원응답만 반환");
            service.GrantTurnCoins(Context(), ids); // delayed turn-start replay
            Assert.AreEqual(4, service.BuildPlayerView(Context(turn: 2), market.GetOwnInvestmentState("P1")).coins);
            Assert.AreEqual(2, service.BuildPlayerView(Context(player: "P2"), market.GetOwnInvestmentState("P2", 1)).companies[0].visibleBundles.Length);
        }
    }
}
