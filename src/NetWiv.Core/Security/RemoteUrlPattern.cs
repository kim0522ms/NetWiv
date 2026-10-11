// Adapted from urlpattern 0.3.0 (MIT), Copyright 2018-2021 the Deno authors,
// and tauri-utils 2.9.3 (MIT OR Apache-2.0), Copyright 2019-2026 Tauri Programme.
// SPDX-License-Identifier: MIT AND (Apache-2.0 OR MIT)
using System.Globalization;
using System.Net;
using System.Text;

namespace NetWiv;

internal sealed class RemoteUrlPattern
{
    private enum Component { Init, Protocol, Authority, Username, Password, Hostname, Port, Pathname, Search, Hash, Done }
    private static readonly string[] SpecialSchemes = ["ftp", "file", "http", "https", "ws", "wss"];
    private readonly Dictionary<Component, UrlPatternComponent> _components = [];

    internal RemoteUrlPattern(string pattern)
    {
        var values = ParseConstructor(pattern);
        if (!values.ContainsKey(Component.Protocol)) throw new FormatException("Remote URL pattern requires an absolute protocol.");
        foreach (var component in new[] { Component.Search, Component.Hash, Component.Pathname })
            if (!values.TryGetValue(component, out var value) || value.Length == 0 || component == Component.Pathname && value == "/") values[component] = "*";
        if (values.TryGetValue(Component.Port, out var port) && port == DefaultPort(values[Component.Protocol])) values[Component.Port] = "";
        var protocol = new UrlPatternComponent(values[Component.Protocol], CanonicalProtocol);
        _components[Component.Protocol] = protocol;
        var special = SpecialSchemes.Any(protocol.Matches);
        var ipv6 = values.GetValueOrDefault(Component.Hostname, "").StartsWith('[')
            || values.GetValueOrDefault(Component.Hostname, "").StartsWith("\\[", StringComparison.Ordinal)
            || values.GetValueOrDefault(Component.Hostname, "").StartsWith("{[", StringComparison.Ordinal);
        foreach (var component in new[] { Component.Username, Component.Password, Component.Hostname, Component.Port, Component.Pathname, Component.Search, Component.Hash })
        {
            var value = values.GetValueOrDefault(component);
            if (component == Component.Search && value?.StartsWith('?') == true) value = value[1..];
            if (component == Component.Hash && value?.StartsWith('#') == true) value = value[1..];
            var delimiter = component == Component.Hostname ? '.' : component == Component.Pathname && special ? '/' : (char?)null;
            _components[component] = new(value, text => Canonical(text, component, special, ipv6), delimiter, component == Component.Pathname && special ? "/" : "");
        }
    }

    internal bool Matches(Uri source) => source.IsAbsoluteUri && Matches(source.OriginalString);

