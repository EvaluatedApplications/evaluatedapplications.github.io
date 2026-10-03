// Vendored from C:\Users\dongy\VirtualCustomer\src\CouncilAudit\SupportedCouncils.cs, copied
// 2026-10-03, re-synced 2026-10-03 (same day, DebtCharge fix sync: Reading's back-catalogue
// widened to the full FY2020-21..FY2025-26 set, Merton/BracknellForest entries carried along
// verbatim from upstream even though CouncilDbBuilder/CouncilSpending.razor only drive
// Wokingham+Reading today - this file is a faithful vendor copy, not a curated subset).
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
            // Bracknell Forest's and (per its own much smaller, one-off case) Wokingham's
            // shares are recharged THROUGH Reading rather than paid to a shared third party
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
    /// exactly this shape and handles it with no new code). NOT driven by the Showroom
    /// today (CouncilDbBuilder/CouncilSpending.razor only onboard Wokingham+Reading) -
    /// vendored anyway, faithfully, so this copy matches upstream exactly.
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
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: new List<CouncilYearFile>
        {
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
            "Publishes by QUARTER (3-month periods), not month (Reading) or financial year (Wokingham/Merton).",
            "No separate Invoice Type / credit-flag column - Schedule R is structurally empty for this council.",
            "No separate SupplierInvoiceNumber field published - Schedule B stays Unclear for every group here.",
            "TransNo is a shared BATCH/PAYMENT-RUN id for Adult Social Care Contracted Services, not a one-" +
                "invoice reference, same trap as Reading's own \"Payment Number\".",
        },
        LastChecked: "2026-10-03 (Session 19 - full back-catalogue)",
        VerificationNote:
            "Session 19: full 22-quarter back-catalogue, 161,336 raw rows; 32,078 (19.9%) redacted and excluded. " +
            "Schedule A correctly inapplicable (no net/gross split). Schedule B: 1,969 groups, all Unclear (no " +
            "SupplierInvoiceNumber field), seeded sample 30/30 confirmed. Schedule D: 519 groups, 519/519 confirmed " +
            "against raw source (full population, not a sample).",
        FoiContactEmail: "information.compliance-officer@bracknell-forest.gov.uk" // verified via council page
        // https://www.bracknell-forest.gov.uk/council-and-democracy/data-protection-and-freedom-information/freedom-information,
        // checked 2026-10-03
    );

    public static readonly IReadOnlyList<SupportedCouncil> All = new[] { Wokingham, Merton, Reading, BracknellForest };
}
