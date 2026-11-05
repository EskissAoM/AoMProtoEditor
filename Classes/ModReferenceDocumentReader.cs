using System.Xml;
using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>One bounded reader for loose XML and read-only compiled tactics references.</summary>
internal static class ModReferenceDocumentReader
{
    internal const long MaximumFileBytes = 256L * 1024 * 1024;

    public static bool IsCompiledTactics(string path)
        => path.EndsWith(".tactics.xmb", StringComparison.OrdinalIgnoreCase) ||
           (Path.GetExtension(path).Equals(".xmb", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .SkipLast(1).Any(part => part.Equals("tactics", StringComparison.OrdinalIgnoreCase)));

    public static InvalidOperationException CannotRewrite(string path)
        => new($"The rename requires changing references in compiled tactics '{path}'. " +
               "Convert this file to editable XML tactics and remove the compiled copy before retrying. No files were written.");

    public static XDocument Load(string path, ReferenceReadBudget? budget = null)
    {
        budget ??= new ReferenceReadBudget();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFileBytes * 2,
            IgnoreComments = false,
            IgnoreWhitespace = false
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.SequentialScan);
        if (stream.Length > MaximumFileBytes)
            throw new InvalidOperationException($"Reference inspection refused '{path}': file exceeds 256 MiB. No files were written.");
        budget.AdmitSource(path, stream.Length);
        if (!IsCompiledTactics(path))
        {
            using var reader = XmlReader.Create(stream, settings);
            using var counted = new BudgetedReferenceXmlReader(reader, budget, path);
            return XDocument.Load(counted, LoadOptions.PreserveWhitespace);
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        var xml = XmbReader.ToFormattedXml(bytes, checked((int)Math.Max(1, Math.Min(MaximumFileBytes * 2, budget.RemainingCharacters))))
            ?? throw new InvalidOperationException($"Compiled tactics '{path}' could not be decoded safely. No files were written.");
        using var text = new StringReader(xml);
        using var compiledReader = XmlReader.Create(text, settings);
        using var countedCompiled = new BudgetedReferenceXmlReader(compiledReader, budget, path);
        var document = XDocument.Load(countedCompiled, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "tactics")
            throw new InvalidOperationException($"Compiled tactics '{path}' does not contain a tactics root. No files were written.");
        return document;
    }
}
