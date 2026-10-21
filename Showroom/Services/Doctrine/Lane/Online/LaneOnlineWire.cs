using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Doctrine;
using Doctrine.Lane;

namespace ShowroomLane;

// THE WIRE of the online lane battle (no Blazor, no network object: bytes, topics and JSON only, so the page, the session and the proofs compile this very file). Every message is a
// small JSON object {"v":1,...}. Reading is trim-safe (JsonDocument / Utf8JsonWriter, no reflection) and every reader returns null on damage and never throws. EVERY topic lives
// under plenum/v1/doctrine-lane/ (never pzc/, never another root).

public enum LaneRole { Host, Guest }

/// <summary>The constants of the protocol, the room-code rules, the commitment and the names.</summary>
public static class LaneOnlineProtocol
{
    public const int Version = 1;
    /// <summary>Every topic the online lane battle publishes or subscribes lives under this.</summary>
    public const string Root = "plenum/v1/doctrine-lane/";
    public const string RoomRoot = Root + "room/";
    public const string LobbyRoot = Root + "lobby/";
    public const string LobbyFilter = LobbyRoot + "+";
    public const int MaxPacksPerPlan = 64;
    const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>The build both players must share: the library's assembly version plus this protocol's number. A room of another build is not shown and cannot be joined.</summary>
    public static string GameTag { get; } = "lane" + Version + "-" + (typeof(LaneBattle).Assembly.GetName().Version?.ToString() ?? "0");

    /// <summary>A code is 4 to 12 upper-case letters or digits: it goes into a topic, so no '/', '+' or '#' can ride in on it.</summary>
    public static bool ValidCode(string? c)
    {
        if (c is null || c.Length < 4 || c.Length > 12) return false;
        foreach (var ch in c) if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))) return false;
        return true;
    }

    /// <summary>What a person typed (trimmed, upper case, spaces and dashes dropped), made into a code; null if it is not a valid one.</summary>
    public static string? Normalize(string? typed)
    {
        var c = new string((typed ?? "").Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray()).ToUpperInvariant();
        return ValidCode(c) ? c : null;
    }

    public static string NewCode(Func<int, int> below)
    {
        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++) sb.Append(Alphabet[below(Alphabet.Length)]);
        return sb.ToString();
    }

    public static string RoleName(LaneRole r) => r == LaneRole.Host ? "host" : "guest";
    public static LaneRole Other(LaneRole r) => r == LaneRole.Host ? LaneRole.Guest : LaneRole.Host;
    public static bool TryRole(string? s, out LaneRole r)
    {
        r = LaneRole.Host;
        if (s == "host") return true;
        if (s == "guest") { r = LaneRole.Guest; return true; }
        return false;
    }

    /// <summary>The side a role plays in game number <paramref name="gen"/>: the host takes the left base first and the two swap at every rematch (the map is not perfectly even).</summary>
    public static Side SideOf(LaneRole role, int gen) => (role == LaneRole.Host) == (gen % 2 == 0) ? Side.Player : Side.Enemy;

    /// <summary>The commitment to a pause's plays: SHA-256 over the room, the game, the pause, the role, the nonce and the plan. It binds the plan without showing it.</summary>
    public static byte[] Commit(string code, int gen, int pause, LaneRole role, byte[] nonce, string plan)
        => SHA256.HashData(Encoding.UTF8.GetBytes($"doctrine-lane/{Version}|{code}|{gen}|{pause}|{RoleName(role)}|{Convert.ToBase64String(nonce)}|{plan}"));

    public static bool Verify(byte[] commit, string code, int gen, int pause, LaneRole role, byte[] nonce, string plan)
        => commit.Length == 32 && CryptographicOperations.FixedTimeEquals(commit, Commit(code, gen, pause, role, nonce, plan));

    public static string HashHex(ulong h) => h.ToString("x16");
}

/// <summary>Plain made-up names ("Lord Aldric"): no accounts and no personal data. Whatever is typed is cleaned; nothing is asked for.</summary>
public static class LaneNames
{
    static readonly string[] First = ["Aldric", "Berengar", "Cedric", "Dunstan", "Edmund", "Faramond", "Godwin", "Halric", "Ivor", "Jorund", "Kenric", "Leofric", "Merrick", "Osric", "Perrin", "Roderic", "Stannic", "Torvald", "Ulric", "Wystan"];
    public const int MaxLength = 24;

