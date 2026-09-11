using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Riga.Sectores.Comun
{
    // Lector JSON minimo, sin dependencias externas.
    internal static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Valor(s, ref i); }

        static object Valor(string s, ref int i)
        {
            Blancos(s, ref i);
            char c = s[i];
            if (c == '{') return Objeto(s, ref i);
            if (c == '[') return Lista(s, ref i);
            if (c == '"') return Texto(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int ini = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(ini, i - ini), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Objeto(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; Blancos(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Blancos(s, ref i);
                string k = Texto(s, ref i);
                Blancos(s, ref i); i++;
                d[k] = Valor(s, ref i);
                Blancos(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return d;
            }
        }

        static List<object> Lista(string s, ref int i)
        {
            var l = new List<object>();
            i++; Blancos(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Valor(s, ref i));
                Blancos(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++; return l;
            }
        }

        static string Texto(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    if (s[i] == 'n') sb.Append('\n');
                    else if (s[i] == 'r') sb.Append('\r');
                    else if (s[i] == 't') sb.Append('\t');
                    else if (s[i] == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(s[i]);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        static void Blancos(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v; return (d != null && d.TryGetValue(k, out v)) ? v as Dictionary<string, object> : null;
        }
        public static List<object> Arr(Dictionary<string, object> d, string k)
        {
            object v; return (d != null && d.TryGetValue(k, out v) && v is List<object>) ? (List<object>)v : new List<object>();
        }
        public static string Str(Dictionary<string, object> d, string k)
        {
            object v; return (d != null && d.TryGetValue(k, out v) && v != null) ? v.ToString() : "";
        }
        public static double Num(Dictionary<string, object> d, string k)
        {
            object v; return (d != null && d.TryGetValue(k, out v) && v != null) ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
        }
        public static Guid Guid_(Dictionary<string, object> d, string k)
        {
            Guid g; return Guid.TryParse(Str(d, k), out g) ? g : Guid.Empty;
        }
        public static double[][] Pol(List<object> arr)
        {
            if (arr == null) return new double[0][];
            var pts = new List<double[]>(arr.Count);
            foreach (var v in arr)
            {
                var p = v as List<object>;
                if (p != null && p.Count >= 2)
                    pts.Add(new double[] { Convert.ToDouble(p[0], CultureInfo.InvariantCulture),
                                           Convert.ToDouble(p[1], CultureInfo.InvariantCulture) });
            }
            return pts.ToArray();
        }
    }
}
