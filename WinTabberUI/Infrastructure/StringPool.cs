namespace WinTabberUI.Infrastructure;

public sealed class StringPool
{
    private readonly Dictionary<string, string> _pool =
        new(StringComparer.Ordinal);

    public string Canonicalize(ReadOnlySpan<char> span)
    {
        var s = span.ToString(); // one allocation per distinct label

        if (_pool.TryGetValue(s, out var existing))
            return existing;

        _pool[s] = s;
        return s;
    }

    public void Clear() => _pool.Clear();
}

