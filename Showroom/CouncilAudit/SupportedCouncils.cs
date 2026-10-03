// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\SupportedCouncils.cs, re-synced 2026-10-03 (Session 25/26 sync): verbatim upstream, all six councils (Wokingham, Merton, Reading, Bracknell Forest, West Berkshire, RBWM).
// CouncilAudit engine by the EA virtual-customer agent. Do not edit here; future engine changes happen upstream
// and get re-vendored into this copy by the Showroom owner.

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
    string VerificationNote,     // what was hand-checked, opened vs confirmed, and the result
    string? FoiContactEmail = null  // verified FOI/transparency contact, if found - for the Showroom page to show
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
            VatType: "VAT Type",
            CostCentreArea: "Cost Centre Area"), // confirmed Session 12: a finer label than Service
            // Area - this is where "Early payment programme" (Oxygen Finance Ltd's supply-chain
            // finance scheme) actually appears, not in Description, which is a generic "Fees".
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
            "KPMG as Wokingham's external auditor: foi/wokingham/auditor/Letter_to_KPMG.md. XlsxReader namespace bug " +
            "(caught by the Showroom owner against this council's real FY2023-24 .xlsx) fixed 2026-10-03 and " +
            "re-verified against the same real file (37,559 rows, correct header/dates) - see FEEDBACK.md.",
        FoiContactEmail: "informationrequests@wokingham.gov.uk" // verified by Showroom owner, council page https://www.wokingham.gov.uk/foi/receiving-foi-requests-email, checked 2026-10-03
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

    /// <summary>
    /// Council #3, onboarded 2026-10-03 per the owner's direction to target Wokingham's
    /// neighbours next. A third, genuinely different publishing shape: ONE signed "Amount"
    /// column (no separate net/gross/VAT at all - negative values are real corrections/
    /// credits, not errors), monthly (not annual) files with inconsistent, sometimes
    /// typo'd filenames, and column headers/org-name values that drift year to year
    /// (confirmed by hand: "RBC Legal Entity" -> "Reading Borough Council", "Cost Centre No"
    /// -> "Cost Centre", "Supplier name" -> "Supplier Name" casing). Schedule A cannot run
    /// for this council at all - there is no published invoice amount distinct from the
    /// payment amount to reconcile against, structurally, not as a bug.
    /// </summary>
    public static readonly SupportedCouncil Reading = new(
        Name: "Reading Borough Council",
        TransparencyPageUrl: "https://www.reading.gov.uk/the-council-and-democracy/finance-and-legal-information/council-spending-over-500/",
        HowToFindTheFile:
            "On Reading's \"Council spending over £500\" page, downloads are listed by MONTH, not year, going back " +
            "to 2017. Pick the month(s) you want (a whole financial year is 12 separate files) and drop each one " +
            "onto this page. A handful of early files (2021 and earlier) are .xlsx instead of .csv.",
        Mapping: new ColumnMapping(
            TransactionId: "Voucher Number (Internal Classification)",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Purpose",
            ServiceArea: "Directorate",
            Net: null,
            Gross: "Amount (£)",
            VatAmount: null,
            VatType: null,
            // Session 10 finding, corrected twice in the same session - record both halves honestly:
            // (1) BUG: no Reading file (checked all 5 real months) has a column literally named
            // "Supplier Invoice No" at all - the mapping below silently returned -1 for every row,
            // so the Session 9 LikelyDuplicate/LikelyRecurring classification never fired for this
            // council (every group stayed Unclear, with no warning). (2) The TEMPTING FIX IS WRONG:
            // "Payment Number" (what Session 8's write-up loosely called an "invoice number") is
            // actually a BACS payment-RUN identifier that bundles MANY DIFFERENT vouchers/invoices
            // for the same supplier into one payment - confirmed by hand against the raw 2026-02
            // file: Payment Number 108150 covers 25 DIFFERENT Voucher Numbers for MMCG (2) LTD, two
            // different Cost Centres (R5773/R5790) and Service Areas (PS Acquired Nursing/Social
            // Support), all at the same standardised £5,200 rate, same pay date - a bundled
            // recurring-rate run across 25 different care placements, not one invoice paid 25
            // times. Mapping SupplierInvoiceNumber to "Payment Number" (tried and reverted this
            // session) would have mislabelled exactly this shape LikelyDuplicate - the opposite of
            // checklist 4b's intent, and a worse failure than leaving it Unclear. CONCLUSION: Reading
            // structurally has no field that safely plays the "Supplier Invoice No" role (Voucher
            // Number is one line, Payment Number is many lines, no field sits at "one invoice").
            // Per the onboarding checklist's own cautious default, leave this unmapped (null) -
            // every Reading Schedule B group stays Unclear, as published, no verdict either way.
            SupplierInvoiceNumber: null,
            // Session 14 finding: a genuinely separate column, "Invoice Type (Internal
            // Classification)", directly labels CREDIT/refund rows ("CREDIT" in
            // 2021-2023 files, "RBC Refunds Manual Entry"/"RBC AR REFUNDS" from 2024) -
            // the council's own classification, not inferred from the sign of Amount
            // (which flips meaning between those two eras - see AuditEngine.ScheduleR).
            InvoiceType: "Invoice Type (Internal Classification)"),
        IdScope: TransactionIdScope.CouncilWideUnique, // checked by hand: of 4,595 distinct vouchers in one month, exactly 1 collision and it was the blank/unpublished voucher case, already dropped by the engine
        // Session 14 ADDENDUM to the note above, found building Schedule R: that earlier
        // check is about SCHEDULE D's definition of a collision (multi-payee or
        // multi-pay-date under one voucher) and is still correct for that purpose - but
        // it does NOT mean Voucher Number is one-line-per-invoice. Confirmed by hand on
        // the same reading_2023-03.csv: 887 of those same 4,595 distinct vouchers appear
        // MORE than once (up to 333 times for one REED voucher alone), always the SAME
        // supplier and SAME pay date every time - bulk agency-staff billing (REED, a
        // staffing agency) publishing many individual timesheet charges under one shared
        // voucher/payment reference. Schedule D correctly stays silent on these (same
        // supplier, same date - not its definition of a collision); any per-ROW check
        // (like Schedule R) must key off the exact source row position, never
        // (TransactionId, Supplier), for this council.
        GrossMeaning: GrossMeaning.PerLineAmount, // each voucher is one line; no repeated-total convention exists here at all
        Years: new List<CouncilYearFile>
        {
            // Session 17: full FY2020-21..FY2025-26 back-catalogue (64 files), replacing the
            // 5-month representative sample used through Session 16. Every URL below was
            // live-fetched from the council's own "Council spending over £500" page on
            // 2026-10-03 and downloaded successfully (64/64, logged in FEEDBACK.md). Reading
            // publishes FY2020-21 as 4 quarterly files (not monthly like every later year).
            new("2020-Q1", "https://images.reading.gov.uk/2020/11/Q1-2020-21.xlsx", SourceFormat.Xlsx),
            new("2020-Q2", "https://images.reading.gov.uk/2020/11/Q2-2020-21.xlsx", SourceFormat.Xlsx),
            new("2020-Q3", "https://images.reading.gov.uk/2021/03/Q3-2020-21-Revised.csv", SourceFormat.Csv),
            new("2021-Q4", "https://images.reading.gov.uk/2021/05/Q4-2020-21-Revised.xlsx", SourceFormat.Xlsx),

            new("2021-04", "https://images.reading.gov.uk/2021/06/April21.xlsx", SourceFormat.Xlsx),
            new("2021-05", "https://images.reading.gov.uk/2021/06/May21-Revised.xlsx", SourceFormat.Xlsx),
            new("2021-06", "https://images.reading.gov.uk/2021/07/Over-£500-June21.xlsx", SourceFormat.Xlsx),
            new("2021-07", "https://images.reading.gov.uk/2021/08/July-21.csv", SourceFormat.Csv),
            new("2021-08", "https://images.reading.gov.uk/2021/10/August21.csv", SourceFormat.Csv),
            new("2021-09", "https://images.reading.gov.uk/2021/10/Over-£500-September21.xlsx", SourceFormat.Xlsx),
            new("2021-10", "https://images.reading.gov.uk/2021/11/Over-500-Spend-October-21.csv", SourceFormat.Csv),
            new("2021-11", "https://images.reading.gov.uk/2022/01/Over-500-Spend-November-21.csv", SourceFormat.Csv),
            new("2021-12", "https://images.reading.gov.uk/2022/01/Over-599-Spend-December-21.csv", SourceFormat.Csv),
            new("2022-01", "https://images.reading.gov.uk/2022/02/Over-500-Spend-January-22.csv", SourceFormat.Csv),
            new("2022-02", "https://images.reading.gov.uk/2022/03/Over-500-Spend-February-22.csv", SourceFormat.Csv),
            new("2022-03", "https://images.reading.gov.uk/2022/04/March-22.csv", SourceFormat.Csv),

            new("2022-04", "https://images.reading.gov.uk/2022/05/Over-500-April-2022.csv", SourceFormat.Csv),
            new("2022-05", "https://images.reading.gov.uk/2022/06/Over-500-May-2022.csv", SourceFormat.Csv),
            new("2022-06", "https://images.reading.gov.uk/2022/07/Over-500-June-22.csv", SourceFormat.Csv),
            new("2022-07", "https://images.reading.gov.uk/2022/08/Over-500-Spend-July-2022.csv", SourceFormat.Csv),
            new("2022-08", "https://images.reading.gov.uk/2022/09/Over-500-Spend-August-2022.csv", SourceFormat.Csv),
            new("2022-09", "https://images.reading.gov.uk/2022/10/Over-500-Spend-September-2022.csv", SourceFormat.Csv),
            new("2022-10", "https://images.reading.gov.uk/2022/11/Over-500-Spend-October-2022.csv", SourceFormat.Csv),
            new("2022-11", "https://images.reading.gov.uk/2022/12/Over-500-November-22.csv", SourceFormat.Csv),
            new("2022-12", "https://images.reading.gov.uk/2023/01/Over-500-Spend-December-2022.csv", SourceFormat.Csv),
            new("2023-01", "https://images.reading.gov.uk/2023/02/Over-500-Spend-January-2023.csv", SourceFormat.Csv),
            new("2023-02", "https://images.reading.gov.uk/2023/03/Over-500-Spend-February-2023.csv", SourceFormat.Csv),
            new("2023-03", "https://images.reading.gov.uk/2023/04/Over-500-Spend-March-2023.csv", SourceFormat.Csv),

            new("2023-04", "https://images.reading.gov.uk/2023/05/Over-500-April-2023.csv", SourceFormat.Csv),
            new("2023-05", "https://images.reading.gov.uk/2023/06/Over-500-Spend-May-2023.csv", SourceFormat.Csv),
            new("2023-06", "https://images.reading.gov.uk/2023/07/Over-500-Spend-June-2023.csv", SourceFormat.Csv),
            new("2023-07", "https://images.reading.gov.uk/2023/08/Over-500-Spend-July-2023.csv", SourceFormat.Csv),
            new("2023-08", "https://images.reading.gov.uk/2025/09/Over-500-Spend-August-23.csv", SourceFormat.Csv),
            new("2023-09", "https://images.reading.gov.uk/2025/09/Over-500-September-2023.csv", SourceFormat.Csv),
            new("2023-10", "https://images.reading.gov.uk/2025/09/Over-500-Spend-October-2023.csv", SourceFormat.Csv),
            new("2023-11", "https://images.reading.gov.uk/2025/09/Over-500-Spend-November-2023.csv", SourceFormat.Csv),
            new("2023-12", "https://images.reading.gov.uk/2025/09/Over-500-December-23.csv", SourceFormat.Csv),
            new("2024-01", "https://images.reading.gov.uk/2025/09/Over-500-Spend-January24.csv", SourceFormat.Csv),
            new("2024-02", "https://images.reading.gov.uk/2025/09/Over-500-February-24B.csv", SourceFormat.Csv),
            new("2024-03", "https://images.reading.gov.uk/2025/09/Over-500-March-24B.csv", SourceFormat.Csv),

            new("2024-04", "https://images.reading.gov.uk/2025/09/over-500-Spend-April-2024.csv", SourceFormat.Csv),
            new("2024-05", "https://images.reading.gov.uk/2025/09/Over-500-Spend-May-2024.csv", SourceFormat.Csv),
            new("2024-06", "https://images.reading.gov.uk/2025/09/Over-500-Spend-June-2024.csv", SourceFormat.Csv),
            new("2024-07", "https://images.reading.gov.uk/2024/08/Over-500-Spend-July-2024.csv", SourceFormat.Csv),
            new("2024-08", "https://images.reading.gov.uk/2024/09/Over-500-August-2024.csv", SourceFormat.Csv),
            new("2024-09", "https://images.reading.gov.uk/2024/10/Over-500-Spend-September-2024.csv", SourceFormat.Csv),
            new("2024-10", "https://images.reading.gov.uk/2025/09/Over-500-Spend-October-2024.csv", SourceFormat.Csv),
            new("2024-11", "https://images.reading.gov.uk/2025/09/Over-500-Novermber-2024.csv", SourceFormat.Csv),
            new("2024-12", "https://images.reading.gov.uk/2025/09/Over-500-Spend-December-2024.csv", SourceFormat.Csv),
            new("2025-01", "https://images.reading.gov.uk/2025/09/Over-500-Spend-January-2025-1.csv", SourceFormat.Csv),
            new("2025-02", "https://images.reading.gov.uk/2025/09/Over-500-February-2025.csv", SourceFormat.Csv),
            new("2025-03", "https://images.reading.gov.uk/2025/09/Over-500-Spend-March-2025-1.csv", SourceFormat.Csv),

            new("2025-04", "https://images.reading.gov.uk/2025/09/Over-500-Spend-April-2025.csv", SourceFormat.Csv),
            new("2025-05", "https://images.reading.gov.uk/2025/09/Over-500-Spend-May-2025.csv", SourceFormat.Csv),
            new("2025-06", "https://images.reading.gov.uk/2025/09/Over-500-Spend-June-2025.csv", SourceFormat.Csv),
            new("2025-07", "https://images.reading.gov.uk/2025/09/Over-500-Spend-July-2025.csv", SourceFormat.Csv),
            new("2025-08", "https://images.reading.gov.uk/2025/09/Over-500-Spend-August-2025.csv", SourceFormat.Csv),
            new("2025-09", "https://images.reading.gov.uk/2025/10/Over-500-Spend-September-2025.csv", SourceFormat.Csv),
            new("2025-10", "https://images.reading.gov.uk/2025/11/Over-500-Spend-October-2025.csv", SourceFormat.Csv),
            new("2025-11", "https://images.reading.gov.uk/2025/12/Over-500-Spend-Novemberl-2025.csv", SourceFormat.Csv),
            new("2025-12", "https://images.reading.gov.uk/2026/09/Over-500-Spend-December-2025.csv", SourceFormat.Csv),
            new("2026-01", "https://images.reading.gov.uk/2026/02/Over-500-Spend-January-2026.csv", SourceFormat.Csv),
            new("2026-02", "https://images.reading.gov.uk/2026/09/Over-500-Spend-February-2026-6.csv", SourceFormat.Csv),
            new("2026-03", "https://images.reading.gov.uk/2026/04/Over-500-Spend-March-2026.csv", SourceFormat.Csv),
        },
        KnownQuirks: new List<string>
        {
            "ONE signed Amount column, no net/gross/VAT split at all - Schedule A (amount mismatch) cannot run for " +
                "this council; it is structurally inapplicable, not a clean result. Only Schedule B and D-style " +
                "checks are meaningful here.",
            "Negative amounts are real: credits, corrections, and refunds are published as negative payments " +
                "under the same voucher/payment number as the original charge (confirmed: a Hexagon theatre " +
                "settlement nets several positive and negative lines to one true total) - never treat a negative " +
                "row as itself an error.",
            "Two reference numbers, not one: \"Voucher Number\" is close to one line/invoice; \"Payment Number\" " +
                "groups several vouchers into one BACS payment run (a real example: one Payment Number covering " +
                "nine Hexagon venue vouchers, several of them credits). Only Voucher Number is modelled this " +
                "session - a payment-run-level schedule is a plausible Schedule E for a future session, not built.",
            "Encoding varies by file, within this one council, same as Wokingham: December 2021's file is UTF-8 " +
                "(with BOM); April 2022 and later checked files are Windows-1252. The engine's per-file auto-" +
                "detection already handles this with no extra configuration.",
            "Column headers and organisation-name values drift year to year: \"RBC Legal Entity\" becomes " +
                "\"Reading Borough Council\"; \"Cost Centre No\" becomes \"Cost Centre\" (not mapped either way - " +
                "not used by this profile); \"Supplier name\"/\"Supplier Name\" casing varies (harmless - header " +
                "lookup is already case-insensitive).",
            "Filenames are inconsistent and sometimes typo'd on the council's own site (\"Over-599-Spend-" +
                "December-21.csv\" for December 2021's £500 file; \"Over-500-March-24B.csv\" with a trailing " +
                "letter for a revised file) - don't assume a filename pattern when scripting a bulk fetch.",
            "Redaction does occur here too, just rarer: 7 of 24,377 rows across the 5 sampled months carry a " +
                "redacted supplier/transaction id (vs. ~2% for Wokingham, ~28% for Merton in the same sampling) - " +
                "the engine's exclusion catches these the same way. Confirmed by running the engine, not by a " +
                "manual text search (an earlier manual grep for \"REDACT\" on one file alone missed this and " +
                "wrongly said 'none found' - corrected here; running the real engine end to end is the check that " +
                "matters, not a spot grep).",
            "A real, not-yet-confirmed lead (owner's tip): \"WOKINGHAM BOROUGH COUNCIL\" appears repeatedly as a " +
                "payee in Reading's own files across multiple months and purposes - Network Management/Leasing " +
                "Costs (£9,058.30, March 2023), Hexagon theatre venue costs, LD Community Services travel " +
                "expenses. Not yet cross-referenced against the specific Wokingham PFI-credit transactions the " +
                "owner flagged (2020-21 to 2023-24, each 15-30% short of stated invoice) - that comparison needs " +
                "the exact Wokingham PFI transaction dates/amounts pulled from the existing schedules and matched " +
                "by date+amount against these Reading entries, not yet done this session. Caution: \"ABC TRAVEL " +
                "(WOKINGHAM) LIMITED\" is a different, unrelated false-positive match on a company trading name " +
                "that merely contains the place name \"Wokingham\" - confirmed by reading the raw row, not a " +
                "council-to-council payment.",
            "Session 15: the single biggest supplier on Schedule R's Unmatched top-20 by far is \"Brighter " +
                "Futures for Children\" (£4.3m/£2.3m/£1.3m/... single \"RBC Refunds Manual Entry\" rows, all " +
                "19-20 June 2024). A published basis DOES exist and partially corroborates these: Brighter " +
                "Futures for Children was Reading Borough Council's own arm's-length children's-services " +
                "company (set up 2018, services moved back INTO the council 1 October 2025 - it no longer " +
                "exists as a separate body); RBC's own committee papers record a £9.139m additional funding " +
                "payment agreed for BFfC's 2023/24 outturn overspend (contract sum £44.933m, actual spend " +
                "£54.177m, council covering £9.139m of the £9.244m gap, excluding ~£105k of accrual costs - " +
                "https://democracy.reading.gov.uk/documents/s29497/Appendix%202%20-%20Brighter%20Futures%20for" +
                "%20Children%20BFfC%20Budget%20Monitoring%20Report%20Quarter%202%202023-24.pdf and reporting on " +
                "the same figure via rdg.today, 'Millions more than expected spent by Reading's council-owned " +
                "company for children's services'). The full set of \"RBC Refunds Manual Entry\" rows to " +
                "Brighter Futures for Children in the loaded 2024-06 file sums to £10,297,261.35 (15 rows, " +
                "19-20 June 2024, confirmed by hand against the raw file) - close to, but NOT exactly, the " +
                "published £9.139m figure (about £1.16m over, plausibly because the June payment run also " +
                "settles the regular quarterly contract balance alongside the one-off top-up, but that is NOT " +
                "independently confirmed this session). Per the no-excuses rule: this is strong, cited, real-" +
                "world context for why a council-owned children's company receiving multi-million payments " +
                "under a generic \"refund\" label is plausible and explainable at the institutional level - " +
                "but the specific row-level amounts do NOT exactly reconcile against the one published figure " +
                "found, so NO classification rule was coded and these rows correctly stay `Unmatched` in " +
                "Schedule R. Recorded here as the caveat for anyone reading the top-20 export, not as a fix.",
            // Session 21: searched the full back-catalogue directly for "RE3"/"FCC" rather
            // than relying only on the Bracknell-side crossref. Reading pays "RE3 LTD" as a
            // named supplier directly (Service Area "Directorate of Economic Growth and
            // Neighbourhood Services", categories "Household Waste"/"Waste
            // Disposal"/"Supplies and Services", roughly £2.0-3.1m per month in the files
            // checked, summing to £159.7m across the rows found) - Reading is the lead/host
            // authority holding the actual re3 operating-company contract, which is why
            // Bracknell Forest's and Wokingham's shares are recharged THROUGH Reading (Session
            // 22 correction: Wokingham's is NOT a "much smaller one-off" - it is ~£8-11m a year under
            // Cost Centre Area "Waste PFI Contract", see Bracknell Forest's KnownQuirks) rather than paid to a shared third party
            // directly. Reading ALSO separately pays "FCC RECYCLING (UK) LTD" (ordinary
            // Tipping Charge/Parks Tipping) - confirmed by hand to be a DIFFERENT entity
            // from "RE3 LTD", not the same contract under two names; do not conflate them.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "5 months run through the engine end to end (2021-12, 2022-04, 2023-03, 2024-06, 2026-02; 24,377 raw " +
            "rows). Schedule A correctly inapplicable (see quirks). Schedule D found ZERO multi-payee/multi-date " +
            "vouchers across all 5 months - consistent with, and stronger evidence for, the CouncilWideUnique " +
            "scope call (voucher-number uniqueness was separately checked by hand on one month: 4,595 distinct " +
            "vouchers, 1 collision, confirmed to be the blank-voucher case, not a real collision). Schedule B found " +
            "5,040 repeated-payment member rows. 7 redacted rows found by the engine (see quirks - a manual grep " +
            "had wrongly said none existed; the full-engine run is what should be trusted, not a spot check). " +
            "Hand-opened 1 Schedule B group against the raw 2021-12 file (vouchers 4619430/4619523, both to " +
            "Andrew & Jacqueline Freeborn, £682.98, same date/purpose, same Payment Number 264133) - confirmed to " +
            "exist exactly as the schedule describes. Not independently verified whether it is itself a genuine " +
            "error (sharing one Payment Number is at least consistent with a deliberate two-voucher rent-scheme " +
            "payment) - per the no-excuses rule, reported as published; only one sample opened so far, more " +
            "needed before treating Reading as fully supported for a public page.",
        FoiContactEmail: "FOI.CRT@reading.gov.uk" // verified via council page https://www.reading.gov.uk/contact-us/freedom-of-information-foi/freedom-of-information-act-procedure-for-dealing-with-requests/, checked 2026-10-03
    );

    /// <summary>
    /// Council #4, onboarded Session 15 per the owner's own checklist (Wokingham's
    /// neighbour, deferred from Session 14 by the owner's mid-cycle "do Reading too"
    /// priority change). Quarterly (not monthly or annual) publication, ONE signed
    /// "Amount £" column (same shape as Reading - no net/gross/VAT split, Schedule A
    /// structurally inapplicable), Date published as an Excel serial number even
    /// though the source file carries it as a genuine date cell (the engine's existing
    /// Excel-serial fallback in <see cref="AuditEngine.ParseDate"/> was written for
    /// exactly this shape and handles it with no new code).
    /// </summary>
    public static readonly SupportedCouncil BracknellForest = new(
        Name: "Bracknell Forest Council",
        TransparencyPageUrl: "https://www.bracknell-forest.gov.uk/council-and-democracy/finance-and-transparency/publication-scheme/what-we-spend-and-how-we-spend-it",
        HowToFindTheFile:
            "On Bracknell Forest's \"What we spend and how we spend it\" page, under \"Payments to suppliers over " +
            "£500\", downloads are listed by QUARTER (3-month periods, e.g. \"April 2026 to June 2026\"), not month " +
            "or financial year, back to December 2020. Each is a plain .xlsx. Download the quarter(s) you want and " +
            "drop each file onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "TransNo",
            Supplier: "Supplier",
            PayDate: "Date",
            Description: "Expenses Type",
            ServiceArea: "Service Area",
            Net: null,
            Gross: "Amount £",
            VatAmount: null,
            VatType: null),
        // CHECKED BY HAND, NOT ASSUMED - and the first pass through this check found a real trap,
        // corrected before shipping rather than left wrong: TransNo is council-assigned (not a
        // supplier's own invoice number re-published - no evidence of that anywhere, and most
        // multi-payee groups below involve safeguarding-redacted individual care recipients, who
        // never furnish their own invoice number), so CouncilWideUnique is the right ENUM value.
        // But "council-assigned" does NOT mean "one invoice per TransNo" here, the way it does for
        // Wokingham's TransNo: for Adult Social Care Contracted Services specifically, Bracknell
        // Forest publishes TransNo as a shared BATCH/PAYMENT-RUN id covering many genuinely
        // different care-agency suppliers and redacted individual care packages in one go -
        // confirmed by hand against the raw March-May 2021 file: trans 30391217 alone bundles 99
        // rows (A&T CARING SERVICES, CHOICE SUPPORTED LIVING, OM CARE T/A CAREMARK WOKINGHAM, and
        // dozens of REDACT PERSONAL INFORMATION individual care-recipient lines); the April-June
        // 2026 file has even larger examples (trans 30509113: 123 rows across 15 distinct care
        // suppliers). This is the exact same shape as Reading's "Payment Number" trap
        // (ONBOARDING_CHECKLIST.md 4b) - a real batch identifier, not evidence of a genuine
        // one-invoice/multi-payee anomaly. Schedule D's raw output for this council is therefore
        // DOMINATED by this batch-run artifact for Adult Social Care rows, not a real finding -
        // see KnownQuirks below; not suppressed by code (SupplierFurnished would be a worse,
        // factually wrong label - TransNo genuinely is council-assigned), but must be stated
        // plainly wherever Schedule D output for this council is shown.
        IdScope: TransactionIdScope.CouncilWideUnique,
        // Confirmed by hand: every multi-row TransNo found has each line with a DIFFERENT Amount £
        // (itemised lines of one invoice), never the same Amount £ repeated identically across every
        // line - i.e. there is no "repeated invoice total" convention here at all, same shape as
        // Reading and Merton's PerLineAmount, NOT Wokingham's RepeatedInvoiceTotal.
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: new List<CouncilYearFile>
        {
            // Session 19: full back-catalogue, 22 quarterly files, December 2020 through
            // April-June 2026 - every URL below live-fetched from the council's own "What we
            // spend and how we spend it" page on 2026-10-03 and downloaded successfully
            // (22/22). Replaces the 2-quarter representative sample used through Session 18.
            // NOTE: "September 2021 to December 2021" is a 4-MONTH period, not a quarter -
            // the council's own cadence has one irregular period here before settling into
            // calendar quarters from January 2022 onward; not a download bug, named here so
            // a future reader doesn't "fix" the tag to a 3-month span that doesn't exist.
            new("2020-DecFeb", "https://www.bracknell-forest.gov.uk/sites/default/files/2021-11/payments-over-500-dec-2020-to-feb-2021%20%281%29.xlsx", SourceFormat.Xlsx),
            new("2021-MarMay", "https://www.bracknell-forest.gov.uk/sites/default/files/2021-11/payments-over-500-march-2021-to-may-2021.xlsx", SourceFormat.Xlsx),
            new("2021-JunAug", "https://www.bracknell-forest.gov.uk/sites/default/files/2021-11/payments-over-500-june-2021-to-august-2021.xlsx", SourceFormat.Xlsx),
            new("2021-SepDec", "https://www.bracknell-forest.gov.uk/sites/default/files/2022-01/payments-over-%C2%A3500-september-2021-to-december-2021.xlsx", SourceFormat.Xlsx),
            new("2022-JanMar", "https://www.bracknell-forest.gov.uk/sites/default/files/2022-05/payments-over-%C2%A3500-january-2022-to-march-2022.xlsx", SourceFormat.Xlsx),
            new("2022-AprJun", "https://www.bracknell-forest.gov.uk/sites/default/files/2022-08/payments-over-500-april-june-2022.xlsx", SourceFormat.Xlsx),
            new("2022-JulSep", "https://www.bracknell-forest.gov.uk/sites/default/files/2022-10/payments-over-%C2%A3500-july-september-2022.xlsx", SourceFormat.Xlsx),
            new("2022-OctDec", "https://www.bracknell-forest.gov.uk/sites/default/files/2023-02/over-500-spend-report-october-december-2022.xlsx", SourceFormat.Xlsx),
            new("2023-JanMar", "https://www.bracknell-forest.gov.uk/sites/default/files/2023-05/payments-over-500-january-to-march-2023.xlsx", SourceFormat.Xlsx),
            new("2023-AprJun", "https://www.bracknell-forest.gov.uk/sites/default/files/2023-08/payments-over-500-April-June%202023.xlsx", SourceFormat.Xlsx),
            new("2023-JulSep", "https://www.bracknell-forest.gov.uk/sites/default/files/2023-11/payments-over-500-july-2023-to-september-2023.xlsx", SourceFormat.Xlsx),
            new("2023-OctDec", "https://www.bracknell-forest.gov.uk/sites/default/files/2024-02/payments-over-500-october-december-2023.xlsx", SourceFormat.Xlsx),
            new("2024-JanMar", "https://www.bracknell-forest.gov.uk/sites/default/files/2024-06/payments-over-500-january-to-march-2024.xlsx", SourceFormat.Xlsx),
            new("2024-AprJun", "https://www.bracknell-forest.gov.uk/sites/default/files/2024-08/payments-over-%C2%A3500-april-june-2024.xlsx", SourceFormat.Xlsx),
            new("2024-JulSep", "https://www.bracknell-forest.gov.uk/sites/default/files/2024-10/payments-over-%C2%A3500-july-september-2024_0.xlsx", SourceFormat.Xlsx),
            new("2024-OctDec", "https://www.bracknell-forest.gov.uk/sites/default/files/2025-02/payments-over-%C2%A3500-october-to-december-2024.xlsx", SourceFormat.Xlsx),
            new("2025-JanMar", "https://www.bracknell-forest.gov.uk/sites/default/files/2025-05/payments-over-%C2%A3500-january-2025-to-march-2025.xlsx", SourceFormat.Xlsx),
            new("2025-AprJun", "https://www.bracknell-forest.gov.uk/sites/default/files/2025-08/payments-over-500-april-2025-to-june-2025.xlsx", SourceFormat.Xlsx),
            new("2025-JulSep", "https://www.bracknell-forest.gov.uk/sites/default/files/2025-10/payments-over-500-july-2025-to-september-2025.xlsx", SourceFormat.Xlsx),
            new("2025-OctDec", "https://www.bracknell-forest.gov.uk/sites/default/files/2026-02/payments-over-%C2%A3500-october-to-december-2025.xlsx", SourceFormat.Xlsx),
            new("2026-JanMar", "https://www.bracknell-forest.gov.uk/sites/default/files/2026-04/Final-January-2026-March-2026.xlsx", SourceFormat.Xlsx),
            new("2026-AprJun", "https://www.bracknell-forest.gov.uk/sites/default/files/2026-09/final-april-2026-to-june-2026.xlsx", SourceFormat.Xlsx),
        },
        KnownQuirks: new List<string>
        {
            "ONE signed \"Amount £\" column, no net/gross/VAT split at all - same structural shape as Reading - " +
                "Schedule A (amount mismatch) cannot run for this council; it is structurally inapplicable, not a " +
                "clean result.",
            "Publishes by QUARTER (3-month periods), not month (Reading) or financial year (Wokingham/Merton) - a " +
                "full financial year is only 4 files, not 12, but the file list runs back to December 2020 with no " +
                "single-file-per-year option.",
            "No separate Invoice Type / credit-flag column (unlike Reading) - a negative Amount £ is the only signal " +
                "of a credit/correction for this council; confirmed present in the sampled data (not yet counted or " +
                "classified - Schedule R needs an InvoiceType column to run at all and is therefore structurally " +
                "empty for this council, same \"not a bug\" principle as Wokingham/Merton).",
            "No separate SupplierInvoiceNumber field published - Schedule B stays Unclear for every group here, " +
                "same structural reason as Wokingham/Merton (see ONBOARDING_CHECKLIST.md 4b).",
            "File URLs are individually stable (each resolves to a real, distinct .xlsx on " +
                "bracknell-forest.gov.uk/sites/default/files/), unlike Wokingham's dropdown-driven page - confirmed " +
                "by downloading the earliest (March-May 2021) and latest (April-June 2026) listed quarters directly.",
            "Column header is stable across the 5+ years sampled (both the earliest and latest files checked have " +
                "the identical 10-column header: Body Name, Body, Service Area, Service Division, Responsible " +
                "Unit, Expenses Type, Date, TransNo, Amount £, Supplier) - no drift found, unlike Reading's header " +
                "and organisation-name drift.",
            "TransNo is a shared BATCH/PAYMENT-RUN id for Adult Social Care Contracted Services, not a one-" +
                "invoice reference - the same trap as Reading's \"Payment Number\" (ONBOARDING_CHECKLIST.md 4b), " +
                "found here on the FIRST real multi-row hand-check rather than assumed clean from a quick look. " +
                "Confirmed real (2021 Mar-May file): trans 30391217 bundles 99 rows across 3 named care agencies " +
                "plus dozens of REDACT PERSONAL INFORMATION individual care-recipient lines; trans 30392544 " +
                "bundles 174 rows. The 2026 Apr-Jun file has even larger examples (trans 30509113: 123 rows, 15 " +
                "distinct suppliers; trans 30511127: 131 rows, 15 suppliers). Schedule D's 29-group output for " +
                "this council (after redaction exclusion) is DOMINATED by this batch-run artifact, not genuine " +
                "one-invoice/multi-payee anomalies - must be stated plainly on any page that shows Schedule D for " +
                "this council, the same honesty standard as Reading's own Payment Number caveat.",
            "High redaction rate: 5,389 of 14,198 raw rows (38%) carry \"REDACT PERSONAL INFORMATION\" as the " +
                "supplier/transaction identifier (adult social care and children's safeguarding placements) - " +
                "higher than Wokingham (~2%) or Reading (~0.03%), closer to Merton's ~22% - consistent with this " +
                "council publishing a higher proportion of individually-paid care placements at this threshold.",
            // Session 20: the Bracknell Forest -> Reading crossref direction's large "Waste
            // Disposal"/"Contracted Services" payments (Session 19 finding, 248 rows, £42.9m)
            // now HAVE a published institutional basis, found by web search, not guessed: re3
            // is a real, formally constituted joint waste-management partnership between
            // EXACTLY Bracknell Forest, Reading, and Wokingham Borough Councils, established
            // 1999 under a Joint Waste Disposal Board, contracted with FCC Environment since
            // 2006 for a 25-year waste disposal/recycling contract serving ~500,000 residents
            // (sources: re3.fccenvironment.co.uk/about-us/; Bracknell News, "re3 partnership
            // celebrates 25 years of waste management", 2024; re3 Waste Strategy, hosted on
            // wokingham.moderngov.co.uk). This is a real, named basis for why Bracknell
            // Forest's own published spending shows large one-off payments to Reading Borough
            // Council under Waste Disposal/Contracted Services service areas - inter-authority
            // cost-sharing/recharging under a joint arrangement the three partner councils
            // themselves publicly describe. NOT coded as a new Schedule classification rule
            // (this is a cross-council institutional fact, not a within-council reconciling
            // rule the engine can test against a single council's own published columns) -
            // stated here as the now-CONFIRMED context for that crossref direction, same
            // honesty standard as the Brighter Futures for Children finding, upgraded from
            // Session 19's \"plausible, not confirmed\" once a citable published source was
            // actually found rather than assumed.",
            // Session 21: isolating just the "Waste Disposal" service area within the
            // Bracknell Forest -> Reading crossref direction (not the whole £42.9m, which
            // includes other service areas too): £36.15m across 21 of the 22 quarters,
            // growing steadily from ~£1.5m/quarter (2020-DecFeb) to ~£2.1m/quarter
            // (2026-AprJun), each netted against a recurring ~£203,793.18 "Government
            // Grants" credit plus smaller "Other Income" credits every quarter - a clean,
            // consistent recurring recharge pattern (not coincidental, confirmed by its own
            // steady quarter-over-quarter growth), consistent with this being Bracknell
            // Forest's own ongoing SHARE of the re3 contract, recharged to Reading as the
            // lead/host authority (see Reading's own KnownQuirks below - Reading pays
            // "RE3 LTD" directly, £159.7m across its own back-catalogue, the actual
            // operating-company payments this recharge is a share of).
            "re3 share, Session 22 CORRECTION of the Session 21 addendum (which said Wokingham's data " +
                "did not show the re3 pattern - that was wrong, caused by searching only for the strings " +
                "\"RE3\"/\"FCC\"): Wokingham publishes its re3 share as payments to Reading Borough Council " +
                "under Cost Centre Area \"Waste PFI Contract\" (Description \"TPP - Other Local Authorities\"): " +
                "67 transactions, about £8.2m-£11.4m net per financial year across all six years, 9-13 " +
                "payments a year, plus 13 negative \"PFI Credits\" lines FY2020-21 to FY2023-24 (about " +
                "-£2.6m net) and one separate \"Interest Payable on PFI Unitary Payments\" line (FY2020-21, " +
                "£1,026,474.96). The shape differs from Bracknell Forest's (a growing quarterly recharge) in " +
                "cadence and lumpiness, not in existence. Hand-opened 1,139/1,139 Wokingham->Reading rows " +
                "against raw source. See BASELINE.md Session 22.",
        },
        LastChecked: "2026-10-03 (Session 19 - full back-catalogue)",
        VerificationNote:
            "Session 19: full 22-quarter back-catalogue (December 2020 through April-June 2026, every URL " +
            "live-fetched from the council's own page and downloaded successfully), replacing Session 15-18's " +
            "2-quarter sample - council #4's first full onboarding against the BASELINE.md standard. 161,336 raw " +
            "rows; 32,078 (19.9%) carry a redacted supplier/transaction id and are excluded (closer to Merton's " +
            "~22% than Wokingham's ~2%/Reading's ~0.03%, as the 2-quarter sample's 38% figure already suggested " +
            "directionally, though the full-population rate is lower than that sample implied). Schedule A " +
            "correctly inapplicable (see quirks - no net/gross split). Schedule B: 1,969 groups, 5,148 member " +
            "rows, all `Unclear` (no SupplierInvoiceNumber field) - seeded random sample n=30, **30/30 confirmed** " +
            "against raw source (existence + independently re-parsed Net/Gross/Description), 95% Wilson CI " +
            "[88.6%, 100.0%]. Schedule D: 519 groups (51,575 member lines) - dominated by the batch-run-TransNo " +
            "quirk above, same honesty caveat as before, but now hand-opened in FULL against raw source (not a " +
            "sample): **519/519 confirmed** to exist exactly as published (`ConfirmedScheduleDMember`, the same " +
            "exact-row-position check used for Reading's Schedule D since Session 18). Crossref (Session 19, full " +
            "data): Bracknell Forest -> Wokingham Borough Council grew from 12 rows/£103,786.12 (2-quarter sample) " +
            "to 147 rows/£1,196,109.68; a new NEW direction, Bracknell Forest -> Reading Borough Council, found " +
            "248 rows/£42,926,894.99, heavily weighted by a small number of very large \"Waste Disposal\"/" +
            "\"Contracted Services\" payments (e.g. £560,250.23 and £582,166.79 single lines) - consistent with " +
            "re3, the real joint waste-management partnership Reading, Wokingham, and Bracknell Forest councils " +
            "operate together. Session 19 named this link as plausible but NOT independently confirmed; Session " +
            "20 found and cites a real published basis (re3's own \"About us\" page, local press covering its " +
            "25th anniversary, and its Waste Strategy document - see KnownQuirks above): a formally constituted " +
            "joint partnership (Joint Waste Disposal Board, established 1999, FCC Environment contracted since " +
            "2006) between exactly these three councils, upgrading this from plausible context to a confirmed " +
            "institutional basis for the Waste Disposal/Contracted Services shape of this crossref direction. Not " +
            "coded as a new engine classification rule - a cross-council institutional fact, not a within-council " +
            "reconciling pattern. Both full populations (147 and 248 rows) hand-opened against Bracknell " +
            "Forest's own raw source files: **147/147 and 248/248 confirmed** (`handopencrossref`, generalised " +
            "this session from a Reading-only check to any payer council). See FEEDBACK.md Session 19 and " +
            "BASELINE.md Section 2/6 for the full log and the deviations from the Wokingham/Reading baseline " +
            "shape this first full Bracknell Forest pass surfaced.",
        FoiContactEmail: "information.compliance-officer@bracknell-forest.gov.uk" // verified via council page
        // https://www.bracknell-forest.gov.uk/council-and-democracy/data-protection-and-freedom-information/freedom-information,
        // checked 2026-10-03
    );

    /// <summary>
    /// Council #5, onboarded Session 22 (2026-10-03) per the coordinator's own direction.
    /// A genuinely NEW structural shape, more extreme than any council so far: ONE signed
    /// "Net amount" column (Schedule A inapplicable, same as Reading/Bracknell) AND no
    /// transaction/invoice/voucher reference column of any kind at all (see
    /// WestBerkshireFixups.cs - the engine synthesizes a per-row-unique id so it can run,
    /// which makes Schedule D structurally empty by construction, not a finding). Only a
    /// FIRST PASS this session (29 of the council's full back-catalogue back to 2012
    /// downloaded and checked, FY2024-25/2025-26/partial 2026-27 - not the full
    /// back-catalogue the way Wokingham/Reading/Bracknell Forest eventually got; carried
    /// forward, named honestly, same discipline as every other partial pass in this file).
    /// </summary>
    public static readonly SupportedCouncil WestBerkshire = new(
        Name: "West Berkshire Council",
        TransparencyPageUrl: "https://www.westberks.gov.uk/expenditure-over-500",
        HowToFindTheFile:
            "On West Berkshire's \"Expenditure over £500\" page, downloads are listed by MONTH back to 2012, " +
            "recent years as .xlsx, older years (2022-09 and earlier) as .csv. The page itself returns HTTP 403 " +
            "to a plain bot fetch (Cloudflare-style protection observed this session) but the individual file " +
            "links under /media/... download cleanly with an ordinary browser user-agent string - the page " +
            "block is on the LANDING page, not the files themselves. Download the month(s) you want and drop " +
            "each file onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "RowRef", // synthetic, see WestBerkshireFixups.cs - no real reference is published
            Supplier: "Supplier name",
            PayDate: "Date",
            Description: "Narrative",
            ServiceArea: "Service",
            Net: null,
            Gross: "Net amount", // the council's OWN column name, despite there being no separate gross/VAT
                                   // figure published anywhere - this is the one and only payment amount.
            VatAmount: null,
            VatType: null),
        IdScope: TransactionIdScope.CouncilWideUnique, // moot in practice - RowRef is synthetic and unique
        // per row by construction, so Schedule D can never find a real collision here either way.
        GrossMeaning: GrossMeaning.PerLineAmount, // each row is its own figure; no repeated-total convention
        // is even possible to observe since no two rows ever share a TransactionId.
        Years: new List<CouncilYearFile>
        {
            new("2024-04", "https://www.westberks.gov.uk/media/59001/2024-April-Spend-over-500/xls/Over500_24_P01_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-05", "https://www.westberks.gov.uk/media/59224/2024-May-Spend-over-500/xls/Over500_24_P02_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-06", "https://www.westberks.gov.uk/media/59327/2024-June-Spend-over-500/xls/Over500_24_P03_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-07", "https://www.westberks.gov.uk/media/59780/2024-July-Spend-over-500/xls/Over500_24_P04_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-08", "https://www.westberks.gov.uk/media/59996/2024-August-Spend-over-500/xls/Over500_24_P05_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-09", "https://www.westberks.gov.uk/media/60321/2024-September-Spend-over-500/xls/Over500_24_P06_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-10", "https://www.westberks.gov.uk/media/60532/2024-October-Spend-over-500/xls/Over500_24_P07_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-11", "https://www.westberks.gov.uk/media/60791/2024-November-Spend-over-500/xls/Over500_24_P08_-_Published.xlsx", SourceFormat.Xlsx),
            new("2024-12", "https://www.westberks.gov.uk/media/61129/2024-December-Spend-over-500/xls/Over500_24_P09_-_Published.xlsx", SourceFormat.Xlsx),
            new("2025-01", "https://www.westberks.gov.uk/media/61664/2025-January-Spend-over-500/xls/Over500_24_P10_-_Published.xlsx", SourceFormat.Xlsx),
            new("2025-02", "https://www.westberks.gov.uk/media/62122/2025-February-Spend-over-500/xls/Over500_24_P11_-_Published.xlsx", SourceFormat.Xlsx),
            new("2025-03", "https://www.westberks.gov.uk/media/62869/2025-March-Spend-over-500/xls/Over500_24_P12_-_Published.xlsx", SourceFormat.Xlsx),
            new("2025-04", "https://www.westberks.gov.uk/media/63421/2025-April-Spend-over-500/xls/Over500_2025_P01_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-05", "https://www.westberks.gov.uk/media/63861/2025-May-Spend-over-500/xls/Over500_2025_P02_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-06", "https://www.westberks.gov.uk/media/63942/2025-June-Spend-over-500/xls/Over500_2025_P03_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-07", "https://www.westberks.gov.uk/media/64177/2025-July-Spend-over-500/xls/Over500_2025_P04_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-08", "https://www.westberks.gov.uk/media/64603/2025-August-Spend-over-500/xls/Over500_2025_P05_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-09", "https://www.westberks.gov.uk/media/64681/2025-September-Spend-over-500/xls/Over500_2025_P06_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-10", "https://www.westberks.gov.uk/media/65328/2025-October-Spend-over-500/xls/Over500_2025_P07_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-11", "https://www.westberks.gov.uk/media/65416/2025-November-Spend-over-500/xls/Over500_2025_P08_-_published.xlsx", SourceFormat.Xlsx),
            new("2025-12", "https://www.westberks.gov.uk/media/65552/2025-December-Spend-over-500/xls/Over500_2025_P09_-_published.xlsx", SourceFormat.Xlsx),
            new("2026-01", "https://www.westberks.gov.uk/media/65748/2026-January-Spend-Over-500/xls/Over500_2025_P10_-_Published.xlsx", SourceFormat.Xlsx),
            new("2026-02", "https://www.westberks.gov.uk/media/66430/2026-February-Spend-Over-500/xls/Over500_2025_P11_-_Published.xlsx", SourceFormat.Xlsx),
            new("2026-03", "https://www.westberks.gov.uk/media/66669/2026-March-Spend-Over-500/xls/Over500_2025_P12_-_Published.xlsx", SourceFormat.Xlsx),
            new("2026-04", "https://www.westberks.gov.uk/media/66870/2026-April-Spend-Over-500/xls/Over500_2026_P01_-_published.xlsx", SourceFormat.Xlsx),
            new("2026-05", "https://www.westberks.gov.uk/media/66999/2026-May-Spend-Over-500/xls/Over500_2026_P02_-_published.xlsx", SourceFormat.Xlsx),
            new("2026-06", "https://www.westberks.gov.uk/media/67175/2026-June-Spend-Over-500/xls/Over500_2026_P03_-_published.xlsx", SourceFormat.Xlsx),
            new("2026-07", "https://www.westberks.gov.uk/media/67489/2026-July-Spend-Over-500/xls/Over500_2026_P04_-_published.xlsx", SourceFormat.Xlsx),
            // 2026-08 EXCLUDED - see KnownQuirks: the downloaded file is not a normal data
            // export at all (an "Excelerator"-flagged macro/settings sheet, 9 rows, no real header).
        },
        KnownQuirks: new List<string>
        {
            "NO transaction/invoice/voucher reference column of any kind - header is exactly \"Service | " +
                "Expenditure category | Narrative | Date | Net amount | Supplier name\", confirmed identical " +
                "across 2024-04 and 2025-04 by hand. The engine's AuditEngine.MapRows requires SOME " +
                "TransactionId column to key Schedule A/B/D's grouping on; leaving it unmapped would make " +
                "every row in a file share one blank id (collapsing the whole file into one false " +
                "\"transaction\"). WestBerkshireFixups.AddSyntheticRowId adds a synthetic per-row-unique " +
                "\"RowRef\" column before mapping instead - this means Schedule A (already near-empty, see " +
                "next point) and Schedule D (needs a REAL shared reference spanning >1 payee/pay-date to be " +
                "meaningful) come out structurally EMPTY BY CONSTRUCTION for this council, not as a finding " +
                "about its spending. Only Schedule B (same supplier+amount+description+pay-date repeated " +
                "under what the engine treats as different transactions) stays a meaningful check here, since " +
                "it does not depend on TransactionId at all.",
            "ONE signed \"Net amount\" column, no VAT split, no separate gross figure anywhere - same " +
                "structural shape as Reading/Bracknell Forest for Schedule A purposes, but note the council's " +
                "OWN column name calls this \"Net\" despite it being the only published amount at all (there is " +
                "no corresponding \"Gross\" column this council ever publishes) - stated neutrally, not as an " +
                "accusation; several UK councils use \"Net\" simply to mean \"net of internal recharges\", not " +
                "as half of a net/gross pair.",
            "Date is an Excel serial number WITH a fractional time-of-day component (e.g. 45404.752629664355, " +
                "not a whole number like Bracknell Forest's) - confirmed the existing AuditEngine.ParseDate " +
                "Excel-serial fallback already handles this correctly (DateOnly.FromDateTime truncates the " +
                "fractional part), no new parsing code needed.",
            "The landing page (https://www.westberks.gov.uk/expenditure-over-500) returns HTTP 403 to a plain " +
                "bot fetch (Cloudflare-style check observed this session) but the individual /media/... file " +
                "links download cleanly with an ordinary browser user-agent header - confirmed by hand, not " +
                "guessed; a future re-check of this council's file list needs a real browser session or an " +
                "equivalent user-agent, not a bare HTTP client.",
            "wb_2026-08.xlsx (\"Over500_2026_P5_WP_Final.xlsx\") is EXCLUDED from every number below - the " +
                "downloaded file is not a normal data export: its first sheet's header literally reads \"* " +
                "This sheet is manipulated by Excelerator and should not be changed by hand\", with only 9 " +
                "data rows, none resembling spending data - almost certainly a budgeting/working-paper " +
                "worksheet the council's own template software left in front of the real data sheet, or the " +
                "wrong file published under this link. Needs opening in real Excel (same unresolved-safely " +
                "category as reading_2021-05.xlsx) before it can be included, not another automated guess.",
            "3 of the 29 files (2025-12, 2026-06, 2026-07) have ONE fully blank decorative row before the " +
                "real header - every other file has the real header at row 0 with no leading blank row at all, " +
                "confirming this is a genuine inconsistency in the council's own export process, not a single " +
                "typo. Fixed structurally (any number of leading blank rows, not hardcoded to exactly 1) by " +
                "WestBerkshireFixups.SkipLeadingBlankHeaderRow, applied unconditionally (a no-op on the files " +
                "that don't need it).",
            "Only a FIRST PASS this session: 28 of 29 downloaded months (2024-04 through 2026-07) loaded " +
                "cleanly; this is NOT yet the full back-catalogue the council publishes (file links exist back " +
                "to 2012-04, per the council's own landing page) - carried forward as a next-cycle item, same " +
                "honesty standard as every other partial council pass recorded in BASELINE.md.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "28 of 29 downloaded months (2024-04 through 2026-07; 2026-08 excluded - see KnownQuirks) run " +
            "through the engine end to end: 91,784 raw mapped rows, 16,494 (18.0%) redacted and excluded " +
            "(between Reading's ~0.03% and Merton's ~22%), 75,290 retained. Schedule A structurally " +
            "near-empty (0 rows - no VAT/gross split, same true-negative shape as Reading/Bracknell Forest). " +
            "Schedule D structurally empty by construction (0 groups - the synthetic per-row RowRef can " +
            "never collide, see KnownQuirks), NOT a finding about this council. Schedule B: 5,013 groups, " +
            "14,927 member rows, £46,676,361.89, all `Unclear` (no supplier invoice number published, same " +
            "structural reason as every other council) - seeded random sample n=30, **30/30 confirmed** " +
            "against raw source (exact member row(s) by position, Net/Gross/Description independently " +
            "re-parsed), 95% Wilson CI [88.6%, 100.0%].",
        FoiContactEmail: "foi@westberks.gov.uk" // verified via council page https://www.westberks.gov.uk/article/40935/Contact-details-for-Freedom-of-Information-Requests, checked 2026-10-03
    );

    /// <summary>Council #6 (Session 23, onboarded because the Royal Borough of Windsor and Maidenhead is the OTHER
    /// owner of Optalis, jointly with Wokingham since April 2017, and pays it). See RbwmFixups.cs.</summary>
    public static readonly SupportedCouncil Rbwm = new(
        Name: "Royal Borough of Windsor and Maidenhead",
        TransparencyPageUrl: "https://www.rbwm.gov.uk/home/council-and-democracy/transparency/budget-spending-and-procurement",
        HowToFindTheFile:
            "On the council's \"Budget, spending and procurement\" transparency page, open \"Payment to Suppliers " +
            "Reports\": one CSV per month for recent months (file name finance_supplier_data_YYYY-MM.csv) and one CSV " +
            "per financial year for older years (2023-24, 2024-25). The threshold is any payment charged to a " +
            "specific cost centre of 100 pounds or more, much lower than the 500 pounds most councils use. The page " +
            "downloads with an ordinary browser user-agent.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction number",
            Supplier: "Supplier (Beneficiary name)",
            PayDate: "Payment Date",
            Description: "Purpose of spend",
            ServiceArea: "Service",
            Net: null,
            Gross: "Net Amount", // the council's own column name; it is the only amount published (no VAT split;
                                 // "Irrecoverable VAT" is text such as "Not applicable", left unmapped)
            VatAmount: null,
            VatType: null),
        IdScope: TransactionIdScope.CouncilWideUnique, // see KnownQuirks
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: new List<CouncilYearFile>
        {
            new("2023-24", "https://www.rbwm.gov.uk/sites/default/files/2024-06/finance_supplier_data_202304-2024303.csv", SourceFormat.Csv),
            new("2024-25", "https://www.rbwm.gov.uk/sites/default/files/2025-07/finance_supplier_data_2024-04_2025-03.csv", SourceFormat.Csv),
            new("2025-04", "https://www.rbwm.gov.uk/sites/default/files/2025-06/finance_supplier_data_2025-04.csv", SourceFormat.Csv),
            new("2025-05", "https://www.rbwm.gov.uk/sites/default/files/2025-06/finance_supplier_data_2025-05.csv", SourceFormat.Csv),
            new("2025-06", "https://www.rbwm.gov.uk/sites/default/files/2025-07/finance_supplier_data_2025-06.csv", SourceFormat.Csv),
            new("2025-07", "https://www.rbwm.gov.uk/sites/default/files/2025-09/finance_supplier_data_2025-07.csv", SourceFormat.Csv),
            new("2025-08", "https://www.rbwm.gov.uk/sites/default/files/2025-11/finance_supplier_data_2025-08.csv", SourceFormat.Csv),
            new("2025-09", "https://www.rbwm.gov.uk/sites/default/files/2025-11/finance_supplier_data_2025-09.csv", SourceFormat.Csv),
            new("2025-10", "https://www.rbwm.gov.uk/sites/default/files/2025-11/finance_supplier_data_2025-10.csv", SourceFormat.Csv),
            new("2025-11", "https://www.rbwm.gov.uk/sites/default/files/2026-01/finance_supplier_data_2025-11.csv", SourceFormat.Csv),
            new("2025-12", "https://www.rbwm.gov.uk/sites/default/files/2026-01/finance_supplier_data_2025-12.csv", SourceFormat.Csv),
            new("2026-01", "https://www.rbwm.gov.uk/sites/default/files/2026-02/finance_supplier_data_2026-01.csv", SourceFormat.Csv),
            new("2026-02", "https://www.rbwm.gov.uk/sites/default/files/2026-04/finance_supplier_data_2026-02.csv", SourceFormat.Csv),
            new("2026-03", "https://www.rbwm.gov.uk/sites/default/files/2026-04/finance_supplier_data_2026-03.csv", SourceFormat.Csv),
            new("2026-04", "https://www.rbwm.gov.uk/sites/default/files/2026-05/finance_supplier_data_2026-04.csv", SourceFormat.Csv),
            new("2026-05", "https://www.rbwm.gov.uk/sites/default/files/2026-06/finance_supplier_data_2026-05.csv", SourceFormat.Csv),
            new("2026-06", "https://www.rbwm.gov.uk/sites/default/files/2026-09/finance_supplier_data_2026-06.csv", SourceFormat.Csv),
        },
        KnownQuirks: new List<string>
        {
            "Optalis (adult social care) is owned by Wokingham Borough Council (since June 2011) and jointly with " +
                "RBWM since April 2017, per optalis.org/our-story-provider-of-choice; this is why RBWM is onboarded. " +
                "RBWM pays it under two supplier spellings that SupplierKey keeps apart, 'Optalis (Main Contract " +
                "Only)' and 'Optalis Ltd'.",
            "THRESHOLD is 100 pounds per cost-centre charge (the file title says so), not 500 pounds, so row counts " +
                "per month (about 2,700) are not comparable with the 500-pound councils.",
            "ONE amount column, 'Net Amount' (mapped as Gross, Net unmapped, as West Berkshire does); 'Irrecoverable " +
                "VAT' is text ('Not applicable', 'Not applicabe', 'Not spplicable' in 2023-24) or blank, never a " +
                "number, so there is no VAT split and Schedule A is empty by construction.",
            "0-2 decorative title/blank rows precede the header; two months (2025-08, 2025-09) use different " +
                "header NAMES over the same positions (Directorate(T), Sercop, Sercop(T), Supplier, Purpose of " +
                "spend(T)); fixed by named renames in RbwmFixups.HeaderFixups.",
            "2025-04, 2025-05 and 2025-06: the header names an 'Irrecoverable VAT' column that the data rows do not " +
                "carry, so every later cell sits one place left (Purpose of spend then reads a classification code " +
                "such as 270000). Detected from the data and realigned by RbwmFixups.AlignIrrecoverableVat.",
            "FILES OVERLAP (checklist step 6, measured): the file named June 2025 holds 1,067 rows PAID IN JUNE 2024 " +
                "(1,002 of them the same payment lines as the 2024-25 annual file) and only 100 rows paid in June 2025, " +
                "against about 2,700 in a normal month, so June 2025 payments are essentially NOT PUBLISHED; 2025-04 " +
                "and 2025-05 overlap by 381 keys, 2025-11 and 2025-12 by 61. Rows already published in an earlier " +
                "file are dropped (1,647 of 65,735) by RbwmFixups.DropAlreadyPublished; without it Schedule B " +
                "counted the same payment twice.",
            "Transaction number: council-assigned and normally one payment line or invoice, but CHAPS payments are " +
                "booked as a 'Sundry Bacs Supplier' line plus the real payee's line and its reversal under ONE number " +
                "(Schedule D's 32 groups are almost all this, plus refunds, DWP CRU claims and one batch of housing " +
                "grants to 5 named people), and 5xxxxxx numbers are batch/payment-run ids (trans 5095002 = 186 lines " +
                "of one agency supplier). Labelled CouncilWideUnique with that caveat; read Schedule D as bookkeeping " +
                "mechanics unless a group shows otherwise.",
            "Gross meaning confirmed PerLineAmount: transaction 20307726 has 40 lines to one supplier with 40 " +
                "different amounts, so a group's amounts are separate charges, not a repeated total.",
            "Redaction: 4,530 of 64,088 retained rows (7.1%) carry a redacted supplier or id (REDACTED PERSONAL DATA) " +
                "and are excluded by the engine; 8 rows have only digits in the supplier-name column (an id).",
            "Data rows carry more cells than the header names (procurement-classification code/name pairs under blank " +
                "headers); these and every other unmapped column are kept in the export's OtherColumns as ColN.",
            "Files are Windows-1252 (0xB4 acute accent in Children's Services); the downloads need an ordinary " +
                "browser user-agent. Older years than 2023-24 were not found on the page this session.",
            "The ledger heading 'External Interest Payable' sits over payment-software, card-processing and brokerage " +
                "lines (ClearAccept, Adelante, BGC, GFI, Tradition): there is NO borrowing/loan/PWLB payment line in " +
                "this file at all, so RBWM's own debt service is not visible here.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "17 files (2023-24 and 2024-25 annual; monthly April 2025 to June 2026), 65,735 mapped rows, 1,647 dropped " +
            "as already published in an earlier file, 64,088 retained, 4,530 redacted and excluded. Schedule A 0 rows " +
            "(no VAT split), Schedule B 3,664 groups / 10,858 member rows / 32,865,949.33 pounds, all Unclear; seeded " +
            "n=30 hand-check 30/30 confirmed against raw source, Wilson 95% CI [88.6%, 100.0%]; Schedule D 32 groups / " +
            "92 lines, every group read (mostly CHAPS booking mechanics). Every RBWM payment row to Optalis (38 + 26) " +
            "and to Achieving for Children (457) hand-opened: 521/521 confirmed.",
        FoiContactEmail: null);

    public static readonly IReadOnlyList<SupportedCouncil> All = new[] { Wokingham, Merton, Reading, BracknellForest, WestBerkshire, Rbwm };
}
