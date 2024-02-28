
using compilers = System.Runtime.CompilerServices;
using UnityEngine;
using ULogger;

public static class Logger {
    private static ULoggerSettings uLoggerSettings;

    public static LogLevel LogLevel => uLoggerSettings.LogLevel;
    public static UAbstractLogger[] Loggers => uLoggerSettings.Loggers;
    internal static ILogHandler DefaultLogHandler { get; private set; }

    private static bool initialized;

    static Logger() {
        DefaultLogHandler = UnityEngine.Debug.unityLogger.logHandler;
    }

    public static void Trace(string message, Object context = null,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        Log(LogLevel.Trace, message, context, caller);
    }
    public static void Debug(string message, Object context = null,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        Log(LogLevel.Debug, message, context, caller);
    }
    public static void Info(string message, Object context = null,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        Log(LogLevel.Info, message, context, caller);
    }
    public static void Warn(string message, Object context = null,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        Log(LogLevel.Warn, message, context, caller);
    }
    public static void Error(string message, Object context = null,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        Log(LogLevel.Error, message, context, caller);
    }
    public static void Fatal(System.Exception exception, Object context,
            [compilers.CallerMemberName] string cmname = null,
            [compilers.CallerLineNumber] int clnumber = -1,
            [compilers.CallerFilePath] string cfpath = null) {
        var caller = new Caller(cmname, cfpath, clnumber);
        LogFatal(exception, context, caller);
    }

    private static void Log(LogLevel level, string message, Object context, Caller caller) {
        if (!initialized) return;
        if (LogLevel > level) return;
        var loggers = Loggers;
        if (loggers.Length == 0) {
            var logType = level switch {
                LogLevel.Warn => LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => LogType.Error,
                _ => LogType.Log
            };
            DefaultLogHandler.LogFormat(logType, context, message, System.Array.Empty<object>());
            return;
        }
        foreach (var logger in loggers)
            logger.LogFormat(level, context, message, caller);
    }
    private static void LogFatal(System.Exception exception, Object context, Caller caller) {
        var loggers = Loggers;
        if (loggers.Length == 0) {
            DefaultLogHandler.LogException(exception, context);
            return;
        }
        foreach (var logger in loggers)
            logger.LogException(exception, context);
        Application.Quit(500);
    }

    internal static void Initialize(ULoggerSettings config) {
        UnityEngine.Debug.unityLogger.logHandler = DefaultLogHandler;
        uLoggerSettings = config;
        var loggers = Loggers;
        DefaultLogHandler = UnityEngine.Debug.unityLogger.logHandler;
        if (!uLoggerSettings.OverwriteDefaultHandler)
            return;
        if (loggers.Length == 0)
            return;
        foreach (var logger in Loggers) {
            logger.Initialize();
        }
        ILogHandler handler = loggers.Length == 1 ? loggers[0] : new CombindedLogHandler(loggers);
        UnityEngine.Debug.unityLogger.logHandler = handler;
        initialized = true;
    }

    private class CombindedLogHandler: ILogHandler {
        private ILogHandler[] handlers;

        public CombindedLogHandler(ILogHandler[] handlers) {
            this.handlers = handlers;
        }

        public void LogException(System.Exception exception, Object context) {
            foreach (var handler in handlers) handler.LogException(exception, context);
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args) {
            foreach (var handler in handlers) handler.LogFormat(logType, context, format, args);
        }
    }
}
