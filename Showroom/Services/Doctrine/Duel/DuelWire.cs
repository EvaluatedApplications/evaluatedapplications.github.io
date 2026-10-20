using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Doctrine;

namespace ShowroomDoctrine.Duel;

// THE WIRE of the Doctrine duel (no Blazor, no network object: bytes, topics and JSON only, so the page, the service and the proof compile this very file).
// Every message is a small JSON object {"v":1,...}; the picks and the result travel as base64 of the library's own bytes (DuelPicks.Canonical, DuelRoundResult.Encode
// gzipped). Reading is trim-safe (JsonDocument / Utf8JsonWriter, no reflection). Every reader returns null/false on damage and never throws.

/// <summary>The protocol's constants and the room-code rules.</summary>
public static class DuelProtocol
{
    public const int Version = 1;
    /// <summary>Every topic the duel publishes or subscribes lives under this (plenum/v1/doctrine/&lt;code&gt;/...). Never pzc/, never another Plenum root.</summary>
    public const string Root = "plenum/v1/doctrine/";
    public static readonly int[] RoundChoices = [3, 5, 8];
    public static readonly int[] TimerPresets = [30, 45, 60, 90];
    public const int DefaultRounds = 3;
    public const int DefaultTimerSec = 45;
    const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>The build of the game both seats must share (the Doctrine library's assembly version). A seat that differs is refused at join.</summary>
    public static string GameTag { get; } = typeof(DuelGame).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>A code is 4 to 12 letters or digits, upper case: it goes into a topic, so no '/', '+' or '#' can ride in on it.</summary>
    public static bool ValidCode(string? c)
    {
        if (c is null || c.Length < 4 || c.Length > 12) return false;
        foreach (var ch in c) if (!((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))) return false;
        return true;
    }

    /// <summary>What a person typed, made into a code (trimmed, upper case); null if it is not a valid one.</summary>
    public static string? Normalize(string? typed)
    {
        var c = (typed ?? "").Trim().ToUpperInvariant();
        return ValidCode(c) ? c : null;
    }

    public static string NewCode(Func<int, int> below)
    {
        var sb = new StringBuilder(6);
        for (int i = 0; i < 6; i++) sb.Append(Alphabet[below(Alphabet.Length)]);
        return sb.ToString();
    }

    public static string SeatName(Seat s) => s == Seat.A ? "A" : "B";
    public static bool TrySeat(string? s, out Seat seat)
    {
        seat = Seat.A;
        if (s == "A") return true;
        if (s == "B") { seat = Seat.B; return true; }
        return false;
    }
}

public enum DuelKind { Room, Join, Ready, Commit, Reveal, Result, Chunk, Next, Presence, End, Rematch, RematchGo }

/// <summary>The topic names of one room, and the reading of a topic back into (kind, round, seat, index, id).</summary>
public sealed class DuelTopics
{
    public string Code { get; }
    public string Root { get; }
    public DuelTopics(string code) { Code = code; Root = DuelProtocol.Root + code + "/"; }

    public string Room => Root + "room";
    public string Join(string cid) => Root + "join/" + cid;
    public string Ready(Seat s) => Root + "ready/" + DuelProtocol.SeatName(s);
    public string Commit(int round, Seat s) => Root + "commit/" + round + "/" + DuelProtocol.SeatName(s);
    public string Reveal(int round, Seat s) => Root + "reveal/" + round + "/" + DuelProtocol.SeatName(s);
    public string Result(int round) => Root + "result/" + round;
    public string Chunk(int round, int i) => Root + "result/" + round + "/" + i;
    public string Next(int round, Seat s) => Root + "next/" + round + "/" + DuelProtocol.SeatName(s);
    public string Presence(Seat s) => Root + "presence/" + DuelProtocol.SeatName(s);
    public string End => Root + "end";
    public string Rematch(Seat s) => Root + "rematch/" + DuelProtocol.SeatName(s);
    public string RematchGo => Root + "rematch/go";
    /// <summary>The one filter a seat subscribes to.</summary>
    public string Filter => Root + "#";

    public readonly record struct Parsed(DuelKind Kind, int Round, Seat Seat, int Index, string Id);

