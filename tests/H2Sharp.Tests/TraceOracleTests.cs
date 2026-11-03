/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * Trace against the reference.
 */
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using H2Sharp.Expressions;
using H2Sharp.Message;
using H2Sharp.Tests.Oracle;
using H2Sharp.Values;
using Xunit;

namespace H2Sharp.Tests;

/// <summary>
/// The trace module against the reference.
/// </summary>
/// <remarks>
/// A trace decides which lines go anywhere and then shapes the ones that do. Both halves are
/// worth the differential test: the level gate is the module's own level against the parent
/// writer unless it follows the parent, and the shapes are the SQL comment with its size
/// prefix — the format a trace file is parsed back with — and the MessageFormat subset the
/// error catalogue already pins down. The reference keeps its writer and the three
/// exception/code informers package private, so the Java side runs inside
/// <c>org.h2.message</c> and records the lines the way a trace file would see them.
/// <para>
/// The one deliberate divergence from the reference source is where the writer's exception
/// text comes from: a JVM <c>Throwable.getMessage()</c> is recorded as a .NET
/// <c>Exception.Message</c>. The recorded exceptions are built with the same text, so the
/// comparison sees the same answer.
/// </para>
/// </remarks>
[Collection(OracleCollection.Name)]
public sealed class TraceOracleTests
{
    [JavaOracleFact]
    public void TraceAgreesWithTheReference()
    {
        List<string> java = OracleRunner
            .Run([], Body(), null, null, "org.h2.message", Members())
            .ToList();
        List<string> csharp = [.. Lines()];
        Compare(java, csharp);
    }

    private static string Body() =>
        """
        {
        Recording w = new Recording();
        Trace t = new Trace(w, Trace.COMMAND);

        // A: the parent writer is off, so nothing is written and nothing is formatted.
        t.info("i1");
        t.error(new RuntimeException("boom"), "e1");
        t.error(null, "e2");
        t.debug("d1");
        t.infoSQL("INSERT INTO T VALUES (1)", "", 1, 5);
        t.infoCode("void f() {}");
        t.debugCode("void g() {}");
        dump("A", w);

        // B: the parent writer answers for errors only.
        w.enabled = TraceSystem.ERROR;
        t.info("i2");
        t.error(new RuntimeException("boom"), "e3");
        t.error(null, "e4 {0} {1}", 10, 20);
        t.error(null, "e5 {0}z", (Object) null);
        t.debug("d2");
        t.infoSQL("UPDATE T SET A=1", "", 0, 0);
        dump("B", w);

        // C: the parent writer answers for info; debug stays behind the gate.
        w.enabled = TraceSystem.INFO;
        t.info("i6");
        t.info(new RuntimeException("bang"), "i7");
        t.info("i{0} {1}", "a", "b");
        t.info("can't {0}", "x");
        t.info("q''s {0}", "y");
        t.infoSQL("INSERT INTO T VALUES (?)", " {1: 42}", 0, 0);
        t.infoSQL("SELECT 1", "", 3, 7);
        t.debug("d3");
        dump("C", w);

        // D: the parent writer answers for everything.
        w.enabled = TraceSystem.DEBUG;
        t.debug("d4");
        t.debug(new RuntimeException("ping"), "d5");
        t.debug("d{0}-{1}", 1, 2);
        t.debugCode("void h() {}");
        t.infoSQL("DELETE FROM T", "", 2, 0);
        // The bare comment: no parameters, no count, no time - the single space case.
        t.infoSQL("SELECT 1", "", 0, 0);
        t.infoCode("void k() {}");
        dump("D", w);

        // E: an own level overrides the parent; the matrix is the level arithmetic.
        Trace o = new Trace(w, Trace.JDBCX);
        for (int lvl = -1; lvl <= 4; lvl++) {
          o.setLevel(lvl);
          System.out.println("en|" + lvl + "|" + o.isInfoEnabled() + "|" + o.isDebugEnabled());
        }
        o.setLevel(TraceSystem.OFF);
        o.info("oi"); o.error(null, "oe"); o.debug("od");
        dump("E-off", w);
        o.setLevel(TraceSystem.ERROR);
        o.info("oi"); o.error(null, "oe"); o.debug("od");
        dump("E-error", w);
        o.setLevel(TraceSystem.INFO);
        o.info("oi"); o.error(null, "oe"); o.debug("od");
        dump("E-info", w);
        o.setLevel(TraceSystem.DEBUG);
        o.info("oi"); o.error(null, "oe"); o.debug("od");
        dump("E-debug", w);

        // F: a module id names the module; a bare name travels as it is.
        Trace named = new Trace(w, "custom");
        named.info("nf");
        Trace trig = new Trace(w, Trace.TRIGGER);
        trig.info("tf");
        dump("F", w);

        // G: the parameter list as the trace comment expects it.
        java.util.ArrayList<org.h2.expression.ParameterInterface> ps = new java.util.ArrayList<>();
        System.out.println("fp|" + hex(Trace.formatParams(ps)));
        ps.add(new FakeParam(false, null));
        System.out.println("fp|" + hex(Trace.formatParams(ps)));
        ps.add(new FakeParam(true, org.h2.value.ValueInteger.get(42)));
        ps.add(new FakeParam(false, null));
        ps.add(new FakeParam(true, org.h2.value.ValueVarchar.get("x,y\"")));
        System.out.println("fp|" + hex(Trace.formatParams(ps)));
        }
        """;

