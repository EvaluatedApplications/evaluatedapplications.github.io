namespace CouncilAudit;

/// <summary>
/// Session 22 (owner's own direction, relayed by the coordinator): "Cross-council
/// references should track debt between them and look for debt sinks that look weird,
/// or weird payments." This is a cross-council READING of the same published data
/// AuditEngine already groups and classifies - not a new source, not a new column.
/// Every entry here is one (SourceTag, TransactionId, Supplier) group whose own
/// Description/CostCentreArea text names it as debt/interest/a loan, with whatever
/// principal/interest decomposition <see cref="AuditEngine.TryDecomposeDebtCharge"/>
/// can find in the group's own stated amount - the SAME decomposition Schedule A's
/// DebtCharge rule uses, just applied here as a standalone ledger rather than folded
/// into one council's exception list, so it can be compared ACROSS councils (which
/// Schedule A, being per-council, cannot do on its own).
/// </summary>
public sealed record DebtLedgerEntry(
    string Council, string SourceTag, string TransactionId, string Counterparty,
    string? PayDateRaw, decimal Amount, bool Decomposed, decimal Principal, decimal Interest,
    decimal? ImpliedRatePercent, string Evidence,
    // Session 24: the interest figure (sum of Net) and the loan-interest decoding as ledger columns.
    decimal Net = 0m,
    CounterpartyClass Class = CounterpartyClass.Other,
    LoanDecode? Decode = null);

public static class DebtLedger
{
    /// <summary>
    /// Narrow keyword match, same four terms and same reasoning as
    /// <see cref="AuditEngine"/>'s own HasDebtChargeEvidence (DEBT/INTEREST/LOAN), plus
    /// PWLB (the Public Works Loan Board - the standard UK local-government lender name,
    /// which would otherwise slip past the other three if a council abbreviates to just
    /// the lender's name with no generic word attached).
    /// </summary>
    /// <summary>Session 23: Description + CostCentreArea + the VALUES of every other published column
    /// (SpendRow.OtherColumns), so a label in a column the mapping does not name (Bracknell's
    /// "Service Division"/"Responsible Unit", Reading's "Cost Centre", Merton's "Purpose of
    /// Expenditure") can no longer hide a debt line. Values only, so a header name never matches.</summary>
    public static string EvidenceText(IReadOnlyList<SpendRow> members)
    {
        var parts = new List<string>();
        foreach (var m in members)
        {
            if (!string.IsNullOrWhiteSpace(m.Description)) parts.Add(m.Description!);
            if (!string.IsNullOrWhiteSpace(m.CostCentreArea)) parts.Add(m.CostCentreArea!);
            // ServiceArea is deliberately NOT scanned: tried in Session 23, Bracknell's service heading
            // "Interest & Investment Income" (a Treasury service line) then pulled in 45 consultants,
            // licences and banking-charge lines that are not borrowing. Heading text is not evidence.
            if (!string.IsNullOrEmpty(m.OtherColumns))
                foreach (var kv in m.OtherColumns!.Split('|'))
                {
                    int eq = kv.IndexOf('=');
                    parts.Add(eq >= 0 ? kv[(eq + 1)..] : kv);
                }
        }
        return string.Join(" ", parts.Distinct());
    }

    public static bool HasDebtEvidence(IReadOnlyList<SpendRow> members)
    {
        string text = EvidenceText(members).ToUpperInvariant();
        // Session 22: the first run (723 groups) was swamped by phrases that contain a keyword but are
        // not the council's own borrowing: "Debtors" (money owed TO the council), "Deferred Debt" (adult
        // social care deferred payments owed by residents), "Stock Loaned" (library stock), "Student
        // Loan Repayments" (HMRC payroll deductions). Strip those first, then apply the keyword test.
        // Session 23: with every published column now scanned (OtherColumns), four more labels surfaced that are
        // headings or debts owed TO the council, not borrowing: Merton "Prov. for Bad & Doubtful Debts" and "HB
        // written off debt recovered" (provisions / benefit overpayments), Reading "Debt Management Costs" (cost
        // centre for debt COLLECTION), Bracknell's service heading "Interest & Investment Income" (which also
        // appears as its Service Division and swept in 45 consultants/licences/banking lines).
        foreach (var noise in new[] { "DEBTORS", "DEFERRED DEBT", "STOCK LOANED", "STUDENT LOAN",
            "BAD & DOUBTFUL DEBTS", "WRITTEN OFF DEBT", "DEBT MANAGEMENT COSTS", "INTEREST & INVESTMENT INCOME",
            // RBWM's ledger heading "External Interest Payable" sits over payment-software, card-processing and
            // brokerage lines (ClearAccept 87, Adelante 33, ...): a cost-centre label, not a borrowing payment.
            "EXTERNAL INTEREST PAYABLE",
            // Session 31: Sheffield publishes the supplier category "DEBT COLLECTION AGENCIES" on every line paid to a
            // collections firm (UKSearch, Newlyn, Capita, Rossendales, Excel Civil Enforcement: 750+ ledger hits). That is money
            // the council is trying to COLLECT, not borrowing; same reasoning as "Debt Management Costs" above.
            "DEBT COLLECTION",
            // Also Sheffield cost-centre / object labels that contain a keyword and are not borrowing: "JUDGEMENT DEBTS AND FINES"
            // (the object code for legal judgements and fines, 84 lines), "CYCLEBOOST LOANS TRAINING" (a cycling-training scheme),
            // "HOMES & LOANS TEAM" (a team name), "TRANSFERS TO BAD DEBT PROV." (a provision).
            "JUDGEMENT DEBTS", "CYCLEBOOST LOANS", "HOMES & LOANS TEAM", "BAD DEBT PROV",
            // Liverpool: the expense type "Debt Management - Bailiffs, Tracing" / "Debt Mang-Bailif,etc" is paid to bailiffs and
            // tracing agents (Bristow & Sutor, Newlyn, CDER): collection of money owed TO the council.
            "DEBT MANAGEMENT - BAILIFF", "DEBT MANG-BAILIF",
            // Session 38, from the nine councils added then (each a label that contains a keyword and is not the council's borrowing): Cornwall's community equipment "Loan Store" and
            // "Equip Loan Serv" cost centres (14,780 lines of walking frames and hoists), Newcastle's "Ind Sector Client Loan Fund" ledger heading, Wirral's cost-centre label
            // "Debt Managment" (sic, windows and roofing contractors), "Debt Recovery" and "Debt Advice" (collection of money owed TO the council, advice services), Kirklees' "Best Interest
            // Assessor" (a social-work role) and Durham's "Car loans / Long Term Debtors" scheme (money owed to the council).
            "LOAN STORE", "LOAN SERV", "CLIENT LOAN", "DEBT MANAGMENT", "DEBT RECOVERY", "DEBT ADVICE", "BEST INTEREST", "LONG TERM DEBTOR", "CAR LOAN",
            // Session 48, Camden: the Purpose label "Other Debtor Entities and Individuals" (sundry debtors: Cyclescheme, Capita Travel and Events, 259 lines) is money owed
            // TO the council, not borrowing; the singular was not covered by "DEBTORS".
            "DEBTOR" })
            text = text.Replace(noise, " ");
        return text.Contains("DEBT") || text.Contains("INTEREST") || text.Contains("LOAN") || text.Contains("PWLB");
    }

