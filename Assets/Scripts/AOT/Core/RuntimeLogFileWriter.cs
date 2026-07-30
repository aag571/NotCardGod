using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using UnityEngine;

    [DefaultExecutionOrder(-1000)]
    public sealed class RuntimeLogFileWriter : MonoBehaviour
    {
        [SerializeField] private string logDirectoryName = "Logs";
        [SerializeField] private string fileNamePrefix = "runtime";
        [SerializeField] private bool includeStackTrace = true;

        private readonly ConcurrentQueue<string> pendingLines = new ConcurrentQueue<string>();
        private StreamWriter writer;
        private bool initialized;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            OpenLogFile();
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            FlushPendingLines();
        }

        private void Update()
        {
            FlushPendingLines();
        }

        private void OnApplicationQuit()
        {
            FlushPendingLines();
            CloseLogFile();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!initialized)
                return;

            var line = new StringBuilder()
                .Append('[')
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append("] [")
                .Append(type)
                .Append("] ")
                .AppendLine(condition ?? string.Empty);

            if (includeStackTrace && !string.IsNullOrEmpty(stackTrace))
                line.AppendLine(stackTrace.TrimEnd());

            pendingLines.Enqueue(line.ToString());
        }

        private void OpenLogFile()
        {
            try
            {
                var directory = Path.Combine(
                    Application.persistentDataPath,
                    string.IsNullOrWhiteSpace(logDirectoryName) ? "Logs" : logDirectoryName);
                Directory.CreateDirectory(directory);

                var prefix = string.IsNullOrWhiteSpace(fileNamePrefix) ? "runtime" : fileNamePrefix;
                var filePath = Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
                initialized = true;
                pendingLines.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Log] Runtime log started: {filePath}");
            }
            catch
            {
                initialized = false;
            }
        }

        private void FlushPendingLines()
        {
            if (writer == null)
                return;

            while (pendingLines.TryDequeue(out var line))
                writer.WriteLine(line);

            writer.Flush();
        }

        private void CloseLogFile()
        {
            if (writer == null)
                return;

            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Log] Runtime log closed.");
            writer.Flush();
            writer.Dispose();
            writer = null;
            initialized = false;
        }
    }
