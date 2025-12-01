using System;
using System.Collections.Generic;

namespace Server.System
{
    public static class VesselContext
    {
        public static List<Guid> RemovedVessels { get; } = new List<Guid>();

        public static void RemoveTrackedVessel(Guid vesselId)
        {
            lock (RemovedVessels)
            {
                RemovedVessels.Remove(vesselId);
            }
        }
    }
}
