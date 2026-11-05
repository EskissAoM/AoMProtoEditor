using System.Xml;

namespace AoMDivineDataEditor.Classes;

// Charge each token before XDocument can allocate it. Delegate XML semantics to
// the existing secure reader; do not normalize or reserialize the input.
internal sealed class BudgetedReferenceXmlReader(XmlReader reader, ReferenceReadBudget budget, string source) : XmlReader
{
    public override bool Read()
    {
        if (!reader.Read()) return false;
        budget.ChargeNode(reader, source);
        return true;
    }
    public override XmlReaderSettings? Settings => reader.Settings;
    public override int AttributeCount => reader.AttributeCount;
    public override string BaseURI => reader.BaseURI;
    public override int Depth => reader.Depth;
    public override bool EOF => reader.EOF;
    public override bool HasValue => reader.HasValue;
    public override bool IsEmptyElement => reader.IsEmptyElement;
    public override string LocalName => reader.LocalName;
    public override string Name => reader.Name;
    public override XmlNameTable NameTable => reader.NameTable;
    public override string NamespaceURI => reader.NamespaceURI;
    public override XmlNodeType NodeType => reader.NodeType;
    public override string Prefix => reader.Prefix;
    public override ReadState ReadState => reader.ReadState;
    public override string Value => reader.Value;
    public override string XmlLang => reader.XmlLang;
    public override XmlSpace XmlSpace => reader.XmlSpace;
    public override string? GetAttribute(string name) => reader.GetAttribute(name);
    public override string? GetAttribute(string name, string? namespaceURI) => reader.GetAttribute(name, namespaceURI);
    public override string GetAttribute(int i) => reader.GetAttribute(i);
    public override string? LookupNamespace(string prefix) => reader.LookupNamespace(prefix);
    public override bool MoveToAttribute(string name) => reader.MoveToAttribute(name);
    public override bool MoveToAttribute(string name, string? ns) => reader.MoveToAttribute(name, ns);
    public override void MoveToAttribute(int i) => reader.MoveToAttribute(i);
    public override bool MoveToElement() => reader.MoveToElement();
    public override bool MoveToFirstAttribute() => reader.MoveToFirstAttribute();
    public override bool MoveToNextAttribute() => reader.MoveToNextAttribute();
    public override bool ReadAttributeValue() => reader.ReadAttributeValue();
    public override void ResolveEntity() => reader.ResolveEntity();
    public override void Close() => reader.Close();
}
