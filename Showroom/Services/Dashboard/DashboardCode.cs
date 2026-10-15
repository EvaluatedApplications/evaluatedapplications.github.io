// Text for the "How this is built" panel. Every SQL string and every API name comes from DashboardSql / OrdersGenerator, the
// same constants the page executes. tools/DashboardProof renders MinimalProgram, compiles it against the published package and
// runs it, so what a visitor copies is what the owner ran.
using System.Text;

namespace OrdersDemo;

public static class DashboardCode
{
    public const string Package = "EvaluatedApplications.HoloDb";
    public const string PackageVersion = "5.1.2";     // keep equal to Showroom.csproj; DashboardProof fails if it differs
    public const int ChunkRows = 50_000;

    static string Lit(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "_");

    /// <summary>The calls the page makes to build the database (the C# around the SQL).</summary>
    public static string BuildListing(int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("var db = Database.Open();                       // in memory, no files, nothing leaves the tab");
        sb.AppendLine($"db.Execute(\"{DashboardSql.Schema}\");");
        sb.AppendLine($"int customers = OrdersGenerator.Customers({Lit(rows)});");
        sb.AppendLine($"for (long first = 1; first <= {Lit(rows)}; first += {Lit(ChunkRows)})   // generate and load in chunks");
        sb.AppendLine("{");
        sb.AppendLine("    var c = OrdersGenerator.Fill(first, rowsInThisChunk, customers);");
        sb.AppendLine("    db.BulkLoad(OrdersGenerator.Table, OrdersGenerator.Columns, c.AsArrays(), c.Count);");
        sb.AppendLine("}");
        sb.AppendLine($"db.Execute(\"{DashboardSql.PlaneIndex}\");");
        sb.AppendLine("db.PleromaMode = PleromaMode.Always;           // serve every shape the plane covers");
        return sb.ToString();
    }

    /// <summary>A complete Program.cs for a console app: the same load, the same plane, the statements for this filter.</summary>
    public static string MinimalProgram(OrdersFilter f, int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// dotnet new console; dotnet add package {Package} --version {PackageVersion}");
        sb.AppendLine("// Put OrdersGenerator.cs (the next tab) beside this file. Same rows, same answers as the page.");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using HoloDb;");
        sb.AppendLine("using OrdersDemo;");
        sb.AppendLine();
        sb.AppendLine($"const int Rows = {rows};");
        sb.AppendLine("var db = Database.Open();                       // in memory, no files");
        sb.AppendLine($"db.Execute(\"{DashboardSql.Schema}\");");
        sb.AppendLine("int customers = OrdersGenerator.Customers(Rows);");
        sb.AppendLine($"for (long first = 1; first <= Rows; first += {ChunkRows})");
        sb.AppendLine("{");
        sb.AppendLine($"    var c = OrdersGenerator.Fill(first, (int)Math.Min({ChunkRows}, Rows - first + 1), customers);");
        sb.AppendLine("    db.BulkLoad(OrdersGenerator.Table, OrdersGenerator.Columns, c.AsArrays(), c.Count);");
        sb.AppendLine("}");
        sb.AppendLine($"db.Execute(\"{DashboardSql.PlaneIndex}\");   // the aggregate plane");
        sb.AppendLine("db.PleromaMode = PleromaMode.Always;           // serve every shape the plane covers");
        sb.AppendLine();
        sb.AppendLine("string[] queries =");
        sb.AppendLine("{");
        foreach (var s in DashboardSql.Plan(f)) sb.AppendLine($"    \"{s.Sql}\",");
        sb.AppendLine("};");
        sb.AppendLine("foreach (var sql in queries)");
        sb.AppendLine("{");
        sb.AppendLine("    Database.RouteTrace = new List<string>();   // which path answered it");
        sb.AppendLine("    long t = Stopwatch.GetTimestamp();");
        sb.AppendLine("    var r = db.Execute(sql);");
        sb.AppendLine("    double ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;");
        sb.AppendLine("    string route = Database.RouteTrace.LastOrDefault(x => x.StartsWith(\"route:\")) ?? \"\";");
        sb.AppendLine("    Database.RouteTrace = null;");
        sb.AppendLine("    Console.WriteLine($\"{ms,9:F2} ms  {r.RowCount,3} rows  {route.Split('#')[0]}\");");
        sb.AppendLine("    Console.WriteLine(\"            \" + sql);");
        sb.AppendLine("    if (r.RowCount > 0) Console.WriteLine(\"            first row: \" + string.Join(\", \", r.Rows[0].Values));");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
