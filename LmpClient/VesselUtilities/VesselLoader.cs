using KSP.UI.Screens.Flight;
using LmpClient.Extensions;
using LmpClient.Systems.VesselPositionSys;
using System;
using Object = UnityEngine.Object;

namespace LmpClient.VesselUtilities
{
    public class VesselLoader
    {
        /// <summary>
        /// Loads/Reloads a vessel into game
        /// </summary>
        public static bool LoadVessel(ProtoVessel vesselProto, bool forceReload)
        {
            try
            {
                return vesselProto.Validate() && LoadVesselIntoGame(vesselProto, forceReload);
            }
            catch (ArgumentOutOfRangeException e)
            {
                // This typically happens when the vessel references a celestial body that doesn't exist
                // (e.g., from a mod that isn't installed)
                LunaLog.LogError($"[LMP]: Error loading vessel {vesselProto?.vesselID} - Invalid body reference: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error loading vessel: {e}");
                return false;
            }
        }

        #region Private methods

        /// <summary>
        /// Loads the vessel proto into the current game
        /// </summary>
        private static bool LoadVesselIntoGame(ProtoVessel vesselProto, bool forceReload)
        {
            if (HighLogic.CurrentGame?.flightState == null)
                return false;

            var reloadingOwnVessel = FlightGlobals.ActiveVessel && vesselProto.vesselID == FlightGlobals.ActiveVessel.id;

            //In case the vessel exists, silently remove them from unity and recreate it again
            var existingVessel = FlightGlobals.FindVessel(vesselProto.vesselID);
            if (existingVessel != null)
            {
                if (!forceReload && existingVessel.Parts.Count == vesselProto.protoPartSnapshots.Count &&
                    GetCrewCountSafe(existingVessel) == GetVesselCrewCountSafe(vesselProto))
                {
                    return true;
                }

                LunaLog.Log($"[LMP]: Reloading vessel {vesselProto.vesselID}");
                if (reloadingOwnVessel)
                {
                    try
                    {
                        existingVessel.RemoveAllCrew();
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogWarning($"[LMP]: Error removing crew from vessel {vesselProto.vesselID}: {e.Message}");
                    }
                }

                FlightGlobals.RemoveVessel(existingVessel);
                foreach (var part in existingVessel.parts)
                {
                    Object.Destroy(part.gameObject);
                }
                Object.Destroy(existingVessel.gameObject);
            }
            else
            {
                LunaLog.Log($"[LMP]: Loading vessel {vesselProto.vesselID}");
            }

            try
            {
                vesselProto.Load(HighLogic.CurrentGame.flightState);
            }
            catch (ArgumentOutOfRangeException e)
            {
                // This happens when OrbitSnapshot.Load() tries to access an invalid body index
                LunaLog.LogError($"[LMP]: Failed to load vessel {vesselProto.vesselID} ({vesselProto.vesselName}) - " +
                    $"Invalid celestial body reference. The vessel may reference a body from an unsupported mod. Error: {e.Message}");
                return false;
            }

            if (vesselProto.vesselRef == null)
            {
                LunaLog.Log($"[LMP]: Protovessel {vesselProto.vesselID} failed to create a vessel!");
                return false;
            }

            VesselPositionSystem.Singleton.ForceUpdateVesselPosition(vesselProto.vesselRef.id);

            vesselProto.vesselRef.protoVessel = vesselProto;
            if (vesselProto.vesselRef.isEVA)
            {
                var evaModule = vesselProto.vesselRef.FindPartModuleImplementing<KerbalEVA>();
                if (evaModule != null && evaModule.fsm != null && !evaModule.fsm.Started)
                {
                    evaModule.fsm?.StartFSM("Idle (Grounded)");
                }
                vesselProto.vesselRef.GoOnRails();
            }

            if (vesselProto.vesselRef.situation > Vessel.Situations.PRELAUNCH)
            {
                try
                {
                    vesselProto.vesselRef.orbitDriver.updateFromParameters();
                }
                catch (Exception e)
                {
                    LunaLog.LogWarning($"[LMP]: Error updating orbit for vessel {vesselProto.vesselID}: {e.Message}");
                }
            }

            if (vesselProto.vesselRef.orbitDriver != null && double.IsNaN(vesselProto.vesselRef.orbitDriver.pos.x))
            {
                LunaLog.Log($"[LMP]: Protovessel {vesselProto.vesselID} has an invalid orbit");
                return false;
            }

            if (reloadingOwnVessel)
            {
                try
                {
                    vesselProto.vesselRef.Load();
                    vesselProto.vesselRef.RebuildCrewList();

                    //Do not do the setting of the active vessel manually, too many systems are dependant of the events triggered by KSP
                    FlightGlobals.ForceSetActiveVessel(vesselProto.vesselRef);

                    vesselProto.vesselRef.SpawnCrew();
                    foreach (var crew in vesselProto.vesselRef.GetVesselCrew())
                    {
                        if (crew != null)
                        {
                            ProtoCrewMember._Spawn(crew);
                            if (crew.KerbalRef)
                                crew.KerbalRef.state = Kerbal.States.ALIVE;
                        }
                    }

                    if (KerbalPortraitGallery.Instance != null && 
                        KerbalPortraitGallery.Instance.ActiveCrewItems.Count != GetCrewCountSafe(vesselProto.vesselRef))
                    {
                        KerbalPortraitGallery.Instance.StartReset(FlightGlobals.ActiveVessel);
                    }
                }
                catch (Exception e)
                {
                    LunaLog.LogWarning($"[LMP]: Error during vessel crew reload for {vesselProto.vesselID}: {e.Message}");
                }
            }

            return true;
        }

        /// <summary>
        /// Safely gets crew count from a vessel, handling null cases
        /// </summary>
        private static int GetCrewCountSafe(Vessel vessel)
        {
            try
            {
                return vessel?.GetCrewCount() ?? 0;
            }
            catch (NullReferenceException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Safely gets crew count from a proto vessel, handling null cases
        /// </summary>
        private static int GetVesselCrewCountSafe(ProtoVessel protoVessel)
        {
            try
            {
                return protoVessel?.GetVesselCrew()?.Count ?? 0;
            }
            catch (NullReferenceException)
            {
                return 0;
            }
        }

        #endregion
    }
}