    public static string Random(Func<int, int> below) => "Lord " + First[below(First.Length)];

    /// <summary>Letters, digits, spaces, apostrophes and hyphens only; at most 24 characters; null when nothing is left.</summary>
    public static string? Clean(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var sb = new StringBuilder();
        bool space = true;
        foreach (var ch in typed)
        {
            if (sb.Length >= MaxLength) break;
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-') { sb.Append(ch); space = false; }
            else if (char.IsWhiteSpace(ch) && !space) { sb.Append(' '); space = true; }
        }
        var s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}

/// <summary>The plays of one side at one pause as a short string: packs "lane.kind" joined by commas, then ';', then orders "lane.order" joined by commas.</summary>
public static class LanePlanCodec
{
    public static string Encode(LanePlan plan)
        => string.Join(",", plan.Packs.Select(p => p.Lane + "." + p.Kind)) + ";" + string.Join(",", plan.Orders.Select(o => o.Lane + "." + (int)o.Order));

    /// <summary>Strict: every number in range, at most 64 packs, one order a lane, only the five orders a lane takes. False (with a reason) for anything else.</summary>
    public static bool TryDecode(string text, int lanes, out LanePlan plan, out string why)
    {
        plan = LanePlan.Empty; why = "";
        if (text is null || text.Length > 2048) { why = "too long"; return false; }
        int semi = text.IndexOf(';');
        if (semi < 0 || text.IndexOf(';', semi + 1) >= 0) { why = "no separator"; return false; }
        var packs = new List<PackBuy>(); var orders = new List<LaneOrderChoice>(); var seenLane = new HashSet<int>();
        foreach (var part in text[..semi].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Pair(part, out int l, out int k) || l < 0 || l >= lanes || k < 0 || k >= Roster.Size) { why = "bad pack " + part; return false; }
            packs.Add(new PackBuy(l, k));
            if (packs.Count > LaneOnlineProtocol.MaxPacksPerPlan) { why = "too many packs"; return false; }
        }
        foreach (var part in text[(semi + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Pair(part, out int l, out int o) || l < 0 || l >= lanes || !seenLane.Add(l)) { why = "bad order " + part; return false; }
            var kind = (FieldOrderKind)o;
            if (o < 0 || o > 255 || kind is not (FieldOrderKind.Advance or FieldOrderKind.Hold or FieldOrderKind.FallBack or FieldOrderKind.Hunt or FieldOrderKind.Guard)) { why = "bad order " + part; return false; }
            orders.Add(new LaneOrderChoice(l, kind));
        }
        plan = new LanePlan(packs, orders);
        return true;
    }

    static bool Pair(string s, out int a, out int b)
    {
        a = b = 0;
        int dot = s.IndexOf('.');
        return dot > 0 && int.TryParse(s.AsSpan(0, dot), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out a)
            && int.TryParse(s.AsSpan(dot + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out b);
    }
}

/// <summary>The topic names of one room, and the reading of a topic back into its parts. Pause numbers are the phase index; <c>g</c> is the game number inside the room (rematches count up).</summary>
public sealed class LaneTopics
{
    public string Code { get; }
    public string Root { get; }
    public LaneTopics(string code) { Code = code; Root = LaneOnlineProtocol.RoomRoot + code + "/"; }

    public string Room => Root + "room";
    public string Join(string cid) => Root + "join/" + cid;
    public string Presence(LaneRole r) => Root + "presence/" + LaneOnlineProtocol.RoleName(r);
    string G(int g) => Root + "g" + g + "/";
    public string Ready(int g, LaneRole r) => G(g) + "ready/" + LaneOnlineProtocol.RoleName(r);
    public string Hash(int g, int n, LaneRole r) => G(g) + "hash/" + n + "/" + LaneOnlineProtocol.RoleName(r);
    public string Commit(int g, int n, LaneRole r) => G(g) + "commit/" + n + "/" + LaneOnlineProtocol.RoleName(r);
    public string Reveal(int g, int n, LaneRole r) => G(g) + "reveal/" + n + "/" + LaneOnlineProtocol.RoleName(r);
    public string Fin(int g, LaneRole r) => G(g) + "fin/" + LaneOnlineProtocol.RoleName(r);
    public string End(int g, LaneRole r) => G(g) + "end/" + LaneOnlineProtocol.RoleName(r);
    public string Rematch(int g, LaneRole r) => G(g) + "rematch/" + LaneOnlineProtocol.RoleName(r);
    /// <summary>The one filter a player subscribes to.</summary>
    public string Filter => Root + "#";

