#nullable enable

using UnityEngine;

namespace ULogger.MonoLogger
{
    // Installs the ULogger handler before the first scene loads. This runs in a later phase than
    // ConsoleLogHandler's SubsystemRegistration capture of Unity's default handler, so console
    // forwarding never loops back into the ULogger chain.
    public static class ULoggerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var handler = Resources.Load<ULogHandler>("CompositeLH");
            if (handler == null)
            {
                Debug.LogWarning("ULogger: 'CompositeLH' not found in Resources; keeping the default handler.");
                return;
            }
            Debug.unityLogger.logHandler = handler;
        }
    }
}
