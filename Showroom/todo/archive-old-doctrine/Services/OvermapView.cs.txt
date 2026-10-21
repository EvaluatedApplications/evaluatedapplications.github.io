using System.Net;
using System.Text;
using Doctrine;

namespace ShowroomDoctrine;

// THE MAP AS A DRAWING. A pure function of what the library reports (Provinces(), the briefing and the engine's arena catalogue): nothing is drawn that the
// library does not say. The enemy capital is at the top, yours at the bottom, three roads between them, each province a box coloured by who holds it. The
// markup is plain SVG text (an SVG <text> cannot be written in a Razor file), so the page injects it; every province carries data attributes the proof reads.
// The drawing is not interactive: the lane cards beside it are where a pick is made.

public static class OvermapView
{
    const int W = 400, ColW = 120, BoxH = 90, Gap = 10, CapH = 46, HeadH = 22, MaxRows = 4;
    static readonly int[] ColX = [6, 140, 274];

    /// <summary>The class a holder's province wears (the stylesheet colours it).</summary>
    public static string OwnerClass(Holder h) => h switch { Holder.Player => "dc-p-player", Holder.Enemy => "dc-p-enemy", _ => "dc-p-neutral" };
    public static string OwnerName(Holder h) => h switch { Holder.Player => "player", Holder.Enemy => "enemy", _ => "neutral" };

