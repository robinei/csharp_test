using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Test.Json
{
    public enum ValueType
    {
        Null,
        Bool,
        Long,
        Double,
        String,
        Array,
        Object
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RawValue
    {
        [FieldOffset(0)]
        public ValueType Type;

        [FieldOffset(4)]
        public bool Bool;

        [FieldOffset(4)]
        public long Long;

        [FieldOffset(4)]
        public double Double;

        [FieldOffset(4)]
        public int StringIndex;

        [FieldOffset(4)]
        public int ArrayOffset;
        [FieldOffset(8)]
        public int ArrayLength;

        [FieldOffset(4)]
        public int ObjectOffset;
        [FieldOffset(8)]
        public int ObjectLength;
    }


    public struct KeyValuePair
    {
        public StringSlice Key;
        public Value Value;
    }


    public struct Value : IEnumerable<Value>
    {
        private readonly RawValue rawValue;
        private readonly Parser parser;

        public Value(RawValue v, Parser p)
        {
            rawValue = v;
            parser = p;
        }

        public ValueType Type { get { return rawValue.Type; } }

        static public explicit operator bool(Value value)
        {
            if (value.Type != ValueType.Bool)
                throw new InvalidCastException();
            return value.rawValue.Bool;
        }

        static public explicit operator long(Value value)
        {
            if (value.Type != ValueType.Long)
                throw new InvalidCastException();
            return value.rawValue.Long;
        }

        static public explicit operator double(Value value)
        {
            if (value.Type != ValueType.Double)
                throw new InvalidCastException();
            return value.rawValue.Double;
        }

        static public explicit operator string(Value value)
        {
            if (value.Type != ValueType.String)
                throw new InvalidCastException();
            return (string)value.parser._GetString(value.rawValue.StringIndex);
        }

        static public explicit operator StringSlice(Value value)
        {
            if (value.Type != ValueType.String)
                throw new InvalidCastException();
            return value.parser._GetString(value.rawValue.StringIndex);
        }

        public int Count
        {
            get
            {
                if (Type == ValueType.Array)
                    return rawValue.ArrayLength;
                if (Type == ValueType.Object)
                    return rawValue.ObjectLength;
                throw new InvalidCastException();
            }
        }

        public Value this[int i]
        {
            get
            {
                if (Type == ValueType.Array) {
                    if (i < 0 || i >= rawValue.ArrayLength)
                        throw new IndexOutOfRangeException();
                    return ArrayValue(i);
                }
                if (Type == ValueType.Object) {
                    if (i < 0 || i >= rawValue.ObjectLength)
                        throw new IndexOutOfRangeException();
                    return ObjectValue(i);
                }
                throw new InvalidCastException();
            }
        }

        public IEnumerator<Value> GetEnumerator()
        {
            if (Type == ValueType.Array) {
                for (int i = 0; i < rawValue.ArrayLength; ++i)
                    yield return ArrayValue(i);
            } else if (Type == ValueType.Object) {
                for (int i = 0; i < rawValue.ObjectLength; ++i)
                    yield return ObjectValue(i);
            } else {
                throw new InvalidCastException();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerable<StringSlice> Keys
        {
            get
            {
                if (Type != ValueType.Object)
                    throw new InvalidCastException();
                for (int i = 0; i < rawValue.ObjectLength; ++i)
                    yield return ObjectKey(i);
            }
        }

        public IEnumerable<KeyValuePair> Items
        {
            get
            {
                if (Type != ValueType.Object)
                    throw new InvalidCastException();
                for (int i = 0; i < rawValue.ObjectLength; ++i) {
                    yield return new KeyValuePair {
                        Key = ObjectKey(i),
                        Value = ObjectValue(i)
                    };
                }
            }
        }

        private StringSlice ObjectKey(int i)
        {
            return parser._GetString(parser._GetIndex(rawValue.ObjectOffset + i * 2));
        }

        private Value ObjectValue(int i)
        {
            return new Value(parser._GetValue(parser._GetIndex(rawValue.ObjectOffset + i * 2 + 1)), parser);
        }

        private Value ArrayValue(int i)
        {
            return new Value(parser._GetValue(parser._GetIndex(rawValue.ArrayOffset + i)), parser);
        }


        public override string ToString()
        {
            var gen = new Generator(true);
            gen.Value(this);
            return gen.ToString();
        }
    }


    public class Parser
    {
        private enum State
        {
            Start,
            Done,
            Error,
            ArrayValue,
            ObjectKey,
            ObjectValue
        }

        private struct Context
        {
            public State State;
            public int IndexCount;
            public int[] Indexes;

            public void AddIndex(int index)
            {
                if (IndexCount == Indexes.Length) {
                    Array.Resize(ref Indexes, Indexes.Length * 2);
                }
                Indexes[IndexCount++] = index;
            }
        }


        private int firstBorrowedString;
        private int borrowedStringsLength;

        private int stringCount;
        private StringSlice[] strings = new StringSlice[8];

        private int valueCount;
        private RawValue[] values = new RawValue[8];

        private int indexCount;
        private int[] indexes = new int[8];

        private int tempIndexCount;
        private int[][] tempIndexes = new int[8][];

        private Context context;
        private int contextStackCount;
        private Context[] contextStack = new Context[8];


        internal StringSlice _GetString(int i) { return strings[i]; }
        internal RawValue _GetValue(int i) { return values[i]; }
        internal int _GetIndex(int i) { return indexes[i]; }


        public Value LastParsedRoot
        {
            get
            {
                if (context.State != State.Done)
                    throw new InvalidOperationException();
                Debug.Assert(contextStackCount == 0);
                Debug.Assert(valueCount > 0);
                return new Value(values[valueCount - 1], this);
            }
        }
        public bool IsDone { get { return context.State == State.Done; } }
        public bool IsFailed { get { return context.State == State.Error; } }
        public bool IsParsing { get { return !IsDone && !IsFailed; } }

        public Parser()
        {
            context = MakeContext(State.Start);
        }

        public void Clear()
        {
            firstBorrowedString = 0;
            borrowedStringsLength = 0;
            stringCount = 0;
            valueCount = 0;
            indexCount = 0;
            Reset();
        }

        public void Reset()
        {
            ReuseTempIndexList(context.Indexes);
            for (int i = 0; i < contextStackCount; ++i) {
                ReuseTempIndexList(contextStack[i].Indexes);
            }
            context = MakeContext(State.Start);
            contextStackCount = 0;
        }

        public bool Parse(IEnumerable<Token> tokens)
        {
            foreach (var token in tokens)
                Feed(token);
            return IsDone;
        }

        public void Feed(Token token)
        {
            switch (context.State) {
            case State.Start:
                context.State = State.Done;
                DispatchValueToken(token);
                break;
            case State.Done:
            case State.Error:
                break;
            case State.ArrayValue:
                if (token.Type == TokenType.ArrayEnd) {
                    AddValue(new RawValue { Type = ValueType.Array, ArrayOffset = indexCount, ArrayLength = context.IndexCount });
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (int i = 0; i < context.IndexCount; ++i) {
                        AddIndex(context.Indexes[i]);
                    }
                    PopContext();
                    context.AddIndex(valueCount - 1);
                } else {
                    DispatchValueToken(token);
                }
                break;
            case State.ObjectKey:
                if (token.Type == TokenType.ObjectEnd) {
                    Debug.Assert((context.IndexCount % 2) == 0);
                    AddValue(new RawValue { Type = ValueType.Object, ObjectOffset = indexCount, ObjectLength = context.IndexCount / 2 });
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (int i = 0; i < context.IndexCount; ++i) {
                        AddIndex(context.Indexes[i]);
                    }
                    PopContext();
                    context.AddIndex(valueCount - 1);
                } else if (token.Type == TokenType.String) {
                    context.AddIndex(stringCount);
                    AddString((StringSlice)token);
                    context.State = State.ObjectValue;
                } else {
                    context.State = State.Error;
                }
                break;
            case State.ObjectValue:
                context.State = State.ObjectKey;
                DispatchValueToken(token);
                break;
            }
        }

        private void DispatchValueToken(Token token)
        {
            switch (token.Type) {
            case TokenType.Null:
                context.AddIndex(valueCount);
                AddValue(new RawValue { Type = ValueType.Null });
                break;
            case TokenType.Bool:
                context.AddIndex(valueCount);
                AddValue(new RawValue { Type = ValueType.Bool, Bool = (bool)token });
                break;
            case TokenType.Long:
                context.AddIndex(valueCount);
                AddValue(new RawValue { Type = ValueType.Long, Long = (long)token });
                break;
            case TokenType.Double:
                context.AddIndex(valueCount);
                AddValue(new RawValue { Type = ValueType.Double, Double = (double)token });
                break;
            case TokenType.String:
                context.AddIndex(valueCount);
                AddValue(new RawValue { Type = ValueType.String, StringIndex = stringCount });
                AddString((StringSlice)token);
                break;
            case TokenType.ArrayBegin:
                PushContext(State.ArrayValue);
                break;
            case TokenType.ObjectBegin:
                PushContext(State.ObjectKey);
                break;
            default:
                context.State = State.Error;
                break;
            }
        }

        // copy all the strings that are not owned into a new buffer,
        // so they can live independent of the old buffer (typically owned by the Tokenizer)
        public void CopyStrings()
        {
            var buffer = new char[borrowedStringsLength];
            int offset = 0;
            for (int i = firstBorrowedString; i < stringCount; ++i) {
                var s = strings[i];
                Buffer.BlockCopy(s.Buffer, s.StartIndex, buffer, offset, s.Length);
                strings[i] = new StringSlice {
                    Buffer = buffer,
                    StartIndex = offset,
                    Length = s.Length
                };
                offset += s.Length;
            }
            firstBorrowedString = stringCount;
            borrowedStringsLength = 0;
        }


        private void AddIndex(int index)
        {
            if (indexCount == indexes.Length) {
                Array.Resize(ref indexes, indexes.Length * 2);
            }
            indexes[indexCount++] = index;
        }

        private void AddValue(RawValue value)
        {
            if (valueCount == values.Length) {
                Array.Resize(ref values, values.Length * 2);
            }
            values[valueCount++] = value;
        }

        private void AddString(StringSlice slice)
        {
            if (stringCount == strings.Length) {
                Array.Resize(ref strings, strings.Length * 2);
            }
            strings[stringCount++] = slice;
            borrowedStringsLength += slice.Length;
        }

        private void PushContext(State state)
        {
            if (contextStackCount == contextStack.Length) {
                Array.Resize(ref contextStack, contextStack.Length * 2);
            }
            contextStack[contextStackCount++] = context;
            context = MakeContext(state);
        }

        private void PopContext()
        {
            ReuseTempIndexList(context.Indexes);
            context = contextStack[--contextStackCount];
        }

        private Context MakeContext(State state)
        {
            return new Context {
                State = state,
                IndexCount = 0,
                Indexes = GetTempIndexList()
            };
        }

        private int[] GetTempIndexList()
        {
            return tempIndexCount > 0 ? tempIndexes[--tempIndexCount] : new int[8];
        }

        private void ReuseTempIndexList(int[] list)
        {
            if (tempIndexCount == tempIndexes.Length) {
                Array.Resize(ref tempIndexes, tempIndexes.Length * 2);
            }
            tempIndexes[tempIndexCount++] = list;
        }
    }
}
