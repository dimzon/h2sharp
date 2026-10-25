/*
 * Copyright 2004-2026 H2 Group. Multiple-Licensed under the MPL 2.0,
 * and the EPL 1.0 (https://h2database.com/html/license.html).
 * Initial Developer: H2 Group
 * Ported from org.h2.expression.function.XMLFunction.
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * D-012..D-018: SQL evaluation/arity follow H2 2.5; XML construction uses BCL.
 * Platform-only adapters are local functions, not additional H2 members/APIs.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using H2Sharp.Api;
using H2Sharp.Engine;
using H2Sharp.Message;
using H2Sharp.Values;

namespace H2Sharp.Expressions.Functions;

/// <summary>An XML function.</summary>
public sealed class XMLFunction : FunctionN
{
    // Keep the reference's constant casing: XMLNODE and the xmlNode method otherwise
    // collide after PascalCase conversion. No extra function identifiers are introduced.
    public const int XMLATTR = 0;
    public const int XMLCDATA = XMLATTR + 1;
    public const int XMLCOMMENT = XMLCDATA + 1;
    public const int XMLNODE = XMLCOMMENT + 1;
    public const int XMLSTARTDOC = XMLNODE + 1;
    public const int XMLTEXT = XMLSTARTDOC + 1;

    private static readonly string[] Names = ["XMLATTR", "XMLCDATA", "XMLCOMMENT", "XMLNODE", "XMLSTARTDOC", "XMLTEXT"];
    private readonly int function;

    /// <summary>Creates an XML function with variable parameters.</summary>
    public XMLFunction(int function) : base(new Expression[4]) => this.function = function;

    /// <inheritdoc/>
    public override Value GetValue(SessionLocal session)
    {
        switch (function)
        {
            case XMLNODE: return XmlNode(session);
            case XMLSTARTDOC:
                // D-018: Unicode SQL string; the byte-stream producer owns its encoding.
                return ValueVarchar.Get(new XmlDocument().CreateXmlDeclaration("1.0", "", "").OuterXml, session);
            default: return base.GetValue(session);
        }
    }

    private Value XmlNode(SessionLocal session)
    {
        Value v1 = args[0].GetValue(session);
        if (v1 == ValueNull.Instance) return ValueNull.Instance;
        int length = args.Length;
        string? attr = length >= 2 ? args[1].GetValue(session).GetString() : null;
        string? content = length >= 3 ? args[2].GetValue(session).GetString() : null;
        bool indent;
        if (length >= 4)
        {
            Value v4 = args[3].GetValue(session);
            if (v4 == ValueNull.Instance) return ValueNull.Instance;
            indent = v4.GetBoolean();
        }
        else indent = false; // D-014, unlike H2's default true.
        string name = v1.GetString()!;

        try
        {
            _ = QName(name, false);
            XmlElement holder = ParseInput("<attributes " + (attr ?? "") + "/>", false, true).DocumentElement!;
            if (content is not null) XmlConvert.VerifyXmlChars(content);
            string fragment;
            using (MemoryStream stream = new())
            {
                using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true)) { Namespaces = false })
                {
                    writer.WriteStartElement(null, name, null);
                    foreach (XmlAttribute attribute in holder.Attributes)
                    {
                        _ = QName(attribute.Name, true);
                        writer.WriteStartAttribute(null, attribute.Name, null);
                        AttributeValue(writer, attribute.Value);
                        writer.WriteEndAttribute();
                    }
                    // H2 content is an XML fragment, not plain unescaped text.
                    if (content is not null) writer.WriteRaw(content);
                    writer.WriteEndElement();
                }
                fragment = Encoding.UTF8.GetString(stream.ToArray());
            }
            bool needsContext = NeedsContext(fragment);
            XmlDocument doc = ParseInput(fragment, !needsContext);
            if (indent)
            {
                // D-015: no custom tree walker; BCL XPath selects the reserved XML attribute.
                XmlNamespaceManager namespaces = new(doc.NameTable);
                namespaces.AddNamespace("xml", "http://www.w3.org/XML/1998/namespace");
                string predicate = needsContext ? "//*[@*[name()='xml:space' and .='preserve']]" : "//*[@xml:space='preserve']";
                if (doc.SelectSingleNode(predicate, namespaces) is not null) indent = false;
            }
            string result = needsContext ? Deferred(fragment, indent) : WriteXml(writer => doc.DocumentElement!.WriteTo(writer), indent);
            return ValueVarchar.Get(result, session);
        }
        catch (Exception e) when (e is XmlException or ArgumentException or InvalidOperationException)
        {
            // Platform adaptation: malformed XML has no Java helper exception equivalent.
            // Preserve an explicit SQL refusal, 90008, instead of leaking BCL exceptions.
            throw DbException.GetInvalidValueException(GetName() + " argument", e.Message);
        }

        static XmlNode QName(string value, bool attribute)
        {
            XmlDocument doc = new();
            XmlNode node = attribute ? doc.CreateAttribute(value) : doc.CreateElement(value);
            if (node.Prefix.Length > 0) XmlConvert.VerifyNCName(node.Prefix);
            XmlConvert.VerifyNCName(node.LocalName);
            return node;
        }

        static XmlTextReader Reader(string xml, bool namespaces, bool normalize = false)
            => new(new StringReader(xml))
            {
                Namespaces = namespaces, Normalization = normalize,
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            };

        static XmlDocument ParseInput(string xml, bool namespaces, bool normalize = false)
        {
            XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
            using XmlTextReader reader = Reader(xml, namespaces, normalize);
            doc.Load(reader);
            return doc;
        }

        static string WriteXml(Action<XmlWriter> action, bool indent = false)
        {
            using MemoryStream stream = new();
            using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false), ConformanceLevel = ConformanceLevel.Fragment,
                OmitXmlDeclaration = true, CheckCharacters = true, Indent = indent,
                NewLineChars = "\n", NewLineHandling = NewLineHandling.Entitize, CloseOutput = false,
            })) action(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        static void AttributeValue(XmlWriter writer, string value)
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

        static bool NeedsContext(string xml)
        {
            using XmlTextReader reader = Reader(xml, false);
            XmlNamespaceManager namespaces = new(reader.NameTable);
            bool needsContext = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement) { namespaces.PopScope(); continue; }
                if (reader.NodeType != XmlNodeType.Element) continue;
                namespaces.PushScope();
                XmlNode element = QName(reader.Name, false);
                bool empty = reader.IsEmptyElement;
                List<XmlAttribute> declarations = [];
                while (reader.MoveToNextAttribute())
                {
                    XmlAttribute attribute = (XmlAttribute)QName(reader.Name, true);
                    attribute.Value = reader.Value;
                    if (attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/")
                    {
                        declarations.Add(attribute);
                        namespaces.AddNamespace(attribute.Name == "xmlns" ? "" : attribute.LocalName, reader.Value);
                    }
                }
                if (declarations.Count > 0)
                    _ = WriteXml(writer =>
                    {
                        writer.WriteStartElement("", "bindings", namespaces.DefaultNamespace);
                        foreach (XmlAttribute declaration in declarations) declaration.WriteTo(writer);
                        writer.WriteEndElement();
                    });
                reader.MoveToElement();
                if (element.Prefix.Length > 0 && namespaces.LookupNamespace(element.Prefix) is null) needsContext = true;
                while (reader.MoveToNextAttribute())
                {
                    XmlNode attribute = QName(reader.Name, true);
                    if (attribute.Prefix.Length > 0 && attribute.Prefix != "xmlns"
                        && namespaces.LookupNamespace(attribute.Prefix) is null) needsContext = true;
                }
                reader.MoveToElement();
                if (empty) namespaces.PopScope();
            }
            return needsContext;
        }

        static string Deferred(string xml, bool indent)
        {
            using MemoryStream stream = new();
            using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true))
            { Namespaces = false, Formatting = indent ? Formatting.Indented : Formatting.None })
            using (XmlTextReader reader = Reader(xml, false))
            {
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
                                AttributeValue(writer, reader.Value);
                                writer.WriteEndAttribute();
                            }
                            reader.MoveToElement();
                            if (empty) writer.WriteEndElement();
                            break;
                        case XmlNodeType.EndElement: writer.WriteFullEndElement(); break;
                        case XmlNodeType.Text:
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            writer.WriteRaw(WriteXml(textWriter => textWriter.WriteString(reader.Value)));
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
    }

    /// <inheritdoc/>
    protected override Value GetValue(SessionLocal session, Value? v1, Value? v2, Value? v3)
    {
        string text = v1!.GetString()!;
        string? value = function == XMLATTR ? v2!.GetString() : null;
        bool escapeNewline = function == XMLTEXT && v2 is not null && v2.GetBoolean();
        string result;
        try
        {
            switch (function)
            {
                case XMLATTR:
                    XmlAttribute attribute = new XmlDocument().CreateAttribute(text);
                    if (attribute.Prefix.Length > 0) XmlConvert.VerifyNCName(attribute.Prefix);
                    XmlConvert.VerifyNCName(attribute.LocalName);
                    XmlConvert.VerifyXmlChars(value!);
                    using (MemoryStream stream = new())
                    {
                        using (XmlTextWriter writer = new(stream, new UTF8Encoding(false, true)) { Namespaces = false })
                        {
                            writer.WriteStartAttribute(null, attribute.Name, null);
                            int start = 0;
                            for (int i = 0; i < value!.Length; i++)
                            {
                                if (value[i] is not ('\t' or '\r' or '\n')) continue;
                                writer.WriteString(value[start..i]);
                                writer.WriteCharEntity(value[i]);
                                start = i + 1;
                            }
                            writer.WriteString(value[start..]);
                            writer.WriteEndAttribute();
                        }
                        // Reference fragment separator: concatenated XMLATTR values stay valid.
                        result = " " + Encoding.UTF8.GetString(stream.ToArray());
                    }
                    break;
                case XMLCDATA: result = WriteXml(writer => writer.WriteCData(text)); break;
                case XMLCOMMENT: result = WriteXml(writer => writer.WriteComment(text)); break;
                case XMLTEXT:
                    result = WriteXml(writer =>
                    {
                        if (!escapeNewline) { writer.WriteString(text); return; }
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
                    break;
                default: throw DbException.GetInternalError("function=" + function);
            }
        }
        catch (Exception e) when (e is XmlException or ArgumentException or InvalidOperationException)
        {
            // Explicit platform XML validation failure, reported through the SQL error surface.
            throw DbException.GetInvalidValueException(GetName() + " argument", e.Message);
        }
        return ValueVarchar.Get(result, session);

        static string WriteXml(Action<XmlWriter> action)
        {
            using MemoryStream stream = new();
            using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false), ConformanceLevel = ConformanceLevel.Fragment,
                OmitXmlDeclaration = true, CheckCharacters = true,
                NewLineChars = "\n", NewLineHandling = NewLineHandling.Entitize, CloseOutput = false,
            })) action(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <inheritdoc/>
    public override Expression Optimize(SessionLocal session)
    {
        bool allConst = OptimizeArguments(session, true);
        int min, max;
        switch (function)
        {
            case XMLATTR: min = max = 2; break;
            case XMLNODE: min = 1; max = 4; break;
            case XMLCDATA:
            case XMLCOMMENT: min = max = 1; break;
            case XMLSTARTDOC: min = max = 0; break;
            case XMLTEXT: min = 1; max = 2; break;
            default: throw DbException.GetInternalError("function=" + function);
        }
        int len = args.Length;
        if (len < min || len > max)
            throw DbException.Get(ErrorCode.InvalidParameterCount2, GetName(),
                min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture));
        type = TypeInfo.TypeVarchar;
        return allConst ? TypedValueExpression.GetTypedIfNull(GetValue(session), type) : this;
    }

    /// <inheritdoc/>
    public override string GetName() => Names[function];
}
