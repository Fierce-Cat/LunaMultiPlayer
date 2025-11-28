using LmpCommon.Locks;
using LmpCommon.Message.Data.Lock;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using System.Linq;

namespace Server.System
{
    public class LockSystemSender
    {
        public static void SendAllLocks(ClientStructure client)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockListReplyMsgData>();
            msgData.Locks = LockSystem.LockQuery.GetAllLocks().ToArray();
            msgData.LocksCount = msgData.Locks.Length;

            MessageQueuer.SendToClient<LockSrvMsg>(client, msgData);
        }

        public static void ReleaseAndSendLockReleaseMessage(ClientStructure client, LockDefinition lockDefinition)
        {
            var lockReleaseResult = LockSystem.ReleaseLock(lockDefinition);
            if (lockReleaseResult)
            {
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockReleaseMsgData>();
                msgData.Lock = lockDefinition;
                msgData.LockResult = true;

                MessageQueuer.RelayMessage<LockSrvMsg>(client, msgData);
                LunaLog.Debug($"{lockDefinition.PlayerName} released lock {lockDefinition}");
            }
            else
            {
                SendStoredLockData(client, lockDefinition);
                LunaLog.Debug($"{lockDefinition.PlayerName} failed to release lock {lockDefinition}");
            }
        }

        public static void SendLockAcquireMessage(ClientStructure client, LockDefinition lockDefinition, bool force)
        {
            // Check if this is an Update lock that will take over from an UnloadedUpdate lock
            LockDefinition releasedUnloadedLock = null;
            if (lockDefinition.Type == LockType.Update &&
                LockSystem.LockQuery.UnloadedUpdateLockExists(lockDefinition.VesselId) &&
                !LockSystem.LockQuery.UpdateLockExists(lockDefinition.VesselId) &&
                !LockSystem.LockQuery.UnloadedUpdateLockBelongsToPlayer(lockDefinition.VesselId, lockDefinition.PlayerName))
            {
                // Store the current UnloadedUpdate lock holder so we can notify about the release
                releasedUnloadedLock = LockSystem.LockQuery.GetUnloadedUpdateLock(lockDefinition.VesselId);
            }

            if (LockSystem.AcquireLock(lockDefinition, force, out var repeatedAcquire))
            {
                // If we took over from UnloadedUpdate, broadcast the release and new acquire
                if (releasedUnloadedLock != null)
                {
                    // Notify about the old owner losing their UnloadedUpdate lock
                    var releaseMsgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockReleaseMsgData>();
                    releaseMsgData.Lock = releasedUnloadedLock;
                    releaseMsgData.LockResult = true;
                    MessageQueuer.SendToAllClients<LockSrvMsg>(releaseMsgData);
                    LunaLog.Debug($"{releasedUnloadedLock.PlayerName} lost UnloadedUpdate lock for vessel {lockDefinition.VesselId} - taken by {lockDefinition.PlayerName}");

                    // Broadcast the new UnloadedUpdate lock ownership
                    var unloadedUpdateMsgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockAcquireMsgData>();
                    unloadedUpdateMsgData.Lock = new LockDefinition(LockType.UnloadedUpdate, lockDefinition.PlayerName, lockDefinition.VesselId);
                    unloadedUpdateMsgData.Force = false;
                    MessageQueuer.SendToAllClients<LockSrvMsg>(unloadedUpdateMsgData);
                }

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockAcquireMsgData>();
                msgData.Lock = lockDefinition;
                msgData.Force = force;

                MessageQueuer.SendToAllClients<LockSrvMsg>(msgData);

                //Just log it if we actually changed the value. Users might send repeated acquire locks as they take a bit of time to reach them...
                if (!repeatedAcquire)
                    LunaLog.Debug($"{lockDefinition.PlayerName} acquired lock {lockDefinition}");
            }
            else
            {
                SendStoredLockData(client, lockDefinition);
                LunaLog.Debug($"{lockDefinition.PlayerName} failed to acquire lock {lockDefinition}");
            }
        }

        /// <summary>
        /// Whenever a release/acquire lock fails, call this method to relay the correct lock definition to the player
        /// </summary>
        private static void SendStoredLockData(ClientStructure client, LockDefinition lockDefinition)
        {
            var storedLockDef = LockSystem.LockQuery.GetLock(lockDefinition.Type, lockDefinition.PlayerName, lockDefinition.VesselId, lockDefinition.KerbalName);
            if (storedLockDef != null)
            {
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<LockAcquireMsgData>();
                msgData.Lock = storedLockDef;
                MessageQueuer.SendToClient<LockSrvMsg>(client, msgData);
            }
        }
    }
}
