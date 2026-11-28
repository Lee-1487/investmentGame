using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Rules.Information
{
    public sealed partial class SharedInformationService
    {
        private static bool ExchangeCard(string company, int start) => (company == "FOOD" || company == "AUTO") && start >= 6 && start <= 21 && (start - 1) % 5 == 0;
        private WindowRecord KnownCard(WindowTurn state, string player, string company, int start) => state.Records
            .FirstOrDefault(r => r.Company == company && r.Start == start && r.Length == 5 && Accessible(r, player));
        private WindowRecord SendableCard(WindowTurn state, string player, string company, int start) => state.Records
            .FirstOrDefault(r => r.Player == player && r.Company == company && r.Start == start && r.Length == 5
                && r.Source == "DEALT");
        public bool HasPendingInformationExchange(RequestContext c)
        {
            lock (gate) return Windows(c).Exchanges.Any(e => e.status == "PENDING" && (e.senderId == c.PlayerId || e.targetPlayerId == c.PlayerId));
        }
        public RuleResult RequestInformationExchange(RequestContext c, string target, string offeredCompany, int offeredStart, string requestedCompany, int requestedStart)
        {
            lock (gate) return WindowRequest(c, "exchange|" + target + "|" + offeredCompany + offeredStart + "|" + requestedCompany + requestedStart, () => {
                string error = ValidateWindow(c, offeredCompany, offeredStart);
                if (error != "") return new RuleResult(error);
                if (!RandomInformation || target == c.PlayerId || !rooms[c.RoomId].Players.Contains(target)) return new RuleResult("INVALID_TARGET");
                if (!ExchangeCard(offeredCompany, offeredStart) || !ExchangeCard(requestedCompany, requestedStart)) return new RuleResult("INVALID_WINDOW");
                if (offeredCompany != requestedCompany) return new RuleResult("EXCHANGE_SAME_COMPANY");
                var state = Windows(c);
                if (SendableCard(state, c.PlayerId, offeredCompany, offeredStart) == null) return new RuleResult("INFORMATION_NOT_KNOWN");
                if (KnownCard(state, c.PlayerId, requestedCompany, requestedStart) != null)
                    return new RuleResult("EXCHANGE_NOT_USEFUL");
                // Pending requests carry card identities only. Never reveal whether the target privately owns the requested card.
                if (state.Exchanges.Any(e => e.status == "PENDING" && e.senderId == c.PlayerId && e.targetPlayerId == target)) return new RuleResult("EXCHANGE_ALREADY_PENDING");
                var request = new InformationExchange { id = c.RoomId + ":" + c.Turn + ":exchange:" + (state.Exchanges.Count + 1),
                    senderId = c.PlayerId, targetPlayerId = target, offeredCompanyId = offeredCompany, offeredStartDay = offeredStart,
                    requestedCompanyId = requestedCompany, requestedStartDay = requestedStart, status = "PENDING" };
                state.Exchanges.Add(request);
                return new RuleResult(payload: new ResultPayload { exchangeId = request.id });
            });
        }
        public RuleResult RespondInformationExchange(RequestContext c, string exchangeId, bool accept)
        {
            lock (gate) return WindowRequest(c, "exchange-response|" + exchangeId + "|" + accept, () => {
                string error = ValidateWindow(c, "FOOD", 1);
                if (error != "") return new RuleResult(error);
                var state = Windows(c);
                var request = state.Exchanges.FirstOrDefault(e => e.id == exchangeId && e.targetPlayerId == c.PlayerId);
                if (request == null) return new RuleResult("EXCHANGE_NOT_FOUND");
                if (request.status != "PENDING") return new RuleResult("EXCHANGE_ALREADY_RESOLVED");
                if (!accept) { request.status = "REJECTED"; return new RuleResult(payload: new ResultPayload { exchangeId = request.id }); }
                var offered = SendableCard(state, request.senderId, request.offeredCompanyId, request.offeredStartDay);
                var wanted = SendableCard(state, request.targetPlayerId, request.requestedCompanyId, request.requestedStartDay);
                if (offered == null || wanted == null) return new RuleResult("INFORMATION_NOT_KNOWN");
                // Revalidate both sides before writing either one; concurrent offers may have made a card redundant.
                if (KnownCard(state, request.targetPlayerId, request.offeredCompanyId, request.offeredStartDay) != null
                    || KnownCard(state, request.senderId, request.requestedCompanyId, request.requestedStartDay) != null) {
                    request.status = "CANCELLED";
                    return new RuleResult(payload: new ResultPayload { exchangeId = request.id });
                }
                void Transfer(WindowRecord original, string recipient) {
                    var copy = AddWindow(new RequestContext(c.RoomId, c.Turn, c.Phase, recipient), original.Company, original.Start, "EXCHANGED", 0);
                    copy.Closes = (int[])original.Closes.Clone(); copy.Opens = (int[])original.Opens.Clone();
                }
                Transfer(offered, request.targetPlayerId); Transfer(wanted, request.senderId);
                request.status = "ACCEPTED";
                return new RuleResult(payload: new ResultPayload { exchangeId = request.id });
            });
        }
    }
}