    /// <summary>Read a topic of THIS room; false for anything else (another room, another root, a malformed tail).</summary>
    public bool TryParse(string topic, out Parsed p)
    {
        p = default;
        if (!topic.StartsWith(Root, StringComparison.Ordinal)) return false;
        var parts = topic.Substring(Root.Length).Split('/');
        switch (parts[0])
        {
            case "room" when parts.Length == 1: p = new(DuelKind.Room, 0, Seat.A, 0, ""); return true;
            case "end" when parts.Length == 1: p = new(DuelKind.End, 0, Seat.A, 0, ""); return true;
            case "join" when parts.Length == 2 && parts[1].Length is > 0 and <= 40: p = new(DuelKind.Join, 0, Seat.A, 0, parts[1]); return true;
            case "ready" when parts.Length == 2 && DuelProtocol.TrySeat(parts[1], out var rs): p = new(DuelKind.Ready, 0, rs, 0, ""); return true;
            case "presence" when parts.Length == 2 && DuelProtocol.TrySeat(parts[1], out var ps): p = new(DuelKind.Presence, 0, ps, 0, ""); return true;
            case "rematch" when parts.Length == 2 && parts[1] == "go": p = new(DuelKind.RematchGo, 0, Seat.A, 0, ""); return true;
            case "rematch" when parts.Length == 2 && DuelProtocol.TrySeat(parts[1], out var ms): p = new(DuelKind.Rematch, 0, ms, 0, ""); return true;
            case "commit" or "reveal" or "next" when parts.Length == 3 && Num(parts[1], out int r) && DuelProtocol.TrySeat(parts[2], out var s):
                p = new(parts[0] == "commit" ? DuelKind.Commit : parts[0] == "reveal" ? DuelKind.Reveal : DuelKind.Next, r, s, 0, ""); return true;
            case "result" when parts.Length == 2 && Num(parts[1], out int rr): p = new(DuelKind.Result, rr, Seat.A, 0, ""); return true;
            case "result" when parts.Length == 3 && Num(parts[1], out int cr) && Num(parts[2], out int ci): p = new(DuelKind.Chunk, cr, Seat.A, ci, ""); return true;
        }
        return false;
    }

    static bool Num(string s, out int v) => int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v) && v is >= 0 and < 1000 && s.Length <= 3;
}

// ---- the messages ---------------------------------------------------------------------------------------------------------------------------------------------

/// <summary>The retained room: the source of truth for a reconnecting seat. Written only by seat A, with a rising Rev.</summary>
public sealed record RoomMsg(int Rev, string Ver, int Seed, int Rounds, int TimerSec, string State, int Round, long Open, long Deadline, long Now, string Host, string Guest, string GuestCid);
public sealed record JoinMsg(string Cid, string Name);
public sealed record CommitMsg(byte[] C, bool Auto, bool ByHost);
public sealed record RevealMsg(byte[] P, byte[] N, bool Auto, bool ByHost);
public sealed record PresenceMsg(bool On, string Cid, long Ts);
public sealed record EndMsg(string Reason, Seat By, int FieldsA, int FieldsB, string Text);

/// <summary>The header of a round's result: the facts every seat shows, the digest the receiver checks, and the data (inline when it fits, else in numbered chunks).</summary>
public sealed record ResultHeader(int Round, int Chunks, int Length, ulong Digest, int[] Idle, bool[] Auto, bool[] Forfeit, string[] Notes, bool Final, string? Inline);

public static class DuelWire
{
    public const int MaxPayload = 4 * 1024 * 1024;
    public const int MaxInflated = 16 * 1024 * 1024;

    // ---- JSON plumbing ------------------------------------------------------------------------------------------------

