# ULogger

Structured logging for Unity: tag filtering, file output, several destinations at once, and an allocation-free API for hot paths. Every handler can be called from any thread.

## Requirements

Unity **2021.2** or newer — the package uses C# 9 and .NET Standard 2.1.

## Installation

Use **Package Manager** → **+** → **Install Package from git URL → https://github.com/iAcolyte/ULogger.git**.

## Initialization

> **Important:** This package replaces Unity's default log handling mechanism.

The package provides three log handler implementations:

- `ConsoleLogHandler` — outputs logs to the Unity Console.
- `FileLogHandler` — writes logs to a text file.
- `CompositeLogHandler` — does not output logs itself but forwards them to other attached handlers.

Choose the handlers you need and create ScriptableObject assets for them (e.g. **Create → ULogger → Console Log**). Place the handler you want to install in a `Resources` folder so it can be loaded at startup.

Then install it as early as possible via `RuntimeInitializeOnLoadMethod`, so the handler is active before any game code logs:

```csharp
using UnityEngine;

namespace ULogger
{
    public static class ULoggerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var handler = Resources.Load<ULogHandler>("CompositeLH");
            if (handler != null) Debug.unityLogger.logHandler = handler;
        }
    }
}
```

> **Important:** Install in `BeforeSceneLoad` (or any phase after `SubsystemRegistration`), **not** in `SubsystemRegistration`. `ConsoleLogHandler` captures Unity's original handler during `SubsystemRegistration`, and the order of methods within the same phase is undefined — installing in a later phase guarantees the capture happens first, so console output is not lost.

The `MonoLogger` sample ships exactly this bootstrap together with a ready-made handler chain; import it from the Package Manager to see the output of every path described below.

If you prefer to scope logging to a specific scene, a `MonoBehaviour` that swaps `Debug.unityLogger.logHandler` in `OnEnable` and restores it in `OnDisable` works too — just remember that restoring it on `OnDisable` also undoes any handler installed at startup.

## Usage

You can use `Debug.Log*` as usual. However, the package makes heavy use of **tags**. To leverage them, use `Debug.unityLogger.Log("TAG", "Message")`.

> **Note:** Tag detection relies on Unity formatting `Logger.Log(tag, message)` calls with the internal `"{0}: {1}"` format string. A call counts as tagged only in exactly that shape — that format, two arguments, a string first. Plain `Debug.Log` and `Debug.LogFormat` calls are always untagged, and their format strings are kept as written.

### Tag Filtering

Every `ULogHandler` has a `tags` field (configured in the Inspector). When the array is **not empty**, the handler only processes tagged messages whose tag is listed in that array; untagged messages are dropped. When the array is **empty**, the handler processes all messages. Exceptions are never tag-filtered.

### Allocation-free API

Every handler also implements `ILogSink`, which formats its arguments without boxing them and without building an `object[]`. The level check runs before any formatting, so a disabled level costs a single virtual call.

```csharp
using ULogger;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    ILogSink log;
    TaggedLogSink net;

    void Awake()
    {
        log = (ILogSink)Debug.unityLogger.logHandler; // the handler installed at startup
        net = log.WithTag("Net").For(this);            // tag and context, bound once
    }

    void OnLoaded(int count, float milliseconds)
    {
        log.Info("Loaded {0} items in {1} ms", count, milliseconds);
        net.Warning("Retrying request #{0}", count);
    }
}
```

- Methods: `Trace`, `Debug`, `Info`, `Warning`, `Error`, `Critical`, each with zero to four arguments.
- Placeholders are `{0}`–`{3}`, optionally with a format specifier: `{0:F3}`, `{1:N0}`, `{2:X}`, or a custom one such as `{0:+0.0;-0.0}`. A placeholder with no matching argument is printed verbatim, spec included, and so is a malformed one such as `{0:F3` with no closing brace. An invalid spec (`{0:Q}`) prints the value as if there were none.
- Numbers are always formatted in the invariant culture: `0.5f` prints as `0.5` on every machine, whatever its locale. A log is parsed by tools, and a decimal comma breaks them.
- Numbers, `bool` and `char` are written without allocating, with or without a spec. Enums are written by name and ignore the spec. Types implementing `IFormattable` (`Vector3`, `DateTime`, `TimeSpan`, …) get the spec and the invariant culture, other types go through `ToString()`; both allocate.
- For more than four arguments, build the message yourself and write it with `Write(level, message)`, available on `ILogSink` and on `TaggedLogSink`:

  ```csharp
  if (!log.IsEnabled(LogLevel.Debug)) return;
  var b = LogFormatter.Scratch;
  b.Append("segment hand=").Append(handedness)
   .Append(" t=").Append(Time.time, "F3")
   .Append(" dt=").Append(Time.deltaTime * 1000f, "F1").Append("ms");
  log.Write(LogLevel.Debug, b.Span);
  ```

  `CharBuffer.Append(value)` and `Append(value, spec)` print a value exactly as `{0}` and `{0:spec}` would.
- `TaggedLogSink` is a readonly struct: keeping one in a field costs nothing.

### Log Levels

`LogLevel` increases with severity, so a filter is a plain comparison. It maps onto Unity's coarser `LogType`:

| `LogLevel`               | `LogType`   |
|--------------------------|-------------|
| `Trace`, `Debug`, `Info` | `Log`       |
| `Warning`                | `Warning`   |
| `Error`                  | `Error`     |
| `Critical`               | `Exception` |

Entries arriving through `Debug.Log*` are mapped back: `Log` → `Info`, `Assert` → `Error`, `Exception` → `Critical`. A handler with `minLevel` set to `Debug` therefore shows every `Debug.Log` call but drops `Trace` calls from the allocation-free API. `Off` silences a handler entirely.

