using System;

using NUnit.Framework;

using ULogger;

/// <summary>
/// The allocation-free "{0}" formatter and its growable char scratch. Pure C#, no Unity.
/// </summary>
public class LogFormatterTests {
    static string Format(string format) {
        var b = LogFormatter.Scratch;
        LogFormatter.Format(b, format);
        return new string(b.Span);
    }

    static string Format<T0>(string format, T0 a0) {
        var b = LogFormatter.Scratch;
        LogFormatter.Format(b, format, a0);
        return new string(b.Span);
    }

    static string Format<T0, T1>(string format, T0 a0, T1 a1) {
        var b = LogFormatter.Scratch;
        LogFormatter.Format(b, format, a0, a1);
        return new string(b.Span);
    }

    static string Value<T>(T value) => Format("{0}", value);

    // ------------------------------------------------------------------ placeholders

    [Test]
    public void SubstitutesByIndex() {
        Assert.AreEqual("a1b", Format("a{0}b", 1));
        Assert.AreEqual("1 2", Format("{0} {1}", 1, 2));
        Assert.AreEqual("2 1", Format("{1} {0}", 1, 2));
    }

    [Test]
    public void RepeatsAnArgumentUsedTwice() {
        Assert.AreEqual("7-7", Format("{0}-{0}", 7));
    }

    [Test]
    public void KeepsPlaceholdersPastTheSuppliedArguments() {
        // string.Format would throw; a logger must not take the process down over a bad format.
        Assert.AreEqual("{3}", Format("{3}", 1));
        Assert.AreEqual("1 {1}", Format("{0} {1}", 1));
    }

    [Test]
    public void HandlesEscapedBraces() {
        Assert.AreEqual("{", Format("{{"));
        Assert.AreEqual("}", Format("}}"));
        Assert.AreEqual("{0}", Format("{{0}}"));
        Assert.AreEqual("{1}", Format("{{{0}}}", 1));
    }

    [Test]
    public void CopiesMalformedFormatsVerbatim() {
        Assert.AreEqual("{", Format("{"));
        Assert.AreEqual("}", Format("}"));
        Assert.AreEqual("{a}", Format("{a}"));
        // Only single-digit indices are recognized; the rest is literal text.
        Assert.AreEqual("{10}", Format("{10}"));
    }

    [Test]
    public void EmptyFormatProducesNothing() {
        Assert.AreEqual(string.Empty, Format(string.Empty));
    }

    // ------------------------------------------------------------------ values

    [Test]
    public void FormatsSignedIntegers() {
        Assert.AreEqual("0", Value(0));
        Assert.AreEqual("-1", Value(-1));
        Assert.AreEqual(int.MinValue.ToString(), Value(int.MinValue));
        Assert.AreEqual(long.MinValue.ToString(), Value(long.MinValue));
        Assert.AreEqual("-32768", Value(short.MinValue));
        Assert.AreEqual("-128", Value(sbyte.MinValue));
    }

    [Test]
    public void FormatsUnsignedIntegers() {
        Assert.AreEqual(uint.MaxValue.ToString(), Value(uint.MaxValue));
        Assert.AreEqual(ulong.MaxValue.ToString(), Value(ulong.MaxValue));
        Assert.AreEqual("65535", Value(ushort.MaxValue));
        Assert.AreEqual("255", Value(byte.MaxValue));
    }

    [Test]
    public void FormatsFloatAtFloatPrecision() {
        // Widening to double first turned 0.1f into 0.10000000149011612.
        Assert.AreEqual("0.1", Value(0.1f));
        Assert.AreEqual("-2.5", Value(-2.5f));
    }

    [Test]
    public void FormatsDoubleAndDecimal() {
        Assert.AreEqual("0.1", Value(0.1d));
        Assert.AreEqual("-2.5", Value(-2.5d));
        Assert.AreEqual("1.25", Value(1.25m));
        Assert.AreEqual(decimal.MinValue.ToString(), Value(decimal.MinValue));
    }

    [Test]
    public void FormatsBoolAndChar() {
        Assert.AreEqual("True", Value(true));
        Assert.AreEqual("False", Value(false));
        Assert.AreEqual("x", Value('x'));
    }

    [Test]
    public void FormatsStringsAndNull() {
        Assert.AreEqual("text", Value("text"));
        Assert.AreEqual(string.Empty, Value((string)null));
        Assert.AreEqual("a  b", Format("a {0} b", (string)null));
    }

    [Test]
    public void FormatsEnumsByName() {
        // The ordinal was unreadable in a log line: "3" instead of "Warning".
        Assert.AreEqual("Warning", Value(LogLevel.Warning));
        Assert.AreEqual("Off", Value(LogLevel.Off));
    }

    [Test]
    public void SurvivesAToStringReturningNull() {
        Assert.AreEqual(string.Empty, Value(new NullToString()));
    }

    sealed class NullToString {
        public override string ToString() => null;
    }

    [Test]
    public void UsesToStringForUnknownTypes() {
        Assert.AreEqual("custom", Value(new CustomToString()));
    }

    sealed class CustomToString {
        public override string ToString() => "custom";
    }

    // ------------------------------------------------------------------ CharBuffer

    [Test]
    public void ScratchIsResetBetweenEntries() {
        Assert.AreEqual("first", Format("first"));
        Assert.AreEqual("second", Format("second"));
    }

    [Test]
    public void GrowsBeyondTheInitialCapacity() {
        var long1 = new string('a', 5000);
        var long2 = new string('b', 5000);

        // Two 5000-char arguments blow well past the 1024-char starting buffer.
        Assert.AreEqual(long1 + long2, Format("{0}{1}", long1, long2));
    }

    [Test]
    public void ShrinksAfterAnOversizedEntry() {
        var huge = new string('x', 100 * 1024);
        Assert.AreEqual(huge.Length, Format("{0}", huge).Length);

        // Reset must hand back a buffer capped at MaxRetained rather than keeping 100 KB alive.
        var b = LogFormatter.Scratch;
        Assert.LessOrEqual(b.Data.Length, 64 * 1024);
    }

    [Test]
    public void EnsureGrowsInPowersOfTwo() {
        var buffer = new CharBuffer(16);
        buffer.Ensure(100);
        Assert.AreEqual(128, buffer.Data.Length);
    }

    [Test]
    public void AppendTracksLength() {
        var buffer = new CharBuffer(4);
        buffer.Append('a');
        buffer.Append("bcde".AsSpan());
        Assert.AreEqual("abcde", new string(buffer.Span));
        Assert.AreEqual(5, buffer.Length);
    }
}
