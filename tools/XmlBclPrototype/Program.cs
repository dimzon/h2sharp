using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace XmlBclPrototype;

internal sealed record Case(string Id, string Function, object?[] Args, string? AttributeContext = null,
    string? SqlOverride = null, Func<string?>? BclOverride = null)
{
    internal string Sql => SqlOverride ?? ("SELECT " + Function + "(" + string.Join(", ", Args.Select(value => value switch
    {
        null => "NULL",
        bool boolean => boolean ? "TRUE" : "FALSE",
        string text => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    })) + ")");
}

internal sealed record ValueResult(string? Value, bool IsNull = false, string? Error = null, int? Code = null);
internal sealed record Inspection(bool? Valid, bool? PayloadAndNodeContract, string? Canonical, string? Error = null);
internal sealed record Evidence(Case Input, string SqlHex, ValueResult Reference, ValueResult Bcl,
    string? ReferenceHex, string? BclHex, Inspection ReferenceInspection, Inspection BclInspection,
    bool? CanonicallyEquivalent);
internal sealed record Control(string Name, string Left, string Right, bool WithComments, bool ExpectedEqual,
    bool ActualEqual, string Explanation);
internal sealed record CommentPolicyCheck(string Name, string? Fragment, string ExpectedBody,
    bool ExpectedAccepted, bool ActualAccepted)
{
    public bool Passed => ExpectedAccepted == ActualAccepted;
}
internal sealed record LineEndingPolicyCheck(string Name, string Function, string? Fragment,
    string ExpectedBody, bool ExpectedAccepted, bool ActualAccepted)
{
    public bool Passed => ExpectedAccepted == ActualAccepted;
}
internal sealed record IndentPolicyCheck(string Name, ValueResult Result, string? ExpectedFragment,
    bool ExpectedAccepted, bool ActualAccepted)
{
    public bool Passed => ExpectedAccepted == ActualAccepted;
}
internal sealed record NamespacePolicyCheck(string Name, string? Fragment, string? ExpectedFragment,
    string? ContextAttributes, bool? ExpectedNeedsContext, bool? ActualNeedsContext, bool Passed);
