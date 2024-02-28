using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using USettings = ULogger.ULoggerSettings;

namespace ULogger.Editor {
    [InitializeOnLoad]
    public class ULoggerSettingsProvider: SettingsProvider {
        static ULoggerSettingsProvider() {
            UnityEditor.EditorApplication.delayCall += () => {
                USettings.GetOrCreateSettings();
            };
        }

        private SerializedObject settings;

        private class Styles {
            public static GUIContent overwriteDefaultHandler = new GUIContent("Overwrite default handler");
            public static GUIContent logLevel = new GUIContent("Log Level");
            public static GUIContent loggers = new GUIContent("Loggers");
        }

        public ULoggerSettingsProvider(string path, SettingsScope scope = SettingsScope.User)
            : base(path, scope) { }

        public static bool IsSettingsAvailable() {
            USettings.GetOrCreateSettings();
            return File.Exists(USettings.SETTINGS_PATH);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement) {
            settings = USettings.GetSerializedSettings();
        }

        public override void OnGUI(string searchContext) {

            EditorGUILayout.PropertyField(settings.FindProperty("overwriteDefaultHandler"), Styles.overwriteDefaultHandler);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Debug logger settings:");
            EditorGUILayout.PropertyField(settings.FindProperty("debugLogLevel"), Styles.logLevel);
            EditorGUILayout.PropertyField(settings.FindProperty("debugLoggers"), Styles.loggers);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Release logger settings:");
            EditorGUILayout.PropertyField(settings.FindProperty("releaseLogLevel"), Styles.logLevel);
            EditorGUILayout.PropertyField(settings.FindProperty("releaseLoggers"), Styles.loggers);
            EditorGUILayout.EndVertical();
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [SettingsProvider]
        public static SettingsProvider CreateULoggerSettingsProvider() {
            if (IsSettingsAvailable()) {
                var provider = new ULoggerSettingsProvider("Project/ULogger", SettingsScope.Project) {
                    keywords = GetSearchKeywordsFromGUIContentProperties<Styles>()
                };
                return provider;
            }
            return null;
        }
    }
}