using System;
using System.Collections.Generic;

using ULogger;

using UnityEngine;

/// <summary>
/// A destination that records what reached it. Used to test <see cref="ULogHandler"/>'s own
/// contract -- filtering, deduplication, tag matching -- without dragging a real sink in.
/// </summary>
public class RecordingLogHandler : ULogHandler {
    // The base class has no level field of its own -- each sink declares one -- so the fake needs
    // one too, otherwise there is nothing for IsEnabledInherit to compare against.
    public LogLevel MinLevel = LogLevel.Trace;

    public readonly List<string> Writes = new();
    public readonly List<string> Formats = new();
    public readonly List<Exception> Exceptions = new();

    public int Total => Writes.Count + Formats.Count + Exceptions.Count;

    public void Clear() {
        Writes.Clear();
        Formats.Clear();
        Exceptions.Clear();
    }

    protected override bool IsEnabledInherit(LogLevel level) => level >= MinLevel;

    protected override void WriteInherit(LogLevel level, ReadOnlySpan<char> tag, ReadOnlySpan<char> message, UnityEngine.Object context) {
        Writes.Add(tag.Length > 0 ? $"[{tag.ToString()}] {message.ToString()}" : message.ToString());
    }

    protected override void LogExceptionInherit(Exception exception, UnityEngine.Object context) {
        Exceptions.Add(exception);
    }

    protected override bool LogFormatInherit(LogType logType, UnityEngine.Object context, string format, params object[] args) {
        Formats.Add(args.Length == 0 ? format : string.Format(format, args));
        return true;
    }
}

/// <summary>A second recorder type, so two instances can have distinct dedup scopes.</summary>
public class OtherRecordingLogHandler : RecordingLogHandler { }
