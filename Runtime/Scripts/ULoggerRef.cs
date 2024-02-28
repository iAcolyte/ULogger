using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ULogger {
    public sealed class ULoggerRef: MonoBehaviour {
        public ULoggerSettings settings;

        private void OnEnable() {
#if !UNITY_EDITOR
            var h = settings.OverwriteDefaultHandler;
#endif
        }
    }
}
