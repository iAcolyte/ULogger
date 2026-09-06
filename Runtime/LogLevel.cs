namespace ULogger {
    /// <summary>
    /// Severity of a log entry. Unlike <see cref="UnityEngine.LogType"/> the values increase with
    /// severity, so a level filter is a plain comparison.
    /// </summary>
    public enum LogLevel: byte {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,

        /// <summary>Filter value only; never the level of an actual entry.</summary>
        Off = 255
    }
}
