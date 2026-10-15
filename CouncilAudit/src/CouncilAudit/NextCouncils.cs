namespace CouncilAudit;

/// <summary>Councils onboarded in Session 38 (the next largest English councils after the twelve). Kept in its own class so
/// SupportedCouncils.cs, which holds the twelve, is not touched; <see cref="SupportedCouncils.Cities"/> lists them.</summary>
public static class NextCouncils
{
    /// <summary>Slug by council name for the councils in this class (used by the two SlugFor switches).</summary>
    public static readonly Dictionary<string, string> SlugByName = new()
    {
        ["Wakefield Metropolitan District Council"] = "wakefield",
        ["Coventry City Council"] = "coventry",
        ["Durham County Council"] = "durham",
        ["Kirklees Council"] = "kirklees",
        ["Leicester City Council"] = "leicester",
        ["Cornwall Council"] = "cornwall",
        ["Nottingham City Council"] = "nottingham",
        ["Wirral Metropolitan Borough Council"] = "wirral",
        ["Newcastle City Council"] = "newcastle",
        ["Surrey County Council"] = "surrey",
        ["Essex County Council"] = "essex",
        ["Hertfordshire County Council"] = "hertfordshire",
        ["Stockport Metropolitan Borough Council"] = "stockport",
        ["City of York Council"] = "york",
        ["Calderdale Metropolitan Borough Council"] = "calderdale",
        ["London Borough of Camden"] = "camden",
    };

    /// <summary>Monthly tags "YYYY-MM" from y0/m0 to y1/m1, minus any listed.</summary>
    internal static List<string> Months(int y0, int m0, int y1, int m1, params string[] except)
    {
        var tags = new List<string>();
        for (int ym = y0 * 12 + (m0 - 1); ym <= y1 * 12 + (m1 - 1); ym++)
        {
            string t = $"{ym / 12:0000}-{ym % 12 + 1:00}";
            if (!except.Contains(t)) tags.Add(t);
        }
        return tags;
    }

    /// <summary>Every council of this class, in onboarding order (static init order: after the councils themselves).</summary>
    public static IReadOnlyList<SupportedCouncil> All => new[] { Wakefield, Coventry, Durham, Kirklees, Leicester, Cornwall, Nottingham, Wirral, Newcastle, Surrey, Essex, Hertfordshire, Stockport, York, Calderdale, Camden };

    /// <summary>Council #28 (Session 48). London Borough of Camden: ONE Socrata export (opendata.camden.gov.uk, listed on data.gov.uk), payments over GBP 500, 16 September 2019 to 28 August 2026, 84 months, a unique identifier on every line.
    /// Claims are in PREREG_S48_CAMDEN.md; the profile text is written after the first run. Files are the export split by payment month (a pure split), so a month is one unit.</summary>
    public static readonly SupportedCouncil Camden = new(
        Name: "London Borough of Camden",
        TransparencyPageUrl: "https://opendata.camden.gov.uk/d/3ixw-qvb8",
        HowToFindTheFile:
            "On the council's open data site (opendata.camden.gov.uk, also listed on data.gov.uk) open \"Camden Council Spend Over 500 GBP\" and export it as CSV: one file holds every month from September 2019. " +
            "Split it by payment month, or drop the export onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Date",
            Description: "Purpose",
            ServiceArea: "Organisational Unit",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2019, 9, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://opendata.camden.gov.uk/d/3ixw-qvb8", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "One export file from the council's open data site, 416,000 lines, 16 September 2019 to 28 August 2026 (84 months, none missing; September 2019 is a part month). The site's own description says \"since 2010\"; this export holds nothing before September 2019. " +
                "The export was split by payment month without changing any line, and the council site's own count and total for each of the 84 months (computed by the site, not by this scanner) equal the file's in all 84 months; the signed total is GBP 5,620,030,075.32.",
            "WHAT THE FILE COVERS: the council says payments over GBP 500; no line is under GBP 500 and no line is negative, so credit notes are not published here. Every line has a unique identifier (\"aug-26-6288\": month, year, number; 416,000 different values over 416,000 lines), " +
                "so Schedule A (one number, different amounts) and Schedule D (one number, several payees) are 0 by construction and the overlap test between months finds nothing. Schedule B (same payee, amount and date) is the repeat test that speaks. " +
                "The file has no VAT amount (the irrecoverable VAT column is 0 on every line) and no payment number other than the identifier.",
            "REDACTION: 66,091 of 416,000 lines (15.9%; 14.1% to 18.2% in each financial year) have the payee \"xxxxREDACTEDxxxx\" (49,232), \"REDACTED\" (16,601) or \"Redacted\" (258); 1,707 of them also redact the purpose (7,331 lines redact the purpose in all; 5,624 of those name the payee). They are excluded from the schedules and counted in the export, which leaves 349,909 retained lines. " +
                "One retained line has an empty payee (10 September 2025, Responsive Repairs Costs, GBP 17,395.07); it is a single line and is in no schedule. 43 payee names end in a run of asterisks (\"24HR AQUAFLOW SERVICES LIMITED****\"; 19 of them also appear without); the supplier key keeps the asterisks, " +
                "but no group of the same payee, amount and date mixes the two spellings, so no repeat group is split by it.",
            "REPEATED PAYMENTS (Schedule B): 23,308 groups, 73,906 member rows (21.1% of the retained lines), GBP 480,233,010.56: 20,940 open (68,465 rows), 1,861 catch-up (4,061 rows) and 507 standing-payment surplus (1,380 rows). This is a large share because many lines are small per-person care, support, housing and recruitment charges. " +
                "A read of 30 randomly chosen open groups by payee name found 18 care, support, housing, charity or health payees, 3 recruitment agencies, 2 groups of one services company (Ashdale Services) whose name does not say what it supplies, and 7 others (a scaffolding firm, a highways contractor, consultants, an online firm, a barristers' chambers, two more companies). " +
                "The largest groups are pairs and sets of equal payments on one day under consecutive identifiers: Greater London Authority \"Levies Paid\" (2 payments of GBP 20,840,838.00 on 22 June 2021 and again on 17 August 2021; 2 of GBP 15,437,658.00 on 19 January 2022), HMRC Central Payroll (2 of GBP 8,325,361.11 on 20 August 2025, PAYE and NI), " +
                "and Veolia Environmental Services (4 of GBP 1,940,998.07 on 1 September 2025); a payments file cannot tell these from separate instalments, and nothing here says a payment was wrong.",
            "FILE-TO-FILE REPEATS: none. 0 of 349,909 retained lines match an earlier month by payee, amount, date, description and service, and no month is flagged by the exact-repeat test (0 of 84).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "One export of 416,000 lines read and split into 84 months (September 2019 to August 2026, none missing), 416,000 raw records, 0 blank lines dropped, 416,000 mapped rows (signed total GBP 5,620,030,075.32 raw and re-read; no negative lines), 66,091 redacted excluded, 349,909 retained: retained + redacted = export rows. " +
            "The council site's own count and total per month (computed by the site's query service) equal the file's in 84 of 84 months. A random sample of 300 exported rows: all 300 were found by payee, date and amount in the split files. " +
            "Schedule A 0; Schedule D 0; Schedule B 23,308 groups / 73,906 member rows: Unclear 20,940, standing-payment surplus 507, catch-up 1,861; random hand-checks 30/30 in each of the three classes against the raw files. " +
            "Same line in an earlier month by payee, amount, date, description and service: 0 of 349,909. Nothing before 16 September 2019 is in this export; the council's older yearly lists are not reachable by script.",
        FoiContactEmail: null);

    /// <summary>Council #27 (Session 47). Calderdale Metropolitan Borough Council: one CSV a month on dataworks.calderdale.gov.uk (listed on data.gov.uk as "Payments to suppliers 2020-21" to "2026-27"), April 2020 on,
    /// payments over GBP 500 excluding benefits and pay, a payment number on every line. Claims are in PREREG_S47_CALDERDALE.md; the profile text is written after the first run.</summary>
    public static readonly SupportedCouncil Calderdale = new(
        Name: "Calderdale Metropolitan Borough Council",
        TransparencyPageUrl: "https://dataworks.calderdale.gov.uk/dataset/payments-to-suppliers-2026-27-2wqx8",
        HowToFindTheFile:
            "On the council's data site (dataworks.calderdale.gov.uk, also listed on data.gov.uk) open \"Payments to suppliers\" for a financial year (2020-21 to 2026-27): each lists one CSV (and an Excel copy) for each month. " +
            "Download the CSVs and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Date",
            Description: "Procurement Category",
            ServiceArea: "Expense Area",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Definition"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2020, 4, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://dataworks.calderdale.gov.uk/dataset/payments-to-suppliers-2026-27-2wqx8", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "77 CSV files on the council's data site (dataworks.calderdale.gov.uk; data.gov.uk lists them as \"Payments to suppliers 2020-21\" to \"2026-27\"), one file a month, April 2020 to August 2026, 77 months and none missing, 408,188 lines. " +
                "Each year's page also lists an Excel copy of every month; all 77 workbooks were read (76 .xlsx and one legacy .xls) a second time and every month has the same number of lines and the same signed total to the penny as its CSV " +
                "(the workbooks have a different column layout, a title row and no payment number, so the match is by date, amount and payee). The only difference is in two payee names that contain quotation marks " +
                "(Next Stage \"A Way Forward\" Youth Development Ltd, Paramount Therapy Centre Ltd -Change for YP\"): the CSV files lose the opening quotation mark of the name (352 lines); the CSV text is shown as published." +
                "Seventy files have a leading \"Body\" column (a statistics.data.gov.uk address) and the seven from February 2026 do not. Every date is dd/MM/yyyy and the commonest month of the dates is the file's own month in 77 of 77 files, so each file is tagged by the month in its name.",
            "WHAT THE FILES COVER: the council says payments to suppliers with a value over GBP 500, excluding benefit payments and employee pay, and the earlier page says the amount is shown net of VAT. The threshold is on the invoice, not the line: " +
                "211,569 of the 408,188 lines (51.8%) are below GBP 500 (27.5% of the retained lines), for example hardware-shop lines of GBP 1.66. Credit lines are published (24,070 negative lines, GBP -24,804,703.95). " +
                "Lines per month run from 3,597 (August 2020) to 7,388 (April 2024), and the signed total of the 77 files is GBP 2,311,536,965.22.",
            "THE PAYMENT NUMBER IS ONE LINE EACH: \"Internal Ref Number\" has 408,188 different values over 408,188 lines, none repeated in a file or between files, so Schedule A (one number, different amounts) and Schedule D (one number, several payees) are 0 by construction " +
                "and the overlap test between files finds nothing (0 of 408,188). Schedule B (same payee, amount and date) is the repeat test that speaks.",
            "REDACTION IS A THIRD OF THE LINES: 147,066 of 408,188 lines (36.0%) have the payee \"REDACTED PERSONAL DATA\" (the one spelling; 32.2% to 37.1% in each financial year). 136,333 of them are \"Boarding Out Allowances\" (foster care allowances), 8,094 " +
                "\"Accounts Payable Invoices\", 2,327 \"Adult Placements\", 279 \"Residential Care\" and 33 credit-card lines. The council's own page says redaction applies to social service payments to adult and foster carers. They are excluded from the schedules and counted in the export, " +
                "which leaves 261,122 retained lines. Another 1,032 lines have a payee cell that is a month and a client group (\"2024:12LD Learning Disabilities\", 702 different labels, Supplier ID \"Personal Budgets\", direct payments): not a payee name, 0 of them in Schedule B. " +
                "HMRC appears as many payees (\"HMRC - AC24 ...\" with an academy's name): a payee naming HMRC has 5 or more lines in 60 of the 77 months.",
            "REPEATED PAYMENTS (Schedule B): 19,722 groups, 68,820 member rows (26.4% of the retained lines), GBP 192,577,649.79: 18,250 open (65,397 rows), 1,017 catch-up, 452 standing-payment surplus and 3 reversed the same day. This is a large share because the lines are small and per-site, per-worker or per-placement: " +
                "the largest payees by member rows are Reed Specialist Recruitment (5,189 rows), Managed Water Services (4,557), a sole-trader electrician (3,314) and a security firm (2,097; one group is 50 payments of GBP 200 on 28 November 2024). " +
                "A read of 30 randomly chosen open groups by payee name found 10 care, support, college or nursery payees and 20 others (trades, a security firm, a water-services contractor, recruitment agencies, a hardware shop, a homes company, the council's own account). " +
                "The largest groups are fixed amounts repeated on one day: Romaquip Ltd (15 payments of GBP 184,248.93 on 13 August 2026, category Equipment, payment numbers close together), Humankind Support Services (5 of GBP 257,179.58 on 21 November 2024, numbers close together) and" +
                "Timeout Children's Homes (several groups of 23 to 31 payments of GBP 20,000 to GBP 23,159.33); a payments file cannot tell these from separate deliveries, instalments or one payment per child, and nothing here says a payment was wrong.",
            "FILE-TO-FILE REPEATS: none. 0 of 261,122 retained lines match an earlier file by payee, amount, date, description and service, no file is flagged by the exact-repeat test (0 of 77), and there are no transaction twins and no doubled transactions.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "77 files downloaded and loaded, 77 months (April 2020 to August 2026, none missing), 408,188 raw records, 0 blank lines dropped, 408,188 mapped rows (signed total GBP 2,311,536,965.22 raw and re-read, 24,070 negative rows, GBP -24,804,703.95), " +
            "147,066 redacted excluded, 261,122 retained: retained + redacted = export rows. A random sample of 300 exported rows: all 300 were found by payee, date and amount in the original downloads. A second read of the 77 Excel copies (76 .xlsx, one .xls) gives the same line count and signed total in all 77 months. " +
            "Schedule A 0; Schedule D 0; Schedule B 19,722 groups / 68,820 member rows: Unclear 18,250, standing-payment surplus 452, catch-up 1,017, reversed the same day 3; random hand-checks 30/30 in each of the three large classes and 3/3 in the fourth against the raw files " +
            "(random samples of 30 groups). Same line in an earlier file by payee, amount, date, description and service: 0 of 261,122.",
        FoiContactEmail: null);

