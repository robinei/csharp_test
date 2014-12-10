using System;
using NUnit.Framework;

namespace Test
{
    [TestFixture]
    public class MessageBusTests
    {
        public struct TestEvent { }
        public struct TestEvent2 { }

        private MessageListener<TestEvent> listener;
        private MessageListener<TestEvent2> listener2;
        private int messageCounter;
        private int deadCounter;

        private string DeadKey { get { return "dead key " + deadCounter; } }

        private MessageHandler<TestEvent> MakeHandler()
        {
            return (msg, key) => {
                ++messageCounter;
            };
        }

        private MessageHandler<TestEvent2> MakeHandler2()
        {
            return (msg, key) => {
                ++messageCounter;
            };
        }


        private void GarbageCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }


        [SetUp]
        public void Init()
        {
            ++deadCounter;
            messageCounter = 0;
            listener = new MessageListener<TestEvent>(null, null);
            listener2 = new MessageListener<TestEvent2>(null, null);
            MessageBus.Default.RemoveAllListeners();
        }

        [Test]
        public void TestRetainedListenerCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(1, messageCounter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRetainedListenersCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            MessageBus.Default.AddListener(out listener2, MakeHandler2());
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(2, messageCounter);
            Assert.AreEqual(2, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestGCedListenerNotCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            listener = new MessageListener<TestEvent>(null, null);
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemovedListenerNotCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            bool removed = MessageBus.Default.RemoveListener(ref listener);
            Assert.IsTrue(removed);
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestOneRemovedListenerNotCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            MessageBus.Default.AddListener(out listener2, MakeHandler2());
            bool removed = MessageBus.Default.RemoveListener(ref listener2);
            Assert.IsTrue(removed);
            MessageBus.Default.Broadcast(new TestEvent());
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(1, messageCounter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNothingCalledAfterRemoveAll()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            MessageBus.Default.RemoveAllListeners();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestOtherListenerNotCalled()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestCalledIfRightKey()
        {
            MessageBus.Default.AddListenerWithStrongKey("key", out listener, MakeHandler());
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent(), "key");
            Assert.AreEqual(1, messageCounter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNotCalledIfWrongKey()
        {
            MessageBus.Default.AddListenerWithStrongKey("foo", out listener, MakeHandler());
            MessageBus.Default.Broadcast(new TestEvent(), "bar");
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNotCalledIfKeyGCed()
        {
            MessageBus.Default.AddListenerWithWeakKey(DeadKey, out listener, MakeHandler());
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent(), DeadKey);
            Assert.AreEqual(0, messageCounter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersWithDeadKey()
        {
            MessageBus.Default.AddListenerWithWeakKey(DeadKey, out listener, MakeHandler());
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersWithDeadHandler()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            listener = new MessageListener<TestEvent>(null, null);
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersDoesntTouchAliveListener()
        {
            MessageBus.Default.AddListener(out listener, MakeHandler());
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }
    }
}