### Exceptions

Every handler has `logExceptions` (default `true`). Exceptions count as `Critical`, so they pass any `minLevel` except `Off`, and the same rule applies whichever API logged them.

### Deduplication

When a `CompositeLogHandler` forwards a `Debug.Log*` call, each destination receives it once: two `ConsoleLogHandler`s in the same composite print an entry once, and two `FileLogHandler`s write it once per distinct file. A handler that rejects an entry (by level or tag) leaves it available to the next one. The allocation-free API is not deduplicated.

## Threading

All handlers accept calls from any thread.

**`ConsoleLogHandler`** writes immediately on the main thread. On any other thread it renders the entry on the spot and queues it; the queue is flushed to the Console at the end of the frame, by a `PostLateUpdate` player loop system (and `EditorApplication.update` in Edit Mode). The context `Object` of an entry is only safe to touch on the main thread, which is why the hand-over exists.

- Cross-thread order is not preserved: an entry queued by a background thread can appear after a main-thread entry that was logged later.
- `backgroundQueueCapacity` (default `256`) — entries buffered per frame. Anything beyond it is dropped, and a warning with the drop count is printed on the next flush.
- `captureBackgroundStackTrace` (default off) — attaches a stack trace captured on the logging thread. It follows the project's Stack Trace setting for each log type (**Project Settings → Player → Other Settings**, or `Application.SetStackTraceLogType`): `None` attaches nothing, and `ScriptOnly` and `Full` both attach the managed trace. Costs an allocation and a symbol lookup per entry. A change to the setting takes effect from the next frame.

**`FileLogHandler`** writes from its own background thread, so logging never waits for the disk. If its buffers overflow, entries are dropped and a `[ULogger] dropped N message(s)` line is written in their place. Enable `openWriterOnEnable` if the very first entry may come from a background thread: opening the file uses Unity APIs that are only available on the main thread.

## Built-in Handlers

Fields shared by every handler:

- `tags` — see [Tag Filtering](#tag-filtering).
- `logExceptions` — see [Exceptions](#exceptions).

### CompositeLogHandler

Forwards log messages to multiple other handlers. Add them to the `logHandlers` list in the Inspector. Empty slots are skipped. Cycles (a composite reaching itself through another) are detected and cut at dispatch time; the Inspector warns about direct self-references and duplicates.

### ConsoleLogHandler

Keeps log output in the Unity Console while giving you control over formatting and filtering.

- `minLevel` (default `Trace`) — messages below this level are discarded.
- `tagFormat` (default `"[{0}] {1}"`) — how a tag and a message are combined: `{0}` is the tag, `{1}` the message. Empty means Unity's own `"{0}: {1}"`.
- `useColors` — colors messages by level: warnings yellow, errors and exceptions red, asserts magenta.
- `infoColor` — works together with `useColors`. Sets the color of `Log`-level messages; left at the default gray, they are not wrapped at all.
- `captureBackgroundStackTrace`, `backgroundQueueCapacity` — see [Threading](#threading).

### FileLogHandler

Writes logs to a text file. Any missing directories in the path are created automatically. The `path` field supports dynamic variables.

- `path` — file path for the log. Example: **%pdp/logs/%dt.log**. Supported variables:
  - `%pdp` — `Application.persistentDataPath`
  - `%dp` — `Application.dataPath`
  - `%dt` — `DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")`
- `minLevel` (default `Trace`) — messages below this level are not written.
- `openWriterOnEnable` — open the file when the handler is enabled instead of on the first message. See [Threading](#threading).
- `appendTimeFormat` (default `"yyyy-MM-dd HH:mm:ss.fff"`) — timestamp prepended to each line. Supported tokens: `yyyy yy MM dd HH hh mm ss fff ff f`; everything else is copied literally. Clear the value to disable timestamps.
- `tagFormat` (default `"[{0}] \"{1}\""`) — how a tag and a message are combined. Empty means Unity's own `"{0}: {1}"`.
- `appendLogLevel` — prefix each line with `ERROR`, `ASSERT`, `WARNING`, `INFO` or `FATAL`.
- `flattenMultiline` (default `true`) — replace line breaks inside a message with spaces, so each entry is exactly one line.
- `maxMessageBytes` (default 16 KB) — longer entries are cut at a UTF-8 character boundary and marked `...[truncated]`. `0` disables the limit.
- `bufferBytes` (default 256 KB) — size of each of the writer's two buffers. Keep it well above `maxMessageBytes`; the Inspector warns when it holds fewer than eight maximum-size messages.

## Upgrading from 1.0.x

Handler assets saved with 1.0.x are migrated automatically when they are loaded. The new layout is written to disk the next time the asset is saved.

- `logLevel` (a `LogType`) becomes `minLevel` (a `LogLevel`), keeping the same entries: `Error` and `Assert` → `Error`, `Warning` → `Warning`, `Log` and `Exception` → `Trace`.
- `ConsoleLogHandler.tagFormatOverride` becomes `tagFormat`. Existing values are kept; newly created assets default to `"[{0}] {1}"`.

Behaviour that changed:

- `Debug.LogFormat` keeps its format string. Previously `ConsoleLogHandler` replaced a one-argument format with `"{0}"` and a longer one with `tagFormatOverride`, and `FileLogHandler` did the same with quotes and `tagFormat` — so `Debug.LogFormat("hp {0}", 5)` printed just `5`.
- Tag detection is strict (see [Usage](#usage)). Previously any call whose first argument was a listed tag string was treated as tagged.
- Exceptions are filtered the same way by every handler and API (see [Exceptions](#exceptions)).
