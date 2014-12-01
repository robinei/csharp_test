using System;
using NUnit.Framework;

namespace Test
{
    [TestFixture]
    public class MessageBusTests
    {
        public struct TestEvent { }
        public struct TestEvent2 { }

        private int counter;
        private MessageListener<TestEvent> listener;
        private MessageListener<TestEvent2> listener2;

        private void AddListener(object requireSender = null)
        {
            MessageBus.Default.AddListener(out listener, (msg, sender) => {
                ++counter;
            }, requireSender);
        }

        private void AddListener2(object requireSender = null)
        {
            MessageBus.Default.AddListener(out listener2, (msg, sender) => {
                ++counter;
            }, requireSender);
        }

        private void AddListenerWithUnrefedSender()
        {
            AddListener(new object());
        }

        private void ClearListener()
        {
            listener = new MessageListener<TestEvent>(0, null);
        }

        private void ClearListener2()
        {
            listener2 = new MessageListener<TestEvent2>(0, null);
        }

        private void GarbageCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }


        [SetUp]
        public void Init()
        {
            counter = 0;
            ClearListener();
            ClearListener2();
            MessageBus.Default.RemoveAllListeners();
        }

        [Test]
        public void TestRetainedListenerCalled()
        {
            AddListener();
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(1, counter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRetainedListenersCalled()
        {
            AddListener();
            AddListener2();
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(2, counter);
            Assert.AreEqual(2, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestGCedListenerNotCalled()
        {
            AddListener();
            ClearListener();
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, counter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemovedListenerNotCalled()
        {
            AddListener();
            bool removed = MessageBus.Default.RemoveListener(ref listener);
            Assert.IsTrue(removed);
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, counter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestOneRemovedListenerNotCalled()
        {
            AddListener();
            AddListener2();
            bool removed = MessageBus.Default.RemoveListener(ref listener2);
            Assert.IsTrue(removed);
            MessageBus.Default.Broadcast(new TestEvent());
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(1, counter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNothingCalledAfterRemoveAll()
        {
            AddListener();
            MessageBus.Default.RemoveAllListeners();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, counter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestOtherListenerNotCalled()
        {
            AddListener();
            MessageBus.Default.Broadcast(new TestEvent2());
            Assert.AreEqual(0, counter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestCalledIfRightSender()
        {
            var sender = new object();
            AddListener(sender);
            MessageBus.Default.Broadcast(new TestEvent(), sender);
            Assert.AreEqual(1, counter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNotCalledIfWrongSender()
        {
            AddListener(new object());
            MessageBus.Default.Broadcast(new TestEvent(), new object());
            MessageBus.Default.Broadcast(new TestEvent(), null);
            Assert.AreEqual(0, counter);
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestNotCalledIfSenderGCed()
        {
            AddListenerWithUnrefedSender();
            GarbageCollect();
            MessageBus.Default.Broadcast(new TestEvent());
            Assert.AreEqual(0, counter);
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersWithDeadSender()
        {
            AddListenerWithUnrefedSender();
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersWithDeadHandler()
        {
            AddListener();
            ClearListener();
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(0, MessageBus.Default.ListenerCount);
        }

        [Test]
        public void TestRemoveDeadListenersDoesntTouchAliveListener()
        {
            AddListener();
            GarbageCollect();
            MessageBus.Default.RemoveDeadListeners();
            Assert.AreEqual(1, MessageBus.Default.ListenerCount);
        }
    }
}

