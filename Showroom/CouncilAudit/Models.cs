// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\Models.cs, copied 2026-10-03,
// re-synced 2026-10-03 (same day, DebtCharge fix sync: Session 19's MemberRowIndexes field on
// ScheduleARow/ScheduleBGroup, needed by HandCheckHelpers.cs added alongside this sync).
// CouncilAudit engine by the EA virtual-customer agent. Do not edit the source repo from here;
// future engine changes happen upstream and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// One payment line, after mapping a council's own column names onto this canonical
/// shape. Every field is "as published" - no VAT or rounding has been applied yet.
/// A council may not publish all of these; absent ones are null/empty and every rule
/// in <see cref="AuditEngine"/> has to cope with that (e.g. blank PayDate, no VatType).
/// </summary>
public sealed record SpendRow(
    string SourceTag,          // which file/year this line came from
    string TransactionId,      // the council's own reference for the invoice/transaction
    string Supplier,
    string? PayDateRaw,
    DateOnly? PayDate,
    string? Description,
    string? ServiceArea,
    decimal Net,               // payment amount excl. VAT, as published or derived (Gross-Vat)
    decimal Gross,             // invoice/payment amount incl. VAT, as published
    decimal? VatAmount,        // published VAT amount, if the council gives one directly
    string? VatType,           // published VAT rate/category code, if the council gives one instead
    int RowIndexInSource,      // 0-based position in the original file, for traceability
    string? SupplierInvoiceNumber = null, // the SUPPLIER's own invoice number, if the council
    // publishes one distinct from its own TransactionId (Reading's "Supplier Invoice No" vs.
    // its "Voucher Number"). Null when the council doesn't publish a separate field, or when
    // its TransactionId already IS the supplier's invoice number (Merton - do not duplicate
    // the same value into both fields). Used only to tell a genuine repeated-invoice duplicate
    // (same invoice number paid twice) apart from a standardised recurring rate (same amount,
    // different invoice numbers - e.g. a care placement or rent-scheme weekly/monthly rate) in
    // Schedule B - see ONBOARDING_CHECKLIST.md step 4b.
    string? CostCentreArea = null, // a finer-grained budget/cost-centre label than ServiceArea,
    // if the council publishes one as a separate column (Wokingham's "Cost Centre Area" vs.
    // its broader "Service Area"). Session 12 finding: this is where Wokingham actually
    // labels its early-payment-discount scheme ("Early payment programme") - NOT in
    // Description, which is a generic "Fees"/"Early payment programme" that is sometimes the
    // Cost Centre Area and sometimes the Description depending on which column a first
    // reading mistook for the other (caught by re-running the real engine against the real
    // header, not by trusting a manual column-position read - see AuditEngine's
    // EarlyPaymentProgramme rule). Null when the council doesn't publish a separate column.
    string? InvoiceType = null // Session 14 finding: Reading publishes an explicit
    // "Invoice Type (Internal Classification)" column with council-labelled values
    // (STANDARD / CREDIT in 2021-2023 files, "RBC Standard Invoice" / "RBC Refunds Manual
    // Entry" / "RBC AR REFUNDS" from 2024 onward) that directly names whether a row is a
    // refund/credit, rather than this having to be inferred from the sign of Amount alone
    // - confirmed by hand: CREDIT-type rows are always negative in the older files, but
    // the newer "RBC Refunds Manual Entry"/"RBC AR REFUNDS" rows are always POSITIVE (the
    // council's own sign convention for a refund changed between publishing eras). Null
    // when the council doesn't publish a separate invoice-type column.
);

