namespace NetWiv.Cli;

internal sealed class CommandArguments(IEnumerable<string> arguments)
{
    private readonly List<string> _arguments = [.. arguments];

    public bool TakeFlag(params string[] names)
    {
        var index = _arguments.FindIndex(item => names.Contains(item, StringComparer.Ordinal));
        if (index < 0) return false;
        _arguments.RemoveAt(index);
        return true;
    }

    public string? TakeOption(params string[] names)
    {
        for (var index = 0; index < _arguments.Count; index++)
        {
            var argument = _arguments[index];
            foreach (var name in names)
            {
                if (argument == name)
                {
                    if (index + 1 >= _arguments.Count) throw new ArgumentException($"Missing value for {name}.");
                    var value = _arguments[index + 1];
                    _arguments.RemoveRange(index, 2);
                    return value;
                }
                if (argument.StartsWith(name + "=", StringComparison.Ordinal))
                {
                    _arguments.RemoveAt(index);
                    return argument[(name.Length + 1)..];
                }
            }
        }
        return null;
    }

    public IReadOnlyList<string> TakeOptions(params string[] names)
    {
        var result = new List<string>();
        string? value;
        while ((value = TakeOption(names)) is not null) result.Add(value);
        return result;
    }

    public IReadOnlyList<string> TakeAfterSeparator()
    {
        var index = _arguments.IndexOf("--");
        if (index < 0) return [];
        var result = _arguments.Skip(index + 1).ToArray();
        _arguments.RemoveRange(index, _arguments.Count - index);
        return result;
    }

    public string? TakePositional()
    {
        var index = _arguments.FindIndex(item => !item.StartsWith("-", StringComparison.Ordinal));
        if (index < 0) return null;
        var value = _arguments[index];
        _arguments.RemoveAt(index);
        return value;
    }

    public void EnsureEmpty()
    {
        if (_arguments.Count > 0) throw new ArgumentException($"Unexpected argument(s): {string.Join(' ', _arguments)}");
    }
}
