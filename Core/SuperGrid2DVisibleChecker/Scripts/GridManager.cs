using SuperGrid2D;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace LiteNetLibManager.SuperGrid2D
{
    /// <summary>
    /// Attach this component to the same game object with LiteNetLibGameManager.
    /// Builds a dynamic spatial grid each AOI update and applies a simple, configurable
    /// per-client cap trimmed by nearest entities: effectiveCap = min(frameCap, networkCap).
    /// - Frame cap: derived from Time.smoothDeltaTime with hysteresis.
    /// - Network cap: optional; RTT-based, using LiteNetLibPlayer.Rtt (no transport edits).
    /// Includes rate-limited debug logs when players near/hit cap.
    /// Exposes GetPlayersInRange() for HarvestManager integration.
    /// </summary>
    public class GridManager : BaseInterestManager
    {
        public enum EAxisMode { XZ, XY }
        private struct CellObject { public uint objectId; public Circle shape; }

        [Header("Grid Settings")]
        public EAxisMode axisMode = EAxisMode.XZ;
        [Tooltip("Cell size used to build the spatial grid")]
        public float cellSize = 100f;
        [Tooltip("Update every ? seconds")]
        public float updateInterval = 1.0f;

        public static EAxisMode AxisMode { get; private set; }
        private float _updateCountDown;
        private readonly List<CellObject> _cellObjects = new List<CellObject>(1024);

        [Header("Frame-Aware Cap")]
        [Min(1)] public int capHealthy = 256;
        [Min(1)] public int capModerate = 124;
        [Min(1)] public int capHeavy = 64;
        [Tooltip("Frame time in ms considered 'healthy' (e.g., 16.7 @60fps)")]
        [Min(1f)] public float targetFrameMs = 16.7f;
        [Tooltip("Seconds of stable readings before changing frame cap bucket")]
        [Min(0.1f)] public float capHysteresisSeconds = 2.0f;

        [Header("RTT-based Network Tie-In")]
        [Tooltip("If ON, also tighten cap when a client's RTT rises")]
        public bool tieToNetworkCaps = true;
        [Tooltip("RTT <= this is healthy")]
        [Min(1)] public int rttHealthyMs = 80;
        [Tooltip("RTT <= this is moderate (above is heavy)")]
        [Min(1)] public int rttModerateMs = 160;

        [Header("Harvest System Integration")]
        [Tooltip("Expose simple AOI API for Harvest system (GetPlayersInRange)")]
        public bool enableHarvestIntegration = true;

        [Header("Cap Debug Messages")]
        public bool enableCapDebug = false;
        [Range(0.1f, 1f)] public float capWarnThreshold = 0.8f;
        [Range(0.1f, 1f)] public float capHitThreshold = 1.0f;
        [Min(0)] public int capDebugSamplePlayers = 3;
        [Min(0.1f)] public float capDebugMinInterval = 1.0f;

        [Header("Telemetry (Read-Only)")]
        [SerializeField] private float lastUpdateMs;
        [SerializeField] private float avgCellsSearched;
        [SerializeField] private float avgUnitsPerCell;
        [SerializeField] private int lastEffectiveCap = 0;
        [SerializeField] private float lastFrameDtMs = 0f;
        [SerializeField] private int lastAvgKeptPerPlayer = 0;

        private int _frameBucket = 0;
        private int _pendingFrameBucket = 0;
        private float _pendingSinceTime = 0f;
        private float _lastCapDebugLogTime = -999f;

        public Vector2 GetPosition(LiteNetLibIdentity identity)
        {
            switch (AxisMode)
            {
                case EAxisMode.XZ: return new Vector2(identity.transform.position.x, identity.transform.position.z);
                case EAxisMode.XY: return new Vector2(identity.transform.position.x, identity.transform.position.y);
            }
            return Vector2.zero;
        }

        public override void Setup(LiteNetLibGameManager manager)
        {
            base.Setup(manager);
            AxisMode = axisMode; // ensure static reflects current instance setting
            _updateCountDown = updateInterval;
            _frameBucket = 0;
            _pendingFrameBucket = 0;
            _pendingSinceTime = Time.time;
        }

        private int GetRawFrameBucket()
        {
            lastFrameDtMs = Time.smoothDeltaTime * 1000f;
            if (lastFrameDtMs <= targetFrameMs) return 0;
            if (lastFrameDtMs <= targetFrameMs * 2f) return 1;
            return 2;
        }

        private void UpdateFrameBucketWithHysteresis()
        {
            int raw = GetRawFrameBucket();
            if (raw != _pendingFrameBucket)
            {
                _pendingFrameBucket = raw;
                _pendingSinceTime = Time.time;
            }
            else if (_frameBucket != _pendingFrameBucket)
            {
                if (Time.time - _pendingSinceTime >= capHysteresisSeconds)
                    _frameBucket = _pendingFrameBucket;
            }
        }

        private int BucketToCap(int bucket)
        {
            switch (bucket)
            {
                case 0: return capHealthy;
                case 1: return capModerate;
                default: return capHeavy;
            }
        }

        // RTT-only network bucket (no transport edits)
        private int GetNetworkBucketForClient(LiteNetLibPlayer player)
        {
            if (!tieToNetworkCaps)
                return 0;

            // If RTT isn't known yet (<= 0), assume healthy
            long rtt = player.Rtt;
            if (rtt <= 0)
                return 0;

            if (rtt <= rttHealthyMs)  return 0; // healthy
            if (rtt <= rttModerateMs) return 1; // moderate
            return 2;                            // heavy
        }

        public override void UpdateInterestManagement(float deltaTime)
        {
            _updateCountDown -= deltaTime;
            if (_updateCountDown > 0)
                return;
            _updateCountDown = updateInterval;

            Profiler.BeginSample("GridManager - Update");
            var t0 = Time.realtimeSinceStartup;

            _cellObjects.Clear();
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            Vector2 tempPosition;
            foreach (LiteNetLibIdentity spawnedObject in Manager.Assets.GetSpawnedObjects())
            {
                if (spawnedObject == null)
                    continue;

                tempPosition = GetPosition(spawnedObject);
                _cellObjects.Add(new CellObject()
                {
                    objectId = spawnedObject.ObjectId,
                    shape = new Circle(tempPosition, GetVisibleRange(spawnedObject)),
                });

                if (tempPosition.x < minX) minX = tempPosition.x;
                if (tempPosition.y < minY) minY = tempPosition.y;
                if (tempPosition.x > maxX) maxX = tempPosition.x;
                if (tempPosition.y > maxY) maxY = tempPosition.y;
            }

            float width = maxX - minX;
            float height = maxY - minY;
            if (width > 0 && height > 0)
            {
                StaticGrid2D<uint> grid = new StaticGrid2D<uint>(new Vector2(minX, minY), width, height, cellSize);
                for (int i = 0; i < _cellObjects.Count; ++i)
                    grid.Add(_cellObjects[i].objectId, _cellObjects[i].shape);

                UpdateFrameBucketWithHysteresis();
                int frameCap = BucketToCap(_frameBucket);
                ApplyCappedSubscriptions(grid, frameCap);

                avgCellsSearched = grid.AverageCellsSearched;
                avgUnitsPerCell = grid.AverageUnitsPerCell;
                lastEffectiveCap = frameCap;
            }

            lastUpdateMs = (Time.realtimeSinceStartup - t0) * 1000f;
            Profiler.EndSample();
        }

        private void ApplyCappedSubscriptions(StaticGrid2D<uint> grid, int frameCap)
        {
            int playersCounted = 0;
            int sumKept = 0;
            int sumCap = 0;
            int perFrameSamplesLeft = capDebugSamplePlayers;
            bool shouldLogThisFrame = enableCapDebug && (Time.time - _lastCapDebugLogTime >= capDebugMinInterval);
            List<string> perPlayerSamples = shouldLogThisFrame ? new List<string>(capDebugSamplePlayers) : null;

            foreach (LiteNetLibPlayer player in Manager.GetPlayers())
            {
                if (player == null || !player.IsReady)
                    continue;

                int netBucket = GetNetworkBucketForClient(player);
                int effectiveCap = Mathf.Min(frameCap, BucketToCap(netBucket));

                foreach (LiteNetLibIdentity playerObject in player.GetSpawnedObjects())
                {
                    if (playerObject == null)
                        continue;

                    var pos = GetPosition(playerObject);
                    List<(uint id, float dist)> candidates = new List<(uint, float)>(256);

                    LiteNetLibIdentity contactedObject;
                    foreach (uint contactedObjectId in grid.Contact(new Point(pos)))
                    {
                        if (Manager.Assets.TryGetSpawnedObject(contactedObjectId, out contactedObject) &&
                            ShouldSubscribe(playerObject, contactedObject, false))
                        {
                            float d = Vector2.Distance(pos, GetPosition(contactedObject));
                            candidates.Add((contactedObjectId, d));
                        }
                    }

                    HashSet<uint> finalSet;
                    int keptCount;
                    if (candidates.Count > effectiveCap)
                    {
                        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                        finalSet = new HashSet<uint>(effectiveCap);
                        for (int i = 0; i < effectiveCap; ++i)
                            finalSet.Add(candidates[i].id);
                        keptCount = effectiveCap;
                    }
                    else
                    {
                        finalSet = new HashSet<uint>(candidates.Count);
                        for (int i = 0; i < candidates.Count; ++i)
                            finalSet.Add(candidates[i].id);
                        keptCount = finalSet.Count;
                    }

                    playerObject.UpdateSubscribings(finalSet);

                    playersCounted++;
                    sumKept += keptCount;
                    sumCap += effectiveCap;

                    if (shouldLogThisFrame && perFrameSamplesLeft > 0 && effectiveCap > 0)
                    {
                        float usage = (float)keptCount / effectiveCap;
                        if (usage >= capWarnThreshold)
                        {
                            int trimmed = Mathf.Max(0, candidates.Count - keptCount);
                            string tag = usage >= capHitThreshold ? "HIT" : "NEAR";
                            perPlayerSamples.Add($"pid={player.ConnectionId} kept={keptCount} cap={effectiveCap} usage={usage:0.00} trimmed={trimmed} [{tag}]");
                            perFrameSamplesLeft--;
                        }
                    }
                }
            }

            if (shouldLogThisFrame && playersCounted > 0)
            {
                int avgKept = Mathf.RoundToInt((float)sumKept / playersCounted);
                int avgCap = Mathf.RoundToInt((float)sumCap / Mathf.Max(1, playersCounted));
                float usage = avgCap > 0 ? (float)avgKept / avgCap : 0f;
                lastAvgKeptPerPlayer = avgKept;

                string aggregate = $"[AOI Cap] players={playersCounted} avgKept={avgKept} avgCap={avgCap} usage={usage:0.00} frameBucket={_frameBucket} dtMs={lastFrameDtMs:0.0}";
                if (perPlayerSamples != null && perPlayerSamples.Count > 0)
                    aggregate += " | samples: " + string.Join("; ", perPlayerSamples);

                Debug.Log(aggregate);
                _lastCapDebugLogTime = Time.time;
            }
        }

        // Harvest integration API
        public List<long> GetPlayersInRange(Vector3 position, float range)
        {
            var result = new List<long>();
            if (!enableHarvestIntegration)
                return result;

            foreach (LiteNetLibPlayer player in Manager.GetPlayers())
            {
                if (player == null || !player.IsReady)
                    continue;

                foreach (LiteNetLibIdentity identity in player.GetSpawnedObjects())
                {
                    float dist = Vector3.Distance(identity.transform.position, position);
                    if (dist <= range)
                    {
                        result.Add(player.ConnectionId);
                        break;
                    }
                }
            }
            return result;
        }
    }
}