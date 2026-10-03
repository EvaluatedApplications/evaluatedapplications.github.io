// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\SupportedCouncils.cs, copied 2026-10-03.
// CouncilAudit engine by the EA virtual-customer agent. Do not edit the source repo from here;
// future engine changes happen upstream and get re-vendored into this copy by the Showroom owner.

namespace CouncilAudit;

public enum SourceFormat { Csv, Xlsx }

/// <summary>One year's worth of a council's published file.</summary>
public sealed record CouncilYearFile(string Tag, string Url, SourceFormat Format, string? EncodingOverride = null);

/// <summary>
/// A council we have manually worked through: verified the transparency-page link,
/// mapped its columns, run the engine, and hand-checked a sample of the output
/// against the source file (opened vs confirmed - see OnboardingChecklist.md). The
/// Showroom renders one page per entry here, pre-filled, so a visitor never sees a
/// column-mapping step. Adding a council means adding one of these, not new code.
/// </summary>
public sealed record SupportedCouncil(
    string Name,
    string TransparencyPageUrl,
    string HowToFindTheFile,     // plain-language steps specific to this council's site
    ColumnMapping Mapping,
    TransactionIdScope IdScope,
    GrossMeaning GrossMeaning,
    IReadOnlyList<CouncilYearFile> Years,
    IReadOnlyList<string> KnownQuirks,
    string LastChecked,          // yyyy-MM-dd, when a human last verified the link + mapping
    string VerificationNote      // what was hand-checked, opened vs confirmed, and the result
);

public static class SupportedCouncils
{
    private const string TransparencyLandingPageOnly =
        "https://www.wokingham.gov.uk/foi/supplier-spend-data"; // dropdown-driven; see KnownQuirks

