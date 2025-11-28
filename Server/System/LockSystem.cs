using LmpCommon.Locks;
using Server.Client;
using System.Linq;

namespace Server.System
{
    public class LockSystem
    {
        private static readonly LockStore LockStore = new LockStore();
        public static readonly LockQuery LockQuery = new LockQuery(LockStore);

        public static bool AcquireLock(LockDefinition lockDef, bool force, out bool repeatedAcquire)
        {
            repeatedAcquire = false;

            //Player tried to acquire a lock that they already own
            if (LockQuery.LockBelongsToPlayer(lockDef.Type, lockDef.VesselId, lockDef.KerbalName, lockDef.PlayerName))
            {
                repeatedAcquire = true;
                return true;
            }

            // Special handling for Update lock requests - they can take over from UnloadedUpdate locks
            // This implements the behavior documented in LockType.cs where a player with a loaded vessel
            // (running physics) should have priority over a player with an unloaded vessel
            if (lockDef.Type == LockType.Update)
            {
                // Case 1: Update lock already exists - another player has vessel loaded
                if (LockQuery.UpdateLockExists(lockDef.VesselId))
                {
                    // IMPORTANT: Allow force=true to override (needed for Control lock cascade)
                    // When a player takes Control of a vessel, they must be able to take the Update lock
                    if (force)
                    {
                        LockStore.AddOrUpdateLock(lockDef);
                        return true;
                    }
                    // Deny without force (first loader keeps priority)
                    return false;
                }

                // Case 2: Only UnloadedUpdate lock exists - owner has vessel unloaded
                // Requester has vessel loaded, they should get priority (loaded vessel takes over)
                if (LockQuery.UnloadedUpdateLockExists(lockDef.VesselId))
                {
                    // Grant Update lock to requester
                    LockStore.AddOrUpdateLock(lockDef);

                    // Also grant UnloadedUpdate lock to requester (Update owner should have both)
                    LockStore.AddOrUpdateLock(new LockDefinition(
                        LockType.UnloadedUpdate,
                        lockDef.PlayerName,
                        lockDef.VesselId));

                    return true;
                }

                // Case 3: No Update or UnloadedUpdate locks exist - grant the Update lock
                LockStore.AddOrUpdateLock(lockDef);
                return true;
            }

            if (force || !LockQuery.LockExists(lockDef))
            {
                if (lockDef.Type == LockType.Control)
                {
                    //If they acquired a control lock they probably switched vessels or something like that and they can only have one control lock.
                    //So remove the other control locks just for safety...
                    var controlLocks = LockQuery.GetAllPlayerLocks(lockDef.PlayerName)
                    .Where(l => l.Type == LockType.Control)
                    .ToArray();

                    foreach (var control in controlLocks)
                    {
                        // ReleaseLock(control);
                        //If releaseLock failed, it means the player didn't own the lock anymore
                        //But since they are acquiring a new control lock, we can just remove it forcefully
                        if (!ReleaseLock(control))
                        {
                            LockStore.RemoveLock(control);
                        }
                    }
                        
                }

                LockStore.AddOrUpdateLock(lockDef);
                return true;
            }
            return false;
        }

        public static bool ReleaseLock(LockDefinition lockDef)
        {
            if (LockQuery.LockBelongsToPlayer(lockDef.Type, lockDef.VesselId, lockDef.KerbalName, lockDef.PlayerName))
            {
                LockStore.RemoveLock(lockDef);
                return true;
            }

            return false;
        }

        public static void ReleasePlayerLocks(ClientStructure client)
        {
            var removeList = LockQuery.GetAllPlayerLocks(client.PlayerName);

            foreach (var lockToRemove in removeList)
            {
                LockSystemSender.ReleaseAndSendLockReleaseMessage(client, lockToRemove);
            }
        }
    }
}
