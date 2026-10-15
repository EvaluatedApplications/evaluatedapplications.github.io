// This exact file runs in the browser (Pages/Dashboard.razor) and is shown to the visitor as the file a console app copies
// (it is embedded in the assembly as a resource, see Showroom.csproj). Integer maths only and no shared state: a given
// (rows) yields the same table in a browser and in a console app, whatever the chunk size.
namespace OrdersDemo;

/// <summary>One slice of the table, as typed arrays in <see cref="OrdersGenerator.Columns"/> order.</summary>
public sealed class OrderChunk
{
    public int Count;
    public long[] Id = [], Customer = [], Month = [], Amount = [];
    public string[] Region = [], Category = [], Status = [];

    /// <summary>The arrays in column order, as <c>BulkLoad</c> takes them.</summary>
    public Array[] AsArrays() => new Array[] { Id, Customer, Month, Region, Category, Status, Amount };
}

public static class OrdersGenerator
{
    public const string Table = "orders";
    public const ulong Seed = 20261015UL;
    public const int Months = 36;                 // Jan 2023 .. Dec 2025, stored as 0..35
    public static readonly string[] Columns = { "id", "customer", "month", "region", "category", "status", "amount" };

    public static readonly string[] Regions =
        { "London", "South East", "South West", "East", "Midlands", "North West", "North East", "Scotland" };
    static readonly int[] RegionWeight = { 30, 16, 10, 9, 14, 10, 6, 5 };

    public static readonly string[] Categories =
        { "Books", "Grocery", "Beauty", "Toys", "Pet", "Garden", "Fashion", "Sports", "Home", "Electronics", "Furniture", "Appliances" };
    static readonly int[] CategoryWeight = { 8, 14, 7, 7, 8, 9, 10, 5, 13, 9, 5, 5 };
    static readonly int[] CategoryBase = { 14, 22, 30, 38, 44, 52, 64, 78, 96, 210, 260, 340 };   // typical order value in pounds

    public static readonly string[] Statuses = { "placed", "paid", "shipped", "delivered", "returned" };
    static readonly int[] StatusWeight = { 4, 6, 10, 74, 6 };

    static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    static readonly int[] Season = { 0, -5, -5, 0, 5, 0, -5, -8, 0, 10, 40, 70 };

    // 4096-slot lookup tables: a weighted pick is one array read
    static readonly int[] MonthLut = Lut(Enumerable.Range(0, Months).Select(m => 100 + 3 * m + Season[m % 12]).ToArray());
    static readonly int[] RegionLut = Lut(RegionWeight);
    static readonly int[] CategoryLut = Lut(CategoryWeight);
    static readonly int[] StatusLut = Lut(StatusWeight);

    public static string MonthLabel(int m) => MonthNames[m % 12] + " " + (2023 + m / 12);

    /// <summary>Distinct customers for a table of this size (customer ids run 1..this).</summary>
    public static int Customers(int rows) => Math.Max(1000, rows / 10);

    public static OrderChunk Fill(long firstId, int count, int customers)
    {
        var c = new OrderChunk
        {
            Count = count, Id = new long[count], Customer = new long[count], Month = new long[count], Amount = new long[count],
            Region = new string[count], Category = new string[count], Status = new string[count],
        };
        for (int i = 0; i < count; i++)
        {
            long id = firstId + i;
            ulong s = Seed + (ulong)id * 0x9E3779B97F4A7C15UL;
            ulong r = Next(ref s), r2 = Next(ref s);
            int cat = CategoryLut[(int)((r >> 24) & 4095)];
            long u = (long)(r2 & 1023), v = (long)((r2 >> 10) & 1023);
            c.Id[i] = id;
            c.Customer[i] = 1 + (u * u * customers >> 20);                   // quadratic skew: low ids are the big accounts
            c.Month[i] = MonthLut[(int)(r & 4095)];
            c.Region[i] = Regions[RegionLut[(int)((r >> 12) & 4095)]];
            c.Category[i] = Categories[cat];
            c.Status[i] = Statuses[StatusLut[(int)((r >> 36) & 4095)]];
            c.Amount[i] = CategoryBase[cat] * (100 + (v * v >> 12)) / 100 + (long)((r2 >> 20) % 5);   // whole pounds
        }
        return c;
    }

    static ulong Next(ref ulong s)      // splitmix64
    {
        s += 0x9E3779B97F4A7C15UL;
        ulong z = s;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static int[] Lut(int[] weights)
    {
        int total = weights.Sum(), idx = 0, acc = weights[0];
        var lut = new int[4096];
        for (int i = 0; i < 4096; i++)
        {
            long pos = (long)i * total / 4096;
            while (pos >= acc) acc += weights[++idx];
            lut[i] = idx;
        }
        return lut;
    }
}
