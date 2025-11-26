using Lidgren.Network;
using LmpClient.Systems.SettingsSys;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using LmpCommon.RepoRetrievers;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

namespace LmpClient.Network
{
    public class NetworkSender
    {
        public static ConcurrentQueue<IMessageBase> OutgoingMessages { get; set; } = new ConcurrentQueue<IMessageBase>();

        /// <summary>
        /// Main sending thread
        /// </summary>
        public static void SendMain()
        {
            LunaLog.Log("[LMP]: Send thread started");
            try
            {
                while (!NetworkConnection.ResetRequested)
                {
                    if (OutgoingMessages.Count > 0 && OutgoingMessages.TryDequeue(out var sendMessage))
                    {
                        SendNetworkMessage(sendMessage);
                    }
                    else
                    {
                        Thread.Sleep(SettingsSystem.CurrentSettings.SendReceiveMsInterval);
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Send thread error: {e}");
            }
            LunaLog.Log("[LMP]: Send thread exited");
        }

        /// <summary>
        /// Adds a new message to the queue
        /// </summary>
        public static void QueueOutgoingMessage(IMessageBase message)
        {
            OutgoingMessages.Enqueue(message);
        }

        /// <summary>
        /// Sends the network message. It will skip client messages to send when we are not connected,
        /// except if it's directed at master servers, then it will start the NetClient and socket.
        /// </summary>
        private static void SendNetworkMessage(IMessageBase message)
        {
            message.Data.SentTime = LunaNetworkTime.UtcNow.Ticks;
            try
            {
                if (message is IMasterServerMessageBase)
                {
                    if (NetworkMain.ClientConnection.State == LmpCommon.Network.NetworkConnectionState.Disconnected)
                    {
                        LunaLog.Log("Starting client to send unconnected message");
                        NetworkMain.ClientConnection.Start();
                    }
                    // We don't need to wait for "Running" state as Start() should initialize what's needed
                    // or the connection implementation handles it.
                    // However, for Lidgren specifically, Start() is synchronous for the peer thread start.

                    IPEndPoint[] masterServers;
                    if (string.IsNullOrEmpty(SettingsSystem.CurrentSettings.CustomMasterServer))
                        masterServers = MasterServerRetriever.MasterServers.GetValues;
                    else
                    {
                        masterServers = new[]
                        {
                            LunaNetUtils.CreateEndpointFromString(SettingsSystem.CurrentSettings.CustomMasterServer)
                        };

                    }
                    foreach (var masterServer in masterServers)
                    {
                        // Use SerializationClient to create the message for serialization
                        var lidgrenMsg = NetworkMain.SerializationClient.CreateMessage(message.GetMessageSize());
                        message.Serialize(lidgrenMsg);

                        // Extract bytes and send using the connection interface
                        // Note: SendUnconnectedMessage is not part of INetworkConnection yet,
                        // but for master servers we might need a specific handling or cast if we want to keep using Lidgren directly for this part
                        // OR we should update INetworkConnection to support unconnected messages.
                        // For now, assuming we are still using Lidgren for master server communication or we need to cast.
                        // BUT the task says "Update SendNetworkMessage to use SerializationClient... and call ClientConnection.SendMessageAsync(bytes)"
                        
                        // Since INetworkConnection doesn't support SendUnconnectedMessage, we might need to cast to LidgrenNetworkConnection
                        // or assume this part is handled differently.
                        // However, the prompt specifically asked to use SerializationClient and ClientConnection.SendMessageAsync.
                        // But SendMessageAsync implies a connected state.
                        
                        // For Master Server messages (unconnected), we might need to use the underlying Lidgren client if available,
                        // or if we are strictly following the interface, we might need to add support for it.
                        // Given the constraints, let's check if we can cast or if we should use a different approach.
                        
                        // Actually, for Master Servers, we are sending to specific endpoints.
                        // INetworkConnection doesn't have SendUnconnectedMessage.
                        // Let's use the SerializationClient to send unconnected messages for now as it is a NetClient,
                        // OR we cast ClientConnection to LidgrenNetworkConnection if we want to use the main connection.
                        // But ClientConnection is now INetworkConnection.
                        
                        // Let's use the SerializationClient for creating the message, and then we need to send it.
                        // Since SerializationClient is a NetClient, we can use it to send unconnected messages directly!
                        // It just needs to be started.
                        
                        if (NetworkMain.SerializationClient.Status == NetPeerStatus.NotRunning)
                            NetworkMain.SerializationClient.Start();

                        NetworkMain.SerializationClient.SendUnconnectedMessage(lidgrenMsg, masterServer);
                    }
                    // Force send of packets
                    NetworkMain.SerializationClient.FlushSendQueue();
                }
                else
                {
                    if (NetworkMain.ClientConnection == null || NetworkMain.ClientConnection.State == LmpCommon.Network.NetworkConnectionState.Disconnected
                        || MainSystem.NetworkState < ClientState.Connected)
                    {
                        return;
                    }
                    
                    // Use SerializationClient to create the message for serialization
                    var lidgrenMsg = NetworkMain.SerializationClient.CreateMessage(message.GetMessageSize());
                    message.Serialize(lidgrenMsg);
                    
                    // Extract bytes
                    var data = new byte[lidgrenMsg.LengthBytes];
                    lidgrenMsg.ReadBytes(data, 0, lidgrenMsg.LengthBytes);

                    // Send using the interface
                    NetworkMain.ClientConnection.SendMessageAsync(data, (LmpCommon.Network.DeliveryMethod)message.NetDeliveryMethod, message.Channel);
                    
                    // Force send of packets
                    NetworkMain.ClientConnection.FlushSendQueue();
                }

                message.Recycle();
            }
            catch (Exception e)
            {
                NetworkMain.HandleDisconnectException(e);
            }
        }
    }
}
