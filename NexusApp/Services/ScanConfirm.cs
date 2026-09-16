namespace NexusApp.Services;

/// <summary>
/// Decision core for the auto-scan confirm debounce. Each RS signature has its own
/// pending count and emitted flag. An unstable candidate does not reset an unrelated
/// stable one. pinFound=false clears every signature, including emitted memory.
/// </summary>
internal sealed class ScanConfirm
{
    private readonly Dictionary<int, int> _counts = new();
    private readonly HashSet<int> _emitted = new();
    private int[] _confirmedVisible = [];

    /// <summary>Highest pending tick count among currently seen, not-yet-emitted values.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Confirmed signatures still present in the latest reading, in reading order.</summary>
    public IReadOnlyList<int> ConfirmedVisible => _confirmedVisible;

    public int? Update(int? value, bool pinFound)
    {
        var newly = Update(value.HasValue ? [value.Value] : Array.Empty<int>(), pinFound);
        return newly.Count == 0 ? null : newly[0];
    }

    /// <summary>
    /// Feed every tick's OCR readings. Returns values newly confirmed by this call.
    /// ConfirmedVisible is the full set of already-stable signatures still in this reading.
    /// </summary>
    public IReadOnlyList<int> Update(IReadOnlyList<int> values, bool pinFound)
    {
        if (!pinFound)
        {
            _counts.Clear();
            _emitted.Clear();
            _confirmedVisible = [];
            PendingCount = 0;
            return Array.Empty<int>();
        }

        values ??= Array.Empty<int>();
        if (values.Count == 0)
            return Array.Empty<int>();
        var seen = new HashSet<int>(values);

        foreach (var key in _counts.Keys.ToList())
        {
            if (!seen.Contains(key) && !_emitted.Contains(key))
                _counts.Remove(key);
        }

        var newly = new List<int>();
        foreach (var v in values)
        {
            _counts.TryGetValue(v, out var n);
            n++;
            _counts[v] = n;
            if (n >= 2 && _emitted.Add(v))
                newly.Add(v);
        }

        var visible = new List<int>();
        var visibleSeen = new HashSet<int>();
        foreach (var v in values)
        {
            if (_emitted.Contains(v) && visibleSeen.Add(v))
                visible.Add(v);
        }
        _confirmedVisible = visible.ToArray();

        PendingCount = 0;
        foreach (var v in values)
        {
            if (_emitted.Contains(v)) continue;
            if (_counts.TryGetValue(v, out var n) && n > PendingCount)
                PendingCount = n;
        }

        return newly;
    }
}
