using Phasor;

// Four tenants, two data bits each, and two tenant bits. The tenant id sits in the high bits of one store: FlagBits = 2 + 2 = 4.
const int dataBits = 2;
const int tenants = 4;
var shared = new Pleroma(new HologramShape { FlagBits = 2 + dataBits });
var separate = new Pleroma[tenants];
for (int t = 0; t < tenants; t++)
    separate[t] = new Pleroma(new HologramShape { FlagBits = dataBits });

// Each write goes to the shared store (tenant in the high bits) and to that tenant's own store.
void Write(int tenant, uint data, long count)
{
    shared.Add(new HologramItem(Key: 1, Flags: ((uint)tenant << dataBits) | data), count);
    separate[tenant].Add(new HologramItem(Key: 1, Flags: data), count);
}
Write(0, 0b01, 2);
Write(1, 0b01, 3);
Write(1, 0b10, 1);
Write(3, 0b11, 4);

// All-tenant aggregate: the marginal on the data bits (the tenant bits are summed out, one transform).
long[] aggregate = shared.Flags(new[] { 0, 1 });
long[] sumOfSeparate = new long[1 << dataBits];
foreach (var s in separate)
    for (int u = 0; u < sumOfSeparate.Length; u++)
        sumOfSeparate[u] += s.Flags()[u];
System.Console.WriteLine($"aggregate {string.Join(" ", aggregate)}; equals the sum of tenant stores: {aggregate.SequenceEqual(sumOfSeparate)}");

// Per-tenant read: the marginal on tenant bits plus data bits, then the tenant's slice.
long[] perTenant = shared.Flags(new[] { 0, 1, 2, 3 });
for (int t = 0; t < tenants; t++)
{
    long[] slice = perTenant.Skip(t << dataBits).Take(1 << dataBits).ToArray();
    System.Console.WriteLine($"tenant {t}: {string.Join(" ", slice)}; equals its own store: {slice.SequenceEqual(separate[t].Flags())}");
}
