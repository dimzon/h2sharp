/*
 * Copyright 2026 H2Sharp contributors.
 * Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
 *
 * The three escaping helpers of StringUtils on long inputs: a plain run and a run broken
 * by the characters each helper exists to escape. Issue #84.
 */
using System;
using System.Text;
using BenchmarkDotNet.Attributes;
using H2Sharp.Util;

namespace H2Sharp.Benchmarks;

public class StringEscapeBench
{
    [Params(1024, 65536)]
    public int Length { get; set; }

    private string plain = null!;

    private string javaEscaped = null!;

    private string xmlEscaped = null!;

    private string noSeparators = null!;

    private string manySeparators = null!;

    [GlobalSetup]
    public void Setup()
    {
        StringBuilder plainBuilder = new(Length);
        StringBuilder javaBuilder = new(Length);
        StringBuilder xmlBuilder = new(Length);
        StringBuilder noSeparatorBuilder = new(Length);
        StringBuilder manySeparatorBuilder = new(Length);
        for (int i = 0; i < Length; i++)
        {
            plainBuilder.Append((char)('a' + (i % 26)));
            javaBuilder.Append(i % 16 == 0 ? '"' : (char)('a' + (i % 26)));
            xmlBuilder.Append(i % 16 == 0 ? '<' : (char)('a' + (i % 26)));
            noSeparatorBuilder.Append((char)('a' + (i % 26)));
            manySeparatorBuilder.Append(i % 5 == 0 ? ',' : (char)('a' + (i % 26)));
        }

        plain = plainBuilder.ToString();
        javaEscaped = javaBuilder.ToString();
        xmlEscaped = xmlBuilder.ToString();
        noSeparators = noSeparatorBuilder.ToString();
        manySeparators = manySeparatorBuilder.ToString();
    }

    [Benchmark]
    public string JavaEncodePlain() => StringUtils.JavaEncode(plain);

    [Benchmark]
    public string JavaEncodeEscaped() => StringUtils.JavaEncode(javaEscaped);

    [Benchmark]
    public string XmlTextPlain() => StringUtils.XmlText(plain);

    [Benchmark]
    public string XmlTextEscaped() => StringUtils.XmlText(xmlEscaped);

    [Benchmark]
    public string[] ArraySplitNoSeparators() => StringUtils.ArraySplit(noSeparators, ',', trim: false)!;

    [Benchmark]
    public string[] ArraySplitManySeparators() => StringUtils.ArraySplit(manySeparators, ',', trim: false)!;
}
