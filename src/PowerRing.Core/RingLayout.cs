namespace PowerRing.Core;

/// <summary>One button placed on the disc: level 1 = main circle, 2 = its children, 3 = their children.</summary>
public sealed record RingNode(RingItem Item, int Level, int Index, double X, double Y, double Size, double IconSize, double Angle, RingNode? Parent);

/// <summary>
/// Where every button goes, all levels visible at once: the main circle around the centre, each item's children on the
/// next circle right behind it (fanned around its angle), and optionally a third circle. Each circle starts as close
/// as possible and only moves out until no two buttons touch. Coordinates are relative to the disc centre, already
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

        // Circle 2: fan each item's children around its angle; grow the radius until nothing touches.
        List<RingNode> level2 = [];
        double r2 = r1 + s1 / 2 + gap + s2 / 2;
        if (showSecond && level1.Any(x => Children(x.Item).Count > 0))
        {
            for (int attempt = 0; attempt < 400; attempt++, r2 += 1.5 * k)
            {
                level2 = Fan(level1, r2, s2, a.SatelliteIconSize * k, gap, 2);
                if (!Overlaps(level2, gap * 0.6) && !Overlaps(level1.Concat(level2).ToList(), gap * 0.6)) break;
            }
        }

        // Circle 3: the same for the children's children.
        List<RingNode> level3 = [];
        double r3 = r2 + s2 / 2 + gap + s3 / 2;
        if (showThird && level2.Any(x => Children(x.Item).Count > 0))
        {
            for (int attempt = 0; attempt < 400; attempt++, r3 += 1.5 * k)
            {
                level3 = Fan(level2, r3, s3, a.ThirdIconSize * k, gap * 0.6, 3);
                if (!Overlaps(level3, gap * 0.4) && !Overlaps(level2.Concat(level3).ToList(), gap * 0.4)) break;
            }
        }

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

    private static List<RingNode> Fan(List<RingNode> parents, double radius, double size, double iconSize, double gap, int level)
    {
        var nodes = new List<RingNode>();
        double step = (size + gap) / radius;
        foreach (RingNode parent in parents)
        {
            IReadOnlyList<RingItem> kids = Children(parent.Item);
            for (int c = 0; c < kids.Count; c++)
            {
                double angle = parent.Angle + (c - (kids.Count - 1) / 2.0) * step;
                nodes.Add(new RingNode(kids[c], level, c, radius * Math.Cos(angle), radius * Math.Sin(angle), size, iconSize, angle, parent));
            }
        }
        return nodes;
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
