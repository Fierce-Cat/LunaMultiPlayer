using Lidgren.Network;
using LmpClient.Base;
using LmpClient.ModuleStore.Patching;
using LmpClient.Network.Adapters;
using LmpClient.Systems.Nakama;
using LmpClient.Systems.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using System;
using System.Net;
using System.Threading;
using UniLinq;

namespace LmpClient.Network
{
    public class NetworkConnection
    {
        private static readonly object DisconnectLock = new object();
        public static volatile bool ResetRequested;

        /// <summary>
        /// Disconnects the network system. You should kill threads ONLY from main thread
        /// </summary>
        /// <param name="reason">Reason</param>
        public static void Disconnect(string reason = "unknown")
        {
            lock (DisconnectLock)
            {
                if (MainSystem.NetworkState > ClientState.Disconnected)
                {
                    //DO NOT set networkstate as disconnected as we are in another thread!
                    MainSystem.NetworkState = ClientState.DisconnectRequested;

                    LunaLog.Log($"[LMP]: Disconnected, reason: {reason}");
                    if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                    {
                        MainSystem.Singleton.ForceQuit = true;
                    }
                    else
                    {
                        //User is in flight so just display a message but don't force them to main menu...
                        NetworkSystem.DisplayDisconnectMessage = true;
                    }

                    MainSystem.Singleton.Status = $"Disconnected: {reason}";

                    NetworkMain.ClientConnection.Disconnect(reason);
                    NetworkMain.ClientConnection.Shutdown();
                    NetworkMain.ResetConnectionStaticsAndQueues();
                }
            }
        }

        public static void ConnectToServer(string hostname, int port, string password)
        {
            var endpoints = LunaNetUtils.CreateAddressFromString(hostname)
                .Select(addr => new IPEndPoint(addr, port))
                .ToArray();
            if (endpoints.Length == 0)
            {
                MainSystem.Singleton.Status = "Hostname resolution failed, check for typos";
                LunaLog.LogError("[LMP]: Hostname resolution failed, check for typos");
                Disconnect("Hostname resolution failed");
            }
            ConnectToServer(endpoints, password);
        }

        public static void ConnectToServer(IPEndPoint[] endpoints, string password)
        {
            if (MainSystem.NetworkState > ClientState.Disconnected || endpoints == null || endpoints.Length == 0)
                return;

            MainSystem.NetworkState = ClientState.Connecting;

            SystemBase.TaskFactory.StartNew(() =>
            {
                while (!PartModuleRunner.Ready)
                {
                    MainSystem.Singleton.Status = $"Patching part modules (runs on every restart). {PartModuleRunner.GetPercentage()}%";
                    Thread.Sleep(50);
                }

                foreach (var endpoint in endpoints)
                {
                    if (endpoint == null)
                        continue;
                    MainSystem.Singleton.Status = $"Connecting to {endpoint.Address}:{endpoint.Port}";
                    LunaLog.Log($"[LMP]: Connecting to {endpoint.Address} port {endpoint.Port}");

                    try
                    {
                        var client = NetworkMain.ClientConnection;

                        client.Start();

                        var connected = client.ConnectAsync(new[] { endpoint }, password).Result;

                        if (connected)
                        {
                            LunaLog.Log($"[LMP]: Connected to {endpoint.Address}:{endpoint.Port}");
                            MainSystem.NetworkState = ClientState.Connected;
                            break;
                        }
                        else
                        {
                            LunaLog.Log($"[LMP]: Initial connection timeout to {endpoint.Address}:{endpoint.Port}");
                            client.Disconnect("Initial connection timeout");
                        }
                    }
                    catch (Exception e)
                    {
                        NetworkMain.HandleDisconnectException(e);
                    }
                }

                if (MainSystem.NetworkState < ClientState.Connected)
                {
                    Disconnect(MainSystem.NetworkState == ClientState.Connecting ? "Initial connection timeout" : "Cancelled connection");
                }
            });
        }

        public static void ConnectToMatch(NakamaMatchSelection selection)
        {
            if (selection?.Summary == null)
            {
                MainSystem.Singleton.Status = "Invalid match selection";
                LunaLog.LogError("[LMP]: Invalid Nakama match selection");
                return;
            }

            if (!(NetworkMain.ClientConnection is NakamaNetworkConnection nakamaConnection))
            {
                MainSystem.Singleton.Status = "Nakama backend not available";
                LunaLog.LogError("[LMP]: Nakama backend not enabled. Cannot join match.");
                return;
            }

            if (MainSystem.NetworkState > ClientState.Disconnected)
                return;

            MainSystem.NetworkState = ClientState.Connecting;

            SystemBase.TaskFactory.StartNew(() =>
            {
                while (!PartModuleRunner.Ready)
                {
                    MainSystem.Singleton.Status = $"Patching part modules (runs on every restart). {PartModuleRunner.GetPercentage()}%";
                    Thread.Sleep(50);
                }

                try
                {
                    MainSystem.Singleton.Status = $"Joining match {selection.Summary.Name}";
                    LunaLog.Log($"[LMP]: Joining Nakama match {selection.MatchId}");

                    nakamaConnection.Start();
                    var connected = nakamaConnection.ConnectToMatchAsync(selection).Result;

                    if (connected)
                    {
                        LunaLog.Log($"[LMP]: Joined Nakama match {selection.MatchId}");
                        MainSystem.NetworkState = ClientState.Connected;
                    }
                    else
                    {
                        LunaLog.LogError("[LMP]: Unable to join Nakama match");
                        nakamaConnection.Disconnect("Failed to join match");
                        Disconnect("Failed to join match");
                    }
                }
                catch (Exception e)
                {
                    NetworkMain.HandleDisconnectException(e);
                }
            });
        }
    }
}
