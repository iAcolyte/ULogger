using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace ULogger {
#if UNITY_EDITOR
    [ExecuteAlways]
#endif

    public class ULoggerSettings: ScriptableObject {
        public const string SETTINGS_PATH = "Assets/Settings/ULoggerSettings.asset";

        [SerializeField] private bool overwriteDefaultHandler = true;
        public bool OverwriteDefaultHandler => overwriteDefaultHandler;

        [SerializeField] private LogLevel debugLogLevel = LogLevel.Debug;
        [SerializeField] private UAbstractLogger[] debugLoggers = new UAbstractLogger[0];
        [SerializeField] private LogLevel releaseLogLevel = LogLevel.Warn;
        [SerializeField] private UAbstractLogger[] releaseLoggers = new UAbstractLogger[0];

        public LogLevel LogLevel => Debug.isDebugBuild ? debugLogLevel : releaseLogLevel;
        public UAbstractLogger[] Loggers => Debug.isDebugBuild ? debugLoggers : releaseLoggers;

        public void OnEnable() {
            Logger.Initialize(this);
        }

#if UNITY_EDITOR
        public static ULoggerSettings GetOrCreateSettings() {
            var settings = AssetDatabase.LoadAssetAtPath<ULoggerSettings>(SETTINGS_PATH);
            if (settings == null) {
                settings = CreateInstance<ULoggerSettings>();
                if (!Directory.Exists(SETTINGS_PATH)) {
                    var fileInfo = new FileInfo(SETTINGS_PATH);
                    Directory.CreateDirectory(fileInfo.DirectoryName);
                }
                settings.debugLogLevel = LogLevel.Debug;
                settings.releaseLogLevel = LogLevel.Warn;
                var consoleLogger = Resources.Load<ConsoleLogger>("ConsoleLogger");
                settings.debugLoggers = new UAbstractLogger[]{
                    consoleLogger
                };
                settings.releaseLoggers = new UAbstractLogger[]{
                    consoleLogger
                };
                AssetDatabase.CreateAsset(settings, SETTINGS_PATH);
                AssetDatabase.SaveAssets();
            }
            var go = GameObject.Find(nameof(ULoggerRef));
            if (!go) go = new GameObject(nameof(ULoggerRef), typeof(ULoggerRef));
            if (!go.TryGetComponent<ULoggerRef>(out var loggerRef))
                loggerRef = go.AddComponent<ULoggerRef>();
            loggerRef.settings = settings;
            go.transform.SetSiblingIndex(0);
            go.hideFlags = HideFlags.HideInHierarchy;
            return settings;
        }

        public static SerializedObject GetSerializedSettings() {
            return new SerializedObject(GetOrCreateSettings());
        }
#endif
    }
}