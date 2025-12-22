using System;
using System.Collections.Generic;

namespace MultiplayerARPG
{
    /// <summary>
    /// Prevents duplicate/spam item/storage operations for the same player + storage + action.
    /// </summary>
    public static class ItemTransactionGuard
    {
        private static readonly HashSet<string> _active = new HashSet<string>();
        private static readonly Dictionary<string, long> _last = new Dictionary<string, long>();

        private const long RepeatWindowMs = 500; // tune: 300–600ms usually good

        private static string Key(string playerId, string storageKey, string actionKey)
            => $"{playerId}|{storageKey}|{actionKey}";

        public static bool BeginTransaction(string playerId, string storageKey, string actionKey)
        {
            string key = Key(playerId, storageKey, actionKey);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (_last.TryGetValue(key, out long last) && (now - last) < RepeatWindowMs)
                return false;

            if (_active.Contains(key))
                return false;

            _active.Add(key);
            _last[key] = now;
            return true;
        }

        public static void EndTransaction(string playerId, string storageKey, string actionKey)
        {
            _active.Remove(Key(playerId, storageKey, actionKey));
        }
    }
}
