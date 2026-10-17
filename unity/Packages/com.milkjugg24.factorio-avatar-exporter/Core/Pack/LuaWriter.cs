using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// Writes data as a Lua table literal with exactly the layout of <c>dumps()</c> in
    /// tools/lua_table.py, so packs read the same as the Python tools write them.
    /// </summary>
    public static class LuaWriter
    {
        private const int Indent = 2;
        private const int MaxInlineLength = 80;

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if",
            "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        };

        /// <summary>
        /// The Lua literal of a value tree (without a leading "return"). Supported values:
        /// <list type="bullet">
        /// <item><c>IDictionary&lt;string, object&gt;</c>: a table, keys sorted ordinally, one entry per
        /// line; identifier keys are written bare, other keys and Lua keywords as ["key"];</item>
        /// <item><c>IList</c>: a sequence, on one line when all items are scalars and the line has at
        /// most 80 characters, otherwise one item per line;</item>
        /// <item><c>string</c>, <c>int</c>, <c>double</c> (see <see cref="FormatNumber"/>) and
        /// <c>bool</c>.</item>
        /// </list>
        /// Nested tables are indented by two spaces and every entry ends with a comma.
        /// </summary>
        public static string Serialize(object value)
        {
            var output = new StringBuilder();
            Write(value, 0, output);
            return output.ToString();
        }

        /// <summary>
        /// A double as Python's repr() writes it, so Lua reads it back as the same float: the shortest
        /// digits that round-trip, always with a '.' or an exponent (1.0, 0.0001, 1e-05, 1e+16).
        /// </summary>
        /// <exception cref="ArgumentException">The value is infinite or NaN.</exception>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Lua literals can not hold infinity or NaN", nameof(value));
            }
            bool negative = BitConverter.DoubleToInt64Bits(value) < 0;
            string sign = negative ? "-" : "";
            if (value == 0)
            {
                return sign + "0.0";
            }
            (string digits, int point) = ShortestDigits(Math.Abs(value));
            // value = 0.<digits> x 10^point; Python switches to exponent notation outside 1e-4 .. 1e16
            if (point <= -4 || point > 16)
            {
                string mantissa = digits.Length > 1 ? digits.Substring(0, 1) + "." + digits.Substring(1) : digits;
                int exponent = point - 1;
                return sign + mantissa + (exponent < 0 ? "e-" : "e+") +
                    Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
            }
            if (point <= 0)
            {
                return sign + "0." + new string('0', -point) + digits;
            }
            if (point >= digits.Length)
            {
                return sign + digits + new string('0', point - digits.Length) + ".0";
            }
            return sign + digits.Substring(0, point) + "." + digits.Substring(point);
        }

        /// <summary>A string literal escaped like lua_table.py: \" \\ \n, other control characters as \ddd.</summary>
        public static string FormatString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var output = new StringBuilder(value.Length + 2);
            output.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                {
                    output.Append('\\').Append(c);
                }
                else if (c == '\n')
                {
                    output.Append("\\n");
                }
                else if (c < 32 || c == 127)
                {
                    output.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
                }
                else
                {
                    output.Append(c);
                }
            }
            output.Append('"');
            return output.ToString();
        }

        /// <summary>The significant digits ("12345", no leading or trailing zeros) of a positive double
        /// and the position of the decimal point: value = 0.digits x 10^point.</summary>
        private static (string digits, int point) ShortestDigits(double value)
        {
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            int exponent = 0;
            int e = text.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0)
            {
                exponent = int.Parse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                text = text.Substring(0, e);
            }
            int dot = text.IndexOf('.');
            string digits = dot >= 0 ? text.Remove(dot, 1) : text;
            int point = (dot >= 0 ? dot : text.Length) + exponent;
            int leadingZeros = 0;
            while (digits[leadingZeros] == '0')
            {
                leadingZeros++;
            }
            return (digits.Substring(leadingZeros).TrimEnd('0'), point - leadingZeros);
        }

        private static void Write(object value, int level, StringBuilder output)
        {
            if (value is IDictionary<string, object> table)
            {
                WriteTable(table, level, output);
            }
            else if (value is IList list)
            {
                WriteSequence(list, level, output);
            }
            else
            {
                output.Append(Scalar(value));
            }
        }

        private static void WriteTable(IDictionary<string, object> table, int level, StringBuilder output)
        {
            if (table.Count == 0)
            {
                output.Append("{}");
                return;
            }
            var keys = new List<string>(table.Keys);
            keys.Sort(StringComparer.Ordinal);
            string pad = new string(' ', Indent * (level + 1));
            output.Append("{\n");
            foreach (string key in keys)
            {
                output.Append(pad).Append(Key(key)).Append(" = ");
                Write(table[key], level + 1, output);
                output.Append(",\n");
            }
            output.Append(' ', Indent * level).Append('}');
        }

        private static void WriteSequence(IList list, int level, StringBuilder output)
        {
            if (list.Count == 0)
            {
                output.Append("{}");
                return;
            }
            string line = InlineSequence(list);
            if (line != null && CodePoints.Count(line) <= MaxInlineLength)
            {
                output.Append(line);
                return;
            }
            string pad = new string(' ', Indent * (level + 1));
            output.Append("{\n");
            foreach (object item in list)
            {
                output.Append(pad);
                Write(item, level + 1, output);
                output.Append(",\n");
            }
            output.Append(' ', Indent * level).Append('}');
        }

        /// <summary>"{ a, b, c }" for a list of scalars, null when it holds a table.</summary>
        private static string InlineSequence(IList list)
        {
            var parts = new string[list.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                object item = list[i];
                if (item is IList || item is IDictionary<string, object>)
                {
                    return null;
                }
                parts[i] = Scalar(item);
            }
            return "{ " + string.Join(", ", parts) + " }";
        }

        private static string Key(string key) => IsIdentifier(key) && !Keywords.Contains(key) ? key : "[" + FormatString(key) + "]";

        private static bool IsIdentifier(string key)
        {
            if (key.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
                if (!letter && (i == 0 || c < '0' || c > '9'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Scalar(object value)
        {
            switch (value)
            {
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case double d:
                    return FormatNumber(d);
                case string s:
                    return FormatString(s);
                case null:
                    throw new ArgumentException("Lua table literals can not hold null (nil) values");
                default:
                    throw new ArgumentException("can not write a " + value.GetType().Name + " as a Lua literal");
            }
        }
    }
}
