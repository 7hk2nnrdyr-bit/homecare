using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HomeCare.Core.Firebase
{
    /// <summary>
    /// 小さなJSONの読み書き。Firebaseとのやり取りに使う。
    /// コアはUnityにも外部のライブラリにも依存しないため、自前で持っている。
    /// 読んだ値は string / long / double / bool / null / List&lt;object&gt; / Dictionary&lt;string, object&gt; になる。
    /// </summary>
    public static class MiniJson
    {
        public static string Write(object value)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value);
            return builder.ToString();
        }

        public static object Parse(string json)
        {
            var reader = new Reader(json ?? throw new ArgumentNullException(nameof(json)));
            var value = reader.ReadValue();
            reader.SkipSpaces();
            if (!reader.AtEnd)
            {
                throw new FormatException("JSONの後ろに余分な文字があります。");
            }
            return value;
        }

        static void WriteValue(StringBuilder builder, object value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case int _:
                case long _:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case float _:
                case double _:
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new ArgumentException("JSONに書けない数です。");
                    }
                    builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> map:
                    builder.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first)
                        {
                            builder.Append(',');
                        }
                        first = false;
                        WriteString(builder, pair.Key);
                        builder.Append(':');
                        WriteValue(builder, pair.Value);
                    }
                    builder.Append('}');
                    break;
                case IList list:
                    builder.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }
                        WriteValue(builder, list[i]);
                    }
                    builder.Append(']');
                    break;
                default:
                    throw new ArgumentException($"JSONに書けない値です：{value.GetType().Name}");
            }
        }

        static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        class Reader
        {
            readonly string m_Json;
            int m_Index;

            public Reader(string json)
            {
                m_Json = json;
            }

            public bool AtEnd => m_Index >= m_Json.Length;

            public void SkipSpaces()
            {
                while (!AtEnd && char.IsWhiteSpace(m_Json[m_Index]))
                {
                    m_Index++;
                }
            }

            public object ReadValue()
            {
                SkipSpaces();
                if (AtEnd)
                {
                    throw Error("JSONが途中で終わっています。");
                }
                var c = m_Json[m_Index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c))
                        {
                            return ReadNumber();
                        }
                        throw Error($"読めない文字です：{c}");
                }
            }

            Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>();
                m_Index++;
                SkipSpaces();
                if (Peek() == '}')
                {
                    m_Index++;
                    return map;
                }
                while (true)
                {
                    SkipSpaces();
                    var key = ReadString();
                    SkipSpaces();
                    Consume(':');
                    map[key] = ReadValue();
                    SkipSpaces();
                    if (Peek() == ',')
                    {
                        m_Index++;
                        continue;
                    }
                    Consume('}');
                    return map;
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                m_Index++;
                SkipSpaces();
                if (Peek() == ']')
                {
                    m_Index++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipSpaces();
                    if (Peek() == ',')
                    {
                        m_Index++;
                        continue;
                    }
                    Consume(']');
                    return list;
                }
            }

            string ReadString()
            {
                Consume('"');
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("文字列が閉じていません。");
                    }
                    var c = m_Json[m_Index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("文字列が閉じていません。");
                    }
                    var escaped = m_Json[m_Index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (m_Index + 4 > m_Json.Length)
                            {
                                throw Error("文字列が閉じていません。");
                            }
                            builder.Append((char)int.Parse(m_Json.Substring(m_Index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            m_Index += 4;
                            break;
                        default:
                            throw Error($"読めない記号です：\\{escaped}");
                    }
                }
            }

            object ReadNumber()
            {
                var start = m_Index;
                while (!AtEnd && "+-0123456789.eE".IndexOf(m_Json[m_Index]) >= 0)
                {
                    m_Index++;
                }
                var text = m_Json.Substring(start, m_Index - start);
                if (text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0
                    && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                {
                    return whole;
                }
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(m_Json, m_Index, word, 0, word.Length) != 0)
                {
                    throw Error($"読めない値です（{word} のはずの場所）。");
                }
                m_Index += word.Length;
            }

            char Peek() => AtEnd ? '\0' : m_Json[m_Index];

            void Consume(char expected)
            {
                if (Peek() != expected)
                {
                    throw Error($"「{expected}」が必要な場所です。");
                }
                m_Index++;
            }

            FormatException Error(string message) => new FormatException($"{message}（{m_Index}文字目）");
        }
    }
}
