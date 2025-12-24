#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LiteNetLibManager
{
    public sealed class SimulatedClientManager : MonoBehaviour
    {
        public static SimulatedClientManager Instance { get; private set; }

        // --------------------------------------------------------------------
        // Simulated connection-id range (MUST NOT collide with real)
        // --------------------------------------------------------------------
        public const long SIM_CONNECTION_OFFSET = 1_000_000;

        public static bool IsSimulatedId(long connectionId)
        {
            return connectionId >= SIM_CONNECTION_OFFSET;
        }

        // --------------------------------------------------------------------
        // CSV logging
        // --------------------------------------------------------------------
        private StreamWriter _csv;
        private int _secondsElapsed;
        private const int CSV_FLUSH_EVERY = 10;

        // --------------------------------------------------------------------
        // State
        // --------------------------------------------------------------------
        private readonly List<SimClient> _clients = new(1024);
        private readonly NetDataWriter _writer = new(false);

        private int _clientCount;
        private float _secondTimer;

        private long _bytesThisSecond;
        private int _packetsThisSecond;

        private LiteNetLibManager _net;
        private bool _initialized;

        public bool HasSimulatedClients => _clients.Count > 0;
        public int SimulatedObserverCount => _clients.Count;

        // --------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(this);

            ParseArgs();
            if (_clientCount <= 0)
            {
                enabled = false;
                return;
            }

            for (int i = 0; i < _clientCount; ++i)
                _clients.Add(new SimClient(i));
        }

        private void Start()
        {
            // IMPORTANT:
            // LiteNetLibManager does NOT exist yet in multi-role startup.
            // We must wait for it instead of failing early.
            StartCoroutine(WaitForServer());
        }

        private void ParseArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg.StartsWith("-simClients="))
                    int.TryParse(arg.Substring(12), out _clientCount);
            }
        }

        // --------------------------------------------------------------------
        // Wait until LiteNetLibManager server actually exists
        // --------------------------------------------------------------------
        private IEnumerator WaitForServer()
        {
            while (true)
            {
                _net = FindObjectOfType<LiteNetLibManager>();
                if (_net != null && _net.IsServer)
                    break;

                yield return null; // wait one frame
            }

            InitializeCsv();
            _initialized = true;
        }

        // --------------------------------------------------------------------
        // CSV init (called ONLY once server is ready)
        // --------------------------------------------------------------------
        private void InitializeCsv()
        {
            string dir = Path.Combine(Application.persistentDataPath, "simnet");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(
                dir,
                $"simnet_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

            _csv = new StreamWriter(
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8,
                4096);

            _csv.WriteLine("seconds,simulated_clients,packets_per_sec,kb_per_sec");
            _csv.Flush();
        }

        private void Update()
        {
            if (!_initialized)
                return;

            _secondTimer += Time.unscaledDeltaTime;

            if (_secondTimer >= 1f)
            {
                LogSecond();
                _secondTimer = 0f;
                _bytesThisSecond = 0;
                _packetsThisSecond = 0;
            }
        }

        // --------------------------------------------------------------------
        // CALLED ONLY from LiteNetLibManager send interception
        // --------------------------------------------------------------------
        public void SimulateServerSend(
            byte dataChannel,
            DeliveryMethod deliveryMethod,
            ushort msgType,
            SerializerDelegate serializer)
        {
            _writer.Reset();
            serializer?.Invoke(_writer);

            int size = _writer.Length;
            _bytesThisSecond += (long)size * _clients.Count;
            _packetsThisSecond += _clients.Count;
        }

        public void SimulateServerBroadcast(
            byte dataChannel,
            DeliveryMethod deliveryMethod,
            ushort msgType,
            SerializerDelegate serializer)
        {
            _writer.Reset();
            serializer?.Invoke(_writer);

            int size = _writer.Length;
            _bytesThisSecond += (long)size * _clients.Count;
            _packetsThisSecond += _clients.Count;
        }

        // --------------------------------------------------------------------

        private void LogSecond()
        {
            _secondsElapsed++;

            float kbPerSec = _bytesThisSecond / 1024f;

            _csv.WriteLine(
                $"{_secondsElapsed}," +
                $"{_clients.Count}," +
                $"{_packetsThisSecond}," +
                $"{kbPerSec:0.00}");

            if ((_secondsElapsed % CSV_FLUSH_EVERY) == 0)
                _csv.Flush();
        }

        private void OnDestroy()
        {
            if (_csv != null)
            {
                _csv.Flush();
                _csv.Dispose();
                _csv = null;
            }
        }

        // --------------------------------------------------------------------

        private sealed class SimClient
        {
            public readonly long ConnectionId;

            public SimClient(int index)
            {
                ConnectionId = SIM_CONNECTION_OFFSET + index;
            }
        }
        public void CountFinalSend(int payloadBytes)
{
    if (_clients.Count <= 0)
        return;

    _bytesThisSecond += (long)payloadBytes * _clients.Count;
    _packetsThisSecond += _clients.Count;
}

    }
}
#endif
