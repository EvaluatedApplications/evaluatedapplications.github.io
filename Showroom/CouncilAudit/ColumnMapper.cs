// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\ColumnMapper.cs, re-synced 2026-10-03 (Session 25/26 sync): verbatim upstream.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

/// <summary>
/// Guesses a <see cref="ColumnMapping"/> from a header row. Every English council
/// publishes the same kind of data under the Local Government Transparency Code, but
/// not the same column names (Wokingham: "TransNo", "Accounts Payable/Accounts
/// Receivable ID"; Merton: "Supplier Invoice No", "Supplier Name"). This is a best
/// guess for a human to confirm or correct in the Showroom UI before the engine runs -
/// never trust it silently on a council we have not manually onboarded (see
/// SupportedCouncils.cs).
/// </summary>
public static class ColumnMapper
{
    // Ordered by preference within each canonical field; first header match wins.
    private static readonly string[] TransactionIdAliases =
        { "TransNo", "Trans No", "Trans.No", "Transaction Number", "Transaction No",
          "Supplier Invoice No", "Invoice Number", "Invoice No", "Invoice Ref", "Reference" };

    private static readonly string[] SupplierAliases =
        { "Supplier Name", "Supplier", "Accounts Payable/Accounts Receivable ID",
          "Account Payable / Account Receivable ID", "Payee", "Vendor Name", "Vendor",
          "Body Name" };

    private static readonly string[] PayDateAliases =
        { "Pay Date", "Payment Date", "Date Paid", "Paid Date" };

    private static readonly string[] DescriptionAliases =
        { "Description", "Purpose of Expenditure", "Expenditure Purpose", "Narrative", "Details" };

    private static readonly string[] ServiceAreaAliases =
        { "Service Area", "Directorate", "Department", "Service", "Cost Centre Area", "Division" };

    private static readonly string[] NetAliases =
        { "Payment Amount (Net)", "Net Amount", "Amount (Net)", "Net" };

    private static readonly string[] GrossAliases =
        { "Invoice Amount (Gross)", "Gross Invoice Value", "Invoice Amount (Net)",
          "Gross Amount", "Amount", "Amount (£)", "AMOUNT (£)", "Value", "Total" };

    private static readonly string[] VatAmountAliases =
        { "Vat Amount", "VAT Amount", "VAT" };

    private static readonly string[] VatTypeAliases =
        { "VAT Type", "Vat Type", "VAT Rate" };

    public static ColumnMapping Guess(string[] header)
    {
        string? Find(string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var hit = header.FirstOrDefault(h =>
                    h.Trim().Equals(alias, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            // loose fallback: contains-match, only if nothing exact matched at all.
            foreach (var alias in aliases)
            {
                var hit = header.FirstOrDefault(h =>
                    h.Trim().Contains(alias, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            return null;
        }

        return new ColumnMapping(
            TransactionId: Find(TransactionIdAliases) ?? "",
            Supplier: Find(SupplierAliases) ?? "",
            PayDate: Find(PayDateAliases),
            Description: Find(DescriptionAliases),
            ServiceArea: Find(ServiceAreaAliases),
            Net: Find(NetAliases),
            Gross: Find(GrossAliases),
            VatAmount: Find(VatAmountAliases),
            VatType: Find(VatTypeAliases));
    }
}