internal sealed record FragmentDiagnostic(string Id, bool? WellFormedFragment, bool? NeedsContext, string? Error);
internal sealed record CDataPolicyCheck(string Name, ValueResult Result, string? ExpectedBody,
    int ExpectedSections, string? PayloadPath, string? ExpectedXml, bool ExpectedAccepted, bool ActualAccepted)
{
    public bool Passed => ExpectedAccepted == ActualAccepted;
}
internal sealed record DeclarationPolicyCheck(string Name, string? Xml, string? BytesHex,
    bool ExpectedAccepted, bool ActualAccepted)
{
    public bool Passed => ExpectedAccepted == ActualAccepted;
}

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static Case[] Cases() =>
    [
        new("attr-simple", "XMLATTR", ["a", "1"]),
        new("attr-quotes", "XMLATTR", ["a", "<&\"'>"]),
        new("attr-newlines", "XMLATTR", ["a", "x\t\r\ny"]),
        new("attr-unicode", "XMLATTR", ["a", "ёж"]),
        new("attr-emoji", "XMLATTR", ["a", "🎉"]),
        new("attr-name-space", "XMLATTR", ["a b", "1"]),
        new("attr-xml-prefix", "XMLATTR", ["xml:lang", "en"]),
        new("attr-prefix-context", "XMLATTR", ["p:a", "1"], "xmlns:p=\"urn:attrs\""),
        new("attr-null", "XMLATTR", ["a", null]),
        new("attr-control", "XMLATTR", ["a", "x\u0001y"]),
        new("attr-surrogate", "XMLATTR", ["a", "\ud800"]),
        new("attr-multi-colon", "XMLATTR", ["p:a:b", "1"], "xmlns:p=\"urn:attrs\""),
        new("node-empty", "XMLNODE", ["a"]),
        new("node-empty-content", "XMLNODE", ["a", null, ""]),
        new("node-text", "XMLNODE", ["a", null, "x"]),
        new("node-text-spaces", "XMLNODE", ["a", null, " x \n y ", false]),
        new("node-crlf-fragment", "XMLNODE", ["a", null, "x\r\ny", false]),
        new("node-attrs", "XMLNODE", ["a", " b='2' a=\"1\"", "x", false]),
        new("node-fragment", "XMLNODE", ["a", null, "<b/>text<c/>", false]),
        new("node-mixed", "XMLNODE", ["a", null, "before <b/> after", true]),
        new("node-ns", "XMLNODE", ["p:a", " xmlns:p=\"urn:x\"", "<p:b/>", false]),
        new("node-default-ns", "XMLNODE", ["a", " xmlns=\"urn:x\"", "<b/>", false]),
        new("node-unbound-prefix", "XMLNODE", ["p:a"]),
        new("node-name-space", "XMLNODE", ["a b"]),
        new("node-duplicate-attrs", "XMLNODE", ["a", " a='1' a='2'", null]),
        new("node-malformed-attrs", "XMLNODE", ["a", " a='1", null]),
        new("node-raw-amp", "XMLNODE", ["a", null, "x & y"]),
        new("node-malformed-content", "XMLNODE", ["a", null, "<b>"] ),
        new("node-emoji", "XMLNODE", ["a", null, "🎉", false]),
        new("node-indent-true", "XMLNODE", ["a", null, "<b/><c/>", true]),
        new("node-indent-false", "XMLNODE", ["a", null, "<b/><c/>", false]),
        new("node-indent-default", "XMLNODE", ["a", null, "<b/><c/>"]),
        new("node-multiline-true", "XMLNODE", ["a", null, "<b/>\n<c/>", true]),
        new("node-multiline-false", "XMLNODE", ["a", null, "<b/>\n<c/>", false]),
        new("node-multiline-default", "XMLNODE", ["a", null, "<b/>\n<c/>"]),
        new("node-text-multiline-default", "XMLNODE", ["a", null, " x\n y "]),
        new("node-mixed-default", "XMLNODE", ["a", null, "before <b/> after"]),
        new("node-space-preserve", "XMLNODE", ["a", " xml:space='preserve'", "<b/><c/>", true]),
        new("node-space-preserve-default", "XMLNODE", ["a", " xml:space='preserve'", "<b/><c/>"]),
        new("node-space-nested", "XMLNODE", ["a", null, "<b xml:space='preserve'><c/><d/></b><e/>", true]),
        new("node-space-deep", "XMLNODE", ["a", null, "<b><c xml:space='preserve'><d/></c></b><e/>", true]),
        new("node-space-default", "XMLNODE", ["a", " xml:space='default'", "<b/><c/>", true]),
        new("node-space-nested-default", "XMLNODE", ["a", null, "<b xml:space='default'><c/><d/></b><e/>", true]),
        new("node-space-preserve-reset", "XMLNODE", ["a", " xml:space='preserve'", "<b xml:space='default'><c/><d/></b><e/>", true]),
        new("node-space-default-preserve", "XMLNODE", ["a", " xml:space='default'", "<b xml:space='preserve'><c/><d/></b><e/>", true]),
        new("node-space-multiline", "XMLNODE", ["a", null, "<b xml:space='preserve'> x\n y <c/></b><e/>", true]),
        new("node-space-lookalike-attr", "XMLNODE", ["a", " space='preserve'", "<b/><c/>", true]),
        new("node-space-lookalike-namespace", "XMLNODE", ["a", " xmlns:p='urn:not-xml' p:space='preserve'", "<b/><c/>", true]),
        new("node-space-lookalike-comment", "XMLNODE", ["a", null, "<!--xml:space='preserve'--><b/><c/>", true]),
        new("composition-attrs", "XMLNODE", ["a", BclXml.Attr("a", "1") + BclXml.Attr("b", "2"), "x", false],
            SqlOverride: "SELECT XMLNODE('a', XMLATTR('a','1') || XMLATTR('b','2'), 'x', FALSE)"),
        new("composition-prefix", "XMLNODE", ["p:a", BclXml.Attr("xmlns:p", "urn:x") + BclXml.Attr("p:v", "1"), "<p:b/>", false],
            SqlOverride: "SELECT XMLNODE('p:a', XMLATTR('xmlns:p','urn:x') || XMLATTR('p:v','1'), '<p:b/>', FALSE)"),
        // The inner reference XMLNODE returns a trailing LF that becomes parent content.
        // Model that supplied fragment explicitly, independently of the BCL refusal below.
        new("composition-inherited-prefix", "XMLNODE", ["r", " xmlns:p='urn:x'", "<p:a/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('p:a'), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("p:a", null, null, false), false)),
        new("composition-inherited-attribute", "XMLNODE", ["r", " xmlns:p='urn:x'", "<a p:v='1'/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('a',XMLATTR('p:v','1')), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("a", BclXml.Attr("p:v", "1"), null, false), false)),
        new("composition-inherited-deep", "XMLNODE", ["r", " xmlns:p='urn:x'", "<b><p:a/>\n</b>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('b',NULL,XMLNODE('p:a'),FALSE), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("b", null, BclXml.Node("p:a", null, null, false), false), false)),
        new("composition-inherited-default", "XMLNODE", ["r", " xmlns='urn:x'", "<a/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns','urn:x'), XMLNODE('a'), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns", "urn:x"), BclXml.Node("a", null, null, false), false)),
        new("composition-rebound-prefix", "XMLNODE", ["r", " xmlns:p='urn:outer'", "<p:a xmlns:p='urn:inner'/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:outer'), XMLNODE('p:a',XMLATTR('xmlns:p','urn:inner')), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:outer"), BclXml.Node("p:a", BclXml.Attr("xmlns:p", "urn:inner"), null, false), false)),
        new("composition-default-reset", "XMLNODE", ["r", " xmlns='urn:x'", "<a xmlns=''/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns','urn:x'), XMLNODE('a',XMLATTR('xmlns','')), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns", "urn:x"), BclXml.Node("a", BclXml.Attr("xmlns", ""), null, false), false)),
        new("composition-deferred-preserve", "XMLNODE", ["r", " xmlns:p='urn:x'", "<p:a xml:space='preserve'><b/><c/></p:a>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('p:a',XMLATTR('xml:space','preserve'),'<b/><c/>',TRUE), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("p:a", BclXml.Attr("xml:space", "preserve"), "<b/><c/>", true), false)),
        new("composition-deferred-text", "XMLNODE", ["r", " xmlns:p='urn:x'", "<p:a> x\t&#xD;&#xA;y &amp; </p:a>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('p:a',NULL,XMLTEXT(' x' || CHAR(9) || CHAR(13) || CHAR(10) || 'y & ',TRUE),FALSE), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("p:a", null, BclXml.Text(" x\t\r\ny & ", true), false), false)),
        new("composition-deferred-tab-attribute", "XMLNODE", ["r", " xmlns:p='urn:x'", "<p:a p:v='x\t\r\ny'/>\n", false],
            SqlOverride: "SELECT XMLNODE('r', XMLATTR('xmlns:p','urn:x'), XMLNODE('p:a',XMLATTR('p:v','x' || CHAR(9) || CHAR(13) || CHAR(10) || 'y')), FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("p:a", BclXml.Attr("p:v", "x\t\r\ny"), null, false), false)),
        new("node-pending-pretty", "XMLNODE", ["p:a", null, "<p:b/><p:c/>", true]),
        new("node-pending-preserve", "XMLNODE", ["p:a", " xml:space='preserve'", "<p:b/><p:c/>", true]),
        new("node-pending-attribute", "XMLNODE", ["a", " p:v='1'", null]),
        new("node-scope-no-leak", "XMLNODE", ["a", null, "<b xmlns:p='urn:inner'><p:c/></b><p:d/>", false]),
        new("composition-prefix-siblings", "XMLNODE", ["r", " xmlns:p='urn:outer'", "<a><b xmlns:p='urn:inner'><p:c/></b><p:d/></a>\n", false],
            SqlOverride: "SELECT XMLNODE('r',XMLATTR('xmlns:p','urn:outer'),XMLNODE('a',NULL,'<b xmlns:p=''urn:inner''><p:c/></b><p:d/>',FALSE),FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:outer"), BclXml.Node("a", null, "<b xmlns:p='urn:inner'><p:c/></b><p:d/>", false), false)),
        new("node-xml-prefix", "XMLNODE", ["xml:a"]),
        new("node-bad-qname", "XMLNODE", ["p:a:b"]),
        new("node-content-bad-qname", "XMLNODE", ["a", null, "<p:a:b/>"]),
        new("node-attrs-bad-qname", "XMLNODE", ["a", " p:v:w='1'", null]),
        new("node-illegal-binding", "XMLNODE", ["q:a", " xmlns:p=''", null]),
        new("node-wrong-xml-binding", "XMLNODE", ["q:a", " xmlns:xml='urn:wrong'", null]),
        new("node-illegal-xmlns-binding", "XMLNODE", ["q:a", " xmlns:p='http://www.w3.org/2000/xmlns/'", null]),
        new("node-duplicate-expanded-attrs", "XMLNODE", ["a", " xmlns:p='urn:x' xmlns:q='urn:x' p:v='1' q:v='2'", null]),
        new("composition-duplicate-expanded-attrs", "XMLNODE", ["r", " xmlns:p='urn:x' xmlns:q='urn:x'", "<a p:v='1' q:v='2'/>\n", false],
            SqlOverride: "SELECT XMLNODE('r',XMLATTR('xmlns:p','urn:x') || XMLATTR('xmlns:q','urn:x'),XMLNODE('a',XMLATTR('p:v','1') || XMLATTR('q:v','2')),FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x") + BclXml.Attr("xmlns:q", "urn:x"), BclXml.Node("a", BclXml.Attr("p:v", "1") + BclXml.Attr("q:v", "2"), null, false), false)),
        new("node-unknown-entity", "XMLNODE", ["a", null, "&missing;"]),
        new("node-invalid-control", "XMLNODE", ["a", null, "x\u0001y"]),
        new("composition-newlines", "XMLNODE", ["a", BclXml.Attr("a", "x\t\r\ny"), "x", false],
            SqlOverride: "SELECT XMLNODE('a', XMLATTR('a','x' || CHAR(9) || CHAR(13) || CHAR(10) || 'y'), 'x', FALSE)"),
        new("node-null", "XMLNODE", [null]),
        new("node-null-indent", "XMLNODE", ["a", null, "x", null]),
        new("comment-simple", "XMLCOMMENT", ["x"]),
        new("comment-spaces", "XMLCOMMENT", [" x "]),
        new("comment-newlines", "XMLCOMMENT", ["x\ny"]),
        new("comment-crlf", "XMLCOMMENT", ["x\r\ny"]),
        new("comment-cr", "XMLCOMMENT", ["x\ry"]),
        new("comment-eol-spaces", "XMLCOMMENT", [" x\t\r\n y "]),
        new("comment-mixed-eol", "XMLCOMMENT", ["a\r\nb\rc\nd"]),
        new("comment-nel", "XMLCOMMENT", ["x\u0085y"]),
        new("comment-control", "XMLCOMMENT", ["x\u0001y"]),
        new("comment-double-hyphen", "XMLCOMMENT", ["x--y"]),
        new("comment-repeated-hyphen", "XMLCOMMENT", ["x---y"]),
        new("comment-last-hyphen", "XMLCOMMENT", ["x-"]),
        new("comment-empty", "XMLCOMMENT", [""]),
        new("comment-emoji", "XMLCOMMENT", ["🎉"]),
        new("cdata-simple", "XMLCDATA", ["x"]),
        new("cdata-empty", "XMLCDATA", [""]),
        new("cdata-newline", "XMLCDATA", ["x\n"]),
        new("cdata-crlf", "XMLCDATA", ["x\r\ny"]),
        new("cdata-cr", "XMLCDATA", ["x\ry"]),
        new("cdata-eol-spaces", "XMLCDATA", [" x\t\r\n y "]),
        new("cdata-mixed-eol", "XMLCDATA", ["a\r\nb\rc\nd"]),
        new("cdata-nel", "XMLCDATA", ["x\u0085y"]),
        new("cdata-control", "XMLCDATA", ["x\u0001y"]),
        new("cdata-terminator", "XMLCDATA", ["x]]>y"]),
        new("cdata-only-terminator", "XMLCDATA", ["]]>"]),
        new("cdata-leading-terminator", "XMLCDATA", ["]]>x"]),
        new("cdata-trailing-terminator", "XMLCDATA", ["x]]>"]),
        new("cdata-repeated-terminator", "XMLCDATA", ["x]]>y]]>z"]),
        new("cdata-adjacent-terminator", "XMLCDATA", ["]]>]]>"]),
        new("cdata-specials-terminator", "XMLCDATA", [" <&\"'\t]]>ёж "]),
        new("cdata-terminator-eol", "XMLCDATA", [" x\r\n]]>\r y "]),
        new("composition-cdata-split", "XMLNODE", ["a", null, "x]]&gt;y", false],
            SqlOverride: "SELECT XMLNODE('a',NULL,XMLCDATA('x]]>y'),FALSE)",
            BclOverride: () => BclXml.Node("a", null, BclXml.CData("x]]>y"), false)),
        new("composition-cdata-deferred", "XMLNODE", ["r", " xmlns:p='urn:x'", "<p:a>x]]&gt;y</p:a>\n", false],
            SqlOverride: "SELECT XMLNODE('r',XMLATTR('xmlns:p','urn:x'),XMLNODE('p:a',NULL,XMLCDATA('x]]>y'),FALSE),FALSE)",
            BclOverride: () => BclXml.Node("r", BclXml.Attr("xmlns:p", "urn:x"), BclXml.Node("p:a", null, BclXml.CData("x]]>y"), false), false)),
        new("cdata-emoji", "XMLCDATA", ["🎉"]),
        new("cdata-surrogate", "XMLCDATA", ["\ud800"]),
        new("cdata-null", "XMLCDATA", [null]),
        new("startdoc", "XMLSTARTDOC", []),
        new("composition-startdoc", "XMLNODE", ["a", null, "ёж&lt;&amp;&quot;", false],
            SqlOverride: "SELECT XMLSTARTDOC() || XMLNODE('a',NULL,XMLTEXT('ёж<&\"'),FALSE)",
            BclOverride: () => BclXml.StartDoc() + BclXml.Node("a", null, BclXml.Text("ёж<&\"", false), false)),
        new("text-simple", "XMLTEXT", ["x"]),
        new("text-specials", "XMLTEXT", ["<&\"'>"]),
        new("text-spaces", "XMLTEXT", [" x \n y "]),
        new("text-newline-escaped", "XMLTEXT", ["x\ny", true]),
        new("text-crlf-escaped", "XMLTEXT", ["x\r\ny", true]),
        new("text-crlf-plain", "XMLTEXT", ["x\r\ny", false]),
        new("text-unicode", "XMLTEXT", ["ёж"]),
        new("text-emoji", "XMLTEXT", ["🎉"]),
        new("text-control", "XMLTEXT", ["x\u0001y"]),
        new("text-surrogate", "XMLTEXT", ["\ud800"]),
        new("text-null", "XMLTEXT", [null]),
        new("text-null-flag", "XMLTEXT", ["x", null]),
        new("arity-startdoc", "XMLSTARTDOC", ["x"]),
        new("arity-node", "XMLNODE", []),
    ];

    private static string Hex(string value)
        => string.Concat(value.Select(ch => ((int)ch).ToString("x4", CultureInfo.InvariantCulture)));

    private static string Unhex(string value)
    {
        char[] chars = new char[value.Length / 4];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)int.Parse(value.AsSpan(i * 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new string(chars);
    }

    private static string Canonical(XmlDocument doc, bool comments = true)
    {
        XmlDsigC14NTransform transform = new(comments);
        transform.LoadInput(doc);
        using Stream output = (Stream)transform.GetOutput(typeof(Stream));
        using StreamReader reader = new(output, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Inspection Inspect(Case input, ValueResult result)
    {
        if (result.Error is not null || result.IsNull) return new(null, null, null);
        string value = result.Value!;
        XmlDocument doc;
        try
        {
            doc = input.Function switch
            {
                "XMLNODE" => BclXml.Parse(value),
                "XMLATTR" => BclXml.Parse("<probe " + (input.AttributeContext ?? "") + " " + value + "/>") ,
                "XMLSTARTDOC" => BclXml.Parse(value + "<probe/>"),
                _ => BclXml.Parse("<probe>" + value + "</probe>"),
            };
        }
        catch (XmlException e) { return new(false, false, null, e.Message); }

        bool contract;
        try
        {
            XmlElement root = doc.DocumentElement!;
            string? expected = input.Args.Length == 0 ? null : (string?)input.Args[0];
            switch (input.Function)
            {
                case "XMLATTR":
                    XmlAttribute[] attributes = root.Attributes.Cast<XmlAttribute>()
                        .Where(attr => attr.NamespaceURI != "http://www.w3.org/2000/xmlns/").ToArray();
                    contract = attributes.Length == 1 && attributes[0].Name == expected
                        && attributes[0].Value == (string?)input.Args[1];
                    break;
                case "XMLNODE":
                    XmlDocument beforeSerialization = BclXml.NodeInput(expected!,
                        input.Args.Length >= 2 ? (string?)input.Args[1] : null,
                        input.Args.Length >= 3 ? (string?)input.Args[2] : null);
                    contract = Canonical(doc) == Canonical(beforeSerialization);
                    break;
                case "XMLSTARTDOC":
                    contract = doc.FirstChild is XmlDeclaration declaration && declaration.Version == "1.0"
                        && (declaration.Encoding.Length == 0 || declaration.Encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase));
                    break;
                case "XMLCOMMENT":
                    XmlComment[] comments = root.ChildNodes.OfType<XmlComment>().ToArray();
                    contract = comments.Length == 1 && comments[0].Value == expected
                        && root.ChildNodes.Cast<XmlNode>().All(node => node is XmlComment or XmlWhitespace);
                    RemoveOutsideWhitespace(root);
                    break;
                case "XMLCDATA":
                    XmlCDataSection[] sections = root.ChildNodes.OfType<XmlCDataSection>().ToArray();
                    contract = sections.Length > 0 && string.Concat(sections.Select(section => section.Value)) == expected
                        && root.ChildNodes.Cast<XmlNode>().All(node => node is XmlCDataSection or XmlWhitespace);
                    RemoveOutsideWhitespace(root);
                    break;
                case "XMLTEXT":
                    contract = root.InnerText == expected && root.ChildNodes.Cast<XmlNode>()
                        .All(node => node is XmlText or XmlWhitespace or XmlSignificantWhitespace);
                    break;
                default: throw new InvalidOperationException(input.Function);
            }
            return new(true, contract, Canonical(doc));
        }
        catch (Exception e) when (e is XmlException or ArgumentException or CryptographicException)
        {
            return new(true, false, null, e.GetType().Name + ": " + e.Message);
        }
    }

    private static void RemoveOutsideWhitespace(XmlElement root)
    {
        // Only formatting outside the expected Comment/CDATA node is removed. Text input,
        // text inside XMLNODE, and whitespace inside Comment/CDATA values are never trimmed.
        foreach (XmlNode node in root.ChildNodes.Cast<XmlNode>().Where(node => node is XmlWhitespace).ToArray())
            root.RemoveChild(node);
    }

    private static List<Control> Controls()
    {
        List<Control> result = [];
        void Check(string name, string left, string right, bool comments, bool equal, string explanation)
            => result.Add(new(name, left, right, comments, equal,
                Canonical(BclXml.Parse(left), comments) == Canonical(BclXml.Parse(right), comments), explanation));
        Check("empty-element", "<a/>", "<a />", true, true, "Equivalent empty-element spellings");
        Check("attribute-order", "<a b='2' a='1'/>", "<a a=\"1\" b=\"2\"/>", true, true, "Quotes and attribute order");
        Check("entity-spelling", "<a>&amp;</a>", "<a>&#x26;</a>", true, true, "Equivalent character references");
        Check("text-spaces", "<a>x</a>", "<a> x </a>", true, false, "Significant text must remain different");
        Check("text-newline", "<a>x</a>", "<a>x\n</a>", true, false, "A text newline must not disappear");
        Check("element-indentation", "<a><b/></a>", "<a>\n  <b/>\n</a>", true, false, "C14N preserves inter-element text whitespace");
        Check("cdata-hidden", "<a><![CDATA[x]]></a>", "<a>x</a>", true, true, "C14N erases CDATA kind: a separate node assertion is required");
        Check("comment-hidden", "<a><!--x--></a>", "<a><!--wrong--></a>", false, true, "Without comments a broken XMLCOMMENT could go unnoticed");
        Check("comment-visible", "<a><!--x--></a>", "<a><!--wrong--></a>", true, false, "With comments the changed data is visible");
        Check("declaration-hidden", "<?xml version='1.0'?><a/>", "<a/>", true, true, "C14N erases declaration: check XmlDeclaration separately");
        return result;
    }

    private static List<CommentPolicyCheck> CommentPolicyChecks(Evidence[] evidence)
    {
        List<CommentPolicyCheck> checks = [];
        void Check(string name, string? fragment, string expectedBody, bool expectedAccepted = true)
        {
            // Read a Comment node and compare its body exactly. Do not derive an expectation
            // by invoking the serializer or by trimming the reference's formatting.
            Inspection inspection = Inspect(new Case(name, "XMLCOMMENT", [expectedBody]),
                new ValueResult(fragment, IsNull: fragment is null));
            checks.Add(new(name, fragment, expectedBody, expectedAccepted,
                inspection.Valid == true && inspection.PayloadAndNodeContract == true));
        }
        (string Id, string BclBody, string ReferenceBody)[] expectations =
        [
            ("comment-simple", "x", " x "),
            ("comment-spaces", " x ", "  x  "),
            ("comment-empty", "", "  "),
            ("comment-double-hyphen", "x- -y", " x- -y "),
            ("comment-repeated-hyphen", "x- - -y", " x- - -y "),
            ("comment-last-hyphen", "x- ", " x- "),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Bcl.Value, expectation.BclBody);
            Check(expectation.Id + "/jar", item.Reference.Value, expectation.ReferenceBody);
        }
        Check("reject-wrong-body", "<!--wrong-->", "x- -y", false);
        Check("reject-text-instead-of-comment", "x- -y", "x- -y", false);
        Check("reject-trimmed-input-spaces", "<!--x-->", " x ", false);
        Check("reject-extra-comment", "<!--x- -y--><!--extra-->", "x- -y", false);
        return checks;
    }

    private static List<LineEndingPolicyCheck> LineEndingPolicyChecks(Evidence[] evidence)
    {
        List<LineEndingPolicyCheck> checks = [];
        void Check(string name, string function, string? fragment, string expectedBody, bool expectedAccepted = true)
        {
            // D-013: fixed expected XML 1.0 content, not a normalization of the observed output.
            // Inspect independently requires the expected node kind and exact body after XmlReader.
            Inspection inspection = Inspect(new Case(name, function, [expectedBody]),
                new ValueResult(fragment, IsNull: fragment is null));
            checks.Add(new(name, function, fragment, expectedBody, expectedAccepted,
                inspection.Valid == true && inspection.PayloadAndNodeContract == true));
        }
        (string Id, string BclBody, string ReferenceBody)[] expectations =
        [
            ("comment-crlf", "x\ny", "\n    x\n    y\n"),
            ("comment-cr", "x\ny", " x\ny "),
            ("comment-eol-spaces", " x\t\n y ", "\n     x\t\n     y \n"),
            ("comment-mixed-eol", "a\nb\nc\nd", "\n    a\n    b\nc\n    d\n"),
            ("comment-newlines", "x\ny", "\n    x\n    y\n"),
            ("comment-nel", "x\u0085y", " x\u0085y "),
            ("cdata-crlf", "x\ny", "x\ny"),
            ("cdata-cr", "x\ny", "x\ny"),
            ("cdata-eol-spaces", " x\t\n y ", " x\t\n y "),
            ("cdata-mixed-eol", "a\nb\nc\nd", "a\nb\nc\nd"),
            ("cdata-newline", "x\n", "x\n"),
            ("cdata-nel", "x\u0085y", "x\u0085y"),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Input.Function, item.Bcl.Value, expectation.BclBody);
            Check(expectation.Id + "/jar", item.Input.Function, item.Reference.Value, expectation.ReferenceBody);
        }
        Check("reject-comment-missing-lf", "XMLCOMMENT", "<!--xy-->", "x\ny", false);
        Check("reject-cdata-missing-lf", "XMLCDATA", "<![CDATA[xy]]>", "x\ny", false);
        Check("reject-comment-trimmed-space", "XMLCOMMENT", "<!--x\t\ny-->", " x\t\n y ", false);
        Check("reject-cdata-trimmed-space", "XMLCDATA", "<![CDATA[x\t\ny]]>", " x\t\n y ", false);
        Check("reject-text-instead-of-cdata", "XMLCDATA", "x\ny", "x\ny", false);
        Check("reject-cdata-instead-of-comment", "XMLCOMMENT", "<![CDATA[x\ny]]>", "x\ny", false);
        Check("reject-comment-nel-to-lf", "XMLCOMMENT", "<!--x\ny-->", "x\u0085y", false);
        Check("reject-cdata-nel-to-lf", "XMLCDATA", "<![CDATA[x\ny]]>", "x\u0085y", false);
        return checks;
    }

    private static List<IndentPolicyCheck> IndentPolicyChecks(Evidence[] evidence)
    {
        List<IndentPolicyCheck> checks = [];
        void Check(string name, ValueResult result, string? expectedFragment, bool expectedAccepted = true)
        {
            bool accepted = false;
            if (result.Error is null)
            {
                if (expectedFragment is null) accepted = result.IsNull;
                else if (!result.IsNull && result.Value is not null)
                {
                    try { accepted = Canonical(BclXml.Parse(result.Value)) == Canonical(BclXml.Parse(expectedFragment)); }
                    catch (XmlException) { accepted = false; }
                }
            }
            checks.Add(new(name, result, expectedFragment, expectedAccepted, accepted));
        }
        // Fixed XML literals, not output of the serializer under test. PreserveWhitespace keeps
        // the difference between generated indentation and actual input LF/text observable.
        const string compact = "<a><b/><c/></a>";
        const string pretty = "<a>\n  <b/>\n  <c/>\n</a>";
        const string multiline = "<a><b/>\n<c/></a>";
        const string referencePretty = "<a>\n    <b/>\n    <c/>\n</a>";
        const string mixed = "<a>before <b/> after</a>";
        const string preserve = "<a xml:space='preserve'><b/><c/></a>";
        (string Id, string? BclFragment, string? ReferenceFragment)[] expectations =
        [
            ("node-indent-default", compact, compact),
            ("node-indent-false", compact, compact),
            ("node-indent-true", pretty, compact),
            ("node-multiline-default", multiline, referencePretty),
            ("node-multiline-false", multiline, multiline),
            ("node-multiline-true", "<a>\n  <b/>\n<c/></a>", referencePretty),
            ("node-text-multiline-default", "<a> x\n y </a>", "<a>\n     x\n     y \n</a>"),
            ("node-mixed-default", mixed, mixed),
            ("node-mixed", mixed, mixed),
            ("node-space-preserve-default", preserve, preserve),
            ("node-null-indent", null, null),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Bcl, expectation.BclFragment);
            Check(expectation.Id + "/jar", item.Reference, expectation.ReferenceFragment);
        }
        Check("reject-pretty-default", new(pretty), compact, false);
        Check("reject-ignored-explicit-true", new(compact), pretty, false);
        Check("reject-removed-input-lf", new(compact), multiline, false);
        Check("reject-trimmed-input-text", new("<a>x\ny</a>"), "<a> x\n y </a>", false);
        return checks;
    }

    private static List<IndentPolicyCheck> XmlSpacePolicyChecks(Evidence[] evidence)
    {
        List<IndentPolicyCheck> checks = [];
        void Check(string name, ValueResult result, string expectedFragment, bool expectedAccepted = true)
        {
            bool accepted = false;
            if (result.Error is null && !result.IsNull && result.Value is not null)
            {
                try { accepted = Canonical(BclXml.Parse(result.Value)) == Canonical(BclXml.Parse(expectedFragment)); }
                catch (XmlException) { accepted = false; }
            }
            checks.Add(new(name, result, expectedFragment, expectedAccepted, accepted));
        }
        // D-015: fixed whole-tree expectations. No XPath or serializer call constructs expected XML.
        const string rootPreserve = "<a xml:space='preserve'><b/><c/></a>";
        const string nested = "<a><b xml:space='preserve'><c/><d/></b><e/></a>";
        const string multiline = "<a><b xml:space='preserve'> x\n y <c/></b><e/></a>";
        const string defaultPretty = "<a xml:space='default'>\n  <b/>\n  <c/>\n</a>";
        const string attrPretty = "<a space='preserve'>\n  <b/>\n  <c/>\n</a>";
        const string commentPretty = "<a>\n  <!--xml:space='preserve'-->\n  <b/>\n  <c/>\n</a>";
        (string Id, string BclFragment, string ReferenceFragment)[] expectations =
        [
            ("node-space-preserve", rootPreserve, rootPreserve),
            ("node-space-preserve-default", rootPreserve, rootPreserve),
            ("node-space-nested", nested, nested),
            ("node-space-deep", "<a><b><c xml:space='preserve'><d/></c></b><e/></a>", "<a><b><c xml:space='preserve'><d/></c></b><e/></a>"),
            ("node-space-default", defaultPretty, "<a xml:space='default'><b/><c/></a>"),
            ("node-space-nested-default", "<a>\n  <b xml:space='default'>\n    <c/>\n    <d/>\n  </b>\n  <e/>\n</a>", "<a><b xml:space='default'><c/><d/></b><e/></a>"),
            ("node-space-preserve-reset", "<a xml:space='preserve'><b xml:space='default'><c/><d/></b><e/></a>", "<a xml:space='preserve'><b xml:space='default'><c/><d/></b><e/></a>"),
            ("node-space-default-preserve", "<a xml:space='default'><b xml:space='preserve'><c/><d/></b><e/></a>", "<a xml:space='default'><b xml:space='preserve'><c/><d/></b><e/></a>"),
            ("node-space-multiline", multiline, "<a>\n    <b xml:space='preserve'> x\n     y <c/></b><e/>\n</a>"),
            ("node-space-lookalike-attr", attrPretty, "<a space='preserve'><b/><c/></a>"),
            ("node-space-lookalike-namespace", "<a xmlns:p='urn:not-xml' p:space='preserve'>\n  <b/>\n  <c/>\n</a>", "<a xmlns:p='urn:not-xml' p:space='preserve'><b/><c/></a>"),
            ("node-space-lookalike-comment", commentPretty, "<a><!--xml:space='preserve'--><b/><c/></a>"),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Bcl, expectation.BclFragment);
            Check(expectation.Id + "/jar", item.Reference, expectation.ReferenceFragment);
        }
        Check("reject-indented-root-preserve", new("<a xml:space='preserve'>\n  <b/>\n  <c/>\n</a>"), rootPreserve, false);
        Check("reject-protection-of-only-subtree", new("<a>\n  <b xml:space='preserve'><c/><d/></b>\n  <e/>\n</a>"), nested, false);
        Check("reject-disabled-default", new("<a xml:space='default'><b/><c/></a>"), defaultPretty, false);
        Check("reject-removed-preserved-lf", new("<a><b xml:space='preserve'> x y <c/></b><e/></a>"), multiline, false);
        Check("reject-unqualified-space-as-directive", new("<a space='preserve'><b/><c/></a>"), attrPretty, false);
        Check("reject-comment-as-directive", new("<a><!--xml:space='preserve'--><b/><c/></a>"), commentPretty, false);
        return checks;
    }

    private static List<NamespacePolicyCheck> NamespacePolicyChecks(Evidence[] evidence)
    {
        List<NamespacePolicyCheck> checks = [];
        void Check(string name, ValueResult result, string expected, bool expectedNeedsContext = false, string? context = null)
        {
            bool passed = false;
            bool? actualNeedsContext = null;
            if (result.Error is null && !result.IsNull && result.Value is not null)
            {
                try
                {
                    actualNeedsContext = BclXml.NeedsNamespaceContext(result.Value);
                    string Wrap(string fragment) => context is null ? fragment : "<context " + context + ">" + fragment + "</context>";
                    passed = actualNeedsContext == expectedNeedsContext
                        && Canonical(BclXml.Parse(Wrap(result.Value))) == Canonical(BclXml.Parse(Wrap(expected)));
                }
                catch (Exception e) when (e is XmlException or ArgumentException) { passed = false; }
            }
            checks.Add(new(name, result.Value, expected, context, expectedNeedsContext, actualNeedsContext, passed));
        }
        (string Id, string BclXml, string ReferenceXml, bool NeedsContext, string? Context)[] expectations =
        [
            ("node-ns", "<p:a xmlns:p='urn:x'><p:b/></p:a>", "<p:a xmlns:p='urn:x'><p:b/></p:a>", false, null),
            ("node-default-ns", "<a xmlns='urn:x'><b/></a>", "<a xmlns='urn:x'><b/></a>", false, null),
            ("node-xml-prefix", "<xml:a/>", "<xml:a/>", false, null),
            ("composition-inherited-prefix", "<r xmlns:p='urn:x'><p:a/></r>", "<r xmlns:p='urn:x'><p:a/>\n</r>", false, null),
            ("composition-inherited-attribute", "<r xmlns:p='urn:x'><a p:v='1'/></r>", "<r xmlns:p='urn:x'><a p:v='1'/>\n</r>", false, null),
            ("composition-inherited-deep", "<r xmlns:p='urn:x'><b><p:a/></b></r>", "<r xmlns:p='urn:x'><b><p:a/>\n</b>\n</r>", false, null),
            ("composition-inherited-default", "<r xmlns='urn:x'><a/></r>", "<r xmlns='urn:x'><a/>\n</r>", false, null),
            ("composition-rebound-prefix", "<r xmlns:p='urn:outer'><p:a xmlns:p='urn:inner'/></r>", "<r xmlns:p='urn:outer'><p:a xmlns:p='urn:inner'/>\n</r>", false, null),
            ("composition-default-reset", "<r xmlns='urn:x'><a xmlns=''/></r>", "<r xmlns='urn:x'><a xmlns=''/>\n</r>", false, null),
            ("composition-deferred-preserve", "<r xmlns:p='urn:x'><p:a xml:space='preserve'><b/><c/></p:a></r>", "<r xmlns:p='urn:x'><p:a xml:space='preserve'><b/><c/></p:a>\n</r>", false, null),
            ("composition-deferred-text", "<r xmlns:p='urn:x'><p:a> x\t&#xD;\ny &amp; </p:a></r>", "<r xmlns:p='urn:x'><p:a> x\t&#xD;&#xA;y &amp; </p:a>\n</r>", false, null),
            ("composition-deferred-tab-attribute", "<r xmlns:p='urn:x'><p:a p:v='x&#x9;&#xD;&#xA;y'/></r>", "<r xmlns:p='urn:x'><p:a p:v='x\t\r\ny'/>\n</r>", false, null),
            ("node-unbound-prefix", "<p:a/>", "<p:a/>\n", true, "xmlns:p='urn:x'"),
            ("node-pending-pretty", "<p:a>\n  <p:b/>\n  <p:c/>\n</p:a>", "<p:a><p:b/><p:c/></p:a>\n", true, "xmlns:p='urn:x'"),
            ("node-pending-preserve", "<p:a xml:space='preserve'><p:b/><p:c/></p:a>", "<p:a xml:space='preserve'><p:b/><p:c/></p:a>\n", true, "xmlns:p='urn:x'"),
            ("node-pending-attribute", "<a p:v='1'/>", "<a p:v='1'/>\n", true, "xmlns:p='urn:x'"),
            ("node-scope-no-leak", "<a><b xmlns:p='urn:inner'><p:c/></b><p:d/></a>", "<a><b xmlns:p='urn:inner'><p:c/></b><p:d/></a>\n", true, "xmlns:p='urn:outer'"),
            ("composition-prefix-siblings", "<r xmlns:p='urn:outer'><a><b xmlns:p='urn:inner'><p:c/></b><p:d/></a></r>", "<r xmlns:p='urn:outer'><a><b xmlns:p='urn:inner'><p:c/></b><p:d/></a>\n</r>", false, null),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Bcl, expectation.BclXml, expectation.NeedsContext, expectation.Context);
            Check(expectation.Id + "/jar", item.Reference, expectation.ReferenceXml, expectation.NeedsContext, expectation.Context);
        }
        foreach (string id in new[] { "node-bad-qname", "node-content-bad-qname", "node-attrs-bad-qname",
            "node-illegal-binding", "node-wrong-xml-binding", "node-illegal-xmlns-binding",
            "node-duplicate-expanded-attrs", "composition-duplicate-expanded-attrs", "node-unknown-entity",
            "node-invalid-control", "node-malformed-content", "node-duplicate-attrs", "node-malformed-attrs", "node-raw-amp" })
        {
            ValueResult result = evidence.Single(item => item.Input.Id == id).Bcl;
            checks.Add(new(id + "/must-reject", result.Value, null, null, null, null, result.Error is not null));
        }
        ValueResult pending = evidence.Single(item => item.Input.Id == "node-unbound-prefix").Bcl;
        foreach (string context in new[] { "", "xmlns:q='urn:x'" })
        {
            bool rejected = false;
            try { _ = BclXml.Parse("<context " + context + ">" + pending.Value + "</context>"); }
            catch (XmlException) { rejected = pending.Error is null && pending.Value is not null; }
            checks.Add(new("reject-unsatisfied-parent-context/" + context, pending.Value, null, context, true, true, rejected));
        }
        return checks;
    }

    private static List<FragmentDiagnostic> FragmentDiagnostics(Evidence[] evidence)
    {
        List<FragmentDiagnostic> result = [];
        foreach (Evidence item in evidence.Where(item => item.Input.Function == "XMLNODE" && item.Bcl.Value is not null))
        {
            try
            {
                bool needsContext = BclXml.NeedsNamespaceContext(item.Bcl.Value!);
                if (!needsContext) _ = BclXml.Parse(item.Bcl.Value!);
                result.Add(new(item.Input.Id, true, needsContext, null));
            }
            catch (Exception e) when (e is XmlException or ArgumentException)
            { result.Add(new(item.Input.Id, false, null, e.Message)); }
        }
        return result;
    }

    private static List<CDataPolicyCheck> CDataPolicyChecks(Evidence[] evidence)
    {
        List<CDataPolicyCheck> checks = [];
        void Check(string name, ValueResult result, string? expectedBody, int expectedSections,
            bool expectedAccepted = true, string? payloadPath = null, string? expectedXml = null)
        {
            bool accepted = false;
            if (result.Error is null)
            {
                if (expectedBody is null) accepted = result.IsNull;
                else if (!result.IsNull && result.Value is not null)
                {
                    try
                    {
                        XmlDocument doc = BclXml.Parse("<probe>" + result.Value + "</probe>");
                        XmlNode? payload = payloadPath is null ? doc.DocumentElement : doc.SelectSingleNode(payloadPath);
                        if (payload is not null)
                        {
                            XmlCDataSection[] sections = payload.ChildNodes.OfType<XmlCDataSection>().ToArray();
                            accepted = sections.Length == expectedSections;
                            if (expectedSections > 0)
                                accepted &= string.Concat(sections.Select(section => section.Value)) == expectedBody
                                    && payload.ChildNodes.Cast<XmlNode>().All(node => node is XmlCDataSection or XmlWhitespace);
                            else
                                accepted &= payload.InnerText == expectedBody && payload.ChildNodes.Cast<XmlNode>()
                                    .All(node => node is XmlText or XmlWhitespace or XmlSignificantWhitespace);
                            if (expectedXml is not null)
                                accepted &= Canonical(doc) == Canonical(BclXml.Parse("<probe>" + expectedXml + "</probe>"));
                        }
                    }
                    catch (XmlException) { accepted = false; }
                }
            }
            checks.Add(new(name, result, expectedBody, expectedSections, payloadPath, expectedXml, expectedAccepted, accepted));
        }
        // Explicit expected text and section counts. No expected output is generated by WriteCData.
        (string Id, string Body, int BclSections, int ReferenceSections)[] expectations =
        [
            ("cdata-simple", "x", 1, 1),
            ("cdata-empty", "", 1, 1),
            ("cdata-crlf", "x\ny", 1, 1),
            ("cdata-terminator", "x]]>y", 2, 0),
            ("cdata-only-terminator", "]]>", 2, 0),
            ("cdata-leading-terminator", "]]>x", 2, 0),
            ("cdata-trailing-terminator", "x]]>", 2, 0),
            ("cdata-repeated-terminator", "x]]>y]]>z", 3, 0),
            ("cdata-adjacent-terminator", "]]>]]>", 3, 0),
            ("cdata-specials-terminator", " <&\"'\t]]>ёж ", 2, 0),
            ("cdata-terminator-eol", " x\n]]>\n y ", 2, 0),
        ];
        foreach (var expectation in expectations)
        {
            Evidence item = evidence.Single(item => item.Input.Id == expectation.Id);
            Check(expectation.Id + "/BCL", item.Bcl, expectation.Body, expectation.BclSections);
            Check(expectation.Id + "/jar", item.Reference, expectation.Body, expectation.ReferenceSections);
        }
        Evidence plain = evidence.Single(item => item.Input.Id == "composition-cdata-split");
        Check(plain.Input.Id + "/BCL", plain.Bcl, "x]]>y", 2, payloadPath: "/*/*",
            expectedXml: "<a><![CDATA[x]]]]><![CDATA[>y]]></a>");
        Check(plain.Input.Id + "/jar", plain.Reference, "x]]>y", 0, payloadPath: "/*/*", expectedXml: "<a>x]]&gt;y</a>\n");
        Evidence deferred = evidence.Single(item => item.Input.Id == "composition-cdata-deferred");
        Check(deferred.Input.Id + "/BCL", deferred.Bcl, "x]]>y", 2, payloadPath: "/*/*/*",
            expectedXml: "<r xmlns:p='urn:x'><p:a><![CDATA[x]]]]><![CDATA[>y]]></p:a></r>");
        Check(deferred.Input.Id + "/jar", deferred.Reference, "x]]>y", 0, payloadPath: "/*/*/*",
            expectedXml: "<r xmlns:p='urn:x'><p:a>x]]&gt;y</p:a>\n</r>\n");
        Evidence nil = evidence.Single(item => item.Input.Id == "cdata-null");
        Check("cdata-null/BCL", nil.Bcl, null, 0);
        Check("cdata-null/jar", nil.Reference, null, 0);
        Check("reject-text-instead-of-sections", new("x]]&gt;y"), "x]]>y", 2, false);
        Check("reject-escaped-text-inside-section", new("<![CDATA[x]]&gt;y]]>"), "x]]>y", 2, false);
        Check("reject-dropped-boundary", new("<![CDATA[x]]><![CDATA[y]]>"), "x]]>y", 2, false);
        Check("reject-trimmed-input-spaces", new("<![CDATA[x]]]]><![CDATA[>y]]>"), " x]]>y ", 2, false);
        Check("reject-extra-element", new("<![CDATA[x]]]]><![CDATA[>y]]><b/>"), "x]]>y", 2, false);
        Check("reject-unexpected-section-count", new("<![CDATA[x]]]]><![CDATA[>]]><![CDATA[y]]>"), "x]]>y", 2, false);
        return checks;
    }

    private static List<DeclarationPolicyCheck> DeclarationPolicyChecks(Evidence[] evidence)
    {
        List<DeclarationPolicyCheck> checks = [];
        bool ValidDocument(XmlDocument doc, string rootName, string expectedText, string expectedEncoding)
            => doc.FirstChild is XmlDeclaration declaration && declaration.Version == "1.0"
                && declaration.Encoding.Equals(expectedEncoding, StringComparison.OrdinalIgnoreCase)
                && declaration.Standalone.Length == 0 && doc.DocumentElement is XmlElement root
                && root.Name == rootName && root.InnerText == expectedText
                && root.ChildNodes.Cast<XmlNode>().All(node => node is XmlText)
                && doc.ChildNodes.Cast<XmlNode>().All(node => node is XmlDeclaration or XmlElement or XmlWhitespace);
        void CheckString(string name, ValueResult result, bool expectedAccepted = true,
            bool completeDocument = false, string expectedText = "")
        {
            bool accepted = false;
            if (result.Error is null && !result.IsNull && result.Value is not null)
            {
                try
                {
                    accepted = ValidDocument(BclXml.Parse(result.Value + (completeDocument ? "" : "<probe/>")),
                        completeDocument ? "a" : "probe", expectedText, "");
                }
                catch (XmlException) { accepted = false; }
            }
            checks.Add(new(name, result.Value, null, expectedAccepted, accepted));
        }
        Evidence declaration = evidence.Single(item => item.Input.Id == "startdoc");
        CheckString("Unicode-string/BCL", declaration.Bcl);
        CheckString("Unicode-string/jar", declaration.Reference);
        CheckString("reject-missing-declaration", new(""), false);
        CheckString("reject-UTF8-promise-in-string", new("<?xml version='1.0' encoding='utf-8'?>"), false);
        CheckString("reject-UTF16-promise-in-string", new("<?xml version='1.0' encoding='utf-16'?>"), false);
        CheckString("reject-comment-instead-of-declaration", new("<!--xml version='1.0'-->"), false);
        CheckString("reject-double-declaration", new("<?xml version='1.0'?><?xml version='1.0'?>"), false);
        CheckString("reject-extra-element", new("<?xml version='1.0'?><extra/>"), false);
        CheckString("reject-standalone-promise", new("<?xml version='1.0' standalone='yes'?>"), false);
        Evidence composition = evidence.Single(item => item.Input.Id == "composition-startdoc");
        CheckString("composed-document/BCL", composition.Bcl, completeDocument: true, expectedText: "ёж<&\"");
        CheckString("composed-document/jar", composition.Reference, completeDocument: true, expectedText: "ёж<&\"");
        const string text = "ёж🎉";
        foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false, true), new UnicodeEncoding(false, true, true) })
        {
            foreach (bool byteSerializer in new[] { false, true })
            {
                using MemoryStream stream = new();
                if (byteSerializer)
                {
                    using XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
                    { Encoding = encoding, OmitXmlDeclaration = false, CheckCharacters = true, CloseOutput = false });
                    writer.WriteStartDocument();
                    writer.WriteStartElement("a");
                    writer.WriteString(text);
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
                else
                {
                    using StreamWriter writer = new(stream, encoding, leaveOpen: true);
                    writer.Write(declaration.Bcl.Value);
                    writer.Write("<a>" + text + "</a>");
                }
                byte[] bytes = stream.ToArray();
                bool accepted = bytes.AsSpan().StartsWith(encoding.GetPreamble());
                stream.Position = 0;
                XmlDocument doc = new() { PreserveWhitespace = true, XmlResolver = null };
                using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                doc.Load(reader);
                accepted &= ValidDocument(doc, "a", text, byteSerializer ? encoding.WebName : "");
                checks.Add(new((byteSerializer ? "byte-XmlWriter/" : "encoded-Unicode-string/") + encoding.WebName,
                    doc.FirstChild!.OuterXml, Convert.ToHexString(bytes).ToLowerInvariant(), true, accepted));
            }
        }
        const string falseEncoding = "<?xml version='1.0' encoding='utf-16'?><a>ёж🎉</a>";
        byte[] mismatch = new UTF8Encoding(false, true).GetBytes(falseEncoding);
        bool mismatchAccepted = true;
        try
        {
            using MemoryStream stream = new(mismatch);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read()) { }
        }
        catch (XmlException) { mismatchAccepted = false; }
        checks.Add(new("reject-byte-encoding-mismatch", falseEncoding, Convert.ToHexString(mismatch).ToLowerInvariant(), false, mismatchAccepted));
        return checks;
    }

    private static Dictionary<string, ValueResult> Reference(string repo, string directory, Case[] cases)
    {
        string transport = Path.Combine(directory, "reference-input.tsv");
        File.WriteAllLines(transport, cases.Select(input => input.Id + "|" + Hex(input.Sql)), Encoding.ASCII);
        ProcessStartInfo start = new("java")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in new[] { "-Duser.language=en", "-Duser.timezone=UTC", "--class-path",
            Path.Combine(repo, "reference/h2/h2/target/h2-2.5.259-SNAPSHOT.jar"),
            Path.Combine(repo, "tools/XmlBclPrototype/JarProbe.java"), transport }) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) throw new TimeoutException("Java probe timed out");
        string output = stdout.GetAwaiter().GetResult();
        string errors = stderr.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(directory, "jar-output.txt"), output);
        File.WriteAllText(Path.Combine(directory, "jar-stderr.txt"), errors);
        if (process.ExitCode != 0) throw new InvalidOperationException(errors);
        Dictionary<string, ValueResult> results = new(StringComparer.Ordinal);
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] data = line.Split('|', 4);
            results.Add(data[0], data[1] switch
            {
                "OK" => new(Unhex(data[2])),
                "NULL" => new(null, IsNull: true),
                "ERR" => new(null, Error: Unhex(data[3]), Code: int.Parse(data[2], CultureInfo.InvariantCulture)),
                _ => throw new InvalidOperationException(line),
            });
        }
        if (results.Count != cases.Length) throw new InvalidOperationException("Reference did not answer every case");
        return results;
    }

    private static string Preview(string value)
    {
        // Diagnostic representation only, not XML escaping. UTF-8 JSON cannot transport an
        // unpaired surrogate faithfully; always preserve its code unit as visible ASCII.
        StringBuilder output = new();
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                output.Append(ch).Append(value[++i]);
            }
            else if (char.IsSurrogate(ch)) output.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            else output.Append(ch);
        }
        return output.ToString();
    }

    private static string Cell(string? value) => value is null ? "—" : "`" + JsonSerializer.Serialize(Preview(value))
        .Replace("|", "&#124;", StringComparison.Ordinal).Replace("`", "&#96;", StringComparison.Ordinal) + "`";
    private static string Flag(bool? value) => value is null ? "—" : value.Value ? "yes" : "NO";

    private static void Report(string directory, Evidence[] evidence, List<Control> controls,
        List<CommentPolicyCheck> commentChecks, List<LineEndingPolicyCheck> lineEndingChecks,
        List<IndentPolicyCheck> indentChecks, List<IndentPolicyCheck> xmlSpaceChecks,
        List<NamespacePolicyCheck> namespaceChecks, List<FragmentDiagnostic> fragmentDiagnostics,
        List<CDataPolicyCheck> cdataChecks, List<DeclarationPolicyCheck> declarationChecks, string jarSha)
    {
        File.WriteAllText(Path.Combine(directory, "evidence.json"), JsonSerializer.Serialize(new
        {
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            XmlAssembly = typeof(XmlWriter).Assembly.FullName,
            CanonicalizationAssembly = typeof(XmlDsigC14NTransform).Assembly.FullName,
            JarSha256 = jarSha,
            Summary = new
            {
                Cases = evidence.Length,
                ReferenceErrors = evidence.Count(item => item.Reference.Error is not null),
                BclErrors = evidence.Count(item => item.Bcl.Error is not null),
                ReferenceInvalidXml = evidence.Count(item => item.ReferenceInspection.Valid == false),
                BclInvalidXml = evidence.Count(item => item.BclInspection.Valid == false),
                BclMalformedNodeFragments = fragmentDiagnostics.Count(item => item.WellFormedFragment == false),
                BclNodeFragmentsNeedingContext = fragmentDiagnostics.Count(item => item.NeedsContext == true),
                BclPayloadViolations = evidence.Where(item => item.BclInspection.PayloadAndNodeContract == false).Select(item => item.Input.Id).ToArray(),
                CanonicalEqual = evidence.Count(item => item.CanonicallyEquivalent == true),
                CanonicalDifferent = evidence.Count(item => item.CanonicallyEquivalent == false),
            },
            // Hex is the authoritative lossless transport; previews are for reading the report.
            Cases = evidence.Select(item => new
            {
                item.Input.Id,
                item.Input.Function,
                item.SqlHex,
                AttributeContext = item.Input.AttributeContext,
                Reference = new { item.Reference.IsNull, item.Reference.Code,
                    ValueHex = item.ReferenceHex,
                    Preview = item.Reference.Value is null ? null : Program.Preview(item.Reference.Value),
                    Error = item.Reference.Error is null ? null : Program.Preview(item.Reference.Error) },
                Bcl = new { item.Bcl.IsNull, item.Bcl.Code,
                    ValueHex = item.BclHex,
                    Preview = item.Bcl.Value is null ? null : Program.Preview(item.Bcl.Value),
                    Error = item.Bcl.Error is null ? null : Program.Preview(item.Bcl.Error) },
                item.ReferenceInspection,
                item.BclInspection,
                item.CanonicallyEquivalent,
            }),
            Controls = controls,
            ApprovedCommentPolicy = new { Decision = "D-012", Checks = commentChecks },
            ApprovedLineEndingPolicy = new { Decision = "D-013", Checks = lineEndingChecks },
            ApprovedIndentPolicy = new { Decision = "D-014", Checks = indentChecks },
            ApprovedXmlSpacePolicy = new { Decision = "D-015", Checks = xmlSpaceChecks },
            ApprovedNamespacePolicy = new { Decision = "D-016", Checks = namespaceChecks },
            ApprovedCDataPolicy = new { Decision = "D-017", Checks = cdataChecks },
            ApprovedDeclarationPolicy = new { Decision = "D-018", Checks = declarationChecks },
            FragmentDiagnostics = fragmentDiagnostics,
        }, JsonOptions));
        StringBuilder report = new("# BCL XML prototype results\n\n");
        report.Append("Jar SHA256: `").Append(jarSha).Append("`\n\n");
        report.Append("XML generation/validation: System.Xml; canonicalization: Microsoft XmlDsigC14NTransform, with comments.\n\n");
        report.Append("The payload contract preserves decoded text/attribute values and expected node kinds. Whitespace is preserved; only outside Comment/CDATA formatting is excluded.\n\n");
        report.Append("Namespace validity below uses the supplied context; an explicitly deferred fragment can be well-formed without being namespace-complete alone.\n\n");
        report.Append("| Case | Jar output | BCL output | Jar namespace-valid in context | BCL namespace-valid in context | Jar payload/node contract | BCL payload/node contract | C14N equal |\n|---|---|---|---|---|---|---|---|\n");
        foreach (Evidence item in evidence)
        {
            report.Append('|').Append(item.Input.Id).Append('|')
                .Append(Cell(item.Reference.Error ?? item.Reference.Value ?? "SQL NULL")).Append('|')
                .Append(Cell(item.Bcl.Error ?? item.Bcl.Value ?? "SQL NULL")).Append('|')
                .Append(Flag(item.ReferenceInspection.Valid)).Append('|').Append(Flag(item.BclInspection.Valid)).Append('|')
                .Append(Flag(item.ReferenceInspection.PayloadAndNodeContract)).Append('|')
                .Append(Flag(item.BclInspection.PayloadAndNodeContract)).Append('|')
                .Append(Flag(item.CanonicallyEquivalent)).Append("|\n");
        }
        report.Append("\n## Canonicalization controls\n\n| Control | Expected equal | Actual equal | Meaning |\n|---|---|---|---|\n");
        foreach (Control control in controls)
            report.Append('|').Append(control.Name).Append('|').Append(control.ExpectedEqual).Append('|')
                .Append(control.ActualEqual).Append('|').Append(control.Explanation).Append("|\n");
        report.Append("\n## Approved XMLCOMMENT checks (D-012)\n\n");
        report.Append("The original strict payload comparisons above remain unchanged. Approved comment repairs and XML 1.0 line endings have separate exact node/body expectations.\n\n");
        report.Append("| Check | Fragment | Expected Comment body | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|---|\n");
        foreach (CommentPolicyCheck check in commentChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Fragment)).Append('|')
                .Append(Cell(check.ExpectedBody)).Append('|').Append(check.ExpectedAccepted).Append('|')
                .Append(check.ActualAccepted).Append('|').Append(check.Passed).Append("|\n");
        report.Append("\n## Approved XML 1.0 line-ending checks (D-013)\n\n");
        report.Append("XmlReader normalizes literal CRLF/CR to LF. Fixed expected bodies retain spaces, TAB, LF and NEL; Comment/CDATA kinds are checked independently. No custom normalizer or raw-fragment rewrite.\n\n");
        report.Append("| Check | Function | Fragment | Expected parsed body | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|---|---|\n");
        foreach (LineEndingPolicyCheck check in lineEndingChecks)
            report.Append('|').Append(check.Name).Append('|').Append(check.Function).Append('|')
                .Append(Cell(check.Fragment)).Append('|').Append(Cell(check.ExpectedBody)).Append('|')
                .Append(check.ExpectedAccepted).Append('|').Append(check.ActualAccepted).Append('|')
                .Append(check.Passed).Append("|\n");
        report.Append("\n## Approved Unicode declaration and byte-stream checks (D-018)\n\n");
        report.Append("Unicode SQL strings declare XML 1.0 without encoding/standalone. UTF-8 and UTF-16/BOM streams are decoded independently; a byte XmlWriter owns the encoding declaration. C14N does not substitute for declaration checks.\n\n");
        report.Append("| Check | XML/declaration | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|\n");
        foreach (DeclarationPolicyCheck check in declarationChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Xml)).Append('|')
                .Append(check.ExpectedAccepted).Append('|').Append(check.ActualAccepted).Append('|')
                .Append(check.Passed).Append("|\n");
        report.Append("\n## Approved XMLNODE Indent checks (D-014)\n\n");
        report.Append("Missing fourth argument and FALSE do not add indentation; TRUE invokes BCL formatting unless D-015 disables it. Existing text/LF and SQL NULL remain observable.\n\n");
        report.Append("| Check | Result | Expected XML | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|---|\n");
        foreach (IndentPolicyCheck check in indentChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Result.Error ?? check.Result.Value ?? "SQL NULL"))
                .Append('|').Append(Cell(check.ExpectedFragment ?? "SQL NULL")).Append('|')
                .Append(check.ExpectedAccepted).Append('|').Append(check.ActualAccepted).Append('|')
                .Append(check.Passed).Append("|\n");
        report.Append("\n## Approved xml:space checks (D-015)\n\n");
        report.Append("A real XML-namespace preserve attribute anywhere disables indentation of the whole result. default alone and lookalike attributes/comments do not. The expected XML literals preserve input LF/text and are independent of the XPath predicate.\n\n");
        report.Append("| Check | Result | Expected whole XML | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|---|\n");
        foreach (IndentPolicyCheck check in xmlSpaceChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Result.Error ?? check.Result.Value ?? "SQL NULL"))
                .Append('|').Append(Cell(check.ExpectedFragment)).Append('|').Append(check.ExpectedAccepted)
                .Append('|').Append(check.ActualAccepted).Append('|').Append(check.Passed).Append("|\n");
        report.Append("\n## Approved namespace checks (D-016)\n\n");
        report.Append("Fixed expected complete XML is checked with a namespace-aware reader. Deferred fragments are separately checked in actual supplied parent context; no URI is invented.\n\n");
        report.Append("| Check | Fragment | Expected XML | Parent attributes | Expected needs context | Actual needs context | Passed |\n|---|---|---|---|---|---|---|\n");
        foreach (NamespacePolicyCheck check in namespaceChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Fragment)).Append('|')
                .Append(Cell(check.ExpectedFragment)).Append('|').Append(Cell(check.ContextAttributes)).Append('|')
                .Append(Flag(check.ExpectedNeedsContext)).Append('|').Append(Flag(check.ActualNeedsContext)).Append('|')
                .Append(check.Passed).Append("|\n");
        report.Append("\n## XMLNODE fragment classification\n\n| Case | Well-formed fragment | Requires parent context | Error |\n|---|---|---|---|\n");
        foreach (FragmentDiagnostic item in fragmentDiagnostics)
            report.Append('|').Append(item.Id).Append('|').Append(Flag(item.WellFormedFragment)).Append('|')
                .Append(Flag(item.NeedsContext)).Append('|').Append(Cell(item.Error)).Append("|\n");
        report.Append("\n## Approved CDATA splitting checks (D-017)\n\n");
        report.Append("Fixed expected text and CDATA section counts are checked independently of canonicalization. The jar's fallback text is verified as text; no oracle result is rewritten.\n\n");
        report.Append("| Check | Result | Expected body | Expected sections | Expected accepted | Actual accepted | Passed |\n|---|---|---|---|---|---|---|\n");
        foreach (CDataPolicyCheck check in cdataChecks)
            report.Append('|').Append(check.Name).Append('|').Append(Cell(check.Result.Error ?? check.Result.Value ?? "SQL NULL"))
                .Append('|').Append(Cell(check.ExpectedBody ?? "SQL NULL")).Append('|').Append(check.ExpectedSections)
                .Append('|').Append(check.ExpectedAccepted).Append('|').Append(check.ActualAccepted).Append('|')
                .Append(check.Passed).Append("|\n");
        File.WriteAllText(Path.Combine(directory, "report.md"), report.ToString());
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: prototype <repo-root> <external-output-root>");
        string repo = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        if (output.StartsWith(repo.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Evidence output must be outside the workspace");
        string directory = Path.Combine(output, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        Case[] cases = Cases();
        Dictionary<string, ValueResult> reference = Reference(repo, directory, cases);
        Evidence[] evidence = cases.Select(input =>
        {
            ValueResult candidate;
            try
            {
                string? value = input.BclOverride is null ? BclXml.Evaluate(input) : input.BclOverride();
                candidate = new(value, value is null);
            }
            catch (Exception e) when (e is XmlException or ArgumentException or InvalidOperationException)
            { candidate = new(null, Error: e.GetType().Name + ": " + e.Message); }
            ValueResult original = reference[input.Id];
            Inspection oldInspection = Inspect(input, original), newInspection = Inspect(input, candidate);
            return new Evidence(input, Hex(input.Sql), original, candidate,
                original.Value is null ? null : Hex(original.Value), candidate.Value is null ? null : Hex(candidate.Value),
                oldInspection, newInspection, oldInspection.Canonical is not null && newInspection.Canonical is not null
                    ? oldInspection.Canonical == newInspection.Canonical : null);
        }).ToArray();
        List<Control> controls = Controls();
        List<CommentPolicyCheck> commentChecks = CommentPolicyChecks(evidence);
        List<LineEndingPolicyCheck> lineEndingChecks = LineEndingPolicyChecks(evidence);
        List<IndentPolicyCheck> indentChecks = IndentPolicyChecks(evidence);
        List<IndentPolicyCheck> xmlSpaceChecks = XmlSpacePolicyChecks(evidence);
        List<NamespacePolicyCheck> namespaceChecks = NamespacePolicyChecks(evidence);
        List<FragmentDiagnostic> fragmentDiagnostics = FragmentDiagnostics(evidence);
        List<CDataPolicyCheck> cdataChecks = CDataPolicyChecks(evidence);
        List<DeclarationPolicyCheck> declarationChecks = DeclarationPolicyChecks(evidence);
        string jarSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
            Path.Combine(repo, "reference/h2/h2/target/h2-2.5.259-SNAPSHOT.jar")))).ToLowerInvariant();
        Report(directory, evidence, controls, commentChecks, lineEndingChecks, indentChecks, xmlSpaceChecks,
            namespaceChecks, fragmentDiagnostics, cdataChecks, declarationChecks, jarSha);
        Console.WriteLine("Cases compared with jar: " + evidence.Length);
        Console.WriteLine("C14N controls passed: " + controls.Count(control => control.ActualEqual == control.ExpectedEqual) + "/" + controls.Count);
        Console.WriteLine("Approved XMLCOMMENT checks passed: " + commentChecks.Count(check => check.Passed) + "/" + commentChecks.Count);
        Console.WriteLine("Approved XML 1.0 line-ending checks passed: " + lineEndingChecks.Count(check => check.Passed) + "/" + lineEndingChecks.Count);
        Console.WriteLine("Approved XMLNODE Indent checks passed: " + indentChecks.Count(check => check.Passed) + "/" + indentChecks.Count);
        Console.WriteLine("Approved xml:space checks passed: " + xmlSpaceChecks.Count(check => check.Passed) + "/" + xmlSpaceChecks.Count);
        Console.WriteLine("Approved namespace checks passed: " + namespaceChecks.Count(check => check.Passed) + "/" + namespaceChecks.Count);
        Console.WriteLine("Approved CDATA splitting checks passed: " + cdataChecks.Count(check => check.Passed) + "/" + cdataChecks.Count);
        Console.WriteLine("Approved declaration/byte-stream checks passed: " + declarationChecks.Count(check => check.Passed) + "/" + declarationChecks.Count);
        Console.WriteLine("Evidence: " + directory);
        foreach (Evidence item in evidence.Where(item => item.ReferenceInspection.Valid != item.BclInspection.Valid
            || item.BclInspection.PayloadAndNodeContract == false || item.ReferenceInspection.PayloadAndNodeContract == false))
        {
            Console.WriteLine(item.Input.Id + ": jar-valid=" + Flag(item.ReferenceInspection.Valid)
                + ", bcl-valid=" + Flag(item.BclInspection.Valid) + ", jar-contract=" + Flag(item.ReferenceInspection.PayloadAndNodeContract)
                + ", bcl-contract=" + Flag(item.BclInspection.PayloadAndNodeContract));
        }
        return controls.All(control => control.ActualEqual == control.ExpectedEqual)
            && commentChecks.All(check => check.Passed) && lineEndingChecks.All(check => check.Passed)
            && indentChecks.All(check => check.Passed) && xmlSpaceChecks.All(check => check.Passed)
            && namespaceChecks.All(check => check.Passed) && fragmentDiagnostics.All(item => item.WellFormedFragment == true)
            && cdataChecks.All(check => check.Passed) && declarationChecks.All(check => check.Passed) ? 0 : 1;
    }
}