    private static string Members() =>
        """
        static void dump(String tag, Recording w) {
          for (String line : w.lines) {
            System.out.println(tag + "|" + line);
          }
          System.out.println(tag + "|count|" + w.lines.size());
          w.lines.clear();
        }

        static class Recording implements TraceWriter {
          final java.util.ArrayList<String> lines = new java.util.ArrayList<>();
          int enabled = TraceSystem.OFF;
          public void setName(String name) { lines.add("setName|" + name); }
          public void write(int level, int moduleId, String s, Throwable t) {
            lines.add("writeM|" + level + "|" + Trace.MODULE_NAMES[moduleId] + "|" + hex(s) + "|"
                + (t == null ? "null" : t.getMessage()));
          }
          public void write(int level, String module, String s, Throwable t) {
            lines.add("write|" + level + "|" + module + "|" + hex(s) + "|"
                + (t == null ? "null" : t.getMessage()));
          }
          public boolean isEnabled(int level) { return level <= enabled; }
        }

        static class FakeParam implements org.h2.expression.ParameterInterface {
          final boolean set;
          final org.h2.value.Value v;
          FakeParam(boolean s, org.h2.value.Value val) { set = s; v = val; }
          public void setValue(org.h2.value.Value value, boolean closeOld) { }
          public org.h2.value.Value getParamValue() { return v; }
          public void checkSet() { }
          public boolean isValueSet() { return set; }
          public org.h2.value.TypeInfo getType() {
            return v == null ? org.h2.value.TypeInfo.TYPE_UNKNOWN : v.getType();
          }
          public int getNullable() { return 2; }
        }
        """;

    private static IEnumerable<string> Lines()
    {
        RecordingTraceWriter w = new();
        Trace t = new(w, Trace.Command);

        // A
        t.Info("i1");
        t.Error(new("boom"), "e1");
        t.Error(null!, "e2");
        t.Debug("d1");
        t.InfoSql("INSERT INTO T VALUES (1)", "", 1, 5);
        t.InfoCode("void f() {}");
        t.DebugCode("void g() {}");
        foreach (string line in Dump("A", w))
        {
            yield return line;
        }

        // B
        w.Enabled = TraceLevels.Error;
        t.Info("i2");
        t.Error(new("boom"), "e3");
        t.Error(null!, "e4 {0} {1}", 10, 20);
        t.Error(null!, "e5 {0}z", (object?)null);
        t.Debug("d2");
        t.InfoSql("UPDATE T SET A=1", "", 0, 0);
        foreach (string line in Dump("B", w))
        {
            yield return line;
        }

        // C
        w.Enabled = TraceLevels.Info;
        t.Info("i6");
        t.Info(new("bang"), "i7");
        t.Info("i{0} {1}", "a", "b");
        t.Info("can't {0}", "x");
        t.Info("q''s {0}", "y");
        t.InfoSql("INSERT INTO T VALUES (?)", " {1: 42}", 0, 0);
        t.InfoSql("SELECT 1", "", 3, 7);
        t.Debug("d3");
        foreach (string line in Dump("C", w))
        {
            yield return line;
        }

        // D
        w.Enabled = TraceLevels.Debug;
        t.Debug("d4");
        t.Debug(new("ping"), "d5");
        t.Debug("d{0}-{1}", 1, 2);
        t.DebugCode("void h() {}");
        t.InfoSql("DELETE FROM T", "", 2, 0);
        // The bare comment: no parameters, no count, no time - the single space case.
        t.InfoSql("SELECT 1", "", 0, 0);
        t.InfoCode("void k() {}");
        foreach (string line in Dump("D", w))
        {
            yield return line;
        }

        // E
        Trace o = new(w, Trace.Jdbcx);
        for (int lvl = -1; lvl <= 4; lvl++)
        {
            o.SetLevel(lvl);
            yield return "en|" + lvl + "|" + J(o.IsInfoEnabled()) + "|" + J(o.IsDebugEnabled());
        }

        o.SetLevel(TraceLevels.Off);
        t_Matrix(o, w);
        foreach (string line in Dump("E-off", w))
        {
            yield return line;
        }

        o.SetLevel(TraceLevels.Error);
        t_Matrix(o, w);
        foreach (string line in Dump("E-error", w))
        {
            yield return line;
        }

        o.SetLevel(TraceLevels.Info);
        t_Matrix(o, w);
        foreach (string line in Dump("E-info", w))
        {
            yield return line;
        }

        o.SetLevel(TraceLevels.Debug);
        t_Matrix(o, w);
        foreach (string line in Dump("E-debug", w))
        {
            yield return line;
        }

        // F
        Trace named = new(w, "custom");
        named.Info("nf");
        Trace trig = new(w, Trace.Trigger);
        trig.Info("tf");
        foreach (string line in Dump("F", w))
        {
            yield return line;
        }

        // G
        List<ParameterInterface> ps = [];
        yield return "fp|" + OracleRunner.Hex(Trace.FormatParams(ps));
        ps.Add(new FakeParam(false, null));
        yield return "fp|" + OracleRunner.Hex(Trace.FormatParams(ps));
        ps.Add(new FakeParam(true, ValueInteger.Get(42)));
        ps.Add(new FakeParam(false, null));
        ps.Add(new FakeParam(true, ValueVarchar.Get("x,y\"")));
        yield return "fp|" + OracleRunner.Hex(Trace.FormatParams(ps));
    }

