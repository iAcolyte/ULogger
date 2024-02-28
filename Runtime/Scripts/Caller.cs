using UnityEngine;

namespace ULogger {
    public readonly struct Caller {
        public readonly string name;
        public readonly string filePath;
        public readonly int lineNumber;

        public Caller(string name, string filePath, int lineNumber) {
            this.name = name;
            if (filePath.Length > Application.dataPath.Length - 6)
                this.filePath = filePath[(Application.dataPath.Length - 6)..];
            else
                this.filePath = filePath;
            this.lineNumber = lineNumber;
        }
    }
}