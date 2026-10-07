namespace Calculator;

public sealed class History
{
    private List<double> _entries;

    public void Clear() => _entries = null;

    public double Last() => _entries[^1];
}
