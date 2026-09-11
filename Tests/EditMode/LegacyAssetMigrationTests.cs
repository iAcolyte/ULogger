using System.IO;
using System.Text;

using NUnit.Framework;

using ULogger;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Handler assets saved by 1.0.x must keep their settings after an upgrade. Each fixture is written
/// in the exact YAML layout 1.0.4 produced, imported through the AssetDatabase and read back, so the
/// test goes through the same deserialization a real project does.
/// </summary>
public class LegacyAssetMigrationTests {
    const string FolderName = "__ULoggerMigrationTests";
    const string Folder = "Assets/" + FolderName;

    [SetUp]
    public void SetUp() {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", FolderName);
    }

    [TearDown]
    public void TearDown() {
        AssetDatabase.DeleteAsset(Folder);
    }

    static string Body(params string[] lines) {
        var builder = new StringBuilder();
        foreach (var line in lines) builder.Append("  ").Append(line).Append('\n');
        return builder.ToString();
    }

    static string ScriptGuid<T>() where T : ScriptableObject {
        var instance = ScriptableObject.CreateInstance<T>();
        try {
            var script = MonoScript.FromScriptableObject(instance);
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        } finally {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    static string Yaml<T>(string name, string body) where T : ScriptableObject =>
        "%YAML 1.1\n" +
        "%TAG !u! tag:unity3d.com,2011:\n" +
        "--- !u!114 &11400000\n" +
        "MonoBehaviour:\n" +
        "  m_ObjectHideFlags: 0\n" +
        "  m_CorrespondingSourceObject: {fileID: 0}\n" +
        "  m_PrefabInstance: {fileID: 0}\n" +
        "  m_PrefabAsset: {fileID: 0}\n" +
        "  m_GameObject: {fileID: 0}\n" +
        "  m_Enabled: 1\n" +
        "  m_EditorHideFlags: 0\n" +
        "  m_Script: {fileID: 11500000, guid: " + ScriptGuid<T>() + ", type: 3}\n" +
        "  m_Name: " + name + "\n" +
        "  m_EditorClassIdentifier: ULogger::ULogger." + typeof(T).Name + "\n" +
        body;

    static T Import<T>(string name, string yaml) where T : ScriptableObject {
        var path = $"{Folder}/{name}.asset";
        File.WriteAllText(path, yaml);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.NotNull(asset, $"The fixture at {path} did not import as {typeof(T).Name}.");
        return asset;
    }

    static string LegacyConsole(int logLevel, string tagFormatOverride = "{0}: {1}") => Body(
        "tags: []",
        "infoColor: {r: 0.7, g: 0.7, b: 0.7, a: 1}",
        "logLevel: " + logLevel,
        "tagFormatOverride: '" + tagFormatOverride + "'",
        "logExceptions: 1",
        "useColors: 1");

    // ------------------------------------------------------------------ level mapping

    [TestCase(LogType.Error, LogLevel.Error)]
    [TestCase(LogType.Assert, LogLevel.Error)]
    [TestCase(LogType.Warning, LogLevel.Warning)]
    [TestCase(LogType.Log, LogLevel.Trace)]
    [TestCase(LogType.Exception, LogLevel.Trace)]
    public void MapsEveryLegacyLevelToTheOneKeepingTheSameEntries(LogType legacy, LogLevel expected) {
        Assert.AreEqual(expected, ULogHandler.MigrateLegacyLogLevel(legacy));
    }

    // ------------------------------------------------------------------ ConsoleLogHandler

    [TestCase(0, LogLevel.Error)]
    [TestCase(2, LogLevel.Warning)]
    [TestCase(3, LogLevel.Trace)]
    public void Console_CarriesLogLevelIntoMinLevel(int legacy, LogLevel expected) {
        var asset = Import<ConsoleLogHandler>("Console" + legacy,
            Yaml<ConsoleLogHandler>("Console" + legacy, LegacyConsole(legacy)));

        Assert.AreEqual(expected, TestConfig.Get(asset, "minLevel"));
    }

    [Test]
    public void Console_CarriesTagFormatOverrideIntoTagFormat() {
        var asset = Import<ConsoleLogHandler>("ConsoleTag", Yaml<ConsoleLogHandler>("ConsoleTag", Body(
            "tags:",
            "- TAG",
            "infoColor: {r: 0.8, g: 1, b: 0.43, a: 1}",
            "logLevel: 3",
            "tagFormatOverride: '<{0}> {1}'",
            "logExceptions: 0",
            "useColors: 1")));

        Assert.AreEqual("<{0}> {1}", TestConfig.Get(asset, "tagFormat"));

        // Fields whose names never changed must come through untouched alongside the migration.
        CollectionAssert.AreEqual(new[] { "TAG" }, (string[])TestConfig.Get(asset, "tags"));
        Assert.AreEqual(false, TestConfig.Get(asset, "logExceptions"));
        Assert.AreEqual(true, TestConfig.Get(asset, "useColors"));
    }

    // ------------------------------------------------------------------ FileLogHandler

    [Test]
    public void File_CarriesLogLevelIntoMinLevel() {
        var asset = Import<FileLogHandler>("File", Yaml<FileLogHandler>("File", Body(
            "tags: []",
            "path: '%dp/Samples/ULogger/%dt.log'",
            "logLevel: 2",
            "logExceptions: 1",
            "appendTimeFormat: yyyy-MM-dd HH:mm:ss.fff",
            "tagFormat: '[{0}] \"{1}\"'",
            "appendLogLevel: 1")));

        Assert.AreEqual(LogLevel.Warning, TestConfig.Get(asset, "minLevel"));
        Assert.AreEqual("%dp/Samples/ULogger/%dt.log", TestConfig.Get(asset, "path"));
        Assert.AreEqual("[{0}] \"{1}\"", TestConfig.Get(asset, "tagFormat"));
    }

    // ------------------------------------------------------------------ current assets

    [Test]
    public void CurrentAssetsAreLeftAlone() {
        var asset = Import<ConsoleLogHandler>("Current", Yaml<ConsoleLogHandler>("Current", Body(
            "tags: []",
            "logExceptions: 1",
            "minLevel: 4",
            "tagFormat: '[{0}] {1}'",
            "useColors: 0",
            "infoColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}",
            "captureBackgroundStackTrace: 0",
            "backgroundQueueCapacity: 256",
            "legacyLogLevel: -1")));

        Assert.AreEqual(LogLevel.Error, TestConfig.Get(asset, "minLevel"));
        Assert.AreEqual("[{0}] {1}", TestConfig.Get(asset, "tagFormat"));
    }

    [Test]
    public void MigrationIsPersistedOnSaveAndNeverReapplied() {
        var path = $"{Folder}/RoundTrip.asset";
        var asset = Import<ConsoleLogHandler>("RoundTrip", Yaml<ConsoleLogHandler>("RoundTrip", LegacyConsole(2)));
        Assert.AreEqual(LogLevel.Warning, TestConfig.Get(asset, "minLevel"));

        // The user lowers the level in the inspector, and the asset gets saved.
        TestConfig.Set(asset, "minLevel", LogLevel.Debug);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        var saved = File.ReadAllText(path);
        StringAssert.Contains("minLevel: 1", saved);
        StringAssert.Contains("legacyLogLevel: -1", saved);
        StringAssert.DoesNotContain("tagFormatOverride", saved,
            "The 1.0.x key survived a save; the migration would run again on every load.");

        // Load the saved text as a brand-new asset, so nothing comes from the object still in memory.
        var copy = Import<ConsoleLogHandler>("RoundTripCopy",
            saved.Replace("m_Name: RoundTrip", "m_Name: RoundTripCopy"));
        Assert.AreEqual(LogLevel.Debug, TestConfig.Get(copy, "minLevel"),
            "The 1.0.x level was resurrected over the user's edit.");
    }
}
