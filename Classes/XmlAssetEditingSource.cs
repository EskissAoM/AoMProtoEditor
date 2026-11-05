using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

internal sealed class XmlAssetSourceChangedException(string message) : IOException(message);

/// <summary>One existing loose XML source; never converts archives or recreates a moved/deleted file.</summary>
internal sealed class XmlAssetEditingSource
{
    private readonly string _directory;
    private readonly string _relativePath;
    private PreservedTextEncoding _encoded;
    private FileIntegrityTransaction.FileState _expected;
    private readonly XName _rootName;
    private readonly string _newLine;
    internal string Path { get; }
    internal string Text => _encoded.Text;
    internal string? ReadOnlyReason { get; }
    internal bool CanEdit => ReadOnlyReason == null;

    private XmlAssetEditingSource(string directory, string relativePath)
    {
        _directory = directory;
        _relativePath = relativePath;
        Path = ResolvePath(directory, relativePath);
        var bytes = ReadBytes(Path);
        _expected = State(bytes);
        _encoded = PreservedTextEncoding.Decode(bytes);
        _rootName = ValidateText(_encoded.Text, _encoded).Root!.Name;
        _newLine = _encoded.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : _encoded.Text.Contains('\r') ? "\r" : "\n";
        ReadOnlyReason = CompiledCounterpart() is { } compiled
            ? $"XML and compiled files share this asset name. Resolve '{compiled}' before editing this XML file."
            : (File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0 ? "The XML source file is read-only." : null;
    }

    internal static Task<XmlAssetEditingSource> OpenAsync(string directory, string relativePath)
        => Task.Run(() => new XmlAssetEditingSource(directory, relativePath));

    internal Task<XmlAssetEditingSource> ReloadAsync() => OpenAsync(_directory, _relativePath);

    private static string ResolvePath(string directory, string relativePath)
    {
        var relative = relativePath.Replace('/', '\\');
        if (System.IO.Path.IsPathRooted(relative) || !relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            !AssetDestinationPolicy.TryResolve(directory, System.IO.Path.GetDirectoryName(relative) ?? "",
                System.IO.Path.GetFileName(relative), ".xml", "", out var destination, out _))
            throw new IOException("The XML source must stay inside the current mod asset folder.");
        return destination!.AbsolutePath;
    }

    private static byte[] ReadBytes(string path)
    {
        FileIntegrityTransaction.EnsureRegularFileOrMissing(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return AssetPayloadReader.ReadStream(stream, AssetPayloadPolicy.Default.MaximumXmlBytes);
    }

    private static FileIntegrityTransaction.FileState State(byte[] bytes)
        => new(true, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));

    private string? CompiledCounterpart()
    {
        foreach (var candidate in new[] { Path + ".xmb", System.IO.Path.ChangeExtension(Path, ".xmb") })
        {
            FileIntegrityTransaction.EnsureRegularFileOrMissing(candidate);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static XDocument ValidateText(string text, PreservedTextEncoding encoding)
    {
        var policy = AssetPayloadPolicy.Default;
        if (text.Length > policy.MaximumXmlCharacters) throw new InvalidDataException("XML exceeds the editing character limit.");
        var sourceBytes = checked(encoding.Encoding.GetByteCount(text) + encoding.Preamble.Length);
        AssetPayloadPolicy.RequireBytes(sourceBytes, policy.MaximumXmlBytes, "XML input");
        AssetPayloadReader.ValidateXmlText(text, policy, sourceBytes);
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        if (document.Root == null) throw new InvalidDataException("The XML document must have one root element.");
        if (document.Declaration?.Encoding is { Length: > 0 } declared)
        {
            var codePage = Encoding.GetEncoding(declared).CodePage;
            var actual = encoding.Encoding.CodePage;
            if (codePage != actual && !(declared.Equals("utf-16", StringComparison.OrdinalIgnoreCase) && actual is 1200 or 1201) &&
                !(declared.Equals("utf-32", StringComparison.OrdinalIgnoreCase) && actual is 12000 or 12001))
                throw new InvalidDataException($"Keep the XML declaration encoding compatible with the source file ({encoding.Encoding.WebName}).");
        }
        return document;
    }

    internal Task<string> SaveAsync(string text) => Task.Run(() => Save(text));

    private string Save(string text)
    {
        if (!CanEdit) throw new InvalidOperationException(ReadOnlyReason);
        var document = ValidateText(text, _encoded);
        if (document.Root!.Name != _rootName) throw new InvalidDataException($"Keep the source XML root <{_rootName}>.");
        var savedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", _newLine, StringComparison.Ordinal);
        var bytes = _encoded.Encode(savedText);
        AssetPayloadPolicy.RequireBytes(bytes.LongLength, AssetPayloadPolicy.Default.MaximumXmlBytes, "XML output");
        RequireUnchanged();
        var expected = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase)
        {
            [Path] = _expected,
            [Path + ".xmb"] = new(false, 0, ""),
            [System.IO.Path.ChangeExtension(Path, ".xmb")] = new(false, 0, "")
        };
        var temporary = Path + ".tmp." + Guid.NewGuid().ToString("N");
        var ownsTemporary = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            RequireUnchanged();
            // All checks precede the transaction's first write. A preflight
            // conflict must not roll back somebody else's external changes.
            FileIntegrityTransaction.Execute([Path], expected, () => File.Move(temporary, Path, overwrite: true));
        }
        finally
        {
            if (ownsTemporary)
            {
                try
                {
                    FileIntegrityTransaction.EnsureRegularFileOrMissing(temporary);
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        _encoded = _encoded with { Text = savedText };
        _expected = State(bytes);
        return savedText;
    }

    private void RequireUnchanged()
    {
        if (!ResolvePath(_directory, _relativePath).Equals(Path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The XML source path changed. No files were written.");
        if (CompiledCounterpart() != null)
            throw new XmlAssetSourceChangedException("A compiled counterpart appeared after opening this XML file. Reload to review the now read-only source.");
        if (!File.Exists(Path) || State(ReadBytes(Path)) != _expected)
            throw new XmlAssetSourceChangedException("The XML file changed, moved or was deleted after this window opened. No files were written.");
        if ((File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0)
            throw new IOException("The XML source file is now read-only. No files were written.");
    }
}