    static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("v", DuelProtocol.Version);
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
            if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n == DuelProtocol.Version) return d;
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

    // ---- room ---------------------------------------------------------------------------------------------------------

    public static byte[] Room(RoomMsg m) => Write(w =>
    {
        w.WriteNumber("rev", m.Rev); w.WriteString("ver", m.Ver); w.WriteNumber("seed", m.Seed); w.WriteNumber("rounds", m.Rounds); w.WriteNumber("timer", m.TimerSec);
        w.WriteString("state", m.State); w.WriteNumber("round", m.Round); w.WriteNumber("open", m.Open); w.WriteNumber("deadline", m.Deadline); w.WriteNumber("now", m.Now);
        w.WriteString("host", m.Host); w.WriteString("guest", m.Guest); w.WriteString("gcid", m.GuestCid);
    });

    public static RoomMsg? ReadRoom(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement;
        var m = new RoomMsg(Int(e, "rev"), Str(e, "ver"), Int(e, "seed"), Int(e, "rounds"), Int(e, "timer"), Str(e, "state"), Int(e, "round"), Long(e, "open"), Long(e, "deadline"), Long(e, "now"), Str(e, "host"), Str(e, "guest"), Str(e, "gcid"));
        if (m.State is not ("lobby" or "play" or "end") || m.Rounds is < 1 or > 200 || m.TimerSec is < 1 or > 3600 || m.Round < 0) return null;
        return m;
    }

    // ---- join / ready / presence / next / end / rematch -----------------------------------------------------------------

    public static byte[] Join(JoinMsg m) => Write(w => { w.WriteString("cid", m.Cid); w.WriteString("name", m.Name); });
    public static JoinMsg? ReadJoin(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; var cid = Str(e, "cid"); return cid.Length is 0 or > 40 ? null : new JoinMsg(cid, Clean(Str(e, "name"))); }

    public static byte[] Flag(string name, bool on) => Write(w => w.WriteBoolean(name, on));
    public static bool? ReadFlag(byte[] p, string name) { using var d = Open(p); if (d is null) return null; return Bool(d.RootElement, name); }

    public static byte[] Presence(PresenceMsg m) => Write(w => { w.WriteBoolean("on", m.On); w.WriteString("cid", m.Cid); w.WriteNumber("ts", m.Ts); });
    public static PresenceMsg? ReadPresence(byte[] p) { using var d = Open(p); if (d is null) return null; var e = d.RootElement; return new PresenceMsg(Bool(e, "on"), Str(e, "cid"), Long(e, "ts")); }

    public static byte[] Next() => Write(w => w.WriteBoolean("seen", true));

    public static byte[] End(EndMsg m) => Write(w => { w.WriteString("reason", m.Reason); w.WriteString("by", DuelProtocol.SeatName(m.By)); w.WriteNumber("fa", m.FieldsA); w.WriteNumber("fb", m.FieldsB); w.WriteString("text", m.Text); });
    public static EndMsg? ReadEnd(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var reason = Str(e, "reason");
        if (reason is not ("final" or "hostLeft" or "guestLeft")) return null;
        DuelProtocol.TrySeat(Str(e, "by"), out var by);
        return new EndMsg(reason, by, Int(e, "fa"), Int(e, "fb"), Clean(Str(e, "text")));
    }

    public static byte[] RematchGo(string code) => Write(w => w.WriteString("code", code));
    public static string? ReadRematchGo(byte[] p) { using var d = Open(p); if (d is null) return null; return DuelProtocol.Normalize(Str(d.RootElement, "code")); }

    // ---- commit / reveal ----------------------------------------------------------------------------------------------

    public static byte[] Commit(CommitMsg m) => Write(w => { w.WriteString("c", Convert.ToBase64String(m.C)); w.WriteBoolean("auto", m.Auto); w.WriteBoolean("host", m.ByHost); });
    public static CommitMsg? ReadCommit(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var c = B64(e, "c");
        return c is { Length: 32 } ? new CommitMsg(c, Bool(e, "auto"), Bool(e, "host")) : null;
    }

    public static byte[] Reveal(RevealMsg m) => Write(w => { w.WriteString("p", Convert.ToBase64String(m.P)); w.WriteString("n", Convert.ToBase64String(m.N)); w.WriteBoolean("auto", m.Auto); w.WriteBoolean("host", m.ByHost); });
    public static RevealMsg? ReadReveal(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement; var picks = B64(e, "p"); var nonce = B64(e, "n");
        return picks is null || nonce is null || picks.Length > 4096 || nonce.Length > 256 ? null : new RevealMsg(picks, nonce, Bool(e, "auto"), Bool(e, "host"));
    }

    // ---- result: header, gzip + base64, chunks --------------------------------------------------------------------------

    public static byte[] Gzip(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    /// <summary>Inflate with a ceiling, so a hostile message cannot blow up memory. Null on damage.</summary>
    public static byte[]? Gunzip(byte[] zipped)
    {
        try
        {
            using var src = new MemoryStream(zipped);
            using var z = new GZipStream(src, CompressionMode.Decompress);
            using var outp = new MemoryStream();
            var buf = new byte[16384]; int n;
            while ((n = z.Read(buf, 0, buf.Length)) > 0) { outp.Write(buf, 0, n); if (outp.Length > MaxInflated) return null; }
            return outp.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>The result as the text that travels: gzip, then base64. This is the unit that is cut into chunks when it is over the payload limit.</summary>
    public static string PackResult(DuelRoundResult r) => Convert.ToBase64String(Gzip(r.Encode()));

    public static DuelRoundResult? UnpackResult(string b64, out string error)
    {
        error = "";
        byte[] zipped;
        try { zipped = Convert.FromBase64String(b64); } catch (FormatException) { error = "the result was not base64"; return null; }
        var raw = Gunzip(zipped);
        if (raw is null) { error = "the result did not inflate"; return null; }
        if (!DuelRoundResult.TryDecode(raw, out var res, out error)) return null;
        return res;
    }

    /// <summary>Cut the packed text into pieces of at most <paramref name="max"/> characters (one piece when it fits).</summary>
    public static IReadOnlyList<string> Cut(string packed, int max)
    {
        max = Math.Max(64, max);
        if (packed.Length <= max) return [packed];
        var parts = new List<string>();
        for (int i = 0; i < packed.Length; i += max) parts.Add(packed.Substring(i, Math.Min(max, packed.Length - i)));
        return parts;
    }

    public static byte[] ResultHeaderBytes(ResultHeader h) => Write(w =>
    {
        w.WriteNumber("round", h.Round); w.WriteNumber("n", h.Chunks); w.WriteNumber("len", h.Length); w.WriteString("dig", h.Digest.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteStartArray("idle"); foreach (var i in h.Idle) w.WriteNumberValue(i); w.WriteEndArray();
        w.WriteStartArray("auto"); foreach (var b in h.Auto) w.WriteBooleanValue(b); w.WriteEndArray();
        w.WriteStartArray("forfeit"); foreach (var b in h.Forfeit) w.WriteBooleanValue(b); w.WriteEndArray();
        w.WriteStartArray("notes"); foreach (var n in h.Notes) w.WriteStringValue(n); w.WriteEndArray();
        w.WriteBoolean("final", h.Final);
        if (h.Inline is not null) w.WriteString("d", h.Inline);
    });

    public static ResultHeader? ReadResultHeader(byte[] p)
    {
        using var d = Open(p); if (d is null) return null;
        var e = d.RootElement;
        if (!ulong.TryParse(Str(e, "dig"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var dig)) return null;
        int n = Int(e, "n", 1);
        if (n is < 1 or > 4096) return null;
        var idle = new int[2]; var auto = new bool[2]; var forfeit = new bool[2]; var notes = new List<string>();
        if (e.TryGetProperty("idle", out var ia) && ia.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var x in ia.EnumerateArray()) { if (i < 2 && x.TryGetInt32(out int v)) idle[i] = v; i++; } }
        if (e.TryGetProperty("auto", out var aa) && aa.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var x in aa.EnumerateArray()) { if (i < 2) auto[i] = x.ValueKind == JsonValueKind.True; i++; } }
        if (e.TryGetProperty("forfeit", out var fa) && fa.ValueKind == JsonValueKind.Array) { int i = 0; foreach (var x in fa.EnumerateArray()) { if (i < 2) forfeit[i] = x.ValueKind == JsonValueKind.True; i++; } }
        if (e.TryGetProperty("notes", out var na) && na.ValueKind == JsonValueKind.Array) foreach (var x in na.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && notes.Count < 8) notes.Add(Clean(x.GetString() ?? ""));
        string? inline = e.TryGetProperty("d", out var dd) && dd.ValueKind == JsonValueKind.String ? dd.GetString() : null;
        if (n == 1 && inline is null) return null;
        return new ResultHeader(Int(e, "round"), n, Int(e, "len"), dig, idle, auto, forfeit, notes.ToArray(), Bool(e, "final"), inline);
    }

    public static byte[] ChunkBytes(int i, string data) => Write(w => { w.WriteNumber("i", i); w.WriteString("d", data); });
    public static string? ReadChunk(byte[] p, out int index) { index = -1; using var d = Open(p); if (d is null) return null; index = Int(d.RootElement, "i", -1); var s = d.RootElement.TryGetProperty("d", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null; return index < 0 ? null : s; }

    /// <summary>A name or a note that came off the wire: control characters out, length capped (it is shown, never interpreted).</summary>
    public static string Clean(string s)
    {
        var sb = new StringBuilder(Math.Min(s.Length, 200));
        foreach (var c in s) { if (sb.Length >= 200) break; sb.Append(char.IsControl(c) ? ' ' : c); }
        return sb.ToString().Trim();
    }
}
