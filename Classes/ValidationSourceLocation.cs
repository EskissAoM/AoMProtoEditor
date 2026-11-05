using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal sealed record ValidationSourceLocation(string Record, string Path)
{
    internal static void Validate(XObject source, Action validation)
    {
        if (source is XAttribute attribute && attribute.Parent is { } owner)
            Validate(owner, "@" + attribute.Name.LocalName, validation);
        else if (source is XElement element) Validate(element, null, validation);
        else validation();
    }

    internal static void Validate(XElement source, string? field, Action validation)
    {
        try { validation(); }
        catch (InvalidDataException error)
        {
            if (!error.Data.Contains(typeof(ValidationSourceLocation))) Attach(error, source, field);
            throw;
        }
    }

    internal static InvalidDataException Attach(InvalidDataException error, XElement source, string? field = null)
    {
        error.Data[typeof(ValidationSourceLocation)] = From(source, field);
        return error;
    }

    internal static ValidationSourceLocation From(XElement element, string? field = null)
    {
        if (field != null && !field.StartsWith('@'))
        {
            var segments = field.Split('/');
            var current = element;
            var found = true;
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].StartsWith('@')) return From(current, segments[index]);
                var child = current.Elements().FirstOrDefault(candidate => candidate.Name.LocalName.Equals(segments[index], StringComparison.OrdinalIgnoreCase));
                if (child == null) { found = false; break; }
                current = child;
            }
            if (found) return From(current);
        }
        var record = element.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate.Parent != null && ReferenceEquals(candidate.Parent, candidate.Document?.Root))
            ?? element.AncestorsAndSelf().Last();
        var name = record.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value
            ?? record.Elements().FirstOrDefault(child => child.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var steps = element.AncestorsAndSelf().TakeWhile(candidate => !ReferenceEquals(candidate, record)).Reverse()
            .Select(candidate => candidate.Name.LocalName + "[" +
                (candidate.ElementsBeforeSelf().Count(sibling => sibling.Name == candidate.Name) + 1) + "]");
        var path = string.Join('/', steps);
        if (field != null) path = path + "/" + field;
        return new(name.Trim(), path);
    }
}

internal sealed record LocatedValidationIssue(string Message, ValidationSourceLocation? Location);

/// <summary>Optional provenance around existing List-based validators; messages and rules are unchanged.</summary>
internal sealed class LocatedValidationErrors : List<string>
{
    private readonly Dictionary<int, ValidationSourceLocation> _locations = [];

    internal static IDisposable At(List<string> errors, XElement element, string? field = null)
        => new Scope(errors, element, field);

    internal IReadOnlyList<LocatedValidationIssue> Issues => this.Select((message, index) =>
        new LocatedValidationIssue(message, _locations.GetValueOrDefault(index)))
        .DistinctBy(issue => issue.Message, StringComparer.Ordinal).ToList();

    private sealed class Scope(List<string> errors, XElement element, string? field) : IDisposable
    {
        private readonly int _start = errors.Count;
        public void Dispose()
        {
            if (errors is not LocatedValidationErrors located) return;
            var source = ValidationSourceLocation.From(element, field);
            for (var index = _start; index < errors.Count; index++)
                located._locations.TryAdd(index, source); // Keep the innermost, most specific scope.
        }
    }
}
