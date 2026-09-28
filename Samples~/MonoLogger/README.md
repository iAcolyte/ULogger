# MonoLogger

A complete, runnable ULogger setup: a handler chain in `Resources`, a bootstrap that installs it before
the first scene loads, and two components that log through the two available APIs.

## Running it

Open `Scenes/SampleScene` and press Play. Nothing else has to be wired up — `ULoggerBootstrap` loads
`CompositeLH` from `Resources` in `BeforeSceneLoad` and assigns it to `Debug.unityLogger.logHandler`.
No component in the scene touches the handler, so the chain stays installed for the whole session.

## The handler chain

`Resources/CompositeLH` forwards every entry to three handlers:

| Asset | Accepts | Notes |
| --- | --- | --- |
| `ConsoleLH TAG` | tag `TAG` only, from `Trace` up | Renders as `[TAG] message`, coloured |
| `ConsoleLH Default Errors` | any tag, from `Error` up | Renders as `TAG: message`, Unity's own form |
| `FileLH` | any tag, from `Trace` up | Writes the file described below |

So the same entry can reach the console, the file, both, or neither — which is the point of the sample.

The log file goes to `%dp/Samples/ULogger/%dt.log`, that is
`Application.dataPath + "/Samples/ULogger/" + "yyyy_MM_dd_HH_mm_ss" + ".log"` — inside the project's
`Assets` folder in the editor. Each run creates a new file.

## The two scripts

`Runtime/LogShowcase.cs` uses `UnityEngine.Debug`, the path existing code already takes: untagged
`Debug.Log` and `Debug.LogError`, the tagged `Debug.unityLogger.Log("TAG", …)` forms, a tag nobody
subscribes to, and `Debug.LogException`.

`Runtime/SinkShowcase.cs` uses the direct API: `ILogSink` with the generic `Info`/`Warning`/`Trace`
overloads, which format up to four arguments without boxing them; `WithTag("GAMEPLAY").For(this)`,
which returns a `TaggedLogSink` struct worth caching in a field; an `IsEnabled` check around a message
whose argument is expensive to build; `Exception`; and one entry written from a worker thread.

## Reading the output

Expected console output, in order:

- `Untagged error: …` — passes `ConsoleLH Default Errors`; the preceding `Debug.Log` does not and is
  only in the file.
- `[TAG] Tagged info.` and the three other tagged entries — from `ConsoleLH TAG`.
- The exception with its stack trace — exceptions bypass both the level and the tag filter, and are
  governed only by each handler's `logExceptions`.
- `started on frame …` from `SinkShowcase`; the `GAMEPLAY` entries after it are missing on purpose —
  no console handler subscribes to that tag, but they are in the file.
- `written from a worker thread …`, one frame later: `ConsoleLogHandler` parks background entries in a
  queue and `MainThreadDispatcher` delivers them at the end of the next frame.

The log file holds all of the above plus everything the console filtered out.
