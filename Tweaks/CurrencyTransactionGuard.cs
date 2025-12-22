using System;
using System.Collections.Generic;

namespace MultiplayerARPG
{
    public static class CurrencyTransactionGuard
    {
        private static readonly HashSet<string> _activeTransactions = new HashSet<string>();
        private static readonly Dictionary<string, long> _lastTransactionTime = new Dictionary<string, long>();
        private const long RepeatBlockWindowMs = 500; // 0.5 second, adjust as needed

        public static bool BeginTransaction(string playerId, string actionKey)
        {
            string key = $"{playerId}-{actionKey}";
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_lastTransactionTime.TryGetValue(key, out long lastTime))
            {
                if (now - lastTime < RepeatBlockWindowMs)
                    return false; // Too soon since last transaction
            }

            if (_activeTransactions.Contains(key))
                return false; // Already in progress

            _activeTransactions.Add(key);
            _lastTransactionTime[key] = now;
            return true;
        }

        public static void EndTransaction(string playerId, string actionKey)
        {
            string key = $"{playerId}-{actionKey}";
            _activeTransactions.Remove(key);
        }
    }
}