    /// <summary>Council #26 (Session 46). City of York Council: one CSV a financial year on data.yorkopendata.org (listed on data.gov.uk as "All Payments to Suppliers"), 2011/12 to 2025/26,
    /// four column layouts, a real transaction number. Claims are in PREREG_S46_YORK.md; the profile text is written after the first run.</summary>
    public static readonly SupportedCouncil York = new(
        Name: "City of York Council",
        TransparencyPageUrl: "https://data.gov.uk/dataset/27bc1dc6-d62f-4b93-a326-13989f5bfb56/all-payments-to-suppliers",
        HowToFindTheFile:
            "On data.gov.uk open the dataset \"All Payments to Suppliers\" from City of York Council: it lists one CSV for each financial year from 2011/12 (files named over500payments2011.csv to over250payments2025.csv). " +
            "Download the CSVs and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Date",
            Description: "Expense Category",
            ServiceArea: "Directorate",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Department"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Enumerable.Range(2011, 15).Select(y => "FY" + y + "-" + ((y + 1) % 100).ToString("00"))
            .Select(t => new CouncilYearFile(t, "https://data.gov.uk/dataset/27bc1dc6-d62f-4b93-a326-13989f5bfb56/all-payments-to-suppliers", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "15 annual CSV files on the council's open-data site (listed on data.gov.uk), 2011/12 to 2025/26, one per financial year, April to March, 1,273,453 rows and none missing. Column names change four times (2011/12 to 2014/15, " +
                "2015/16 to 2017/18, 2018/19, 2019/20 on) and the files are named over500payments (2011 and 2012), allpayments (2013 to 2017) and over250payments (2018 on), so the threshold changes too (the file names are the only statement of it found). Each file is tagged by the financial year in its name; every row's payment date is inside its own financial year (1,273,419 of 1,273,453; the 34 others are dated " +
                "up to 11 April 2019 in the 2018/19 file). In 2018/19 the file has two dates, a GL date and a payment date; the payment date is used (96.7% are within a month of the GL date). The 2011/12 to 2014/15 files give one \"Amount\" " +
                "and do not say whether it includes VAT; from 2015/16 the column says excluding VAT.",
            "COUNTS ARE NOT COMPARABLE ACROSS YEARS: rows per file are 37,522 (2011/12), 88,575 (2012/13) with almost the same total (GBP 150.7m and 150.8m), 164,221 (2013/14), then 139,000 to 146,000 until a drop in December 2016 from about " +
                "14,000 rows a month to under 5,000 (58,490 in 2017/18) and 56,000 to 68,000 a year since. August 2016 alone holds 27,612 rows (home-care lines of Riccall Carers Ltd 2,102 against 810 in September, Springfield Healthcare 1,224 against 474); it " +
                "is not a repeated file (identical lines are 16% of it against 11% in the months around it, where a repeat would give about 50%).",
            "THE TRANSACTION NUMBER IS NOT ONE PAYMENT IN 2011/12 TO 2017/18 AND NOT UNIQUE IN 2024/25, so Schedule D (one number, several payees) is large there and means nothing by itself: 46,498 transactions / 350,520 lines, 30,578 in " +
                "2011/12 to 2017/18 and 15,920 in 2024/25, none in 2018/19 to 2023/24 or 2025/26. In 2011/12 to 2017/18 the number (CR0000105453) is shared by unrelated payees paid on the same day (Adecco and Tarmac; Harrogate Borough Council and a hotel): " +
                "a payment-run reference, like Reading's payment number. In 2024/25 the numbers \"202425CRCR000nnnnn\" (38,184 rows, September 2024 to March 2025, not in date order) are each used by exactly two lines of different payees, e.g. 202425CRCR00024919 " +
                "is the Ouse & Derwent Internal Drainage Board levy (GBP 84,166.04, 30 October 2024) and Sensation Care Ltd (GBP -8,267.09, 7 February 2025); every other number in 2018/19 to 2025/26 (\"201819CR00000001\") is one line. Nothing here shows an error " +
                "in a payment; the numbering is what it is, and no test that keys on the number can be read for these years. Schedule A is 0 for the same reason.",
            "REDACTION VARIES WITH THE YEAR: 136,053 of 1,273,453 rows (10.7%) have the payee \"REDACTED - PERSONAL DATA\" (also written \"REDACTED- PERSONAL DATA\", \"REDACTED-PERSONAL DATA\", \"REDACTED PERSONAL DATA\"): 3.7% in 2011/12, " +
                "17.8% in 2012/13, 21.6%, 26.0%, 7.2%, 13.2% and 10.2% in the next five years, then 0.9% to 3.5% from 2018/19 on (3.4% in 2025/26). They are excluded from the schedules and counted in the export.",
            "REPEATED PAYMENTS (Schedule B) are 35,058 groups, 103,039 member rows, GBP 187.13m: 31,582 open, 1,533 catch-up, 414 standing-payment surplus and 1,529 reversed the same day. A read of 30 randomly chosen open groups by payee found 19 care, " +
                "support or housing providers (care homes, home care, supported living) and 11 others (a nursery, a plasterer, a waste contractor, a telecoms supplier, a training firm, vehicle hire, a prepaid-card wallet with 14 lines); the largest groups are " +
                "repeated fixed amounts under consecutive numbers on one day (First York Ltd, six payments of GBP 192,000 on 26 November 2019; Dennis Eagle Ltd, four of GBP 228,920 on each of 30 December 2021 and 6 January 2022), which a payments file cannot " +
                "tell from separate deliveries or instalments. Seven pairs of transactions with the same lines under two numbers within a week are in the twins file (three identical in every column but the number: Baydale Controls Systems, GBP 22,353.17, " +
                "25 September 2015; Whale Tankers, GBP 129,553.00, 20 March 2018; Hull City Council, GBP -13,000, 22 November 2017).",
            "FILE-TO-FILE REPEATS: none. 0 of 1,137,400 retained rows match an earlier file by payee, amount, date, description and service, and no file is flagged by the exact-repeat test (rates 0 from 2018/19, 8.9% to 20.0% before it, where the " +
                "number is not unique per line; 2016/17 is the highest, 20.0%, and is not flagged against a neighbour norm of 14.3%). HM Revenue and Customs and Teachers' Pensions are not here in any quantity (a payee naming HM Revenue or HMRC has 0 to 19 lines a year, 5 or more in 5 of 15 years).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "15 files downloaded and loaded, 15 financial years, 1,273,453 raw records, 0 blank lines dropped, 1,273,453 mapped rows (signed total GBP 3,396,056,245.58 raw and re-read, 40,758 negative rows, GBP -159,751,344.63), " +
            "136,053 redacted excluded, 1,137,400 retained: retained + redacted = export rows. A random sample of 300 exported rows: all 300 were found by payee, date and amount in the original downloads. Schedule A 0; Schedule D 46,498 transactions / " +
            "350,520 lines (a payment-run number in 2011/12 to 2017/18, a reused number in 2024/25, see above); Schedule B 35,058 groups / 103,039 member rows: Unclear 31,582, standing-payment surplus 414, catch-up 1,533, reversed the same day 1,529; " +
            "random hand-checks of 30 groups: 30/30 in each of the four classes against the raw files. Same line in an earlier file by payee, amount, date, description and service: 0 of 1,137,400.",
        FoiContactEmail: null);

    /// <summary>Council #25 (Session 45). Stockport Metropolitan Borough Council: one CSV a month on its own S3 bucket (listed on data.gov.uk as "All spend"), February 2017 on; payments over GBP 500
    /// until March 2025 and all spend from April 2025; no transaction number before April 2025. Profile text is written after the first run (see PREREG_S45_STOCKPORT.md).</summary>
    public static readonly SupportedCouncil Stockport = new(
        Name: "Stockport Metropolitan Borough Council",
        TransparencyPageUrl: "https://www.data.gov.uk/dataset/0c5487f4-c863-4f99-b882-459d3acf4b54/stockport-council-all-spend",
        HowToFindTheFile:
            "On data.gov.uk open the dataset \"All spend\" from Stockport Metropolitan Borough Council: it lists one CSV (and an Excel copy) for each month from February 2017. " +
            "Download the CSVs and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Date",
            Description: "Summary of Purpose of Expenditure",
            ServiceArea: "Service",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Merchant Category"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2017, 2, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://www.data.gov.uk/dataset/0c5487f4-c863-4f99-b882-459d3acf4b54/stockport-council-all-spend", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "116 files on the council's own S3 bucket (data.gov.uk dataset \"All spend\"): 113 CSV and 3 Excel workbooks, one file a month, February 2017 to August 2026, 115 months and none missing. July 2018 is published as both CSV and " +
                "Excel with the same 8,490 rows (one is used); November 2019 and October 2021 are Excel only (the CSV link listed for October 2021 has a question mark where the pound sign belongs and did not download). File names change " +
                "constantly (\"Spend over GBP500 Feb 17\", \"Over GBP500 spend Dec18\", \"Expenditure over GBP500 - June 21\", \"All Spend April 2025\"), so each file is tagged by the month its name says; the payment dates agree " +
                "(the most common date month is the tagged month in all 112 dated files).",
            "THRESHOLD AND COVERAGE: the files say \"Over GBP500\" until March 2025 and the council says it publishes all spend from April 2025 (rows go from 22,884 in March 2025 to 35,991 in April). Monthly rows also double " +
                "from March 2020 (6,947 in February 2020, 13,444 in March and 14,000 to 20,000 after) with no change in the file name, so counts are not comparable across those two steps.",
            "WHICH DATE: the payment date is the \"paid_date\" column only in the 14 files from September 2018 to October 2019 (which also publish an invoice date) and the 17 files from April 2025. In 66 CSV files (April to August 2018, " +
                "December 2019 to March 2025) the only date is an \"invoice_date\", in the 13 files from February 2017 to February 2018 the column is just \"Date\", and the two Excel months have one date column. The scanner uses the one date it has, so for those 81 files " +
                "\"same date\" in the repeated-payment check means the same date in that column (an invoice date where it is named so), not necessarily the same payment day. Three files have no date at all (March 2018, July 2022 and September 2022): their 46,890 rows are in the file totals " +
                "and in no financial year.",
            "NO TRANSACTION NUMBER BEFORE APRIL 2025: only the 17 files from April 2025 carry a \"transaction_id\". Every other row gets a per-row placeholder \"(no number published) N\", so Schedule A (amount mismatch) and " +
                "Schedule D (one number, several payees) are empty by construction there, a multi-line invoice appears as separate rows, and the repeated-payment check (Schedule B) is the only repeat test. Six pairs of " +
                "transactions with the same lines under two numbers within a week are in the 17 numbered months (see the twins file).",
            "REDACTION IS HEAVY AND ALWAYS HAS BEEN: 616,498 of 1,947,912 rows (31.6%) have the payee \"*Redact - Personal Information\" (544,596 lines) or \"Redact - Personal Information\" (70,284), 163 are the council's own label " +
                "\"*Exclude\" or \"*Exclude - VAT only\" (October 2025 only; treated as a placeholder) and 1,455 are other lines whose payee the redaction test treats as a withheld name; payee text with \"Redact\" is 30.2% of rows before April 2025 and 36.0% from then (about 40% in 2017 to 2019, 25% in 2022 to 2024). " +
                "They are excluded from the schedules and counted in the export. The signed total is GBP 6,872,346,948.88 (312,041 negative rows, GBP -532.5m). The 17 numbered months show no pooled payee label like Essex's.",
            "REPEATED PAYMENTS (Schedule B) are 117,912 groups, 495,751 member rows, GBP 942.15m: 108,888 open, 2,982 catch-up, 2,067 standing-payment surplus and 3,975 reversed the same day. A read of 12 randomly chosen groups by payee was " +
                "10 care-provider groups (one line per resident, same fee, same invoice date), one cleaning contract and one fees line of a care and education provider; none can be called a duplicate from this data. The largest groups are annual " +
                "levies paid in monthly instalments with one invoice date: the Greater Manchester Combined Authority, GBP 1,800,000 and GBP 2,101,587 under an invoice date of 2 April 2024 in 11 monthly files in a row (May 2024 to " +
                "March 2025), and the same pattern in other years. A payments file cannot show the instalment plan.",
            "FILE-TO-FILE REPEATS: 6,632 of 1,331,414 retained rows (0.50%) match an earlier file by payee, amount, date, description and service, and the exact-repeat rate is 8.8% to 59.9% a file (median 38.6%) with no file " +
                "flagged. 5,379 of the 6,632 are September 2022 against July 2022, the two files with no date: with the date blank, fixed weekly care fees match (eight pairs of adjacent months compared without the date match 29% to 63% " +
                "of rows by payee, amount, purpose, service and category; two months apart 43% to 59%, and September against July is 43%), so this is not a republished month. The overlap test also lists 164 keys between those two files, which are row-position placeholders agreeing by chance.",
            "NOT COMPARABLE MONTH TO MONTH: payees starting HMRC (or naming HM Revenue) have fewer than 5 lines in 10 of 115 months (December 2021, June 2023, October 2023, March 2024, June 2024, December 2024, March 2025, July 2025, March 2026, " +
                "May 2026) against 24 to 123 in the others, and the dates of each file run up to a month behind it (91.5% of all rows fall in the file's own month, 5.6% in the month before). November 2024 carries " +
                "about 1,023,000 trailing empty lines (dropped); amounts are written 1787, \"12,204.95\" with spaces, \"GBP3,340.00\" and -GBP51.46 (one standard reading, no cell changed).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "116 files downloaded, 115 months loaded (113 CSV and 2 Excel; the July 2018 Excel copy left out because it holds exactly the rows of the CSV), 2,971,136 raw records, 1,023,224 blank lines dropped, " +
            "1,947,912 mapped rows (signed total GBP 6,872,346,948.88 raw and re-read), 616,498 redacted excluded, 1,331,414 retained: retained + redacted = export rows. Schedule A 0, Schedule D 0 (no transaction number before " +
            "April 2025). A random sample of 300 exported rows: all 300 were found by payee, date and amount in the original downloads. Schedule B 117,912 groups / 495,751 member rows / GBP 942.15m: Unclear 108,888, standing-payment " +
            "surplus 2,067, catch-up 2,982, reversed the same day 3,975; random hand-checks of 30 groups: 30/30 in each class against the raw files. Same line in an earlier file by payee, amount, date, description and service: 0.50% of retained rows.",
        FoiContactEmail: null);

    /// <summary>Council #24 (Session 43). Hertfordshire County Council: one CSV a quarter (April 2022 to December 2024) then one a month (January 2025 on) on its own site, a real transaction number
    /// and a beneficiary id; payments over GBP 250 until March 2025 and over GBP 500 from April 2025.</summary>
    public static readonly SupportedCouncil Hertfordshire = new(
        Name: "Hertfordshire County Council",
        TransparencyPageUrl: "https://www.hertfordshire.gov.uk/about-the-council/freedom-of-information-and-council-data/open-data-statistics-about-hertfordshire/what-we-spend-and-how-we-spend-it/what-we-spend-and-how-we-spend-it.aspx",
        HowToFindTheFile:
            "On Hertfordshire County Council's \"What we spend and how we spend it\" page the \"Supplier payments over GBP 250/500\" list has one CSV per month (per quarter before 2025). " +
            "Download the CSVs and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier (Beneficiary)",
            PayDate: "Date",
            Description: "Purpose of Expenditure (Expenditure Category)",
            ServiceArea: "Dept. where expenditure incurred",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Procurement (Merchant Category)"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Quarters(2022, 4, 2024, 10).Concat(Months(2025, 1, 2026, 8))
            .Select(t => new CouncilYearFile(t, "https://www.hertfordshire.gov.uk/about-the-council/freedom-of-information-and-council-data/open-data-statistics-about-hertfordshire/what-we-spend-and-how-we-spend-it/what-we-spend-and-how-we-spend-it.aspx", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "31 CSVs on the page \"What we spend and how we spend it\": 11 quarterly files (April 2022 to December 2024) then 20 monthly files (January 2025 to August 2026), none missing. THE THRESHOLD CHANGED: " +
                "\"over 250\" in the file names up to March 2025 and \"over 500\" from April 2025 (21,913 rows in March 2025, 15,938 in April 2025), so counts and totals are not comparable across April 2025. " +
                "December 2025's file has no year in its name. Each file is tagged by its name and every payment date falls inside its period (963,275 of 963,276 rows; the one other has no date).",
            "A REAL TRANSACTION NUMBER (an SAP document number, 5101xxxxxx from 2022 and 1905xxxxxx in the other series) and a Beneficiary ID are published; the columns change order between files (read by name), " +
                "dates are written dd/MM/yyyy or d/M/yyyy (\"10/3/2025\", written in full by `prep`). 81,746 of 862,399 (file, number) groups have more than one line; no number carries more than one payee or date, " +
                "so Schedule D is empty. Same line in two files by number, amount and date: 0 keys. Amounts are \"Net Amount\" (mapped as Net and Gross); \"Irrecoverable VAT\" is a separate, mostly empty column kept in the export.",
            "REDACTION IS 36% OF ALL ROWS: \"Identity Redacted\" (in five capitalisations, and once \"Idenity Redacted\") stands in for 347,332 payees, 36.1% of rows and GBP 521.9m, and is excluded from the schedules and counted " +
                "in the export. 42,413 other rows (GBP 276.3m) have a number as the payee name (\"402720\", the Beneficiary ID of a children's-home or home-care placement); the number is stable, so lines under one " +
                "number are one payee. Two real companies, \"REDACTIVE EVENTS LTD\" and \"Redactive Events Limited\", were wrongly dropped as redacted until 4 October 2026 (the test is now \"REDACT but not REDACTIVE\", " +
                "which moves 270 rows across 22 of the 24 councils' files).",
            "ONE STRAY HEADER LINE: the December 2025 file repeats a header line as a data row (\"Vendor Name for Publication\", \"Payment Date\", \"Net Amount\"); it has no date and no amount and is carried as a row with " +
                "no pay date. \"Purchasing Card Payment\" (7,414 lines, one beneficiary id, card statement lines of different cardholders) is a label for many payments; it is 1.2% of Schedule B's member rows (940 of 79,235) " +
                "and no rule is applied to it.",
            "Schedule B (same payee, amount and date under different numbers): 22,319 groups, 79,235 member rows, GBP 329.5m of value: Unclear 20,749, catch-up 1,120, standing-payment surplus 306, reversed the same day 144. " +
                "The largest are NHS community providers and Public Health contracts paid in equal parts on one day under consecutive transaction numbers (4,583 of the open size-2 groups have consecutive numbers).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "31 files downloaded and loaded, 963,276 raw records, 0 blank lines, 963,276 mapped rows (signed total GBP 6,939,472,722.64 raw and re-read), 347,332 redacted excluded (every one a spelling of \"Identity Redacted\"), " +
            "615,944 retained: retained + redacted = export rows. Schedule A 0, Schedule D 0. A random sample of 300 exported rows: all 300 were found by payee, date and amount in the original downloads. Schedule B random " +
            "hand-checks 120 of 120 against the raw files (30 in each class).",
        FoiContactEmail: null);

    /// <summary>Council #23 (Session 43). Essex County Council: one workbook a quarter on its own site (half legacy .xls), one sheet a month, ALL items (no threshold), no transaction number.</summary>
    public static readonly SupportedCouncil Essex = new(
        Name: "Essex County Council",
        TransparencyPageUrl: "https://www.essex.gov.uk/spending-and-council-tax/finance-and-spending-breakdowns",
        HowToFindTheFile:
            "On Essex County Council's \"Finance and spending breakdowns\" page each quarter's \"Day to day spending\" workbook is a link (Excel .xls or .xlsx; the file names change every quarter). " +
            "Download the workbooks and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Name",
            PayDate: "Date",
            Description: "Spend Description",
            ServiceArea: "Function",
            Net: "Value",
            Gross: "Value",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Source"),
        IdScope: TransactionIdScope.SupplierFurnished,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2019, 4, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://www.essex.gov.uk/spending-and-council-tax/finance-and-spending-breakdowns", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "30 workbooks on the page \"Finance and spending breakdowns\" (15 legacy .xls, 15 .xlsx; names change every quarter), one sheet a month, ALL items (no threshold) and every source: Accounts Payable, " +
                "Mileage & Expenses and Purchase Card (April 2025: 3,946 card lines and 3,599 mileage lines). 89 months, April 2019 to August 2026, none missing; the \"July to September 2026\" workbook " +
                "also has an empty September 2026 sheet (not loaded). 24 other workbooks on the page (LGTC contract registers) are not spending and are not read; a sheet named \"Field Descriptions\" is skipped. " +
                "The legacy .xls files are read with a standard spreadsheet reader. Each sheet is tagged by the month its dates hold.",
            "EACH SHEET PUBLISHES ITS OWN CONTROL TOTALS: a summary block above the header gives Accounts Payable, Mileage & Expenses, Purchase Card and Total. In all 89 non-empty sheets the rows sum to that Total " +
                "to the penny, and the 90 totals add to GBP 17,118,041,469.02, the signed total of the prepared files (3,713,962 rows after 69,229 blank padding rows are dropped, 3,713,962 mapped).",
            "NO TRANSACTION NUMBER: every row gets a placeholder \"(no number published) N\", so Schedule A and D are empty by construction and a multi-line invoice appears as separate rows. The sheets are " +
                "ordered by value, not by date. 979 rows are paid in the month before their sheet's month (31 March in the April sheet).",
            "EIGHT SHEETS WRITE SOME DATES AS TEXT: April and May 2020, October and December 2019 and January 2020 entirely, March 2020 mostly, and 3,135 and 3,110 rows of May and March 2023 (\"27/04/2020\", " +
                "\"23 March 2020\", \"16th May 2023\"); the scanner writes them dd/MM/yyyy and every row falls in its sheet's month or the one before.",
            "POOLED PAYEE LABELS: 14 labels stand for many people, not for one payee (\"FOSTER CARE PAYMENT\" 728,434 lines, \"ECC EMPLOYEE\" 297,311, \"DIRECT PAYMENT\" 293,982, \"PAYMENT TO INDIVIDUAL\" 31,495, \"NON EMPLOYEE EXPENSE\" " +
                "25,579, \"TRAINING BURSARY\" 21,780, five \"ONE OFF ...\" labels and \"FOSTER CARERS\"): 1,436,202 lines, 38.7% of all. The council writes \"Some names have been removed in line with Data Protection policy\". " +
                "The scanner renames them (exact match, nothing fuzzy) to \"Redacted (pooled label): <label>\" so the scanner leaves them out of every schedule and counts them in the export; with the labels as published they were 57.8% " +
                "of Schedule B's member rows (832,330 of 1,439,391; 283,957 groups, 100,316 of them with a pooled label). The schedules say NOTHING about these lines.",
            "Redaction otherwise: none (18 lines of REDACTIVE EVENTS LTD, a real supplier, were counted as redacted until the fix of 4 October 2026). Retained 2,277,760 + redacted 1,436,202 = 3,713,962 mapped rows. Credits are published as negative rows (1,944 of the 50,789 rows of April 2025).",
            "Schedule B (the same payee, amount and date, no number to separate them): 183,641 groups, 607,061 member rows, GBP 811.47m of extra value: Unclear 161,910, standing-payment surplus 8,524, catch-up 10,062, reversed " +
                "the same day 3,145. A read of 30 randomly chosen groups of the open ones by payee name: 11 care providers, 4 IT and 3 travel suppliers, 4 card merchants, 3 housing associations, 2 agency-staff firms and 3 others, one line per client, " +
                "purchase or booking; none can be called a duplicate from this data. No line appears in two files (0 of 2,277,760).",
            "A MONTH CAN BE THIN: July 2022 has 26,412 lines (GBP 114.1m) against 41,802 in June and 44,502 in August (GBP 198m and 180m); October 2021 30,541 lines (GBP 97.5m). Each sheet equals its own published total, so the thin " +
                "month is the council's publication, not a loss in loading.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "30 workbooks read (15 .xls, 15 .xlsx), 90 sheets, 89 months, 3,783,191 raw rows, 69,229 blank padding rows dropped, 3,713,962 mapped rows (signed total GBP 17,118,041,469.02, equal to the sum of the sheets' own totals " +
            "to the penny), 1,436,202 pooled excluded, 2,277,760 retained: retained + redacted = export rows. Schedule A 0, Schedule D 0 (no transaction number). A random sample of 300 exported rows: all 300 were found by " +
            "payee, date and amount in the original workbooks. Schedule B random hand-checks 120 of 120 against the raw files (30 in each class).",
        FoiContactEmail: null);

    /// <summary>Council #22 (Session 43). Surrey County Council: one CSV per quarter on its open data site (CKAN), payments over GBP 250; no transaction number is published.</summary>
    public static readonly SupportedCouncil Surrey = new(
        Name: "Surrey County Council",
        TransparencyPageUrl: "https://www.surreyi.gov.uk/dataset/council-spending-surrey-county-council-e6rgn",
        HowToFindTheFile:
            "On Surrey-i's data site open the \"Surrey County Council spend over GBP 250\" dataset and download each quarter's CSV (SAP_Spend_Data_Q<n>_<years> to 2022-23, ERP_Spend_Q<n>_<years> from 2023-24). " +
            "Drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Beneficiary Name",
            PayDate: "Date Spend Occurred",
            Description: "Summary of Purpose of Spend",
            ServiceArea: "Department Incurring Spend",
            Net: "Gross Amount",
            Gross: "Gross Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Merchant Category"),
        IdScope: TransactionIdScope.SupplierFurnished,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Quarters(2017, 4, 2026, 4)
            .Select(t => new CouncilYearFile(t, "https://www.surreyi.gov.uk/dataset/council-spending-surrey-county-council-e6rgn", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "47 quarterly CSVs on the Surrey-i dataset \"Council Spending - Surrey County Council\" (payments over GBP 250, purchase-card spend excluded, from SAP, personal data redacted); 37 are loaded " +
                "(April 2017 to June 2026, none missing) and the 10 older ones (2014-15 to 2016-17, a different year-ending layout with an extract date as the first column) are not used. File names are " +
                "\"SAP_Spend_Data_Q4_2022-23\" to 2022-23 and \"ERP_Spend_Q1_2023-2024_\" from 2023-24 (the council moved from SAP to a new ERP); each file is tagged by the quarter its name says and every " +
                "payment date falls inside that quarter (2,398,240 of 2,398,241 rows; the one other has no date).",
            "NO TRANSACTION NUMBER IS PUBLISHED. Every row gets a per-row placeholder \"(no number published) N\", so Schedule A (amount mismatch) and Schedule D (one number, several payees) are empty by " +
                "construction, a multi-line invoice appears as separate rows, and the same-payee-same-amount-same-day test (Schedule B) is the only repeat test: 138,536 groups, 431,772 member rows, " +
                "GBP 1,371.4m, of which 127,983 groups are open, 8,936 catch-up, 1,588 standing-payment surplus and 29 reversed the same day. A read of 30 randomly chosen groups of the open ones, by payee name, is " +
                "mostly care providers and schools and nurseries, where equal lines on one day are one per resident or child; none can be called a duplicate from this data.",
            "SEVERAL LAYOUTS: the files up to April 2018 (and the 2018-19 quarter files with a 13-column header) lead with extractdate, publisher and service-type columns; later files have six to eleven columns (some with trailing empty cells, one with Merchant " +
                "Category before Gross Amount); the spelling of the date column varies (\"Occured\", \"Incurred\"), and Gross Amount is written with a pound sign and thousands commas (a few files drop the pence, \"2625\") or with stray spaces. One standard set by header " +
                "and column name; no data cell changes. The cells \"NULL\" (department or description not recorded) are kept as published (108,351 description cells).",
            "Redaction: 633,837 of 2,398,241 rows (26.4%) have the payee \"Redacted Personal Data\" (2,374 of them in misspellings the plain test missed: \"Redated\", \"Redcated\", \"Dedacted\" Personal Data, and the word \"Null\"; caught since 4 October 2026) and are excluded from the schedules and counted in the export. The signed total is GBP 15,643,217,474.79 " +
                "(9,923 negative rows, GBP -23.5m); 5,892 blank lines in the 2024-25 Q2 file are dropped.",
            "COVERAGE VARIES BY QUARTER, SO TOTALS ARE NOT COMPARABLE ACROSS QUARTERS: HM Revenue & Customs lines (about 300 and GBP 50 to 71m a quarter from January 2018 to March 2023) are absent from 8 " +
                "of the 37 quarters (April to December 2017, October to December 2020, April to June 2025, October 2025 and January to June 2026), and fall to 34 to 90 lines from July 2023; Teachers Pensions is absent from 9. " +
                "October to December 2020 totals GBP 143.4m against GBP 352.2m and 363.4m either side, and has neither payee. October to December 2018 has 127,205 rows against 73,685 and 46,374 either side, " +
                "11,467 of them \"Staff Expenses\" (1,232 and 1,494 in the quarters either side).",
            "THE EXACT-REPEAT TEST WAS BLIND FOR ONE FILE: April to June 2018 stamps every row with its own extract timestamp (20180724135050.0726188+01:00), so all 54,248 rows looked unique (0 repeats; every " +
                "other quarter 4.6% to 17.5%). The \"Extract Date\" cell is now left out of the repeat test; the file reads 6,026 repeats, 11.1%. January to March 2018 has two " +
                "extract times a minute apart and gains 60. No other council's row changed.",
            "Payment dates are payment-run days (61 to 67 distinct dates a quarter). No line appears in two files by payee, date, amount and description (0 of 1,764,404).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "47 files downloaded, 37 loaded, 2,404,133 raw records, 5,892 blank lines dropped, 2,398,241 mapped rows (signed total GBP 15,643,217,474.79 raw and re-read), 633,837 redacted excluded, " +
            "1,764,404 retained: retained + redacted = export rows. Schedule A 0, Schedule D 0 (no transaction number). A random sample of 300 exported rows: all 300 were found by payee, date and amount in the original " +
            "downloads. Schedule B random hand-checks 119 of 119 against the raw files (30 open, 30 surplus, 30 catch-up, 29 of 29 reversed).",
        FoiContactEmail: null);

    /// <summary>Council #21 (Session 38). Newcastle City Council: one CSV a month on its own site, January 2018 on (earlier years are on Data Mill North to December 2016 only);
    /// payments over GBP 250, the total excludes VAT.</summary>
    public static readonly SupportedCouncil Newcastle = new(
        Name: "Newcastle City Council",
        TransparencyPageUrl: "https://www.newcastle.gov.uk/local-government/access-information-and-data/open-data/payments-over-ps250-data-sets",
        HowToFindTheFile:
            "On Newcastle City Council's \"Payments over £250 data sets\" page every month is a CSV and a PDF; for 2018 to early 2021 there are two CSVs a month (\"v2\" is the revised one). " +
            "Download the CSVs and drop them onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Internal Ref",
            Supplier: "Supplier Name",
            PayDate: "Paid Date",
            Description: "Cost Centre Name",
            ServiceArea: "Service Area",
            Net: "Total",
            Gross: "Total",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2018, 1, 2026, 7)
            .Select(t => new CouncilYearFile(t, "https://www.newcastle.gov.uk/local-government/access-information-and-data/open-data/payments-over-ps250-data-sets", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "144 CSV links on the page, January 2018 to July 2026 (103 months, none missing); the threshold is GBP 250 and the total excludes VAT. Nothing earlier than January 2018 is on the " +
                "page (Data Mill North's copy of the series stops at December 2016, so 2017 is not available). Each file is tagged by the month its own dates hold.",
            "TWO FILES A MONTH FROM JANUARY 2018 TO MAY 2021 (41 months): the original and a \"v2\". The council does not say what changed; the v2 file is used and the difference is reported. " +
                "In 39 of the 41 pairs the two files have the same number of rows and the same total, and differ in number formatting (97972.2 against 97972.20), row order, the header " +
                "(\"Total\" against \"Total (excludes VAT)\") and some payee labels (62,187 \"Redacted Personal Data\" rows in the 41 originals against 61,515 in the 41 v2 files). October 2019: v2 " +
                "has one more line, an amount-only footer of 62,139,328.17 equal to the rest of the file, and names payees (for example CRAGHALL) that the original shows as redacted. " +
                "March 2020: the original has 7,574 data rows, footer included (GBP 62,782,850.49), and v2 7,573 (GBP 62,762,800.49), a difference of GBP 20,050.00 over a few rows.",
            "Layout: a title row (\"Newcastle City Council Invoices over £250 paid in April 2018\") and a blank row above the header; \"Total\" or \"Total (excludes VAT)\" for the amount; 22 files " +
                "add \"Capital Code\" and \"Capital Code Name\" columns; 133,785 blank lines in all are dropped. Dates are payment dates (11-Apr-2018, 03/03/2020, " +
                "02/06/2026), every row inside its file's month. Service names carry trailing spaces, trimmed.",
            "One amount column per row, mapped as both Net and Gross: no VAT split, so Schedule A is empty by construction. 30,456 rows are negative (GBP -38,485,648.20). Two lines are not " +
                "payments (the October 2019 footer, one in March 2024) and are dropped.",
            "Redaction: 169,623 of 647,513 rows (26.2%) have the payee \"Redacted Personal Data\" and are excluded from the schedules and counted in the export.",
            "\"Internal Ref\" repeats across the lines of one payment (52,921 of 518,231 (file, number) groups have more than one line); no number carries more than one payee, so Schedule D is empty " +
                "(0 transactions). Same line in two files by number, amount and date: 0 keys.",
            "THE BIGGEST OPEN GROUPS ARE GRANT-SIZED, NOT REPEATS: \"MISCELLANEOUS PAYMENTS BACS\" is the payee of every Small Business Grant Fund payment (703 lines of GBP 10,000.00 paid on 6 April 2020 and " +
                "other days) and Retail, Hospitality and Leisure Grant payment (217 lines of GBP 25,000.00 on 30 March 2020): one label standing for many businesses, grouped by the engine as one payee " +
                "paying the same amount again and again. They stay open and unflagged.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "144 files downloaded, 103 months, 41 original files left out in favour of their v2 revisions, 781,300 raw records, 133,785 blank lines dropped, 647,515 prepared rows (signed total " +
            "GBP 5,086,428,619.14 raw and re-read, footers included), 2 non-payment lines dropped by the mapping, 647,513 mapped rows (GBP 5,024,289,290.97), 169,623 redacted excluded, " +
            "477,890 retained: retained + redacted = export rows. Schedule A 0, Schedule D 0. Schedule B 13,439 groups / 48,078 member rows / GBP 160.14m: Unclear 12,026, standing-payment " +
            "surplus 493, catch-up 920; random hand-checks of 30 groups: 30/30 in each class against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #20 (Session 38). Wirral Council: one CSV a month on its own site (and data.gov.uk), April 2022 on; a title row above the header.</summary>
    public static readonly SupportedCouncil Wirral = new(
        Name: "Wirral Metropolitan Borough Council",
        TransparencyPageUrl: "https://www.wirral.gov.uk/about-council/budgets-and-spending/payments-suppliers-and-agents",
        HowToFindTheFile:
            "On Wirral Council's \"Payments to suppliers and agents\" page open each year's page (2022, 2023-2024, 2024, 2025, 2026): every month has a CSV and a PDF. Download the CSV of " +
            "each month and drop the files onto this page. December 2022 is published only as a legacy Excel file (.xls); it is already loaded in the data shown here.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Paid Date",
            Description: "Description",
            ServiceArea: "Department",
            Net: "Paid Amount",
            Gross: "Paid Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Cost Centre"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2022, 4, 2026, 6)
            .Select(t => new CouncilYearFile(t, "https://www.wirral.gov.uk/about-council/budgets-and-spending/payments-suppliers-and-agents", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "51 monthly files, April 2022 to June 2026, from the council's five year pages (2022, 2023-2024, 2024, 2025, 2026; the same files are on data.gov.uk). 47 are CSV, three " +
                "(January, February and March 2023) are Excel workbooks and one (December 2022) is a legacy Excel 97-2003 (.xls) file; file names change constantly (\"spend-report-april-2022\", \"over-ps500-payments-supplier-august-2025\", " +
                "\"december-2024-amended-c\"), so each file is tagged by the month its own payment dates hold. The December 2022 file has 12,603 rows (GBP 36.09m, paid 1 to 23 December 2022), more than the months around it, because one supplier, Potensial Ltd, has 2,570 lines in four transactions all paid 20 December 2022 (63 to 82 lines in other months). " +
                "The .xls was read with one reader only; the checks on it are its own row count, total and dates.",
            "March 2023 is saved as \"Strict Open XML\" (a different set of namespaces), which the xlsx reader could not open; the reader now rewrites those namespaces to the usual ones before " +
                "parsing. January and February 2023 store the paid date as an Excel serial (44931), written dd/MM/yyyy by the scanner; the other months write 04/04/2022 or 12-JAN-2024.",
            "A title row (\"Payments for Publishing for invoices paid between 01-JAN-2024 and 31-JAN-2024\") sits above the header; header names vary (\" Paid Date\", \"Department \", \"Payment Number\" " +
                "for \"Transaction Number\" and \"Line Amount\" for \"Paid Amount\" in two months, \"Subjective Desc\" for \"Description\"), some files have up to 230 trailing empty header cells: one standard set " +
                "by header; no data cell changes.",
            "DECEMBER 2025 PUBLISHES NO PAID DATE: the header lists \"Paid Date\" but the 4,986 rows have no such cell, so the amount sits under \"Paid Date\" and the department under \"Paid Amount\". " +
                "The scanner inserts an empty date cell (applied only when nearly every row has a number where the date belongs and text where the amount belongs); the file " +
                "is tagged 2025-12 by its name and its rows have no date (they are in the file total and not in any financial year; 2025-26 has no published outturn anyway).",
            "One amount column, \"Paid Amount\" (written 6,840.00 or 9647.5), mapped as both Net and Gross; \"Irrecoverable VAT\" is a separate, mostly empty column kept in the export, so there is no " +
                "VAT split and Schedule A is empty by construction. 8,593 rows are negative (GBP -25,906,192.90).",
            "The transaction number repeats across the lines of one payment (35,282 of 100,999 (file, number) groups have more than one line, one line per cost centre); 16 numbers carry more than one " +
                "payee and 15 more than one payment date, which is Schedule D's 13 transactions / 112 lines. Because a transaction is the whole payment, Schedule B (the same supplier, total, description and " +
                "date under different numbers) is small here: 38 groups.",
            "Redaction: 14,755 of 341,203 rows (4.3%) have a redacted payee (8 of them spelt \"REDACETED\" or \"REEDACTED\", caught since 4 October 2026) and are excluded from the schedules and counted in the export. Same line in two files by number, amount and date: 0 keys.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "51 files downloaded and loaded (47 CSV, 3 workbooks and one legacy .xls), 51 months, 341,208 raw records, 5 blank lines dropped, 341,203 mapped rows (signed total GBP 2,485,540,161.27 raw and " +
            "re-read), 14,755 redacted excluded, 326,448 retained: retained + redacted = export rows. Schedule A 0, Schedule D 13 transactions / 112 lines. Schedule B 38 groups / 88 member rows / " +
            "GBP 0.26m: Unclear 37, catch-up 1; random hand-checks 30/30 for the 37 Unclear and 1/1 for the catch-up against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #19 (Session 38). Nottingham City Council: annual Excel workbooks on its Data Hub page (one financial year each; one workbook holds 2016-17 to
    /// 2022-23 as seven sheets); seven columns, no VAT, purchase card transactions included.</summary>
    public static readonly SupportedCouncil Nottingham = new(
        Name: "Nottingham City Council",
        TransparencyPageUrl: "https://www.nottinghamcity.gov.uk/your-council/about-the-council/access-to-information/nottingham-data-hub/",
        HowToFindTheFile:
            "On Nottingham City Council's Data Hub page open \"Payments to Suppliers\": one Excel workbook per financial year from 2023-24 (the newest is a first quarter), and one workbook " +
            "named \"Payments to Suppliers 2016 - 2023\" with a sheet for each year. Download them and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Expenditure Category",
            ServiceArea: "Department",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Enumerable.Range(2017, 10).Select(y => "FY" + y + "-" + ((y + 1) % 100).ToString("00"))
            .Select(t => new CouncilYearFile(t, "https://www.nottinghamcity.gov.uk/your-council/about-the-council/access-to-information/nottingham-data-hub/", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "Ten units from five downloads on the Data Hub page, each a financial year: one workbook with a sheet per year (the link text says \"Payments to Suppliers 2016 - 2023\", the file is " +
                "named \"october-2023\") holding 2016-17 to 2022-23, then 2023-24, 2024-25 and 2025-26 (the file name ends \"-f\") as separate workbooks, and 2026-27 as a first-quarter workbook " +
                "(April to the first days of July 2026, 10,141 rows). The 2016-17 sheet is not loaded (the budget test starts at 2017-18). Each unit is tagged FY<year>-<yy>. The page says the " +
                "data is updated quarterly and \"excluding VAT\"; purchase card transactions are in the same files.",
            "THE TRANSACTION NUMBER IS OFTEN A PAYMENT METHOD, NOT A NUMBER: 46,193 of 336,170 rows (13.7%) carry \"CHAPS\" (33,462), \"P CARD\" (12,468) or \"FOREIGN\" (263), shared by thousands of " +
                "unrelated payments. Left alone, every payment of one supplier by CHAPS in a year would be one \"transaction\"; `prep` leaves them and the Prepare step gives each its own number " +
                "\"(no number: CHAPS) N\". Real numbers repeat across lines (52,661 of 215,072 (file, number) groups).",
            "Dates are payment dates written yyyymmdd; each financial year's file also holds the first days of the next April (18 to 46 rows dated 3 to 9 April, 242 rows in all) and a few " +
                "earlier rows (the 2019-20 sheet has a card payment dated 14 December 2018). One row of 2019-20 is an empty placeholder (payee \"REDACTED GENERAL SUPPLIER\", no number, date or amount) and is dropped by the mapping; one mapped row of that year has no payment date.",
            "One signed amount column \"Net Amount\" (excluding VAT, per the page), mapped as both Net and Gross: no VAT split, so Schedule A is empty by construction. 6,599 rows are negative " +
                "(GBP -361,810,035.76). Redaction: 11,532 of 336,170 rows (3.4%) have the payee \"REDACTED PERSONAL DATA\" (one of them, \"REDACTED GENERAL SUPPLIER\" with no number, no date and 0.00, was left out until 4 October 2026) and are excluded from the schedules and counted in the export.",
            "THE 2020-21 SHEET LISTS ALMOST EVERY LINE TWICE: its 62,420 rows hold 29,828 distinct lines (28,895 occur exactly twice, 686 four times and up to twenty-two times for a few card " +
                "lines), signed total GBP 921.43m as published, about twice the next year's total (GBP 472.05m). Rows with a real number repeat exactly (the file-duplication scan flags the sheet: 19,794 exact " +
                "repeats of numbered rows); rows numbered by the Prepare step (CHAPS, P CARD) repeat in every other column. The same sheet has 32 rows dated April 2020 and none dated May 2020 (its payments run March 2020, then June 2020 to March 2021), so 2020-21 has no twelve covered months; " +
                "2021-22 has 628 rows dated April 2021 against about 1,400 needed, the other reason a year is not eligible.",
            "THE VALUE OF THE YEARS IS NOT STEADY AND THE FILE DOES NOT SAY WHY: signed total GBP 1.53bn (2017-18), 1.48bn, 1.16bn, 0.92bn (2020-21, 62,420 rows), 0.47bn (2021-22, 26,525 rows), " +
                "0.55bn, 0.52bn, 0.57bn, 0.64bn (2025-26). Same line in two files by number, amount and date: 0 keys (the placeholders cannot be matched).",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "10 units, 336,170 raw records, no blank lines, 336,170 mapped rows (signed total GBP 8,033,541,333.71 raw and re-read), 11,532 redacted excluded, " +
            "324,638 retained: retained + redacted = export rows. Schedule A 0, Schedule D 2 transactions / 4 lines. Schedule B 12,141 groups / 25,368 member rows / GBP 373.65m: Unclear 12,054, " +
            "standing-payment surplus 22, catch-up 51, reversed the same day 14; random hand-checks 30/30 in each class where there were 30 (22/22 and 14/14 for the two small ones) " +
            "against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #18 (Session 38). Cornwall Council: one CSV (and a duplicate Excel copy) a month on its own site, April 2019 on; a voucher number in
    /// some months only; three amount columns (line, invoice, invoice net).</summary>
    public static readonly SupportedCouncil Cornwall = new(
        Name: "Cornwall Council",
        TransparencyPageUrl: "https://www.cornwall.gov.uk/the-council-and-democracy/council-spending-and-finance/payments-to-suppliers-where-the-invoiced-payments-are-greater-than-or-equal-to-500/",
        HowToFindTheFile:
            "On Cornwall Council's \"Payments to suppliers where the invoiced payments are greater than or equal to £500\" page each month has an Excel and a CSV link " +
            "(from April 2019; file names differ from month to month). Download the CSV of each month and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Voucher Number",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Expense Description",
            ServiceArea: "Service",
            Net: "Line Amount",
            Gross: "Line Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Cost Centre Description"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2019, 4, 2026, 2)
            .Select(t => new CouncilYearFile(t, "https://www.cornwall.gov.uk/the-council-and-democracy/council-spending-and-finance/payments-to-suppliers-where-the-invoiced-payments-are-greater-than-or-equal-to-500/", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "83 months, April 2019 to February 2026, none missing; the page links each month twice (an Excel and a CSV) and file names change from month to month (\"april-2020-spending-" +
                "csv-format\", \"for-publishing-over-500-payables-to-suppliers-june-2021-msdos\", \"final-approved-collated-july-2021-report-for-publishing\"), so each file is tagged by the " +
                "month its payment dates hold. TWO MONTHS ARE ONLY AVAILABLE AS WORKBOOKS: the links labelled CSV for February 2020 and July 2024 lead to .xlsx files. Five months have a " +
                "second copy (March 2020, February 2020, December 2024, July 2024, March 2025: the \"excel\" file saved with a .csv name, or a second workbook) that holds the same rows " +
                "apart from number formatting (768 against 768.00), a date written two ways (28-Feb-2020, 28/02/2020) or an en dash lost to \"?\"; one copy is loaded.",
            "THREE LAYOUTS, STRAY SPACES, UPPER CASE: 2019-20 has SUPPLIER_NAME, VOUCHER_NUM, PAYMENT_DATE, LINE_AMOUNT, INVOICE_AMOUNT and \"Net AMOUNT\"; later files use the same " +
                "fields in normal case with leading spaces in the header cells; from July 2025 the entity name, cost centre and project columns are gone and the cost centre text is in " +
                "\"Description\"; two columns are both called \"Description\" (the cost centre's, then the expense's after the code in \"Subjective\"). " +
                "The scanner gives one standard set; no data cell changes except trimming spaces.",
            "FEBRUARY 2022 WRITES DATES d/M/yy (\"25/2/22\"): the engine's list needs four-digit years and a US-style fallback read \"8/2/22\" as 2 August 2022 (7,819 rows came out as an unreadable " +
                "date and the rest in August and October). `prep` writes them dd/MM/yyyy; checked: every payment date of that file now falls in February 2022.",
            "THE VOUCHER NUMBER IS PUBLISHED IN 19 OF 83 FILES ONLY (2019-20 and some later months). The other 64 files get a per-row placeholder \"(no number published) N\", so there " +
                "a repeated payment can only be seen by supplier, amount, date and description (Schedule B), and a multi-line invoice appears as separate rows; Schedule A is empty by " +
                "construction. Where it exists the voucher is the council's own payment number and covers many lines (up to 573 lines of one agency); 97 vouchers carry more than one payee " +
                "string, mostly one company spelt two ways (\"Need-A-Cab Taxis\" and \"Taxi Services (Plymouth) Ltd t/a Need-A-Cab Taxis\"): Schedule D's 97 transactions / 1,905 lines.",
            "THREE AMOUNT COLUMNS, ONE USED: \"Line Amount\" (one per row, the engine's Net and Gross), \"Invoice Amount\" and \"Net Amount\" (an invoice's gross and net totals repeated on every line " +
                "of the invoice and blank on continuation lines from 2025). The two totals do not sum from the lines (in April 2025 none of 6,115 vouchers' lines add up to the Net Amount; one " +
                "agency voucher has 573 lines adding to 30,008.94 against an invoice amount of 27,272.64), and summing them would count an invoice once per line (July 2024: 743.65m as Net Amount, " +
                "124.64m as Line Amount), so the file total is the sum of Line Amount. Both totals stay in the export's other columns.",
            "SIGNED LINES: 166,864 rows are negative (GBP -14,236,017,316.79) against GBP 21.07bn of positive lines, net GBP 6,834,646,379.76. The large negative lines sit beside bigger lines of " +
                "the same supplier and date (for example MWJV, May 2020: lines of -1,963,826.65 and -2,815,244.21 on an invoice amount of 195,992.22; Rosenbauer UK, January 2020); the file does not " +
                "say what they are. Equal-and-opposite pairs are Schedule B's same-day reversal class (1,345 groups).",
            "Redaction: 334,953 of 1,936,816 rows (17.3%) have the payee \"Redacted - Personal data\" (or similar) and are excluded from the schedules and counted in the export. How the count is made: a payee that contains REDACT counts, and so does a payee labelled \"personal data\" (37 rows in 13 spellings, such as \"A2B Taxis St Austell- personal data\" and \"Personal Data\"); three real suppliers whose names contain Redactive (Media Sales, Events and Publishing, 11 rows) do not. A plain text search of the payee names for \"redact\" therefore finds 334,927 rows, 26 fewer than the 334,953.",
            "SOME LINES ARE LISTED TWICE, IN ONE FILE OR IN TWO: by number (voucher, amount, date) 210 lines are in both the September and October 2019 files (the October file holds 242 rows dated " +
                "September 2019). By content (supplier, amount, date, description, service; the 64 files with no voucher cannot be compared by number) the November 2021 file holds 8,846 rows dated " +
                "October 2021 and 6,512 of its retained lines (35%, GBP 19.80m of absolute value) match a line in the October file; the September 2021 file shares 627 lines (GBP 8.10m) with August; " +
                "the April 2025 file lists 3,386 of its 16,748 retained rows twice (20%, GBP 20.17m) and June 2025 561 (2.7%). The file cannot say whether such a line was paid twice or published twice. " +
                "Dates are payment dates: 99.5% of rows fall in their file's month, 9,444 in the month before, 865 after.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "88 downloaded files (84 CSV links and 4 workbooks), 83 months tagged, 5 copies left out (same rows), 1,936,818 raw records, 2 blank lines dropped, 1,936,816 prepared and mapped rows " +
            "(signed total GBP 6,834,646,379.76 raw and re-read), 334,953 redacted excluded, 1,601,863 retained: retained + redacted = export rows. Schedule A 0 (one amount per line), Schedule D 97 " +
            "transactions / 1,905 lines. Schedule B 110,661 groups / 330,586 member rows / GBP 774.09m: Unclear 98,412, standing-payment surplus 4,517, catch-up 6,387, reversed the same day 1,345; " +
            "random hand-checks of 30 groups: 30/30 in each class against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #17 (Session 38). Leicester City Council: one dataset per calendar year on its Opendatasoft open-data portal, 2015 on;
    /// the id is a row number, the date is a payment date, VAT is a separate column.</summary>
    public static readonly SupportedCouncil Leicester = new(
        Name: "Leicester City Council",
        TransparencyPageUrl: "https://data.leicester.gov.uk/explore/assets/expenditure-exceeding-ps500-2025/",
        HowToFindTheFile:
            "On Leicester's open-data portal (data.leicester.gov.uk) search for \"Expenditure exceeding £500\": there is one dataset per calendar year. Open each year " +
            "(2017 to the current year), choose Export, CSV, and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "unique_id",
            Supplier: "beneficiary",
            PayDate: "payment_date",
            Description: "purpose_of_expenditure",
            ServiceArea: "department",
            Net: "amount",
            Gross: "amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "merchant_category"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Enumerable.Range(2017, 10).Select(y => $"CY{y}")
            .Select(t => new CouncilYearFile(t, "https://data.leicester.gov.uk/explore/assets/expenditure-exceeding-ps500-2025/", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "Ten datasets on the open-data portal, one per calendar year: 2017 to 2025 and 2026 to August. Each is downloaded through the portal's CSV export and tagged " +
                "CY<year>. The portal also holds 2015 and 2016 (not used). The page is a portal listing, not a file list, so the export link is the portal's own API address.",
            "THE FOUR EARLIEST DATASETS (2017 to 2020) HAVE NO ROW IDENTIFIER: 8 columns (payment_date, department, beneficiary, purpose_of_expenditure, amount, vat, " +
                "vat_code, merchant_category). From 2021 a ninth column unique_id appears, which is a row counter within the dataset (81,602 on the last day of 2024, 81,891 rows), " +
                "not a council ledger reference. `prep leicester` adds a per-row placeholder \"(no number published) N\" to the four early files; two 2021 rows (a PayPoint bank " +
                "charge and a Marshalls Street Furniture postage line, both 30 July 2021) have a blank unique_id and are numbered \"(no number) N\". Schedule A is empty by " +
                "construction and transaction-number twins cannot be found; Schedule B (supplier, amount, date, description) works. Four unique_id values appear twice with two " +
                "different payees and dates, which is Schedule D's 4 transactions / 8 lines.",
            "THE AMOUNT IS NET OF VAT AND VAT IS A SEPARATE COLUMN: where \"vat\" is not zero it is 20% of \"amount\" (16,245 of 17,105 such rows in 2024) or 5% (860), " +
                "so gross is amount plus vat. The engine's Net and Gross both read \"amount\"; the vat column is kept in the export's other columns.",
            "Dates are payment dates (ISO yyyy-mm-dd). The calendar-year datasets overlap their neighbours by a handful of rows: 391 rows of 692,704 are dated outside their " +
                "dataset's year (389 after it, 2 before it). Same line in two datasets by number, amount and date: not testable by number (no ids before 2021); see the content check.",
            "Redaction: 14,468 of 692,704 rows (2.1%) have the payee \"Redacted\" and are excluded from the schedules and counted in the export. 4,203 rows are negative " +
                "(GBP -11,101,654.24 in all).",
            "The datasets were last refreshed on different dates (2020: February 2021; 2021: October 2022; 2023: February 2024; 2024: May 2025; 2025: January 2026; 2026: " +
                "September 2026), per the portal's own processing dates; the files here were downloaded on 4 October 2026.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "10 datasets, 692,704 raw records, no blank lines, 692,704 mapped rows (signed total GBP 4,113,517,967.18 raw and re-read), 14,468 redacted excluded, 678,236 retained: " +
            "retained + redacted = export rows. Schedule A 0 (one amount, no row ids before 2021), Schedule D 4 transactions / 8 lines. Schedule B 57,581 groups / 241,201 member " +
            "rows / GBP 614.30m: Unclear 54,373, standing-payment surplus 1,567, catch-up 1,623, reversed the same day 18; random hand-checks 30/30 in each class (18/18 for the last) " +
            "against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #16 (Session 38). Kirklees Council: one Excel workbook a month on its own site, April 2017 on; SAP document numbers; the
    /// amount is published excluding VAT.</summary>
    public static readonly SupportedCouncil Kirklees = new(
        Name: "Kirklees Council",
        TransparencyPageUrl: "https://www.kirklees.gov.uk/beta/information-and-data/expenditure-data.aspx",
        HowToFindTheFile:
            "On Kirklees Council's \"Expenditure data\" page every month is an Excel workbook (\"Published Data\" with the month-end date in the name), from April 2017; " +
            "the page also lists purchase card workbooks, which are a separate dataset. Download each month's published-data workbook and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Vendor Name",
            PayDate: "Payment Date",
            Description: "Proclass Description",
            ServiceArea: "Cost Centre Description",
            Net: "Amount Excluding VAT",
            Gross: "Amount Excluding VAT",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2017, 4, 2026, 8)
            .Select(t => new CouncilYearFile(t, "https://www.kirklees.gov.uk/beta/information-and-data/expenditure-data.aspx", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "113 monthly Excel workbooks on the page, April 2017 to August 2026, none missing (the page's purchase card workbooks are a different dataset and are not " +
                "loaded). File names are not reliable (\"Published-Data-2017-04-30\", \"KC-Published-Data -2023-11-30\", \"KC Published Data - 2025 12 31\", " +
                "\"KC-published-data-2023-07-31\"), so each file is tagged by the month its own payment dates hold; every file holds exactly one month and each month " +
                "appears once.",
            "The payment date is an Excel date serial (45446); `prep kirklees` writes it dd/MM/yyyy. 14 columns in every file: Payment Date, Transaction Number (a 10-digit SAP " +
                "document number), Amount Excluding VAT, Vendor Number, Vendor Name, Company Number, Debit/Credit Indicator, PO Number, Document Type, Cost Centre, " +
                "Cost Centre Description, Proclass Code, Proclass Description, Purpose of Spend.",
            "One signed amount column \"Amount Excluding VAT\", mapped as both Net and Gross: there is no VAT split, so Schedule A is empty by construction. 10,214 rows are " +
                "negative (GBP -199,165,889.89 in all).",
            "THE LARGEST REDACTION OF ANY COUNCIL SO FAR: 177,385 of 516,440 rows (34.3%) have the payee \"REDACTED DATA\" or \"REDACTED PERSONAL DATA\" and are excluded from the " +
                "schedules (they are counted in the export and in every total). 79,980 rows carry no Proclass description.",
            "The transaction number repeats across the lines of one document in 2,523 of 513,917 (file, number) groups, all of one payee; no number carries more than one " +
                "payee, so Schedule D is empty (0 transactions).",
            "JUNE 2018 LISTS EVERY ROW TWICE: the file has 5,038 rows, 2,519 distinct rows each present exactly twice (GBP 58.98m as published, about twice the month's spend; May and July 2018 have 2,976 and " +
                "2,824 rows). The file-duplication scan flags it (50.0% exact repeats); the budget test counts the second copies in tier T_A. Dates are payment dates, every row inside its file's month. " +
                "Same line in two files by number, amount and date: 0.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "113 files, 539,096 raw records, 22,656 blank lines dropped, 516,440 mapped rows (signed total GBP 4,833,367,911.51 raw and re-read), 177,385 redacted excluded, " +
            "339,055 retained: retained + redacted = export rows. Same-key repeats across files: 0. Schedule A 0, Schedule D 0. Schedule B 11,623 groups / 33,485 member rows / " +
            "GBP 119.44m: Unclear 9,828, standing-payment surplus 244, catch-up 1,487, reversed the same day 64; random hand-checks of 30 groups: 30/30 in each class (30 of 64 for the " +
            "last) against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #15 (Session 38). Durham County Council: one CSV a month on its own site, April 2022 on (the page keeps the current
    /// year and the three before it); real transaction numbers; the amount is published excluding VAT.</summary>
    public static readonly SupportedCouncil Durham = new(
        Name: "Durham County Council",
        TransparencyPageUrl: "https://www.durham.gov.uk/article/2437/Payments-to-suppliers-over-500",
        HowToFindTheFile:
            "On Durham County Council's \"Payments to suppliers over £500\" page each month is a CSV link (about 1 MB); the page keeps the current financial year " +
            "and the three before it. Download each month and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Detailed Expense Type",
            ServiceArea: "Service Area",
            Net: "Amount Exc VAT",
            Gross: "Amount Exc VAT",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Service Division"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2022, 4, 2026, 8, "2023-06")
            .Select(t => new CouncilYearFile(t, "https://www.durham.gov.uk/article/2437/Payments-to-suppliers-over-500", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "53 CSV links on the page, April 2022 to August 2026 (the page keeps the current year and the three before it; nothing earlier is on it). File names are " +
                "unreliable, so each file is tagged by the month its own payment dates hold: the file named \"...February2026\" with 6,379 rows is March 2026, and " +
                "\"PaymentsOver500December2024\" is listed out of order at the end.",
            "JUNE 2023 IS NOT PUBLISHED: the file named June 2023 holds exactly the rows of the July 2023 file (5,232 rows, GBP 56,527,817.14, every row dated July), so there " +
                "is no June 2023 file and financial year 2023-24 cannot have twelve covered months. 52 files are loaded.",
            "The January 2024 file ends its lines with a bare carriage return (5,958 of them and one line feed), which a line-based reader sees as one line of 47,665 cells; " +
                "`prep durham` reads it with LF line ends (5,958 data rows) and changes nothing else.",
            "One amount column, \"Amount Exc VAT\" (net of VAT; written \"£7,339.30\" in a few files), mapped as both Net and Gross: there is no VAT split, so Schedule A is " +
                "empty by construction. There are no negative amounts in the whole file: credit notes are not published, so a reversal cannot appear (no same-day reversal class).",
            "EVERY TRANSACTION NUMBER IS UNIQUE TO ONE ROW (285,258 numbers in 285,258 rows, a ledger reference with the line in it, for example 4219343-RES-12-2023-324), so " +
                "Schedule D is empty by construction (0 transactions) and a repeated payment can only be seen by supplier, amount, date and description (Schedule B).",
            "Dates are payment dates, every row inside its file's month. Payee names end with a full stop in the file (\"HARROGATE & DISTRICT NHS FOUNDATION TRUST.\"); names are " +
                "shown as published and the supplier key ignores the punctuation.",
            "Redaction: 28,625 of 285,258 rows (10.0%) have the payee \"REDACTED - PAYMENT TO INDIVIDUAL\" and are excluded from the schedules and counted in the export.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "52 files, 285,258 raw records, no blank lines, 285,258 mapped rows (signed total GBP 3,327,355,822.70 raw and re-read), 28,625 redacted excluded, 256,633 retained: " +
            "retained + redacted = export rows; one file left out as an exact copy of another, one file read with LF line ends. Same-key repeats across files: 0. Schedule A 0, " +
            "Schedule D 0 (unique numbers). Schedule B 8,076 groups / 25,538 member rows / GBP 84.37m: Unclear 7,191, standing-payment surplus 323, catch-up 562; random " +
            "hand-checks of 30 groups: 30/30 in each class against the raw files.",
        FoiContactEmail: null);

    /// <summary>Council #14 (Session 38). Coventry City Council: one CSV a month on its own site, April 2017 on; its transaction date is
    /// an invoice or posting date, not a payment date.</summary>
    public static readonly SupportedCouncil Coventry = new(
        Name: "Coventry City Council",
        TransparencyPageUrl: "https://www.coventry.gov.uk/downloads/download/818/spending_over_500",
        HowToFindTheFile:
            "On Coventry City Council's \"Spending over £500\" download page each month is a link to a CSV (the oldest April 2017). Download each month " +
            "and drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Transaction Date",
            Description: "Account Description",
            ServiceArea: "Directorate",
            Net: null,
            Gross: "Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: "Cost Centre Name"),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Months(2017, 4, 2026, 9)
            .Select(t => new CouncilYearFile(t, "https://www.coventry.gov.uk/downloads/download/818/spending_over_500", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "114 monthly CSVs on the council's download page, April 2017 to September 2026, none missing; each is tagged by the month in its own file name. " +
                "THE OCTOBER 2022 FILE IS AN EXCEL WORKBOOK SAVED WITH A .csv NAME (it begins with the zip signature); it is read as the workbook it is, " +
                "and written out as CSV like the others.",
            "Header layouts wander: more than 40 different header rows. The payee name is \"Supplier(T)\", \"Supplier (T)\", \"Supplier(T) 2\", \"Supplier T\", " +
                "\"Supper (T)\" or, once (June 2017), \"Check and edit column\"; the supplier code is plain \"Supplier\"; there are Supplier Group columns in some months, " +
                "a TT column pair in August 2021, extra ID columns in October 2019. The scanner gives one standard set; no data cell changes.",
            "THE SEVEN FILES FROM APRIL TO OCTOBER 2017 PUBLISH NO TRANSACTION NUMBER. Each row of those files gets a per-row placeholder \"(no number published) N\", so " +
                "Schedule A and D are empty for them by construction and transaction-number twins cannot be found there; Schedule B (supplier, amount, date, description) still works.",
            "THE DATE IS A TRANSACTION (INVOICE OR POSTING) DATE, NOT A PAYMENT DATE, and a month's file mixes months: of 1,099,011 rows 65.1% are dated in the file's own month, " +
                "26.7% one month earlier, 8.0% two to twelve months earlier, 0.2% more than twelve months earlier, 515 rows after the file's month (505 within twelve months; " +
                "10 more than a year ahead: typed years such as 2027, 2028, 2030, 2033). A note in the May 2018 file says some dates are the expected date of payment. " +
                "A financial year built from these dates is a year of transaction dates; one row (July 2017) has no date.",
            "ONE LINE OF GBP 31 BILLION IS IN THE FILE: Technogym UK Ltd, transaction 3389825, 9 April 2019, 31,053,485,253.81, with a line of " +
                "-31,053,475,253.81 against it; the two lines net to 10,000.00. They sit in the June 2019 file, which does not say why.",
            "Redaction: 122,685 of 1,099,011 mapped rows (11.2%) have the payee \"REDACTED PERSONAL DATA\" (foster care and client support lines) and are excluded from " +
                "the schedules and counted in the export. Amounts are one signed column \"Amount\" (written 4,406.00), mapped as both Net and Gross; no VAT split, so " +
                "Schedule A is empty by construction.",
            "NON-PAYMENT LINES: 8 rows are notes or footers, not payments (two notes in May and July 2018, for example \"line 3968 - this is a pro forma invoice\"; " +
                "an amount-only footer in February 2024 equal to the rest of the file, 42,857,653.94; two amount-only rows in January 2021, 13,947,624.97 and 50,863,925.09, " +
                "that do NOT equal the rest of the file, 38,626,156.41). The engine drops them.",
            "The transaction number repeats across the lines of one payment: 145,809 of 557,431 (file, number) groups have more than one line, mostly agency staffing " +
                "(one number carries 400 to 660 lines of one agency, several hundred distinct amounts). 175 numbers carry more than one payee and 13 more than one " +
                "date, which is Schedule D's 176 transactions / 1,149 lines (grant payment runs; care providers whose invoices carry different dates).",
            "Same line in two files: by number, supplier, amount and date, 0 keys. By content (supplier, amount, date, description, service) 4,755 retained lines (0.49%) match a line in an earlier " +
                "month's file under a different transaction number; 31 files are above their neighbours' rate, the largest September 2025 (415 lines, GBP 1.37m of absolute value, 373 of them in " +
                "August). The file cannot say whether those lines were re-listed or paid again.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "114 files, 1,100,140 raw records, 1,121 blank lines dropped, 1,099,019 prepared rows (signed total GBP 5,178,983,294.43 raw and re-read), 8 notes or footers " +
            "dropped by the mapping, 1,099,011 mapped rows, 122,685 redacted excluded, 976,326 retained: retained + redacted = export rows. Schedule A 0 (one amount per " +
            "line), Schedule D 176 transactions / 1,149 lines. Schedule B 30,180 groups / 105,448 member rows / GBP 350.57m: Unclear 28,214, standing-payment surplus 809, " +
            "catch-up 887, reversed the same day 270; random hand-checks of 30 groups: 30/30 in each class against the raw files.",
        FoiContactEmail: null);

    /// <summary>Quarter tags "YYYY-MM_MM" from the first quarter starting in y0/m0 to the last starting in y1/m1, minus any listed.</summary>
    internal static List<string> Quarters(int y0, int m0, int y1, int m1, params string[] except)
    {
        var tags = new List<string>();
        for (int ym = y0 * 12 + (m0 - 1); ym <= y1 * 12 + (m1 - 1); ym += 3)
        {
            string t = $"{ym / 12:0000}-{ym % 12 + 1:00}_{ym % 12 + 3:00}";
            if (!except.Contains(t)) tags.Add(t);
        }
        return tags;
    }

    /// <summary>Council #13 (Session 38). Wakefield MDC on Data Mill North: one CSV per quarter (an xlsx for 2018-19 Q1).</summary>
    public static readonly SupportedCouncil Wakefield = new(
        Name: "Wakefield Metropolitan District Council",
        TransparencyPageUrl: "https://datamillnorth.org/dataset/wakefield-council-spend-over-500-pounds",
        HowToFindTheFile:
            "On Data Mill North open \"Wakefield Council spend over £500\" and download each quarter's CSV (the page also lists a PDF of each " +
            "quarter and, separately, procurement card files: take the \"Supplier Spend\" or \"Spend over £500\" CSV). Drop the files onto this page.",
        Mapping: new ColumnMapping(
            TransactionId: "Transaction Number",
            Supplier: "Supplier Name",
            PayDate: "Payment Date",
            Description: "Purpose of Spend",
            ServiceArea: "Service Area",
            Net: "Net Amount",
            Gross: "Net Amount",
            VatAmount: null,
            VatType: null,
            CostCentreArea: null),
        IdScope: TransactionIdScope.CouncilWideUnique,
        GrossMeaning: GrossMeaning.PerLineAmount,
        Years: Quarters(2017, 4, 2026, 4, "2022-01_03")
            .Select(t => new CouncilYearFile(t, "https://datamillnorth.org/dataset/wakefield-council-spend-over-500-pounds", SourceFormat.Csv)).ToList(),
        KnownQuirks: new List<string>
        {
            "36 quarterly files on Data Mill North, Q1 2017-18 (April 2017) to Q1 2026-27 (June 2026): 35 CSV and one Excel workbook (Q1 2018-19). " +
                "THE JANUARY TO MARCH 2022 QUARTER (2021-22 Q4) IS NOT ON THE PAGE (only the procurement card file for it is), so financial year 2021-22 has no " +
                "full twelve months. Earlier quarters back to 2011-12 are on the page and were not used (the budget test starts at 2017-18). The page's " +
                "procurement card files are a separate dataset and are not loaded. Files are tagged by the quarter in their own name (2017-04_06 and so on).",
            "Three header layouts: to 2023-03 \"Payment Date, Transaction Number (TransNo), Net Amount\" with a Directorate and a SERCOP service; from " +
                "2023-04 \"Date, TransNo, Amount\" with a \"Cost Centre Narrative\" in place of the Directorate and no Supplier ID. `prep wakefield` gives them one " +
                "standard set (Payment Date, Transaction Number, Net Amount, Service Area, Purpose of Spend); a header name that appears twice (the " +
                "Procurement Classification and Class Code columns) gets \" (2)\". No data cell changes except trimming spaces and writing an Excel date serial as dd/MM/yyyy.",
            "THE NUMBER OF LINES CHANGES WITH THE PUBLISHING METHOD, NOT WITH THE SPENDING: about 7,000 to 10,000 lines a quarter in 2017-18 to 2018-19, " +
                "21,000 to 25,500 from 2019-20 (all transactions, not only those over GBP 500), 45,000 to 81,000 from 2021-22 to 2022-23, then 6,900 to 10,300 " +
                "from 2023-24 (April 2023) at a similar value. Row-level counts (Schedule B groups per quarter, twins) are therefore not comparable across the three eras.",
            "AMOUNT-ONLY FOOTER ROWS: 14 files end with one or two rows that hold only an amount (no number, date or payee). In 10 files they equal the rest of " +
                "the file to the penny; in three (2023-24 Q4, 2025-26 Q3, 2026-27 Q1) they differ (101,031,252.39 against 101,013,237.81; 84,952,921.08 against " +
                "86,375,063.52; 81,008,869.80 against 94,343,583.86). They are not payments: the engine drops them (18 rows) and they are not in any total.",
            "LINES WITH NO TRANSACTION NUMBER AND NO PAYEE: 2,229 lines with a payment date (payroll-type lines such as \"Service Pension Contribution (Employers)\" " +
                "and car allowances, by Directorate, for example 66 lines and GBP 4.86m in April to June 2017) carry no transaction number. The mapping would drop " +
                "them and take GBP 127.5m out of the file total, so the scanner gives each its own number \"(no number) N\"; with no payee they " +
                "are excluded from every schedule as blank-supplier rows, but they are exported and counted in the total.",
            "Redaction: 63,197 of 746,555 mapped lines (8.5%) have a redacted or blank payee (the council withholds the payee where it judges that necessary, Supplier ID \"Redacted\", plus the " +
                "2,229 unnumbered lines above) and are excluded from the schedules and counted in the export.",
            "TRANSACTION NUMBERS ARE COUNCIL-WIDE BUT ARE ALSO PAYMENT-RUN NUMBERS: 53,096 of 425,172 (file, number) groups have more than one line (the Seq No " +
                "distinguishes the lines; each line has its own amount, so the amount is per line); 147 numbers carry more than one payee and 295 more than one " +
                "payment date, which is Schedule D's 405 transactions / 7,886 lines. The biggest are children's services payment runs: 50 to 75 nursery education " +
                "funding lines to nurseries, 92 to 109 independent fostering agency lines to 23 to 29 agencies, care leavers rent, one number for 1,254 lines from a " +
                "single staffing agency. A shared number there is a batch, not a doubled payment.",
            "The council's own scheduled flows are among the biggest open Schedule B groups and are not errors: precept instalments to West Yorkshire Police, Fire & " +
                "Rescue and the Combined Authority booked as 9 to 11 consecutively numbered lines of one amount on one day (the largest, 11 x GBP 1,929,415.95 on 4 April 2022), " +
                "the same for town councils, and vehicle purchases in tranches (Dennis Eagle).",
            "Same line in two files: by number, amount and date 0; by content (supplier, amount, date, description, service) 131 retained lines (0.02%) match a line in an earlier quarter's file, one " +
                "file above its neighbours' rate.",
            "Not a payment date problem: a quarter's file holds payment dates from a few days before its first month to a few days after its last (for example 2017-04_06 " +
                "from 24 March to 30 June 2017); 18 transactions have no payment date published (the cell holds \"?\") and sit outside Schedule B's same-day grouping only.",
        },
        LastChecked: "2026-10-04",
        VerificationNote:
            "36 files, 748,997 raw records, 2,424 blank lines dropped, 746,573 prepared rows (signed total GBP 4,019,103,674.42 raw and re-read), of which 18 amount-only " +
            "footers dropped by the mapping, 746,555 mapped rows (GBP 2,885,602,189.86 excluding footers), 63,197 redacted or blank-payee excluded, 683,358 retained: " +
            "retained + redacted = export rows. Same-key repeats across files: 0 by number, amount and date, with and without the payee name. Schedule A 0 (one " +
            "amount per line), Schedule D 405 transactions / 7,886 lines. Schedule B 18,015 groups / 50,902 member rows / GBP 204.01m: Unclear 16,040, standing-payment surplus " +
            "521, catch-up 1,419, reversed the same day 35; random hand-checks 30/30 in each class (30 of 35 for the last) against the raw files.",
        FoiContactEmail: null);
}

/// <summary>Fixups for the Session 38 councils. Each is named after a real row found in a real file, with a regression test.</summary>
public static class NextFixups
{
    /// <summary>A header name that appears twice gets " (2)", " (3)" so a mapping by name is unambiguous.</summary>
    public static string[] CanonHeader(string[] header, Func<string, string> canon)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var o = new string[header.Length];
        for (int i = 0; i < header.Length; i++)
        {
            string c = canon(header[i]);
            if (c.Length == 0) c = "(blank column)";
            if (seen.TryGetValue(c, out var n)) { seen[c] = n + 1; o[i] = c + $" ({n + 1})"; }
            else { seen[c] = 1; o[i] = c; }
        }
        return o;
    }

    // ---- Nottingham (own site: annual workbooks, and one workbook with a sheet per financial year 2016-17 to 2022-23) ----
    /// <summary>Sheet or file name -> "FY2020-21". "Council Spend 2020 21" (a sheet), "payments-to-suppliers-2023-2024.xlsx", "...-2025-2026-f.xlsx", "...-2026-2027-q1.xlsx"; "" (skip) for a
    /// financial year starting before 2017 or a name with no year pair.</summary>
    public static string? NottinghamTag(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(20\d\d)[ \-_](\d{2,4})");
        if (!m.Success) return "";
        int y1 = int.Parse(m.Groups[1].Value);
        if (y1 < 2017) return "";
        return $"FY{y1}-{(y1 + 1) % 100:00}";
    }

    /// <summary>Nottingham's "Transaction Number" is often not a number but the way the payment was made ("P CARD", "CHAPS"): thousands of unrelated payments share it. Such a value
    /// is replaced by "(no number: P CARD) N" with N the row's position, so unrelated payments are not grouped as one transaction; numeric ids are left alone.</summary>
    public static List<string[]> NumberTheLabels(List<string[]> table, string idHeader)
    {
        if (table.Count == 0) return table;
        int iId = Array.FindIndex(table[0], h => h.Trim().TrimStart('\uFEFF').Equals(idHeader, StringComparison.OrdinalIgnoreCase));
        if (iId < 0) return table;
        var result = new List<string[]>(table.Count) { table[0] };
        for (int i = 1; i < table.Count; i++)
        {
            var r = table[i];
            string v = iId < r.Length ? r[iId].Trim() : "";
            if (v.Length > 0 && !v.All(char.IsDigit))
            {
                var copy = (string[])r.Clone();
                copy[iId] = $"(no number: {v}) {i}";
                result.Add(copy);
            }
            else result.Add(r);
        }
        return result;
    }
    // ---- Wirral (own site and data.gov.uk: one CSV a month from April 2022; a title row above the header) ----
    /// <summary>Wirral's header names vary by month (" Paid Date", "Department ", "Payment Number" for "Transaction Number", "Line Amount" for "Paid Amount", "Subjective Desc" for
    /// "Description"); one standard set. No data cell changes.</summary>
    public static string WirralCanon(string h)
    {
        string t = h.Trim();
        return t.ToLowerInvariant() switch
        {
            "supplier name" => "Supplier Name",
            "transaction number" or "payment number" => "Transaction Number",
            "paid date" => "Paid Date",
            "paid amount" or "line amount" => "Paid Amount",
            "department" => "Department",
            "cost centre" => "Cost Centre",
            "description" or "subjective desc" => "Description",
            "irrecoverable vat" => "Irrecoverable VAT",
            _ => t,
        };
    }

    /// <summary>File name -> "YYYY-MM": a month name then a two- or four-digit year ("april-25", "spend-report-april-2022", "over-ps500-october-25"); "" when none. Used only when a file has no
    /// payment dates (the dates, when present, decide).</summary>
    public static string? WirralTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName.ToLowerInvariant(), @"(january|february|march|april|may|june|july|august|september|october|november|december)[-_ ]?(\d{4}|\d{2})(?!\d)");
        if (!m.Success) return "";
        int mi = Array.IndexOf(MonthNames, m.Groups[1].Value) + 1;
        string y = m.Groups[2].Value.Length == 2 ? "20" + m.Groups[2].Value : m.Groups[2].Value;
        return $"{y}-{mi:00}";
    }

    /// <summary>Wirral's December 2025 file has the header "Supplier Name | Transaction Number | Paid Date | Paid Amount | ..." but its rows have NO date cell, so the amount sits under
    /// "Paid Date" and the department under "Paid Amount". When nearly every row has a money value where the date belongs, an empty date cell is inserted into each data row (the date
    /// stays unknown; nothing is invented) and the rows line up with the header again.</summary>
    public static List<string[]> WirralShift(List<string[]> table)
    {
        int hr = table.FindIndex(r => r.Any(c => c.Trim().Equals("Supplier Name", StringComparison.OrdinalIgnoreCase)));
        if (hr < 0) return table;
        int iDate = Array.FindIndex(table[hr], c => c.Trim().Equals("Paid Date", StringComparison.OrdinalIgnoreCase));
        if (iDate < 0) return table;
        var money = new System.Text.RegularExpressions.Regex(@"^-?\d[\d,]*(\.\d+)?$");   // 4521.12, 2825.7 and 9875 all occur
        var data = Enumerable.Range(hr + 1, table.Count - hr - 1).Where(i => table[i].Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (data.Count == 0 || data.Count(i => iDate + 1 < table[i].Length && money.IsMatch(table[i][iDate].Trim()) && table[i][iDate + 1].Trim().Length > 0 && !money.IsMatch(table[i][iDate + 1].Trim())) * 10 < data.Count * 9) return table;   // an amount where the date belongs AND text (the department) where the amount belongs
        var result = new List<string[]>(table);
        foreach (int i in data)
        {
            var r = table[i].ToList();
            r.Insert(iDate, "");
            result[i] = r.ToArray();
        }
        return result;
    }
    // ---- Newcastle (own site: one CSV a month from January 2018, "v2" revisions of the 2018 to 2021 months) ----
    /// <summary>"Total (excludes VAT)" and "Total" are the same column; one standard name. Other headers are already standard.</summary>
    public static string NewcastleCanon(string h)
    {
        string t = h.Trim();
        return t.StartsWith("Total", StringComparison.OrdinalIgnoreCase) ? "Total" : t;
    }

    // ---- Surrey County Council (surreyi.gov.uk, one CSV per quarter; SAP layout to 2022-23, ERP layout from 2023-24) ----
    /// <summary>Surrey's header spellings (Occured / Occurred / Incurred, upper and lower case, stray spaces, "LA Department") are made one standard set; the early files' "extractdate" /
    /// "extraced" is "Extract Date". No data cell changes.</summary>
    public static string SurreyCanon(string h)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " ");
        return t.ToLowerInvariant() switch
        {
            "date spend occurred" or "date spend occured" or "date spend incurred" => "Date Spend Occurred",
            "department incurring spend" or "la department incurring spend" => "Department Incurring Spend",
            "beneficiary name" => "Beneficiary Name",
            "summary of purpose of spend" => "Summary of Purpose of Spend",
            "gross amount" => "Gross Amount",
            "merchant category" => "Merchant Category",
            "text" => "Text",
            "document header text" => "Document Header Text",
            "extractdate" or "extraced" => "Extract Date",
            _ => t,
        };
    }

    /// <summary>Surrey file names "SAP_Spend_Data_Q4_2022-23.csv" and "ERP_Spend_Q1_2023-2024_.csv": Q1 is April to June of the first year named. Quarter tag "YYYY-MM_MM";
    /// "" (not in scope) before financial year 2017-18.</summary>
    public static string? SurreyTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"_Q(\d)_(\d{4})-\d{2,4}");
        if (!m.Success) return "";
        int q = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value);
        if (y < 2017 || q < 1 || q > 4) return "";
        int startMonth = new[] { 4, 7, 10, 1 }[q - 1];
        int year = q == 4 ? y + 1 : y;
        return $"{year}-{startMonth:00}_{startMonth + 2:00}";
    }

    // ---- Hertfordshire County Council (hertfordshire.gov.uk: one CSV a month or a quarter, a real transaction number) ----
    /// <summary>Hertfordshire's header names are already standard (the column ORDER differs between files; the mapping is by name). Stray spaces trimmed.</summary>
    public static string HertsCanon(string h) => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " ");

    /// <summary>Hertfordshire file names: "supplier-payments-over-250-april-to-june-2022.csv" (a quarter, to December 2024), "supplier-payments-over-250-march-2025.csv" and
    /// "supplier-payments-over-500-august-2026.csv" (a month from January 2025; the threshold went from 250 to 500 pounds in April 2025), and "supplier-payments-over-500-december.csv"
    /// (no year: the month after November 2025). Quarter tag "YYYY-MM_MM", month tag "YYYY-MM"; "" = not a spending file.</summary>
    public static string? HertsTag(string fileName)
    {
        string n = fileName.ToLowerInvariant();
        string[] months = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
        var q = System.Text.RegularExpressions.Regex.Match(n, @"over-\d+-(" + string.Join("|", months) + @")-to-(" + string.Join("|", months) + @")-(\d{4})");
        if (q.Success)
        {
            int m1 = Array.IndexOf(months, q.Groups[1].Value) + 1, m2 = Array.IndexOf(months, q.Groups[2].Value) + 1;
            return $"{int.Parse(q.Groups[3].Value):0000}-{m1:00}_{m2:00}";
        }
        var m = System.Text.RegularExpressions.Regex.Match(n, @"over-\d+-(" + string.Join("|", months) + @")(?:-(\d{4}))?\.");
        if (!m.Success) return "";
        int mon = Array.IndexOf(months, m.Groups[1].Value) + 1;
        int year = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : (mon == 12 ? 2025 : 0);
        return year == 0 ? "" : $"{year:0000}-{mon:00}";
    }

    /// <summary>Hertfordshire writes dates dd/MM/yyyy in some files and d/M/yyyy ("10/3/2025", "5/3/2025") in others; written dd/MM/yyyy so no month/day fallback can swap them.</summary>
    public static string HertsDate(string s) =>
        DateTime.TryParseExact(s.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : s;

    // ---- Stockport Metropolitan Borough Council (data.gov.uk "All spend": one CSV a month on the council's S3 bucket, February 2017 on; see PREREG_S45_STOCKPORT.md) ----
    static string StockportNorm(string h) => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿').Replace('_', ' '), @"\s+", " ").ToLowerInvariant();

    /// <summary>Stockport's header spellings (stray spaces, underscores, "Supplier" or "Supplier Name", "Net Amount" or "net_amount", a typo "expeniture", "Merchant ID" over a category column,
    /// "Services to People" over the Service column in two 2019 files) are made one standard set. The 2025 files' "transaction_id" is "Transaction Number". Dates: see <see cref="StockportHeader"/>.</summary>
    public static string StockportCanon(string h)
    {
        string t = StockportNorm(h);
        return t switch
        {
            "supplier" or "supplier name" => "Supplier Name",
            "service" or "service area" or "services to people" => "Service",
            "directorate" => "Directorate",
            "summary of purpose of expenditure" or "summary of purpose of expeniture" => "Summary of Purpose of Expenditure",
            "merchant category" or "merchant id" => "Merchant Category",
            "net amount" => "Net Amount",
            "transaction id" => "Transaction Number",
            "date" or "paid date" => "Date",
            "invoice date" => "Invoice Date",
            _ => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " "),
        };
    }

    /// <summary>The payment date is the "paid_date" column where a file has one (2018-19 and April 2025 on); where a file has only "invoice_date" (about sixty files) or "Date" (2017-18), that is the
    /// date used. A file with both gets its invoice date kept as "Invoice Date". Done on the whole header because one cell's meaning depends on the other.</summary>
    public static string[] StockportHeader(string[] header)
    {
        bool hasPaid = header.Any(c => StockportNorm(c) == "paid date");
        return header.Select(c => StockportNorm(c) == "invoice date" ? (hasPaid ? "Invoice Date" : "Date") : c).ToArray();
    }

    /// <summary>Three files (March 2018, July 2022, September 2022) have no date column at all. An empty "Date" column is appended to the header and every row, so the file loads and every
    /// row has no payment date (it is in the file total and in no financial year). No cell changes.</summary>
    public static List<string[]> StockportTable(List<string[]> table)
    {
        int hr = Enumerable.Range(0, Math.Min(25, table.Count)).FirstOrDefault(i => table[i].Any(c => StockportNorm(c) == "net amount"), -1);
        if (hr < 0) return table;
        var hdr = table[hr];
        if (hdr.Any(c => { var n = StockportNorm(c); return n is "date" or "paid date" or "invoice date"; })) return table;
        int width = hdr.Length; while (width > 0 && hdr[width - 1].Trim().Length == 0) width--;
        var res = new List<string[]>(table.Count);
        for (int i = 0; i < table.Count; i++)
        {
            if (i < hr) { res.Add(table[i]); continue; }
            var src = table[i];
            var row = new string[width + 1];
            for (int c = 0; c < width; c++) row[c] = c < src.Length ? src[c] : "";
            row[width] = i == hr ? "Date" : "";
            res.Add(row);
        }
        return res;
    }

    /// <summary>File names such as "Spend over £500 Feb 17.csv", "Over £500 spend Dec18.csv", "Over £500 spend - July 2022.csv", "Expenditure over £500 - June 21.csv" and "All Spend April 2025.csv" carry the month
    /// and a two- or four-digit year. Tag "YYYY-MM"; "" = no month in the name (not a spending file). The tag is the name's because the dates in most files are invoice dates.</summary>
    public static string? StockportTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName.ToLowerInvariant(), @"(?<![a-z])(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[^a-z0-9]*(\d{4}|\d{2})(?!\d)");
        if (!m.Success) return "";
        int mon = Array.IndexOf(new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" }, m.Groups[1].Value) + 1;
        int year = int.Parse(m.Groups[2].Value); if (year < 100) year += 2000;
        return $"{year:0000}-{mon:00}";
    }

    /// <summary>Stockport writes dates dd/MM/yyyy, d/M/yyyy or two-digit years; written dd/MM/yyyy so no month/day fallback can swap them. Anything else is left as it is.</summary>
    public static string StockportDate(string s) =>
        DateTime.TryParseExact(s.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "dd-MMM-yy", "dd-MMM-yyyy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : s;

    // ---- The Borough of Calderdale (dataworks.calderdale.gov.uk: one CSV a month, April 2020 on, one line a payment number; see PREREG_S47_CALDERDALE.md) ----
    /// <summary>Calderdale's header: 70 files start with a "Body" column (a statistics.data.gov.uk URL) and seven (February to August 2026) do not; "Supplier(Beneficiary) Name" is the payee, "Internal Ref Number" the payment number,
    /// "Procurement Classification: Council's Procurement Category Name" is long. Made one standard set; every other column keeps its published name.</summary>
    public static string CalderdaleCanon(string h)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " ");
        return t.ToLowerInvariant() switch
        {
            "supplier(beneficiary) name" or "supplier (beneficiary) name" => "Supplier Name",
            "internal ref number" => "Transaction Number",
            "procurement classification: council's procurement category name" => "Procurement Category",
            "purpose of spend (summary)" => "Purpose of Spend",
            "voluntary community and social enterprise supplier" => "VCSE Supplier",
            _ => t,
        };
    }

    /// <summary>The header row is made standard BEFORE the header is searched, because the raw payee column "Supplier(Beneficiary) Name" is not the anchor "Supplier Name". Only the first row changes.</summary>
    public static List<string[]> CalderdaleTable(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var res = new List<string[]>(table);
        res[0] = table[0].Select(c => c.Trim().Length == 0 ? c : CalderdaleCanon(c)).ToArray();
        return res;
    }

    /// <summary>Calderdale's files are named "April 2020.csv" to "August 2026.csv": the month and year in the name; tag "YYYY-MM"; "" = no month in the name. The dates agree (77 of 77 files).</summary>
    public static string? CalderdaleTag(string fileName) => StockportTag(fileName);

    /// <summary>Calderdale writes dd/MM/yyyy in every row; written dd/MM/yyyy so no month/day fallback can swap them.</summary>
    public static string CalderdaleDate(string s) => StockportDate(s);

    // ---- London Borough of Camden (opendata.camden.gov.uk, one export split by payment month; see PREREG_S48_CAMDEN.md) ----
    /// <summary>Camden's export columns: "Beneficiary Name" is the payee, "Payment Date" the date, "Amount GBP" the line amount, "Unique Identifier" the id ("aug-26-6288"). Made one standard set; every other column keeps its published name.</summary>
    public static string CamdenCanon(string h)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " ");
        return t.ToLowerInvariant() switch
        {
            "beneficiary name" => "Supplier Name",
            "payment date" => "Date",
            "amount gbp" => "Net Amount",
            "unique identifier" => "Transaction Number",
            _ => t,
        };
    }

    /// <summary>The split files are named "Camden 2019-09.csv" to "Camden 2026-08.csv": the month is in the name as YYYY-MM. "" = no such month in the name.</summary>
    public static string? CamdenTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"(?<!\d)(20\d{2})-(0[1-9]|1[0-2])(?!\d)");
        return m.Success ? m.Groups[1].Value + "-" + m.Groups[2].Value : "";
    }

    /// <summary>Camden writes dd/MM/yyyy in every row; written dd/MM/yyyy so no month/day fallback can swap them.</summary>
    public static string CamdenDate(string s) => StockportDate(s);

    // ---- City of York Council (data.yorkopendata.org: one CSV a financial year, four layouts; see PREREG_S46_YORK.md) ----
    static string YorkNorm(string h) => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿').Replace('_', ' '), @"\s+", " ").ToLowerInvariant();

    /// <summary>York's header spellings across four layouts ("SupplierName", "Supplier_Beneficiary", "Creditor_Name"; "Date", "PaymentDate", "Payment_Date"; "TransactionNumber", "TransactionReference", "Transaction_No";
    /// "Amount", "NetAmount_ExcVAT", "Net_Amount"; "ExpenseCategory", "Subjective_Detail") are made one standard set. 2018/19's "GL_Date" stays a separate column ("GL Date"): the payment date is "Payment_Date".</summary>
    public static string YorkCanon(string h)
    {
        string t = YorkNorm(h);
        return t switch
        {
            "suppliername" or "supplier beneficiary" or "creditor name" => "Supplier Name",
            "date" or "paymentdate" or "payment date" => "Date",
            "gl date" => "GL Date",
            "transactionnumber" or "transactionreference" or "transaction no" => "Transaction Number",
            "amount" or "netamount excvat" or "net amount" => "Net Amount",
            "expensecategory" or "subjective detail" => "Expense Category",
            "bodyname" or "organisationname" or "organisation name" => "Organisation Name",
            "organisationunit" or "directorate" => "Directorate",
            "department" => "Department",
            "service plan" => "Service Plan",
            _ => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " "),
        };
    }

    /// <summary>The header row is made standard BEFORE the header is searched (the payee column has three spellings, so no one raw spelling can anchor the search). Only the first row changes.</summary>
    public static List<string[]> YorkTable(List<string[]> table)
    {
        if (table.Count == 0) return table;
        var res = new List<string[]>(table);
        res[0] = table[0].Select(c => c.Trim().Length == 0 ? c : YorkCanon(c)).ToArray();
        return res;
    }

    /// <summary>York's files are named over500payments2011.csv, allpayments2013.csv, over250payments2025.csv: the number is the year the financial year starts. Tag "FY2011-12". "" = no year in the name.</summary>
    public static string? YorkTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Matches(fileName, @"(20\d\d)");
        if (m.Count == 0) return "";
        int y = int.Parse(m[m.Count - 1].Groups[1].Value);
        return $"FY{y}-{(y + 1) % 100:00}";
    }

    /// <summary>York writes dd/MM/yyyy, and "25/04/2024 00:00" from 2019/20; written dd/MM/yyyy so no month/day fallback can swap them.</summary>
    public static string YorkDate(string s) =>
        DateTime.TryParseExact(s.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm:ss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : s;

    // ---- Essex County Council (essex.gov.uk: one workbook a quarter, one sheet a month, all items, no transaction number) ----
    /// <summary>Essex's header cells carry a trailing space ("Spend Description "); the Field Descriptions sheet shows "Merchant Group*" with an asterisk the data sheets do not. One standard set.</summary>
    public static string EssexCanon(string h) => System.Text.RegularExpressions.Regex.Replace(h.Trim().TrimStart('﻿'), @"\s+", " ").TrimEnd('*').Trim();

    /// <summary>Essex's monthly sheets are named "April 2025"; any other sheet or file (the "Field Descriptions" sheet, the LGTC contract-register workbooks on the same page) is not spending: "" = not in scope. null = tag by the month the dates hold.</summary>
    public static string? EssexTag(string unitName) => System.Text.RegularExpressions.Regex.IsMatch(unitName.Trim(), @"^(January|February|March|April|May|June|July|August|September|October|November|December) \d{4}$") ? null : "";

    /// <summary>Essex writes most dates as real dates but six sheets as text: "27/04/2020", "23 March 2020" and "16th May 2023". Written dd/MM/yyyy; anything else is left as it is.</summary>
    public static string EssexDate(string s)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"(\d)(st|nd|rd|th)\b", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return DateTime.TryParseExact(t, new[] { "dd/MM/yyyy", "d/M/yyyy", "d MMMM yyyy", "dd MMMM yyyy", "d MMM yyyy", "dd/MM/yyyy HH:mm" }, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : s;
    }

    /// <summary>Payee labels that stand for many people, not for one counterparty. The council says "Some names have been removed in line with Data Protection policy"; these are what it wrote instead.
    /// Case-insensitive exact match, nothing fuzzy (see PREREG_S43_ESSEX.md for the counts).</summary>
    public static readonly string[] EssexPooledLabels =
    {
        "FOSTER CARE PAYMENT", "FOSTER CARERS", "ECC EMPLOYEE", "DIRECT PAYMENT", "DIRECT PAYMENTS", "PAYMENT TO INDIVIDUAL", "PAYMENT TO INDIVIDIUAL", "NON EMPLOYEE EXPENSE",
        "TRAINING BURSARY", "ONE OFF NON INVOICE", "ONE OFF UNDER £10K", "ONE OFF GRANTS", "ONE OFF TRAVEL EXPENSES", "ONE OFF CORONERS EXPENSES",
    };
    public static bool IsEssexPooledLabel(string name) => EssexPooledLabels.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The name cell of a pooled label becomes "Redacted (pooled label): FOSTER CARE PAYMENT", so the engine leaves it out of every schedule and counts it in the export; the published
    /// label stays readable. Applied to the data rows under the header row only. Setting the environment variable COUNCILAUDIT_ESSEX_NORULE=1 leaves every label as published (the control run
    /// of PREREG_S43_ESSEX.md). No other cell changes.</summary>
    public static List<string[]> EssexTable(List<string[]> table)
    {
        if (Environment.GetEnvironmentVariable("COUNCILAUDIT_ESSEX_NORULE") == "1") return table;
        int hr = table.FindIndex(r => r.Any(c => c.Trim() == "Name"));
        if (hr < 0) return table;
        int iName = Array.FindIndex(table[hr], c => c.Trim() == "Name");
        var o = new List<string[]>(table.Count);
        for (int i = 0; i < table.Count; i++)
        {
            var r = table[i];
            if (i > hr && iName < r.Length && IsEssexPooledLabel(r[iName]))
            {
                r = (string[])r.Clone();
                r[iName] = "Redacted (pooled label): " + r[iName].Trim();
            }
            o.Add(r);
        }
        return o;
    }

    // ---- Cornwall (own site, one file per month from April 2019; two or three header layouts) ----
    /// <summary>One standard header set for Cornwall's layouts. The two "Description" columns of the 2020 to 2025 layout are the cost centre's description (first) and
    /// the expense's (second, after the code in "Subjective"); in the layouts with one "Description" or a "Cost Centre Name", the text of the expense is in "Subjective"
    /// itself ("31503 Taxi and Minibus"). Stray spaces and upper-case/underscore spellings (SUPPLIER_NAME, VOUCHER_NUM) are made standard. No data cell changes.</summary>
    public static string[] CornwallHeader(string[] raw)
    {
        var h = raw.Select(x => x.Trim()).ToArray();
        string L(int i) => h[i].Replace('_', ' ').ToLowerInvariant();
        int nDesc = Enumerable.Range(0, h.Length).Count(i => L(i) == "description");
        var o = new string[h.Length];
        int seen = 0;
        for (int i = 0; i < h.Length; i++)
        {
            o[i] = L(i) switch
            {
                "entity name" => "Entity Name",
                "directorate" => "Directorate",
                "service/board" or "service" => "Service",
                "sub-service" or "sub service" => "Sub Service",
                "cost centre" or "costcentre" => "Cost Centre",
                "cost centre name" => "Cost Centre Description",
                "description" => nDesc >= 2 && ++seen == 2 ? "Expense Description" : "Cost Centre Description",
                "subjective" => nDesc >= 2 ? "Subjective Code" : "Expense Description",
                "project" => "Project",
                "supplier name" => "Supplier Name",
                "voucher number" or "voucher num" => "Voucher Number",
                "payment date" => "Payment Date",
                "line amount" => "Line Amount",
                "invoice amount" => "Invoice Amount",
                "net amount" => "Net Amount",
                _ => h[i],
            };
            if (L(i) == "description" && nDesc < 2) seen++;
        }
        return o;
    }

    /// <summary>February 2022 writes dates "25/2/22" (d/M/yy). The engine's date list needs four-digit years; anything else falls to a US-style month/day
    /// reading that turns 8/2/22 into 2 August 2022. Written as dd/MM/yyyy (20yy); every other form is left as it is.</summary>
    public static string CornwallDate(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^(\d{1,2})/(\d{1,2})/(\d{2})$");
        return m.Success ? $"{int.Parse(m.Groups[1].Value):00}/{int.Parse(m.Groups[2].Value):00}/20{m.Groups[3].Value}" : s;
    }

    // ---- Coventry (own site, one file per month, 114 files April 2017 to September 2026) ----
    /// <summary>Coventry's headers wander (the payee name is "Supplier(T)", "Supplier (T)", "Supplier(T) 2", "Supplier T", "Supper (T)" or, once,
    /// "Check and edit column"; the code column is plain "Supplier"). One standard set; no data cell changes.</summary>
    public static string CoventryCanon(string h)
    {
        string t = h.Trim();
        string l = t.ToLowerInvariant();
        if (l == "transaction no") return "Transaction Number";
        if (l == "supplier group") return "Supplier Group Code";
        if (l.StartsWith("supplier group")) return "Supplier Group";
        if (l == "supplier") return "Supplier Code";
        if (l.StartsWith("supplier") || l.StartsWith("supper") || l == "check and edit column") return "Supplier Name";
        if (l.StartsWith("comment") || l.StartsWith("procurement comment")) return "Comments";
        if (l == "directorate(t)") return "Directorate";
        if (l == "cost centre(t)") return "Cost Centre Name";
        if (l == "account code(t)") return "Account Description";
        if (l == "proclass(t)") return "Proclass Description";
        return t;
    }

    static readonly string[] MonthNames = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };

    /// <summary>"spend-over-500-april-2017.csv" -> "2017-04" (the month in the file's own name); "" when the name is not that pattern.</summary>
    public static string? CoventryTag(string fileName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileName, @"spend-over-500-([a-z]+)-(\d{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        int mi = Array.IndexOf(MonthNames, m.Groups[1].Value.ToLowerInvariant());
        return mi < 0 ? "" : $"{m.Groups[2].Value}-{mi + 1:00}";
    }

    // ---- Wakefield (Data Mill North, one file per quarter) ----
    public static string WakefieldCanon(string h)
    {
        string t = h.Trim();
        string l = t.ToLowerInvariant();
        if (l is "payment date" or "date") return "Payment Date";
        if (l is "transno" or "transaction number" or "transaction no" or "transaction no." or "trans no.") return "Transaction Number";
        if (l is "seq no" or "seq no.") return "Seq No";
        if (l is "net amount" or "amount") return "Net Amount";
        if (l is "service (sercop)" or "cost centre narrative") return "Service Area";
        if (l.StartsWith("purpose") || l.StartsWith("description of spend")) return "Purpose of Spend";
        if (l is "type" or "rev/cap") return "Type";
        if (l.StartsWith("comp")) return "Company Registration";
        if (l.StartsWith("charity")) return "Charity Registration";
        return t;
    }

    /// <summary>Quarter file name -> tag "YYYY-MM_MM" (calendar months of the quarter); "" (skip) when the name is not a quarter file
    /// of financial year 2017-18 or later (earlier files are not used; card files are never in the raw folder). A null result would
    /// mean "tag by the modal month of the dates" (the monthly councils).</summary>
    public static string? WakefieldTag(string fileName)
    {
        int y1, q;
        var a = System.Text.RegularExpressions.Regex.Match(fileName, @"[Qq](\d)[ -](\d{4})-?(\d{2,4})");
        var b = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})-?(\d{2})\s*[Qq](\d)");
        if (a.Success) { q = int.Parse(a.Groups[1].Value); y1 = int.Parse(a.Groups[2].Value); }
        else if (b.Success) { y1 = int.Parse(b.Groups[1].Value); q = int.Parse(b.Groups[3].Value); }
        else return "";
        if (y1 < 2017) return "";
        int startMonth = new[] { 4, 7, 10, 1 }[q - 1];
        int year = q == 4 ? y1 + 1 : y1;
        return $"{year}-{startMonth:00}_{startMonth + 2:00}";
    }

    /// <summary>
    /// Wakefield's files carry payroll-type lines with no transaction number, no seq number and no payee name, for example
    /// Directorate ADULTS, "Car Allowances (Employees)", 15/04/2017, 30,292.63 (66 such lines, GBP 4.86m, in the first quarter of
    /// 2017-18 alone). The column mapping drops a row with no transaction number, which would take those pounds out of the file
    /// total F that the declared-spend test compares with. Each such row now gets its own number "(no number) N" (N = its position
    /// in the file), so it is exported and counted; with no payee name it is excluded from every schedule as a blank-supplier row,
    /// exactly as a redacted row is. A row with no number and no payment date is an amount-only footer (each file's footer rows sum to
    /// the rest of the file, checked by `prep`), so it is left unnumbered and dropped as a non-payment. No other cell changes.
    /// </summary>
    public static List<string[]> NumberTheUnnumbered(List<string[]> table, string idHeader, string dateHeader)
    {
        if (table.Count == 0) return table;
        int iId = Array.FindIndex(table[0], h => h.Trim().TrimStart('\uFEFF').Equals(idHeader, StringComparison.OrdinalIgnoreCase));
        int iDate = Array.FindIndex(table[0], h => h.Trim().TrimStart('\uFEFF').Equals(dateHeader, StringComparison.OrdinalIgnoreCase));
        if (iId < 0 || iDate < 0) return table;
        var result = new List<string[]>(table.Count) { table[0] };
        for (int i = 1; i < table.Count; i++)
        {
            var r = table[i];
            bool blankId = iId >= r.Length || string.IsNullOrWhiteSpace(r[iId]);
            // a row with no number AND no payment date is a column or sub-total footer (amount only): it is not a payment and stays
            // unnumbered, so the mapping drops it as it does Bristol's footers
            bool hasDate = iDate < r.Length && !string.IsNullOrWhiteSpace(r[iDate]);
            if (blankId && hasDate)
            {
                var copy = new string[Math.Max(r.Length, iId + 1)];
                for (int c = 0; c < copy.Length; c++) copy[c] = c < r.Length ? r[c] : "";
                copy[iId] = "(no number) " + i;
                result.Add(copy);
            }
            else result.Add(r);
        }
        return result;
    }
}