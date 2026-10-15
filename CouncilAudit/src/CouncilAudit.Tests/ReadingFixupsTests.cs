using System.Text;
using CouncilAudit;
using Xunit;

namespace CouncilAudit.Tests;

/// <summary>
/// Session 18: unit tests for the three Reading parser/layout fixes BASELINE.md Section 5
/// named as a real gap (proven only by `reading` passing clean end to end on real data,
/// no synthetic-byte unit test) - CP850 encoding detection, the header-text drift
/// fixups, the split two-row xlsx header layout fix - plus the bad-file guard. Each test
/// builds the smallest synthetic input that exercises the real bug, not a real council
/// file, so these run without any file I/O.
/// </summary>
public class ReadingFixupsTests
{
    // -----------------------------------------------------------------------------
    // 1. CP850 encoding detection (CsvReader.DetectEncoding)
    // -----------------------------------------------------------------------------

    [Fact]
    public void DetectEncoding_Cp850Pound_IsNotMisreadAsWindows1252Oe()
    {
        // Byte 0x9C is "£" in CP850 but "œ" (U+0153) in Windows-1252. A real Reading
        // CP850 header ("Amount (£)") encoded as CP850 bytes must decode back to "£",
        // not silently become "Amount (œ)" under a 1252 fallback.
        var cp850 = Encoding.GetEncoding(850);
        byte[] bytes = cp850.GetBytes("Header,Amount (£)\r\nAcme Ltd,100.00\r\n");

        var detected = CsvReader.DetectEncoding(bytes);
        string decoded = detected.GetString(bytes);

        Assert.Contains("£", decoded);
        Assert.DoesNotContain("œ", decoded);
    }

    [Fact]
    public void DetectEncoding_Windows1252Pound_StaysWindows1252NotCp850()
    {
        // A genuine Windows-1252 file (Wokingham's own convention) must NOT be
        // misdetected as CP850 just because it has non-ASCII bytes - only the
        // tell-tale "œ" glyph after a 1252 decode should trigger the CP850 fallback.
        var win1252 = Encoding.GetEncoding(1252);
        byte[] bytes = win1252.GetBytes("Header,Amount (£)\r\nAcme Ltd,100.00\r\n");

        var detected = CsvReader.DetectEncoding(bytes);
        string decoded = detected.GetString(bytes);

        Assert.Contains("£", decoded);
        Assert.Equal(1252, detected.CodePage);
    }

    [Fact]
    public void Parse_Cp850File_RoundTripsPoundSignInHeaderAndData()
    {
        var cp850 = Encoding.GetEncoding(850);
        byte[] bytes = cp850.GetBytes("Supplier,Amount (£)\r\nAcme Ltd,100.00\r\n");

        var table = CsvReader.Parse(bytes);

        Assert.Equal(2, table.Count);
        Assert.Equal("Amount (£)", table[0][1]);
    }

    // -----------------------------------------------------------------------------
    // 2. Header-text drift fixups (ReadingFixups.HeaderFixups / ApplyHeaderFixups)
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Voucher Number", "Voucher Number (Internal Classification)")]
    [InlineData("Voucher Number (Jnternal Classification)", "Voucher Number (Internal Classification)")]
    [InlineData("Voucher Number Iinternal Classification)", "Voucher Number (Internal Classification)")]
    [InlineData("Invoice Type", "Invoice Type (Internal Classification)")]
    [InlineData("Decsription", "Directorate")]
    public void ApplyHeaderFixups_EachNamedDriftOrTypo_IsCorrectedToCanonicalName(string raw, string expected)
    {
        var table = new List<string[]> { new[] { raw, "Other Column" } };

        ReadingFixups.ApplyHeaderFixups(table, ReadingFixups.HeaderFixups);

        Assert.Equal(expected, table[0][0]);
    }

    [Fact]
    public void ApplyHeaderFixups_AlreadyCanonicalHeader_IsLeftUnchanged()
    {
        var table = new List<string[]> { new[] { "Voucher Number (Internal Classification)", "Supplier Name" } };

        ReadingFixups.ApplyHeaderFixups(table, ReadingFixups.HeaderFixups);

        Assert.Equal("Voucher Number (Internal Classification)", table[0][0]);
        Assert.Equal("Supplier Name", table[0][1]);
    }