    public static readonly SupportedCouncil Wokingham = new(
        Name: "Wokingham Borough Council",
        TransparencyPageUrl: "https://www.wokingham.gov.uk/council-and-meetings/access-to-information/transparency-code",
        HowToFindTheFile:
            "On the Wokingham transparency page, open \"Spending over £500\". Each financial year " +
            "(e.g. \"2025-26\") has its own download, usually a CSV, one year is only available as an " +
            ".xlsx. Download the year(s) you want and drop each file onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "TransNo",
            Supplier: "Accounts Payable/Accounts Receivable ID",
            PayDate: "Pay Date",
            Description: "Description",
            ServiceArea: "Service Area",
            Net: "Payment Amount (Net)",
            Gross: "Invoice Amount (Gross)",
            VatAmount: null,
            VatType: "VAT Type"),
        IdScope: TransactionIdScope.CouncilWideUnique, // TransNo is the council's own ledger reference
        GrossMeaning: GrossMeaning.RepeatedInvoiceTotal, // confirmed: Invoice Amount (Gross) repeats identically across a TransNo's settlement lines
        // NOTE ON THESE URLs: the six per-year files used to build and verify this profile
        // came from an existing local copy (VirtualCustomer/inbox/wokingham/), not a fresh
        // download this session. The landing page below was re-fetched and re-confirmed
        // today, but it renders its file list dynamically (a dropdown filter), so the
        // per-year direct file URLs are NOT independently re-verified in this entry and
        // must not be presented to a visitor as live links until someone clicks through
        // the landing page and confirms each one. Treat Years[].Url as "last known",
        // not "live", until that check happens.
        Years: new List<CouncilYearFile>
        {
            new("FY2020-21", TransparencyLandingPageOnly, SourceFormat.Csv, "windows-1252"),
            new("FY2021-22", TransparencyLandingPageOnly, SourceFormat.Csv, "windows-1252"),
            new("FY2022-23", TransparencyLandingPageOnly, SourceFormat.Csv, "windows-1252"),
            new("FY2023-24", TransparencyLandingPageOnly, SourceFormat.Xlsx),
            new("FY2024-25", TransparencyLandingPageOnly, SourceFormat.Csv, "windows-1252"),
            new("FY2025-26", TransparencyLandingPageOnly, SourceFormat.Csv, "windows-1252"),
        },
        KnownQuirks: new List<string>
        {
            "CSV years are Windows-1252, not UTF-8; reading as UTF-8 silently zeroes every £ amount (no error).",
            "FY2023-24 is published as .xlsx only.",
            "Pay Date is blank on 8,348 of 32,152 rows in FY2021-22; blank dates are excluded from Schedule B grouping only.",
            "A separate file, \"Supplier spend over £500 - 2022.csv\" (calendar year), duplicates rows already in the " +
                "FY2021-22 and FY2022-23 files byte-for-byte - do not load it alongside the financial-year files or " +
                "every schedule double-counts.",
            "VAT Type codes (EXEM/NBUS/OSCP/ZERO/RRTE/STD) must be read per-row, not assumed uniform across a file.",
            "TransNo is not always one invoice to one payee: 433 transaction numbers in six years span more than one " +
                "supplier or pay date (Schedule D exists because of this).",
            "The council's \"Supplier Spend Data\" / \"Expenditure over £500\" pages list the file as a dropdown-driven " +
                "download (not a static link list); the per-year file URLs above are last-known, from an existing " +
                "local copy, and need a human click-through to re-confirm before being shown live to a visitor.",
        },
        LastChecked: "2026-10-02 (landing page); per-year file URLs not reconfirmed this session",
        VerificationNote:
            "Six financial years, 217,117 raw rows. Schedule A/B/D reproduced exactly against the hand-built FOI " +
            "schedules in foi/wokingham/auditor/. 7 cross-transaction candidate pairs opened by hand against the raw " +
            "file across 3 years: 7/7 confirmed to exist exactly as the schedule describes them. Full letter sent to " +
            "KPMG as Wokingham's external auditor: foi/wokingham/auditor/Letter_to_KPMG.md."
    );

    /// <summary>
    /// Council #2, worked through for this session to prove the engine generalises past
    /// Wokingham's own format. Merton publishes Gross + VAT Amount directly (no net
    /// column, no VAT-type code) and dates as "10-Apr-24" - both different from
    /// Wokingham, and both now provable, not assumed, because the mapping ran on real
    /// Merton files and the output was spot-checked below.
    /// </summary>
    public static readonly SupportedCouncil Merton = new(
        Name: "London Borough of Merton",
        TransparencyPageUrl: "https://www.merton.gov.uk/council-and-local-democracy/data-protection-and-freedom-of-information/open-data/spending-over-500",
        HowToFindTheFile:
            "On the Merton \"Spending over £500\" page, downloads are listed by calendar year (Merton publishes by " +
            "calendar year, not financial year), with the current year as a year-to-date file updated periodically. " +
            "Each is a plain CSV. Download the year(s) you want and drop each file onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Supplier Invoice No",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Description",
            ServiceArea: "Directorate",
            Net: null,
            Gross: "Gross Invoice Value",
            VatAmount: "Vat Amount",
            VatType: null),
        IdScope: TransactionIdScope.SupplierFurnished, // confirmed by hand: trans 30970 is two unrelated suppliers' own invoice numbers colliding
        GrossMeaning: GrossMeaning.PerLineAmount, // confirmed by hand: equal PRECEPT instalments and Canon multi-line invoices are each line's own amount, not one total repeated
        Years: new List<CouncilYearFile>
        {
            new("CY2023", "https://www.merton.gov.uk/sites/default/files/2025-06/2023.csv", SourceFormat.Csv),
            new("CY2024", "https://www.merton.gov.uk/sites/default/files/2025-06/2024.csv", SourceFormat.Csv),
            new("CY2025", "https://www.merton.gov.uk/sites/default/files/2026-04/2025YEARENDInvoice500.csv", SourceFormat.Csv),
        },
        KnownQuirks: new List<string>
        {
            "No net-amount column: net is derived as Gross - VAT Amount per row (the engine does this automatically " +
                "when ColumnMapping.Net is null and VatAmount is set).",
            "Dates are \"dd-MMM-yy\" (e.g. \"10-Apr-24\"), not dd/MM/yyyy.",
            "A leading blank/unnamed first column holds a row sequence number, not data; ignored by the mapping.",
            "Publishes by calendar year, not financial year, so a council-to-council comparison must line up periods, " +
                "not just filenames.",
            "Encoding: valid UTF-8/ASCII in the files checked, no Windows-1252 bytes found - do not assume this holds " +
                "for every year without checking (Wokingham's own encoding varied by year too).",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "CY2024 (44,843 rows) run through the engine end to end. Schedule A found 0 rows over 1p (every row's " +
            "Gross - VatAmount nets to the row itself - Merton does not publish a separate invoice-vs-payment amount " +
            "the way Wokingham does, so Schedule A is structurally near-empty for this council; this is a true " +
            "negative, not a bug - see PRODUCT_PLAN.md). Schedule B (repeated payments) and Schedule D (multi-payee " +
            "transaction numbers) both ran and produced candidate groups; 3 hand-opened against the raw CSV, 3/3 " +
            "confirmed to exist exactly as the schedule describes them (opened vs confirmed)."
    );

    public static readonly IReadOnlyList<SupportedCouncil> All = new[] { Wokingham, Merton };
}
