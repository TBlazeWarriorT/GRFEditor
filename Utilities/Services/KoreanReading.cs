using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Services {
	/// <summary>
	/// Hangul, English and roBrowser-style hex for a path, without changing the GRF key.
	/// </summary>
	public static class KoreanReading {
		private static readonly Encoding _cp949 = Encoding.GetEncoding(949);
		private static readonly Encoding _latin1 = Encoding.GetEncoding(28591);
		private static readonly Encoding _western = Encoding.GetEncoding(1252);
		private static readonly string[] _onset = { "g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h" };
		private static readonly string[] _vowel = { "a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i" };
		private static readonly string[] _coda = { "", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t" };
		private static readonly string[] _carry = { "", "g", "kk", "ks", "n", "nj", "nh", "d", "r", "lg", "lm", "lb", "ls", "lt", "lp", "lh", "m", "b", "bs", "s", "ss", "ng", "j", "ch", "k", "t", "p", "h" };
		private static readonly Dictionary<string, string> _words = new Dictionary<string, string>(StringComparer.Ordinal);
		private static readonly ConcurrentDictionary<string, Info> _cache = new ConcurrentDictionary<string, Info>(StringComparer.Ordinal);
		private static int _maxLen = 1;

		private sealed class Info {
			public string Describe;
			public string English;
			public string Blob;
			public string Hex = "";
			public bool HasHigh;
		}

		static KoreanReading() {
			var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("korean-english.txt");
			if (stream == null)
				return;

			using (stream)
			using (var reader = new StreamReader(stream, Encoding.UTF8)) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					if (line.Length == 0 || line[0] == '#')
						continue;

					int tab = line.IndexOf('\t');
					if (tab <= 0 || tab == line.Length - 1)
						continue;

					string ko = line.Substring(0, tab);
					string en = line.Substring(tab + 1);
					_words[ko] = en;
					if (ko.Length > _maxLen)
						_maxLen = ko.Length;
				}
			}
		}

		public static string Describe(string text) {
			return Read(text).Describe;
		}

		public static string English(string text) {
			return Read(text).English;
		}

		public static string Hex(string text) {
			return Read(text).Hex;
		}

		public static string EncodedHex(string text) {
			Info info = Read(text);
			return info.HasHigh ? info.Hex : null;
		}

		public static bool Contains(string text, string query) {
			if (string.IsNullOrEmpty(query))
				return true;
			if (!string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;

			string blob = Read(text).Blob;
			return blob.Length > 0 && blob.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public static bool IsMatch(string text, Regex regex) {
			if (regex.IsMatch(text ?? ""))
				return true;

			string blob = Read(text).Blob;
			return blob.Length > 0 && regex.IsMatch(blob);
		}

		private static Info Read(string text) {
			if (string.IsNullOrEmpty(text))
				return new Info { Blob = "" };

			return _cache.GetOrAdd(text, Build);
		}

		private static Info Build(string text) {
			var hangul = new StringBuilder(text.Length);
			var english = new StringBuilder(text.Length);
			var hex = new StringBuilder(text.Length);
			bool anyHangul = false;
			bool anyEnglish = false;
			bool anyHex = false;
			bool hangulDiffers = false;

			int start = 0;
			for (int i = 0; i <= text.Length; i++) {
				bool boundary = i == text.Length || text[i] == '\\' || text[i] == '/';
				if (!boundary)
					continue;

				if (i > start)
					AppendSegment(text.Substring(start, i - start), hangul, english, hex, ref anyHangul, ref anyEnglish, ref anyHex, ref hangulDiffers);

				if (i < text.Length) {
					hangul.Append(text[i]);
					english.Append(text[i]);
					hex.Append(text[i]);
				}

				start = i + 1;
			}

			var info = new Info();
			if (!anyHangul && !anyEnglish && !anyHex) {
				info.Blob = "";
				info.Hex = Escaped(BytesOf(text));
				return info;
			}

			var blob = new StringBuilder();
			if (anyHangul) {
				blob.Append(hangul);
				blob.Append(' ');
			}
			if (anyEnglish)
				blob.Append(english);
			info.Blob = blob.ToString();

			var described = new StringBuilder();
			if (hangulDiffers) {
				described.Append("Hangul: ");
				described.Append(hangul);
			}
			if (anyEnglish) {
				if (described.Length > 0)
					described.AppendLine();
				described.Append("English: ");
				described.Append(english);
			}
			string rendered = english.ToString();
			if (ContainsHangulScript(rendered))
				rendered = RomanizeEmbedded(rendered);
			info.English = anyEnglish ? rendered : null;
			info.Describe = described.Length == 0 ? null : described.ToString();
			info.HasHigh = anyHex;
			info.Hex = hex.ToString();
			return info;
		}

		private static void AppendSegment(string segment, StringBuilder hangul, StringBuilder english, StringBuilder hex, ref bool anyHangul, ref bool anyEnglish, ref bool anyHex, ref bool hangulDiffers) {
			string stem;
			string ext;
			SplitExtension(segment, out stem, out ext);

			byte[] bytes = BytesOf(stem);
			string ko = _cp949.GetString(bytes);
			string en = Translate(ko);
			bool high = false;
			for (int i = 0; i < bytes.Length; i++) {
				if (bytes[i] >= 0x80) {
					high = true;
					break;
				}
			}

			if (ko != stem)
				hangulDiffers = true;
			if (HasHangul(ko))
				anyHangul = true;
			hangul.Append(ko);
			hangul.Append(ext);

			if (en != null) {
				anyEnglish = true;
				english.Append(en);
			}
			else {
				english.Append(ko);
			}
			english.Append(ext);

			if (high) {
				anyHex = true;
				hex.Append(Escaped(bytes));
			}
			else {
				hex.Append(stem);
			}
			hex.Append(ext);
		}

		private static string Translate(string hangul) {
			if (hangul.Length == 0 || !HasHangul(hangul))
				return null;

			string exact;
			if (_words.TryGetValue(hangul, out exact))
				return exact;

			var sb = new StringBuilder();
			int i = 0;
			while (i < hangul.Length) {
				if (!IsSyllable(hangul[i])) {
					int j = i + 1;
					while (j < hangul.Length && !IsSyllable(hangul[j]))
						j++;
					string run = hangul.Substring(i, j - i).Trim();
					if (run.Length > 0) {
						if (sb.Length > 0)
							sb.Append(' ');
						sb.Append(run);
					}
					i = j;
					continue;
				}

				string gloss;
				int end = MatchWord(hangul, i, out gloss);
				if (end == i) {
					int j = i + 1;
					while (j < hangul.Length && IsSyllable(hangul[j]) && MatchWord(hangul, j, out gloss) == j)
						j++;
					if (sb.Length > 0)
						sb.Append(' ');
					sb.Append(RomanizeSpan(hangul, i, j));
					i = j;
					continue;
				}

				if (sb.Length > 0)
					sb.Append(' ');
				sb.Append(gloss);
				i = end;
			}

			string rendered = sb.ToString();
			return ContainsHangulScript(rendered) ? RomanizeEmbedded(rendered) : rendered;
		}

		private static int MatchWord(string hangul, int i, out string gloss) {
			gloss = null;
			int bestEnd = i;
			var compact = new StringBuilder();
			int p = i;
			while (compact.Length < _maxLen && p < hangul.Length && IsSyllable(hangul[p])) {
				compact.Append(hangul[p]);
				p++;
				string found;
				if (compact.Length >= 2 && _words.TryGetValue(compact.ToString(), out found)) {
					gloss = found;
					bestEnd = p;
				}

				int q = p;
				while (q < hangul.Length && hangul[q] == ' ')
					q++;
				if (q > p && q < hangul.Length && IsSyllable(hangul[q]))
					p = q;
			}

			return bestEnd;
		}

		private static bool IsSyllable(char c) {
			return c >= 0xAC00 && c <= 0xD7A3;
		}

		private static bool ContainsHangulScript(string text) {
			for (int i = 0; i < text.Length; i++) {
				char c = text[i];
				if (IsSyllable(c) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F))
					return true;
			}
			return false;
		}

		private static string RomanizeEmbedded(string text) {
			var sb = new StringBuilder(text.Length);
			int i = 0;
			while (i < text.Length) {
				if (!IsSyllable(text[i])) {
					sb.Append(RomanizeJamo(text[i]));
					i++;
					continue;
				}

				int j = i + 1;
				while (j < text.Length && IsSyllable(text[j]))
					j++;
				sb.Append(RomanizeSpan(text, i, j));
				i = j;
			}
			return sb.ToString();
		}

		private static string RomanizeSpan(string text, int start, int end) {
			var sb = new StringBuilder();
			string carried = null;
			int prevFinal = 0;
			for (int i = start; i < end; i++) {
				if (!IsSyllable(text[i]))
					continue;

				int code = text[i] - 0xAC00;
				int onset = code / 588;
				int vowel = (code % 588) / 28;
				int final = code % 28;
				bool vowelNext = i + 1 < end && IsSyllable(text[i + 1]) && (text[i + 1] - 0xAC00) / 588 == 11;

				if (carried != null) {
					sb.Append(carried);
					carried = null;
				}
				else if (onset == 5 && (prevFinal == 4 || prevFinal == 8))
					sb.Append("l");
				else
					sb.Append(_onset[onset]);

				sb.Append(_vowel[vowel]);
				if (final != 0 && vowelNext)
					carried = _carry[final];
				else
					sb.Append(_coda[final]);
				prevFinal = final;
			}

			if (carried != null)
				sb.Append(carried);
			return sb.ToString();
		}

		private static string RomanizeJamo(char c) {
			if (c < 0x3131 || c > 0x3163)
				return (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F) ? "" : c.ToString();

			switch (c) {
				case '\u3131': return "g";
				case '\u3132': return "kk";
				case '\u3134': return "n";
				case '\u3137': return "d";
				case '\u3138': return "tt";
				case '\u3139': return "r";
				case '\u3141': return "m";
				case '\u3142': return "b";
				case '\u3143': return "pp";
				case '\u3145': return "s";
				case '\u3146': return "ss";
				case '\u3147': return "ng";
				case '\u3148': return "j";
				case '\u3149': return "jj";
				case '\u314A': return "ch";
				case '\u314B': return "k";
				case '\u314C': return "t";
				case '\u314D': return "p";
				case '\u314E': return "h";
				case '\u314F': return "a";
				case '\u3150': return "ae";
				case '\u3151': return "ya";
				case '\u3153': return "eo";
				case '\u3154': return "e";
				case '\u3155': return "yeo";
				case '\u3157': return "o";
				case '\u315C': return "u";
				case '\u3160': return "yu";
				case '\u3161': return "eu";
				case '\u3163': return "i";
				default: return "";
			}
		}

		private static byte[] BytesOf(string text) {
			if (HasHangul(text))
				return _cp949.GetBytes(text);
			for (int i = 0; i < text.Length; i++) {
				if (text[i] > 0xFF)
					return _western.GetBytes(text);
			}
			return _latin1.GetBytes(text);
		}

		private static bool HasHangul(string text) {
			for (int i = 0; i < text.Length; i++) {
				if (text[i] >= 0xAC00 && text[i] <= 0xD7A3)
					return true;
			}
			return false;
		}

		private static void SplitExtension(string segment, out string stem, out string ext) {
			int dot = segment.LastIndexOf('.');
			if (dot > 0 && segment.Length - dot <= 8) {
				for (int i = dot + 1; i < segment.Length; i++) {
					char c = segment[i];
					if (c >= 128 || !char.IsLetterOrDigit(c)) {
						stem = segment;
						ext = "";
						return;
					}
				}
				stem = segment.Substring(0, dot);
				ext = segment.Substring(dot);
				return;
			}

			stem = segment;
			ext = "";
		}

		private static string Escaped(byte[] bytes) {
			var sb = new StringBuilder(bytes.Length * 4);
			for (int i = 0; i < bytes.Length; i++) {
				sb.Append("\\x");
				sb.Append(bytes[i].ToString("x2"));
			}
			return sb.ToString();
		}
	}
}
