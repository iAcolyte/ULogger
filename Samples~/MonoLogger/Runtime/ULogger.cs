#nullable enable

using UnityEngine;

namespace ULogger.MonoLogger
{
    public sealed class ULogger : MonoBehaviour
    {
        [SerializeField] ULogHandler uLogHandler;
        ILogHandler defaultHandler;

        void OnEnable()
        {
            defaultHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = uLogHandler;

            Debug.Log("No Appears");
            Debug.LogError("Appears");

            Debug.unityLogger.Log("TAG", "Appears with TAG");
            Debug.unityLogger.LogWarning("TAG", "Appears with TAG");
            Debug.unityLogger.LogError("TAG", "Appears with TAG");
            Debug.unityLogger.Log(LogType.Assert, "TAG", "Appears with TAG");
            throw new System.Exception("Test Exception");
        }

        void OnDisable()
        {
            Debug.unityLogger.logHandler = defaultHandler;
        }
    }
}
