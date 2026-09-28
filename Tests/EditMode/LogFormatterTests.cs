using System;
using System.Globalization;

using NUnit.Framework;

using ULogger;

using UnityEngine;
using UnityEngine.TestTools.Constraints;

using Is = UnityEngine.TestTools.Constraints.Is;

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

    static string Format<T0, T1, T2, T3>(string format, T0 a0, T1 a1, T2 a2, T3 a3) {
        var b = LogFormatter.Scratch;
        LogFormatter.Format(b, format, a0, a1, a2, a3);
        return new string(b.Span);
    }

    static string Value<T>(T value) => Format("{0}", value);

    static string Spec<T>(T value, string spec) => Format("{0:" + spec + "}", value);

    static string Invariant(string format, object value) => string.Format(CultureInfo.InvariantCulture, format, value);

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

    // ------------------------------------------------------------------ format specifiers

    [Test]
    public void AppliesStandardSpecsToFloatingPoint() {
        Assert.AreEqual(Invariant("{0:F3}", 1.23456f), Spec(1.23456f, "F3"));
        Assert.AreEqual(Invariant("{0:F3}", 1.23456d), Spec(1.23456d, "F3"));
        Assert.AreEqual(Invariant("{0:F3}", 1.23456m), Spec(1.23456m, "F3"));
        Assert.AreEqual("0.125", Spec(0.125f, "F3"));
        Assert.AreEqual(Invariant("{0:P2}", 0.125d), Spec(0.125d, "P2"));
    }

    [Test]
    public void AppliesCustomSpecs() {
        Assert.AreEqual("+1.5", Spec(1.5f, "+0.0;-0.0"));
        Assert.AreEqual("-1.5", Spec(-1.5f, "+0.0;-0.0"));
        Assert.AreEqual(Invariant("{0:+0.0;-0.0}", 0f), Spec(0f, "+0.0;-0.0"));
        Assert.AreEqual("+0.250", Spec(0.25d, "+0.000;-0.000"));
    }

    [Test]
    public void AppliesSpecsToIntegers() {
        Assert.AreEqual("00042", Spec(42, "D5"));
        Assert.AreEqual("1,234,567", Spec(1234567, "N0"));
        Assert.AreEqual("FF", Spec(255, "X"));
        Assert.AreEqual("ff", Spec((byte)255, "x"));
        // Each type is formatted as itself: widened to long, -1 would print sixteen F's.
        Assert.AreEqual("FFFFFFFF", Spec(-1, "X"));
        Assert.AreEqual("FFFF", Spec((short)-1, "X"));
        Assert.AreEqual(Invariant("{0:N0}", ulong.MaxValue), Spec(ulong.MaxValue, "N0"));
    }

    [Test]
    public void GrowsForSpecsLongerThanTheOldFixedReserve() {
        // long.MaxValue under N0 is 25 chars, past the 20 WriteInt used to reserve.
        var buffer = new CharBuffer(4);
        buffer.Append(long.MaxValue, "N0");
        Assert.AreEqual(long.MaxValue.ToString("N0", CultureInfo.InvariantCulture), new string(buffer.Span));

        buffer = new CharBuffer(4);
        buffer.Append(7, "D40");
        Assert.AreEqual(new string('0', 39) + "7", new string(buffer.Span));
    }

    [Test]
    public void IgnoresSpecsOnTypesWithoutFormatting() {
        Assert.AreEqual("text", Spec("text", "F3"));
        Assert.AreEqual("True", Spec(true, "F3"));
        Assert.AreEqual("x", Spec('x', "F3"));
        Assert.AreEqual("Warning", Spec(LogLevel.Warning, "D"));
        Assert.AreEqual(string.Empty, Spec((string)null, "F3"));
    }

    [Test]
    public void PassesSpecsToFormattableTypes() {
        Assert.AreEqual("(1.0, 2.0, 3.0)", Spec(new Vector3(1f, 2f, 3f), "F1"));
        Assert.AreEqual("2026-09-28", Spec(new DateTime(2026, 9, 28), "yyyy-MM-dd"));
        Assert.AreEqual("01:02:03", Spec(new TimeSpan(1, 2, 3), "c"));
    }

    [Test]
    public void FallsBackToThePlainValueOnAnInvalidSpec() {
        // string.Format would throw FormatException; a logger must not.
        Assert.AreEqual("42", Spec(42, "Q"));
        Assert.AreEqual("1.5", Spec(1.5f, "Q"));
    }

    [Test]
    public void TreatsAnEmptySpecAsNone() {
        Assert.AreEqual("0.5", Spec(0.5f, string.Empty));
    }

    [Test]
    public void CopiesMalformedSpecsVerbatim() {
        Assert.AreEqual("{0:", Format("{0:", 1));
        Assert.AreEqual("{0:F3", Format("{0:F3", 1));
        Assert.AreEqual("{a:F3}", Format("{a:F3}", 1));
        // A '{' before the closing brace means the brace is missing, not part of the spec.
        Assert.AreEqual("{0:F3 1", Format("{0:F3 {0}", 1));
    }

    [Test]
    public void KeepsTheSpecOfAPlaceholderPastTheSuppliedArguments() {
        Assert.AreEqual("1 {1:F2}", Format("{0} {1:F2}", 1));
        Assert.AreEqual("{3:+0.0;-0.0}", Format("{3:+0.0;-0.0}"));
    }

    [Test]
    public void MixesPlaceholdersWithAndWithoutSpecs() {
        Assert.AreEqual("t=1.235 depth=+2.5mm tip=3 step=-0.010mm",
            Format("t={0:F3} depth={1:+0.0;-0.0}mm tip={2} step={3:+0.000;-0.000}mm", 1.23456f, 2.5f, 3, -0.01f));
    }

    // ------------------------------------------------------------------ culture

    [Test]
    public void FormatsInTheInvariantCultureWhateverTheCurrentOne() {
        var previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            Assert.AreEqual("0.5", Value(0.5f));
            Assert.AreEqual("0.5", Value(0.5d));
            Assert.AreEqual("0.5", Value(0.5m));
            Assert.AreEqual("1,234.5", Spec(1234.5d, "N1"));
            Assert.AreEqual("09/28/2026 00:00:00", Value(new DateTime(2026, 9, 28)));
        } finally {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ------------------------------------------------------------------ CharBuffer.Append chain

    [Test]
    public void AppendChainsValuesAndSpecs() {
        var b = LogFormatter.Scratch;
        b.Append("t=").Append(1.5f, "F2").Append(' ').Append(3).Append(" ").Append(LogLevel.Info).Append((string)null);
        Assert.AreEqual("t=1.50 3 Info", new string(b.Span));
    }

    // ------------------------------------------------------------------ allocations

    [Test]
    public void FormatsNumbersWithoutAllocating() {
        var b = new CharBuffer(256);
        void Run() {
            b.Length = 0;
            LogFormatter.Format(b, "{0} {1} {2} {3}", 1.2345f, 42, 0.5d, 7L);
        }

        Run();
        Assert.That(new TestDelegate(Run), Is.Not.AllocatingGCMemory());
    }

    [Test]
    public void FormatsNumbersWithSpecsWithoutAllocating() {
        var b = new CharBuffer(256);
        void Run() {
            b.Length = 0;
            LogFormatter.Format(b, "{0:F3} {1:D5} {2:+0.0;-0.0} {3:X}", 1.2345f, 42, -0.5d, 255);
        }

        Run();
        Assert.That(new TestDelegate(Run), Is.Not.AllocatingGCMemory());
    }
}
