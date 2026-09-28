#nullable enable

using System;

using UnityEngine;

namespace ULogger.MonoLogger
{
    /// <summary>
    /// The UnityEngine.Debug side of the package: everything here is a plain Debug call, routed to
    /// the handler chain that <see cref="ULoggerBootstrap"/> installed before the scene loaded.
    /// Nothing in this component touches Debug.unityLogger -- a scene object that swaps the handler
    /// would also have to restore it, and would undo the bootstrap the moment the scene unloads.
    /// </summary>
    public sealed class LogShowcase : MonoBehaviour
    {
        void Start()
        {
            // Untagged. 'ConsoleLH Default Errors' has minLevel = Error, so this never reaches the
            // console -- but FileLH takes everything from Trace up, so it is in the log file.
            Debug.Log("Untagged info: only the file handler keeps this one.");

            // Error clears that same minLevel, so this shows up in both places.
            Debug.LogError("Untagged error: console and file.");

            // The tagged form. The tag is the first argument, and 'ConsoleLH TAG' only accepts the
            // literal tag "TAG", rendering it through its tagFormat as "[TAG] message".
            Debug.unityLogger.Log("TAG", "Tagged info.");
            Debug.unityLogger.LogWarning("TAG", "Tagged warning.");
            Debug.unityLogger.LogError("TAG", "Tagged error.");
            Debug.unityLogger.Log(LogType.Assert, "TAG", "Tagged assert.");

            // A tag no handler subscribes to is dropped by the tag filter, not by the level filter.
            Debug.unityLogger.Log("UNKNOWN", "Nobody listens to this tag; dropped by every handler.");

            // Exceptions have their own switch (logExceptions) and are never tag-filtered, so this
            // reaches every handler in the chain regardless of minLevel.
            Debug.LogException(new InvalidOperationException("Sample exception, nothing is broken."), this);
        }
    }
}
