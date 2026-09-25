namespace PowerRing.Core;

/// <summary>One button placed on the disc: level 1 = main circle, 2 = its children, 3 = their children.</summary>
public sealed record RingNode(RingItem Item, int Level, int Index, double X, double Y, double Size, double IconSize, double Angle, RingNode? Parent);

/// <summary>
/// Where every button goes, all levels visible at once: the main circle around the centre, each item's children on the
/// next circle right behind it (fanned around its angle), and optionally a third circle. Each circle sits as close as
/// possible: children first slide sideways along their circle (staying centred on their parent where there is room),
/// and the circle only moves out when the whole circle is full. Coordinates are relative to the disc centre, already
/// multiplied by appearance.scale.
/// </summary>
public static class RingLayout
{
    public sealed record Result(IReadOnlyList<RingNode> Nodes, double DiscSize, double CenterSize, IReadOnlyList<double> Radii);

    public static Result Compute(IReadOnlyList<RingItem> items, RingAppearance a)
    {
        double k = a.Scale, gap = a.Spacing * k;
        double center = a.CenterSize * k, s1 = a.SlotSize * k, s2 = a.SatelliteSize * k, s3 = a.ThirdSize * k;
        int n = Math.Max(1, items.Count);
        bool showSecond = a.ShowSatellites, showThird = a.ShowSatellites && a.ShowThirdRing;

        // Circle 1: clear of the centre, and wide enough for n buttons side by side.
        double r1 = Math.Max(center / 2 + gap + s1 / 2, n * (s1 + gap) / (2 * Math.PI));
        if (a.SlotRadius is double fixedRadius) r1 = Math.Max(r1, fixedRadius * k);
        double sector = 2 * Math.PI / n;
        var level1 = new List<RingNode>();
        for (int i = 0; i < items.Count; i++)
        {
            double angle = -Math.PI / 2 + sector * i;
            level1.Add(new RingNode(items[i], 1, i, r1 * Math.Cos(angle), r1 * Math.Sin(angle), s1, a.IconSize * k, angle, null));
        }

        // Circle 2: children packed along the circle right behind circle 1.
        List<RingNode> level2 = showSecond ? Pack(level1, r1 + s1 / 2 + gap * 0.7 + s2 / 2, s2, a.SatelliteIconSize * k, gap * 0.6, 2) : [];
        double r2 = level2.Count > 0 ? Radius(level2[0]) : r1 + s1 / 2 + gap + s2 / 2;

        // Circle 3: the same for the children's children.
        List<RingNode> level3 = showThird && level2.Count > 0 ? Pack(level2, r2 + s2 / 2 + gap * 0.6 + s3 / 2, s3, a.ThirdIconSize * k, gap * 0.4, 3) : [];
        double r3 = level3.Count > 0 ? Radius(level3[0]) : r2;

        var nodes = level1.Concat(level2).Concat(level3).ToList();
        double outer = nodes.Count == 0 ? center / 2 : nodes.Max(x => Math.Sqrt(x.X * x.X + x.Y * x.Y) + x.Size / 2);
        double disc = a.RingSize is double fixedDisc ? Math.Max(fixedDisc * k, 2 * outer + 8) : 2 * (outer + gap + 6 * k);
        var radii = new List<double> { r1 };
        if (level2.Count > 0) radii.Add(r2);
        if (level3.Count > 0) radii.Add(r3);
        return new Result(nodes, disc, center, radii);
    }

    /// <summary>What an item shows on the next circle: its children, capped at the satellite limit.</summary>
    public static IReadOnlyList<RingItem> Children(RingItem item) =>
        item.Items is { Count: > 0 } items ? items.Take(RingConfigs.MaxSatellites).ToList() : [];

    private static double Radius(RingNode node) => Math.Sqrt(node.X * node.X + node.Y * node.Y);

    /// <summary>
    /// Places every parent's children on one circle: each group starts fanned around its parent's angle, then
    /// neighbours are pushed apart along the circle until none touch (keeping their order). The radius only grows
    /// when the circle cannot hold them all side by side.
    /// </summary>
    private static List<RingNode> Pack(List<RingNode> parents, double minRadius, double size, double iconSize, double gap, int level)
    {
        var wanted = new List<(RingItem Item, int Index, RingNode Parent, double Angle)>();
        int count = parents.Sum(p => Children(p.Item).Count);
        if (count == 0) return [];
        double radius = Math.Max(minRadius, count * (size + gap) / (2 * Math.PI) * 1.02);
        double step = (size + gap) / radius;
        foreach (RingNode parent in parents)
        {
            IReadOnlyList<RingItem> kids = Children(parent.Item);
            for (int c = 0; c < kids.Count; c++)
                wanted.Add((kids[c], c, parent, parent.Angle + (c - (kids.Count - 1) / 2.0) * step));
        }

        // 1D packing on the circle: relax overlapping neighbours in angle order (around the full turn).
        var angles = wanted.Select(w => w.Angle).ToArray();
        int[] order = Enumerable.Range(0, angles.Length).OrderBy(i => angles[i]).ToArray();
        for (int iteration = 0; iteration < 600; iteration++)
        {
            bool moved = false;
            for (int j = 0; j < order.Length && order.Length > 1; j++)
            {
                int a = order[j], b = order[(j + 1) % order.Length];
                double diff = angles[b] - angles[a];
                if (j == order.Length - 1) diff += 2 * Math.PI;
                if (diff < step - 1e-9)
                {
                    double push = (step - diff) / 2;
                    angles[a] -= push;
                    angles[b] += push;
                    moved = true;
                }
            }
            if (!moved) break;
        }

        return wanted.Select((w, i) => new RingNode(w.Item, level, w.Index, radius * Math.Cos(angles[i]), radius * Math.Sin(angles[i]), size, iconSize, angles[i], w.Parent)).ToList();
    }

    /// <summary>True when two buttons are closer than their radii plus <paramref name="gap"/>.</summary>
    public static bool Overlaps(IReadOnlyList<RingNode> nodes, double gap)
    {
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                double dx = nodes[i].X - nodes[j].X, dy = nodes[i].Y - nodes[j].Y;
                if (Math.Sqrt(dx * dx + dy * dy) < (nodes[i].Size + nodes[j].Size) / 2 + gap - 0.01) return true;
            }
        return false;
    }
}