/// <summary>
/// Maps one council's actual column headers onto the canonical fields above.
/// <see cref="ColumnMapper.Guess"/> proposes this from the header row; a human (the
/// Showroom visitor) can override any field before the engine runs, which is why this
/// is a plain mutable record rather than baked into the reader.
/// </summary>
public sealed record ColumnMapping(
    string TransactionId,
    string Supplier,
    string? PayDate,
    string? Description,
    string? ServiceArea,
    string? Net,               // column holding the net/excl-VAT amount, if published directly
    string? Gross,              // column holding the gross/incl-VAT amount
    string? VatAmount,          // column holding a VAT amount, if published instead of a net column
    string? VatType,            // column holding a VAT rate/category code, if published instead of an amount
    string? SupplierInvoiceNumber = null, // column holding the supplier's OWN invoice number,
    // only when it's a genuinely separate column from TransactionId (see SpendRow above)
    string? CostCentreArea = null, // column holding a finer-grained cost-centre label, separate
    // from ServiceArea (see SpendRow.CostCentreArea above)
    string? InvoiceType = null // column holding a council-labelled invoice-type code that
    // directly identifies credits/refunds (see SpendRow.InvoiceType above)
)
{
    /// <summary>True once enough fields are mapped to run the engine at all.</summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(TransactionId)
        && !string.IsNullOrWhiteSpace(Supplier)
        && !string.IsNullOrWhiteSpace(Gross);
}

/// <summary>
/// Whether the TransactionId column is a reference the council itself assigns and
/// keeps unique across every supplier (Wokingham's "TransNo"), or a reference the
/// supplier wrote on their own invoice and the council copied verbatim (Merton's
/// "Supplier Invoice No" - two unrelated suppliers can and do use the same number by
/// coincidence; confirmed on real Merton data, see SupportedCouncils.cs). Schedule D
/// ("one transaction number, more than one payee") is only a meaningful finding under
/// CouncilWideUnique; under SupplierFurnished it mostly reports coincidence and must
/// say so rather than imply a discrepancy.
/// </summary>
public enum TransactionIdScope { CouncilWideUnique, SupplierFurnished }

/// <summary>
/// What the Gross column means when a transaction+supplier group has more than one
/// line. This is NOT safe to infer from the data (equal instalments and a repeated
/// invoice total look identical when the amounts happen to match - confirmed on real
/// Merton PRECEPT payments: two equal quarterly instalments of the same gross value
/// look exactly like Wokingham's one-gross-repeated-per-line convention, but here net
/// correctly equals *twice* that gross, not once). It must be set per council, by a
/// human who checked the source documentation or a multi-line example by hand.
/// </summary>
public enum GrossMeaning
{
    /// <summary>Gross is one invoice total, repeated identically on every line of the
    /// group (Wokingham's "Invoice Amount (Gross)"). Expect sum(Net) ~= that one value.</summary>
    RepeatedInvoiceTotal,
    /// <summary>Gross is the amount for that one line; a group's stated total is the
    /// sum of every line's own Gross (Merton's "Gross Invoice Value").</summary>
    PerLineAmount
}

