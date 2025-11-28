using LmpClient.Extensions;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Utilities;
using LmpClient.VesselUtilities;
using System;

namespace LmpClient.Systems.VesselProtoSys
{
    public class VesselProto
    {
        public Guid VesselId;
        public byte[] RawData = new byte[0];
        public int NumBytes;
        public double GameTime;
        public bool ForceReload;

        public Vessel LoadVessel()
        {
            return null;
        }

        public ProtoVessel CreateProtoVessel()
        {
            var configNode = RawData.DeserializeToConfigNode(NumBytes);
            if (configNode == null || configNode.VesselHasNaNPosition())
            {
                LunaLog.LogError($"[LMP]: Received a malformed vessel from SERVER. Id {VesselId}");
                VesselRemoveSystem.Singleton.KillVessel(VesselId, true, "Malformed vessel");
                return null;
            }

            // Validate orbit body index before attempting to create ProtoVessel
            // This prevents ArgumentOutOfRangeException in OrbitSnapshot.Load()
            var maxBodyIndex = FlightGlobals.Bodies != null ? FlightGlobals.Bodies.Count - 1 : -1;
            if (configNode.VesselHasInvalidOrbitBodyIndex(maxBodyIndex))
            {
                var orbitNode = configNode.GetNode("ORBIT");
                var bodyIndex = orbitNode?.GetValue("REF") ?? "unknown";
                var vesselName = configNode.GetValue("name") ?? "Unknown";
                LunaLog.LogWarning($"[LMP]: Skipping vessel {vesselName} ({VesselId}) - Invalid orbit body index {bodyIndex}. " +
                    $"Max valid index is {maxBodyIndex}. The vessel may reference a body from an unsupported mod.");
                return null;
            }

            var newProto = VesselSerializer.CreateSafeProtoVesselFromConfigNode(configNode, VesselId);
            if (newProto == null)
            {
                LunaLog.LogError($"[LMP]: Received a malformed vessel from SERVER. Id {VesselId}");
                VesselRemoveSystem.Singleton.KillVessel(VesselId, true, "Malformed vessel");
                return null;
            }

            return newProto;
        }
    }
}
