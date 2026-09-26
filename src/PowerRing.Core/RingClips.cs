using System.Text.Json;

namespace PowerRing.Core;

/// <summary>A copied text kept for the "clipboard" table. Memory only: never written to disk.</summary>
public sealed record ClipText(string Text, DateTimeOffset At);

/// <summary>
/// Recent copied texts, newest first, deduplicated (copying the same text again moves it to the top), bounded.
/// Pure logic: the app feeds it from WM_CLIPBOARDUPDATE, and it never persists anything.
/// </summary>
public sealed class ClipHistory(int capacity = 20)
{
    public const int MaxTextLength = 20_000;
    private readonly List<ClipText> _items = [];

    public int Capacity { get; set; } = Math.Clamp(capacity, 1, 50);
    public IReadOnlyList<ClipText> Items => _items;

    /// <summary>False when ignored: empty, whitespace or too long.</summary>
    public bool Add(string? text, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength) return false;
        _items.RemoveAll(x => x.Text == text);
        _items.Insert(0, new ClipText(text, at));
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        return true;
    }

    public void Clear() => _items.Clear();

    /// <summary>One-line preview for a table cell.</summary>
    public static string Preview(string text, int max = 140)
    {
        string flat = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }

    /// <summary>"now", "5 min", "2 h", "3 d".</summary>
    public static string Age(DateTimeOffset at, DateTimeOffset now)
    {
        TimeSpan age = now - at;
        return age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min" : age.TotalDays < 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalDays} d";
    }
}

public sealed record RingNote(string Text, DateTimeOffset Created);

/// <summary>Quick notes of the "notes" table, saved in notes.json next to ring.json (newest first).</summary>
public sealed class RingNotesStore(string directory)
{
    public const int MaxNotes = 200, MaxLength = 4000;
    public string FilePath { get; } = Path.Combine(directory, "notes.json");

    public List<RingNote> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<RingNote>>(File.ReadAllText(FilePath), RingConfigs.Json)?
                .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Text)).Take(MaxNotes).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    public List<RingNote> Add(string text, DateTimeOffset at)
    {
        List<RingNote> notes = Load();
        text = text.Trim();
        if (text.Length == 0) return notes;
        if (text.Length > MaxLength) text = text[..MaxLength];
        notes.RemoveAll(x => x.Text == text);
        notes.Insert(0, new RingNote(text, at));
        Save(notes.Take(MaxNotes).ToList());
        return Load();
    }

    public List<RingNote> Remove(RingNote note)
    {
        List<RingNote> notes = Load();
        notes.RemoveAll(x => x.Text == note.Text && x.Created == note.Created);
        Save(notes);
        return notes;
    }

    private void Save(List<RingNote> notes)
    {
        // A corrupt notes.json is kept aside rather than silently lost.
        if (File.Exists(FilePath))
        {
            try { JsonSerializer.Deserialize<List<RingNote>>(File.ReadAllText(FilePath), RingConfigs.Json); }
            catch (JsonException) { File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: false); }
        }
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(notes, RingConfigs.Json));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
