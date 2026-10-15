using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 53: the pure rules behind `checklist all` (PREREG_S53_CHECKLIST.md). Each rule is tested on a defect planted in a small
/// input (it must fail) and on its clean twin (it must pass): a check that cannot fail is not a check.
/// </summary>
public class Session53Tests
{
    // ---- C12: first-person, working-note and jargon text ----

    [Fact]
    public void PrivateText_CleanProse_HasNoFindings() =>
        Assert.Empty(PrivateText.Find("Download the monthly files from the council's page. 12,174 repeated-payment groups were hand-checked and all were confirmed."));

    [Theory]
    [InlineData("I read all 77 workbooks and found one header per file.", "first person")]
    [InlineData("Computed by the site, not by me.", "first person")]
    [InlineData("30 of 30 confirmed, Wilson 95% CI [88.6%, 100.0%].", "working note")]
    [InlineData("Recorded here as the caveat for anyone reading the top-20 export, not as a fix.", "working note")]
    [InlineData("A seeded random sample of 30 groups was opened.", "protocol jargon")]
    [InlineData("See Session 12 and ONBOARDING_CHECKLIST step 4.", "working-file reference")]
    [InlineData("A random sample (seed 20263001) of 30 groups was opened.", "protocol jargon")]
    [InlineData("The scanner writes it with NextFixups.WirralShift.", "working-file reference")]
    public void PrivateText_PlantedDefect_IsFound(string text, string kind) =>
        Assert.Contains(PrivateText.Find(text), x => x.StartsWith(kind));

    // ---- C11: a council paying itself ----

    [Theory]
    [InlineData("leeds", "Leeds City Council", "Leeds CC", true)]
    [InlineData("surrey", "Surrey County Council", "Surrey CC", true)]
    [InlineData("birmingham", "Birmingham City Council", "Birmingham CC", true)]
    [InlineData("york", "City of York Council", "York", true)]
    [InlineData("rbwm", "Royal Borough of Windsor and Maidenhead", "RBWM", true)]
    [InlineData("reading", "Reading Borough Council", "Reading BC", true)]
    [InlineData("camden", "London Borough of Camden", "Camden LBC", true)]
    [InlineData("leeds", "Leeds City Council", "Coventry CC", false)]
    [InlineData("wokingham", "Wokingham Borough Council", "Reading BC", false)]
    [InlineData("newcastle", "Newcastle City Council", "Newcastle-under-Lyme BC", false)]
    [InlineData("leeds", "Leeds City Council", "", false)]
    public void SelfPayment_ClassifiesTheEntity(string slug, string council, string entity, bool self) =>
        Assert.Equal(self, SelfPayment.IsSelf(slug, council, entity));

    // ---- C06: money in prose ----

    [Fact]
    public void MoneyFigures_ExtractsAmountsWithTheirPrecision()
    {
        var m = MoneyFigures.Extract("FY2023-24 holds 454 rows, GBP 8,434,327.20, and 2,021 rows of about GBP 69.1m; Gateley PLC £3,704,464.99.");
        Assert.Equal(3, m.Count);
        Assert.Equal(8434327.20m, m[0].Value);
        Assert.Equal(69_100_000m, m[1].Value);
        Assert.True(m[1].Tolerance > 40_000m && m[1].Tolerance < 60_000m);   // 69.1m is stated to 0.1m
        Assert.Equal(3704464.99m, m[2].Value);
    }

    [Fact]
    public void MoneyFigures_EqualAmountVerified_NearMissCaught_ElseNotDerivable()
    {
        var derivable = new[] { 8434327.20m, -5783038.88m };
        var ok = MoneyFigures.Extract("GBP 8,434,327.20")[0];
        var typo = MoneyFigures.Extract("GBP 8,434,372.20")[0];          // two digits swapped, within 0.5%
        var other = MoneyFigures.Extract("GBP 1,111,111.11")[0];
        var millions = MoneyFigures.Extract("GBP 5.8m")[0];              // 5,783,038.88 rounds to 5.8m
        Assert.Equal(MoneyFigures.Verdict.Verified, MoneyFigures.Judge(ok, derivable, out _));
        Assert.Equal(MoneyFigures.Verdict.NearMiss, MoneyFigures.Judge(typo, derivable, out var nearest));
        Assert.Equal(8434327.20m, nearest);
        Assert.Equal(MoneyFigures.Verdict.NotDerivable, MoneyFigures.Judge(other, derivable, out _));
        Assert.Equal(MoneyFigures.Verdict.Verified, MoneyFigures.Judge(millions, derivable, out _));   // absolute value compared
    }

