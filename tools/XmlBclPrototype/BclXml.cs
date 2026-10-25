// Prototype only. All parsing, escaping and XML serialization go through System.Xml.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace XmlBclPrototype;

internal static class BclXml
{
    internal static XmlDocument Parse(string xml)
    {
        XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            CheckCharacters = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreWhitespace = false,
        });
        doc.Load(reader);
        return doc;
    }

    internal static string Write(Action<XmlWriter> action, bool document = false, bool indent = false)
    {
        using MemoryStream stream = new();
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            ConformanceLevel = document ? ConformanceLevel.Document : ConformanceLevel.Fragment,
            OmitXmlDeclaration = !document,
            CheckCharacters = true,
            Indent = indent,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false,
        }))
        {
            action(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string Attr(string name, string value)
    {
        XmlDocument doc = new() { XmlResolver = null };
        XmlAttribute attribute = doc.CreateAttribute(name);
        if (attribute.Prefix.Length > 0) XmlConvert.VerifyNCName(attribute.Prefix);
        XmlConvert.VerifyNCName(attribute.LocalName);
        XmlConvert.VerifyXmlChars(value);
        using MemoryStream stream = new();
        // XMLATTR is a QName fragment: its namespace binding can be supplied later by XMLNODE.
        // The namespace-aware writer dropped an unresolved prefix. The BCL's namespace-disabled
        // fragment writer preserves the QName; lexical names and characters are verified by BCL.
        using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true)) { Namespaces = false })
        {
            writer.WriteStartAttribute(null, attribute.Name, null);
            WriteAttributeValue(writer, value);
            writer.WriteEndAttribute();
        }
        // The reference prepends this separator so XMLATTR(...) || XMLATTR(...) stays valid.
        // It is a fragment boundary, not an escaping or serialization implementation.
        return " " + Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static XmlDocument NodeInput(string name, string? attributes, string? content)
        => NodeInput(name, attributes, content, out _, out _);

    private static void WriteAttributeValue(XmlWriter writer, string value)
    {
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] is not ('\t' or '\r' or '\n')) continue;
            writer.WriteString(value[start..i]);
            writer.WriteCharEntity(value[i]);
            start = i + 1;
        }
        writer.WriteString(value[start..]);
    }

    private static XmlNode QualifiedName(string name, bool attribute)
    {
        XmlDocument doc = new();
        XmlNode node = attribute ? doc.CreateAttribute(name) : doc.CreateElement(name);
        if (node.Prefix.Length > 0) XmlConvert.VerifyNCName(node.Prefix);
        XmlConvert.VerifyNCName(node.LocalName);
        return node;
    }

    private static XmlTextReader InputReader(string xml, bool namespaces, bool normalize)
        => new(new StringReader(xml))
        {
            Namespaces = namespaces,
            Normalization = normalize,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

    private static XmlDocument ParseInput(string xml, bool namespaces, bool normalize = false)
    {
        XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
        using XmlTextReader reader = InputReader(xml, namespaces, normalize);
        doc.Load(reader);
        return doc;
    }

    internal static bool NeedsNamespaceContext(string fragment)
    {
        // D-016: XmlReader checks XML syntax; XmlConvert checks QName components;
        // XmlNamespaceManager supplies scope/binding semantics. No invented namespace URI.
        using XmlTextReader reader = InputReader(fragment, false, false);
        XmlNamespaceManager namespaces = new(reader.NameTable);
        bool needsContext = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement) { namespaces.PopScope(); continue; }
            if (reader.NodeType != XmlNodeType.Element) continue;
            namespaces.PushScope();
            XmlNode element = QualifiedName(reader.Name, false);
            bool empty = reader.IsEmptyElement;
            List<XmlAttribute> declarations = [];
            while (reader.MoveToNextAttribute())
            {
                XmlAttribute attribute = (XmlAttribute)QualifiedName(reader.Name, true);
                attribute.Value = reader.Value;
                if (attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/")
                {
                    declarations.Add(attribute);
                    namespaces.AddNamespace(attribute.Name == "xmlns" ? "" : attribute.LocalName, reader.Value);
                }
            }
            // Validate locally provided declarations using the namespace-aware BCL writer,
            // even when another prefix still requires the future parent's context.
            if (declarations.Count > 0)
                _ = Write(writer =>
                {
                    writer.WriteStartElement("", "bindings", namespaces.DefaultNamespace);
                    foreach (XmlAttribute declaration in declarations) declaration.WriteTo(writer);
                    writer.WriteEndElement();
                });
            reader.MoveToElement();
            if (element.Prefix.Length > 0 && namespaces.LookupNamespace(element.Prefix) is null) needsContext = true;
            while (reader.MoveToNextAttribute())
            {
                XmlNode attribute = QualifiedName(reader.Name, true);
                if (attribute.Prefix.Length > 0 && attribute.Prefix != "xmlns"
                    && namespaces.LookupNamespace(attribute.Prefix) is null) needsContext = true;
            }
            reader.MoveToElement();
            if (empty) namespaces.PopScope();
        }
        return needsContext;
    }

    private static XmlDocument NodeInput(string name, string? attributes, string? content,
        out string fragment, out bool needsContext)
    {
        _ = QualifiedName(name, false);
        XmlElement holder = ParseInput("<attributes " + (attributes ?? "") + "/>", false, true).DocumentElement!;
        if (content is not null) XmlConvert.VerifyXmlChars(content);
        using MemoryStream stream = new();
        using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true)) { Namespaces = false })
        {
            writer.WriteStartElement(null, name, null);
            foreach (XmlAttribute attr in holder.Attributes)
            {
                _ = QualifiedName(attr.Name, true);
                writer.WriteStartAttribute(null, attr.Name, null);
                WriteAttributeValue(writer, attr.Value);
                writer.WriteEndAttribute();
            }
            // Content is an input XML fragment, not unescaped text. BCL validates the complete
            // staged fragment below before any result is returned.
            if (content is not null) writer.WriteRaw(content);
            writer.WriteEndElement();
        }
        fragment = Encoding.UTF8.GetString(stream.ToArray());
        needsContext = NeedsNamespaceContext(fragment);
        return ParseInput(fragment, !needsContext);
    }

    private static string WriteDeferred(string fragment, bool indent)
    {
        using MemoryStream stream = new();
        using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true))
        { Namespaces = false, Formatting = indent ? Formatting.Indented : Formatting.None })
        using (XmlTextReader reader = InputReader(fragment, false, false))
        {
            // Streaming BCL reader/writer adaptation preserves the lexical QName until a
            // parent supplies its namespace. All escaping still uses the BCL operations.
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        bool empty = reader.IsEmptyElement;
                        writer.WriteStartElement(null, reader.Name, null);
                        while (reader.MoveToNextAttribute())
                        {
                            writer.WriteStartAttribute(null, reader.Name, null);
                            WriteAttributeValue(writer, reader.Value);
                            writer.WriteEndAttribute();
                        }
                        reader.MoveToElement();
                        if (empty) writer.WriteEndElement();
                        break;
                    case XmlNodeType.EndElement: writer.WriteFullEndElement(); break;
                    case XmlNodeType.Text:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        writer.WriteRaw(Text(reader.Value, false));
                        break;
                    case XmlNodeType.CDATA: writer.WriteCData(reader.Value); break;
                    case XmlNodeType.Comment: writer.WriteComment(reader.Value); break;
                    case XmlNodeType.ProcessingInstruction: writer.WriteProcessingInstruction(reader.Name, reader.Value); break;
                    default: throw new XmlException("Unsupported node in an XMLNODE fragment: " + reader.NodeType);
                }
            }
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string Node(string name, string? attributes, string? content, bool indent)
    {
        XmlDocument doc = NodeInput(name, attributes, content, out string fragment, out bool needsContext);
        if (indent)
        {
            // D-015: any actual xml:space='preserve' disables formatting for the whole result.
            // XPath and namespace matching are BCL operations, not a custom tree traversal.
            XmlNamespaceManager namespaces = new(doc.NameTable);
            namespaces.AddNamespace("xml", "http://www.w3.org/XML/1998/namespace");
            string predicate = needsContext ? "//*[@*[name()='xml:space' and .='preserve']]" : "//*[@xml:space='preserve']";
            if (doc.SelectSingleNode(predicate, namespaces) is not null) indent = false;
        }
        return needsContext ? WriteDeferred(fragment, indent) : Write(writer => doc.DocumentElement!.WriteTo(writer), indent: indent);
    }

    // D-012: the user approved BCL's standard comment repairs. No custom sanitizer.
    // D-013: literal CRLF/CR in Comment and CDATA normalize to LF when read as XML 1.0.
    // XmlReader performs that normalization; the SQL fragment is not rewritten here.
    internal static string Comment(string text) => Write(writer => writer.WriteComment(text));
    // D-017: BCL splits ]]> into CDATA sections; do not substitute escaped plain text.
    internal static string CData(string text) => Write(writer => writer.WriteCData(text));

    internal static string StartDoc()
    {
        // D-018: this is a Unicode SQL string, not a byte stream. The future encoder owns
        // its encoding declaration. Let BCL serialize a declaration with no encoding.
        XmlDocument doc = new();
        return doc.CreateXmlDeclaration("1.0", "", "").OuterXml;
    }

    internal static string Text(string text, bool escapeNewline) => Write(writer =>
    {
        if (!escapeNewline)
        {
            writer.WriteString(text);
            return;
        }
        // Map the SQL flag to the BCL entity operation; escaping is still XmlWriter's job.
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            writer.WriteString(text[start..i]);
            writer.WriteCharEntity(text[i]);
            start = i + 1;
        }
        writer.WriteString(text[start..]);
    });

    internal static string? Evaluate(Case input)
    {
        object?[] args = input.Args;
        int min = input.Function switch { "XMLSTARTDOC" => 0, "XMLATTR" => 2, _ => 1 };
        int max = input.Function switch { "XMLNODE" => 4, "XMLTEXT" => 2, "XMLSTARTDOC" => 0, "XMLATTR" => 2, _ => 1 };
        if (args.Length < min || args.Length > max) throw new ArgumentException("Invalid argument count");
        if (input.Function == "XMLSTARTDOC") return StartDoc();
        if (args[0] is null) return null;
        string value = Convert.ToString(args[0], CultureInfo.InvariantCulture)!;
        if (input.Function == "XMLNODE")
        {
            if (args.Length >= 4 && args[3] is null) return null;
            // D-014: no fourth argument means no formatting; only explicit TRUE enables it.
            return Node(value, args.Length >= 2 ? (string?)args[1] : null,
                args.Length >= 3 ? (string?)args[2] : null,
                args.Length >= 4 && Convert.ToBoolean(args[3], CultureInfo.InvariantCulture));
        }
        if (args.Length >= 2 && args[1] is null) return null;
        return input.Function switch
        {
            "XMLATTR" => Attr(value, (string)args[1]!),
            "XMLCOMMENT" => Comment(value),
            "XMLCDATA" => CData(value),
            "XMLTEXT" => Text(value, args.Length >= 2 && Convert.ToBoolean(args[1], CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException("Unknown prototype function"),
        };
    }
}
