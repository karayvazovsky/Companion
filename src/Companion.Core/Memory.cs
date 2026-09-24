using System.Globalization;

namespace Companion.Core;

public sealed record Fact(string Id, string Text, DateTimeOffset Created);

public sealed record MemoryResult(bool Ok, string Message, Fact? Fact);

/// <summary>Facts about the user, stored in data\memory.json. Thread-safe: tools run on background threads.</summary>
public sealed class MemoryStore
{
    public const int SoftLimit = 100;

    private sealed class Document
    {
        public List<Fact> Facts { get; set; } = new();
    }

    private readonly string _path;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Document _doc;

    public MemoryStore(string path, IClock clock, ILog log)
    {
        _path = path;
        _clock = clock;
        _doc = JsonFile.Load(path, () => new Document(), log, clock);
    }

    public event Action? Changed;

    public IReadOnlyList<Fact> Facts
    {
        get { lock (_gate) return _doc.Facts.ToList(); }
    }

    public MemoryResult Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return new MemoryResult(false, "The fact is empty.", null);
        Fact fact;
        lock (_gate)
        {
            var existing = _doc.Facts.FirstOrDefault(f => string.Equals(f.Text, text, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return new MemoryResult(true, $"Already remembered as {existing.Id}.", existing);
            if (_doc.Facts.Count >= SoftLimit)
                return new MemoryResult(false, $"Memory is full ({SoftLimit} facts). Merge or forget older facts first.", null);
            fact = new Fact(NextId(), text, _clock.Now);
            _doc.Facts.Add(fact);
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return new MemoryResult(true, $"Saved as {fact.Id}.", fact);
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_doc.Facts.RemoveAll(f => f.Id == id) == 0) return false;
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return true;
    }

    public bool Update(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        lock (_gate)
        {
            var index = _doc.Facts.FindIndex(f => f.Id == id);
            if (index < 0) return false;
            _doc.Facts[index] = _doc.Facts[index] with { Text = text };
            JsonFile.Save(_path, _doc);
        }
        Changed?.Invoke();
        return true;
    }

    private string NextId()
    {
        var max = 0;
        foreach (var fact in _doc.Facts)
        {
            if (fact.Id.StartsWith("f_", StringComparison.Ordinal) &&
                int.TryParse(fact.Id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                max = Math.Max(max, n);
            }
        }
        return $"f_{(max + 1).ToString("00", CultureInfo.InvariantCulture)}";
    }
}