    public static string Lobby(string code) => LaneOnlineProtocol.LobbyRoot + code;

    public enum Kind { Room, Join, Presence, Ready, Hash, Commit, Reveal, Fin, End, Rematch }
    public readonly record struct Parsed(Kind Kind, int Gen, int Pause, LaneRole Role, string Id);

    /// <summary>Read a topic of THIS room; false for anything else (another room, another root, a malformed tail).</summary>
    public bool TryParse(string topic, out Parsed p)
    {
        p = default;
        if (!topic.StartsWith(Root, StringComparison.Ordinal)) return false;
        var parts = topic[Root.Length..].Split('/');
        switch (parts[0])
        {
            case "room" when parts.Length == 1: p = new(Kind.Room, 0, 0, LaneRole.Host, ""); return true;
            case "join" when parts.Length == 2 && parts[1].Length is > 0 and <= 40: p = new(Kind.Join, 0, 0, LaneRole.Guest, parts[1]); return true;
            case "presence" when parts.Length == 2 && LaneOnlineProtocol.TryRole(parts[1], out var pr): p = new(Kind.Presence, 0, 0, pr, ""); return true;
        }
        if (parts[0].Length is >= 2 and <= 3 && parts[0][0] == 'g' && Num(parts[0][1..], 100, out int g))
        {
            switch (parts[1 .. ])
            {
                case ["ready", var rr] when LaneOnlineProtocol.TryRole(rr, out var r1): p = new(Kind.Ready, g, 0, r1, ""); return true;
                case ["fin", var fr] when LaneOnlineProtocol.TryRole(fr, out var r2): p = new(Kind.Fin, g, 0, r2, ""); return true;
                case ["end", var er] when LaneOnlineProtocol.TryRole(er, out var r5): p = new(Kind.End, g, 0, r5, ""); return true;
                case ["rematch", var mr] when LaneOnlineProtocol.TryRole(mr, out var r3): p = new(Kind.Rematch, g, 0, r3, ""); return true;
                case ["hash" or "commit" or "reveal", var ns, var rs] when Num(ns, 1000, out int n) && LaneOnlineProtocol.TryRole(rs, out var r4):
                    p = new(parts[1] == "hash" ? Kind.Hash : parts[1] == "commit" ? Kind.Commit : Kind.Reveal, g, n, r4, ""); return true;
            }
        }
        return false;
    }

    static bool Num(string s, int below, out int v) => int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 0 && v < below && s.Length <= 3;
}

// ---- the messages ---------------------------------------------------------------------------------------------------------------------------------------------

/// <summary>The retained room: the host's account of the table. <c>State</c> is lobby (waiting for a guest), seated (a guest has the second seat; both build the match) or play (the start is agreed).</summary>
public sealed record LaneRoomMsg(int Rev, string Ver, int Seed, int Lanes, int LaneCols, int PhaseTicks, int TickCap, int BaseHp, int MaxUnits, string State, int Gen, string Host, string Guest, string GuestCid, long StartAt, long HostNow)
{
    public LaneSetup ToSetup() => new() { Lanes = Lanes, LaneCols = LaneCols, PhaseTicks = PhaseTicks, TickCap = TickCap, BaseHp = BaseHp, MaxUnitsPerSide = MaxUnits };
}
public sealed record LaneJoinMsg(string Cid, string Name, string Ver, long Ts);
public sealed record LaneReadyMsg(string Hash, long T);
public sealed record LaneHashMsg(string Hash);
public sealed record LaneCommitMsg(byte[] C);
/// <summary><c>Missed</c> true = "I never saw your commitment in time, so your plays for this pause are empty": the only way a pause is resolved without the other side's plays, and it is part of the reveal so that both sides read the same decision from one message.</summary>
public sealed record LaneRevealMsg(string Plan, byte[] Nonce, bool Missed);
public sealed record LaneFinMsg(int Tick, int Status, string Hash);
public sealed record LanePresenceMsg(bool On, string Cid, long Ts);
public sealed record LaneEndMsg(string Reason, LaneRole By, LaneRole? Winner);
public sealed record LaneLobbyMsg(string Code, string Host, string Ver, long Ts);

public static class LaneWire
{
    public const int MaxPayload = 16 * 1024;

