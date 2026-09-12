using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>Atomic XML output that retains the destination encoding, BOM, newlines, and untouched trivia.</summary>
public static class PreservedXmlFile
{
    public static void SaveAtomic(
        XDocument document,
        string path,
        Func<string, string>? serializedXmlTransform = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            ProtoXmlHandler.SaveProtoXml(document, temporaryPath, formattingSourcePath: fullPath);
            if (serializedXmlTransform != null)
            {
                var encoded = PreservedTextEncoding.Decode(File.ReadAllBytes(temporaryPath));
                File.WriteAllBytes(temporaryPath, encoded.Encode(serializedXmlTransform(encoded.Text)));
            }

            var output = File.ReadAllBytes(temporaryPath);
            if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(output))
                return;
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
