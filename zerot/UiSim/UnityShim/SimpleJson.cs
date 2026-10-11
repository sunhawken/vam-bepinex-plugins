using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Small working SimpleJSON (the subset VaM scripts use): parse, build, serialize.
namespace SimpleJSON
{
	public abstract class JSONNode
	{
		public virtual JSONNode this[string key] { get { return null; } set { } }
		public virtual JSONNode this[int index] { get { return null; } set { } }
		public virtual string Value { get { return ""; } set { } }
		public virtual int Count { get { return 0; } }
		public virtual IEnumerable<string> Keys { get { return new string[0]; } }
		public virtual void Add(string key, JSONNode item) { }
		public virtual void Add(JSONNode item) { Add("", item); }
		public virtual bool HasKey(string key) { return false; }
		public virtual int AsInt { get { int v; return int.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0; } set { Value = value.ToString(CultureInfo.InvariantCulture); } }
		public virtual float AsFloat { get { float v; return float.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0f; } set { Value = value.ToString("R", CultureInfo.InvariantCulture); } }
		public virtual bool AsBool { get { bool v; if (bool.TryParse(Value, out v)) return v; return !string.IsNullOrEmpty(Value) && Value != "0"; } set { Value = value ? "true" : "false"; } }
		public virtual JSONArray AsArray { get { return this as JSONArray; } }
		public virtual JSONClass AsObject { get { return this as JSONClass; } }
		public static bool operator ==(JSONNode a, object b) { if (ReferenceEquals(b, null)) return ReferenceEquals(a, null) || a is JSONLazyCreator; return ReferenceEquals(a, b); }
		public static bool operator !=(JSONNode a, object b) { return !(a == b); }
		public override bool Equals(object o) { return ReferenceEquals(this, o); }
		public override int GetHashCode() { return base.GetHashCode(); }
		public static implicit operator JSONNode(string s) { return new JSONData(s); }
		public static implicit operator string(JSONNode d) { return d == null ? null : d.Value; }
		public override string ToString() { var sb = new StringBuilder(); Write(sb); return sb.ToString(); }
		internal abstract void Write(StringBuilder sb);
		internal static void Esc(StringBuilder sb, string s)
		{
			sb.Append('"');
			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default: sb.Append(c); break;
				}
			}
			sb.Append('"');
		}
	}

	public class JSONData : JSONNode
	{
		private string data;
		public JSONData(string v) { data = v; }
		public JSONData(float v) { data = v.ToString("R", CultureInfo.InvariantCulture); }
		public JSONData(int v) { data = v.ToString(CultureInfo.InvariantCulture); }
		public JSONData(bool v) { data = v ? "true" : "false"; }
		public override string Value { get { return data; } set { data = value; } }
		internal override void Write(StringBuilder sb) { Esc(sb, data ?? ""); }
	}

	public class JSONClass : JSONNode
	{
		private readonly Dictionary<string, JSONNode> dict = new Dictionary<string, JSONNode>();
		private readonly List<string> order = new List<string>();
		public override JSONNode this[string key]
		{
			get { JSONNode n; return dict.TryGetValue(key, out n) ? n : new JSONLazyCreator(this, key); }
			set { if (!dict.ContainsKey(key)) order.Add(key); dict[key] = value; }
		}
		public override JSONNode this[int index] { get { return dict[order[index]]; } }
		public override int Count { get { return dict.Count; } }
		public override IEnumerable<string> Keys { get { return order; } }
		public override void Add(string key, JSONNode item) { this[key] = item; }
		public IEnumerator<KeyValuePair<string, JSONNode>> GetEnumerator() { foreach (var k in order) yield return new KeyValuePair<string, JSONNode>(k, dict[k]); }
		public override bool HasKey(string key) { return dict.ContainsKey(key); }
		internal override void Write(StringBuilder sb)
		{
			sb.Append('{');
			for (int i = 0; i < order.Count; i++)
			{
				if (i > 0) sb.Append(',');
				Esc(sb, order[i]); sb.Append(':'); dict[order[i]].Write(sb);
			}
			sb.Append('}');
		}
	}

	public class JSONArray : JSONNode
	{
		private readonly List<JSONNode> list = new List<JSONNode>();
		public override JSONNode this[int index] { get { return list[index]; } set { list[index] = value; } }
		public override int Count { get { return list.Count; } }
		public override void Add(string key, JSONNode item) { list.Add(item); }
		public override void Add(JSONNode item) { list.Add(item); }
		public IEnumerator<JSONNode> GetEnumerator() { return list.GetEnumerator(); }
		internal override void Write(StringBuilder sb)
		{
			sb.Append('[');
			for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); list[i].Write(sb); }
			sb.Append(']');
		}
	}

	// returned for a missing key so that  jc["x"].AsBool = true  creates the entry
	internal class JSONLazyCreator : JSONNode
	{
		private readonly JSONClass owner; private readonly string key;
		public JSONLazyCreator(JSONClass o, string k) { owner = o; key = k; }
		public override string Value { get { return ""; } set { owner[key] = new JSONData(value); } }
		public override int AsInt { get { return 0; } set { owner[key] = new JSONData(value); } }
		public override float AsFloat { get { return 0f; } set { owner[key] = new JSONData(value); } }
		public override bool AsBool { get { return false; } set { owner[key] = new JSONData(value); } }
		public static implicit operator string(JSONLazyCreator c) { return null; }
		internal override void Write(StringBuilder sb) { sb.Append("null"); }
	}

	public static class JSON
	{
		public static JSONNode Parse(string text) { int i = 0; return Read(text, ref i); }
		private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
		private static JSONNode Read(string s, ref int i)
		{
			Ws(s, ref i);
			if (i >= s.Length) return null;
			char c = s[i];
			if (c == '{')
			{
				i++; var o = new JSONClass();
				while (true)
				{
					Ws(s, ref i);
					if (s[i] == '}') { i++; break; }
					string k = ReadString(s, ref i); Ws(s, ref i);
					i++; // ':'
					o[k] = Read(s, ref i); Ws(s, ref i);
					if (s[i] == ',') i++;
				}
				return o;
			}
			if (c == '[')
			{
				i++; var a = new JSONArray();
				while (true)
				{
					Ws(s, ref i);
					if (s[i] == ']') { i++; break; }
					a.Add(Read(s, ref i)); Ws(s, ref i);
					if (s[i] == ',') i++;
				}
				return a;
			}
			if (c == '"') return new JSONData(ReadString(s, ref i));
			int st = i;
			while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
			return new JSONData(s.Substring(st, i - st));
		}
		private static string ReadString(string s, ref int i)
		{
			i++; var sb = new StringBuilder();
			while (s[i] != '"')
			{
				if (s[i] == '\\')
				{
					i++;
					switch (s[i]) { case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break; case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break; default: sb.Append(s[i]); break; }
				}
				else sb.Append(s[i]);
				i++;
			}
			i++;
			return sb.ToString();
		}
	}
}
