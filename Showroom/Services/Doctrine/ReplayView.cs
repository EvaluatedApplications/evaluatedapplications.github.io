using Doctrine;
using Doctrine.Battle;

namespace ShowroomDoctrine;

/// <summary>A horizontal run of one kind of ground in one row of the arena layout: '#' wall, ',' cover, 'G' gate.</summary>
public readonly record struct GroundRun(char Kind, int Row, int Col, int Length);

/// <summary>
/// How the page reads a recording: which frame is on screen, what the ground and the gate look like in it, and the strengths. Nothing is
/// recomputed from the game: every number is a recorded one (positions are 1/16 of an arena unit), and the gate's state is the frame's own
/// WorldFlags. The ground comes from the arena layout, which the recording does not carry.
/// </summary>
public sealed class ReplayView
{
    public ReplayView(BattleReplay replay, ArenaLayout? layout)
    {
        Replay = replay; Layout = layout;
        var rows = layout?.Cells ?? Array.Empty<string>();
        Cols = rows.Count > 0 ? rows[0].Length : 0; Rows = rows.Count; Cell = layout?.CellSize ?? 2;
        var runs = new List<GroundRun>();
        for (int r = 0; r < rows.Count; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length;)
            {
                char k = row[c]; int e = c;
                while (e < row.Length && row[e] == k) e++;
                if (k is '#' or ',' or 'G') runs.Add(new GroundRun(k, r, c, e - c));
                c = e;
            }
        }
        Ground = runs;
        // The frame the gate first shows as fallen, read from the recording itself.
        for (int i = 0; i < replay.Frames.Count; i++)
            if ((replay.Frames[i].WorldFlags & WorldFlag.GateFallen) != 0) { GateFellAt = replay.Frames[i].Tick; break; }
    }

    public BattleReplay Replay { get; }
    public ArenaLayout? Layout { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int Cell { get; }
    public IReadOnlyList<GroundRun> Ground { get; }
    public int? GateFellAt { get; }
    public bool HasGate => Layout is { GateRadius: > 0 };
    public int Last => Math.Max(0, Replay.Frames.Count - 1);

    /// <summary>Arena size in arena units (the layout's; or the recording's extent when the layout is unknown).</summary>
    public (int W, int H) Units
    {
        get
        {
            if (Cols > 0) return (Cols * Cell, Rows * Cell);
            int mx = 8, my = 8;
            foreach (var f in Replay.Frames) foreach (var b in f.Blobs) { mx = Math.Max(mx, b.X / BattleReplay.Quantum + 4); my = Math.Max(my, b.Y / BattleReplay.Quantum + 4); }
            return (mx, my);
        }
    }

    public ReplayFrame At(int tick) => Replay.Frames[Math.Clamp(tick, 0, Last)];
    public bool GateFallen(int tick) => (At(tick).WorldFlags & WorldFlag.GateFallen) != 0;

    public int Soldiers(int tick, Side side) => At(tick).Blobs.Where(b => b.Side == side).Sum(b => b.Strength);

    /// <summary>Strength of a blob at the start of the recording (the bar a cohort's bar is measured against).</summary>
    public int StartStrength(int blobId) => Replay.Frames[0].Blobs.FirstOrDefault(b => b.Id == blobId).Strength;

    public static double X(ReplayBlob b) => b.X / (double)BattleReplay.Quantum;
    public static double Y(ReplayBlob b) => b.Y / (double)BattleReplay.Quantum;
    /// <summary>Drawn radius in arena units: grows with the square root of the soldiers, so a cohort of four times the size is twice as wide.</summary>
    public static double Radius(ReplayBlob b) => b.Strength <= 0 ? 0.7 : 0.9 + Math.Sqrt(b.Strength) * 0.45;
}
