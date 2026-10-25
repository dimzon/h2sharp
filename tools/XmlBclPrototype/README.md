# BCL XML prototype

Standalone experiment, not an engine implementation and not part of the test suites.
It compares six XML constructors with the pinned H2 jar, validates their XML with
System.Xml, and evaluates C14N **with comments** using Microsoft's
System.Security.Cryptography.Xml. The prototype uses the installed
Microsoft.AspNetCore.App shared framework solely to expose that transform; the
engine's dependencies are unchanged.

Run from the repository root (output directory must be outside the workspace):

```powershell
dotnet run --project tools/XmlBclPrototype/XmlBclPrototype.csproj -- "C:\Work\h2sharp" "C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025"
```

Outputs: reference SQL/UTF-16 transport, jar output, JSON evidence and Markdown
report. No regression test or SQL-function expectation is changed.

The experiment preserves whitespace. Formatting differences are distinguished
from invalid XML, changed text/attribute values, and missing Comment/CDATA/
XmlDeclaration nodes. Canonicalization alone is deliberately tested against
negative controls that could otherwise erase those guarantees.

XMLCOMMENT repairs are approved as D-012. Separate checks assert exact BCL
comment bodies and the jar's exact formatting; negative controls reject wrong
content, missing Comment nodes, trimmed input spaces and extra comments. The
original strict comparisons are retained.

XML 1.0 line-ending normalization for Comment/CDATA is approved as D-013.
Separate checks require CRLF/CR to read as LF, preserving spaces, TAB, LF and
NEL. Wrong content and lost node kinds fail even if canonicalization hides the
difference. SQL fragments are not rewritten by a custom normalizer.

XMLNODE Indent is approved as D-014: missing argument and FALSE disable
formatting; explicit TRUE invokes the BCL formatter. Separate controls preserve
existing input LF/text and SQL NULL and compare the reference's exact tree
independently.

xml:space priority is approved as D-015. A preserve attribute in the XML
namespace on the root or any descendant disables Indent for the whole result,
including explicit TRUE. BCL XPath finds the directive. default alone, ordinary
space attributes, other namespaces and comment text do not disable formatting.

Deferred namespace context is approved as D-016. BCL XmlTextReader validates
fragment structure, XmlConvert validates QName components, and XmlNamespaceManager
tracks actual bindings/scopes. Locally supplied namespace declarations are checked
by the namespace-aware BCL writer. Complete contexts use the namespace-aware
reader/writer; deferred fragments retain their QName using BCL's namespace-disabled
writer. No URI is fabricated. The report distinguishes well-formed fragments that
require context from independently namespace-valid XML.

Namespace controls compare fixed expected complete trees, including prefix rebinding,
default namespace/reset, siblings, deferred attributes/text and xml:space. Negative
controls require malformed names, illegal bindings, expanded-name duplicates and
unsatisfied parent contexts to fail. Original strict comparisons remain visible.

CDATA splitting is approved as D-017. WriteCData retains CDATA sections for
embedded ]]>; exact parsed text and section counts are checked for leading,
trailing, repeated/adjacent boundaries, whitespace, Unicode, EOL and composition
with XMLNODE/deferred namespace. Plain text substituted for CDATA is rejected by
the policy checks even when C14N considers the contents equal.

The Unicode declaration is approved as D-018: XMLSTARTDOC returns XML 1.0 with
no encoding/standalone, using BCL's declaration node serializer. Separate checks
validate the declaration and composition, UTF-8/UTF-16 byte streams, BOM and a
byte XmlWriter's encoding declaration. An encoding mismatch in a byte stream
must fail; C14N does not validate these properties.
