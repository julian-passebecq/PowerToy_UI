namespace PowerRing.Core;

/// <summary>Which profile and which circle (level) the ring shows. Pure state: the window only renders it.</summary>
public sealed class RingNavigator
{
    private readonly List<RingItem> _path = [];
    private RingConfig _config;

    public RingNavigator(RingConfig config)
    {
        _config = config;
        ProfileIndex = Math.Max(0, config.Profiles.FindIndex(x => string.Equals(x.Id, config.StartProfile, StringComparison.OrdinalIgnoreCase)));
    }

    public int ProfileIndex { get; private set; }
    public RingProfile Profile => _config.Profiles[ProfileIndex];
    public IReadOnlyList<RingProfile> Profiles => _config.Profiles;
    public int Depth => _path.Count + 1;
    public bool AtRoot => _path.Count == 0;
    public IReadOnlyList<RingItem> Items => AtRoot ? Profile.Items : _path[^1].Items!;
    /// <summary>"Dev", "Dev › Web", "Dev › Web › Local".</summary>
    public string Breadcrumb => string.Join(" › ", _path.Select(x => x.Label).Prepend(Profile.Name));

    /// <summary>New config (hot reload): keeps the same profile when it still exists, back to its first circle.</summary>
    public void Reload(RingConfig config)
    {
        string current = Profile.Id;
        _config = config;
        int index = config.Profiles.FindIndex(x => string.Equals(x.Id, current, StringComparison.OrdinalIgnoreCase));
        ProfileIndex = index >= 0 ? index : 0;
        _path.Clear();
    }

    public void Open(RingItem group)
    {
        if (!group.IsGroup || group.Items is null || !Items.Contains(group)) throw new InvalidOperationException("Not a circle of the current level.");
        _path.Add(group);
    }

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
        if (index < 0 || index >= _config.Profiles.Count) return;
        ProfileIndex = index;
        _path.Clear();
    }

    public void CycleProfile(int delta) => SetProfile(((ProfileIndex + delta) % _config.Profiles.Count + _config.Profiles.Count) % _config.Profiles.Count);

    /// <summary>Slot centre relative to the ring centre: slot 0 at the top, then clockwise (screen Y grows downward).</summary>
    public static (double X, double Y) SlotOffset(int index, int count, double radius)
    {
        if (count < 1 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        double angle = -Math.PI / 2 + 2 * Math.PI * index / count;
        return (Math.Round(radius * Math.Cos(angle), 6), Math.Round(radius * Math.Sin(angle), 6));
    }

    /// <summary>Default distance from the centre to the slots: halfway between the centre button and the rim.</summary>
    public static double DefaultSlotRadius(RingAppearance a) => (a.CenterSize / 2 + (a.RingSize / 2 - 8)) / 2 + a.SlotSize / 8;
}
