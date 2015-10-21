using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Test.Json
{
    public class GeneratorException : Exception
    {
        
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
        private readonly int indent;
        private readonly string colon;
        private readonly string comma;

        private Context context;
        private int contextStackCount;
        private Context[] contextStack = new Context[8];
        private readonly StringBuilder builder = new StringBuilder();

        public Generator(bool pretty = false, int indent = 4)
        {
            this.pretty = pretty;
            this.indent = indent;
            colon = pretty ? ": " : ":";
            comma = pretty ? ", " : ",";
        }

        public void Reset()
        {
            context.State = State.Start;
            context.NeedComma = false;
            contextStackCount = 0;
            builder.Clear();
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
                } else {
                    DispatchValueType(tokenType);
                }
                break;
            case State.ObjectKey:
                if (tokenType == TokenType.ObjectEnd) {
                    PopContext();
                } else if (tokenType == TokenType.String) {
                    context.State = State.ObjectValue;
                } else {
                    context.State = State.Error;
                    throw new GeneratorException();
                }
                break;
            case State.ObjectValue:
                context.State = State.ObjectKey;
                DispatchValueType(tokenType);
                builder.Append(colon);
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
                MaybeEmitComma();
                break;
            case TokenType.ArrayBegin:
                MaybeEmitComma();
                PushContext(State.ArrayValue);
                break;
            case TokenType.ObjectBegin:
                MaybeEmitComma();
                PushContext(State.ObjectKey);
                break;
            default:
                context.State = State.Error;
                throw new GeneratorException();
            }
        }

        private void MaybeEmitComma()
        {
            if (context.NeedComma) {
                builder.Append(",");
            } else {
                context.NeedComma = true;
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
            context = contextStack[contextStack.Length - 1];
            --contextStackCount;
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
            UpdateState(TokenType.String);
            builder.Append("\"");
            /*for (int i = 0; i < value.Length; ++i) {
                switch (value[i]) {
                case '\n':
                    builder.Append("\\n");
                }
            }*/
            builder.Append("\"");
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

        public void Value(object[] value)
        {
            ArrayBegin();
            for (int i = 0; i < value.Length; ++i) {
                Value(i);
            }
            ArrayEnd();
        }

        public void Value<T>(T[] value)
        {
            ArrayBegin();
            for (int i = 0; i < value.Length; ++i) {
                Value(i);
            }
            ArrayEnd();
        }

        public void Value(IReadOnlyList<object> value)
        {
            ArrayBegin();
            for (int i = 0; i < value.Count; ++i) {
                Value(i);
            }
            ArrayEnd();
        }

        public void Value<T>(IReadOnlyList<T> value)
        {
            ArrayBegin();
            for (int i = 0; i < value.Count; ++i) {
                Value(i);
            }
            ArrayEnd();
        }

        public void Value(IReadOnlyDictionary<string, object> value)
        {
            ObjectBegin();
            foreach (var item in value) {
                Value(item.Key);
                Value(item.Value);
            }
            ObjectEnd();
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
    }
}
