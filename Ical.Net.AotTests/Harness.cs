//
// Copyright ical.net project maintainers and contributors.
// Licensed under the MIT license.
//

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ical.Net.AotTests;

/// <summary>
/// A minimal assertion harness. Deliberately not NUnit: VSTest and NUnit3TestAdapter are not
/// AOT-compatible, so this project is a plain Exe with hand-rolled assertions.
/// </summary>
/// <remarks>
/// Every fixture writes its observations to stdout as stable <c>key = value</c> lines. The gate
/// runs the same binary under the JIT and under NativeAOT and diffs the two transcripts, so a
/// divergence is caught even when nobody thought to write the matching assertion. That is how the
/// UID corruption this work started from was found.
/// </remarks>
internal static class Harness
{
    private static int _failures;
    private static string _section = string.Empty;

    internal static void Section(string name)
    {
        _section = name;
        Console.WriteLine();
        Console.WriteLine($"## {name}");
    }

    /// <summary>Records an observed value in the transcript that the JIT/AOT diff compares.</summary>
    internal static void Report(string key, object? value)
        => Console.WriteLine($"{key} = {Format(value)}");

    internal static void Check(string what, object? actual, object? expected)
    {
        var a = Format(actual);
        var e = Format(expected);
        Console.WriteLine($"{what} = {a}");
        if (!string.Equals(a, e, StringComparison.Ordinal))
        {
            _failures++;
            Console.Error.WriteLine($"FAIL [{_section}] {what}: expected <{e}> but was <{a}>");
        }
    }

    internal static void Fail(string what, string detail)
    {
        _failures++;
        Console.Error.WriteLine($"FAIL [{_section}] {what}: {detail}");
    }

    /// <summary>
    /// Runs a fixture, turning an unhandled exception into a recorded failure so that one broken
    /// fixture does not hide the rest - and so the transcript still diffs cleanly.
    /// </summary>
    internal static void Run(string name, Action fixture)
    {
        Section(name);
        try
        {
            fixture();
        }
        catch (Exception ex)
        {
            Fail(name, $"threw {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine($"{name}.exception = {ex.GetType().FullName}");
        }
    }

    internal static int Complete()
    {
        Console.WriteLine();
        Console.WriteLine($"## failures = {_failures}");
        return _failures == 0 ? 0 : 1;
    }

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable<string> items => string.Join(",", items),
        _ => value.ToString() ?? "<null>",
    };
}
