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
        var sb = new StringBuilder();
        sb.Append("{\"cell\":").Append(lay.Cell).Append(",\"width\":").Append(F(lay.Width)).Append(",\"height\":").Append(F(lay.Height));
        sb.Append(",\"playerEdge\":").Append(F(lay.PlayerBaseEdgeX)).Append(",\"enemyEdge\":").Append(F(lay.EnemyBaseEdgeX));
        sb.Append(",\"laneHalf\":").Append(F(half)).Append(",\"holdPct\":").Append(LaneRules.HoldPct);
        sb.Append(",\"laneCentreY\":[").Append(string.Join(",", lay.LaneCentreY.Select(y => F(y)))).Append(']');
        sb.Append(",\"rows\":[").Append(string.Join(",", lay.Rows.Select(r => "\"" + r + "\""))).Append("]}");
        return sb.ToString();
    }

    /// <summary>The state the script cannot read from the packet.</summary>
    public static string Info(LaneMatch m, int selected)
    {
        int lanes = m.Battle.Setup.Lanes;
        var sb = new StringBuilder();
        sb.Append("{\"selected\":").Append(selected).Append(",\"stage\":").Append((int)m.Stage);
        sb.Append(",\"orders\":[");
        for (int s = 0; s < 2; s++)
        {
            if (s > 0) sb.Append(',');
            sb.Append('[').Append(string.Join(",", Enumerable.Range(0, lanes).Select(l => (int)m.Battle.OrderOf((Doctrine.Side)s, l)))).Append(']');
        }
        sb.Append("],\"stagedPacks\":[");
        var h = m.Human;
        if (h is not null) sb.Append(string.Join(",", h.Packs.Select(p => "{\"lane\":" + p.Lane + ",\"kind\":" + p.Kind + ",\"count\":" + LaneEconomy.PackSize + "}")));
        sb.Append("],\"stagedOrders\":{");
        if (h is not null) sb.Append(string.Join(",", h.Orders.OrderBy(o => o.Key).Select(o => "\"" + o.Key + "\":" + (int)o.Value)));
        sb.Append("}}");
        return sb.ToString();
    }
}
