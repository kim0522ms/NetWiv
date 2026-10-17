using System.Xml;
using System.Xml.Linq;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static class MacPropertyList
{
    internal static XDocument Load(string path) => Parse(File.ReadAllText(path));

    internal static XDocument Parse(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024
            });
            var document = XDocument.Load(reader);
            if (document.Root?.Name != "plist" || document.Root.Elements().Count() != 1 || document.Root.Element("dict") is not { } dictionary)
                throw new NetWivConfigException("A macOS property list must contain a single plist dictionary (XML format).");
            Validate(dictionary);
            return document;
        }
        catch (XmlException)
        {
            throw new NetWivConfigException("Invalid macOS XML property list.");
        }
    }

    private static void Validate(XElement value)
    {
        if (value.Name == "dict")
        {
            var entries = value.Elements().ToArray();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (entries.Length % 2 != 0) throw new NetWivConfigException("Property-list dictionaries require key/value pairs.");
            for (int i = 0; i < entries.Length; i += 2)
            {
                if (entries[i].Name != "key" || entries[i].HasElements || !keys.Add(entries[i].Value))
                    throw new NetWivConfigException("Property-list dictionary keys must be unique.");
                Validate(entries[i + 1]);
            }
        }
        else if (value.Name == "array")
        {
            foreach (var child in value.Elements()) Validate(child);
        }
        else if (value.Name.Namespace != XNamespace.None || value.Name.LocalName is not ("string" or "integer" or "real" or "data" or "date" or "true" or "false") || value.HasElements)
            throw new NetWivConfigException("Unsupported property-list value.");
        else if (value.Name.LocalName is "true" or "false" && !string.IsNullOrWhiteSpace(value.Value))
            throw new NetWivConfigException("Property-list booleans must be empty elements.");
    }

    internal static XElement? Get(XElement dictionary, string key) => dictionary.Elements("key")
        .SingleOrDefault(element => element.Value == key)?.ElementsAfterSelf().First();

    internal static void Set(XElement dictionary, string key, XElement value)
    {
        if (Get(dictionary, key) is { } existing) existing.ReplaceWith(new XElement(value));
        else dictionary.Add(new XElement("key", key), new XElement(value));
    }
}
