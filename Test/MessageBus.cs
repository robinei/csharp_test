using System;
using System.Collections.Generic;

namespace Test
{
    public delegate void MessageHandler<T>(T message, object sender);


    public struct MessageListener<T>
    {
        public readonly int ID;
        public readonly MessageHandler<T> Handler;

        internal MessageListener(int id, MessageHandler<T> handler)
        {
            ID = id;
            Handler = handler;
        }
    }


    public class MessageBus
    {
        private struct HandlerEntry
        {
            public readonly int ID;
            public readonly WeakReference Handler;
            public readonly WeakReference RequireSender; // if non-null, only accept messages from this sender

            public HandlerEntry(int id, object handler, object requireSender)
            {
                ID = id;
                Handler = new WeakReference(handler);
                RequireSender = requireSender == null ? null : new WeakReference(requireSender);
            }
        }

        private int idCounter = 0;
        private readonly Dictionary<Type, List<HandlerEntry>> registry = new Dictionary<Type, List<HandlerEntry>>();


        public static readonly MessageBus Default = new MessageBus();


        public int ListenerCount
        {
            get
            {
                int count = 0;
                foreach (var entries in registry.Values)
                    count += entries.Count;
                return count;
            }
        }


        // if the listener object is reclaimed you may stop receiving messages because
        // the handler may then also be GCed, so keep it alive
        public void AddListener<T>(out MessageListener<T> listener, MessageHandler<T> handler, object requireSender = null)
        {
            if (handler == null)
                throw new ArgumentNullException("handler was null");

            var entry = new HandlerEntry(++idCounter, handler, requireSender);

            List<HandlerEntry> entries;
            if (!registry.TryGetValue(typeof(T), out entries)) {
                entries = new List<HandlerEntry>();
                registry.Add(typeof(T), entries);
            }
            entries.Add(entry);

            listener = new MessageListener<T>(entry.ID, handler);
        }


        // remove listeners explicitly using this method when they are no longer wanted,
        // because waiting for the GC to reclaim your handler is highly undeterministic
        public bool RemoveListener<T>(ref MessageListener<T> listener)
        {
            if (listener.ID != 0 && listener.Handler == null)
                throw new ArgumentException("Trying to remove bad MessageListener!"); // should not happen
            if (listener.ID == 0)
                return false; // it has already been removed, or it hasn't been added (this is not an error)

            List<HandlerEntry> entries;
            if (!registry.TryGetValue(typeof(T), out entries))
                return false;

            for (int i = 0; i < entries.Count; ++i) {
                var entry = entries[i];

                if (listener.ID != entry.ID)
                    continue;

                object handler = entry.Handler.Target;
                if (handler == null)
                    throw new ArgumentException("ID matched while Handler has been GCed. " +
                        "Did you try to remove a MessageListener that was added to a different MessageBus?");

                if (handler != (object)listener.Handler)
                    throw new ArgumentException("ID matched while Handler did not. " +
                        "Did you try to remove a MessageListener that was added to a different MessageBus?");

                entries.RemoveAt(i);
                listener = new MessageListener<T>(0, null);
                return true;
            }

            return false;
        }


        public void Broadcast<T>(T message, object sender = null)
        {
            List<HandlerEntry> entries;
            if (!registry.TryGetValue(typeof(T), out entries))
                return;

            for (int i = 0; i < entries.Count; ++i) {
                var entry = entries[i];

                if (entry.RequireSender != null) {
                    object requireSender = entry.RequireSender.Target;
                    if (requireSender == null) {
                        entries.RemoveAt(i--);
                        continue;
                    }
                    if (requireSender != sender)
                        continue;
                }

                object handler = entry.Handler.Target;
                if (handler == null) {
                    entries.RemoveAt(i--);
                    continue;
                }

                ((MessageHandler<T>)handler)(message, sender);
            }
        }


        public void RemoveDeadListeners()
        {
            foreach (var entries in registry.Values) {
                for (int i = 0; i < entries.Count; ++i) {
                    var entry = entries[i];
                    if (entry.RequireSender != null && !entry.RequireSender.IsAlive)
                        entries.RemoveAt(i--);
                    else if (!entry.Handler.IsAlive)
                        entries.RemoveAt(i--);
                }
            }
        }


        public void RemoveAllListeners()
        {
            registry.Clear();
        }
    }
}

