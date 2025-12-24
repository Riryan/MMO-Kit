#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MultiplayerARPG
{
    public class ServerMetricsLogger : MonoBehaviour
    {
        private const float LOG_INTERVAL = 60f;

        private float _nextLogTime;
        private float _serverStartTime;

        private int _frameCount;
        private float _frameTimeAccum;
        private float _frameTimeP99;

        private int _gc0Last;
        private int _gc1Last;
        private int _gc2Last;

        private StreamWriter _writer;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            _serverStartTime = Time.realtimeSinceStartup;
            _nextLogTime = Time.realtimeSinceStartup + LOG_INTERVAL;

            _gc0Last = GC.CollectionCount(0);
            _gc1Last = GC.CollectionCount(1);
            _gc2Last = GC.CollectionCount(2);

            string path = Path.Combine(
                Application.persistentDataPath,
                $"server_metrics_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            // ----------------------------------------------------------------
            // FIX: Open file with shared read/write access (multi-server safe)
            // ----------------------------------------------------------------
            var fs = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite);

            _writer = new StreamWriter(fs);

            _writer.WriteLine(
                "timestamp,uptime_sec,avg_frame_ms,p99_frame_ms," +
                "gc0,gc1,gc2,mono_used_mb,mono_reserved_mb,total_alloc_mb," +
                "players,entities");

            _writer.Flush();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime * 1000f;

            _frameCount++;
            _frameTimeAccum += dt;
            if (dt > _frameTimeP99)
                _frameTimeP99 = dt;

            if (Time.realtimeSinceStartup < _nextLogTime)
                return;

            WriteLog();
            ResetFrameStats();

            _nextLogTime = Time.realtimeSinceStartup + LOG_INTERVAL;
        }

        private void WriteLog()
        {
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);

            long monoUsed = Profiler.GetMonoUsedSizeLong();
            long monoReserved = Profiler.GetMonoHeapSizeLong();
            long totalAlloc = Profiler.GetTotalAllocatedMemoryLong();

            int players = BaseGameNetworkManager.Singleton != null
                ? BaseGameNetworkManager.Singleton.PlayersCount
                : 0;

            int entities = 0;

            float uptime = Time.realtimeSinceStartup - _serverStartTime;
            float avgFrame = _frameCount > 0 ? _frameTimeAccum / _frameCount : 0f;

            _writer.WriteLine(string.Join(",",
                DateTime.UtcNow.ToString("o"),
                uptime.ToString("F0"),
                avgFrame.ToString("F3"),
                _frameTimeP99.ToString("F3"),
                gc0 - _gc0Last,
                gc1 - _gc1Last,
                gc2 - _gc2Last,
                (monoUsed / (1024f * 1024f)).ToString("F1"),
                (monoReserved / (1024f * 1024f)).ToString("F1"),
                (totalAlloc / (1024f * 1024f)).ToString("F1"),
                players,
                entities
            ));

            _writer.Flush();

            _gc0Last = gc0;
            _gc1Last = gc1;
            _gc2Last = gc2;
        }

        private void ResetFrameStats()
        {
            _frameCount = 0;
            _frameTimeAccum = 0f;
            _frameTimeP99 = 0f;
        }

        private void OnDestroy()
        {
            _writer?.Flush();
            _writer?.Close();
        }
    }
}
#endif
