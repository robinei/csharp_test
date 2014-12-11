using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Test.Json
{
    public enum TokenType
    {
        Null,
        Bool,
        Long,
        Double,
        String,
        ArrayBegin,
        ArrayEnd,
        ObjectBegin,
        ObjectEnd
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RawToken
    {
        [FieldOffset(0)]
        public TokenType Type;

        [FieldOffset(4)]
        public bool Bool;

        [FieldOffset(4)]
        public long Long;

        [FieldOffset(4)]
        public double Double;

        [FieldOffset(4)]
        public int StringOffset;
        [FieldOffset(8)]
        public int StringLength;
    }


    public struct Token
    {
        private readonly RawToken rawToken;
        private readonly char[] stringBuffer;

        public Token(RawToken raw, char[] buf)
        {
            rawToken = raw;
            stringBuffer = raw.Type == TokenType.String ? buf : null;
        }

        public TokenType Type { get { return rawToken.Type; } }

        static public explicit operator bool(Token token)
        {
            if (token.Type != TokenType.Bool)
                throw new InvalidCastException();
            return token.rawToken.Bool;
        }

        static public explicit operator long(Token token)
        {
            if (token.Type != TokenType.Long)
                throw new InvalidCastException();
            return token.rawToken.Long;
        }

        static public explicit operator double(Token token)
        {
            if (token.Type != TokenType.Double)
                throw new InvalidCastException();
            return token.rawToken.Double;
        }

        static public explicit operator string(Token token)
        {
            if (token.Type != TokenType.String)
                throw new InvalidCastException();
            return new string(token.stringBuffer, token.rawToken.StringOffset, token.rawToken.StringLength);
        }

        static public explicit operator StringSlice(Token token)
        {
            if (token.Type != TokenType.String)
                throw new InvalidCastException();
            return new StringSlice {
                Buffer = token.stringBuffer,
                StartIndex = token.rawToken.StringOffset,
                Length = token.rawToken.StringLength
            };
        }

        public override string ToString()
        {
            switch (Type) {
            case TokenType.Bool:
                return String.Format("{0}({1})", Type, (bool)this);
            case TokenType.Long:
                return String.Format("{0}({1})", Type, (long)this);
            case TokenType.Double:
                return String.Format("{0}({1})", Type, (double)this);
            case TokenType.String:
                return String.Format("{0}(\"{1}\")", Type, (string)this);
            default:
                return Type.ToString();
            }
        }
    }


    public class Tokenizer : IEnumerable<Token>
    {
        private enum State
        {
            Start,
            Done,
            Error,

            ArrayValue,
            ArrayComma,

            ObjectKey,
            ObjectColon,
            ObjectValue,
            ObjectComma,

            StringChar,
            StringEscape,
            StringU1, StringU2, StringU3, StringU4,

            NumWhole,
            NumZero,
            NumMinus,
            NumFrac0,
            NumFrac,
            NumExp0,
            NumExp,

            N, Nu, Nul,
            T, Tr, Tru,
            F, Fa, Fal, Fals
        }

        private State state = State.Start;
        private readonly Stack<State> stateStack = new Stack<State>(10);

        private readonly List<RawToken> tokens = new List<RawToken>(32);

        private char[] stringBuffer = new char[512];
        private int stringStart;
        private int stringPos;
        private int stringUniChar;

        private int numSign;
        private long numWhole;
        private int numFracDivisor;
        private long numFrac;
        private int numExpSign;
        private int numExp;

        private char lastChar;
        private int charPos;

        // state at time of error (for constructing ErrorString)
        private char failedChar;
        private char failedLastChar;
        private int failedCharPos;
        private string failureReason;


        public string ErrorString
        {
            get
            {
                if (!IsFailed)
                    return "";
                var err = new StringBuilder();
                err.Append("JSON tokenization error:\n    offset: ");
                err.Append(failedCharPos.ToString(CultureInfo.InvariantCulture));
                err.Append("\n    unexpected char: ");
                err.Append(failedChar.ToString(CultureInfo.InvariantCulture));
                if (failedCharPos > 0) {
                    err.Append("\n    last char: ");
                    err.Append(failedLastChar.ToString(CultureInfo.InvariantCulture));
                }
                err.Append("\n    reason: ");
                err.Append(failureReason);
                return err.ToString();
            }
        }

        public char[] StringBuffer
        {
            get { return stringBuffer; }
        }

        public bool IsDone { get { return state == State.Done; } }
        public bool IsFailed { get { return state == State.Error; } }
        public bool IsTokenizing { get { return !IsDone && !IsFailed; } }

        public int Count { get { return tokens.Count; } }

        public Token this[int i]
        {
            get
            {
                return new Token(tokens[i], stringBuffer);
            }
        }

        public IEnumerator<Token> GetEnumerator()
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var rawToken in tokens)
                yield return new Token(rawToken, stringBuffer);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // wipe the tokens that have been emitted since last Reset.
        // the string buffer will be overwritten after this, so any existing StringSlice
        // values from previously parsed tokens will no longer be safe to hang on to
        public void Clear()
        {
            if (stringPos == stringStart) {
                stringStart = 0;
                stringPos = 0;
            } else {
                // if we are in the middle of parsing a string token,
                // shift the parsed chars to the beginning of the buffer
                Buffer.BlockCopy(stringBuffer, stringStart, stringBuffer, 0, stringPos - stringStart);
                stringPos = stringPos - stringStart;
                stringStart = 0;

            }
            tokens.Clear();
        }

        public void Reset()
        {
            state = State.Start;
            stateStack.Clear();
            stringStart = 0;
            stringPos = 0;
            tokens.Clear();
            lastChar = default(char);
            charPos = 0;
        }


        public bool Tokenize(char[] buffer, int startIndex, int length)
        {
            Feed(buffer, startIndex, length);
            return IsDone;
        }
        public bool Tokenize(string str)
        {
            Feed(str);
            return IsDone;
        }


        public void Feed(char[] buffer, int startIndex, int length)
        {
            for (int i = startIndex; i < startIndex + length; ++i)
                Feed(buffer[i]);
        }
        public void Feed(string str)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < str.Length; ++i)
                Feed(str[i]);
        }


        public void Feed(char ch)
        {
        EntryPoint:
            switch (state) {
            case State.Start:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                default:
                    DispatchFirstValueChar(ch, State.Done);
                    break;
                }
                break;
            case State.Done:
            case State.Error:
                // do nothing
                break;


            case State.ArrayValue:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                case ']':
                    EmitToken(new RawToken { Type = TokenType.ArrayEnd });
                    PopState();
                    break;
                default:
                    DispatchFirstValueChar(ch, State.ArrayComma);
                    break;
                }
                break;
            case State.ArrayComma:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                case ',':
                    SetState(State.ArrayValue);
                    break;
                case ']':
                    EmitToken(new RawToken { Type = TokenType.ArrayEnd });
                    PopState();
                    break;
                default:
                    Unexpected(ch, "expected ',' or ']' while parsing array");
                    break;
                }
                break;


            case State.ObjectKey:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                case '"':
                    PushState(State.StringChar, State.ObjectColon);
                    break;
                case '}':
                    EmitToken(new RawToken { Type = TokenType.ObjectEnd });
                    PopState();
                    break;
                default:
                    Unexpected(ch, "expected string key");
                    break;
                }
                break;
            case State.ObjectColon:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                case ':':
                    SetState(State.ObjectValue);
                    break;
                default:
                    Unexpected(ch, "expected ':' while parsing object");
                    break;
                }
                break;
            case State.ObjectValue:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                default:
                    DispatchFirstValueChar(ch, State.ObjectComma);
                    break;
                }
                break;
            case State.ObjectComma:
                switch (ch) {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case '\v':
                case '\f':
                    // skip
                    break;
                case ',':
                    SetState(State.ObjectKey);
                    break;
                case '}':
                    EmitToken(new RawToken { Type = TokenType.ObjectEnd });
                    PopState();
                    break;
                default:
                    Unexpected(ch, "expected ',' or '}' while parsing object");
                    break;
                }
                break;


            case State.StringChar:
                if (IsControl(ch)) {
                    Unexpected(ch, "did not expect control character while parsing string");
                    break;
                }
                switch (ch) {
                case '"':
                    EmitStringToken();
                    PopState();
                    break;
                case '\\':
                    SetState(State.StringEscape);
                    break;
                default:
                    EmitStringChar(ch);
                    break;
                }
                break;
            case State.StringEscape:
                switch (ch) {
                case '"':
                    EmitStringChar('"');
                    SetState(State.StringChar);
                    break;
                case '\\':
                    EmitStringChar('\\');
                    SetState(State.StringChar);
                    break;
                case '/':
                    EmitStringChar('/');
                    SetState(State.StringChar);
                    break;
                case 'b':
                    EmitStringChar('\b');
                    SetState(State.StringChar);
                    break;
                case 'f':
                    EmitStringChar('\f');
                    SetState(State.StringChar);
                    break;
                case 'n':
                    EmitStringChar('\n');
                    SetState(State.StringChar);
                    break;
                case 'r':
                    EmitStringChar('\r');
                    SetState(State.StringChar);
                    break;
                case 't':
                    EmitStringChar('\t');
                    SetState(State.StringChar);
                    break;
                case 'u':
                    stringUniChar = 0;
                    SetState(State.StringU1);
                    break;
                default:
                    Unexpected(ch, "expected valid escape char");
                    break;
                }
                break;
            case State.StringU1: {
                int val = HexVal(ch);
                if (val >= 0) {
                    stringUniChar |= val << 12;
                    SetState(State.StringU2);
                } else {
                    Unexpected(ch, "expected hex digit in unicode escape sequence");
                }
                break;
            }
            case State.StringU2: {
                int val = HexVal(ch);
                if (val >= 0) {
                    stringUniChar |= val << 8;
                    SetState(State.StringU3);
                } else {
                    Unexpected(ch, "expected hex digit in unicode escape sequence");
                }
                break;
            }
            case State.StringU3: {
                int val = HexVal(ch);
                if (val >= 0) {
                    stringUniChar |= val << 4;
                    SetState(State.StringU4);
                } else {
                    Unexpected(ch, "expected hex digit in unicode escape sequence");
                }
                break;
            }
            case State.StringU4: {
                int val = HexVal(ch);
                if (val >= 0) {
                    stringUniChar |= val;
                    EmitStringChar((char)stringUniChar);
                    SetState(State.StringChar);
                } else {
                    Unexpected(ch, "expected hex digit in unicode escape sequence");
                }
                break;
            }

            case State.NumWhole:
                switch (ch) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numWhole = numWhole * 10 + ch - '0';
                    break;
                case '.':
                    SetState(State.NumFrac0);
                    break;
                case 'e':
                case 'E':
                    SetState(State.NumExp0);
                    break;
                default:
                    EmitNumToken();
                    PopState();
                    goto EntryPoint; // read it again in parent state
                }
                break;
            case State.NumZero:
                switch (ch) {
                case '.':
                    SetState(State.NumFrac0);
                    break;
                case 'e':
                case 'E':
                    SetState(State.NumExp0);
                    break;
                default:
                    EmitNumToken();
                    PopState();
                    goto EntryPoint; // read it again in parent state
                }
                break;
            case State.NumMinus:
                switch (ch) {
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numWhole = ch - '0';
                    SetState(State.NumWhole);
                    break;
                case '0':
                    SetState(State.NumZero);
                    break;
                default:
                    Unexpected(ch, "expected digit after '0'");
                    break;
                }
                break;
            case State.NumFrac0:
                switch (ch) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numFracDivisor = 10;
                    numFrac = ch - '0';
                    SetState(State.NumFrac);
                    break;
                default:
                    Unexpected(ch, "expected digit after '.' in number");
                    break;
                }
                break;
            case State.NumFrac:
                switch (ch) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numFracDivisor *= 10;
                    numFrac = numFrac * 10 + ch - '0';
                    break;
                case 'e':
                case 'E':
                    SetState(State.NumExp0);
                    break;
                default:
                    EmitNumToken();
                    PopState();
                    goto EntryPoint; // read it again in parent state
                }
                break;
            case State.NumExp0:
                switch (ch) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numExp = ch - '0';
                    SetState(State.NumExp);
                    break;
                case '+':
                    SetState(State.NumExp);
                    break;
                case '-':
                    numExpSign = -1;
                    SetState(State.NumExp);
                    break;
                default:
                    Unexpected(ch, "expected digit or '+' or '-' after 'e' or 'E' in number");
                    break;
                }
                break;
            case State.NumExp:
                switch (ch) {
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    numExp = numExp * 10 + ch - '0';
                    break;
                default:
                    EmitNumToken();
                    PopState();
                    goto EntryPoint; // read it again in parent state
                }
                break;

            case State.N:
                if (ch == 'u') {
                    SetState(State.Nu);
                } else {
                    Unexpected(ch, "expected 'u' while parsing 'null'");
                }
                break;
            case State.Nu:
                if (ch == 'l') {
                    SetState(State.Nul);
                } else {
                    Unexpected(ch, "expected 'l' while parsing 'null'");
                }
                break;
            case State.Nul:
                if (ch == 'l') {
                    EmitToken(new RawToken { Type = TokenType.Null });
                    PopState();
                } else {
                    Unexpected(ch, "expected 'l' while parsing 'null'");
                }
                break;


            case State.T:
                if (ch == 'r') {
                    SetState(State.Tr);
                } else {
                    Unexpected(ch, "expected 'r' while parsing 'true'");
                }
                break;
            case State.Tr:
                if (ch == 'u') {
                    SetState(State.Tru);
                } else {
                    Unexpected(ch, "expected 'u' while parsing 'true'");
                }
                break;
            case State.Tru:
                if (ch == 'e') {
                    EmitToken(new RawToken { Type = TokenType.Bool, Bool = true });
                    PopState();
                } else {
                    Unexpected(ch, "expected 'e' while parsing 'true'");
                }
                break;


            case State.F:
                if (ch == 'a') {
                    SetState(State.Fa);
                } else {
                    Unexpected(ch, "expected 'a' while parsing 'false'");
                }
                break;
            case State.Fa:
                if (ch == 'l') {
                    SetState(State.Fal);
                } else {
                    Unexpected(ch, "expected 'l' while parsing 'false'");
                }
                break;
            case State.Fal:
                if (ch == 's') {
                    SetState(State.Fals);
                } else {
                    Unexpected(ch, "expected 's' while parsing 'false'");
                }
                break;
            case State.Fals:
                if (ch == 'e') {
                    EmitToken(new RawToken { Type = TokenType.Bool, Bool = false });
                    PopState();
                } else {
                    Unexpected(ch, "expected 'e' while parsing 'false'");
                }
                break;
            }

            lastChar = ch;
            ++charPos;
        }


        private void DispatchFirstValueChar(char ch, State nextState)
        {
            switch (ch) {
            case '[':
                EmitToken(new RawToken {Type = TokenType.ArrayBegin});
                PushState(State.ArrayValue, nextState);
                break;
            case '{':
                EmitToken(new RawToken {Type = TokenType.ObjectBegin});
                PushState(State.ObjectKey, nextState);
                break;
            case '"':
                PushState(State.StringChar, nextState);
                break;
            case 'n':
                PushState(State.N, nextState);
                break;
            case 't':
                PushState(State.T, nextState);
                break;
            case 'f':
                PushState(State.F, nextState);
                break;
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
                ResetNumVars();
                numWhole = ch - '0';
                PushState(State.NumWhole, nextState);
                break;
            case '0':
                ResetNumVars();
                PushState(State.NumZero, nextState);
                break;
            case '-':
                ResetNumVars();
                numSign = -1;
                PushState(State.NumMinus, nextState);
                break;
            default:
                Unexpected(ch, "expected a JSON value");
                break;
            }
        }

        private void ResetNumVars()
        {
            numSign = 1;
            numWhole = 0;
            numFracDivisor = 1;
            numFrac = 0;
            numExpSign = 1;
            numExp = 0;
        }


        private void SetState(State s)
        {
            state = s;
        }

        private void PushState(State s, State nextState)
        {
            SetState(s);
            stateStack.Push(nextState);
        }

        private void PopState()
        {
            SetState(stateStack.Pop());
        }

        private void EmitToken(RawToken token)
        {
            tokens.Add(token);
        }

        private void EmitStringChar(char ch)
        {
            if (stringPos >= stringBuffer.Length)
                Array.Resize(ref stringBuffer, stringBuffer.Length * 2);
            stringBuffer[stringPos++] = ch;
        }

        private void EmitStringToken()
        {
            EmitToken(new RawToken {
                Type = TokenType.String,
                StringOffset = stringStart,
                StringLength = stringPos - stringStart
            });
            stringStart = stringPos;
        }

        private void EmitNumToken()
        {
            long wholePart = numSign * numWhole;
            if (numFracDivisor == 1 && numExp == 0) {
                EmitToken(new RawToken { Type = TokenType.Long, Long = wholePart });
            } else {
                double num = wholePart + (double)numFrac / numFracDivisor;
                if (numExp != 0)
                    num *= Math.Pow(10, numExpSign * numExp);
                EmitToken(new RawToken { Type = TokenType.Double, Double = num });
            }
        }

        private void Unexpected(char ch, string reason)
        {
            failedChar = ch;
            failedLastChar = lastChar;
            failedCharPos = charPos;
            failureReason = reason;
            SetState(State.Error);
        }


        private static bool IsControl(char ch)
        {
            return (ch >= 0 && ch <= 31) || ch == 127;
        }

        private static int HexVal(char ch)
        {
            if (ch >= '0' && ch <= '9')
                return ch - '0';
            if (ch >= 'a' && ch <= 'f')
                return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F')
                return ch - 'A' + 10;
            return -1;
        }
    }
}
