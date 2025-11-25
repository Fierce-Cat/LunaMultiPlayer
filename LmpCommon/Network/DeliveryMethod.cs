namespace LmpCommon.Network
{
    /// <summary>
    /// Defines how messages should be delivered over the network.
    /// This abstraction allows different network backends to map to their own delivery methods.
    /// </summary>
    public enum DeliveryMethod
    {
        /// <summary>
        /// No guarantees. Messages may be lost, duplicated, or arrive out of order.
        /// Use for unimportant messages like heartbeats.
        /// </summary>
        Unreliable,

        /// <summary>
        /// Late messages will be dropped if newer ones were already received.
        /// </summary>
        UnreliableSequenced,

        /// <summary>
        /// All packages will arrive, but not necessarily in the same order.
        /// </summary>
        ReliableUnordered,

        /// <summary>
        /// All packages will arrive, but late ones will be dropped.
        /// This means that we will always receive the latest message eventually, but may miss older ones.
        /// </summary>
        ReliableSequenced,

        /// <summary>
        /// All packages will arrive, and they will do so in the same order.
        /// Unlike all the other methods, here the library will hold back messages until all previous ones are received,
        /// before handing them to us.
        /// </summary>
        ReliableOrdered
    }
}
