using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerARPG
{
    public sealed class AsyncAutosaveService
    {
        private CancellationTokenSource _cts;
        private Task _task;
        private readonly object _lock = new object();
        private WorldSaveSnapshot _pending;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _task = Task.Run(WorkerLoop, _cts.Token);
        }

        public void StopAndFlush()
        {
            _cts?.Cancel();
            _task?.Wait();
        }

        public void Enqueue(WorldSaveSnapshot snapshot)
        {
            lock (_lock)
            {
                _pending = snapshot; // last-write-wins
            }
        }

        private async Task WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                WorldSaveSnapshot snapshot = null;

                lock (_lock)
                {
                    snapshot = _pending;
                    _pending = null;
                }

                if (snapshot != null)
                {
                    Save(snapshot);
                }

                await Task.Delay(500); // autosave cadence (tune as needed)
            }
        }

        private void Save(WorldSaveSnapshot snapshot)
        {
            // NO Unity API calls here

            WorldSaveData world = new WorldSaveData();
            world.buildings.AddRange(snapshot.buildings);
            world.SavePersistentData("world", "map");

            StorageSaveData storage = new StorageSaveData();
            storage.storageItems.AddRange(snapshot.storageItems);
            storage.SavePersistentData("storage");

            SummonBuffsSaveData buffs = new SummonBuffsSaveData();
            buffs.summonBuffs.AddRange(snapshot.summonBuffs);
            buffs.SavePersistentData("buffs");
        }
    }
}
