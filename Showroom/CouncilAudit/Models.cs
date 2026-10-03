// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\Models.cs, copied 2026-10-03.
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
    int RowIndexInSource       // 0-based position in the original file, for traceability
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
    string? VatType             // column holding a VAT rate/category code, if published instead of an amount
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

public sealed record ScheduleARow(
    string SourceTag, string TransactionId, string Supplier, string? PayDate,
    decimal Net, decimal Gross, decimal ExpectedGross, decimal Difference,
    string? VatType, string? ServiceArea, string? Description, int LineCount);

public sealed record ScheduleBGroup(
    int GroupId, string Supplier, decimal Net, decimal Gross, string? Description, string? PayDate,
    IReadOnlyList<SpendRow> Members);

public sealed record ScheduleDGroup(
    string SourceTag, string TransactionId, int DistinctSuppliers, int DistinctPayDates,
    IReadOnlyList<SpendRow> Members);

public sealed record AuditResult(
    int RawRowCount,
    int DistinctTransactionSupplierCount,
    IReadOnlyList<ScheduleARow> ScheduleA,
    IReadOnlyList<ScheduleBGroup> ScheduleB,
    IReadOnlyList<ScheduleDGroup> ScheduleD,
    TransactionIdScope ScheduleDScope,
    IReadOnlyList<string> Warnings);
