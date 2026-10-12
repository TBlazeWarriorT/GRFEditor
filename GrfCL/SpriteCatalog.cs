using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.Image;
using Utilities.CommandLine;
using Utilities.Services;

namespace GrfCL {
	public static partial class GrfCL {
		private sealed class CatalogSprite {
			public string Path, Kind, Signature = "", Status = "ok", Error = "", Walk = "unknown";
			public int Actions, WalkVisible, WalkAnimated, WalkIdleCopies;
			public List<int> Ids = new List<int>();
			public bool HasWalk { get { return Walk == "animated"; } }
		}

		// Only groups 0 and 1 are needed: eight idle and eight walk directions.
		// Parsing errors are reported per row instead of producing partial animation data.
		private static Act _catalogRead(byte[] data, Spr sprite, out int count) {
			if (data.Length < 16) throw new InvalidDataException("Truncated ACT header.");
			count = BitConverter.ToUInt16(data, 4);
			int selected = Math.Min(count, 16);
			var act = new Act(sprite);
			var reader = new GRF.IO.ByteReader(data);
			act.Header = new ActHeader(reader);
			// Bound parsing to the two groups, without reading later actions or sounds.
			_catalogReadFrames(act, data, selected);
			return act;
		}

		private static void _catalogSkip(BinaryReader reader, long bytes) {
			if (bytes < 0 || bytes > reader.BaseStream.Length - reader.BaseStream.Position)
				throw new InvalidDataException("Truncated ACT frame.");
			reader.BaseStream.Position += bytes;
		}

