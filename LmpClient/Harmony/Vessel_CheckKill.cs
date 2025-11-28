using HarmonyLib;
using LmpClient.Extensions;
using LmpClient.Systems.VesselPositionSys;
using LmpCommon.Enums;
using System;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// This harmony patch is intended to avoid run the kill checks in vessels that are immortal.
    /// It also protects against NullReferenceExceptions that can occur when CheckKill is called
    /// on crewless vessels (like asteroids) that crash through terrain.
    /// </summary>
    [HarmonyPatch(typeof(Vessel))]
    [HarmonyPatch("CheckKill")]
    public class Vessel_CheckKill
    {
        [HarmonyPrefix]
        private static bool PrefixCheckKill(Vessel __instance)
        {
            if (MainSystem.NetworkState < ClientState.Connected || !__instance) return true;

            if (__instance.IsImmortal())
                return false;

            //The vessel have updates queued as it was left there by a player in a future subspace
            if (VesselPositionSystem.Singleton.VesselHavePositionUpdatesQueued(__instance.id))
                return false;

            // Skip kill checks for asteroids/comets to prevent NullReferenceException cascade
            // when they crash through terrain (they don't have crew to murder)
            if (__instance.IsCometOrAsteroid())
            {
                // Let the vessel be destroyed but skip crew-related checks by returning false
                // and handling the destruction ourselves
                try
                {
                    if (__instance.state == Vessel.State.DEAD)
                        return false;
                }
                catch (Exception e)
                {
                    LunaLog.LogWarning($"[LMP]: Error checking asteroid/comet state for {__instance.id}: {e.Message}");
                    return false;
                }
            }

            return true;
        }
    }
}
