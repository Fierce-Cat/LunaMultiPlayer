using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.VesselUtilities;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Interface;
using System.Collections.Concurrent;

namespace LmpClient.Systems.VesselPositionSys
{
    public class VesselPositionMessageHandler : SubSystem<VesselPositionSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is VesselPositionMsgData msgData)) return;

            var vesselId = msgData.VesselId;
            if (!VesselCommon.DoVesselChecks(vesselId))
                return;

            // Validate body index before processing position update
            // This prevents issues when vessels reference celestial bodies from mods that aren't installed
            if (!ValidateBodyIndex(msgData.BodyIndex, vesselId))
                return;

            if (!VesselPositionSystem.CurrentVesselUpdate.ContainsKey(vesselId))
            {
                VesselPositionSystem.CurrentVesselUpdate.TryAdd(vesselId, new VesselPositionUpdate(msgData));
                VesselPositionSystem.TargetVesselUpdateQueue.TryAdd(vesselId, new PositionUpdateQueue());
            }
            else
            {
                VesselPositionSystem.TargetVesselUpdateQueue.TryGetValue(vesselId, out var queue);
                queue?.Enqueue(msgData);
            }
        }

        /// <summary>
        /// Validates that the body index in the position message is valid
        /// </summary>
        private static bool ValidateBodyIndex(int bodyIndex, System.Guid vesselId)
        {
            if (FlightGlobals.Bodies == null)
                return false;

            if (bodyIndex < 0 || bodyIndex >= FlightGlobals.Bodies.Count)
            {
                LunaLog.LogWarning($"[LMP]: Ignoring position update for vessel {vesselId} - Invalid body index {bodyIndex}. " +
                    $"Max valid index is {FlightGlobals.Bodies.Count - 1}.");
                return false;
            }

            return true;
        }
    }
}