		private static void _catalogReadFrames(Act act, byte[] data, int count, bool full = false, Dictionary<Frame, string> preciseOffsets = null) {
			using (var reader = new BinaryReader(new MemoryStream(data))) {
				if (reader.ReadUInt16() != 0x4341) throw new InvalidDataException("Invalid ACT header.");
				reader.BaseStream.Position = 16;
				for (int a = 0; a < count; a++) {
					int frames = reader.ReadInt32();
					if (frames < 0 || frames > (data.Length - reader.BaseStream.Position) / 36)
						throw new InvalidDataException("Invalid ACT frame count.");
					var action = new GRF.FileFormats.ActFormat.Action(); act.Actions.Add(action);
					for (int f = 0; f < frames; f++) {
						_catalogSkip(reader, 32);
						int layers = reader.ReadInt32();
						if (layers < 0 || layers > (data.Length - reader.BaseStream.Position) / 16)
							throw new InvalidDataException("Invalid ACT layer count.");
						var frame = new Frame(); action.Frames.Add(frame); var offsets = new StringBuilder();
						for (int l = 0; l < layers; l++) {
							var layer = new Layer(SpriteIndex.Null);
							double x = act.Version >= 2.6 ? (double)reader.ReadSingle() : reader.ReadInt32();
							double y = act.Version >= 2.6 ? (double)reader.ReadSingle() : reader.ReadInt32();
							layer.OffsetX = (int)Math.Floor(x); layer.OffsetY = (int)Math.Floor(y);
							layer.SpriteIndex = reader.ReadInt32(); layer.Mirror = reader.ReadInt32() != 0;
							if (act.Version >= 2.0) {
								layer.Color.R = reader.ReadByte(); layer.Color.G = reader.ReadByte();
								layer.Color.B = reader.ReadByte(); layer.Color.A = reader.ReadByte();
								layer.ScaleX = reader.ReadSingle(); layer.ScaleY = act.Version >= 2.4 ? reader.ReadSingle() : layer.ScaleX;
								layer.Rotation = reader.ReadInt32(); layer.SpriteType = (SpriteTypes)reader.ReadInt32();
								if (act.Version >= 2.5) _catalogSkip(reader, 8);
								if (act.Version >= 2.6 && act.Sprite.Header.IsCompatibleWith(3, 2)) layer.SpriteType = SpriteTypes.Bgra32;
							}
							if (layer.SpriteIndex >= 0 && layer.Color.A > 0 && layer.ScaleX != 0 && layer.ScaleY != 0) {
								frame.Layers.Add(layer);
								if (full && (Double.IsNaN(x) || Double.IsInfinity(x) || Double.IsNaN(y) || Double.IsInfinity(y) || Single.IsNaN(layer.ScaleX) || Single.IsInfinity(layer.ScaleX) || Single.IsNaN(layer.ScaleY) || Single.IsInfinity(layer.ScaleY))) throw new InvalidDataException("Invalid ACT layer transform.");
								if (full) offsets.Append(x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
							}
						}
						if (act.Version >= 2.0) frame.SoundId = reader.ReadInt32();
						if (act.Version >= 2.3) {
							int anchors = reader.ReadInt32();
							if (!full) _catalogSkip(reader, (long)anchors * 16);
							else {
								if (anchors < 0 || anchors > (data.Length - reader.BaseStream.Position) / 16) throw new InvalidDataException("Invalid ACT anchor count.");
								for (int anchor = 0; anchor < anchors; anchor++) frame.Anchors.Add(new Anchor(reader.ReadBytes(4), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
							}
						}
						if (full) preciseOffsets.Add(frame, offsets.ToString());
					}
				}
				if (full && act.Version >= 2.1) {
					int sounds = reader.ReadInt32();
					if (sounds < 0 || sounds > (data.Length - reader.BaseStream.Position) / 40) throw new InvalidDataException("Invalid ACT sound count.");
					for (int sound = 0; sound < sounds; sound++) act.SoundFiles.Add(EncodingService.DisplayEncoding.GetString(reader.ReadBytes(40)).Split('\0')[0]);
					if (act.Version >= 2.2) foreach (var action in act.Actions) {
						action.AnimationSpeed = reader.ReadSingle();
						if (Single.IsNaN(action.AnimationSpeed) || Single.IsInfinity(action.AnimationSpeed) || action.AnimationSpeed < 0) throw new InvalidDataException("Invalid ACT animation delay.");
					}
				}
				if (full && reader.BaseStream.Position != data.Length) throw new InvalidDataException("Unexpected trailing ACT data; exact verification refused.");
			}
		}

		// Cache decoded image digests only for this sprite; discard pixels after its row.
		private static string _catalogFrame(Act act, Frame frame, Dictionary<int, string> images) {
			if (frame.Layers.Count == 0) return "";
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream)) {
				writer.Write(frame.Layers.Count);
				foreach (var layer in frame.Layers) {
					int index = layer.SpriteIndex + (layer.SpriteType == SpriteTypes.Bgra32 ? act.Sprite.NumberOfIndexed8Images : 0);
					if (index < 0 || index >= act.Sprite.Images.Count) throw new InvalidDataException("Frame references a missing SPR image.");
					string digest;
					if (!images.TryGetValue(index, out digest)) {
						var image = act.Sprite.Images[index];
						var pixels = new byte[checked(image.Width * image.Height * 4)];
						for (int i = 0; i < image.Width * image.Height; i++) {
							int p = i * 4;
							if (image.GrfImageType == GrfImageType.Indexed8) {
								if (image.Pixels[i] == 0) continue;
								int pal = image.Pixels[i] * 4;
								pixels[p] = image.Palette[pal]; pixels[p + 1] = image.Palette[pal + 1];
								pixels[p + 2] = image.Palette[pal + 2]; pixels[p + 3] = 255;
							}
							else if (image.GrfImageType == GrfImageType.Bgra32) {
								if (image.Pixels[p + 3] == 0) continue;
								pixels[p] = image.Pixels[p + 2]; pixels[p + 1] = image.Pixels[p + 1];
								pixels[p + 2] = image.Pixels[p]; pixels[p + 3] = image.Pixels[p + 3];
							}
							else throw new InvalidDataException("Unsupported SPR pixel format.");
						}
						using (var hash = SHA256.Create()) digest = Convert.ToBase64String(hash.ComputeHash(pixels));
						images.Add(index, digest);
					}
					var source = act.Sprite.Images[index];
					writer.Write(layer.OffsetX); writer.Write(layer.OffsetY); writer.Write(layer.Mirror);
					writer.Write(layer.Rotation); writer.Write(layer.ScaleX); writer.Write(layer.ScaleY);
					writer.Write(layer.Color.R); writer.Write(layer.Color.G); writer.Write(layer.Color.B); writer.Write(layer.Color.A);
					writer.Write(source.Width); writer.Write(source.Height); writer.Write(digest);
				}
				writer.Flush();
				using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
			}
		}

		private static void _catalogAnalyze(CatalogSprite row, Act act) {
			var images = new Dictionary<int, string>();
			if (act.NumberOfActions == 0 || act[0].Frames.Count == 0) row.Status = "empty-idle";
			else {
				row.Signature = _catalogFrame(act, act[0, 0], images);
				if (row.Signature.Length == 0) row.Status = "empty-idle";
			}
			if (row.Kind == "other") { row.Walk = "not-applicable"; return; }
			bool hasBlank = false;
			for (int direction = 0; direction < 8; direction++) {
				int slot = 8 + direction;
				if (slot >= act.NumberOfActions || act[slot].Frames.Count == 0) continue;
				var walk = act[slot].Frames.Select(f => _catalogFrame(act, f, images)).ToList();
				if (walk.Any(s => s.Length == 0)) { hasBlank = true; continue; }
				row.WalkVisible++;
				if (walk.Distinct().Take(2).Count() < 2) continue;
				var idle = act[direction].Frames.Select(f => _catalogFrame(act, f, images)).ToList();
				// Ignore duplicate consecutive poses and loop repetitions when detecting idle copies.
				if (_catalogCycle(walk) == _catalogCycle(idle)) row.WalkIdleCopies++;
				else row.WalkAnimated++;
			}
			if (row.WalkVisible == 0) row.Walk = hasBlank ? "empty" : "missing";
			else if (row.WalkAnimated == 8) row.Walk = "animated";
			else if (row.WalkAnimated > 0) row.Walk = "partial";
			else if (row.WalkIdleCopies > 0) row.Walk = "idle-copy";
			else row.Walk = "static";
		}

		private static string _catalogCycle(List<string> frames) {
			var poses = new List<string>();
			foreach (string frame in frames) if (poses.Count == 0 || poses[poses.Count - 1] != frame) poses.Add(frame);
			if (poses.Count > 1 && poses[0] == poses[poses.Count - 1]) poses.RemoveAt(poses.Count - 1);
			for (int period = 1; period <= poses.Count; period++) {
				if (poses.Count % period != 0) continue;
				bool equal = true;
				for (int i = period; i < poses.Count; i++) if (poses[i] != poses[i % period]) { equal = false; break; }
				if (equal) {
					var cycle = poses.Take(period).ToList();
					string best = null;
					for (int start = 0; start < period; start++) {
						string rotation = String.Join(";", cycle.Skip(start).Concat(cycle.Take(start)));
						if (best == null || String.CompareOrdinal(rotation, best) < 0) best = rotation;
					}
					return best;
				}
			}
			return "";
		}

		private static Dictionary<string, List<int>> _catalogIds(string file) {
			var result = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			if (String.IsNullOrEmpty(file)) return result;
			string text = File.ReadAllText(file, Encoding.UTF8);
			// Read the existing literal table, never execute arbitrary JavaScript.
			foreach (Match match in Regex.Matches(text, @"^\s*(\d+)\s*:\s*'((?:[^'\\]|\\.)*)'\s*,?", RegexOptions.Multiline)) {
				int id = Int32.Parse(match.Groups[1].Value);
				string name = Regex.Replace(match.Groups[2].Value, @"\\(?:x([0-9a-fA-F]{2})|u([0-9a-fA-F]{4})|(.))", escape => {
					if (escape.Groups[1].Success) return ((char)Convert.ToInt32(escape.Groups[1].Value, 16)).ToString();
					if (escape.Groups[2].Success) return ((char)Convert.ToInt32(escape.Groups[2].Value, 16)).ToString();
					switch (escape.Groups[3].Value) {
						case "n": return "\n";
						case "r": return "\r";
						case "t": return "\t";
						default: return escape.Groups[3].Value;
					}
				});
				var names = new List<string> { name };
				// The client's table retains CP949 filename bytes as Latin-1 characters.
				if (name.All(c => c <= 255)) names.Add(EncodingService.DisplayEncoding.GetString(Encoding.GetEncoding(28591).GetBytes(name)));
				foreach (string key in names.Distinct(StringComparer.OrdinalIgnoreCase)) {
					List<int> ids;
					if (!result.TryGetValue(key, out ids)) result.Add(key, ids = new List<int>());
					if (!ids.Contains(id)) ids.Add(id);
				}
			}
			if (result.Count == 0) throw new InvalidDataException("No numeric sprite-name entries found in the supplied MonsterTable.js.");
			return result;
		}

		private static string _catalogCsv(params object[] values) {
			return String.Join(",", values.Select(v => "\"" + Convert.ToString(v).Replace("\"", "\"\"") + "\""));
		}

		private static string _catalogKind(string path) {
			if (path.IndexOf("\\npc\\", StringComparison.OrdinalIgnoreCase) >= 0) return "npc";
			string monster = EncodingService.DisplayEncoding.GetString(Encoding.GetEncoding(949).GetBytes("몬스터"));
			if (path.IndexOf("\\" + monster + "\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
				path.IndexOf("\\monster\\", StringComparison.OrdinalIgnoreCase) >= 0) return "monster";
			return "other";
		}

		private static void _spriteCatalog(string output, string folder, string table) {
			string directory = System.IO.Path.GetFullPath(output);
			string[] names = { "sprites.csv", "npcs.csv", "idle-groups.csv", "walk-upgrades.csv", "verified-upgrades.csv" };
			foreach (string name in names) if (File.Exists(System.IO.Path.Combine(directory, name)))
				throw new IOException("Catalog already exists: " + name + ". Choose a new output folder.");
			var ids = _catalogIds(table);
			string prefix = folder.Replace('/', '\\').TrimEnd('\\'); if (prefix.Length > 0) prefix += "\\";
			var files = _grf.FileTable.Files.Where(p => p.EndsWith(".act", StringComparison.OrdinalIgnoreCase) && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
			var available = new HashSet<string>(_grf.FileTable.Files, StringComparer.OrdinalIgnoreCase);
			var rows = new List<CatalogSprite>();
			var timer = Stopwatch.StartNew(); var progress = Stopwatch.StartNew();
			int failures = 0;
			foreach (string path in files) {
				var row = new CatalogSprite { Path = path, Kind = _catalogKind(path) }; rows.Add(row);
				List<int> mapped;
				if ((row.Kind == "npc" || row.Kind == "monster") && ids.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(path), out mapped)) row.Ids = mapped;
				if (!available.Contains(_idlePath(path, ".spr"))) { row.Status = "missing-spr"; }
				else try {
					byte[] data = _grf.FileTable[path].GetDecompressedData();
					if (data.Length < 16) throw new InvalidDataException("Truncated ACT header.");
					new ActHeader(new GRF.IO.ByteReader(data));
					row.Actions = BitConverter.ToUInt16(data, 4);
					if (row.Kind == "other") {
						// Equipment and effects only participate in idle grouping, not the NPC walk catalog.
						if (row.Actions == 0 || BitConverter.ToInt32(data, 16) == 0) {
							row.Status = "empty-idle"; row.Walk = "not-applicable";
						}
						else {
							var first = _idleRead(data).Act;
							byte[] spriteData = _grf.FileTable[_idlePath(path, ".spr")].GetDecompressedData();
							// Existing first-image decoder avoids loading unrelated equipment frames.
							if (first[0, 0].Layers.All(l => l.SpriteIndex == 0 && l.SpriteType == SpriteTypes.Indexed8) && spriteData.Length >= 8 && spriteData[3] < 3) {
								var image = Spr.GetFirstImage(spriteData);
								if (image == null) throw new InvalidDataException("No first SPR image.");
								var sprite = new Spr(); sprite.Images.Add(image); first.SetSprite(sprite);
							}
							else _idleSprite(first, spriteData);
							_catalogAnalyze(row, first);
						}
					}
					else {
						var spr = new Spr(_grf.FileTable[_idlePath(path, ".spr")].GetDecompressedData());
						_catalogAnalyze(row, _catalogRead(data, spr, out row.Actions));
					}
				}
				catch (Exception error) {
					row.Status = "error"; row.Walk = "unknown"; row.Signature = ""; row.Error = error.Message;
					if (++failures <= 5) CLHelper.Warning = "Skipped " + path + ": " + error.Message;
				}
				if (progress.Elapsed.TotalSeconds >= 5) {
					Console.WriteLine("Catalog: " + rows.Count + "/" + files.Count + " ACTs; " + timer.Elapsed.TotalSeconds.ToString("0.0") + "s."); progress.Restart();
				}
			}
			var groups = rows.Where(r => r.Signature.Length > 0).GroupBy(r => r.Signature).ToDictionary(g => g.Key, g => g.ToList());
			Directory.CreateDirectory(directory);
			string header = "ids,kind,act,action_entries,walk,walk_visible_directions,walk_animated_directions,walk_idle_copy_directions,idle_signature,status,error,walk_candidates";
			foreach (string name in new[] { "sprites.csv", "npcs.csv" })
				using (var writer = new StreamWriter(System.IO.Path.Combine(directory, name), false, new UTF8Encoding(true))) {
					writer.WriteLine(header);
					foreach (var row in rows.Where(r => name == "sprites.csv" || r.Kind == "npc"))
						writer.WriteLine(_catalogCsv(String.Join(";", row.Ids), row.Kind, row.Path, row.Actions, row.Walk, row.WalkVisible, row.WalkAnimated, row.WalkIdleCopies, row.Signature, row.Status, row.Error, row.Signature.Length == 0 || row.HasWalk ? "" : String.Join(";", groups[row.Signature].Where(r => (r.Kind == "npc" || r.Kind == "monster") && r.HasWalk).Select(r => r.Path))));
				}
			int duplicateGroups = 0, upgrades = 0;
			using (var writer = new StreamWriter(System.IO.Path.Combine(directory, "idle-groups.csv"), false, new UTF8Encoding(true))) {
				writer.WriteLine("idle_signature,group_size,ids,kind,act,action_entries,walk");
				foreach (var group in groups.Values.Where(g => g.Count > 1).OrderBy(g => g[0].Path, StringComparer.OrdinalIgnoreCase)) {
					duplicateGroups++;
					foreach (var row in group.OrderByDescending(r => r.Actions).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
						writer.WriteLine(_catalogCsv(row.Signature, group.Count, String.Join(";", row.Ids), row.Kind, row.Path, row.Actions, row.Walk));
				}
			}
			using (var writer = new StreamWriter(System.IO.Path.Combine(directory, "walk-upgrades.csv"), false, new UTF8Encoding(true))) {
				writer.WriteLine("source_ids,source_act,source_walk,candidate_ids,candidate_act,candidate_action_entries,candidate_walk,match");
				foreach (var source in rows.Where(r => r.Kind == "npc" && !r.HasWalk && r.Signature.Length > 0))
					foreach (var candidate in groups[source.Signature].Where(r => (r.Kind == "npc" || r.Kind == "monster") && r.HasWalk)) {
						upgrades++;
						writer.WriteLine(_catalogCsv(String.Join(";", source.Ids), source.Path, source.Walk, String.Join(";", candidate.Ids), candidate.Path, candidate.Actions, candidate.Walk, "first-idle-frame"));
					}
			}
			_verifySpriteUpgrades(System.IO.Path.Combine(directory, "walk-upgrades.csv"), System.IO.Path.Combine(directory, "verified-upgrades.csv"));
			CLHelper.Log = String.Format("Catalog: {0} ACTs, {1} NPCs ({2} animated walk), {3} identical-idle groups, {4} walk-upgrade candidates, {5} errors; {6:0.00}s. Output: {7}", rows.Count, rows.Count(r => r.Kind == "npc"), rows.Count(r => r.Kind == "npc" && r.HasWalk), duplicateGroups, upgrades, failures, timer.Elapsed.TotalSeconds, directory);
		}
	}
}