/// <summary>
/// What kind of amount mismatch a Schedule A row is, so a reader isn't handed one
/// undifferentiated pile of "doesn't reconcile." Separating these out is the whole
/// point of the rule: a debt-charge invoice reconciling exactly once interest is
/// accounted for is not the same finding as a line genuinely unaccounted for, and
/// burying one inside the other hides both.
/// </summary>
public enum ScheduleAClassification
{
    /// <summary>No specific pattern recognised; the published numbers simply don't
    /// reconcile under either a VAT uplift or a raw net/gross comparison. The schedule's
    /// actual target - ask the council for the invoice and the reason.</summary>
    Unreconciled,
    /// <summary>The group's member lines are identical (same net/gross/description,
    /// repeated), and their net sum is a whole-number multiple (>=2) of the one stated
    /// invoice gross - i.e. the same invoice line was published more than once, not
    /// itemised into genuinely different charges. Measured rule (Wokingham 2020-21:
    /// 1,990 transactions where the lines sum to exactly 2x the stated invoice).
    /// Itemisation (lines sum to the stated invoice ONCE) is not a Schedule A row at
    /// all - there's nothing to flag. Session 14 generalisation: also covers a group
    /// where EVERY distinct (Net,Gross,Description) line repeats the SAME number of
    /// times (k >= 2) and one copy of each distinct line sums to the stated invoice -
    /// i.e. a whole itemised (multi-line) invoice published k times, not just one
    /// line (confirmed real on Wokingham FY2020-21: 23 rows, 15 suppliers, all EXEM).</summary>
    DoubleListing,
    /// <summary>The stated amount decomposes exactly into an exact round principal plus
    /// a (non-round) interest/charge remainder - a debt/loan-interest-style invoice, not
    /// an unexplained discrepancy. Labelled, not dropped: a real analyst still might want
    /// to see these, just not mixed in with genuine exceptions.</summary>
    DebtCharge,
    /// <summary>Session 12 judgement-sample finding, confirmed on real Wokingham data
    /// (3,730 raw rows across suppliers including Oxygen Finance Ltd, a real UK council
    /// supply-chain-finance provider): every member line carries the council's own
    /// "Early payment programme" description, each a NEGATIVE per-invoice early-payment
    /// discount fee, under one repeated Gross figure that is the batch's stated total -
    /// not something derivable by summing these line amounts. A known, named scheme, not
    /// an arithmetic mismatch, but the engine still can't reconcile the total from what's
    /// published (the individual underlying invoice amounts aren't in this file), so it is
    /// labelled rather than silently explained away.</summary>
    EarlyPaymentProgramme,
    /// <summary>Session 12 judgement-sample finding, confirmed on real Wokingham data
    /// (Education Boutique Ltd trans 3991866, diff 0.02; Carrington West Ltd trans
    /// 3626256, diff 0.02): the VAT-type-uplifted expected gross is within a few pence of
    /// the stated gross - consistent with VAT being rounded per line then summed (or vice
    /// versa) rather than a genuine unexplained gap. Deliberately narrow (5p): Holt
    /// School's trans 3928781 in the same judgement sample was off by GBP 4.03 under the
    /// same RRTE rate and correctly stays Unreconciled - this is pence-level rounding
    /// noise, not a general tolerance widening.</summary>
    VatRoundingNoise,
    /// <summary>Session 13 judgement-sample finding (a dedicated census of the opposite-
    /// sign shape Session 9/10 first saw in small samples, widened per the Showroom
    /// owner's flag that ExpectedGross can be negative against a positive Gross):
    /// netSum is negative, grossStated is positive, and flipping ONLY Net's sign (same
    /// magnitude) reconciles Gross exactly under the VAT-type uplift or the raw
    /// comparison - confirmed real on Wokingham (2,383 of 3,479 Unreconciled rows
    /// going into this cycle, hundreds of distinct suppliers, every VAT type, growing
    /// year over year). Named and separated out so it isn't buried in the generic
    /// Unreconciled bucket, but NOT claimed to be "explained" - nothing here establishes
    /// why Net's sign is inverted, so it is still a real, repeating, unanswered
    /// question for the council, same honesty as the single-line version of this
    /// pattern Session 9 first insisted on.</summary>
    NegativeNetSignFlip,
}

public sealed record ScheduleARow(
    string SourceTag, string TransactionId, string Supplier, string? PayDate,
    decimal Net, decimal Gross, decimal ExpectedGross, decimal Difference,
    string? VatType, string? ServiceArea, string? Description, int LineCount,
    ScheduleAClassification Classification = ScheduleAClassification.Unreconciled,
    string? ClassificationDetail = null,
    // Session 19: the exact 0-based raw-file row positions (SpendRow.RowIndexInSource)
    // of every member line that made up this group's (SourceTag, TransactionId,
    // Supplier) aggregate, as AuditEngine.Run itself grouped them - not re-discovered by
    // a later text scan. Lets a hand-check look up precisely these rows by position
    // instead of re-scanning the whole raw table for anything matching the transaction
    // id/supplier as TEXT (the shape that produced the retired ConfirmedInRawDetailed's
    // repeated double-counting failure - see HandCheckHelpers.cs). Defaults to empty for
    // any caller built before this field existed; a hand-check against an empty list
    // reports that explicitly rather than silently passing.
    IReadOnlyList<int> MemberRowIndexes = null!)
{
    public IReadOnlyList<int> MemberRowIndexes { get; init; } = MemberRowIndexes ?? Array.Empty<int>();
}