    [Fact]
    public void ApplyHeaderFixups_EmptyTable_DoesNotThrow()
    {
        var table = new List<string[]>();
        ReadingFixups.ApplyHeaderFixups(table, ReadingFixups.HeaderFixups);
        Assert.Empty(table);
    }

    // -----------------------------------------------------------------------------
    // 3. Split two-row xlsx header (ReadingFixups.FixXlsxLayout)
    // -----------------------------------------------------------------------------

    [Fact]
    public void FixXlsxLayout_FourBlankRowsThenSplitHeader_DropsBlanksAndQualifierRow()
    {
        var table = new List<string[]>
        {
            new[] { "", "" },                                            // row 1: decorative blank
            new[] { "", "" },                                            // row 2
            new[] { "", "" },                                            // row 3
            new[] { "", "" },                                            // row 4
            new[] { "Voucher Number", "Supplier Type", "Invoice Type" }, // row 5: real header
            new[] { "(Internal Classification)", "", "(Internal Classification)" }, // row 6: qualifier
            new[] { "V1", "A", "STANDARD" },                              // row 7: real first data row
        };

        var fixedTable = ReadingFixups.FixXlsxLayout(table);

        Assert.Equal(2, fixedTable.Count); // header + 1 data row, blanks and qualifier row gone
        Assert.Equal("Voucher Number", fixedTable[0][0]);
        Assert.Equal("V1", fixedTable[1][0]);
    }

    [Fact]
    public void FixXlsxLayout_NoLeadingBlanksOrQualifierRow_IsLeftAsIs()
    {
        // A flat export (Wokingham's own FY2023-24 xlsx shape) must not be altered by
        // a fix aimed at Reading's decorative-template layout.
        var table = new List<string[]>
        {
            new[] { "TransNo", "Supplier" },
            new[] { "T1", "Acme Ltd" },
        };

        var fixedTable = ReadingFixups.FixXlsxLayout(table);

        Assert.Equal(2, fixedTable.Count);
        Assert.Equal("TransNo", fixedTable[0][0]);
    }

    [Fact]
    public void FixXlsxLayout_SecondRowHasRealData_IsNotMistakenForQualifierRow()
    {
        // Guard against over-matching: a second row that happens to have SOME blank
        // cells but at least one real (non-parenthetical) value must survive - only a
        // row where every non-blank cell starts with "(" is the qualifier artifact.
        var table = new List<string[]>
        {
            new[] { "TransNo", "Supplier" },
            new[] { "T1", "Acme Ltd" }, // genuine data, not a qualifier row
            new[] { "T2", "Other Ltd" },
        };

        var fixedTable = ReadingFixups.FixXlsxLayout(table);

        Assert.Equal(3, fixedTable.Count);
    }

    // -----------------------------------------------------------------------------
    // 4. The bad-file guard (ReadingFixups.IsFileMappingUsable)
    // -----------------------------------------------------------------------------

    [Fact]
    public void IsFileMappingUsable_NoNewWarnings_ReturnsTrue()
    {
        var warnings = new List<string> { "pre-existing unrelated warning" };
        int before = warnings.Count;

        Assert.True(ReadingFixups.IsFileMappingUsable(warnings, before));
    }

    [Fact]
    public void IsFileMappingUsable_CriticalColumnMappingFailed_ReturnsFalse()
    {
        var warnings = new List<string>();
        int before = warnings.Count;
        warnings.Add("2021-05: transaction-id column \"Voucher Number (Internal Classification)\" not found in header.");

        Assert.False(ReadingFixups.IsFileMappingUsable(warnings, before));
    }

    [Fact]
    public void MapRows_CriticalColumnMissing_AddsWarning_SoGuardCanDetectIt()
    {
        // End-to-end proof the guard actually fires on the real failure mode (a header
        // whose critical columns don't match the mapping at all) - not just the pure
        // IsFileMappingUsable logic in isolation.
        var table = new List<string[]>
        {
            new[] { "Some Other Column", "Yet Another" },
            new[] { "x", "y" },
        };
        var map = SupportedCouncils.Reading.Mapping;
        var warnings = new List<string>();
        int before = warnings.Count;

        AuditEngine.MapRows(table, map, "2021-05", warnings);

        Assert.False(ReadingFixups.IsFileMappingUsable(warnings, before));
    }
}
