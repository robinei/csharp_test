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

        public IEnumerator<StringSlice> Keys
        {
            get
            {
                if (Type != ValueType.Object)
                    throw new InvalidCastException();
                for (int i = 0; i < rawValue.ObjectLength; ++i)
                    yield return ObjectKey(i);
            }
        }

        public IEnumerator<KeyValuePair> Items
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
            var str = new StringBuilder();
            Dump(str, 0);
            return str.ToString();
        }

        private void Dump(StringBuilder str, int depth)
        {
            int count;
            switch (Type) {
            case ValueType.Null:
                str.Append("null");
                break;
            case ValueType.Bool:
                str.Append(rawValue.Bool ? "true" : "false");
                break;
            case ValueType.Long:
                str.Append(rawValue.Long.ToString(CultureInfo.InvariantCulture));
                break;
            case ValueType.Double:
                str.Append(rawValue.Double.ToString(CultureInfo.InvariantCulture));
                break;
            case ValueType.String:
                str.Append("\"");
                str.Append((string)this); // FIXME: escape string
                str.Append("\"");
                break;
            case ValueType.Array:
                count = Count;
                str.Append("[\n");
                for (int i = 0; i < count; ++i) {
                    Indent(str, depth + 1);
                    ArrayValue(i).Dump(str, depth + 1);
                    if (i != count - 1)
                        str.Append(",");
                    str.Append("\n");
                }
                Indent(str, depth);
                str.Append("]");
                break;
            case ValueType.Object:
                count = Count;
                str.Append("{\n");
                for (int i = 0; i < count; ++i) {
                    Indent(str, depth + 1);
                    str.Append("\"");
                    str.Append((string)ObjectKey(i));
                    str.Append("\"");
                    str.Append(": ");
                    ObjectValue(i).Dump(str, depth + 1);
                    if (i != count - 1)
                        str.Append(",");
                    str.Append("\n");
                }
                Indent(str, depth);
                str.Append("}");
                break;
            }
        }

        private void Indent(StringBuilder str, int depth)
        {
            for (int i = 0; i < depth; ++i)
                str.Append("    ");
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
            public List<int> Indexes;
        }

        private int firstBorrowedString;
        private int borrowedStringsLength;
        private readonly List<StringSlice> strings = new List<StringSlice>();
        private readonly List<RawValue> values = new List<RawValue>();
        private readonly List<int> indexes = new List<int>();
        private readonly List<List<int>> tempIndexes = new List<List<int>>();
        private readonly List<Context> contextStack = new List<Context>();
        private Context context;


        internal StringSlice _GetString(int i) { return strings[i]; }
        internal RawValue _GetValue(int i) { return values[i]; }
        internal int _GetIndex(int i) { return indexes[i]; }


        public Value LastParsedRoot
        {
            get
            {
                if (context.State != State.Done)
                    throw new InvalidOperationException();
                Debug.Assert(contextStack.Count == 0);
                Debug.Assert(values.Count > 0);
                return new Value(values[values.Count - 1], this);
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
            strings.Clear();
            values.Clear();
            indexes.Clear();
            Reset();
        }

        public void Reset()
        {
            foreach (var c in contextStack)
                ReuseTempIndexList(c.Indexes);
            contextStack.Clear();
            context.State = State.Start;
            context.Indexes.Clear();
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
                    values.Add(new RawValue { Type = ValueType.Array, ArrayOffset = indexes.Count, ArrayLength = context.Indexes.Count });
                    foreach (int index in context.Indexes)
                        indexes.Add(index);
                    PopContext();
                    context.Indexes.Add(values.Count - 1);
                } else {
                    DispatchValueToken(token);
                }
                break;
            case State.ObjectKey:
                if (token.Type == TokenType.ObjectEnd) {
                    Debug.Assert((context.Indexes.Count % 2) == 0);
                    values.Add(new RawValue { Type = ValueType.Object, ObjectOffset = indexes.Count, ObjectLength = context.Indexes.Count/2 });
                    foreach (int index in context.Indexes)
                        indexes.Add(index);
                    PopContext();
                    context.Indexes.Add(values.Count - 1);
                } else if (token.Type == TokenType.String) {
                    context.Indexes.Add(strings.Count);
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
                context.Indexes.Add(values.Count);
                values.Add(new RawValue { Type = ValueType.Null });
                break;
            case TokenType.Bool:
                context.Indexes.Add(values.Count);
                values.Add(new RawValue { Type = ValueType.Bool, Bool = (bool)token });
                break;
            case TokenType.Long:
                context.Indexes.Add(values.Count);
                values.Add(new RawValue { Type = ValueType.Long, Long = (long)token });
                break;
            case TokenType.Double:
                context.Indexes.Add(values.Count);
                values.Add(new RawValue { Type = ValueType.Double, Double = (double)token });
                break;
            case TokenType.String:
                context.Indexes.Add(values.Count);
                values.Add(new RawValue { Type = ValueType.String, StringIndex = strings.Count });
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
            for (int i = firstBorrowedString; i < strings.Count; ++i) {
                var s = strings[i];
                Buffer.BlockCopy(s.Buffer, s.StartIndex, buffer, offset, s.Length);
                strings[i] = new StringSlice {
                    Buffer = buffer,
                    StartIndex = offset,
                    Length = s.Length
                };
                offset += s.Length;
            }
            firstBorrowedString = strings.Count;
            borrowedStringsLength = 0;
        }


        private void AddString(StringSlice slice)
        {
            borrowedStringsLength += slice.Length;
            strings.Add(slice);
        }

        private void PushContext(State state)
        {
            contextStack.Add(context);
            context = MakeContext(state);
        }

        private void PopContext()
        {
            ReuseTempIndexList(context.Indexes);
            context = contextStack[contextStack.Count - 1];
            contextStack.RemoveAt(contextStack.Count - 1);
        }

        private Context MakeContext(State state)
        {
            return new Context {
                State = state,
                Indexes = GetTempIndexList()
            };
        }

        private List<int> GetTempIndexList()
        {
            List<int> result;
            if (tempIndexes.Count > 0) {
                result = tempIndexes[tempIndexes.Count - 1];
                tempIndexes.RemoveAt(tempIndexes.Count - 1);
            } else {
                result = new List<int>();
            }
            return result;
        }

        private void ReuseTempIndexList(List<int> list)
        {
            list.Clear();
            tempIndexes.Add(list);
        }
    }
}