    // ---- JSON plumbing ------------------------------------------------------------------------------------------------

    static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("v", LaneOnlineProtocol.Version);
            body(w);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    static JsonDocument? Open(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > MaxPayload) return null;
        try
        {
            var d = JsonDocument.Parse(payload);
            if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n == LaneOnlineProtocol.Version) return d;
            d.Dispose();
        }
        catch (JsonException) { }
        return null;
    }

    static string Str(JsonElement e, string name, string def = "") => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? def : def;
    static int Int(JsonElement e, string name, int def = 0) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v) ? v : def;
    static long Long(JsonElement e, string name, long def = 0) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long v) ? v : def;
    static bool Bool(JsonElement e, string name, bool def = false) => e.TryGetProperty(name, out var p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False) ? p.GetBoolean() : def;
    static byte[]? B64(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        try { return Convert.FromBase64String(p.GetString() ?? ""); } catch (FormatException) { return null; }
    }

    /// <summary>A name or a text that came off the wire: control characters out, length capped (it is shown, never interpreted).</summary>
    public static string Clean(string s)
    {
        var sb = new StringBuilder(Math.Min(s.Length, LaneNames.MaxLength));
        foreach (var c in s) { if (sb.Length >= LaneNames.MaxLength) break; sb.Append(char.IsControl(c) || c == '<' || c == '>' ? ' ' : c); }
        return sb.ToString().Trim();
    }

    // ---- room ---------------------------------------------------------------------------------------------------------

    public static byte[] Room(LaneRoomMsg m) => Write(w =>
    {
        w.WriteNumber("rev", m.Rev); w.WriteString("ver", m.Ver); w.WriteNumber("seed", m.Seed);
        w.WriteNumber("lanes", m.Lanes); w.WriteNumber("cols", m.LaneCols); w.WriteNumber("phase", m.PhaseTicks); w.WriteNumber("cap", m.TickCap); w.WriteNumber("hp", m.BaseHp); w.WriteNumber("max", m.MaxUnits);
        w.WriteString("state", m.State); w.WriteNumber("gen", m.Gen); w.WriteString("host", m.Host); w.WriteString("guest", m.Guest); w.WriteString("gcid", m.GuestCid);
        w.WriteNumber("start", m.StartAt); w.WriteNumber("now", m.HostNow);
    });

    /// <summary>The setup is bounded: a host cannot ask a guest's browser for a bigger battle than the page itself builds.</summary>
    public static LaneRoomMsg? ReadRoom(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement;
        var m = new LaneRoomMsg(Int(e, "rev"), Str(e, "ver"), Int(e, "seed"), Int(e, "lanes"), Int(e, "cols"), Int(e, "phase"), Int(e, "cap"), Int(e, "hp"), Int(e, "max"),
            Str(e, "state"), Int(e, "gen"), Clean(Str(e, "host")), Clean(Str(e, "guest")), Str(e, "gcid"), Long(e, "start"), Long(e, "now"));
        if (m.State is not ("lobby" or "seated" or "play") || m.Gen is < 0 or > 98 || m.Rev < 0) return null;
        if (m.Lanes is < 1 or > 3 || m.LaneCols is < 24 or > 80 || m.PhaseTicks is < 100 or > 600 || m.TickCap is < 100 or > 20000 || m.BaseHp is < 1 or > 5000 || m.MaxUnits is < 1 or > 300) return null;
        if (m.GuestCid.Length > 40) return null;
        return m;
    }

    // ---- join / ready / hash / commit / reveal / fin / presence / end / rematch -----------------------------------------

    public static byte[] Join(LaneJoinMsg m) => Write(w => { w.WriteString("cid", m.Cid); w.WriteString("name", m.Name); w.WriteString("ver", m.Ver); w.WriteNumber("ts", m.Ts); });
    public static LaneJoinMsg? ReadJoin(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; var cid = Str(e, "cid"); return cid.Length is 0 or > 40 ? null : new LaneJoinMsg(cid, Clean(Str(e, "name")), Str(e, "ver"), Long(e, "ts")); }

    public static byte[] Ready(LaneReadyMsg m) => Write(w => { w.WriteString("h", m.Hash); w.WriteNumber("t", m.T); });
    public static LaneReadyMsg? ReadReady(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; var h = Str(e, "h"); return h.Length is 0 or > 32 ? null : new LaneReadyMsg(h, Long(e, "t")); }

    public static byte[] Hash(LaneHashMsg m) => Write(w => w.WriteString("h", m.Hash));
    public static LaneHashMsg? ReadHash(byte[] p) { using var d = Open(p); if (d is null) return null; var h = Str(d.RootElement, "h"); return h.Length is 0 or > 32 ? null : new LaneHashMsg(h); }

    public static byte[] Commit(LaneCommitMsg m) => Write(w => w.WriteString("c", Convert.ToBase64String(m.C)));
    public static LaneCommitMsg? ReadCommit(byte[] p) { using var d = Open(p); if (d is null) return null; var c = B64(d.RootElement, "c"); return c is { Length: 32 } ? new LaneCommitMsg(c) : null; }

    public static byte[] Reveal(LaneRevealMsg m) => Write(w => { w.WriteString("p", m.Plan); w.WriteString("n", Convert.ToBase64String(m.Nonce)); w.WriteBoolean("missed", m.Missed); });
    public static LaneRevealMsg? ReadReveal(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var plan = Str(e, "p", "\0"); var nonce = B64(e, "n");
        return plan == "\0" || plan.Length > 2048 || nonce is null || nonce.Length is < 8 or > 64 ? null : new LaneRevealMsg(plan, nonce, Bool(e, "missed"));
    }

    public static byte[] Fin(LaneFinMsg m) => Write(w => { w.WriteNumber("tick", m.Tick); w.WriteNumber("status", m.Status); w.WriteString("h", m.Hash); });
    public static LaneFinMsg? ReadFin(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; var h = Str(e, "h"); return h.Length is 0 or > 32 ? null : new LaneFinMsg(Int(e, "tick"), Int(e, "status"), h); }

    public static byte[] Presence(LanePresenceMsg m) => Write(w => { w.WriteBoolean("on", m.On); w.WriteString("cid", m.Cid); w.WriteNumber("ts", m.Ts); });
    public static LanePresenceMsg? ReadPresence(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; return new LanePresenceMsg(Bool(e, "on"), Str(e, "cid"), Long(e, "ts")); }

    public static byte[] End(LaneEndMsg m) => Write(w => { w.WriteString("reason", m.Reason); w.WriteString("by", LaneOnlineProtocol.RoleName(m.By)); w.WriteString("winner", m.Winner is { } x ? LaneOnlineProtocol.RoleName(x) : ""); });
    public static LaneEndMsg? ReadEnd(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var reason = Str(e, "reason");
        if (reason is not ("left" or "gone" or "away" or "noreveal" or "desync" or "refused")) return null;
        if (!LaneOnlineProtocol.TryRole(Str(e, "by"), out var by)) return null;
        LaneRole? winner = LaneOnlineProtocol.TryRole(Str(e, "winner"), out var w) ? w : null;
        return new LaneEndMsg(reason, by, winner);
    }

    public static byte[] Flag(string name, bool on) => Write(w => w.WriteBoolean(name, on));
    public static bool? ReadFlag(byte[] p, string name) { using var d = Open(p); if (d is null) return null; return Bool(d.RootElement, name); }

    // ---- the lobby entry ------------------------------------------------------------------------------------------------

    public static byte[] Lobby(LaneLobbyMsg m) => Write(w => { w.WriteString("code", m.Code); w.WriteString("host", m.Host); w.WriteString("ver", m.Ver); w.WriteNumber("ts", m.Ts); });
    public static LaneLobbyMsg? ReadLobby(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var code = Str(e, "code");
        return LaneOnlineProtocol.ValidCode(code) ? new LaneLobbyMsg(code, Clean(Str(e, "host")), Str(e, "ver"), Long(e, "ts")) : null;
    }
}
