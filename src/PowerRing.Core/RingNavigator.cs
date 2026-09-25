namespace PowerRing.Core;

/// <summary>Which profile and which circle (level) the ring shows. Pure state: the window only renders it.</summary>
public sealed class RingNavigator
{
    private readonly List<RingItem> _path = [];
    // Enabled workspaces only: a disabled one stays in ring.json but is never shown.
    private List<RingProfile> _profiles;

    public RingNavigator(RingConfig config)
    {
        _profiles = config.Profiles.Where(x => x.Enabled).ToList();
        ProfileIndex = Math.Max(0, _profiles.FindIndex(x => string.Equals(x.Id, config.StartProfile, StringComparison.OrdinalIgnoreCase)));
    }

    public int ProfileIndex { get; private set; }
    public RingProfile Profile => _profiles[ProfileIndex];
    public IReadOnlyList<RingProfile> Profiles => _profiles;
    public int Depth => _path.Count + 1;
    public bool AtRoot => _path.Count == 0;
    public IReadOnlyList<RingItem> Items => AtRoot ? Profile.Items : _path[^1].Items!;
    /// <summary>"Dev", "Dev › Web", "Dev › Web › Local".</summary>
    public string Breadcrumb => string.Join(" › ", _path.Select(x => x.Label).Prepend(Profile.Name));

    /// <summary>New config (hot reload): keeps the same profile when it still exists, back to its first circle.</summary>
    public void Reload(RingConfig config)
    {
        string current = Profile.Id;
        _profiles = config.Profiles.Where(x => x.Enabled).ToList();
        int index = _profiles.FindIndex(x => string.Equals(x.Id, current, StringComparison.OrdinalIgnoreCase));
        ProfileIndex = index >= 0 ? index : 0;
        _path.Clear();
    }

    /// <summary>Opens a group shown anywhere on the current circles (main circle or behind it) as the new first circle.</summary>
    public void Open(RingItem group)
    {
        if (!group.IsGroup || group.Items is null || !Visible(Items).Contains(group)) throw new InvalidOperationException("Not a group of the current circles.");
        _path.Add(group);
    }

    private static IEnumerable<RingItem> Visible(IEnumerable<RingItem> items) =>
        items.SelectMany(x => Visible(RingLayout.Children(x)).Prepend(x));

    /// <summary>False when already on the first circle (the caller then closes the ring).</summary>
    public bool Back()
    {
        if (AtRoot) return false;
        _path.RemoveAt(_path.Count - 1);
        return true;
    }

    public void Home() => _path.Clear();

    public void SetProfile(int index)
    {
        if (index < 0 || index >= _profiles.Count) return;
        ProfileIndex = index;
        _path.Clear();
    }

    public void CycleProfile(int delta) => SetProfile(((ProfileIndex + delta) % _profiles.Count + _profiles.Count) % _profiles.Count);

    /// <summary>Slot centre relative to the ring centre: slot 0 at the top, then clockwise (screen Y grows downward).</summary>
    public static (double X, double Y) SlotOffset(int index, int count, double radius)
    {
        if (count < 1 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        double angle = -Math.PI / 2 + 2 * Math.PI * index / count;
        return (Math.Round(radius * Math.Cos(angle), 6), Math.Round(radius * Math.Sin(angle), 6));
    }
}
