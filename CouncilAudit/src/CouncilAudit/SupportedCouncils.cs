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
        // Session 52 (audit E8): the old transparency-code address returns 404 (checked 2026-10-04); the files are listed on the
        // "Data sets and open data" page under the filter "Finance and supplier payments over £500" (fetched 2026-10-04).
        TransparencyPageUrl: "https://www.wokingham.gov.uk/council-and-meetings/open-data-and-transparency/council-and-meetings/open-data-and-transparency/data-sets-and-open-data",
        HowToFindTheFile:
            "On Wokingham's \"Data sets and open data\" page, choose \"Finance and supplier payments over £500\" in the filter. Each financial year " +
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
        // came from an existing local copy (the author's local download folder), not a fresh
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
            "A separate file, \"Supplier spend over £500 - 2022.csv\" (calendar year), repeats rows already in the " +
                "FY2021-22 and FY2022-23 files (the same row content; the file itself is UTF-8 where the year files are Windows-1252, " +
                "and its header says \"Invoice Amount (Net)\") - do not load it alongside the financial-year files or " +
                "every schedule double-counts.",
            "VAT Type codes (EXEM/NBUS/OSCP/ZERO/RRTE/STD) must be read per-row, not assumed uniform across a file.",
            "TransNo is not always one invoice to one payee: 433 transaction numbers in the six years' raw files span more than one " +
                "supplier or pay date (Schedule D exists because of this; it lists 420 transactions / 2,168 lines, because it leaves out the redacted rows, 13 of the 433).",
            "The council's page lists the files through a filter, not as fixed links, so no per-year file address is given here.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "Current counts: 217,117 mapped rows, 4,166 (1.9%) redacted and excluded, 212,951 retained; every row of the six files is " +
            "published (raw records against published rows, file by file: 0 left out). Schedule A 7,853 rows; six of its classes " +
            "hand-checked against the raw files (21/21 and 40/40 samples all confirmed); its difference is the stated Gross minus the " +
            "VAT-adjusted expected Gross, and a gap under one pound is its own class. Schedule B 12,177 groups / 39,279 " +
            "member rows / GBP 145,902,266.89, four classes with 30/30, 30/30, 30/30 and 18/18 confirmed (the samples were drawn before the amount change described below); Schedule D 420 transactions / 2,168 lines. " +
            "Six financial years, 217,117 raw rows. 7 cross-transaction candidate pairs opened by hand against the raw " +
            "file across 3 years: 7/7 confirmed to exist exactly as the schedule describes them. The FY2023-24 workbook was read again " +
            "after a reader fix on 2026-10-03 (37,558 data rows under the header row, header and dates correct). That workbook stores some computed amounts with 17 digits " +
            "(for example 2,279.9899999999998 for 2,279.99); the scanner now keeps the 15 digits the sheet shows. This moved 14 rows out of Schedule A " +
            "(each a gap of exactly 1p that the extra digits had pushed just over the 1p tolerance) and 18 Schedule B groups from Unclear to standing-payment catch-up (11,028 to 11,010 and 894 to 912); " +
            "three more groups appear, all standing-payment surplus (234 to 237).",
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
            new("CY2017", "https://www.merton.gov.uk/sites/default/files/2025-06/2017.csv", SourceFormat.Csv),
            new("CY2018", "https://www.merton.gov.uk/sites/default/files/2025-06/2018.csv", SourceFormat.Csv),
            new("CY2019", "https://www.merton.gov.uk/sites/default/files/2025-06/2019.csv", SourceFormat.Csv),
            new("CY2020", "https://www.merton.gov.uk/sites/default/files/2025-06/2020.csv", SourceFormat.Csv),
            new("CY2021", "https://www.merton.gov.uk/sites/default/files/2025-06/2021.csv", SourceFormat.Csv),
            new("CY2022", "https://www.merton.gov.uk/sites/default/files/2025-06/2022.csv", SourceFormat.Csv),
            new("CY2023", "https://www.merton.gov.uk/sites/default/files/2025-06/2023.csv", SourceFormat.Csv),
            new("CY2024", "https://www.merton.gov.uk/sites/default/files/2025-06/2024.csv", SourceFormat.Csv),
            new("CY2025", "https://www.merton.gov.uk/sites/default/files/2026-04/2025YEARENDInvoice500.csv", SourceFormat.Csv),
            new("CY2026", "https://www.merton.gov.uk/sites/default/files/2026-10/2026%20GTR500YTD06.xlsx.csv", SourceFormat.Csv), // year to date, April-June 2026 payment dates in the file saved 2026-10-03
        },
        KnownQuirks: new List<string>
        {
            "No net-amount column: net is derived as Gross - VAT Amount per row (the scanner does this automatically" +
                "when no net column is named and a VAT amount is).",
            "Dates are \"dd-MMM-yy\" (e.g. \"10-Apr-24\"), not dd/MM/yyyy.",
            "A leading blank/unnamed first column holds a row sequence number, not data; ignored by the mapping.",
            "Publishes by calendar year, not financial year, so a council-to-council comparison must line up periods, " +
                "not just filenames.",
            "Encoding: valid UTF-8/ASCII in the files checked, no Windows-1252 bytes found - do not assume this holds " +
                "for every year without checking (Wokingham's own encoding varied by year too).",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "Current counts (all ten files): 376,383 mapped rows, 84,621 (22.5%) redacted and excluded, 291,762 retained. " +
            "Schedule B 15,844 groups / 59,191 member rows / GBP 255,819,248.84: Unclear 14,225 groups, standing-payment surplus 579, " +
            "catch-up 1,040; random hand-checks of 30 groups per class against the raw files, 30/30 confirmed in each class. " +
            "Schedule D 6,502 transactions / 25,001 lines, which the engine reads as most likely coincidental: the transaction id is the " +
            "supplier's own invoice number, so two suppliers can share one. The first end-to-end run was CY2024 (44,843 rows). Schedule A found 0 rows over 1p (every row's " +
            "Gross - VatAmount nets to the row itself - Merton does not publish a separate invoice-vs-payment amount " +
            "the way Wokingham does, so Schedule A is structurally near-empty for this council; this is a true " +
            "negative, not a bug). Schedule B (repeated payments) and Schedule D (multi-payee " +
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
            // Session 29: FY2017-18 to FY2019-20 quarterly back-catalogue, from the same page. 11 files; the page
            // lists no file for July to September 2018 (FY2018-19 Q2): not a download we missed.
            new("2017-Q1", "https://images.reading.gov.uk/2020/01/Transactions-over-500-April-to-June-2017.xlsx", SourceFormat.Xlsx),
            new("2017-Q2", "https://images.reading.gov.uk/2020/01/Transactions_over_500_-_July_to_September_2017.xlsx", SourceFormat.Xlsx),
            new("2017-Q3", "https://images.reading.gov.uk/2020/01/Transactions_over_500_-_October_to_December_2017.xlsx", SourceFormat.Xlsx),
            new("2017-Q4", "https://images.reading.gov.uk/2020/01/Transactions_over_500_-_January_to_March_2018.xlsx", SourceFormat.Xlsx),
            new("2018-Q1", "https://images.reading.gov.uk/2020/01/Over_500_April_to_June_2018.xlsx", SourceFormat.Xlsx),
            new("2018-Q3", "https://images.reading.gov.uk/2020/01/Copy_of_Transactions_over_500_-_Oct_to_Dec_2018A.xlsx", SourceFormat.Xlsx),
            new("2018-Q4", "https://images.reading.gov.uk/2020/01/Transactions_over__500_-_January_to_March_2019.xlsx", SourceFormat.Xlsx),
            new("2019-Q1", "https://images.reading.gov.uk/2020/01/April_to_June_2019.xlsx", SourceFormat.Xlsx),
            new("2019-Q2", "https://images.reading.gov.uk/2020/01/Over__500_Jul_to_Sept_2019_Report.xlsx", SourceFormat.Xlsx),
            new("2019-Q3", "https://images.reading.gov.uk/2020/11/Q3-2019-20.xlsx", SourceFormat.Xlsx),
            new("2019-Q4", "https://images.reading.gov.uk/2020/11/Q4-2019-20.xlsx", SourceFormat.Xlsx),
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
            new("2026-04", "https://images.reading.gov.uk/2026/05/Over-500-Spend-April-2026.csv", SourceFormat.Csv),
            new("2026-05", "https://images.reading.gov.uk/2026/06/Over-500-Spend-May-2026.csv", SourceFormat.Csv),
            new("2026-06", "https://images.reading.gov.uk/2026/07/Over-500-Spend-June-2026.csv", SourceFormat.Csv),
            new("2026-07", "https://images.reading.gov.uk/2026/09/Over-500-Spend-July-2026.csv", SourceFormat.Csv),
            new("2026-08", "https://images.reading.gov.uk/2026/09/Over-500-Spend-August-2026.csv", SourceFormat.Csv),
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
                "nine Hexagon venue vouchers, several of them credits). Only Voucher Number is used as the transaction number.",
            "BLANK VOUCHER NUMBERS ON REAL PAYMENTS: 2,621 lines (GBP 206,026,279.04) have a payee and a date but no Voucher Number: 2,034 " +
                "from July 2021 (HMRC remittances, CHAPS and Faster Payments, rents, capital fees; Gateley PLC GBP 3,704,464.99 on 12/08/2025) and " +
                "587 in the files from July-September 2019 to June 2021 (among them inter-authority loans such as GBP 5,009,567.12 to the City & " +
                "County of Swansea and GBP 10,004,246.58 to Essex County Council); 109 of them have the payee \"Redacted\". Each gets its own " +
                "placeholder number. Until 4 October 2026 these lines were left out of this page without a word (an independent audit found it).",
            "The May 2021 workbook is not read: its voucher-number column is labelled \"Purchasing Organisation\" and the damage reaches the first " +
                "data row (4,000 rows, GBP 24,296,795.14). It is left out until it is re-saved from the council's own copy. The July-September 2018 " +
                "quarter file has 36 lines that read \"#N/A\" and nothing else; April 2025 has one amount cell that reads \"#REF!\" (The Welding Shop " +
                "Ltd, 09/04/2025), read as 0.",
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
            "Redaction is rare here: over all loaded files 1,227 of 508,453 rows (0.24%) carry a redacted supplier or transaction id, " +
                "against Wokingham's 1.9% and Merton's 22.5%; they are excluded from the schedules the same way. Three real suppliers whose names contain " +
                "Redactive (Events, Media Group and Publishing, 9 rows) are not redactions, so a plain text search of the payee names for \"redact\" finds 1,236.",
            "\"WOKINGHAM BOROUGH COUNCIL\" appears as a payee in Reading's own files across several months and purposes: Network " +
                "Management/Leasing Costs (£9,058.30, March 2023), Hexagon theatre venue costs, LD Community Services travel " +
                "expenses; ordinary charges between neighbouring councils. \"ABC TRAVEL (WOKINGHAM) LIMITED\" is a company whose name " +
                "contains the place name, not a council.",
            "The largest single \"RBC Refunds Manual Entry\" payee is \"Brighter " +
                "Futures for Children\" (£4.3m/£2.3m/£1.3m/... single rows, all " +
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
                "published £9.139m figure (about £1.16m over; the files do not say why). The rows do not " +
                "reconcile exactly to the one published figure found, so they are shown as published, not explained.",
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
            "Current counts (79 of the 80 listed files; May 2021 is not read, see the notes): 512,489 raw records, 508,453 published rows, " +
            "36 \"#N/A\" lines and the 4,000 rows of May 2021 left out with their reason (raw records against published rows, file by file: " +
            "every record accounted for; the 62 monthly and quarterly CSVs from October 2020 that an independent audit downloaded afresh " +
            "match the published rows and totals to the penny). 1,227 (0.24%) redacted and excluded, 507,226 retained. Schedule A 0 (no net/gross split, inapplicable). " +
            "Schedule B 28,925 groups / 96,444 member rows; random hand-checks of 30 groups per class against " +
            "the raw files, 30/30 confirmed in each of the four classes (run before the unnumbered lines were added, and before computed amounts stored with 17 digits in the quarterly workbooks were read as the 15 digits the sheet shows, which moved the class counts a little: Unclear 26,004 to 25,988, standing-payment catch-up 1,571 to 1,580, standing-payment surplus 1,266 to 1,274, reversed the same day 84 to 83). Schedule D 24 transactions / 73 lines (a " +
            "voucher number shared by more than one pay date or payee; none of the 24 is in the five months of the " +
            "original sample, which is why that sample found none; 14 of the 24 are in the 2017 and 2018 quarterly files). Voucher-number uniqueness " +
            "was checked by hand on one month: 4,595 distinct vouchers, 1 collision, the blank-voucher case. " +
            "Hand-opened 1 Schedule B group against the raw 2021-12 file (vouchers 4619430/4619523, both to " +
            "two private landlords, GBP 682.98, same date/purpose, same Payment Number 264133) - confirmed to " +
            "exist exactly as the schedule describes. Whether it is itself an error is not known (sharing one " +
            "Payment Number is consistent with a deliberate two-voucher rent-scheme payment); reported as published.",
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
                "classified - the credit-note check needs an InvoiceType column to run at all and is therefore structurally " +
                "empty for this council, same \"not a bug\" principle as Wokingham/Merton).",
            "No separate SupplierInvoiceNumber field published - Schedule B stays Unclear for every group here, " +
                "same structural reason as Wokingham/Merton.",
            "File URLs are individually stable (each resolves to a real, distinct .xlsx on " +
                "bracknell-forest.gov.uk/sites/default/files/), unlike Wokingham's dropdown-driven page - confirmed " +
                "by downloading the earliest (March-May 2021) and latest (April-June 2026) listed quarters directly.",
            "Column header is stable across the 5+ years sampled (both the earliest and latest files checked have " +
                "the identical 10-column header: Body Name, Body, Service Area, Service Division, Responsible " +
                "Unit, Expenses Type, Date, TransNo, Amount £, Supplier) - no drift found, unlike Reading's header " +
                "and organisation-name drift.",
            "TransNo is a shared BATCH/PAYMENT-RUN id for Adult Social Care Contracted Services, not a one-" +
                "invoice reference - the same trap as Reading's \"Payment Number\", " +
                "found here on the FIRST real multi-row hand-check rather than assumed clean from a quick look. " +
                "Confirmed real (2021 Mar-May file): trans 30391217 bundles 99 rows across 3 named care agencies " +
                "plus dozens of REDACT PERSONAL INFORMATION individual care-recipient lines; trans 30392544 " +
                "bundles 174 rows. The 2026 Apr-Jun file has even larger examples (trans 30509113: 123 rows, 15 " +
                "distinct suppliers; trans 30511127: 131 rows, 15 suppliers). Schedule D's output for " +
                "this council (519 groups on the full back-catalogue, after redaction exclusion) is DOMINATED by this batch-run artifact, not genuine " +
                "one-invoice/multi-payee anomalies - must be stated plainly on any page that shows Schedule D for " +
                "this council, the same honesty standard as Reading's own Payment Number caveat.",
            "High redaction rate: in the first two-quarter sample, 5,389 of 14,198 raw rows (38%) carried \"REDACT PERSONAL INFORMATION\" as the " +
                "supplier/transaction identifier (adult social care and children's safeguarding placements); over the full 22 quarters it is " +
                "32,074 of 161,336 (19.9%) - " +
                "higher than Wokingham (~2%) or Reading (~0.2%), close to Merton's ~22% - consistent with this " +
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
            "re3 share in Wokingham's files (an earlier note here said Wokingham's data did not show the re3 pattern; that was wrong, " +
                "because it searched only for the strings \"RE3\"/\"FCC\"): Wokingham publishes its re3 share as payments to Reading Borough Council " +
                "under Cost Centre Area \"Waste PFI Contract\" (Description \"TPP - Other Local Authorities\"): " +
                "67 transactions, about £8.2m-£11.4m net per financial year across all six years, 9-13 " +
                "payments a year, plus 13 negative \"PFI Credits\" lines FY2020-21 to FY2023-24 (about " +
                "-£2.6m net) and one separate \"Interest Payable on PFI Unitary Payments\" line (FY2020-21, " +
                "£1,026,474.96). The shape differs from Bracknell Forest's (a growing quarterly recharge) in " +
                "cadence and lumpiness, not in existence. All 1,139 Wokingham to Reading rows were opened by hand " +
                "against the raw files and confirmed.",
        },
        LastChecked: "2026-10-03 (the full list of files)",
        VerificationNote:
            "All 22 quarterly files (December 2020 to April-June 2026, every link fetched from the council's own page). 161,336 raw " +
            "rows, all published (raw records against published rows, file by file: 0 left out); 32,074 (19.9%) carry a redacted " +
            "supplier or transaction id and are excluded, 129,262 retained (closer to Merton's about 22% than to Wokingham's about 2% or " +
            "Reading's about 0.2%). Schedule A does not apply (no net/gross split, see the notes). Schedule B: 1,969 groups, 5,148 member " +
            "rows, all Unclear (no supplier invoice number published) - a random sample of 30 groups, 30/30 confirmed " +
            "against the raw files (each line found, Net, Gross and description read again). Schedule D: 519 groups (51,575 member lines), " +
            "dominated by the batch-run TransNo noted above, all 519 opened against the raw files and confirmed to exist exactly as published. " +
            "Payments to other councils: Bracknell Forest to Wokingham Borough Council 147 rows / £1,196,109.68; Bracknell Forest to Reading " +
            "Borough Council 248 rows / £42,926,894.99, weighted by a few very large \"Waste Disposal\" and \"Contracted Services\" payments " +
            "(e.g. £560,250.23 and £582,166.79 single lines), consistent with re3, the joint waste partnership of Reading, Wokingham and " +
            "Bracknell Forest (Joint Waste Disposal Board, established 1999, FCC Environment contracted since 2006; sources: re3's own " +
            "\"About us\" page, local press on its 25th anniversary, its Waste Strategy document). Both populations (147 and 248 rows) were " +
            "opened against Bracknell Forest's own files: 147/147 and 248/248 confirmed.",
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
            "recent years as .xlsx, older years (2022-09 and earlier) as .csv. Open the page in an ordinary browser " +
            "(it refuses automated requests; the file links themselves download normally). Download the month(s) you want and drop " +
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
            "NO TRANSACTION NUMBER: the header is exactly \"Service | Expenditure category | Narrative | Date | Net amount | Supplier name\" " +
                "(the same in every file). The scanner gives every row its own row number so the checks can run; the invoice-amount " +
                "and shared-transaction-number checks are therefore empty by construction, not a finding about this council's spending. " +
                "The repeated-payment check (same supplier, amount, description and pay date) does not depend on a number and stays meaningful.",
            "One signed \"Net amount\" column, no VAT split and no separate gross figure; the council's own column name says \"Net\" " +
                "although it is the only amount published (several councils use \"Net\" to mean net of internal recharges).",
            "Dates are Excel serial numbers with a time of day (e.g. 45404.752629664355); the date part is used.",
            "The August 2026 workbook (\"Over500_2026_P5_WP_Final.xlsx\") is left out of every number here: its first sheet's header reads " +
                "\"* This sheet is manipulated by Excelerator and should not be changed by hand\" and it holds 9 rows that are not payments. " +
                "It can be included once the council's own copy is re-saved from Excel.",
            "3 of the 29 files (2025-12, 2026-06, 2026-07) have one fully blank row before the real header; the scanner skips it. " +
                "Six files from December 2025 to May 2026 end in blank rows (1,113 in all, 577 in February 2026 and 531 in April 2026); they hold nothing and are " +
                "counted as blank rows, not payments.",
            "Only April 2024 to July 2026 are loaded; the council's page lists monthly files back to April 2012.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "28 of 29 downloaded months (2024-04 through 2026-07; 2026-08 left out, see the notes): " +
            "91,784 mapped rows, all published (raw records against published rows, file by file: 0 left out apart from blank rows), " +
            "16,491 (18.0%) redacted and excluded (between Reading's about 0.2% and Merton's about 22%), 75,293 retained. Schedule A " +
            "0 rows (no VAT/gross split, as for Reading and Bracknell Forest). " +
            "Schedule D empty by construction (0 groups: the scanner's per-row number never repeats), not a finding about this council. Schedule B: 5,013 groups, " +
            "14,927 member rows, £46,676,361.89, all Unclear (no supplier invoice number published, same " +
            "structural reason as every other council) - a random sample of 30 groups, 30/30 confirmed " +
            "against the raw files (each line found by position, Net, Gross and description read again).",
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
                "spend(T)); fixed by named renames.",
            "2025-04, 2025-05 and 2025-06: the header names an 'Irrecoverable VAT' column that the data rows do not " +
                "carry, so every later cell sits one place left (Purpose of spend then reads a classification code " +
                "such as 270000). Detected from the data and realigned by the scanner.",
            "FILES OVERLAP (measured): the file named June 2025 holds 1,067 rows PAID IN JUNE 2024 " +
                "(1,002 of them the same payment lines as the 2024-25 annual file) and only 100 rows paid in June 2025, " +
                "against about 2,700 in a normal month, so June 2025 payments are essentially NOT PUBLISHED; 2025-04 " +
                "and 2025-05 overlap by 381 keys, 2025-11 and 2025-12 by 61. Rows already published in an earlier " +
                "file are left out (1,703 of 70,349 mapped rows); without that the repeated-payment check would count the same payment twice.",
            "Some payment lines carry no transaction number (84 lines in April and May 2025, 48 of them with a redacted payee); each gets its own " +
                "placeholder number so it is published and checked like any other line (until 4 October 2026 they were left out). 405 lines hold " +
                "no number, no payee and no date (two total lines, GBP 15,669,506.90 in June 2025 and GBP 20,631,564.74 in August 2025, and lines " +
                "with only the organisation name) and are not payments.",
            "Transaction number: council-assigned and normally one payment line or invoice, but CHAPS payments are " +
                "booked as a 'Sundry Bacs Supplier' line plus the real payee's line and its reversal under ONE number " +
                "(Schedule D's 32 groups are almost all this, plus refunds, DWP CRU claims and one batch of housing " +
                "grants to 5 named people), and 5xxxxxx numbers are batch/payment-run ids (trans 5095002 = 186 lines " +
                "of one agency supplier). Labelled CouncilWideUnique with that caveat; read Schedule D as bookkeeping " +
                "mechanics unless a group shows otherwise.",
            "Gross meaning confirmed PerLineAmount: transaction 20307726 has 40 lines to one supplier with 40 " +
                "different amounts, so a group's amounts are separate charges, not a repeated total.",
            "Redaction: 4,545 of 68,646 published rows (6.6%) carry a redacted supplier or id (REDACTED PERSONAL DATA) " +
                "and are excluded from the schedules; 8 rows have only digits in the supplier-name column (an id).",
            "Data rows carry more cells than the header names (procurement-classification code/name pairs under blank " +
                "headers); these and every other unmapped column are kept in the export's OtherColumns as ColN.",
            "Files are Windows-1252 (0xB4 acute accent in Children's Services); the downloads need an ordinary " +
                "browser user-agent. Older years than 2023-24 were not found on the council's page.",
            "The ledger heading 'External Interest Payable' sits over payment-software, card-processing and brokerage " +
                "lines (ClearAccept, Adelante, BGC, GFI, Tradition): there is NO borrowing/loan/PWLB payment line in " +
                "this file at all, so RBWM's own debt service is not visible here.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "17 files (2023-24 and 2024-25 annual; monthly April 2025 to June 2026), 70,754 raw records: 70,349 mapped rows, 1,703 left out " +
            "as already published in an earlier file, 405 not payment lines (see the notes), leaving 68,646 published rows: 64,101 retained, " +
            "4,545 redacted and excluded (raw records against published rows, file by file: every record accounted for). Schedule A 0 rows " +
            "(no VAT split), Schedule B 3,665 groups / 10,865 member rows, all Unclear; a random hand-check of 30 groups, 30/30 confirmed against " +
            "the raw files (before the 28 unnumbered lines were added); Schedule D 32 groups / " +
            "92 lines, every group read (mostly CHAPS booking mechanics). Every RBWM payment row to Optalis (38 + 26) " +
            "and to Achieving for Children (457) hand-opened: 521/521 confirmed.",
        FoiContactEmail: null);

    private static List<string> Months(int y0, int m0, int y1, int m1, params string[] except)
    {
        var tags = new List<string>();
        for (int ym = y0 * 12 + (m0 - 1); ym <= y1 * 12 + (m1 - 1); ym++)
        {
            string t = $"{ym / 12:0000}-{ym % 12 + 1:00}";
            if (!except.Contains(t)) tags.Add(t);
        }
        return tags;
    }

    /// <summary>Council #7 (Session 30, the first big city). A single download from the Birmingham City Observatory,
    /// split by month for the engine (see CityOnboard.PrepBirmingham). Seven published fields, no document reference.</summary>
    public static readonly SupportedCouncil Birmingham = new(
        Name: "Birmingham City Council",
        TransparencyPageUrl: "https://cityobservatory.birmingham.gov.uk/explore/assets/payments-to-suppliers-over-gbp500/",
        HowToFindTheFile:
            "On the Birmingham City Observatory, open \"Payments to Suppliers over £500\" and use Export, CSV (or the " +
            "dataset's records API). It is ONE file for the whole published period, not one per month; drop it onto this " +
            "page and it is split by the month of the payment date.",
        Mapping: new ColumnMapping(
            TransactionId: "RowRef", // synthetic, see KnownQuirks: the data has no document reference of any kind
            Supplier: "beneficiary",
            PayDate: "date",
            Description: "merchant_category",
            ServiceArea: "department",
            Net: null,
            Gross: "amount", // the only amount published; the dataset text says amounts include VAT
            VatAmount: null,
            VatType: null,
            CostCentreArea: "summary"), // a business-unit label such as "Housing Management BU"
        IdScope: TransactionIdScope.CouncilWideUnique, // moot: the synthetic RowRef is unique per row
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2024, 7, 2026, 7)
            .Select(t => new CouncilYearFile(t, "https://cityobservatory.birmingham.gov.uk/explore/assets/payments-to-suppliers-over-gbp500/", SourceFormat.Csv))
            .ToList(),
        KnownQuirks: new List<string>
        {
            "ONE download holds every month (records API v2.1, 247,785 records, dataset last modified 2026-09-22); the " +
                "page text says \"the last 12 months\" but the data holds 25 months, July 2024 to July 2026. It is split " +
                "by the month of `date` into per-month files; the record count of the pieces equals the download.",
            "NO transaction or document reference: the page text lists a \"unique document reference\", the seven " +
                "published fields are date, department, beneficiary, summary, amount, merchant_category and " +
                "supplier_number. A synthetic per-row RowRef is added (as for West Berkshire), so Schedule A and " +
                "Schedule D are empty BY CONSTRUCTION, not as a finding. Schedule B (same supplier, amount, " +
                "description and date repeated) is the one schedule that stays meaningful.",
            "One signed `amount` column, no VAT split. The page says amounts are \"including VAT\" and \"payments made " +
                "in the period\", not necessarily invoices dated in the period.",
            "ACCRUAL-CODED ROWS: 48,162 of 247,785 rows (19%, GBP 1.29bn of GBP 3.87bn signed total) carry the " +
                "merchant category \"Accounts payable PO  Accrual  - GRNI\" (goods received, not invoiced). The file does " +
                "not say whether those are payments or accounting accruals; they are kept as published and counted " +
                "separately in the reconciliation so a visitor can see the total with and without them.",
            "Redaction: 7,681 rows carry the beneficiary \"REDACTED - PERSONAL DATA\" (mostly adult care packages); the " +
                "engine excludes them from schedules and counts them in the export.",
            "Directorate names change inside the period (\"Adults Social Care Directorate\" and \"Adult Social Care & Health " +
                "Directorate\"; \"Places, Prosperity & Sustainability\" and \"Place, Prosperity & Sustainability\"), so a " +
                "per-directorate total must merge both spellings.",
            "422 rows are negative (credits or reversals) and are kept with their sign.",
            "Birmingham's published history starts at July 2024 on this dataset, so the only complete financial year in " +
                "the file is 2025-26, for which no Revenue Outturn is published yet: no declared-spend comparison year yet.",
            "The ledger's debt keywords hit labels, not borrowing: 11 rows read \"Treasury Management Loans\" (a business-unit " +
                "name over fees to Arlingclose, MUFG and Equiniti) or \"LT Assets - New Revenue Loans\" (a category over two " +
                "150,000-pound property-consultancy lines). There is NO PWLB, bank or inter-authority loan line in this file, " +
                "so Birmingham's own debt service is not visible here.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "One download of 247,785 records split into 25 monthly files (July 2024 to July 2026; pieces sum to the download); " +
            "247,785 mapped rows, 7,681 redacted and excluded (3.1%), 240,104 retained: retained + redacted = export rows. " +
            "Schedule A 0 (no VAT split) and Schedule D 0 (synthetic row ids), both by construction. Schedule B 1,355 groups / 2,909 member " +
            "rows / GBP 3,607,709.56 (extra copies GBP 1.88m, 0.05% of the signed file total GBP 3.87bn): Unclear 1,285, standing-payment " +
            "surplus 52, catch-up 18; random hand-checks of 30 groups: 30/30 and 30/30, the 18 catch-up groups 18/18, against the raw " +
            "download. Same-content repeats across files: 0. Largest groups are two same-day " +
            "lines for one supplier under two business-unit labels in the accrual category (for example The Breastfeeding Network, " +
            "two lines of 57,000.00 on 26 August 2025).",
        FoiContactEmail: null);

    /// <summary>Council #8 (Session 30). Leeds publishes one CSV per month on Data Mill North, with a transaction
    /// number, a payment date, purchasing-card flag and capital/revenue flag.</summary>
    public static readonly SupportedCouncil Leeds = new(
        Name: "Leeds City Council",
        TransparencyPageUrl: "https://datamillnorth.org/dataset/council-spending-2gpp0",
        HowToFindTheFile:
            "On Data Mill North open \"Council spending\" (Leeds City Council). Each month has its own CSV named " +
            "spending_YYYY_MM.csv next to a PDF of the same month; pick the CSVs. Leeds publishes every transaction of " +
            "250 pounds or more and every purchasing-card transaction. Download each month and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Beneficiary Name",
            PayDate: "Payment Date",
            Description: "Purpose",
            ServiceArea: "Service Division Label",
            Net: null,
            Gross: "Amount", // the only amount; "Irrecoverable VAT Amount" is a different quantity and left unmapped
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Organisational Unit"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2017, 4, 2026, 6)
            .Select(t => new CouncilYearFile(t, "https://datamillnorth.org/dataset/council-spending-2gpp0", SourceFormat.Csv))
            .ToList(),
        KnownQuirks: new List<string>
        {
            "One CSV per month on Data Mill North (dataset \"Council spending\"), each next to a PDF of the same month; the " +
                "per-file download links carry a hash and change, so Years[].Url is the dataset page, not a file link. " +
                "Months held: April 2017 to June 2026, 111 files, tagged by the month of the file's own Effective Date. " +
                "The dataset page lists files back to 2010 but names two files \"spending_2019_12.csv\": the first one " +
                "(held here as 2020-03) has Effective Date 31/03/2020, transaction numbers 2003-nnnnn and payments " +
                "dated November 2019 to March 2020, i.e. it is March 2020, and no file is named for March 2020; the " +
                "second (held as 2019-12) is December 2019.",
            "Effective Date typos inside the files (payment dates and the rest of each file are right): the file named " +
                "2019-03 carries Effective Date 31/03/2018 and transaction numbers 1803-nnnnn on payments dated October " +
                "2018 to March 2019, and also 979 completely blank rows; the files named 2022-08 and 2022-09 carry " +
                "Effective Dates 30/09/2022 and 31/10/2022. Many files end with a blank row (1,090 blank rows " +
                "across the files, 979 of them in the 2019-03 file); 1,080 are entirely empty, 9 carry only an organisation " +
                "code, effective date or Procurement Card flag and no payee or amount, and one (2018-09) carries a footer total of 93,408,532.37 that equals the sum of " +
                "that file's payment rows. No payment is lost to the blank transaction number.",
            "JUNE 2025 IS MOSTLY UNPUBLISHED: the file named June 2025 holds 13,806 rows against about 25,000 to 30,000 " +
                "in a normal month, and only 6,108 of them are paid in June (a normal month holds about 15,000 to " +
                "24,000 of its own); payments dated June 2025 show up in the July 2025 file as well, so the June " +
                "total is shown as published, not as complete.",
            "Threshold: every transaction of 250 pounds or more, and every purchasing-card transaction (flag column " +
                "\"Procurement Card\", Yes on about a quarter of the rows), so row counts per month (about 26,000) are not " +
                "comparable with 500-pound councils.",
            "ONE signed \"Amount\" column. \"Irrecoverable VAT Amount\" is a separate quantity (VAT the council cannot " +
                "reclaim), not a gross-less-net split, so it is left unmapped and kept in OtherColumns; there is no VAT " +
                "split and Schedule A is empty by construction. \"Capital Or Revenue\" (C or R) is kept in OtherColumns.",
            "Transaction Number looks like yymm-nnnnn (2404-00001) and restarts every month: one number per payment line, " +
                "never repeated inside a file, so Schedule D is empty by construction and Schedule B (same supplier, " +
                "amount, description, date) is the schedule that stays meaningful.",
            "The payee column is spelled \"Benificiary Name\" in the 36 files from April 2017 to March 2020 and \"Beneficiary " +
                "Name\" afterwards; the scanner renames the header cell so the mapping finds it.",
            "Files hold payments dated in earlier months (a small share of each month's rows; the run lists the files where " +
                "more than 5% are paid in another month).",
            "Redaction: payees are replaced by \"REDACTED PERSONAL DATA\"; the engine excludes those rows from the " +
                "schedules and counts them in the export (191,131 rows, 7.1%).",
            "SOME LINES APPEAR IN TWO FILES: mostly from August 2022 to May 2023, the purchasing-card lines of one month are " +
                "published again in the next month's file under a new transaction number and effective date, with smaller " +
                "blocks in 2017-09 (177 rows, including a 962,000-pound land payment on 8 September 2017 present in both the " +
                "August and September 2017 files) and a few later months (the 2022-12 file holds 8,556 lines already in 2022-11; 2023-03 holds 7,144 already in " +
                "2023-02; every one of the 2022-12 and 2023-03 matches is a Procurement Card = Yes line; in December 2022 the purpose text was " +
                "re-capitalised, e.g. \"And\" for \"and\", so the comparison ignores letter case). Found by content " +
                "(supplier, amount, payment date, description, service), because the per-file numbers cannot reveal it. Whole " +
                "population: 36,032 of 2,510,140 retained rows (1.44%, GBP 15.6m of absolute value), 26,402 rows / GBP 9.24m in " +
                "FY2022-23 and 8,196 rows / GBP 3.23m in FY2023-24. Leeds is not alone: 10 of the other 27 councils have such rows, 20,343 in all " +
                "(Cornwall 7,901, Stockport 6,679, Coventry 4,755, Liverpool 542). The " +
                "file cannot say whether the card spend was posted twice or published twice.",
            "No borrowing, loan or PWLB payment line: the debt ledger's 43 keyword hits are labels such as \"Interest Paid\" " +
                "(three 10,000-pound lines to Basis Yorkshire), \"Provision For Doubtful Debts\" and \"Interest & Premiums From " +
                "Borrowers\", not Leeds' own debt service, which is not visible in this file.",
            "Business-rate settlement lines are in the file as ordinary payments, with signs as published (descriptions " +
                "\"Business Rates Tariffs\", signed total 917.9m, and \"NNDR Income\", signed total 837.1m, over the nine years; " +
                "in 2017 the payee is The Secretary Of State): pass-through between the council and central government, " +
                "not spending on services.",
        },
        LastChecked: "2026-10-03",
        VerificationNote:
            "111 monthly files (April 2017 to June 2026), 2,702,361 raw records, 1,090 blank or footer rows dropped by the " +
            "mapping (none holds a payment), 2,701,271 mapped rows, 191,131 redacted and excluded, 2,510,140 retained: " +
            "retained + redacted = export rows. Schedule A 0 (no VAT split) and Schedule D 0 (per-file sequential numbers), both by " +
            "construction. Overlap by transaction number: 0 keys in two files (numbers restart each month, so this cannot " +
            "see a republication); by content: 31,421 rows, see the notes below. Schedule B 171,903 groups / 527,865 member rows / " +
            "GBP 525.67m (extra copies GBP 337.2m): Unclear 156,487, standing-payment catch-up 11,219, surplus 3,836, " +
            "reversed the same day 361; random hand-checks of 30 groups: 30/30 in each of the first three classes and every one of the " +
            "361 reversed groups re-derived from the export and 361/361 confirmed against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #9 (Session 31). Sheffield publishes on Data Mill North, one report per month from April 2019 (blocks of
    /// two to four months from 2017 to March 2019), with NO transaction number: a synthetic per-row id is added.</summary>
    public static readonly SupportedCouncil Sheffield = new(
        Name: "Sheffield City Council",
        TransparencyPageUrl: "https://datamillnorth.org/dataset/emd0m/council-spend-over-pound250",
        HowToFindTheFile:
            "On Data Mill North open \"Council spend over £250\" (Sheffield City Council). Each month has a report named " +
            "\"Spend over £250 - <month> <year>\" (an Excel workbook; older months are CSV and some are quarterly blocks); the " +
            "same page also lists credit-card, procurement-card, Precision Pay and grant reports, which are different datasets: " +
            "pick only the \"Spend over £250\" ones. Download each and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "RowRef", // synthetic, see KnownQuirks: no transaction or document reference in 2017-04 onwards
            Supplier: "Supplier",
            PayDate: "Payment Date", // Certified Date from April 2019, GL Date before (CityFixups.SheffieldHeader renames both)
            Description: "Object Code Description", // the nature of the spend, e.g. "ADVERTISING SERVICES INVOICED"
            ServiceArea: "Portfolio", // the directorate, e.g. PLACE, PEOPLE
            Net: null,
            Gross: "Value", // the only amount; the files do not say whether it includes VAT
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Org Code Description"),
        IdScope: TransactionIdScope.CouncilWideUnique, // moot: the synthetic RowRef is unique per row
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2017, 1, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://datamillnorth.org/dataset/emd0m/council-spend-over-pound250", SourceFormat.Csv))
            .ToList(),
        KnownQuirks: new List<string>
        {
            "Data Mill North's \"Council spend over £250\" is Sheffield's (publisher Sheffield City Council; CKAN id emd0m, 204 resources) and mixes " +
                "four datasets: only the \"Spend over £250\" reports are used (101 raw files: 29 CSV, 30 xlsx, 42 xlsm), not the credit-card, " +
                "procurement-card, Precision Pay and grant reports on the same page. January 2017 to August 2026, 116 payment months, none missing; " +
                "files for 2011 to 2016 exist and are not used. (A search summary once named this dataset as Leeds'; it is not.)",
            "IRREGULAR PUBLICATION: January to March 2017, April to July 2017, August to September 2017, October to December 2017, each quarter of 2018 " +
                "and January to March 2019 are single block files, split here by each row's own date into months; from April 2019 there is one file a " +
                "month (April 2019 on). A month is tagged by the month its dates mostly fall in. Three months are offered both as CSV and xlsx " +
                "(2019-04, 2020-02, 2020-04); the pairs hold the same rows and the same total to the penny and the CSV is kept.",
            "FIVE HEADER LAYOUTS: January to July 2017 (\"Organisational Unit, Expense Code, GL Date, Document No., Invoice No., Invoiced Value, Supplier " +
                "Name\"), August 2017 to March 2019 (\"GL Date, Invoice No., Value, Supplier\", one file's first column is \"Company\"), then \"Certified " +
                "Date, Supplier Reference, Value, Supplier\" with misspellings (\"Objective Code\", \"Suplier Number\", \"Category & Description\"); " +
                "the scanner gives one standard set and trims every cell (the 2017 files pad names with spaces). From April 2019 the date is " +
                "the CERTIFIED date (when the invoice was certified for payment), before it the GL date; neither need be the day cash left.",
            "The Excel macro workbooks (.xlsm: November 2020 and January 2021 to November 2023) put a cover sheet and notes sheets before the data; the data sheet is the " +
                "one with the most rows. Trailing blank rows are common (31,237 dropped in all); the preparation step reconciles raw rows = written + blank " +
                "and the signed total before and after. xlsx amounts that arrive as binary floats are written to the penny.",
            "NO TRANSACTION NUMBER from April 2017 (January to July 2017 files carry a Document No., kept in OtherColumns): a synthetic per-row id is added, so " +
                "Schedules A and D are empty BY CONSTRUCTION and Schedule B is the schedule that stays meaningful. One signed amount column (\"£1,080.00\"), no VAT split.",
            "Row counts per month changed: the January to July 2017 files hold 6,000 to 9,000 rows a month, from August 2017 about 12,000 to 20,000, from April " +
                "2019 mostly 17,000 to 43,000 (April 2026: 43,623; September 2024 only 12,520); the file does not say whether the threshold or the scope changed. Sheffield 2017-18 and 2018-19 therefore " +
                "fail the budget test's coverage rule.",
            "Redaction: payees are replaced by \"REDACTED PERSONAL DATA\"; those rows are excluded from the schedules and counted in the export " +
                "(521,473 rows, 19.8%), the largest share among the cities.",
            "Care and placement lines have no client reference, so many identical fees to one provider on one day (Sandford House 143 lines of 4,687.44) are " +
                "Schedule B \"Unclear\" groups: the file shows the repeat, it cannot say it is an error.",
            "LEDGER MIGRATION: 931 lines dated 6 August 2017 carry the description \"AP MIGRATION CONTROL ACCOUNT\" on the BALANCE SHEET cost centre (the day " +
                "the payables ledger was converted), among them seven lines of 1,985,333.00 to Sheffield City Region Combined Authority and seven of 1,762,744.00 " +
                "to South Yorkshire Police. Schedule B groups whose every line carries the label are classed MigrationControlLabel (37 groups, 155 rows, " +
                "GBP 28.6m of extra copies); the file does not say whether cash left under it. Birmingham carries a business unit \"Migration Control\" " +
                "(492 rows) in no repeated group.",
            "Debt lines: the ledger's 349 entries are mostly labels (\"Bank charges on loans\", \"Longer term bonds - Consolidated Loans Fund\", \"Regional " +
                "Homes Board loan redemptions\", \"EP loans Bradford\"); \"Debt collection agencies\" (the supplier category of UKSearch, Capita, Newlyn) " +
                "and \"Judgement debts and fines\" are not borrowing and are excluded.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "101 raw files read, 98 kept (3 xlsx twins identical to their CSV), 2,669,336 data rows = 2,638,099 written + 31,237 blank, signed Value total " +
            "GBP 9,274,330,308.08 equal before and after; 116 months. 300 of 300 exported rows found by supplier, date and amount in the original downloads " +
            "(random sample). 2,638,099 mapped rows, 521,473 redacted excluded, 2,116,626 retained: retained + redacted = export rows. Schedule A 0 and " +
            "Schedule D 0, both by construction. Schedule B 169,840 groups / 712,593 member rows / GBP 975.82m (extra copies GBP 735.5m, 7.9% of the " +
            "signed total): Unclear 157,099, standing-payment surplus 5,210, catch-up 4,920, reversed the same day 2,574, migration control label 37; " +
            "random hand-checks of 30 groups: 30/30 in each of the five classes against the monthly files. Same-content " +
            "repeats across files: 1 row (GBP 72,737).",
        FoiContactEmail: null);

    /// <summary>The Bradford file tags: one annual file (FY2020-21), sixteen quarterly files (2021-04_06 to 2025-01_03), then monthly
    /// files from 2025-04. See CityPrep.BradfordTag; a tag is the period in the file's own name.</summary>
    private static List<string> BradfordTags()
    {
        var tags = new List<string> { "FY2020-21" };
        for (int ym = 2021 * 12 + 3; ym <= 2025 * 12 + 0; ym += 3)
            tags.Add($"{ym / 12:0000}-{ym % 12 + 1:00}_{ym % 12 + 3:00}");
        tags.AddRange(Months(2025, 4, 2026, 8));
        return tags;
    }

    /// <summary>Council #10 (Session 31). Bradford Data Hub: an annual file, quarterly files, then monthly files; real transaction
    /// numbers; one amount column labelled net.</summary>
    public static readonly SupportedCouncil Bradford = new(
        Name: "Bradford Metropolitan District Council",
        TransparencyPageUrl: "https://datahub.bradford.gov.uk/datasets/finance/bradford-council-expenditure-greater-than-500/",
        HowToFindTheFile:
            "On the Bradford Data Hub open \"Bradford Council expenditure greater than £500\" and download every CSV (each period " +
            "is offered as .xlsx and .csv: take the CSV). Files are one for 2020-21, then quarterly to March 2025, then monthly. " +
            "Drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Expenditure Description",
            ServiceArea: "Service Label",
            Net: "Net Amount £",
            Gross: "Net Amount £", // the only amount; labelled net, and "Irrecoverable VAT" is a separate column kept in OtherColumns
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: BradfordTags().Select(t => new CouncilYearFile(t, "https://datahub.bradford.gov.uk/datasets/finance/bradford-council-expenditure-greater-than-500/", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "34 CSV files on the Bradford Data Hub, named by period: one annual file (\"Payments Data 2020-2021 FINAL\", 107,490 rows, April 2020 " +
                "to March 2021), sixteen quarterly files (\"Data 04.2021 to 06.2021\" to \"Data 01.2025 to 03.2025\") and monthly files from " +
                "April 2025 to August 2026. Each period is offered as .xlsx and .csv; the CSV is used. December 2025 is offered a second time as " +
                "\"01.12.2025 to 31.12.2025.xlsx\" (not used). Nothing earlier than April 2020 is on the page. Files are tagged by the period in " +
                "their own name (FY2020-21, 2021-04_06, 2025-04), not re-split.",
            "FOUR MONTHLY FILES HAVE NO PAYEE NAMES: 2025-04, 2025-05, 2026-02 and the file named August 2025 publish the \"Supplier Name\" column " +
                "empty on every row (68,646 rows, 7.5% of the council's rows); the Supplier Number is filled. Left blank, unrelated payees would " +
                "become one \"payee\" with the same amount (groups of 500+ lines), so such a row is labelled \"(no name published) N01651\" " +
                "using the published number as the payee identity (no name taken from any other file).",
            "THE FILE NAMED AUGUST 2025 REPEATS THE JULY FILE: \"Data 07.2025 to 07.2025\" holds 34,604 rows spanning July, August and September " +
                "2025 (14,077 / 10,156 / 10,371 by payment date) and \"Data 08.2025 to 08.2025\" holds the same rows (34,600 identical transaction " +
                "number, amount and payment date keys) with the payee name empty; \"Data 09.2025 to 09.2025\" then lists September again " +
                "(10,655 rows). The page's August 2025 file is therefore not August's payments; there is no separate August file. Found by " +
                "transaction number without the payee name (the run's Step 6 second key), which the name-based cross-file scan cannot see.",
            "One amount column, \"Net Amount £\" (labelled net), mapped as both Net and Gross; \"Irrecoverable VAT\" is a separate quantity kept in " +
                "OtherColumns, so there is no VAT split and Schedule A is empty by construction.",
            "Four header layouts (annual file; quarterlies; 2025 files with \"SupplierName\"; mid-2026 files where \"Expenditure Category\" holds the " +
                "CODE, \"Expenditure Category Description\" the text, and both \"SupplierName\" and \"Supplier Name\" exist: in the mid-2026 files one of them is empty except in August 2026, where \"SupplierName\" reads REDACTED PERSONAL DATA on 229 childcare-voucher rows and \"Supplier Name\" holds a personal name on the same rows): " +
                "the scanner gives one standard set and puts the standard payee name on the column that holds names. A row is treated as redacted when EITHER name column carries a redaction marker, and then both name columns are replaced by the marker, so those 229 names are not published. Payment dates " +
                "come in three forms (13/03/2025 in the annual file, 2021-04-13 in the quarterlies, 20250313 in the Jan-Mar 2025 file); " +
                "all parse.",
            "Blank records: 15,895 empty lines across the files (none carries data); mapped rows 909,957 = raw 925,852 less these. Redaction: payees " +
                "are replaced by REDACTED text; those rows are excluded from the schedules and counted in the export (17,553 rows, 1.9%, of which 229 are the August 2026 rows redacted in one of the two name columns).",
            "The Transaction Number repeats across lines of one transaction (2,128 of 905,105 file-and-number groups have more than one line) and " +
                "is council-wide; two transactions carry more than one pay date, so Schedule D has 2 transactions.",
            "Payee names carry spelling variants of the same body (\"NHS WEST YORKS INTERGRATED CARE\" as published, \"BRADFORD DISTRICT CARE NHS " +
                "FOUNDATI\" cut at 35 characters in some files); names are shown as published.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "34 files, 925,852 raw records, 15,895 blank lines dropped, 909,957 mapped rows, 17,553 redacted rows excluded, 892,404 retained: " +
            "retained + redacted = export rows. Schedule A 0 (no VAT split) and Schedule D 2 transactions. Schedule B 71,798 groups / 302,839 member " +
            "rows / GBP 543.28m: Unclear 70,671, standing-payment surplus 145, catch-up 619, reversed the same day 363; random hand-checks of 30 groups: " +
            "30/30 in each class against the raw files. Same-content repeats across files: 44,076 rows, " +
            "all 33,928 retained rows of the file named August 2025 and 10,148 of the 10,423 retained rows of September 2025 are already in the file named July 2025 (the pay date is compared as a day because the files print it two ways, and the name-less August file is matched on the Supplier Number).",
        FoiContactEmail: null);

    /// <summary>Council #11 (Session 31). Liverpool: one report a month from its own site (SAP document numbers, a posting date and
    /// one actual-value column); xlsx files with a few CSVs.</summary>
    public static readonly SupportedCouncil Liverpool = new(
        Name: "Liverpool City Council",
        TransparencyPageUrl: "https://liverpool.gov.uk/council/spending-and-performance/transparency-in-local-government",
        HowToFindTheFile:
            "On Liverpool City Council's \"Transparency in local government\" page every month's \"over £500\" report is listed as an Excel " +
            "workbook (a few months as CSV), next to a PDF of the same report; older months are on the archive page linked from it. " +
            "Download each workbook or CSV and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "SAP Document Number",
            Supplier: "Vendor",
            PayDate: "Posting Date", // the posting date of the document, not necessarily the day it was paid
            Description: "Description",
            ServiceArea: "Service Area",
            Net: null,
            Gross: "Actual Value",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Expense Type"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2023, 1, 2026, 6)
            .Select(t => new CouncilYearFile(t, "https://liverpool.gov.uk/council/spending-and-performance/transparency-in-local-government", SourceFormat.Csv))
            .ToList(),
        KnownQuirks: new List<string>
        {
            "42 monthly reports, January 2023 to June 2026, from Liverpool's own pages (the current page and its archive); 35 Excel workbooks and 7 " +
                "CSVs. Data Mill North holds only January to June 2025 (6 CSVs); nothing before January 2023 is on the council's pages. Each " +
                "file is tagged by the month its posting dates mostly fall in.",
            "THE DATE IS A POSTING DATE, not a payment date (column \"Posting Date\"): 74.4% of the rows of a file are posted in its own month and the " +
                "rest in earlier months (a 2023-02 file holds postings back to May 2021), so a month's file is not that month's payments.",
            "The six CSVs for January to June 2025 print the date as \"06/08/2024 00:00\" (an Excel workbook shows a date serial). Read the " +
                "wrong way a date such as that is June 8, silently, for every day of 12 or less; the engine now knows the form, and the " +
                "preparation step writes dd/MM/yyyy.",
            "One signed amount column (\"Actual Value\", written \"£15,499.00\" or \"-£640.00\"), no VAT split, so Schedule A is empty by construction. " +
                "The SAP Document Number repeats across the lines of one document (442 of 546,715 file-and-number groups have more than one line) " +
                "and 4 documents carry more than one payee or date (Schedule D).",
            "Redaction: payees are replaced by REDACTED text; those rows are excluded from the schedules and counted in the export (48,489 rows, 8.9%). Redactive Events Ltd (9 rows) is a real supplier, not a redaction, so a plain text search of the payee names for \"redact\" finds 48,498.",
            "SOME LINES APPEAR IN TWO FILES: by document number, amount and posting date 128 keys sit in more than one file (31 between 2023-04 and 2023-06, " +
                "15 between 2024-06 and 2024-08, 10 between 2024-12 and 2025-01); by content (supplier, amount, date, description, service) 542 rows " +
                "(GBP 5.06m of absolute value) are also in an earlier file. The file cannot say whether a line was posted twice or published twice.",
            "Debt lines are real here: 19 \"PWLB\" lines and 59 \"Temporary Loans\" lines paid to other councils sit under the service \"Asset Management Restatement " +
                "Account\" (Liverpool's own heading); \"Debt Management - Bailiffs, Tracing\" lines are payments to bailiffs for money owed to the council " +
                "and are not borrowing.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "42 files, 547,351 data rows, 5 blank rows dropped, 547,346 mapped rows (signed total GBP 2,778,012,979.89, equal before and after the " +
            "preparation step), 48,489 redacted excluded, 498,857 retained: retained + redacted = export rows. Schedule A 0 (no VAT split), Schedule D 4 " +
            "documents / 10 lines. Schedule B 44,077 groups / 225,094 member rows / GBP 500.25m: Unclear 43,215, standing-payment surplus 309, catch-up 386, " +
            "reversed the same day 167; random hand-checks of 30 groups: 30/30 in each class against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #12 (Session 31). Bristol: one CSV a month from the council's own page, January 2020 on; real transaction
    /// numbers; VAT is published as separate lines under the same transaction number.</summary>
    public static readonly SupportedCouncil Bristol = new(
        Name: "Bristol City Council",
        TransparencyPageUrl: "https://www.bristol.gov.uk/council/council-spending-and-performance/spending-over-500",
        HowToFindTheFile:
            "On Bristol City Council's \"Spending over £500\" page each month is a link (a CSV, about 1 MB). Download each month and drop the " +
            "files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Name",
            PayDate: "Pay Date",
            Description: "Description 1",
            ServiceArea: "Description 2",
            Net: null,
            Gross: "amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2020, 1, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://www.bristol.gov.uk/council/council-spending-and-performance/spending-over-500", SourceFormat.Csv))
            .ToList(),
        KnownQuirks: new List<string>
        {
            "80 monthly CSVs on the council's own page, January 2020 to August 2026, none missing (the data.gov.uk catalogue lists fewer). File names are " +
                "ids, so each file is tagged by the month its Pay Date column holds. Files from April 2024 on are named \"supplier spend\", earlier ones " +
                "\"payment over 500\"; the amount column is \"amount\" or \"Amount\" and in the November 2025 file the columns are in a different order (the " +
                "mapping goes by name).",
            "VAT IS A SEPARATE LINE: a payment of 429.00 to Weightmans LLP is followed by a line \"Input VAT\" of 85.80 under the same Transaction Number " +
                "(26,288 of 378,543 file-and-number groups have more than one line). One signed amount column, no VAT field, so Schedule A is empty by " +
                "construction; lines are compared as published.",
            "Footer totals: 29 files end with a row that holds only the file's total (2020-06 and the files from 2024-04 on, except 2025-06); each equals " +
                "the sum of that file's payment rows to the penny, so the engine drops it as a non-payment (31 such or empty lines in all). Two files " +
                "(2020-04, 2025-06) end with an empty line only.",
            "Some files (for example August 2026) carry the pound sign as a Windows-1252 byte (\"�560.00\" if read as UTF-8); the reader detects the " +
                "encoding.",
            "One file re-lists earlier payments: the December 2020 file holds 11 rows paid in September 2020 (for example transaction 30827925, 14 " +
                "September 2020, the West of England Combined Authority transport levy, 981,500.00) that are already in the September 2020 file " +
                "(same transaction number, supplier, amount, date; GBP 1.12m).",
            "Redaction: payees are replaced by REDACTED text; those rows are excluded from the schedules and counted in the export (13,692 rows, 2.9%).",
            "Treasury and loan lines are in the file: loans to the council's companies (Goram Homes, Bristol Holdings, Bristol Waste Company, " +
                "\"Balance Sheet - Loans Fund / Long Term Debtors - Capital loans\") and broker fees (Tradition, Link Treasury, Imperial Treasury, TP ICAP, MUFG); " +
                "no PWLB payment line.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "80 files, 469,725 raw records, 31 footer or empty lines dropped (29 footer totals, each equal to its file's payment rows), 469,694 mapped rows, " +
            "13,692 redacted excluded, 456,002 retained: retained + redacted = export rows. Schedule A 0 (no VAT split), Schedule D 19 transactions / 48 lines. " +
            "Schedule B 20,389 groups / 67,686 member rows / GBP 231.30m: Unclear 19,468, standing-payment surplus 211, catch-up 710; random hand-checks of 30 groups: " +
            "30/30 in each class against the raw files. Same-content repeats across files: 11 rows (GBP 1.12m).",
        FoiContactEmail: null);

    public static readonly IReadOnlyList<SupportedCouncil> All = new[] { Wokingham, Merton, Reading, BracknellForest, WestBerkshire, Rbwm };

    /// <summary>The big cities onboarded from Session 30. Kept apart from <see cref="All"/> so the six-council baseline
    /// stays reproducible; <see cref="Everyone"/> is what the cross-council checks run over.</summary>
    public static readonly IReadOnlyList<SupportedCouncil> Cities = new[] { Birmingham, Leeds, Sheffield, Bradford, Liverpool, Bristol }
        .Concat(NextCouncils.All).ToList();

    /// <summary>Every onboarded council. Setting the environment variable COUNCILAUDIT_SIX_ONLY=1 limits this to the original
    /// six, which is how the check "adding the cities changes no six-council row" is run.</summary>
    public static readonly IReadOnlyList<SupportedCouncil> Everyone =
        Environment.GetEnvironmentVariable("COUNCILAUDIT_SIX_ONLY") == "1" ? All : All.Concat(Cities).ToList();
}
