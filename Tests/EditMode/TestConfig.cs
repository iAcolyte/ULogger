using System.Reflection;

using NUnit.Framework;

using ULogger;

using UnityEngine;

/// <summary>
/// Reaches the handlers' private [SerializeField] state. Reflection rather than an internal
/// setter: the inspector fields are the real configuration surface, and adding a parallel
/// programmatic one just to be testable would mean two ways to configure a handler.
/// </summary>
public static class TestConfig {
    const BindingFlags Fields =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;

    /// <summary>
    /// Creates a handler and applies field values, then re-runs OnEnable so anything compiled
    /// from those fields (time format, level labels, colour cache) is rebuilt.
    /// </summary>
    public static T Create<T>(params (string field, object value)[] fields) where T : ScriptableObject {
        var instance = ScriptableObject.CreateInstance<T>();
        instance.name = typeof(T).Name + "_Test";

        foreach (var (field, value) in fields) Set(instance, field, value);
        Reinitialize(instance);
        return instance;
    }

    public static void Set(object target, string field, object value) {
        var info = Find(target.GetType(), field);
        Assert.NotNull(info, $"No field '{field}' on {target.GetType().Name}. Renamed? Update the test.");
        info.SetValue(target, value);
    }

    public static object Get(object target, string field) {
        var info = Find(target.GetType(), field);
        Assert.NotNull(info, $"No field '{field}' on {target.GetType().Name}. Renamed? Update the test.");
        return info.GetValue(target);
    }

    /// <summary>Invokes a private lifecycle method, if the type declares one.</summary>
    public static void Invoke(object target, string method) {
        for (var type = target.GetType(); type != null; type = type.BaseType) {
            var info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (info == null) continue;
            info.Invoke(target, null);
            return;
        }
    }

    public static void Reinitialize(object target) => Invoke(target, "OnEnable");

    /// <summary>Destroying is what fires OnDisable, which is where handlers unregister and flush.</summary>
    public static void Destroy(UnityEngine.Object target) {
        if (target != null) UnityEngine.Object.DestroyImmediate(target);
    }

    static FieldInfo Find(System.Type type, string field) {
        for (; type != null; type = type.BaseType) {
            var info = type.GetField(field, Fields);
            if (info != null) return info;
        }
        return null;
    }
}
