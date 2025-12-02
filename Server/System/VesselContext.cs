using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Server.System
{
    public static class VesselContext
    {
        // Keep a short-lived, thread-safe kill list so late packets referencing deleted vessels are ignored
        // while also throttling cleanup to avoid constant dictionary scans under heavy load.
        private const int KillListTtlMs = 2500;
        private const int MinCleanupIntervalMs = 500;

        private static readonly TimeSpan KillListTtl = TimeSpan.FromMilliseconds(KillListTtlMs);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMilliseconds(MinCleanupIntervalMs);

        private static readonly ConcurrentDictionary<Guid, DateTime> RemovedVessels = new ConcurrentDictionary<Guid, DateTime>();
        private static long _lastCleanupTicks;

        public static void TrackRemovedVessel(Guid vesselId)
        {
            CleanupExpiredEntries();
            RemovedVessels.AddOrUpdate(vesselId, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
        }

        public static bool IsVesselRemoved(Guid vesselId)
        {
            CleanupExpiredEntries();
            return RemovedVessels.ContainsKey(vesselId);
        }

        public static void RemoveTrackedVessel(Guid vesselId)
        {
            RemovedVessels.TryRemove(vesselId, out _);
        }

        private static void CleanupExpiredEntries()
        {
            var now = DateTime.UtcNow;
            var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);

            if (now.Ticks - lastCleanup < CleanupInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, lastCleanup) != lastCleanup)
                return;

            var expiration = now - KillListTtl;
            foreach (var entry in RemovedVessels)
            {
                if (entry.Value < expiration)
                {
                    RemovedVessels.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}
