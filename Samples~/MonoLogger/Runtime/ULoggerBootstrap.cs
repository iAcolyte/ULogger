#nullable enable

using UnityEngine;

namespace ULogger.MonoLogger
{
    // Installs the ULogger handler before the first scene loads. This runs in a later phase than
    // ConsoleLogHandler's SubsystemRegistration capture of Unity's default handler, so console
    // forwarding never loops back into the ULogger chain.
    //
    // The chain is loaded from Resources rather than referenced by a scene object on purpose: a
    // handler installed by a component would have to be uninstalled when that scene unloads, and
    // logging would go dark for everything that runs outside it.
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
