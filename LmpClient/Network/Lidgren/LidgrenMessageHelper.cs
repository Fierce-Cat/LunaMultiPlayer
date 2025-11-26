using Lidgren.Network;
using System;
using System.Reflection;

namespace LmpClient.Network.Lidgren
{
    /// <summary>
    /// Helper class to bridge generic byte[] data to Lidgren's NetIncomingMessage.
    /// This is necessary because Lidgren's NetIncomingMessage constructor is internal,
    /// and we need to create instances of it when receiving data from the generic INetworkConnection.
    /// </summary>
    public static class LidgrenMessageHelper
    {
        private static readonly ConstructorInfo NetIncomingMessageConstructor;

        static LidgrenMessageHelper()
        {
            // Get the internal constructor of NetIncomingMessage that takes a NetIncomingMessageType
            NetIncomingMessageConstructor = typeof(NetIncomingMessage).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(NetIncomingMessageType) },
                null);

            if (NetIncomingMessageConstructor == null)
            {
                throw new Exception("Could not find internal constructor for NetIncomingMessage");
            }
        }

        /// <summary>
        /// Creates a NetIncomingMessage from a byte array.
        /// </summary>
        /// <param name="data">The raw data received from the network</param>
        /// <returns>A NetIncomingMessage populated with the data</returns>
        public static NetIncomingMessage CreateMessageFromBytes(byte[] data)
        {
            // Create a new NetIncomingMessage instance
            var msg = (NetIncomingMessage)NetIncomingMessageConstructor.Invoke(new object[] { NetIncomingMessageType.Data });

            // Set the data buffer
            msg.Data = data;
            msg.LengthBytes = data.Length;
            msg.Position = 0;

            return msg;
        }
    }
}