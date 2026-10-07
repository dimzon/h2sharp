/*
 * Copyright 2026 H2Sharp contributors.
 * Ported from none (new file for H2Sharp).
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Dumps, for every BMP code point whose Java simple case mapping is not the
 * identity, the simple and the full (Locale.ENGLISH) upper and lower results.
 * tools/gen_case_folding.py diffs this against the .NET invariant mapping and emits
 * src/H2Sharp/Util/CaseFolding.cs from the differences.
 *
 * Usage:
 *   javac -d tools/out tools/DumpCaseTable.java
 *   java -cp tools/out DumpCaseTable > case-java.tsv
 */
public class DumpCaseTable {
    public static void main(String[] args) {
        StringBuilder sb = new StringBuilder();
        for (int cp = 0; cp <= 0x10FFFF; cp++) {
            if (cp >= 0xD800 && cp <= 0xDFFF) {
                continue;
            }

            String s = new String(Character.toChars(cp));
            String fullUpper = s.toUpperCase(java.util.Locale.ENGLISH);
            String fullLower = s.toLowerCase(java.util.Locale.ENGLISH);
            String simpleUpper = simple(cp, true);
            String simpleLower = simple(cp, false);

            // A code point matters when either
            //   (a) Java's full mapping differs from its simple mapping (ß -> "SS"), or
            //   (b) Java's simple mapping is not the identity (ı -> I), because .NET
            //       may disagree with it even though full and simple agree.
            // Filtering on only one of the two misses half the differences.
            if (fullUpper.equals(simpleUpper)
                    && fullLower.equals(simpleLower)
                    && simpleUpper.equals(s)
                    && simpleLower.equals(s)) {
                continue;
            }

            sb.append(Integer.toHexString(cp)).append('\t')
                    .append(hexOf(fullUpper)).append('\t')
                    .append(hexOf(fullLower)).append('\n');
        }
        System.out.print(sb);
    }

    static String simple(int cp, boolean up) {
        int m = up ? Character.toUpperCase(cp) : Character.toLowerCase(cp);
        return new String(Character.toChars(m));
    }

    static String hexOf(String s) {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < s.length();) {
            int c = s.codePointAt(i);
            if (b.length() > 0) {
                b.append('+');
            }
            b.append(Integer.toHexString(c));
            i += Character.charCount(c);
        }
        return b.toString();
    }
}