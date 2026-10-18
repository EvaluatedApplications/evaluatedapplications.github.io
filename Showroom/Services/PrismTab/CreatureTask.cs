using System.Text;
using Tracer.Helpers;

namespace Showroom.Services.PrismTab;

/// <summary>
/// The Creature written as text. The creature stands on a 20x20 grid of walls and apples; the model is shown the 5x5
/// window around it as characters and answers with one move letter, scored by next-token logits over N E S W:
///
///     map: ..#.. | .A... | ..@.. | ..... | ##... best move:   ->  N / E / S / W
///
/// Glyphs: # wall or off the grid, . free, A apple, @ the creature (centre), north is the top row. Tracer's breadth-first
/// search (<see cref="GridTactics.Reachable"/>, four-way moves) is the answer key: it gives every cell its distance to the
/// nearest apple, and a move is right when it lands one step closer. The model is never shown the distance field.
/// The prompt format is the one chosen on the dev set in the 2026-10-18 probe (format C2).
/// </summary>
public sealed class CreatureTask : ITextTask
{
    public const int W = 20, H = 20, Radius = 2, NApples = 8;
    public const double WallDensity = 0.12;

    /// <summary>North, East, South, West: the order of <see cref="Labels"/>, <see cref="TaskItem.Allowed"/> and <see cref="TaskItem.Good"/>.</summary>
    public static readonly (int Dx, int Dy)[] Moves = { (0, -1), (1, 0), (0, 1), (-1, 0) };
    public static readonly string[] MoveLetters = { "N", "E", "S", "W" };
    public static readonly string[] MoveArrows = { "↑", "→", "↓", "←" };

    public IReadOnlyList<string> Labels => MoveLetters;
    public string BaselineName => "a random legal move";

    // The eval set and the training stream are seeded apart, and a training item is also checked against the eval prompts.
    const int EvalSeed = 22;
    HashSet<string>? _evalPrompts;

    public static string Prompt(string[] rows) => "map: " + string.Join(" | ", rows) + " best move: ";

    public static bool Passable(bool[] wall, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && !wall[y * W + x];

    /// <summary>Distance from every cell to its nearest apple by Tracer's BFS (<c>int.MaxValue</c> = unreachable; an apple is 0).</summary>
    public static int[] DistanceField(bool[] wall, IEnumerable<int> apples)
    {
        var dist = new int[W * H];
        Array.Fill(dist, int.MaxValue);
        foreach (var ap in apples)
        {
            foreach (var kv in GridTactics.Reachable(ap % W, ap / W, W * H, (x, y) => Passable(wall, x, y), diagonals: false))
            {
                int c = kv.Key.Y * W + kv.Key.X;
                if (kv.Value < dist[c]) dist[c] = kv.Value;
            }
            dist[ap] = 0;
        }
        return dist;
    }

    /// <summary>The 5x5 window around (x, y) as five strings.</summary>
    public static string[] Window(bool[] wall, ICollection<int> apples, int x, int y)
    {
        var rows = new string[2 * Radius + 1];
        for (int dy = -Radius; dy <= Radius; dy++)
        {
            var sb = new StringBuilder(2 * Radius + 1);
            for (int dx = -Radius; dx <= Radius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                char ch = dx == 0 && dy == 0 ? '@'
                    : !Passable(wall, nx, ny) ? '#'
                    : apples.Contains(ny * W + nx) ? 'A' : '.';
                sb.Append(ch);
            }
            rows[dy + Radius] = sb.ToString();
        }
        return rows;
    }

    /// <summary>The item for the creature standing at (x, y): its window as text, which moves are legal, which are right.</summary>
    public static TaskItem ItemAt(bool[] wall, ICollection<int> apples, int[] dist, int x, int y)
    {
        var allowed = new bool[4]; var good = new bool[4];
        int d0 = dist[y * W + x];
        for (int a = 0; a < 4; a++)
        {
            int nx = x + Moves[a].Dx, ny = y + Moves[a].Dy;
            if (!Passable(wall, nx, ny)) continue;
            allowed[a] = true;
            int nd = dist[ny * W + nx];
            if (d0 != int.MaxValue && d0 > 0 && nd == d0 - 1) good[a] = true;
        }
        return new TaskItem(Prompt(Window(wall, apples, x, y)), allowed, good);
    }

    /// <summary>
    /// A random scored state: a fresh random world, a free cell 1 to 4 steps from the nearest apple whose window shows that
    /// apple (so the right answer can be read off the prompt). Null if this draw found none (the caller draws again).
    /// </summary>
    public static TaskItem? TryRandomItem(Random r)
    {
        var wall = new bool[W * H];
        for (int i = 0; i < W * H; i++) wall[i] = r.NextDouble() < WallDensity;
        var apples = new List<int>();
        int guard = 0;
        while (apples.Count < NApples && guard++ < 4000)
        {
            int i = r.Next(W * H);
            if (!wall[i] && !apples.Contains(i)) apples.Add(i);
        }
        if (apples.Count < NApples) return null;
        var dist = DistanceField(wall, apples);
        for (int k = 0; k < 30; k++)
        {
            int cx = r.Next(W), cy = r.Next(H), ci = cy * W + cx;
            if (wall[ci] || apples.Contains(ci)) continue;
            int d0 = dist[ci];
            if (d0 < 1 || d0 > 4) continue;
            var item = ItemAt(wall, apples, dist, cx, cy);
            if (!item.Good.Contains(true)) continue;
            // The nearest apple must be inside the window, so the answer is visible in the prompt.
            bool seen = false;
            for (int dy = -Radius; dy <= Radius && !seen; dy++)
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    if (apples.Contains(ny * W + nx) && Math.Abs(dx) + Math.Abs(dy) == d0) { seen = true; break; }
                }
            if (seen) return item;
        }
        return null;
    }

    static TaskItem RandomItem(Random r)
    {
        for (int tries = 0; tries < 10000; tries++) { var it = TryRandomItem(r); if (it is not null) return it; }
        throw new InvalidOperationException("Could not draw a creature state.");
    }

    public TaskItem[] MakeEvalSet(int n)
    {
        var r = new Random(EvalSeed);
        var items = new TaskItem[n];
        for (int i = 0; i < n; i++) items[i] = RandomItem(r);
        _evalPrompts = new HashSet<string>(items.Select(i => i.Prompt));
        return items;
    }

    public TaskItem NextTrainItem(Random r)
    {
        if (_evalPrompts is null) MakeEvalSet(PrismTabDefaults.EvalN);
        var evalPrompts = _evalPrompts!;
        // A 5x5 window repeats by chance, so identical TEXT is skipped: the model is never trained on a prompt it is scored on.
        for (int tries = 0; tries < 1000; tries++)
        {
            var it = RandomItem(r);
            if (!evalPrompts.Contains(it.Prompt)) return it;
        }
        throw new InvalidOperationException("Could not draw a training state apart from the eval set.");
    }

    public double Baseline(IReadOnlyList<TaskItem> items) => items.Count == 0 ? 0 : items.Average(i => i.RandomHitRate);
}
