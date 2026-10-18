using ShowroomPlenum;

static class H
{
    public static int Failures, Checks, BatchFails;
    public static string FirstRed = "";
    public static bool Quiet;

    public static void Check(bool ok, string what)
    {
        Checks++;
        if (!ok)
        {
            BatchFails++;
            if (BatchFails == 1) FirstRed = what;
            if (!Quiet) { Failures++; Console.WriteLine("  FAIL: " + what); }
        }
    }

    public static void Section(string s) { if (!Quiet) Console.WriteLine("== " + s); }
    public static void Note(string s) { if (!Quiet) Console.WriteLine("  " + s); }

    public static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));
    public static byte[] Bytes(string hex) => hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();
    public static byte[] Ascii(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    public static async Task<bool> Until(Func<bool> cond, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; await Task.Delay(10); }
        return cond();
    }
}

sealed class TestClock : IPlenumClock
{
    public long NowMs { get; set; } = 1_000_000;
    public long UnixMs { get; set; } = 1_760_000_000_000;
    public void Advance(long ms) { NowMs += ms; UnixMs += ms; }
}

/// <summary>A broker, a clock and two seated sessions (host = seat 0, guest = seat 1).</summary>
sealed class Room
{
    public LoopbackBroker Broker = new();
    public LoopbackRelayFactory Factory = null!;
    public TestClock Clock = new();
    public PlenumSession Host = null!, Guest = null!;

    public static async Task<Room> Make(ulong seed, bool isPrivate = false)
    {
        var r = new Room();
        r.Factory = new LoopbackRelayFactory(r.Broker);
        r.Host = new PlenumSession(r.Factory, r.Clock, "Ann");
        await r.Host.OpenLobbyAsync();
        await r.Host.CreateRoomAsync(isPrivate, seed);
        r.Guest = new PlenumSession(r.Factory, r.Clock, "Bo");
        await r.Guest.OpenLobbyAsync();
        await r.Guest.JoinAsync(r.Host.Code);
        return r;
    }

    public LoopbackRelay RelayOf(PlenumSession s) => Factory.Made.Last(x => x.ClientId == s.MyId);
    public PlenumSession NewSession(string nick) => new PlenumSession(Factory, Clock, nick);

    /// <summary>Host submits, then guest submits.</summary>
    public async Task Round(string[] hostPlays, string[] guestPlays)
    {
        await Host.SubmitAsync(hostPlays);
        await Guest.SubmitAsync(guestPlays);
    }

    public bool Agree()
    {
        if (Host.Match is null || Guest.Match is null) return false;
        if (Host.Match.ResolvedRounds != Guest.Match.ResolvedRounds) return false;
        for (int r = 0; r <= Host.Match.ResolvedRounds; r++) if (Host.Match.HashAfter(r) != Guest.Match.HashAfter(r)) return false;
        return Host.Match.Hash() == Guest.Match.Hash();
    }
}