    /// <summary>
    /// The one write of each kind through a module with its own level set.
    /// </summary>
    private static void t_Matrix(Trace o, RecordingTraceWriter w)
    {
        o.Info("oi");
        o.Error(null!, "oe");
        o.Debug("od");
    }

    private static IEnumerable<string> Dump(string tag, RecordingTraceWriter w)
    {
        foreach (string line in w.Lines)
        {
            yield return tag + "|" + line;
        }

        yield return tag + "|count|" + w.Lines.Count.ToString(CultureInfo.InvariantCulture);
        w.Lines.Clear();
    }

    /// <summary>
    /// The recorded writer, mirroring the Java one line for line: level, module, the line in
    /// hex so the embedded line separators and braces survive, and the exception text.
    /// </summary>
    private sealed class RecordingTraceWriter : TraceWriter
    {
        internal List<string> Lines { get; } = [];

        internal int Enabled { get; set; } = TraceLevels.Off;

        public void SetName(string name) => Lines.Add("setName|" + name);

        public void Write(int level, string module, string s, Exception? t) => Lines.Add(
            "write|" + level + "|" + module + "|" + OracleRunner.Hex(s) + "|"
            + (t is null ? "null" : t.Message));

        public void Write(int level, int moduleId, string s, Exception? t) => Lines.Add(
            "writeM|" + level + "|" + Trace.ModuleNames[moduleId] + "|" + OracleRunner.Hex(s) + "|"
            + (t is null ? "null" : t.Message));

        public bool IsEnabled(int level) => level <= Enabled;
    }

    private sealed class FakeParam : ParameterInterface
    {
        private readonly bool set;
        private readonly Value? v;

        internal FakeParam(bool set, Value? v)
        {
            this.set = set;
            this.v = v;
        }

        public void SetValue(Value value, bool closeOld)
        {
        }

        public Value GetParamValue() => v!;

        public void CheckSet()
        {
        }

        public bool IsValueSet() => set;

        public TypeInfo GetTypeInfo()
            => v is null ? TypeInfo.GetTypeInfo(Value.Unknown) : v.GetTypeInfo();

        public int GetNullable() => 2;
    }

    private static string J(bool value) => value ? "true" : "false";

    private static void Compare(List<string> java, List<string> csharp)
    {
        int common = Math.Min(java.Count, csharp.Count);
        for (int i = 0; i < common; i++)
        {
            Assert.True(java[i] == csharp[i],
                $"line {i}:\n  java:   {java[i]}\n  csharp: {csharp[i]}");
        }

        Assert.True(java.Count == csharp.Count,
            "строк: java=" + java.Count.ToString(CultureInfo.InvariantCulture)
            + " csharp=" + csharp.Count.ToString(CultureInfo.InvariantCulture)
            + "\n  последнее совпадение: " + (common - 1).ToString(CultureInfo.InvariantCulture));
    }
}
