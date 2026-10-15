using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 38: the next councils (NextCouncils.cs). Each case is a real row or file name from Wakefield's Data Mill North files.
/// </summary>
public class Session38Tests
{
    [Theory]
    [InlineData("Spend Over Â£500 Q2 2017-18.csv", "2017-07_09")]
    [InlineData("spend over Â£500 Q1 2017-2018.csv", "2017-04_06")]
    [InlineData("Spend over Â£500 Q4 2017-18.csv", "2018-01_03")]          // Q4 is January to March of the NEXT calendar year
    [InlineData("Supplier Spend 201920 Q1.csv", "2019-04_06")]
    [InlineData("Supplier Spend 202021 Q1 .csv", "2020-04_06")]
    [InlineData("Supplier Spend 2021-22 Q3.csv", "2021-10_12")]
    [InlineData("Supplier Spend 2026-27 Q1.csv", "2026-04_06")]
    [InlineData("spend-over-500-q4-2016-2017.csv", "")]                  // before 2017-18: not used
    [InlineData("Wakefield Council Procurement Card Transactions 2122 Quarter 4.csv", "")]
    public void WakefieldTagIsTheQuarterOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.WakefieldTag(name));

    [Fact]
    public void WakefieldHeadersOfTheThreeLayoutsBecomeOneSet()
    {
        var old = new[] { "Organisation Name", "Directorate", "Service (Sercop)", "Supplier Name", "Payment Date", "Transaction Number", "Net Amount", "Purpose of Spend", "Procurement Classification", "Class Code", "Procurement Classification", "Class Code" };
        var mid = new[] { "Supplier Name", "Comp. reg. no", "Charity Number", "Payment Date", "TransNo", "Seq No", "Net Amount", "Purpose (Desc) of Spend", "Type" };
        var now = new[] { "Cost Centre Narrative", "Supplier Name", "Date", "TransNo", "Seq No", "Amount", "Description of Spend(T)" };
        var o = NextFixups.CanonHeader(old, NextFixups.WakefieldCanon);
        Assert.Equal(new[] { "Organisation Name", "Directorate", "Service Area", "Supplier Name", "Payment Date", "Transaction Number", "Net Amount", "Purpose of Spend", "Procurement Classification", "Class Code", "Procurement Classification (2)", "Class Code (2)" }, o);
        var m = NextFixups.CanonHeader(mid, NextFixups.WakefieldCanon);
        Assert.Equal(new[] { "Supplier Name", "Company Registration", "Charity Registration", "Payment Date", "Transaction Number", "Seq No", "Net Amount", "Purpose of Spend", "Type" }, m);
        var n = NextFixups.CanonHeader(now, NextFixups.WakefieldCanon);
        Assert.Equal(new[] { "Service Area", "Supplier Name", "Payment Date", "Transaction Number", "Seq No", "Net Amount", "Purpose of Spend" }, n);
    }

    [Fact]
    public void ADatedLineWithNoNumberIsNumbered_AnAmountOnlyFooterIsLeftToBeDropped()
    {
        // Wakefield 2017-04_06: "Directorate ADULTS, Car Allowances (Employees), 15/04/2017, 30,292.63", no number, no payee; and a file footer with only an amount
        var t = new List<string[]>
        {
            new[] { "Directorate", "Supplier Name", "Payment Date", "Transaction Number", "Net Amount" },
            new[] { "ADULTS", "", "15/04/2017", "", "30292.63" },
            new[] { "ADULTS", "ACME LTD", "16/04/2017", "23382334", "638.00" },
            new[] { "", "", "", "", "81856093.27" },
        };
        var o = NextFixups.NumberTheUnnumbered(t, "Transaction Number", "Payment Date");
        Assert.Equal("(no number) 1", o[1][3]);
        Assert.Equal("23382334", o[2][3]);
        Assert.Equal("", o[3][3]);                                         // the footer keeps its blank number, so the mapping drops it
        var rows = AuditEngine.MapRows(o, SupportedCouncils.Cities.First(c => c.Name.StartsWith("Wakefield")).Mapping, "t", new List<string>());
        Assert.Equal(2, rows.Count);                                       // the numbered payroll-type line and the real payment; not the footer
        Assert.Equal(30292.63m + 638.00m, rows.Sum(r => r.Net));
    }

    [Theory]
    [InlineData("spend-over-500-april-2017.csv", "2017-04")]
    [InlineData("spend-over-500-october-2022.csv", "2022-10")]
    [InlineData("spend-over-500-september-2026.csv", "2026-09")]
    [InlineData("something-else.csv", "")]
    public void CoventryTagIsTheMonthOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.CoventryTag(name));

    [Fact]
    public void CoventryPayeeNameColumnIsFoundUnderAllItsSpellings()
    {
        foreach (var h in new[] { "Supplier(T)", "Supplier (T)", "Supplier(T) 2", "Supplier (T)2", "Supplier T", "Supper (T)", "Check and edit column", "Supplier (T) 2" })
            Assert.Equal("Supplier Name", NextFixups.CoventryCanon(h));
        Assert.Equal("Supplier Code", NextFixups.CoventryCanon("Supplier"));
        Assert.Equal("Supplier Group", NextFixups.CoventryCanon("Supplier Group(T)"));
        Assert.Equal("Supplier Group Code", NextFixups.CoventryCanon("Supplier Group"));
        Assert.Equal("Transaction Number", NextFixups.CoventryCanon("Transaction No"));
        // a real 2019 header with extra unnamed columns: blanks get a stable name and nothing collides
        var h2 = NextFixups.CanonHeader(new[] { "Amount", "", "", "ID3" }, NextFixups.CoventryCanon);
        Assert.Equal(new[] { "Amount", "(blank column)", "(blank column) (2)", "ID3" }, h2);
    }

    [Fact]
    public void WakefieldIsInTheEveryoneListAndHasASlug()
    {
        Assert.Contains(SupportedCouncils.Everyone, c => c.Name == "Wakefield Metropolitan District Council");
        Assert.Equal("wakefield", NextCouncils.SlugByName["Wakefield Metropolitan District Council"]);
        Assert.Equal(36, NextCouncils.Wakefield.Years.Count);              // 37 quarters from 2017-04 to 2026-04 less the missing 2022-01_03
    }

    [Fact]
    public void CornwallHeadersOfTheThreeLayoutsBecomeOneSet()
    {
        var y2019 = new[] { "Entity Name", "Directorate", "Service/Board", "Cost Centre", "Cost Centre Name", "Subjective", "Project", "SUPPLIER_NAME", "VOUCHER_NUM", "PAYMENT_DATE", "LINE_AMOUNT", "INVOICE_AMOUNT", "Net AMOUNT" };
        Assert.Equal(new[] { "Entity Name", "Directorate", "Service", "Cost Centre", "Cost Centre Description", "Expense Description", "Project", "Supplier Name", "Voucher Number", "Payment Date", "Line Amount", "Invoice Amount", "Net Amount" },
            NextFixups.CanonHeader(NextFixups.CornwallHeader(y2019), h => h));
        // 2022: two columns both called Description (the cost centre's, then the expense's after the code in Subjective), stray leading spaces
        var y2022 = new[] { "Entity Name ", " Directorate", "Service/Board", " Cost Centre", " Description", " Subjective", " Description", " Supplier Name", " Payment Date", " Line Amount", " Invoice Amount", " Net Amount" };
        Assert.Equal(new[] { "Entity Name", "Directorate", "Service", "Cost Centre", "Cost Centre Description", "Subjective Code", "Expense Description", "Supplier Name", "Payment Date", "Line Amount", "Invoice Amount", "Net Amount" },
            NextFixups.CornwallHeader(y2022));
        // from July 2025: one Description, and Subjective holds the code and the text ("31503 Taxi and Minibus")
        var y2025 = new[] { "Directorate", "Service/Board", " Description", " Subjective", " Supplier Name", " Payment Date", " Line Amount", " Invoice Amount", " Net Amount" };
        Assert.Equal(new[] { "Directorate", "Service", "Cost Centre Description", "Expense Description", "Supplier Name", "Payment Date", "Line Amount", "Invoice Amount", "Net Amount" },
            NextFixups.CornwallHeader(y2025));
    }

    [Theory]
    [InlineData("25/2/22", "25/02/2022")]       // February 2022: d/M/yy, which a US-style fallback would read as month/day
    [InlineData("8/2/22", "08/02/2022")]
    [InlineData("29/04/2022", "29/04/2022")]    // other forms are left alone
    [InlineData("30-Apr-2019", "30-Apr-2019")]
    [InlineData("2025-11-11", "2025-11-11")]
    public void CornwallShortYearDatesAreWrittenInFull(string input, string expected) => Assert.Equal(expected, NextFixups.CornwallDate(input));
    [Theory]
    [InlineData("Council Spend 2020 21", "FY2020-21")]
    [InlineData("Council Spend 2016 17", "")]                       // before 2017-18: not used
    [InlineData("payments-to-suppliers-2023-2024.xlsx", "FY2023-24")]
    [InlineData("payments-to-suppliers-2025-2026-f.xlsx", "FY2025-26")]
    [InlineData("payments-to-suppliers-2026-2027-q1.xlsx", "FY2026-27")]
    [InlineData("payments-to-suppliers-october-2023.xlsx", "")]     // the multi-sheet workbook is read sheet by sheet, never by its file name
    public void NottinghamTagIsTheFinancialYear(string name, string expected) => Assert.Equal(expected, NextFixups.NottinghamTag(name));

    [Fact]
    public void NottinghamPaymentMethodLabelsInTheNumberColumnBecomeSeparateTransactions()
    {
        var t = new List<string[]>
        {
            new[] { "Payment Date", "Transaction Number", "Supplier Name", "Net Amount" },
            new[] { "20200304", "P CARD", "BUNZL CLEANING SUPPLIE", "721" },
            new[] { "20200304", "P CARD", "BUNZL CLEANING SUPPLIE", "721" },
            new[] { "20210401", "CHAPS", "STELLA PRODUCTIONS LTD", "-1000" },
            new[] { "20220401", "3148368", "3PB BARRISTERS", "675" },
        };
        var o = NextFixups.NumberTheLabels(t, "Transaction Number");
        Assert.Equal("(no number: P CARD) 1", o[1][1]);
        Assert.Equal("(no number: P CARD) 2", o[2][1]);       // two identical card lines are two transactions, not one with two lines
        Assert.Equal("(no number: CHAPS) 3", o[3][1]);
        Assert.Equal("3148368", o[4][1]);                      // a real number is untouched
    }
    [Theory]
    [InlineData("spend-report-april-2022.csv", "2022-04")]
    [InlineData("april-25.csv", "2025-04")]
    [InlineData("over-ps500-october-25.csv", "2025-10")]
    [InlineData("over-ps500-payments-supplier-august-2025.csv", "2025-08")]
    [InlineData("december-2024-amended-c.csv", "2024-12")]
    [InlineData("nothing.csv", "")]
    public void WirralTagIsTheMonthOfTheFileName(string name, string expected) => Assert.Equal(expected, NextFixups.WirralTag(name));

    [Fact]
    public void WirralDecember2025_RowsWithNoDateCellAreRealignedWithTheHeader()
    {
        var t = new List<string[]>
        {
            new[] { "Payments for Publishing for invoices paid between 01 December 2025 and 31 December 2025", "", "", "", "", "", "", "" },
            new[] { "Supplier Name", "Transaction Number", "Paid Date", "Paid Amount", "Department ", "Cost Centre", "Description", "Irrecoverable VAT" },
            new[] { "1ST AFFINITY FOSTERING SERVICE LIMITED", "354558", "4521.12", "Children, Families & Education", "E7080", "Care Provision", "", "" },
            new[] { "20 Winston records", "357260", "9875", "Neighbourhood Services", "L1", "Equipment", "", "" },
        };
        var o = NextFixups.WirralShift(t);
        Assert.Equal("", o[2][2]);                       // the date stays unknown
        Assert.Equal("4521.12", o[2][3]);                // the amount is under Paid Amount again
        Assert.Equal("Children, Families & Education", o[2][4]);
        Assert.Equal("9875", o[3][3]);
    }

    [Fact]
    public void WirralJanuary2023_SerialDatesAreNotMistakenForAMissingDateCell()
    {
        // an Excel serial (44931) is a number where the date belongs, but the amount column holds an amount, not text: no shift
        var t = new List<string[]>
        {
            new[] { "Supplier Name", "Transaction Number", "Paid Date", "Paid Amount", "Department", "Cost Centre", "Description" },
            new[] { "1 CALL BUSINESS SOLUTIONS LIMITED", "2226669", "44931", "9647.5", "Children & Young People", "C0324", "Contractors - Main" },
            new[] { "1 CALL BUSINESS SOLUTIONS LIMITED", "2226394", "44930", "35", "Children, Families & Education", "E1440", "Repairs" },
        };
        var o = NextFixups.WirralShift(t);
        Assert.Equal("44931", o[1][2]);
        Assert.Equal("9647.5", o[1][3]);
    }

    [Fact]
    public void StrictOpenXmlWorkbooksCanBeRead()
    {
        // a minimal workbook in the "Strict" namespaces (Wirral's March 2023 file)
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            void Add(string name, string xml) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open()); w.Write(xml); }
            Add("xl/workbook.xml", "<workbook xmlns=\"http://purl.oclc.org/ooxml/spreadsheetml/main\" xmlns:r=\"http://purl.oclc.org/ooxml/officeDocument/relationships\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://purl.oclc.org/ooxml/officeDocument/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://purl.oclc.org/ooxml/spreadsheetml/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Supplier</t></is></c><c r=\"B1\"><v>12.5</v></c></row></sheetData></worksheet>");
        }
        var rows = XlsxReader.Parse(ms.ToArray());
        Assert.Equal(new[] { "Supplier", "12.5" }, rows[0]);
        Assert.Equal("Supplier", XlsxReader.ParseAllSheets(ms.ToArray())["S"][0][0]);
    }
    [Theory]
    [InlineData("(no number published) 17", "")]
    [InlineData("(no number) 4", "")]
    [InlineData("(no number: CHAPS) 912", "")]
    [InlineData("2748314", "2748314")]
    [InlineData("", "")]
    public void ExactRepeatsIgnoreAPlaceholderNumberButNotARealOne(string id, string expected) => Assert.Equal(expected, FileDuplication.IdForRepeat(id));

    [Fact]
    public void ARowListedTwiceWithPlaceholderNumbersIsAnExactRepeat()
    {
        // Nottingham FY2020-21: 20200304 P CARD BUNZL CLEANING SUPPLIE 721 appears twice in a row; each row had its own placeholder number
        SpendRow R(string id) => new("FY2020-21", id, "BUNZL CLEANING SUPPLIE", "20200304", null, "404-Cleaning & Domestic Supp", "C-Commercial & Operations", 721m, 721m, null, null, 1, null, null, null, "Supplier Post Code=SL3 8NZ");
        var rows = new List<SpendRow> { R("(no number: P CARD) 1"), R("(no number: P CARD) 2") };
        var s = FileDuplication.Scan(rows).Single();
        Assert.Equal(1, s.RepeatRows);
    }
    [Theory]
    [InlineData("Equipment Special Purchases Cornwall Equip Loan Serv East", false)]
    [InlineData("ICES Standard Equipment Children's Equipment (Health) Loan Store", false)]
    [InlineData("Debt Managment F8000", false)]
    [InlineData("Credit check & Debt Recovery Costs Council Tax Collection", false)]
    [InlineData("Best Interest Assessor - Stage 2", false)]
    [InlineData("Car loans Non Service Specific-Financing Long Term Debtors 924001", false)]
    [InlineData("Third Party Loans Corporate Budgets", true)]       // a loan the council makes is still a loan relationship
    [InlineData("Capital Loans Sports Organisations", true)]
    [InlineData("Interest on PWLB loan", true)]
    public void DebtLedgerIgnoresLabelsThatAreNotBorrowing(string text, bool expected)
    {
        var r = new SpendRow("t", "1", "SUPPLIER", "01/04/2020", null, text, "svc", 100m, 100m, null, null, 1, null, null, null, "");
        Assert.Equal(expected, DebtLedger.HasDebtEvidence(new[] { r }));
    }}
