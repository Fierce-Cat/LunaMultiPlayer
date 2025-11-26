using LmpCommon.Message.Base;

namespace LmpClient.Network
{
    public class NetworkStatistics
    {
        public static volatile float PingSec;
        public static float AvgPingSec => (float)NetworkMain.ClientConnection.LatencyMs / 1000f;
        public static int SentBytes => (int)NetworkMain.ClientConnection.Statistics.BytesSent;
        public static int ReceivedBytes => (int)NetworkMain.ClientConnection.Statistics.BytesReceived;
        // TimeOffset is specific to Lidgren's clock sync.
        // For now, we can try to get it if the connection is Lidgren, or default to 0.
        // Ideally, INetworkConnection should expose this if needed, or we handle time sync differently.
        public static float TimeOffset
        {
            get
            {
                if (NetworkMain.ClientConnection is LmpClient.Network.Adapters.LidgrenNetworkConnection lidgrenConn)
                {
                    // We need to access the underlying NetClient to get RemoteTimeOffset
                    // But LidgrenNetworkConnection doesn't expose it directly.
                    // However, we can use reflection or cast if we made the field public/internal.
                    // Or we can just return 0 for now as this might be refactored later.
                    // Wait, LidgrenNetworkConnection is in the same assembly.
                    // But _client is private.
                    
                    // Let's check if we can access it via reflection or if we should just return 0.
                    // Returning 0 might break time sync.
                    
                    // Actually, NetworkMain.ClientConnection is INetworkConnection.
                    // If we cast it to LidgrenNetworkConnection, we still can't access _client.
                    
                    // BUT, we have NetworkMain.SerializationClient which is a NetClient.
                    // But that one is not connected.
                    
                    // Let's look at how TimeOffset is used. It's used for clock sync.
                    // If we are moving to Nakama, Nakama has its own time sync.
                    
                    // For now, let's try to keep it working for Lidgren.
                    // We can add TimeOffset to INetworkConnection? No, that's changing the interface which might be shared.
                    // But wait, I can't change INetworkConnection easily if it's in LmpCommon.
                    
                    // Let's assume for this refactoring step, we might lose this specific metric display
                    // or we need to expose it in LidgrenNetworkConnection.
                    
                    // Let's check if we can use reflection to get the field from LidgrenNetworkConnection.
                    var field = typeof(LmpClient.Network.Adapters.LidgrenNetworkConnection).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var client = field.GetValue(lidgrenConn) as global::Lidgren.Network.NetClient;
                        return client?.ServerConnection?.RemoteTimeOffset ?? 0;
                    }
                }
                return 0;
            }
        }
        public static int MessagesInCache => MessageStore.GetMessageCount(null);
        public static int MessageDataInCache => MessageStore.GetMessageDataCount(null);
    }
}
