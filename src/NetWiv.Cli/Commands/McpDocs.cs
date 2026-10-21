using System.Text;

namespace NetWiv.Cli;

/// <summary>
/// The NetWiv guides shipped with this CLI, split into heading sections for search. They come
/// from the same source revision as the CLI, so answers match the version a project uses.
/// </summary>
internal sealed class McpDocs
{
    internal sealed record Section(string Document, string Heading, int Level, string Body);

    private readonly Dictionary<string, string> _documents;
    private readonly List<Section> _sections;

    private McpDocs(Dictionary<string, string> documents)
    {
        _documents = documents;
        _sections = documents.OrderBy(static pair => pair.Key, StringComparer.Ordinal).SelectMany(static pair => Split(pair.Key, pair.Value)).ToList();
    }

    public IReadOnlyCollection<string> Names => _documents.Keys;

    public static McpDocs Load(string directory)
    {
        var documents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
                documents[Path.GetFileNameWithoutExtension(path)] = File.ReadAllText(path);
        return new McpDocs(documents);
    }

    internal static McpDocs FromText(IReadOnlyDictionary<string, string> documents)
        => new(new Dictionary<string, string>(documents, StringComparer.OrdinalIgnoreCase));

    /// <summary>Ranks sections by how often the query words appear, headings weighted above body text.</summary>
    public IReadOnlyList<(Section Section, string Snippet)> Search(string query, int limit)
    {
        var terms = Terms(query);
        if (terms.Length == 0) return [];
        return _sections
            .Select(section =>
            {
                var heading = section.Heading.ToLowerInvariant();
                var body = section.Body.ToLowerInvariant();
                var matched = terms.Count(term => heading.Contains(term, StringComparison.Ordinal) || body.Contains(term, StringComparison.Ordinal));
                var score = terms.Sum(term => Count(heading, term) * 5 + Math.Min(Count(body, term), 10));
                // Sections that contain every word rank above those that repeat one word.
                return (section, score: matched == 0 ? 0 : score + (matched == terms.Length ? 100 : matched * 10));
            })
            .Where(static item => item.score > 0)
            .OrderByDescending(static item => item.score)
            .ThenBy(static item => item.section.Document, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => (item.section, Snippet(item.section.Body, terms)))
            .ToArray();
    }

    /// <summary>Returns a whole guide, or the section whose heading matches, with its subsections.</summary>
    public string? Read(string name, string? heading, out string? error)
    {
        error = null;
        var key = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        if (!_documents.TryGetValue(key, out var text))
        {
            error = $"No guide named '{name}'. Available: {string.Join(", ", _documents.Keys.Order(StringComparer.Ordinal))}.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(heading)) return text;
        var sections = _sections.Where(section => section.Document.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        var start = sections.FindIndex(section => section.Heading.Equals(heading.Trim().TrimStart('#').Trim(), StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            error = $"No section '{heading}' in {key}. Sections: {string.Join("; ", sections.Where(static s => s.Level > 0).Select(static s => s.Heading))}.";
            return null;
        }
        var builder = new StringBuilder();
        for (var i = start; i < sections.Count && (i == start || sections[i].Level > sections[start].Level); i++)
        {
            if (sections[i].Level > 0) builder.Append('#', sections[i].Level).Append(' ').Append(sections[i].Heading).Append('\n');
            builder.Append(sections[i].Body).Append('\n');
        }
        return builder.ToString().TrimEnd() + "\n";
    }

    private static IEnumerable<Section> Split(string document, string text)
    {
        var heading = "";
        var level = 0;
        var body = new StringBuilder();
        var fenced = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            var hashes = fenced ? 0 : raw.TakeWhile(static c => c == '#').Count();
            if (hashes is >= 1 and <= 6 && raw.Length > hashes && raw[hashes] == ' ')
            {
                if (level > 0 || body.ToString().Trim().Length > 0) yield return new Section(document, heading, level, body.ToString().Trim());
                heading = raw[(hashes + 1)..].Trim();
                level = hashes;
                body.Clear();
                continue;
            }
            body.Append(raw).Append('\n');
        }
        if (level > 0 || body.ToString().Trim().Length > 0) yield return new Section(document, heading, level, body.ToString().Trim());
    }

    private static string[] Terms(string query)
        => query.ToLowerInvariant()
            .Split([' ', '\t', '\n', ',', '.', ';', ':', '?', '!', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '`'], StringSplitOptions.RemoveEmptyEntries)
            .Where(static term => term.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static int Count(string text, string term)
    {
        var count = 0;
        for (var index = text.IndexOf(term, StringComparison.Ordinal); index >= 0; index = text.IndexOf(term, index + term.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string Snippet(string body, string[] terms)
    {
        var flat = string.Join(' ', body.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(static line => line.Trim()));
        var lower = flat.ToLowerInvariant();
        var at = terms.Select(term => lower.IndexOf(term, StringComparison.Ordinal)).Where(static index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, at - 80);
        var length = Math.Min(280, flat.Length - start);
        return (start > 0 ? "…" : "") + flat.Substring(start, length) + (start + length < flat.Length ? "…" : "");
    }
}
