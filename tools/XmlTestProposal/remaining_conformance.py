"""Summarize the latest SQL conformance TRX only."""
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

path = Path(r"C:\Users\User\AppData\Local\Temp\opencode\h2-xml-bcl-prototype-20261025\xml-test-proposal\regex-specs-full.trx")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
rows = ET.parse(path).getroot().findall("t:Results/t:UnitTestResult", ns)
failed = [row.attrib["testName"] for row in rows if row.attrib["outcome"] == "Failed"]
print("SQL conformance failed: " + str(len(failed)))
groups = Counter(name.rsplit(".", 1)[0] for name in failed)
for name, count in groups.most_common(): print(str(count) + " " + name)
print("Remaining function tests:")
for name in sorted(failed):
    if ".Functions." in name: print(name)
