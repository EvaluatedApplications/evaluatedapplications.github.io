using System.Numerics;
using Doctrine.Battle;

namespace Doctrine.Lane;

/// <summary>
/// The ground of a lane battle, built as an <see cref="Arena"/> (so the baker, the cover rule and the snap are the ones the old battle uses): the LEFT half is generated, the
/// right half is its mirror, so neither side owns the ground. Both base zones are open ground across the whole height (the lanes meet there); between the zones each lane is
/// its own corridor of <see cref="LaneRows"/> rows, walled off from the next lane by one row of rock.
/// Three lane kinds, so the lanes differ: 0 = a NECK (four units wide, in the middle, with a rock ahead of it), 1 = PILLARS (two rocks split the flow, cover behind them),
/// 2 = a DOUBLE NECK (two narrows with a thicket between them). One lane is always kind 0.
/// </summary>
internal sealed class LaneMap
{
    public const int BaseCols = 5;
    public const int LaneRows = 8;

    public Arena Arena { get; }
    public int Lanes { get; }
    public float Width => Arena.Width;
    public float Height => Arena.Height;
    /// <summary>The depth of a base zone in units.</summary>
    public float BaseDepth => BaseCols * Arena.Cell;
    public float PlayerEdgeX => BaseDepth;
    public float EnemyEdgeX => Width - BaseDepth;
    public float LaneLength => EnemyEdgeX - PlayerEdgeX;
    public float[] LaneCentreY { get; }

    LaneMap(Arena arena, int lanes)
    {
        Arena = arena; Lanes = lanes;
        LaneCentreY = new float[lanes];
        for (int k = 0; k < lanes; k++) LaneCentreY[k] = (k * (LaneRows + 1) + LaneRows / 2) * arena.Cell;
    }

    public static LaneMap Build(int lanes, int laneCols)
    {
        int half = BaseCols + laneCols, rows = lanes * LaneRows + (lanes - 1);
        var g = new char[rows][];
        for (int r = 0; r < rows; r++)
        {
            g[r] = new char[half];
            bool wallRow = r % (LaneRows + 1) == LaneRows;
            for (int c = 0; c < half; c++) g[r][c] = wallRow && c >= BaseCols ? '#' : '.';
        }
        for (int k = 0; k < lanes; k++)
        {
            int kind = lanes == 1 ? 0 : k % 3, top = k * (LaneRows + 1);
            // fc = columns from the middle line (0 = the cell next to the mirror). rr = row inside the lane (0..7).
            void Put(int fc0, int fc1, int[] rr, char ch)
            {
                for (int fc = fc0; fc <= fc1; fc++)
                {
                    int c = half - 1 - fc;
                    if (c < BaseCols + 2) continue;
                    foreach (int r in rr) g[top + r][c] = ch;
                }
            }
            switch (kind)
            {
                case 0:
                    Put(0, 4, [0, 1, 2, 5, 6, 7], '#');          // the neck: rows 3 and 4 stay open (4 units)
                    Put(14, 15, [3, 4], '#');                     // a rock ahead of it
                    Put(8, 9, [1, 6], ',');                       // cover on both approaches
                    break;
                case 1:
                    Put(5, 6, [1, 2], '#'); Put(5, 6, [5, 6], '#');   // two pillars
                    Put(10, 11, [3, 4], ','); Put(1, 2, [3, 4], ',');
                    break;
                default:
                    Put(12, 14, [0, 1, 6, 7], '#'); Put(2, 4, [0, 1, 6, 7], '#');   // two narrows (8 units wide)
                    Put(7, 8, [3, 4], ',');
                    break;
            }
        }
        var spec = new ArenaSpec { Id = $"lanes-{lanes}-{laneCols}", Name = $"{lanes} lane(s)", Half = g.Select(r => new string(r)).ToArray(), Cell = 2 };
        return new LaneMap(new Arena(spec), lanes);
    }

    public LaneLayout Layout() => new(Arena.Cell, Width, Height, Arena.FullRows(), LaneCentreY, PlayerEdgeX, EnemyEdgeX);

    /// <summary>The geometry the baker turns into the navmesh.</summary>
    public (List<Vector2> Vertices, List<(int A, int B, int C)> Triangles) Geometry() => Arena.Geometry();
}
