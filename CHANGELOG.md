# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses [Semantic Versioning](https://semver.org/).

## [1.1.0] - Unreleased

Handler assets saved with 1.0.x are migrated automatically on load; see **Upgrading from 1.0.x** in the README.

### Added

- Allocation-free logging API: `ILogSink`, implemented by every handler, with `LogSinkExtensions` (`Trace`, `Debug`, `Info`, `Warning`, `Error`, `Critical`, up to four arguments formatted without boxing), `TaggedLogSink` (`WithTag`, `For`), `LogLevel` and `LogFormatter`.
- Every handler can be called from any thread.
- `ConsoleLogHandler` hands entries logged on background threads over to the main thread and emits them at the end of the frame. New fields: `captureBackgroundStackTrace` (follows the project's Stack Trace setting) and `backgroundQueueCapacity`.
- `FileLogHandler`: `bufferBytes` and `openWriterOnEnable`.
- `logExceptions` on every handler; previously only `FileLogHandler` had it.
- `CompositeLogHandler` detects cycles in the handler graph and cuts them at dispatch time.
- Format specifiers in placeholders on the allocation-free API: `{0:F3}`, `{0:N0}`, `{0:X}`, custom formats such as `{0:+0.0;-0.0}`. Numbers stay allocation-free; `IFormattable` types such as `Vector3` and `DateTime` receive the spec too.
- `CharBuffer.Append(value)` / `Append(value, spec)` / `Append(string)`, chainable, for building messages with more than four arguments in `LogFormatter.Scratch`.
- `Write(level, message)` on `TaggedLogSink` and as an `ILogSink` extension, for writing such a message.
- EditMode and PlayMode test suites.

### Changed

- Minimum Unity version is now 2021.2.
- `logLevel` (a `LogType`) is replaced by `minLevel` (a `LogLevel`) on `ConsoleLogHandler` and `FileLogHandler`. Existing values are migrated to the level that keeps the same entries.
- `ConsoleLogHandler.tagFormatOverride` is renamed to `tagFormat`. Existing values are migrated; new assets default to `"[{0}] {1}"`.
- `Debug.LogFormat` keeps its format string. Both handlers used to replace it, so `Debug.LogFormat("hp {0}", 5)` printed only `5`.
- A call is treated as tagged only in the exact shape Unity's `Logger.Log(tag, message)` produces. Previously any call whose first argument was a listed tag matched.
- Exceptions are filtered the same way by every handler and API: by `logExceptions`, and by `minLevel` as `Critical`; never by tag.
- An empty `tagFormat` means Unity's own `"{0}: {1}"`, identically for `Debug.Log*` calls and the allocation-free API.
- The allocation-free API formats numbers and `IFormattable` values in the invariant culture, like `FileLogHandler` already did for `Debug.LogFormat`. On a machine with a Russian locale `0.5f` printed as `0,5`.
- `CharBuffer.Append(char)` and `Append(ReadOnlySpan<char>)` return the buffer instead of `void`.
- The `MonoLogger` sample is rebuilt around the startup bootstrap: its scene no longer swaps the handler, its assets are saved in the 1.1.0 format, the file handler is part of the chain, and a second script demonstrates `ILogSink`, `WithTag`/`For` and logging from a worker thread.

### Fixed

- `ConsoleLogHandler` dropped entries logged as `LogType.Exception` through `LogFormat` at the default level.
- Deduplication state was shared between threads and could be corrupted by concurrent logging.
- A cycle through another composite (A → B → A) overflowed the stack.
- `FileLogHandler` wrote `Debug.LogFormat("{0:F2}", x)` as the literal text `{0:F2}`; the console, going through `string.Format`, printed the value.
