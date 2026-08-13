using UnityEngine;

namespace ULogger {
    public interface IOverrideContextForException {
        public Object Context { get; }
    }
}
