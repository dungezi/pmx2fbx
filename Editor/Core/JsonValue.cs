using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pmx2Fbx.Core
{
    // Small, strict JSON DOM used for extension dictionaries in glTF/VRM. No external dependency.
    public sealed class JsonValue
    {
        private object value;
        private static readonly JsonValue Missing = new JsonValue();
        public JsonValue this[string key] => value is Dictionary<string, JsonValue> d && d.TryGetValue(key, out var v) ? v : Missing;
        public JsonValue this[int index] => value is List<JsonValue> a && index >= 0 && index < a.Count ? a[index] : Missing;
        public bool Exists => value != null;
        public int Count => value is List<JsonValue> a ? a.Count : value is Dictionary<string,JsonValue> d ? d.Count : 0;
        public IEnumerable<KeyValuePair<string,JsonValue>> Members => value is Dictionary<string,JsonValue> d ? d : (IEnumerable<KeyValuePair<string,JsonValue>>)Array.Empty<KeyValuePair<string,JsonValue>>();
        public string Text(string fallback = "") => value is string s ? s : fallback;
        public float Number(float fallback = 0) => value is double n ? (float)n : fallback;
        public int Int(int fallback = -1)
        {
            if (value == null) return fallback;
            if (!(value is double n) || n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new InvalidDataException("VRM JSON 整数无效。");
            return (int)n;
        }
        public bool Bool(bool fallback = false) => value is bool b ? b : fallback;
        public static JsonValue Parse(string json)
        {
            var parser = new Parser(json); var result = parser.Read(0); parser.Space();
            if (parser.Position != json.Length) throw new InvalidDataException("VRM JSON 存在多余内容。");
            return result;
        }
        private sealed class Parser
        {
            private readonly string source;
            public int Position;
            public Parser(string s) { source = s; }
            public void Space() { while (Position < source.Length && char.IsWhiteSpace(source[Position])) Position++; }
            private bool Take(char c) { Space(); if (Position < source.Length && source[Position] == c) { Position++; return true; } return false; }
            private void Need(char c) { if (!Take(c)) throw new InvalidDataException("VRM JSON 缺少 " + c); }
            private string String()
            {
                Need('"'); var s = new StringBuilder();
                while (Position < source.Length)
                {
                    char c = source[Position++]; if (c == '"') return s.ToString();
                    if (c < 32) throw new InvalidDataException("VRM JSON 字符串含控制字符。");
                    if (c != '\\') { s.Append(c); continue; }
                    if (Position >= source.Length) break;
                    c = source[Position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': s.Append(c); break;
                        case 'b': s.Append('\b'); break; case 'f': s.Append('\f'); break;
                        case 'n': s.Append('\n'); break; case 'r': s.Append('\r'); break; case 't': s.Append('\t'); break;
                        case 'u':
                            if (Position + 4 > source.Length || !ushort.TryParse(source.Substring(Position,4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort n)) throw new InvalidDataException("VRM JSON Unicode 转义无效。");
                            s.Append((char)n); Position += 4; break;
                        default: throw new InvalidDataException("VRM JSON 转义无效。");
                    }
                }
                throw new InvalidDataException("VRM JSON 字符串未结束。");
            }
            public JsonValue Read(int depth)
            {
                if (depth > 128) throw new InvalidDataException("VRM JSON 嵌套过深。");
                Space(); if (Position >= source.Length) throw new EndOfStreamException("VRM JSON 被截断。");
                var node = new JsonValue(); char c = source[Position];
                if (c == '"') node.value = String();
                else if (Take('{'))
                {
                    var d = new Dictionary<string,JsonValue>(StringComparer.Ordinal); node.value=d;
                    if (!Take('}')) do { string key=String(); Need(':'); if (d.ContainsKey(key)) throw new InvalidDataException("VRM JSON 键重复。"); d.Add(key,Read(depth+1)); if (Take('}')) return node; Need(','); } while(true);
                }
                else if (Take('['))
                {
                    var a=new List<JsonValue>(); node.value=a;
                    if (!Take(']')) do { a.Add(Read(depth+1)); if (Take(']')) return node; Need(','); } while(true);
                }
                else if (c=='t' || c=='f' || c=='n')
                {
                    string literal=c=='t' ? "true" : c=='f' ? "false" : "null";
                    if (Position+literal.Length>source.Length || source.Substring(Position,literal.Length)!=literal) throw new InvalidDataException("VRM JSON 值无效。");
                    Position+=literal.Length; node.value=c=='n' ? null : (object)(c=='t');
                }
                else
                {
                    int start=Position; if (source[Position]=='-') Position++;
                    if (Position>=source.Length || !char.IsDigit(source[Position])) throw new InvalidDataException("VRM JSON 数值无效。");
                    if (source[Position]=='0') Position++; else while(Position<source.Length && char.IsDigit(source[Position])) Position++;
                    if(Position<source.Length && source[Position]=='.') { Position++; int digits=Position; while(Position<source.Length && char.IsDigit(source[Position])) Position++; if(digits==Position) throw new InvalidDataException("VRM JSON 小数无效。"); }
                    if(Position<source.Length && (source[Position]=='e'||source[Position]=='E')) { Position++; if(Position<source.Length && (source[Position]=='+'||source[Position]=='-'))Position++; int digits=Position; while(Position<source.Length&&char.IsDigit(source[Position]))Position++; if(digits==Position)throw new InvalidDataException("VRM JSON 指数无效。"); }
                    if(!double.TryParse(source.Substring(start,Position-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||double.IsNaN(n)||double.IsInfinity(n))throw new InvalidDataException("VRM JSON 数值超出范围。");
                    node.value=n;
                }
                return node;
            }
        }
    }
}