    /// <summary>The rows of the lanes the drawing shows, nearest the enemy first: the farthest province present down to at most <see cref="MaxRows"/> rows.</summary>
    public static (int Hi, int Lo) Window(IReadOnlyList<Province> provs)
    {
        int longest = provs.Where(p => !p.IsCapital).Select(p => p.Index + 1).DefaultIfEmpty(MapKeys.StartLaneLength).Max();
        longest = Math.Max(longest, MapKeys.StartLaneLength);
        int hi = longest - 1;
        return (hi, Math.Max(0, hi - (MaxRows - 1)));
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    static string Num(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Svg(IReadOnlyList<Province> provs, CampaignBriefing? briefing, Func<int, ArenaInfo?> arenaOf)
    {
        var targets = new HashSet<int>((briefing?.Lanes ?? []).Where(l => l.Target is not null).Select(l => l.Target!.Key));
        var (hi, lo) = Window(provs);
        var enemyCap = provs.FirstOrDefault(p => p.Key == MapKeys.EnemyCapital);
        var myCap = provs.First(p => p.Key == MapKeys.PlayerCapital);
        int rows = hi - lo + 1;
        int y = 0;
        var sb = new StringBuilder();
        int height = HeadH + (enemyCap is null ? 0 : CapH + Gap) + rows * (BoxH + Gap) + (lo > 0 ? 22 : 0) + CapH + 4;
        sb.Append($"<svg class=\"dc-mapsvg\" viewBox=\"0 0 {Num(W)} {Num(height)}\" role=\"img\" aria-label=\"The map of the war: the enemy capital above, yours below, and the roads between them\" preserveAspectRatio=\"xMidYMin meet\">");
        for (int lane = 0; lane < MapKeys.Lanes; lane++)
            sb.Append($"<text class=\"dc-road\" x=\"{Num(ColX[lane] + ColW / 2)}\" y=\"14\" text-anchor=\"middle\">{E(DoctrineCampaignWords.Road(lane))}</text>");
        y = HeadH;
        if (enemyCap is not null) { Capital(sb, enemyCap, y, targets, arenaOf); y += CapH + Gap; }
        for (int idx = hi; idx >= lo; idx--)
        {
            for (int lane = 0; lane < MapKeys.Lanes; lane++)
            {
                var p = provs.FirstOrDefault(q => !q.IsCapital && q.Lane == lane && q.Index == idx);
                if (p is not null) Box(sb, p, ColX[lane], y, targets.Contains(p.Key), arenaOf(p.ArenaIndex));
            }
            y += BoxH + Gap;
        }
        if (lo > 0) { sb.Append($"<text class=\"dc-behind\" x=\"{Num(W / 2)}\" y=\"{Num(y + 12)}\" text-anchor=\"middle\">The first {E(DoctrineWords.Cardinal(lo))} {(lo == 1 ? "province" : "provinces")} of each road lie behind this view.</text>"); y += 22; }
        Capital(sb, myCap, y, targets, arenaOf);
        sb.Append("</svg>");
        return sb.ToString();
    }

    static void Attrs(StringBuilder sb, Province p, bool target, ArenaInfo? a)
        => sb.Append($"<g class=\"dc-prov {OwnerClass(p.Owner)}\" data-key=\"{Num(p.Key)}\" data-lane=\"{Num(p.Lane)}\" data-index=\"{Num(p.Index)}\" data-owner=\"{OwnerName(p.Owner)}\" data-feature=\"{p.Feature}\" data-arena=\"{E(a?.Id ?? "")}\" data-yield=\"{Num(p.Yield)}\" data-siege=\"{(a?.IsSiege == true ? "true" : "false")}\" data-capital=\"{(p.IsCapital ? "true" : "false")}\" data-target=\"{(target ? "true" : "false")}\">");

    static void Icon(StringBuilder sb, Feature f, int x, int y)
    {
        switch (f)
        {
            case Feature.Barracks: sb.Append($"<rect class=\"dc-ic\" x=\"{Num(x)}\" y=\"{Num(y)}\" width=\"12\" height=\"12\"/>"); break;
            case Feature.Stables: sb.Append($"<polygon class=\"dc-ic\" points=\"{Num(x)},{Num(y + 12)} {Num(x + 6)},{Num(y)} {Num(x + 12)},{Num(y + 12)}\"/>"); break;
            case Feature.Shrine: sb.Append($"<polygon class=\"dc-ic\" points=\"{Num(x + 6)},{Num(y)} {Num(x + 12)},{Num(y + 6)} {Num(x + 6)},{Num(y + 12)} {Num(x)},{Num(y + 6)}\"/>"); break;
            case Feature.Granary: sb.Append($"<circle class=\"dc-ic\" cx=\"{Num(x + 6)}\" cy=\"{Num(y + 6)}\" r=\"6\"/>"); break;
        }
    }

    static void SiegeMark(StringBuilder sb, int x, int y)
        => sb.Append($"<path class=\"dc-siegemark\" d=\"M{Num(x)},{Num(y)} v-9 h3 v3 h3 v-3 h3 v3 h3 v-3 h3 v9 z\"/>");

    static void Box(StringBuilder sb, Province p, int x, int y, bool target, ArenaInfo? a)
    {
        Attrs(sb, p, target, a);
        sb.Append($"<rect class=\"dc-box\" x=\"{Num(x)}\" y=\"{Num(y)}\" width=\"{Num(ColW)}\" height=\"{Num(BoxH)}\" rx=\"8\"/>");
        if (target) sb.Append($"<rect class=\"dc-target\" x=\"{Num(x - 3)}\" y=\"{Num(y - 3)}\" width=\"{Num(ColW + 6)}\" height=\"{Num(BoxH + 6)}\" rx=\"10\"/>");
        int tx = x + 8;
        if (p.Feature != Feature.None) { Icon(sb, p.Feature, x + 8, y + 7); tx = x + 26; }
        sb.Append($"<text class=\"dc-pname\" x=\"{Num(tx)}\" y=\"{Num(y + 18)}\">{E(DoctrineCampaignWords.FeatureName(p.Feature))}</text>");
        sb.Append($"<text class=\"dc-parena\" x=\"{Num(x + 8)}\" y=\"{Num(y + 36)}\">{E(a?.Name ?? "")}</text>");
        if (a?.IsSiege == true) { SiegeMark(sb, x + 8, y + 54); sb.Append($"<text class=\"dc-pnote\" x=\"{Num(x + 28)}\" y=\"{Num(y + 53)}\">Siege</text>"); }
        else sb.Append($"<text class=\"dc-pnote\" x=\"{Num(x + 8)}\" y=\"{Num(y + 53)}\">Open ground</text>");
        sb.Append($"<text class=\"dc-pyield\" x=\"{Num(x + 8)}\" y=\"{Num(y + 70)}\">Pays {Num(p.Yield)} gold</text>");
        sb.Append($"<text class=\"dc-powner\" x=\"{Num(x + 8)}\" y=\"{Num(y + 84)}\">{E(DoctrineCampaignWords.HolderWord(p.Owner))}</text>");
        sb.Append("</g>");
    }

    static void Capital(StringBuilder sb, Province p, int y, HashSet<int> targets, Func<int, ArenaInfo?> arenaOf)
    {
        var a = arenaOf(p.ArenaIndex);
        bool target = targets.Contains(p.Key);
        Attrs(sb, p, target, a);
        sb.Append($"<rect class=\"dc-box\" x=\"6\" y=\"{Num(y)}\" width=\"{Num(W - 12)}\" height=\"{Num(CapH)}\" rx=\"8\"/>");
        if (target) sb.Append($"<rect class=\"dc-target\" x=\"3\" y=\"{Num(y - 3)}\" width=\"{Num(W - 6)}\" height=\"{Num(CapH + 6)}\" rx=\"10\"/>");
        sb.Append($"<text class=\"dc-pname\" x=\"16\" y=\"{Num(y + 18)}\">{E(DoctrineCampaignWords.ProvinceName(p))}</text>");
        sb.Append($"<text class=\"dc-powner\" x=\"{Num(W - 16)}\" y=\"{Num(y + 18)}\" text-anchor=\"end\">{E(DoctrineCampaignWords.HolderWord(p.Owner))}</text>");
        string ground = a is null ? "" : a.Name + (a.IsSiege ? ", a siege" : ", open ground");
        sb.Append($"<text class=\"dc-parena\" x=\"16\" y=\"{Num(y + 36)}\">{E(ground)}</text>");
        if (a?.IsSiege == true) SiegeMark(sb, W / 2 + 10, y + 36);
        sb.Append($"<text class=\"dc-pyield\" x=\"{Num(W - 16)}\" y=\"{Num(y + 36)}\" text-anchor=\"end\">Pays {Num(p.Yield)} gold</text>");
        sb.Append("</g>");
    }
}
