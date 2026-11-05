using System.Text;
using System.Xml;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

internal static class AssetPayloadReader
{
    internal static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or
            NotSupportedException or NotImplementedException or FormatException or InvalidOperationException or OverflowException or XmlException;

    internal static byte[] ReadStream(Stream stream, int maximum)
    {
        if (stream.CanSeek) AssetPayloadPolicy.RequireBytes(stream.Length - stream.Position, maximum, "Asset input");
        using var output = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(chunk, 0, chunk.Length)) != 0)
        {
            AssetPayloadPolicy.RequireBytes(output.Length + count, maximum, "Actual asset input");
            output.Write(chunk, 0, count);
        }
        return output.ToArray();
    }

    internal static byte[] ReadEntry(Stream stream, BarArchiveEntry entry, int maximum)
    {
        AssetPayloadPolicy.RequireBytes(entry.SizeInArchive, maximum, "Stored BAR payload");
        AssetPayloadPolicy.RequireBytes(entry.SizeUncompressed, maximum, "Expanded BAR payload");
        if (!stream.CanSeek || entry.ContentOffset < 0 || entry.ContentOffset > stream.Length ||
            entry.SizeInArchive > stream.Length - entry.ContentOffset)
            throw new InvalidDataException("BAR payload is truncated or its offset is invalid.");
        // BAR decompression validates its own advertised output against this
        // admitted destination before decoding, including ALZ4/L33T headers.
        var bytes = new byte[entry.IsCompressed ? entry.SizeUncompressed : entry.SizeInArchive];
        var count = entry.ReadDataDecompressed(stream, bytes);
        AssetPayloadPolicy.RequireBytes(count, maximum, "Actual BAR output");
        return count == bytes.Length ? bytes : bytes[..count];
    }

    internal static string ReadXml(byte[] bytes, bool compiled, AssetPayloadPolicy policy)
    {
        AssetPayloadPolicy.RequireBytes(bytes.Length, policy.MaximumXmlBytes, "XML input");
        string xml;
        if (compiled)
            xml = XmbReader.ToFormattedXml(bytes, policy.MaximumXmlCharacters)
                ?? throw new InvalidDataException($"Compiled XML is invalid or exceeds the {policy.MaximumXmlCharacters:N0}-character preview limit.");
        else
        {
            using var memory = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(memory, detectEncodingFromByteOrderMarks: true);
            var output = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (count > policy.MaximumXmlCharacters - output.Length)
                    throw new AssetPayloadLimitException($"XML exceeds the {policy.MaximumXmlCharacters:N0}-character preview limit.");
                output.Append(buffer, 0, count);
            }
            xml = output.ToString();
        }
        return ValidateXmlText(xml, policy, bytes.Length);
    }

    internal static string ValidateXmlText(string xml, AssetPayloadPolicy policy, int sourceBytes)
    {
        if (xml.Length > policy.MaximumXmlCharacters)
            throw new AssetPayloadLimitException("XML exceeds the preview character limit.");
        using var text = new StringReader(xml);
        using var validator = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = policy.MaximumXmlCharacters
        });
        long nodes = 0;
        long formattedCharacters = xml.Length;
        var hasChildren = policy.EnforceViewerLimits ? new bool[policy.MaximumXmlDepth + 1] : null;
        var referenceBudget = policy.EnforceViewerLimits ? null : new ReferenceReadBudget();
        referenceBudget?.AdmitSource("Animation XML validation", sourceBytes);
        while (validator.Read())
        {
            nodes += 1L + validator.AttributeCount;
            if (nodes > policy.MaximumXmlNodes || validator.Depth > policy.MaximumXmlDepth)
                throw new AssetPayloadLimitException("XML structure exceeds the preview node/depth limit.");
            if (!policy.EnforceViewerLimits)
            {
                referenceBudget!.ChargeNode(validator, "Animation XML validation");
                continue;
            }
            // The existing viewer formats recursively. Admit its indentation too,
            // before it allocates a DOM and a potentially much larger string.
            if (validator.NodeType == XmlNodeType.Element)
            {
                hasChildren![validator.Depth] = false;
                if (validator.Depth > 0)
                {
                    hasChildren[validator.Depth - 1] = true;
                    formattedCharacters += Environment.NewLine.Length + validator.Depth;
                }
                if (validator.IsEmptyElement) formattedCharacters++;
            }
            else if (validator.NodeType == XmlNodeType.EndElement && hasChildren![validator.Depth])
                formattedCharacters += Environment.NewLine.Length + validator.Depth;
            if (formattedCharacters > policy.MaximumXmlCharacters)
                throw new AssetPayloadLimitException("Formatted XML exceeds the preview character limit.");
        }
        if (string.IsNullOrWhiteSpace(xml)) throw new InvalidDataException("XML payload is empty.");
        return xml;
    }

    internal static async Task<AssetPayloadResult<string>> LoadLooseXmlAsync(string path, AssetPayloadPolicy? policy = null)
    {
        policy ??= AssetPayloadPolicy.Default;
        try
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return AssetPayloadResult<string>.Success(ReadXml(ReadStream(stream, policy.MaximumXmlBytes), false, policy), path);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        { return AssetPayloadResult<string>.Failure(path, exception.Message); }
    }

    internal static async Task<AssetPayloadResult<string>> LoadArchiveXmlAsync(string path, string gamePath, AssetPayloadPolicy? policy = null)
    {
        policy ??= AssetPayloadPolicy.Default;
        var source = $"{path} :: {gamePath}";
        try
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var archive = new BarArchive(stream);
                if (!archive.Load(out var error) || archive.Entries == null)
                    throw new InvalidDataException($"Archive metadata is unavailable: {error}.");
                var entry = archive.Entries.FirstOrDefault(candidate =>
                    AnimFileCatalog.TryGetGamePath(candidate.RelativePath, out var candidatePath) &&
                    candidatePath.Equals(gamePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new FileNotFoundException("XML payload was not found in the archive.");
                var xml = ReadXml(ReadEntry(stream, entry, policy.MaximumXmlBytes),
                    entry.RelativePath.EndsWith(".XMB", StringComparison.OrdinalIgnoreCase), policy);
                return AssetPayloadResult<string>.Success(xml, source);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        { return AssetPayloadResult<string>.Failure(source, exception.Message); }
    }
}
