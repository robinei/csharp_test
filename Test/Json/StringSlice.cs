using System;

namespace Test.Json
{
    public struct StringSlice : IEquatable<StringSlice>
    {
        public char[] Buffer;
        public int StartIndex;
        public int Length;

        public bool Equals(StringSlice other)
        {
            return this == other;
        }

        public override bool Equals(object other)
        {
            if (other is StringSlice) {
                return this == (StringSlice)other;
            }
            return other is string && this == (string)other;
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public override string ToString()
        {
            return (string)this;
        }

        static public explicit operator string(StringSlice slice)
        {
            return new string(slice.Buffer, slice.StartIndex, slice.Length);
        }
        
        public static bool operator !=(StringSlice a, StringSlice b) { return !(a == b); }
        public static bool operator ==(StringSlice a, StringSlice b)
        {
            if (a.Length != b.Length)
                return false;
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int i = 0; i < a.Length; ++i)
                if (a.Buffer[a.StartIndex + i] != b.Buffer[b.StartIndex + i])
                    return false;
            return true;
        }

        public static bool operator !=(StringSlice slice, string str) { return !(slice == str); }
        public static bool operator ==(StringSlice slice, string str)
        {
            if (str == null || slice.Length != str.Length) {
                return false;
            }
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int i = 0; i < str.Length; ++i) {
                if (slice.Buffer[slice.StartIndex + i] != str[i]) {
                    return false;
                }
            }
            return true;
        }

        public static bool operator !=(string str, StringSlice slice) { return !(slice == str); }
        public static bool operator ==(string str, StringSlice slice) { return slice == str; }
    }
}
