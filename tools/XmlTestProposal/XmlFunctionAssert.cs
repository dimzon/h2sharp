// Copyright 2026 h2sharp-sql contributors.
// Licensed under the MPL 2.0 or the EPL 1.0.
// XML function assertions: compare nodes/data while retaining escapeNewline's lexical guarantee.
using System.IO;
using System.Xml;
using Xunit;

namespace H2SharpSql.Specs.Infrastructure;

internal static class XmlFunctionAssert
{
    private static XmlDocument Parse(string xml)
    {
        XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            CheckCharacters = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
        });
        doc.Load(reader);
        return doc;
    }

    internal static void Attribute(string actual, string name, string value)
    {
        XmlElement root = Parse("<probe " + actual + "/>").DocumentElement!;
        Assert.Equal(1, root.Attributes.Count);
        XmlAttribute attribute = root.Attributes[0]!;
        Assert.Equal(name, attribute.Name);
        Assert.Equal("", attribute.NamespaceURI);
        Assert.Equal(value, attribute.Value);
        Assert.False(root.HasChildNodes);
    }

    internal static void Element(string actual, string name, string text)
    {
        XmlElement root = Parse(actual).DocumentElement!;
        Assert.Equal(name, root.Name);
        Assert.Equal("", root.NamespaceURI);
        Assert.Equal(0, root.Attributes.Count);
        if (text.Length == 0) Assert.False(root.HasChildNodes);
        else
        {
            Assert.Equal(1, root.ChildNodes.Count);
            XmlText value = Assert.IsType<XmlText>(root.FirstChild);
            Assert.Equal(text, value.Value);
        }
    }

    internal static void Declaration(string actual)
    {
        XmlDocument doc = Parse(actual + "<probe/>");
        XmlDeclaration declaration = Assert.IsType<XmlDeclaration>(doc.FirstChild);
        Assert.Equal("1.0", declaration.Version);
        Assert.Equal("", declaration.Encoding);
        Assert.Equal("", declaration.Standalone);
        foreach (XmlNode node in doc.ChildNodes)
            Assert.True(node is XmlDeclaration or XmlElement or XmlWhitespace);
        XmlElement root = doc.DocumentElement!;
        Assert.Equal("probe", root.Name);
        Assert.Equal("", root.NamespaceURI);
        Assert.Equal(0, root.Attributes.Count);
        Assert.False(root.HasChildNodes);
    }

    internal static void EscapedText(string actual, string expected)
    {
        // The SQL flag promises an escaped newline, not just equivalent decoded text.
        Assert.DoesNotContain('\r', actual);
        Assert.DoesNotContain('\n', actual);
        XmlElement root = Parse("<probe>" + actual + "</probe>").DocumentElement!;
        Assert.Equal(1, root.ChildNodes.Count);
        XmlText text = Assert.IsType<XmlText>(root.FirstChild);
        Assert.Equal(expected, text.Value);
    }
}
