// Fast checks for the Dashboard tool (no timing is reported as a result; this is correctness only):
//  1. the package version the page shows equals the one Showroom.csproj and this project reference;
//  2. every statement the page can issue returns the same rows with the plane on and off, for three filter states;
//  3. the console program the page renders for a filter compiles and runs against the published package.
using System.Diagnostics;
using System.Text.RegularExpressions;
using HoloDb;
using OrdersDemo;

int rows = args.Length > 0 ? int.Parse(args[0]) : 20_000;
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
int failures = 0;
void Check(string name, bool ok, string detail = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {detail}"); if (!ok) failures++; }

// 1. version single source
string csproj = File.ReadAllText(Path.Combine(root, "Showroom.csproj"));
var m = Regex.Match(csproj, @"EvaluatedApplications\.HoloDb""[^>]*>\s*<Version>([^<]+)</Version>");
Check("page version equals Showroom.csproj", m.Success && m.Groups[1].Value == DashboardCode.PackageVersion, $"({DashboardCode.PackageVersion} vs {m.Groups[1].Value})");

// 2. plane and engine agree on every statement
var db = Database.Open();
db.Execute(DashboardSql.Schema);
int customers = OrdersGenerator.Customers(rows);
for (long first = 1; first <= rows; first += DashboardCode.ChunkRows)
{
    var c = OrdersGenerator.Fill(first, (int)Math.Min(DashboardCode.ChunkRows, rows - first + 1), customers);
    db.BulkLoad(OrdersGenerator.Table, OrdersGenerator.Columns, c.AsArrays(), c.Count);
}
db.Execute(DashboardSql.PlaneIndex);
Console.WriteLine($"rows loaded: {db.Execute("SELECT count(*) FROM orders").GetValue(0, 0)}; plane verify: {db.PleromaVerify("orders") ?? "equals a rebuild"}");

var states = new Dictionary<string, OrdersFilter>
{
    ["no filter"] = new(),
    ["region + months"] = Make(f => { f.Regions.Add("London"); f.Regions.Add("Scotland"); f.SetMonths(10, 24); }),
    ["category + status + month"] = Make(f => { f.Categories.Add("Books"); f.Statuses.Add("paid"); f.ClickMonth(5); }),
};
foreach (var (name, f) in states)
{
    foreach (var s in DashboardSql.Plan(f))
    {
        db.PleromaMode = PleromaMode.Always; string a = Canon(db.Execute(s.Sql));
        db.PleromaMode = PleromaMode.Off; string b = Canon(db.Execute(s.Sql));
        Check($"{name}: {s.Id} plane == engine", a == b);
    }
}

// 3. the rendered console program compiles and runs against the published package
string dir = Path.Combine(Path.GetTempPath(), "dashboard-proof-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dir);
File.WriteAllText(Path.Combine(dir, "Program.cs"), DashboardCode.MinimalProgram(states["region + months"], rows));
File.Copy(Path.Combine(root, "Services", "Dashboard", "OrdersGenerator.cs"), Path.Combine(dir, "OrdersGenerator.cs"));
File.WriteAllText(Path.Combine(dir, "Minimal.csproj"),
    $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>" +
    $"<ItemGroup><PackageReference Include=\"{DashboardCode.Package}\" Version=\"{DashboardCode.PackageVersion}\" /></ItemGroup></Project>");
var psi = new ProcessStartInfo("dotnet", "run -c Release") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
using (var p = Process.Start(psi)!)
{
    string output = p.StandardOutput.ReadToEnd(), err = p.StandardError.ReadToEnd();
    p.WaitForExit();
    Console.WriteLine(output);
    if (err.Length > 0) Console.WriteLine(err);
    Check("rendered console program builds and runs", p.ExitCode == 0 && output.Contains("rows"));
    int lines = DashboardCode.MinimalProgram(states["region + months"], rows).Split('\n').Length;
    Console.WriteLine($"program length: {lines} lines; kept at {dir}");
}
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

static OrdersFilter Make(Action<OrdersFilter> set) { var f = new OrdersFilter(); set(f); return f; }
static string Canon(QueryResult r)
{
    var rows = new List<string>();
    for (int i = 0; i < r.RowCount; i++)
        rows.Add(string.Join(",", Enumerable.Range(0, r.Columns.Count).Select(j => r.GetValue(i, j))));
    return string.Join("|", rows.OrderBy(x => x, StringComparer.Ordinal));
}
