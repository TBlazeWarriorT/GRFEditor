using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using Utilities.CommandLine;

namespace GrfCL {
	public static partial class GrfCL {
		private sealed class UpgradeFrame {
			public string Visual, Drawing, Offsets, Anchors, Sound;
		}

		private sealed class UpgradeAsset {
			public string Error = "", FirstFrame = "", Walk = "unknown";
			public UpgradeFrame[][] Frames;
			public float[] Delays;
		}

		private sealed class UpgradeDifference {
			public int Action, Frame;
			public string Reason, SourceValue, CandidateValue;
			public UpgradeDifference(int action, int frame, string reason, string source = "", string candidate = "") { Action = action; Frame = frame; Reason = reason; SourceValue = source; CandidateValue = candidate; }
		}

		private static UpgradeAsset _upgradeLoad(string path, Dictionary<string, UpgradeAsset> cache) {
			path = path.Replace('/', '\\');
			UpgradeAsset result;
			if (cache.TryGetValue(path, out result)) return result;
			result = new UpgradeAsset(); cache.Add(path, result);
			try {
				if (!path.EndsWith(".act", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Candidate path must end in .act.");
				byte[] data = _grf.FileTable[path].GetDecompressedData();
				if (data.Length < 16) throw new InvalidDataException("Truncated ACT header.");
				var spr = new Spr(_grf.FileTable[_idlePath(path, ".spr")].GetDecompressedData());
				var act = new Act(spr); act.Header = new ActHeader(new GRF.IO.ByteReader(data));
				if (act.Version < 1.0 || act.Version > 2.6) throw new InvalidDataException("Unsupported ACT version for exact verification.");
				int count = BitConverter.ToUInt16(data, 4);
				var offsets = new Dictionary<Frame, string>();
				// Reuse the catalog parser in strict full mode, including the sound and delay tables.
				_catalogReadFrames(act, data, count, true, offsets);
				var row = new CatalogSprite { Path = path, Kind = "npc" };
				_catalogAnalyze(row, act); result.Walk = row.Walk;
				if (result.Walk == "animated" && act.Actions.Skip(8).Take(8).Any(a => a.AnimationSpeed <= 0)) result.Walk = "zero-delay";
				var images = new Dictionary<int, string>();
				result.Frames = new UpgradeFrame[count][]; result.Delays = new float[count];
				for (int a = 0; a < count; a++) {
					result.Delays[a] = act[a].AnimationSpeed;
					result.Frames[a] = new UpgradeFrame[act[a].NumberOfFrames];
					for (int f = 0; f < act[a].NumberOfFrames; f++) {
						var frame = act[a, f];
						string sound;
						if (frame.SoundId < 0) sound = "sentinel:" + frame.SoundId;
						else {
							if (frame.SoundId >= act.SoundFiles.Count) throw new InvalidDataException("Frame references a missing sound-table entry.");
							sound = "file:" + act.SoundFiles[frame.SoundId];
						}
						string anchors;
						using (var stream = new MemoryStream())
						using (var writer = new BinaryWriter(stream)) {
							writer.Write(frame.Anchors.Count);
							foreach (var anchor in frame.Anchors) {
								writer.Write(anchor.Unknown); writer.Write(anchor.OffsetX); writer.Write(anchor.OffsetY); writer.Write(anchor.Other);
							}
							writer.Flush(); anchors = Convert.ToBase64String(stream.ToArray());
						}
						var drawing = new Frame();
						foreach (var layer in frame.Layers) { var copy = new Layer(layer); copy.OffsetX = 0; copy.OffsetY = 0; drawing.Layers.Add(copy); }
						result.Frames[a][f] = new UpgradeFrame { Visual = _catalogFrame(act, frame, images), Drawing = _catalogFrame(act, drawing, images), Offsets = offsets[frame], Anchors = anchors, Sound = sound };
					}
				}
				if (count > 0 && result.Frames[0].Length > 0) result.FirstFrame = result.Frames[0][0].Visual;
			}
			catch (Exception error) { result.Error = error.Message; result.Frames = null; result.Delays = null; result.FirstFrame = ""; result.Walk = "unknown"; }
			// Only fingerprints and metadata remain cached; ACT, SPR and pixels can be collected.
			return result;
		}

		private static List<UpgradeDifference> _upgradeDifferences(UpgradeAsset source, UpgradeAsset candidate, int actions, bool visualOnly) {
			var differences = new List<UpgradeDifference>();
			for (int a = 0; a < actions; a++) {
				if (a >= source.Frames.Length) { differences.Add(new UpgradeDifference(a, -1, "source-action-missing", source.Frames.Length.ToString(), candidate.Frames.Length.ToString())); continue; }
				if (a >= candidate.Frames.Length) { differences.Add(new UpgradeDifference(a, -1, "candidate-action-missing", source.Frames.Length.ToString(), candidate.Frames.Length.ToString())); continue; }
				if (source.Frames[a].Length != candidate.Frames[a].Length) differences.Add(new UpgradeDifference(a, -1, "frame-count", source.Frames[a].Length.ToString(), candidate.Frames[a].Length.ToString()));
				if (!visualOnly && source.Delays[a] != candidate.Delays[a]) differences.Add(new UpgradeDifference(a, -1, "animation-delay", ((double)source.Delays[a] * 25).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " ms", ((double)candidate.Delays[a] * 25).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " ms"));
				for (int f = 0; f < Math.Min(source.Frames[a].Length, candidate.Frames[a].Length); f++) {
					var left = source.Frames[a][f]; var right = candidate.Frames[a][f];
					if (left.Drawing != right.Drawing) differences.Add(new UpgradeDifference(a, f, "pixels-or-layer-layout", left.Drawing, right.Drawing));
					if (left.Offsets != right.Offsets) differences.Add(new UpgradeDifference(a, f, "precise-layer-position", left.Offsets, right.Offsets));
					if (!visualOnly && left.Anchors != right.Anchors) differences.Add(new UpgradeDifference(a, f, "anchors", left.Anchors, right.Anchors));
					if (!visualOnly && left.Sound != right.Sound) differences.Add(new UpgradeDifference(a, f, "sound-event", left.Sound, right.Sound));
				}
			}
			return differences;
		}

		private static UpgradeDifference _upgradeCompare(UpgradeAsset source, UpgradeAsset candidate, int actions, bool visualOnly) {
			return _upgradeDifferences(source, candidate, actions, visualOnly).FirstOrDefault();
		}

		private static bool _upgradeIdleCycleEqual(UpgradeAsset source, UpgradeAsset candidate) {
			if (source.Frames.Length < 8 || candidate.Frames.Length < 8) return false;
			for (int a = 0; a < 8; a++) {
				if (source.Frames[a].Length == 0 || candidate.Frames[a].Length == 0) return false;
				string left = _catalogCycle(source.Frames[a].Select(f => f.Drawing + "|" + f.Offsets).ToList());
				string right = _catalogCycle(candidate.Frames[a].Select(f => f.Drawing + "|" + f.Offsets).ToList());
				if (left != right) return false;
			}
			return true;
		}

		// RFC4180-style CSV reader: supports escaped quotes and quoted multiline fields.
		private static List<string[]> _upgradeReadCsv(string file) {
			var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
			bool quoted = false, afterQuote = false, started = false;
			using (var reader = new StreamReader(file, Encoding.UTF8, true)) {
				int next;
				while ((next = reader.Read()) != -1) {
					char c = (char)next;
					if (quoted) {
						if (c == '"') {
							if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
							else { quoted = false; afterQuote = true; }
						}
						else field.Append(c);
						continue;
					}
					if (c == ',') { row.Add(field.ToString()); field.Clear(); started = false; afterQuote = false; continue; }
					if (c == '\r' || c == '\n') {
						if (c == '\r' && reader.Peek() == '\n') reader.Read();
						row.Add(field.ToString()); rows.Add(row.ToArray()); row.Clear(); field.Clear(); started = false; afterQuote = false; continue;
					}
					if (afterQuote) throw new InvalidDataException("Unexpected character after a quoted CSV field.");
					if (c == '"') {
						if (started) throw new InvalidDataException("Unexpected quote in CSV field.");
						quoted = true; started = true;
					}
					else { field.Append(c); started = true; }
				}
			}
			if (quoted) throw new InvalidDataException("Unclosed quoted CSV field.");
			if (started || afterQuote || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
			return rows;
		}

		private static void _verifySpriteUpgrades(string input, string output) {
			string destination = Path.GetFullPath(output);
			if (File.Exists(destination)) throw new IOException("Verification output already exists. Choose a new output file.");
			var rows = _upgradeReadCsv(input);
			if (rows.Count == 0) throw new InvalidDataException("Candidate CSV has no header.");
			string[] header = rows[0];
			if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length) throw new InvalidDataException("Duplicate candidate CSV column.");
			int sourceColumn = Array.IndexOf(header, "source_act"), candidateColumn = Array.IndexOf(header, "candidate_act");
			if (sourceColumn < 0 || candidateColumn < 0) throw new InvalidDataException("Candidate CSV requires source_act and candidate_act columns.");
			string[] extra = { "classification", "idle_equal", "idle_visual_equal", "idle_cycle_equal", "original_actions_equal", "verified_source_walk", "verified_candidate_walk", "walk_gained", "difference_action", "difference_frame", "difference_reason", "source_value", "candidate_value", "idle_difference_types", "original_difference_types", "difference_details", "verification_error" };
			if (header.Any(h => extra.Contains(h))) throw new InvalidDataException("Input already contains verification columns; use the original candidate CSV.");
			foreach (var row in rows.Skip(1)) if (row.Length != header.Length) throw new InvalidDataException("Candidate CSV row has an incorrect number of fields.");
			var cache = new Dictionary<string, UpgradeAsset>(StringComparer.OrdinalIgnoreCase);
			var totals = new Dictionary<string, int>(); var timer = Stopwatch.StartNew(); var progress = Stopwatch.StartNew();
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
			using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) {
				writer.WriteLine(_catalogCsv(header.Concat(extra).Cast<object>().ToArray()));
				foreach (var row in rows.Skip(1)) {
					var source = _upgradeLoad(row[sourceColumn], cache); var candidate = _upgradeLoad(row[candidateColumn], cache);
					string classification, error = ""; UpgradeDifference difference = null;
					bool idleEqual = false, visualEqual = false, cycleEqual = false, originalsEqual = false, gained = false;
					var idleDifferences = new List<UpgradeDifference>(); var originalDifferences = new List<UpgradeDifference>();
					if (source.Error.Length > 0 || candidate.Error.Length > 0) {
						classification = "error";
						error = (source.Error.Length > 0 ? "Source: " + source.Error : "") + (candidate.Error.Length > 0 ? " Candidate: " + candidate.Error : "");
					}
					else {
						idleDifferences = _upgradeDifferences(source, candidate, 8, false);
						var idleDifference = idleDifferences.FirstOrDefault();
						idleEqual = idleDifference == null;
						visualEqual = _upgradeCompare(source, candidate, 8, true) == null;
						cycleEqual = _upgradeIdleCycleEqual(source, candidate);
						originalDifferences = _upgradeDifferences(source, candidate, source.Frames.Length, false);
						var originalsDifference = originalDifferences.FirstOrDefault();
						originalsEqual = originalsDifference == null;
						gained = candidate.Walk == "animated" && source.Walk != "animated";
						if (source.FirstFrame.Length == 0 || source.FirstFrame != candidate.FirstFrame) {
							classification = "no-match"; difference = new UpgradeDifference(0, 0, "first-idle-frame", source.FirstFrame, candidate.FirstFrame);
						}
						else if (!idleEqual) { classification = "first-frame-only"; difference = idleDifference; }
						else if (originalsEqual && gained) classification = "direct-upgrade";
						else {
							classification = "identical-idle"; difference = originalsDifference;
							if (difference == null && !gained) difference = new UpgradeDifference(8, -1, candidate.Walk == "animated" ? "source-already-walks" : "candidate-walk-not-animated");
						}
					}
					if (!totals.ContainsKey(classification)) totals.Add(classification, 0); totals[classification]++;
					var values = row.Cast<object>().Concat(new object[] { classification, error.Length == 0 ? idleEqual.ToString().ToLowerInvariant() : "", error.Length == 0 ? visualEqual.ToString().ToLowerInvariant() : "", error.Length == 0 ? cycleEqual.ToString().ToLowerInvariant() : "", error.Length == 0 ? originalsEqual.ToString().ToLowerInvariant() : "", source.Walk, candidate.Walk, error.Length == 0 ? gained.ToString().ToLowerInvariant() : "", difference == null ? "" : difference.Action.ToString(), difference == null || difference.Frame < 0 ? "" : difference.Frame.ToString(), difference == null ? "" : difference.Reason, difference == null ? "" : difference.SourceValue, difference == null ? "" : difference.CandidateValue, String.Join(";", idleDifferences.Select(d => d.Reason).Distinct()), String.Join(";", originalDifferences.Select(d => d.Reason).Distinct()), String.Join("\n", originalDifferences.Select(d => "action " + d.Action + (d.Frame < 0 ? "" : " / frame " + d.Frame) + ": " + d.Reason + " (" + d.SourceValue + " -> " + d.CandidateValue + ")")), error }).ToArray();
					writer.WriteLine(_catalogCsv(values));
					if (progress.Elapsed.TotalSeconds >= 5) { Console.WriteLine("Verification: " + totals.Values.Sum() + "/" + (rows.Count - 1) + " pairs."); progress.Restart(); }
				}
			}
			CLHelper.Log = String.Format("Verified {0} pair(s), {1} unique asset(s); {2}; {3:0.00}s. Output: {4}", rows.Count - 1, cache.Count, String.Join(", ", totals.OrderBy(t => t.Key).Select(t => t.Key + "=" + t.Value)), timer.Elapsed.TotalSeconds, destination);
		}
	}
}