/// <summary>
/// What a Schedule B repeated-payment group looks like once a genuine supplier invoice
/// number (not the council's own transaction id) is available to check: see
/// ONBOARDING_CHECKLIST.md step 4b, confirmed on real Reading data (Session 8).
/// </summary>
public enum ScheduleBClassification
{
    /// <summary>No supplier invoice number was available to check (most councils, most
    /// of the time) - the group is reported exactly as before, as published, no verdict.</summary>
    Unclear,
    /// <summary>Every member shares the same supplier invoice number as well as the
    /// same amount - the strongest signal of a genuine duplicate payment (the same
    /// invoice paid more than once).</summary>
    LikelyDuplicate,
    /// <summary>Members carry DIFFERENT supplier invoice numbers despite the identical
    /// amount - consistent with a standardised recurring rate (a care placement, a
    /// guaranteed-rent-scheme tenancy, a school transport route) rather than a repeat
    /// payment error.</summary>
    LikelyRecurring,
}

public sealed record ScheduleBGroup(
    int GroupId, string Supplier, decimal Net, decimal Gross, string? Description, string? PayDate,
    IReadOnlyList<SpendRow> Members,
    ScheduleBClassification Classification = ScheduleBClassification.Unclear,
    string? ClassificationDetail = null,
    // Session 19: parallel to Members - MemberRowIndexes[i] is the full list of exact
    // 0-based raw-file row positions underlying Members[i] (one per raw line of that
    // (SourceTag, TransactionId) transaction, which can be more than one - Members[i]
    // itself is a per-transaction AGGREGATE, its own RowIndexInSource is only the FIRST
    // of those lines). Lets a hand-check re-sum exactly the rows AuditEngine.Run itself
    // used to build this member, rather than re-discovering "which raw rows belong here"
    // by a text scan - see HandCheckHelpers.cs. Null for any caller built before this
    // field existed; CLI call sites fall back to a single-row list per member in that case.
    IReadOnlyList<IReadOnlyList<int>>? MemberRowIndexes = null);

public sealed record ScheduleDGroup(
    string SourceTag, string TransactionId, int DistinctSuppliers, int DistinctPayDates,
    IReadOnlyList<SpendRow> Members);

/// <summary>
/// Whether a credit/refund row (identified by the council's own published Invoice
/// Type label, not by the sign of Amount - see <see cref="SpendRow.InvoiceType"/>)
/// has a same-supplier charge of matching magnitude anywhere in the loaded data.
/// Session 14, built for Reading (the only onboarded council that publishes an
/// Invoice Type column). A match is NOT a claim the credit genuinely offsets that
/// specific charge - only that a plausible explanation exists in the data; same
/// honesty standard as every other classification in this engine.
/// </summary>
public enum CreditMatchClassification
{
    /// <summary>A same-supplier row with a non-credit Invoice Type and a matching
    /// (within 1p) absolute amount exists within the date window searched.</summary>
    MatchedOffsettingCharge,
    /// <summary>No same-supplier charge of matching magnitude was found in the
    /// loaded data - the real, unexplained target of this schedule. Not established
    /// to be an error (the matching charge could simply be in a file not loaded this
    /// run), reported as published.</summary>
    Unmatched,
}

public sealed record ScheduleRRow(
    string SourceTag, string TransactionId, string Supplier, decimal Amount,
    string? InvoiceType, string? PayDate, string? Description,
    CreditMatchClassification Classification, string? ClassificationDetail,
    int RowIndexInSource = -1 // Session 14 finding: Reading's own TransactionId
    // ("Voucher Number") is NOT unique per line for some suppliers - REED alone
    // repeats one voucher number across 333 separate raw lines in a single month
    // (confirmed by hand against reading_2023-03.csv: 887 of 4,595 distinct
    // vouchers in that one month appear more than once, same supplier, same pay
    // date every time, so Schedule D's own multi-payee/multi-date test correctly
    // never flags it - but (TransactionId, Supplier) is NOT a safe key to re-find
    // "this one row" in the raw file for a per-row check like Schedule R. Carries
    // the exact source row position instead, so a hand-check can look up the exact
    // line rather than aggregating every line that happens to share a voucher
    // number).
);

public sealed record AuditResult(
    int RawRowCount,
    int DistinctTransactionSupplierCount,
    IReadOnlyList<ScheduleARow> ScheduleA,
    IReadOnlyList<ScheduleBGroup> ScheduleB,
    IReadOnlyList<ScheduleDGroup> ScheduleD,
    TransactionIdScope ScheduleDScope,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ScheduleRRow> ScheduleR);
