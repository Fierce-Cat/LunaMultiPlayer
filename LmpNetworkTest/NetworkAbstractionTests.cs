using LmpCommon.Network;
using NUnit.Framework;

namespace LmpNetworkTest
{
    [TestFixture]
    public class NetworkAbstractionTests
    {
        [Test]
        public void TestDeliveryMethodValues()
        {
            // Verify all delivery methods are defined with expected values
            Assert.That((int)DeliveryMethod.Unreliable, Is.EqualTo(0));
            Assert.That((int)DeliveryMethod.UnreliableSequenced, Is.EqualTo(1));
            Assert.That((int)DeliveryMethod.ReliableUnordered, Is.EqualTo(2));
            Assert.That((int)DeliveryMethod.ReliableSequenced, Is.EqualTo(3));
            Assert.That((int)DeliveryMethod.ReliableOrdered, Is.EqualTo(4));
        }

        [Test]
        public void TestNetworkConnectionStateValues()
        {
            // Verify all connection states are defined with expected values
            Assert.That((int)NetworkConnectionState.Disconnected, Is.EqualTo(0));
            Assert.That((int)NetworkConnectionState.Connecting, Is.EqualTo(1));
            Assert.That((int)NetworkConnectionState.Connected, Is.EqualTo(2));
            Assert.That((int)NetworkConnectionState.Disconnecting, Is.EqualTo(3));
        }

        [Test]
        public void TestNetworkStatisticsBase_InitialValues()
        {
            var stats = new NetworkStatisticsBase();

            Assert.That(stats.BytesSent, Is.EqualTo(0));
            Assert.That(stats.BytesReceived, Is.EqualTo(0));
            Assert.That(stats.MessagesSent, Is.EqualTo(0));
            Assert.That(stats.MessagesReceived, Is.EqualTo(0));
            Assert.That(stats.RoundTripTimeMs, Is.EqualTo(0));
        }

        [Test]
        public void TestNetworkStatisticsBase_AddSentMessage()
        {
            var stats = new NetworkStatisticsBase();

            stats.AddSentMessage(100);
            Assert.That(stats.BytesSent, Is.EqualTo(100));
            Assert.That(stats.MessagesSent, Is.EqualTo(1));

            stats.AddSentMessage(50);
            Assert.That(stats.BytesSent, Is.EqualTo(150));
            Assert.That(stats.MessagesSent, Is.EqualTo(2));
        }

        [Test]
        public void TestNetworkStatisticsBase_AddReceivedMessage()
        {
            var stats = new NetworkStatisticsBase();

            stats.AddReceivedMessage(200);
            Assert.That(stats.BytesReceived, Is.EqualTo(200));
            Assert.That(stats.MessagesReceived, Is.EqualTo(1));

            stats.AddReceivedMessage(75);
            Assert.That(stats.BytesReceived, Is.EqualTo(275));
            Assert.That(stats.MessagesReceived, Is.EqualTo(2));
        }

        [Test]
        public void TestNetworkStatisticsBase_Reset()
        {
            var stats = new NetworkStatisticsBase();

            stats.AddSentMessage(100);
            stats.AddReceivedMessage(200);
            stats.RoundTripTimeMs = 50.0;

            Assert.That(stats.BytesSent, Is.EqualTo(100));
            Assert.That(stats.BytesReceived, Is.EqualTo(200));
            Assert.That(stats.RoundTripTimeMs, Is.EqualTo(50.0));

            stats.Reset();

            Assert.That(stats.BytesSent, Is.EqualTo(0));
            Assert.That(stats.BytesReceived, Is.EqualTo(0));
            Assert.That(stats.MessagesSent, Is.EqualTo(0));
            Assert.That(stats.MessagesReceived, Is.EqualTo(0));
            Assert.That(stats.RoundTripTimeMs, Is.EqualTo(0));
        }

        [Test]
        public void TestNetworkStatisticsBase_ThreadSafety()
        {
            var stats = new NetworkStatisticsBase();
            const int iterations = 1000;

            // Simulate concurrent access
            var task1 = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    stats.AddSentMessage(1);
                }
            });

            var task2 = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    stats.AddReceivedMessage(1);
                }
            });

            Task.WaitAll(task1, task2);

            Assert.That(stats.BytesSent, Is.EqualTo(iterations));
            Assert.That(stats.MessagesSent, Is.EqualTo(iterations));
            Assert.That(stats.BytesReceived, Is.EqualTo(iterations));
            Assert.That(stats.MessagesReceived, Is.EqualTo(iterations));
        }

        [Test]
        public void TestDeliveryMethodNames()
        {
            // Verify all delivery method names are meaningful
            Assert.That(DeliveryMethod.Unreliable.ToString(), Is.EqualTo("Unreliable"));
            Assert.That(DeliveryMethod.UnreliableSequenced.ToString(), Is.EqualTo("UnreliableSequenced"));
            Assert.That(DeliveryMethod.ReliableUnordered.ToString(), Is.EqualTo("ReliableUnordered"));
            Assert.That(DeliveryMethod.ReliableSequenced.ToString(), Is.EqualTo("ReliableSequenced"));
            Assert.That(DeliveryMethod.ReliableOrdered.ToString(), Is.EqualTo("ReliableOrdered"));
        }

        [Test]
        public void TestNetworkConnectionStateNames()
        {
            // Verify all connection state names are meaningful
            Assert.That(NetworkConnectionState.Disconnected.ToString(), Is.EqualTo("Disconnected"));
            Assert.That(NetworkConnectionState.Connecting.ToString(), Is.EqualTo("Connecting"));
            Assert.That(NetworkConnectionState.Connected.ToString(), Is.EqualTo("Connected"));
            Assert.That(NetworkConnectionState.Disconnecting.ToString(), Is.EqualTo("Disconnecting"));
        }
    }
}
