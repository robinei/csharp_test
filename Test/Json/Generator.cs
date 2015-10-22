using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Test.Json
{
    public class GeneratorException : Exception
    {
        public GeneratorException(string msg) : base(msg) { }
    }

    public class Generator
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
            public bool NeedComma;
        }

        private readonly bool pretty;
        private readonly string indentString;
        private readonly string colonString;

        private readonly StringBuilder builder = new StringBuilder();

        private Context context;
        private int contextStackCount;
        private Context[] contextStack = new Context[8];

        public Generator(bool pretty = false, string indentString = "    ")
        {
            this.pretty = pretty;
            this.indentString = indentString;
            colonString = pretty ? ": " : ":";
        }

        public void Reset()
        {
            builder.Clear();
            context.State = State.Start;
            context.NeedComma = false;
            contextStackCount = 0;
        }

        public override string ToString()
        {
            return builder.ToString();
        }


        private void UpdateState(TokenType tokenType)
        {
            switch (context.State) {
            case State.Start:
                context.State = State.Done;
                DispatchValueType(tokenType);
                break;
            case State.Done:
            case State.Error:
                break;
            case State.ArrayValue:
                if (tokenType == TokenType.ArrayEnd) {
                    PopContext();
                    MaybeIndent();
                } else {
                    MaybeCommaIndent();
                    DispatchValueType(tokenType);
                }
                break;
            case State.ObjectKey:
                if (tokenType == TokenType.ObjectEnd) {
                    PopContext();
                    MaybeIndent();
                } else if (tokenType == TokenType.String) {
                    context.State = State.ObjectValue;
                    MaybeCommaIndent();
                } else {
                    context.State = State.Error;
                    throw new GeneratorException(String.Format("Got {0} while expecting ObjectEnd or String (ObjectKey)", tokenType));
                }
                break;
            case State.ObjectValue:
                context.State = State.ObjectKey;
                builder.Append(colonString);
                DispatchValueType(tokenType);
                break;
            }
        }

        private void DispatchValueType(TokenType tokenType)
        {
            switch (tokenType) {
            case TokenType.Null:
            case TokenType.Bool:
            case TokenType.Long:
            case TokenType.Double:
            case TokenType.String:
                break;
            case TokenType.ArrayBegin:
                PushContext(State.ArrayValue);
                break;
            case TokenType.ObjectBegin:
                PushContext(State.ObjectKey);
                break;
            default:
                context.State = State.Error;
                throw new GeneratorException(String.Format("Got {0} while expecting value", tokenType));
            }
        }

        private void PushContext(State newState)
        {
            if (contextStackCount == contextStack.Length) {
                Array.Resize(ref contextStack, contextStack.Length * 2);
            }
            contextStack[contextStackCount++] = context;
            context.State = newState;
            context.NeedComma = false;
        }

        private void PopContext()
        {
            context = contextStack[--contextStackCount];
        }

        private void MaybeIndent()
        {
            if (pretty) {
                builder.Append("\n");
                for (int i = 0; i < contextStackCount; ++i) {
                    builder.Append(indentString);
                }
            }
        }

        private void MaybeCommaIndent()
        {
            if (context.NeedComma) {
                builder.Append(",");
            } else {
                context.NeedComma = true;
            }
            MaybeIndent();
        }



        public void ArrayBegin()
        {
            UpdateState(TokenType.ArrayBegin);
            builder.Append("[");
        }

        public void ArrayEnd()
        {
            UpdateState(TokenType.ArrayEnd);
            builder.Append("]");
        }

        public void ObjectBegin()
        {
            UpdateState(TokenType.ObjectBegin);
            builder.Append("{");
        }

        public void ObjectEnd()
        {
            UpdateState(TokenType.ObjectEnd);
            builder.Append("}");
        }

        public void Null()
        {
            UpdateState(TokenType.Null);
            builder.Append("null");
        }

        public void Value(bool value)
        {
            UpdateState(TokenType.Bool);
            builder.Append(value ? "true" : "false");
        }

        public void Value(int value)
        {
            UpdateState(TokenType.Long);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Value(long value)
        {
            UpdateState(TokenType.Long);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Value(float value)
        {
            UpdateState(TokenType.Double);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Value(double value)
        {
            UpdateState(TokenType.Double);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Value(string value)
        {
            if (value == null) {
                Null();
                return;
            }

            UpdateState(TokenType.String);

            builder.Append("\"");

            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < value.Length; ++i) {
                char ch = value[i];
                switch (ch) {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(ch);
                    break;
                }
            }

            builder.Append("\"");
        }

        public void Value(StringSlice value)
        {
            Value((string)value);
        }

        public void Value(object value)
        {
            if (value == null) {
                Null();
                return;
            }
            var str = value as string;
            if (str != null) {
                Value(str);
            } else if (value is bool) {
                Value((bool)value);
            } else if (value is int) {
                Value((int)value);
            } else if (value is long) {
                Value((long)value);
            } else if (value is float) {
                Value((float)value);
            } else if (value is double) {
                Value((double)value);
            }
        }

        public void Value<T>(T[] value)
        {
            ArrayBegin();
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < value.Length; ++i) {
                Value(value[i]);
            }
            ArrayEnd();
        }

        public void Value<T>(IReadOnlyList<T> value)
        {
            ArrayBegin();
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < value.Count; ++i) {
                Value(value[i]);
            }
            ArrayEnd();
        }

        public void Value<T>(IEnumerable<T> value)
        {
            ArrayBegin();
            foreach (var item in value) {
                Value(item);
            }
            ArrayEnd();
        }

        public void Value<T>(IReadOnlyDictionary<string, T> value)
        {
            ObjectBegin();
            foreach (var item in value) {
                Value(item.Key);
                Value(item.Value);
            }
            ObjectEnd();
        }

        public void Value(Token token)
        {
            switch (token.Type) {
            case TokenType.Null:
                Null();
                break;
            case TokenType.Bool:
                Value((bool)token);
                break;
            case TokenType.Long:
                Value((long)token);
                break;
            case TokenType.Double:
                Value((double)token);
                break;
            case TokenType.String:
                Value((string)token);
                break;
            case TokenType.ArrayBegin:
                ArrayBegin();
                break;
            case TokenType.ArrayEnd:
                ArrayEnd();
                break;
            case TokenType.ObjectBegin:
                ObjectBegin();
                break;
            case TokenType.ObjectEnd:
                ObjectEnd();
                break;
            }
        }

        public void Value(Value value)
        {
            switch (value.Type) {
            case ValueType.Null:
                Null();
                break;
            case ValueType.Bool:
                Value((bool)value);
                break;
            case ValueType.Long:
                Value((long)value);
                break;
            case ValueType.Double:
                Value((double)value);
                break;
            case ValueType.String:
                Value((string)value);
                break;
            case ValueType.Array:
                ArrayBegin();
                foreach (var item in value) {
                    Value(item);
                }
                ArrayEnd();
                break;
            case ValueType.Object:
                ObjectBegin();
                foreach (var item in value.Items) {
                    Value(item.Key);
                    Value(item.Value);
                }
                ObjectEnd();
                break;
            }
        }
    }
}
