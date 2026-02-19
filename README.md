# ULogger

## Installation

Use **Package Manager** → **+** → **Install Package from git URL**.

## Initialization

> **Important:** This package replaces Unity's default log handling mechanism.

The package provides three log handler implementations:

- `ConsoleLogHandler` — outputs logs to the Unity Console.
- `FileLogHandler` — writes logs to a text file.
- `CompositeLogHandler` — does not output logs itself but forwards them to other attached handlers.

Choose the handlers you need and create ScriptableObject assets for them (e.g. **Create → ULogger → Console Log**).

Then create a class that swaps the logging mechanism. You can use the sample from Samples:

```csharp
using ULogger;
using UnityEngine;

public sealed class ULogger : MonoBehaviour
{
    [SerializeField] ULogHandler uLogHandler;
    ILogHandler defaultHandler;

    void OnEnable()
    {
        defaultHandler = Debug.unityLogger.logHandler;
        Debug.unityLogger.logHandler = uLogHandler;
    }

    void OnDisable()
    {
        Debug.unityLogger.logHandler = defaultHandler;
    }
}
```

## Usage

You can use `Debug.Log*` as usual. However, the package makes heavy use of **tags**. To leverage them, use `Debug.unityLogger.Log("TAG", "Message")`.

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

Writes logs to a text file. The `path` field supports dynamic variables.

- `path` — file path for the log. Example: **%pdp/logs/%dt.log**. Supported variables:
  - `%pdp` — `Application.persistentDataPath`
  - `%dp` — `Application.dataPath`
  - `%dt` — `DateTime.Now.ToString("yyyy_MM_dd_hh_mm_ss")`
- `logLevel` — minimum log level. Messages below this level are not written.
- `appendTimeFormat` (default `"yyyy-MM-dd hh:mm:ss.fff"`) — timestamp format prepended to each line. Clear the value to disable timestamps.
- `tagFormat` — defaults to `"[{0}] \"{1}\""`. Overrides the format string for tagged messages. Leave empty to hide tags.
- `appendLogLevel` — whether to include the log level label in the output.