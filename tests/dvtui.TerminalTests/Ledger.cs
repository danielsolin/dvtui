namespace dvtui.TerminalTests;

internal sealed class Ledger
{
    private readonly string _path;
    private readonly string _marker;
    private readonly List<string> _lines = [];

    public Ledger(string path, string marker)
    {
        _path = path;
        _marker = marker;
    }

    public void Add(string key, string value)
    {
        _lines.Add($"[{_marker}] {key}: {value}");
        Flush();
    }

    public void Remove(string key)
    {
        _lines.RemoveAll(line => line.Contains(key + ":"));
        Flush();
    }

    public void Flush()
    {
        File.WriteAllLines(_path, _lines);
    }
}
