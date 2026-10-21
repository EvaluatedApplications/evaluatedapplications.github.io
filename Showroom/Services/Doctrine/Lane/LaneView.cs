using Doctrine;
using Doctrine.Battle;
using Doctrine.Lane;

namespace ShowroomLane;

/// <summary>One body as the renderer draws it: the position is BETWEEN two ticks, hit points are the latest tick's.</summary>
public readonly record struct LaneBodyDraw(int Id, Side Side, int Lane, int Kind, float X, float Y, float FacingX, float FacingY, float HpFrac, bool Fighting);

/// <summary>The picture between two ticks (a pure function, so a proof can check it).</summary>
public static class LaneInterp
{
    /// <summary>
    /// Bodies of <paramref name="curr"/>, each placed <paramref name="alpha"/> of the way from its place in <paramref name="prev"/> (0 = where it was, 1 = where it is). Both lists are in id
    /// order, so this is one merge walk. A body that is new in <paramref name="curr"/> (a spawn) has no earlier place and is drawn where it stands; one that fell is not drawn.
    /// </summary>
    public static void Between(LaneSnapshot prev, LaneSnapshot curr, double alpha, List<LaneBodyDraw> into)
    {
        into.Clear();
        float a = (float)Math.Clamp(alpha, 0.0, 1.0);
        var p = prev.Units; var c = curr.Units; int i = 0;
        for (int j = 0; j < c.Count; j++)
        {
            var u = c[j];
            while (i < p.Count && p[i].Id < u.Id) i++;
            float x = u.X, y = u.Y;
            if (i < p.Count && p[i].Id == u.Id) { x = p[i].X + (u.X - p[i].X) * a; y = p[i].Y + (u.Y - p[i].Y) * a; }
            into.Add(new LaneBodyDraw(u.Id, u.Side, u.Lane, u.Kind, x, y, u.FacingX, u.FacingY, Math.Clamp((float)u.Hp / u.MaxHp, 0f, 1f), u.Target >= 0));
        }
    }
}

/// <summary>
/// The bytes handed to the canvas, one packet per frame: eight header floats (tick, stage, status, your base, their base as 0..1 of full, body count, alpha, spare) then ten floats a
/// body (id, side, lane, kind, x, y, facing x, facing y, hit points 0..1, fighting 0/1). Little-endian float32, so the script reads it as a Float32Array.
/// </summary>
public static class LanePack
{
    public const int HeaderFloats = 8, BodyFloats = 10;

    /// <summary>
    /// The packet seen from the human seat: side 0 is always YOU and your base is the first fraction. A guest who sits on the Enemy side (an online match) gets the board turned the
    /// other way round (x mirrored, facing mirrored), so every player sees their own base on their own side; the lanes keep their numbers and the simulation is untouched.
    /// </summary>
    public static byte[] Pack(LaneMatch m)
    {
        var bodies = m.Bodies();
        var f = new float[HeaderFloats + bodies.Count * BodyFloats];
        var s = m.Curr;
        bool mirror = m.LocalSide == Doctrine.Side.Enemy;
        float width = m.Battle.Layout.Width;
        float mine = s.BaseMaxHp == 0 ? 0 : (float)(mirror ? s.EnemyBaseHp : s.PlayerBaseHp) / s.BaseMaxHp;
        float theirs = s.BaseMaxHp == 0 ? 0 : (float)(mirror ? s.PlayerBaseHp : s.EnemyBaseHp) / s.BaseMaxHp;
        f[0] = s.Tick; f[1] = (int)m.Stage; f[2] = (int)s.Status;
        f[3] = mine; f[4] = theirs;
        f[5] = bodies.Count; f[6] = (float)m.Alpha;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i]; int o = HeaderFloats + i * BodyFloats;
            f[o] = b.Id; f[o + 1] = b.Side == m.LocalSide ? 0 : 1; f[o + 2] = b.Lane; f[o + 3] = b.Kind; f[o + 4] = mirror ? width - b.X : b.X; f[o + 5] = b.Y;
            f[o + 6] = mirror ? -b.FacingX : b.FacingX; f[o + 7] = b.FacingY; f[o + 8] = b.HpFrac; f[o + 9] = b.Fighting ? 1f : 0f;
        }
        var bytes = new byte[f.Length * sizeof(float)];
        Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Reads a packet back (the proof's round trip; the page never needs it).</summary>
    public static (float[] Header, List<LaneBodyDraw> Bodies) Unpack(byte[] bytes)
    {
        var f = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        var header = f[..HeaderFloats]; int n = (int)f[5];
        var list = new List<LaneBodyDraw>(n);
        for (int i = 0; i < n; i++)
        {
            int o = HeaderFloats + i * BodyFloats;
            list.Add(new LaneBodyDraw((int)f[o], (Side)(int)f[o + 1], (int)f[o + 2], (int)f[o + 3], f[o + 4], f[o + 5], f[o + 6], f[o + 7], f[o + 8], f[o + 9] > 0.5f));
        }
        return (header, list);
    }
}

