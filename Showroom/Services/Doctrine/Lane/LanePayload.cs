using System.Globalization;
using System.Text;
using Doctrine.Lane;

namespace ShowroomLane;

/// <summary>
/// The two small JSON texts the page hands the canvas script: the ground (once) and the information that is not in the per-frame packet (the selected lane, each side's orders per lane,
/// what is staged). Built by hand and parsed in the script, so nothing depends on reflection-based serialisation in a trimmed build.
/// </summary>
public static class LanePayload
{
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The ground: cell size, map size, the rows of the map as text, the lanes' centres and half height, the two base edges, the hold line's share of the lane.</summary>
    public static string Layout(LaneMatch m)
    {
        var lay = m.Battle.Layout; int lanes = m.Battle.Setup.Lanes;
        float pitch = lanes > 1 ? lay.LaneCentreY[1] - lay.LaneCentreY[0] : lay.Height;
        float half = lanes > 1 ? (pitch - lay.Cell) / 2f : lay.Height / 2f;
        // a guest on the Enemy side sees the ground turned round: the rows read right to left and the two base edges swap (the lanes keep their numbers)
        bool mirror = m.LocalSide == Doctrine.Side.Enemy;
        float pe = mirror ? lay.Width - lay.EnemyBaseEdgeX : lay.PlayerBaseEdgeX, ee = mirror ? lay.Width - lay.PlayerBaseEdgeX : lay.EnemyBaseEdgeX;
        var sb = new StringBuilder();
        sb.Append("{\"cell\":").Append(lay.Cell).Append(",\"width\":").Append(F(lay.Width)).Append(",\"height\":").Append(F(lay.Height));
        sb.Append(",\"playerEdge\":").Append(F(pe)).Append(",\"enemyEdge\":").Append(F(ee));
        sb.Append(",\"laneHalf\":").Append(F(half)).Append(",\"holdPct\":").Append(LaneRules.HoldPct);
        sb.Append(",\"laneCentreY\":[").Append(string.Join(",", lay.LaneCentreY.Select(y => F(y)))).Append(']');
        sb.Append(",\"mirror\":").Append(mirror ? "true" : "false");
        sb.Append(",\"rows\":[").Append(string.Join(",", lay.Rows.Select(r => "\"" + (mirror ? new string(r.Reverse().ToArray()) : r) + "\""))).Append("]}");
        return sb.ToString();
    }

    /// <summary>The state the script cannot read from the packet.</summary>
    public static string Info(LaneMatch m, int selected)
    {
        int lanes = m.Battle.Setup.Lanes;
        var sb = new StringBuilder();
        sb.Append("{\"selected\":").Append(selected).Append(",\"stage\":").Append((int)m.Stage);
        sb.Append(",\"orders\":[");
        int local = (int)m.LocalSide;   // the first list is always YOURS
        for (int k = 0; k < 2; k++)
        {
            int s = k == 0 ? local : 1 - local;
            if (k > 0) sb.Append(',');
            sb.Append('[').Append(string.Join(",", Enumerable.Range(0, lanes).Select(l => (int)m.Battle.OrderOf((Doctrine.Side)s, l)))).Append(']');
        }
        var h = m.Human;
        // the order chips drawn on the lane (names are the page's words), who is playing, whole seconds left of the pause, and what the last pause's packs came to (only shown during the run)
        sb.Append("],\"human\":").Append(h is not null ? "true" : "false").Append(",\"left\":").Append((int)Math.Ceiling(Math.Max(0, m.PauseLeft)));
        sb.Append(",\"menu\":[").Append(string.Join(",", LaneWords.Orders.Select(o => "{\"order\":" + (int)o + ",\"name\":\"" + LaneWords.OrderName(o) + "\"}"))).Append(']');
        sb.Append(",\"work\":{");
        if (m.Stage == MatchStage.Run)
            sb.Append(string.Join(",", m.LastPlans[local].Packs.GroupBy(p => p.Lane).OrderBy(g => g.Key).Select(g =>
                "\"" + g.Key + "\":[" + string.Join(",", g.GroupBy(p => p.Kind).Select(k => "\"" + LaneWords.KindName(k.Key) + " x" + (k.Count() * LaneEconomy.PackSize) + " sent\"")) + "]")));
        sb.Append("},\"stagedPacks\":[");
        if (h is not null) sb.Append(string.Join(",", h.Packs.Select(p => "{\"lane\":" + p.Lane + ",\"kind\":" + p.Kind + ",\"count\":" + LaneEconomy.PackSize + "}")));
        sb.Append("],\"stagedOrders\":{");
        if (h is not null) sb.Append(string.Join(",", h.Orders.OrderBy(o => o.Key).Select(o => "\"" + o.Key + "\":" + (int)o.Value)));
        sb.Append("}}");
        return sb.ToString();
    }
}
