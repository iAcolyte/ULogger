# ULogger

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

If you prefer to scope logging to a specific scene, a `MonoBehaviour` that swaps `Debug.unityLogger.logHandler` in `OnEnable` and restores it in `OnDisable` works too (see the `MonoLogger` sample).

## Usage

You can use `Debug.Log*` as usual. However, the package makes heavy use of **tags**. To leverage them, use `Debug.unityLogger.Log("TAG", "Message")`.

> **Note:** Tag detection relies on Unity formatting `Logger.Log(tag, message)` calls with the internal `"{0}: {1}"` format string. Only messages logged through the `Debug.unityLogger.Log*(tag, message)` overloads are recognized as tagged; plain `Debug.Log` messages are always treated as untagged.

### Tag Filtering

Every `ULogHandler` has a `tags` field (configured in the Inspector). When the array is **not empty**, the handler will only process tagged messages whose tag is listed in that array. When the array is **empty**, the handler processes all messages.

### Built-in Handlers

#### CompositeLogHandler

Forwards log messages to multiple other handlers. Add them to the `logHandlers` list in the Inspector.

#### ConsoleLogHandler

Keeps log output in the Unity Console while giving you control over formatting and filtering.

- `infoColor` — works together with `useColors`. Sets the text color for Log-level messages.
- `logLevel` — minimum log level. Messages below this level are discarded.
- `tagFormatOverride` — defaults to `"{0}: {1}"`. Overrides the format string for tagged messages. Leave empty to hide tags.
- `useColors` — enables colored output based on the log level.

#### FileLogHandler

Writes logs to a text file. Any missing directories in the path are created automatically. The `path` field supports dynamic variables.

- `path` — file path for the log. Example: **%pdp/logs/%dt.log**. Supported variables:
  - `%pdp` — `Application.persistentDataPath`
  - `%dp` — `Application.dataPath`
  - `%dt` — `DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")`
- `logLevel` — minimum log level. Messages below this level are not written.
- `logExceptions` — whether exceptions are written to the file (defaults to `true`).
- `appendTimeFormat` (default `"yyyy-MM-dd HH:mm:ss.fff"`) — timestamp format prepended to each line. Clear the value to disable timestamps.
- `tagFormat` — defaults to `"[{0}] \"{1}\""`. Overrides the format string for tagged messages. Leave empty to hide tags.
- `appendLogLevel` — whether to include the log level label in the output.
