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
- EditMode and PlayMode test suites.

### Changed

- Minimum Unity version is now 2021.2.
- `logLevel` (a `LogType`) is replaced by `minLevel` (a `LogLevel`) on `ConsoleLogHandler` and `FileLogHandler`. Existing values are migrated to the level that keeps the same entries.
- `ConsoleLogHandler.tagFormatOverride` is renamed to `tagFormat`. Existing values are migrated; new assets default to `"[{0}] {1}"`.
- `Debug.LogFormat` keeps its format string. Both handlers used to replace it, so `Debug.LogFormat("hp {0}", 5)` printed only `5`.
- A call is treated as tagged only in the exact shape Unity's `Logger.Log(tag, message)` produces. Previously any call whose first argument was a listed tag matched.
- Exceptions are filtered the same way by every handler and API: by `logExceptions`, and by `minLevel` as `Critical`; never by tag.
- An empty `tagFormat` means Unity's own `"{0}: {1}"`, identically for `Debug.Log*` calls and the allocation-free API.

### Fixed

- `ConsoleLogHandler` dropped entries logged as `LogType.Exception` through `LogFormat` at the default level.
- Deduplication state was shared between threads and could be corrupted by concurrent logging.
- A cycle through another composite (A → B → A) overflowed the stack.