/// <summary>Plain words for the page (no jargon, and no figure typed here: names and prices are read from the game's tables).</summary>
public static class LaneWords
{
    /// <summary>Lane names by index, from the map's three kinds (<c>LaneMap</c>: a neck, pillars, a double neck; a one-lane map is a neck).</summary>
    public static string LaneName(int lane, int lanes) => lanes == 1 ? "The lane" : lane switch { 0 => "Lane 1", 1 => "Lane 2", _ => "Lane 3" };
    public static string LaneGround(int lane, int lanes) => lanes == 1 || lane % 3 == 0 ? "a narrow gap in the middle" : lane % 3 == 1 ? "two stone pillars" : "two narrow gaps";

    public static string OrderName(FieldOrderKind o) => o switch
    {
        FieldOrderKind.Advance => "Advance", FieldOrderKind.Hold => "Hold", FieldOrderKind.FallBack => "Fall back", FieldOrderKind.Hunt => "Hunt", FieldOrderKind.Guard => "Guard", _ => "Advance",
    };

    public static string OrderLine(FieldOrderKind o) => o switch
    {
        FieldOrderKind.Advance => "March on the enemy and fight whatever you meet.",
        FieldOrderKind.Hold => "Stop a third of the way down the lane and wait for them.",
        FieldOrderKind.FallBack => "Pull back to your own base.",
        FieldOrderKind.Hunt => "Go for the weakest enemy in the lane.",
        FieldOrderKind.Guard => "Stay together where you stand and keep the ground.",
        _ => "",
    };

    public static readonly FieldOrderKind[] Orders = [FieldOrderKind.Advance, FieldOrderKind.Hold, FieldOrderKind.FallBack, FieldOrderKind.Hunt, FieldOrderKind.Guard];

    static string ClassWord(KindClass c) => c switch { KindClass.Plain => "plain foot", KindClass.Swift => "fast", KindClass.Ranged => "shooters", _ => "armoured", };

    /// <summary>One line about a kind, read from the game's counter ring (who it beats and who beats it).</summary>
    public static string KindLine(int kind)
    {
        var c = UnitRules.ClassOf(Roster.TraitsOf(kind));
        var strong = (KindClass)(((int)c + 1) % UnitRules.KindClassCount);
        var weak = (KindClass)(((int)c + UnitRules.KindClassCount - 1) % UnitRules.KindClassCount);
        return $"{ClassWord(c)}: strong against {ClassWord(strong)}, weak against {ClassWord(weak)}.";
    }

    public static string KindName(int kind) => Roster.NameOf(kind);

    public static string Outcome(LaneStatus s) => Outcome(s, Side.Player);

    /// <summary>The result in the words of the seat that reads it: <paramref name="local"/> is the side that seat sits on.</summary>
    public static string Outcome(LaneStatus s, Side local)
    {
        if (local == Side.Enemy && s is LaneStatus.PlayerWon or LaneStatus.EnemyWon) s = s == LaneStatus.PlayerWon ? LaneStatus.EnemyWon : LaneStatus.PlayerWon;
        return s switch
        {
            LaneStatus.PlayerWon => "You win. The enemy base has fallen.",
            LaneStatus.EnemyWon => "You lose. Your base has fallen.",
            LaneStatus.Draw => "A draw. Neither base fell in time, or both fell together.",
            _ => "",
        };
    }

    /// <summary>Minutes and seconds of a count of seconds, as 0:24.</summary>
    public static string Clock(double seconds) { int s = (int)Math.Ceiling(Math.Max(0, seconds)); return $"{s / 60}:{s % 60:00}"; }
}
