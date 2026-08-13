using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public static class HotkeyContextCatalog
{
    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] sources)
        => sources
            .SelectMany(source => source)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> ReadBaseFile(string? path)
        => ReadFile(path, expectedRoot: "unitcontexts");

    public static IReadOnlyList<string> ReadModFile(string? path)
        => ReadFile(path, expectedRoot: "unitcontextsmods");

    public static IReadOnlyList<string> ExtractContextValues(XContainer container)
        => container
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("context", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> ExtractBaseContextsFromBar(BarArchive? barFile, string? barPath)
    {
        if (barFile?.Entries == null || string.IsNullOrWhiteSpace(barPath) || !File.Exists(barPath))
            return [];

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = barFile.Entries.Where(entry =>
        {
            var fileName = entry.Name.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
            return fileName.Equals("unit_contexts.xml.xmb", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("unit_contexts.xmb", StringComparison.OrdinalIgnoreCase);
        });

        using var stream = File.OpenRead(barPath);
        foreach (var entry in entries)
        {
            try
            {
                var size = entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive;
                var bytes = new byte[size];
                var read = entry.ReadDataDecompressed(stream, bytes);
                if (read <= 0)
                    continue;

                var xml = XmbReader.ToFormattedXml(bytes.AsSpan(0, read));
                if (string.IsNullOrWhiteSpace(xml))
                    continue;

                foreach (var value in ExtractContextValues(XDocument.Parse(xml, LoadOptions.PreserveWhitespace)))
                    names.Add(value);
            }
            catch
            {
                // Ignore a malformed matching BAR entry and retain contexts from other sources.
            }
        }

        return names.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ReadFile(string? path, string expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            if (document.Root == null ||
                !document.Root.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return ExtractContextValues(document);
        }
        catch
        {
            return [];
        }
    }
}