    // ---- C02: the Schedule A gap ----

    [Fact]
    public void ScheduleAGap_MatrixScm_IsThirtySevenPence_NotTheVat()
    {
        // audit E2: Net 105,245.41, VAT type STD, stated Gross 126,294.86. The old "difference" was Gross - Net = 21,049.45 (the VAT).
        var gap = ScheduleAGap.Gap(new[] { (105245.41m, "STD") }, 126294.86m);
        Assert.Equal(0.37m, gap);
        Assert.NotEqual(126294.86m - 105245.41m, gap);
    }

    [Fact]
    public void ScheduleAGap_ZeroRatedAndReducedAndMixedLines()
    {
        Assert.Equal(0m, ScheduleAGap.Gap(new[] { (500m, "ZERO") }, 500m));
        Assert.Equal(0m, ScheduleAGap.Gap(new[] { (100m, "RRTE") }, 105m));
        // Wokingham Network Healthcare 3765708: Standard 1,003.38 + Exempt 214.62 stated 1,461.60 = the whole 1,218.00 at Standard
        var mixed = new[] { (1003.38m, "STD"), (214.62m, "EXEM") };
        Assert.Equal(0m, ScheduleAGap.Gap(mixed, 1461.60m));
        Assert.Null(ScheduleAGap.Gap(Array.Empty<(decimal, string)>(), 10m));
    }

    // ---- C03 / C04 / C05: the twins list ----

    private static readonly string[] TwinHeader = { "Council", "SupplierName", "TransactionA", "YearA", "DateA", "TransactionB", "YearB", "DateB", "Lines", "TotalValue",
        "DaysApart", "SameYearTagFile", "OtherColumnsIdentical", "SamePaymentNumber", "LineDatesIdentical", "Kind", "Reading", "GroupId", "GroupSize", "ExtraCopyValue" };

    private static string[] Twin(string a, string b, string da, string db, string days, string total, string gid, string size, string extra) =>
        new[] { "wokingham", "Cambian Ltd", a, "FY2122", da, b, "FY2122", db, "3", total, days, "True", "True", "False", "True", "k", "rule", gid, size, extra };

    private static List<string[]> Table(params string[][] rows) { var t = new List<string[]> { TwinHeader }; t.AddRange(rows); return t; }

    [Fact]
    public void TwinChecks_CleanPair_Passes() =>
        Assert.Empty(TwinChecks.Findings(Table(Twin("1", "2", "2021-05-06", "2021-05-08", "2", "54336.67", "1", "2", "54336.67")), "wokingham"));

    [Fact]
    public void TwinChecks_UndatedPairShownAsOneDayApart_IsCaught()
    {
        // audit E3: DaysApart -1 and an empty date, listed as "within seven days"
        var f = TwinChecks.Findings(Table(Twin("3644308", "3605427", "", "2021-05-06", "-1", "10000.00", "1", "2", "10000.00")), "wokingham");
        Assert.Contains(f, x => x.StartsWith("C03"));
    }

    [Fact]
    public void TwinChecks_StatedGapThatIsNotTheRealGap_IsCaught()
    {
        var f = TwinChecks.Findings(Table(Twin("1", "2", "2021-05-06", "2021-05-20", "3", "20000.00", "1", "2", "20000.00")), "wokingham");
        Assert.Contains(f, x => x.StartsWith("C03"));
    }

    [Fact]
    public void TwinChecks_TripleCountedPerPair_IsCaught_AndPerGroupPasses()
    {
        // audit E4: three identical transactions of 54,336.67 make three pairs but two extra copies (108,673.34), not three (163,010.01)
        var wrong = Table(Twin("1", "2", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "54336.67"),
                          Twin("1", "3", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "54336.67"),
                          Twin("2", "3", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "54336.67"));
        Assert.Contains(TwinChecks.Findings(wrong, "wokingham"), x => x.StartsWith("C04"));
        var right = Table(Twin("1", "2", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "54336.67"),
                          Twin("1", "3", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "54336.67"),
                          Twin("2", "3", "2023-05-01", "2023-05-01", "0", "54336.67", "7", "3", "0.00"));
        Assert.Empty(TwinChecks.Findings(right, "wokingham"));
    }