    /// <summary>Scans one council's already-mapped rows for debt/interest/loan-shaped
    /// transactions and builds one ledger entry per (SourceTag, TransactionId, Supplier)
    /// group that carries the evidence. Does NOT filter to Schedule A's "doesn't
    /// reconcile" condition - every debt-shaped group is ledgered, reconciling or not,
    /// because the point here is the debt relationship itself, not an amount mismatch.</summary>
    public static List<DebtLedgerEntry> Scan(string council, IReadOnlyList<SpendRow> rows, GrossMeaning grossMeaning)
    {
        var result = new List<DebtLedgerEntry>();
        foreach (var g in rows.GroupBy(r => (r.SourceTag, r.TransactionId, r.Supplier)))
        {
            var members = g.ToList();
            if (!HasDebtEvidence(members)) continue;
            var first = members[0];
            decimal grossStated = grossMeaning == GrossMeaning.RepeatedInvoiceTotal
                ? first.Gross
                : members.Sum(r => r.Gross);
            decimal net = members.Sum(r => r.Net);
            string text = EvidenceText(members).Trim();
            bool decomposed = AuditEngine.TryDecomposeDebtCharge(Math.Abs(grossStated), out var principal, out var interest);
            decimal? rate = decomposed && principal != 0m ? Math.Round(interest / principal * 100m, 4) : null;
            result.Add(new DebtLedgerEntry(
                council, first.SourceTag, first.TransactionId, first.Supplier, first.PayDateRaw,
                grossStated, decomposed, decomposed ? principal : 0m, decomposed ? interest : 0m,
                rate, text,
                net, LoanDecoder.Classify(council, first.Supplier),
                LoanDecoder.ForEntry(council, first.Supplier, grossStated, net)));
        }
        return result;
    }

    /// <summary>
    /// Flag 1 - rate outliers: among entries where a principal/interest split WAS found,
    /// the implied rate (interest / principal, as a ONE-OFF charge, not annualised - the
    /// published data never states a term, so "rate" here means "charge as a % of
    /// principal", not a comparable APR) is outside a wide, deliberately generous sanity
    /// band. UK Public Works Loan Board certainty-rate loans were priced roughly 1.8%-5.5%
    /// across 2020-2026 (lowest in 2020-21 near-zero-rate era, highest after the 2022-23
    /// rate rises) - a one-off charge under 0.1% of principal or over 15% of principal is
    /// far outside anything a normal annual rate times a normal loan term could produce,
    /// so it is flagged as worth a human look, NOT claimed to be wrong (a short-term
    /// bridging charge or a part-year accrual could legitimately sit outside a full-year
    /// rate band).
    /// </summary>
    public static List<DebtLedgerEntry> FlagRateOutliers(IReadOnlyList<DebtLedgerEntry> ledger) =>
        ledger.Where(e => e.Decomposed && e.ImpliedRatePercent.HasValue
            && (e.ImpliedRatePercent.Value < 0.1m || e.ImpliedRatePercent.Value > 15m))
            .ToList();

    /// <summary>
    /// Flag 2 - same counterparty on both sides: a counterparty name (normalised) that
    /// appears as a debt-shaped PAYEE for one council in the ledger, when that same name
    /// is ALSO one of the councils whose own ledger this scan covers. This is the only
    /// "lender vs borrower" check the data supports - the published files are
    /// expenditure-only, so we can never see a counterparty's own books, only whether IT
    /// is itself one of our onboarded councils acting the other way round.
    /// </summary>
    public static List<(string counterparty, string asPayeeOfCouncil, string sourceTag, string transactionId)> FlagCrossCouncilCounterparties(
        IReadOnlyList<DebtLedgerEntry> ledger, IReadOnlyList<string> onboardedCouncilNames)
    {
        var normalisedCouncils = onboardedCouncilNames.Select(SupplierKey.Normalize).ToHashSet();
        return ledger
            .Where(e => normalisedCouncils.Contains(SupplierKey.Normalize(e.Counterparty)))
            .Select(e => (SupplierKey.Normalize(e.Counterparty), e.Council, e.SourceTag, e.TransactionId))
            .Distinct()
            .ToList();
    }
}
