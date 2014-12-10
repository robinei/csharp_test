using System;
using System.Collections.Generic;
using System.Linq;

namespace Test
{
    public delegate void MessageHandler<in T>(T message, object key);


    public struct MessageListener<T>
    {
        public readonly MessageHandler<T> Handler;
        public readonly int? KeyHashCode;

        internal MessageListener(MessageHandler<T> handler, int? keyHashCode)
        {
            Handler = handler;
            KeyHashCode = keyHashCode;
        }
    }


    public class MessageBus
    {
        private struct HandlerEntry
        {
            public readonly WeakReference Handler;
            public readonly WeakReference WeakKey;
            public readonly object StrongKey;

            public HandlerEntry(WeakReference handler, WeakReference weakKey, object strongKey)
            {
                Handler = handler;
                WeakKey = weakKey;
                StrongKey = strongKey;
            }
        }

        // stores handlers for messages which aren't broadcasted with a target key
        private readonly Dictionary<Type, List<HandlerEntry>> untargeted = new Dictionary<Type, List<HandlerEntry>>();

        // add an additional layer of indirection with a map from key hash code to entry list, so we don't have to
        // iterate through too many entries when sending targeted messages (only the entries with the right hash code)
        private readonly Dictionary<Type, Dictionary<int, List<HandlerEntry>>> targeted = new Dictionary<Type, Dictionary<int, List<HandlerEntry>>>();

        public static readonly MessageBus Default = new MessageBus();


        public int ListenerCount
        {
            get
            {
                return untargeted.Values.Sum(entries => entries.Count) +
                    targeted.Values.SelectMany(entryLists => entryLists.Values).Sum(entries => entries.Count);
            }
        }


        public void AddListener<T>(out MessageListener<T> listener, MessageHandler<T> handler)
        {
            AddListener(out listener, handler, null, false);
        }

        public void AddListenerWithStrongKey<T>(object key, out MessageListener<T> listener, MessageHandler<T> handler)
        {
            AddListener(out listener, handler, key, false);
        }

        public void AddListenerWithWeakKey<T>(object key, out MessageListener<T> listener, MessageHandler<T> handler)
        {
            AddListener(out listener, handler, key, true);
        }

        // if the listener object is reclaimed you may stop receiving messages because
        // the handler may then also be GCed, so the caller is responsible for keeping it alive
        private void AddListener<T>(out MessageListener<T> listener, MessageHandler<T> handler, object key, bool keyIsWeak)
        {
            if (handler == null)
                throw new ArgumentNullException("handler");

            List<HandlerEntry> entries;
            int? keyHash = null;
            WeakReference weakKey = null;

            if (key == null) {
                if (!untargeted.TryGetValue(typeof(T), out entries)) {
                    entries = new List<HandlerEntry>();
                    untargeted.Add(typeof(T), entries);
                }
            } else {
                Dictionary<int, List<HandlerEntry>> entryLists;
                if (!targeted.TryGetValue(typeof(T), out entryLists)) {
                    entryLists = new Dictionary<int, List<HandlerEntry>>();
                    targeted.Add(typeof(T), entryLists);
                }

                keyHash = key.GetHashCode();
                if (!entryLists.TryGetValue(keyHash.Value, out entries)) {
                    entries = new List<HandlerEntry>();
                    entryLists.Add(keyHash.Value, entries);
                }

                if (keyIsWeak) {
                    weakKey = new WeakReference(key);
                    key = null;
                }
            }

            entries.Add(new HandlerEntry(new WeakReference(handler), weakKey, key));
            listener = new MessageListener<T>(handler, keyHash);
        }


        // remove listeners explicitly using this method when they are no longer wanted,
        // because waiting for the GC to reclaim your handler is highly undeterministic
        public bool RemoveListener<T>(ref MessageListener<T> listener)
        {
            List<HandlerEntry> entries;
            if (!listener.KeyHashCode.HasValue) {
                if (!untargeted.TryGetValue(typeof(T), out entries))
                    return false;
            } else {
                Dictionary<int, List<HandlerEntry>> entryLists;
                if (!targeted.TryGetValue(typeof(T), out entryLists))
                    return false;
                if (!entryLists.TryGetValue(listener.KeyHashCode.Value, out entries))
                    return false;
            }

            for (int i = 0; i < entries.Count; ++i) {
                var entry = entries[i];

                object handler = entry.Handler.Target;
                if (handler == null) {
                    entries.RemoveAt(i--);
                    continue;
                }

                if (handler.Equals(listener.Handler)) {
                    entries.RemoveAt(i);
                    listener = new MessageListener<T>(null, null);
                    return true;
                }
            }

            return false;
        }


        public void Broadcast<T>(T message, object key = null)
        {
            List<HandlerEntry> entries;
            if (key == null) {
                if (!untargeted.TryGetValue(typeof(T), out entries))
                    return;
            } else {
                int hash = key.GetHashCode();
                Dictionary<int, List<HandlerEntry>> entryLists;
                if (!targeted.TryGetValue(typeof(T), out entryLists))
                    return;
                if (!entryLists.TryGetValue(hash, out entries))
                    return;
            }

            for (int i = 0; i < entries.Count; ++i) {
                var entry = entries[i];

                if (entry.StrongKey != null) {
                    if (!entry.StrongKey.Equals(key))
                        continue;
                } else if (entry.WeakKey != null) {
                    object requireKey = entry.WeakKey.Target;
                    if (requireKey == null) {
                        entries.RemoveAt(i--);
                        continue;
                    }
                    if (!requireKey.Equals(key))
                        continue;
                }

                object handler = entry.Handler.Target;
                if (handler == null) {
                    entries.RemoveAt(i--);
                    continue;
                }

                ((MessageHandler<T>)handler)(message, key);
            }
        }


        private readonly List<int> emptyHashCodes = new List<int>();

        public void RemoveDeadListeners()
        {
            foreach (var entries in untargeted.Values)
                RemoveDeadEntries(entries);

            foreach (var entryLists in targeted.Values) {
                foreach (var item in entryLists) {
                    RemoveDeadEntries(item.Value);
                    if (item.Value.Count == 0)
                        emptyHashCodes.Add(item.Key);
                }
                if (emptyHashCodes.Count != 0) {
                    foreach (int code in emptyHashCodes)
                        entryLists.Remove(code);
                    emptyHashCodes.Clear();
                }
            }
        }

        private static void RemoveDeadEntries(List<HandlerEntry> entries)
        {
            for (int i = 0; i < entries.Count; ++i) {
                var entry = entries[i];
                if (entry.WeakKey != null && !entry.WeakKey.IsAlive)
                    entries.RemoveAt(i--);
                else if (!entry.Handler.IsAlive)
                    entries.RemoveAt(i--);
            }
        }


        public void RemoveAllListeners()
        {
            untargeted.Clear();
            targeted.Clear();
        }
    }
}