    [Fact]
    public void TwinChecks_MissingPairInAGroup_IsCaught()
    {
        var t = Table(Twin("1", "2", "2023-05-01", "2023-05-01", "0", "100.00", "7", "3", "100.00"));   // a group of three listing one pair
        Assert.Contains(TwinChecks.Findings(t, "wokingham"), x => x.StartsWith("C04"));
    }

    [Fact]
    public void TwinChecks_GroupOfFourOrMore_BreaksTheStatedRule()
    {
        var rows = new List<string[]>();
        for (int i = 1; i <= 4; i++) for (int j = i + 1; j <= 4; j++)
                rows.Add(Twin(i.ToString(), j.ToString(), "2023-05-01", "2023-05-01", "0", "100.00", "9", "4", i == 1 ? "100.00" : "0.00"));
        Assert.Contains(TwinChecks.Findings(Table(rows.ToArray()), "wokingham"), x => x.StartsWith("C05"));
    }

    [Fact]
    public void TwinChecks_ChainOfThree_TwoPairs_IsAValidGroup()
    {
        // 1-2 within seven days, 2-3 within seven days, 1-3 further apart: a group of three listing two pairs, extra copies 2 x value
        string[] P(string a, string b, string extra) => new[] { "wokingham", "Cambian Ltd", a, "FY2122", "2023-05-01", b, "FY2122", "2023-05-05", "3", "100.00", "4", "True", "True", "False", "True", "k", "rule", "5", "3", extra };
        var ok = Table(P("1", "2", "100.00"), P("2", "3", "100.00"));
        Assert.Empty(TwinChecks.Findings(ok, "wokingham"));
        // the same two pairs naming four different transactions are not one group of three
        var broken = Table(P("1", "2", "100.00"), P("3", "4", "100.00"));
        Assert.Contains(TwinChecks.Findings(broken, "wokingham"), x => x.StartsWith("C04"));
    }

    // ---- C02: spreadsheet float noise ----

    [Theory]
    [InlineData("-6680.1000000000004", "-6680.1")]
    [InlineData("22744619.289999999", "22744619.29")]
    [InlineData("1234.5", "1234.5")]
    [InlineData("500", "500")]
    [InlineData("", "")]
    [InlineData("not a number at all", "not a number at all")]
    public void XlsxTidyNumber_KeepsWhatTheSheetShows(string raw, string expected) => Assert.Equal(expected, XlsxReader.TidyNumber(raw));

    [Fact]
    public void XlsxTidyMoneyCells_OnlyTouchesTheNamedMoneyColumns()
    {
        var t = new List<string[]> { new[] { "Ref", "Net Amount", "Note" }, new[] { "1234567890123456789", "-6680.1000000000004", "0.30000000000000004" } };
        XlsxReader.TidyMoneyCells(t, "Net Amount", null);
        Assert.Equal("-6680.1", t[1][1]);
        Assert.Equal("1234567890123456789", t[1][0]);
        Assert.Equal("0.30000000000000004", t[1][2]);
    }

    // ---- C08: a scanner row number is never a transaction number ----

    [Fact]
    public void SyntheticRowId_CarriesTheUnnumberedPlaceholder()
    {
        var table = new List<string[]> { new[] { "Supplier", "Net" }, new[] { "Acme", "1000" }, new[] { "Beta", "2000" } };
        var o = WestBerkshireFixups.AddSyntheticRowId(table);
        Assert.Equal("(no number published) 1", o[1][2]);
        Assert.Equal("", FileDuplication.IdForRepeat(o[1][2]));
    }

    // ---- C11: the council's own name is not another authority ----

    [Fact]
    public void LoanDecoder_ACouncilPayingItself_IsOwnCouncil_NotInterAuthority()
    {
        Assert.Equal(CounterpartyClass.OwnCouncil, LoanDecoder.Classify("London Borough of Camden", "LONDON BOROUGH OF CAMDEN"));
        Assert.Equal(CounterpartyClass.InterAuthority, LoanDecoder.Classify("London Borough of Camden", "London Borough of Islington"));
        Assert.Equal(CounterpartyClass.InterAuthority, LoanDecoder.Classify("Wokingham Borough Council", "Wandsworth Borough Council"));
        Assert.Equal(DecodeStatus.NotApplicable, LoanDecoder.ForEntry("London Borough of Camden", "LONDON BOROUGH OF CAMDEN", 116909.68m, 116909.68m).Status);
    }}