    internal bool Matches(string absoluteUrl)
    {
        // Uri.AbsolutePath decodes unreserved escapes (e.g. %61), unlike rust-url.
        // Native source URLs are absolute; retain their original escaped components.
        var raw = absoluteUrl.Replace("\t", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Trim();
        var scheme = raw[..raw.IndexOf(':')].ToLowerInvariant();
        var rest = raw[(raw.IndexOf(':') + 1)..];
        var hashAt = rest.IndexOf('#');
        var hash = hashAt < 0 ? "" : rest[(hashAt + 1)..];
        if (hashAt >= 0) rest = rest[..hashAt];
        var searchAt = rest.IndexOf('?');
        var search = searchAt < 0 ? "" : rest[(searchAt + 1)..];
        if (searchAt >= 0) rest = rest[..searchAt];
        var special = SpecialSchemes.Contains(scheme);
        if (special) rest = rest.Replace('\\', '/');
        var username = "";
        var password = "";
        var hostname = "";
        var port = "";
        if (rest.StartsWith("//", StringComparison.Ordinal) || special && scheme != "file")
        {
            rest = special && scheme != "file" ? rest.TrimStart('/') : rest[2..];
            var slash = rest.IndexOf('/');
            var authority = slash < 0 ? rest : rest[..slash];
            rest = slash < 0 ? special ? "/" : "" : rest[slash..];
            var at = authority.LastIndexOf('@');
            if (at >= 0)
            {
                var info = authority[..at].Split(':', 2);
                username = Encode(info[0], Component.Username, special);
                password = info.Length == 2 ? Encode(info[1], Component.Password, special) : "";
                authority = authority[(at + 1)..];
            }
            var colon = authority.LastIndexOf(':');
            if (colon > authority.LastIndexOf(']'))
            {
                var rawPort = authority[(colon + 1)..];
                port = rawPort == "" ? "" : Canonical(rawPort, Component.Port, special, false);
                authority = authority[..colon];
            }
            hostname = authority.StartsWith('[') ? CanonicalIpv6Address(authority) : special ? Canonical(authority, Component.Hostname, true, false) : Encode(authority, Component.Pathname, false);
            if (port == DefaultPort(scheme)) port = "";
        }
        var path = special ? Canonical(rest, Component.Pathname, true, false) : Encode(rest, Component.Pathname, false);
        return Match(Component.Protocol, scheme)
            && Match(Component.Username, username) && Match(Component.Password, password)
            && Match(Component.Hostname, hostname) && Match(Component.Port, port)
            && Match(Component.Pathname, path)
            && Match(Component.Search, Encode(search, Component.Search, special))
            && Match(Component.Hash, Encode(hash, Component.Hash, special));
    }

    private bool Match(Component component, string value) => _components[component].Matches(value);

    private static Dictionary<Component, string> ParseConstructor(string input)
    {
        var tokens = UrlPatternTokens.Parse(input, strict: false);
        var values = new Dictionary<Component, string>();
        var state = Component.Init;
        var start = 0;
        var index = 0;
        var increment = 1;
        var depth = 0;
        var ipv6Depth = 0;
        var special = false;
        while (index < tokens.Count)
        {
            increment = 1;
            if (tokens[index].Kind == UrlTokenKind.End)
            {
                if (state == Component.Init) throw new FormatException("Remote URL pattern requires an absolute protocol.");
                if (state == Component.Authority) { Rewind(Component.Hostname); continue; }
                Change(Component.Done, 0);
                break;
            }
            if (tokens[index].Kind == UrlTokenKind.Open) { depth++; index++; continue; }
            if (depth > 0)
            {
                if (tokens[index].Kind == UrlTokenKind.Close) depth--;
                else { index++; continue; }
            }
            switch (state)
            {
                case Component.Init:
                    if (Is(index, ":")) Rewind(Component.Protocol);
                    break;
                case Component.Protocol:
                    if (Is(index, ":"))
                    {
                        var protocol = new UrlPatternComponent(Slice(), CanonicalProtocol);
                        special = SpecialSchemes.Any(protocol.Matches);
                        var slashes = Is(index + 1, "/") && Is(index + 2, "/");
                        Change(slashes || special ? Component.Authority : Component.Pathname, slashes ? 3 : 1);
                    }
                    break;
                case Component.Authority:
                    if (Is(index, "@")) Rewind(Component.Username);
                    else if (Is(index, "/") || Search() || Is(index, "#")) Rewind(Component.Hostname);
                    break;
                case Component.Username:
                    if (Is(index, ":")) Change(Component.Password, 1);
                    else if (Is(index, "@")) Change(Component.Hostname, 1);
                    break;
                case Component.Password:
                    if (Is(index, "@")) Change(Component.Hostname, 1);
                    break;
                case Component.Hostname:
                    if (Is(index, "[")) ipv6Depth++;
                    else if (Is(index, "]")) ipv6Depth--;
                    else if (Is(index, ":") && ipv6Depth == 0) Change(Component.Port, 1);
                    else if (Is(index, "/")) Change(Component.Pathname, 0);
                    else if (Search()) Change(Component.Search, 1);
                    else if (Is(index, "#")) Change(Component.Hash, 1);
                    break;
                case Component.Port:
                    if (Is(index, "/")) Change(Component.Pathname, 0);
                    else if (Search()) Change(Component.Search, 1);
                    else if (Is(index, "#")) Change(Component.Hash, 1);
                    break;
                case Component.Pathname:
                    if (Search()) Change(Component.Search, 1);
                    else if (Is(index, "#")) Change(Component.Hash, 1);
                    break;
                case Component.Search:
                    if (Is(index, "#")) Change(Component.Hash, 1);
                    break;
            }
            index += increment;
        }
        if (values.ContainsKey(Component.Hostname)) values.TryAdd(Component.Port, "");
        return values;

        bool Is(int at, string value) => at < tokens.Count && tokens[at].Value == value && tokens[at].Kind is UrlTokenKind.Char or UrlTokenKind.Escaped or UrlTokenKind.Invalid;
        bool Search() => Is(index, "?") || tokens[index].Value == "?" && (index == 0 || tokens[index - 1].Kind is not (UrlTokenKind.Name or UrlTokenKind.Regex or UrlTokenKind.Close or UrlTokenKind.Star));
        string Slice() => input[tokens[start].Index..tokens[index].Index];
        void Rewind(Component next) { index = start; increment = 0; state = next; }
        void Change(Component next, int skip)
        {
            if (state is not (Component.Init or Component.Authority or Component.Done)) values[state] = Slice();
            if (state != Component.Init && next != Component.Done)
            {
                if (state <= Component.Password && next is Component.Port or Component.Pathname or Component.Search or Component.Hash) values.TryAdd(Component.Hostname, "");
                if (state <= Component.Port && next is Component.Search or Component.Hash) values.TryAdd(Component.Pathname, special ? "/" : "");
                if (state <= Component.Pathname && next == Component.Hash) values.TryAdd(Component.Search, "");
            }
            state = next;
            index += skip;
            start = index;
            increment = 0;
        }
    }

    private static string CanonicalProtocol(string value)
    {
        if (value == "") return value;
        if (!char.IsAsciiLetter(value[0]) || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.')))
            throw new FormatException("Invalid URL pattern protocol.");
        return value.ToLowerInvariant();
    }

    private static string Canonical(string value, Component component, bool special, bool ipv6)
    {
        if (value == "") return value;
        if (component == Component.Hostname)
        {
            if (ipv6)
            {
                if (value.Any(c => !char.IsAsciiHexDigit(c) && c is not ('[' or ']' or ':'))) throw new FormatException("Invalid IPv6 URL pattern.");
                return value.ToLowerInvariant();
            }
            value = Uri.UnescapeDataString(value);
            if (value.Any(c => c <= 32 || c is '#' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '|' or '%')) throw new FormatException("Invalid URL pattern hostname.");
            var ascii = string.Join('.', value.Split('.').Select(label => label.Length == 0 ? "" : new IdnMapping().GetAscii(label))).ToLowerInvariant();
            if (Uri.TryCreate("http://" + ascii + "/", UriKind.Absolute, out var address) && address.HostNameType == UriHostNameType.IPv4) return address.Host;
            return ascii;
        }
        if (component == Component.Port)
        {
            if (!ushort.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var port)) throw new FormatException("Invalid URL pattern port.");
            return port.ToString(CultureInfo.InvariantCulture);
        }
        if (component == Component.Pathname && special)
        {
            var leading = value.StartsWith('/');
            var encoded = Encode(leading ? value : "/-" + value, component, special).Replace('\\', '/');
            var segments = encoded.Split('/');
            var normalized = new List<string>();
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var dots = segment.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase);
                if (dots is "." or "..")
                {
                    if (dots == ".." && normalized.Count > 1) normalized.RemoveAt(normalized.Count - 1);
                    if (i == segments.Length - 1) normalized.Add("");
                }
                else normalized.Add(segment);
            }
            var path = string.Join('/', normalized);
            return leading ? path : path.Length >= 2 ? path[2..] : "";
        }
        if (component == Component.Pathname && !special && value.StartsWith('/'))
            return "%2F" + Encode(value[1..], component, special);
        return Encode(value, component, special);
    }

    private static string Encode(string value, Component component, bool special)
    {
        var result = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var c = rune.Value;
            var escape = c < 32 || c > 126;
            if (component is Component.Username or Component.Password)
                escape |= c is ' ' or '"' or '#' or '<' or '>' or '?' or '`' or '{' or '}' or '/' or ':' or ';' or '=' or '@' or '[' or '\\' or ']' or '^' or '|';
            else if (component == Component.Pathname && special)
                escape |= c is ' ' or '"' or '#' or '<' or '>' or '?' or '`' or '{' or '}';
            else if (component == Component.Search)
                escape |= c is ' ' or '"' or '#' or '<' or '>' || special && c == '\'';
            else if (component == Component.Hash)
                escape |= c is ' ' or '"' or '<' or '>' or '`';
            if (!escape) result.Append(rune.ToString());
            else foreach (var b in Encoding.UTF8.GetBytes(rune.ToString())) result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    private static string? DefaultPort(string protocol) => protocol switch
    {
        "http" or "ws" => "80", "https" or "wss" => "443", "ftp" => "21", _ => null
    };

    private static string CanonicalIpv6Address(string hostname)
    {
        var bytes = IPAddress.Parse(hostname[1..^1]).GetAddressBytes();
        var groups = Enumerable.Range(0, 8).Select(i => (bytes[i * 2] << 8) | bytes[i * 2 + 1]).ToArray();
        var longestStart = -1;
        var longestLength = 1;
        for (var i = 0; i < groups.Length; i++)
        {
            if (groups[i] != 0) continue;
            var start = i;
            while (i + 1 < groups.Length && groups[i + 1] == 0) i++;
            if (i - start + 1 > longestLength) { longestStart = start; longestLength = i - start + 1; }
        }
        if (longestStart < 0) return "[" + string.Join(':', groups.Select(g => g.ToString("x", CultureInfo.InvariantCulture))) + "]";
        return "[" + string.Join(':', groups.Take(longestStart).Select(g => g.ToString("x", CultureInfo.InvariantCulture)))
            + "::" + string.Join(':', groups.Skip(longestStart + longestLength).Select(g => g.ToString("x", CultureInfo.InvariantCulture))) + "]";
    }
}
