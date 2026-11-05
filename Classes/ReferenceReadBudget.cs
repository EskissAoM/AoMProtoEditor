using System.Xml;
using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal sealed class ReferenceResourceLimitException(string message) : InvalidOperationException(message) { }

// One operation owns one budget. The estimate is deliberately conservative, not
// an assertion about exact CLR heap usage; all parsed documents count, including
// unchanged ones, so retained documents cannot escape the same aggregate bound.
internal sealed class ReferenceReadBudget(
    long maximumSourceBytes = 2L * 1024 * 1024 * 1024,
    long maximumXmlCharacters = 512L * 1024 * 1024,
    long maximumNodes = 4_000_000,
    long maximumEstimatedDomBytes = 1024L * 1024 * 1024)
{
    private readonly Dictionary<string, long> _sources = new(StringComparer.OrdinalIgnoreCase);
    private long _sourceBytes, _characters, _nodes;
    internal long RemainingCharacters => Math.Max(0, maximumXmlCharacters - _characters);

    internal void AdmitFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists) AdmitSource(path, info.Length);
    }

    internal void AdmitSource(string path, long bytes)
    {
        if (bytes > ModReferenceDocumentReader.MaximumFileBytes)
            throw new InvalidOperationException(
                $"Reference inspection refused '{path}': per-file bytes exceed 256 MiB. No files were written.");
        var fullPath = Path.GetFullPath(path);
        var previous = _sources.GetValueOrDefault(fullPath);
        var total = checked(_sourceBytes + Math.Max(0, bytes - previous));
        if (total > maximumSourceBytes) Refuse(path, "aggregate source bytes", maximumSourceBytes);
        if (!_sources.ContainsKey(fullPath) && _sources.Count >= 20_000)
            Refuse(path, "reference file count", 20_000);
        _sourceBytes = total;
        _sources[fullPath] = Math.Max(bytes, previous);
    }

    internal void ChargeNode(XmlReader reader, string source)
    {
        long characters = reader.Name.Length + (long)reader.Value.Length;
        var nodes = 1L + reader.AttributeCount;
        if (reader.MoveToFirstAttribute())
        {
            do { characters = checked(characters + reader.Name.Length + (long)reader.Value.Length); }
            while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        var totalCharacters = checked(_characters + characters);
        var totalNodes = checked(_nodes + nodes);
        if (totalCharacters > maximumXmlCharacters) Refuse(source, "aggregate XML characters", maximumXmlCharacters);
        if (totalNodes > maximumNodes) Refuse(source, "aggregate XML nodes/attributes", maximumNodes);
        if (checked(totalCharacters * 2 + totalNodes * 128) > maximumEstimatedDomBytes)
            Refuse(source, "aggregate estimated document bytes", maximumEstimatedDomBytes);
        _characters = totalCharacters;
        _nodes = totalNodes;
    }

    internal void InspectExisting(XContainer document, string source)
    {
        using var reader = document.CreateReader();
        while (reader.Read()) ChargeNode(reader, source);
    }

    private static void Refuse(string source, string resource, long limit)
        => throw new ReferenceResourceLimitException(
            $"Reference inspection refused '{source}': {resource} limit ({limit:N0}) exceeded. " +
            "Reduce or split the reference data and retry. No files were written.");
}
