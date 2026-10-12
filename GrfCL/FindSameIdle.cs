using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.Image;
using Utilities.CommandLine;

namespace GrfCL {
	public static partial class GrfCL {
		private static string _idlePath(string path, string extension) {
			return Path.ChangeExtension(path.Replace('/', '\\'), extension);
		}

		private sealed class IdleFrame {
			public Act Act;
			public int ActionCount;
		}

		// Read only action 0 / frame 0; later frames, sounds and anchors are irrelevant.
		private static IdleFrame _idleRead(byte[] data) {
			using (var reader = new BinaryReader(new MemoryStream(data))) {
				if (reader.ReadUInt16() != 0x4341) throw new InvalidDataException("Invalid ACT header.");
				int minor = reader.ReadByte(), major = reader.ReadByte();
				double version = major + minor / 10.0;
				int count = reader.ReadUInt16();
				reader.BaseStream.Position = 16;
				if (count == 0 || reader.ReadInt32() <= 0) throw new InvalidDataException("No first idle frame.");
				reader.BaseStream.Position += 32;
				int layerCount = reader.ReadInt32();
				if (layerCount < 0 || layerCount > (data.Length - reader.BaseStream.Position) / 16)
					throw new InvalidDataException("Invalid ACT layer count.");
				var act = new Act();
				act.Header = new ActHeader(new GRF.IO.ByteReader(data));
				var action = new GRF.FileFormats.ActFormat.Action();
				var frame = new Frame(); action.Frames.Add(frame); act.Actions.Add(action);
				for (int i = 0; i < layerCount; i++) {
					var layer = new Layer(SpriteIndex.Null);
					layer.OffsetX = version >= 2.6 ? (int)Math.Floor(reader.ReadSingle()) : reader.ReadInt32();
					layer.OffsetY = version >= 2.6 ? (int)Math.Floor(reader.ReadSingle()) : reader.ReadInt32();
					layer.SpriteIndex = reader.ReadInt32(); layer.Mirror = reader.ReadInt32() != 0;
					if (version >= 2.0) {
						layer.Color.R = reader.ReadByte(); layer.Color.G = reader.ReadByte();
						layer.Color.B = reader.ReadByte(); layer.Color.A = reader.ReadByte();
						layer.ScaleX = reader.ReadSingle(); layer.ScaleY = version >= 2.4 ? reader.ReadSingle() : layer.ScaleX;
						layer.Rotation = reader.ReadInt32(); layer.SpriteType = (SpriteTypes)reader.ReadInt32();
						if (version >= 2.5) { layer.Width = reader.ReadInt32(); layer.Height = reader.ReadInt32(); }
					}
					if (layer.SpriteIndex >= 0 && layer.Color.A > 0 && layer.ScaleX != 0 && layer.ScaleY != 0)
						frame.Layers.Add(layer);
				}
				return new IdleFrame { Act = act, ActionCount = count };
			}
		}

		private static void _idleSprite(Act act, byte[] data) {
			act.SetSprite(new Spr(data));
			if (act.Version >= 2.6 && act.Sprite.Header.IsCompatibleWith(3, 2))
				foreach (var layer in act[0, 0].Layers) layer.SpriteType = SpriteTypes.Bgra32;
		}

		private static bool _idleLayoutMatches(Act target, Act candidate) {
			var a = target[0, 0].Layers; var b = candidate[0, 0].Layers;
			if (a.Count != b.Count) return false;
			for (int i = 0; i < a.Count; i++) {
				if (a[i].OffsetX != b[i].OffsetX || a[i].OffsetY != b[i].OffsetY || a[i].Mirror != b[i].Mirror ||
					a[i].Rotation != b[i].Rotation || a[i].ScaleX != b[i].ScaleX || a[i].ScaleY != b[i].ScaleY ||
					!a[i].Color.Equals(b[i].Color)) return false;
			}
			return true;
		}

		// Compare decoded pixels, not SPR compression, image indices or palette indices.
		// Invisible layers and RGB under zero alpha cannot affect the first frame.
		private static string _idleSignature(Act act) {
			var layers = act[0, 0].Layers.Where(l => l.SpriteIndex >= 0 && l.Color.A > 0 && l.ScaleX != 0 && l.ScaleY != 0).ToList();
			if (layers.Count == 0)
				throw new InvalidDataException("The first idle frame has no visible layers.");
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream)) {
				writer.Write(layers.Count);
				foreach (var layer in layers) {
					int index = layer.SpriteIndex + (layer.SpriteType == SpriteTypes.Bgra32 ? act.Sprite.NumberOfIndexed8Images : 0);
					if (index < 0 || index >= act.Sprite.Images.Count)
						throw new InvalidDataException("Idle frame references a missing SPR image.");
					var image = act.Sprite.Images[index];
					writer.Write(layer.OffsetX); writer.Write(layer.OffsetY);
					writer.Write(layer.Mirror); writer.Write(layer.Rotation);
					writer.Write(layer.ScaleX); writer.Write(layer.ScaleY);
					writer.Write(layer.Color.R); writer.Write(layer.Color.G);
					writer.Write(layer.Color.B); writer.Write(layer.Color.A);
					writer.Write(image.Width); writer.Write(image.Height);
					for (int i = 0; i < image.Width * image.Height; i++) {
						byte r, g, b, a;
						if (image.GrfImageType == GrfImageType.Indexed8) {
							int pal = image.Pixels[i] * 4;
							r = image.Palette[pal]; g = image.Palette[pal + 1]; b = image.Palette[pal + 2];
							a = image.Pixels[i] == 0 ? (byte)0 : (byte)255;
						}
						else if (image.GrfImageType == GrfImageType.Bgra32) {
							b = image.Pixels[i * 4]; g = image.Pixels[i * 4 + 1]; r = image.Pixels[i * 4 + 2]; a = image.Pixels[i * 4 + 3];
						}
						else throw new InvalidDataException("Unsupported SPR pixel format.");
						writer.Write(a == 0 ? (byte)0 : r); writer.Write(a == 0 ? (byte)0 : g);
						writer.Write(a == 0 ? (byte)0 : b); writer.Write(a);
					}
				}
				writer.Flush();
				using (var hash = SHA256.Create())
					return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
			}
		}

		private static void _findSameIdle(string target, string folder) {
			if (!new[] { ".act", ".spr" }.Contains(Path.GetExtension(target), StringComparer.OrdinalIgnoreCase))
				throw new ArgumentException("Target must be an .act or .spr file.");
			var timer = Stopwatch.StartNew();
			bool local = File.Exists(target);
			string targetAct = _idlePath(target, ".act"), targetSpr = _idlePath(target, ".spr");
			var reference = _idleRead(local ? File.ReadAllBytes(targetAct) : _grf.FileTable[targetAct].GetDecompressedData()).Act;
			_idleSprite(reference, local ? File.ReadAllBytes(targetSpr) : _grf.FileTable[targetSpr].GetDecompressedData());
			string signature = _idleSignature(reference);
			string prefix = folder.Replace('/', '\\').TrimEnd('\\');
			if (prefix.Length > 0) prefix += "\\";
			var files = _grf.FileTable.Files.Where(p => p.EndsWith(".act", StringComparison.OrdinalIgnoreCase) && p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
			var available = new HashSet<string>(_grf.FileTable.Files, StringComparer.OrdinalIgnoreCase);
			var matches = new List<Tuple<string, int>>();
			int checkedPairs = 0, missingPairs = 0, failed = 0, empty = 0;
			Console.WriteLine("Same first idle frame: " + target);
			foreach (string path in files) {
				if (!available.Contains(_idlePath(path, ".spr"))) { missingPairs++; continue; }
				try {
					var candidate = _idleRead(_grf.FileTable[path].GetDecompressedData());
					checkedPairs++;
					if (candidate.Act[0, 0].Layers.Count == 0) { empty++; continue; }
					if (!_idleLayoutMatches(reference, candidate.Act)) continue;
					_idleSprite(candidate.Act, _grf.FileTable[_idlePath(path, ".spr")].GetDecompressedData());
					if (_idleSignature(candidate.Act) == signature)
						matches.Add(Tuple.Create(path, candidate.ActionCount));
				}
				catch (Exception error) {
					failed++;
					if (failed <= 5) CLHelper.Warning = "Skipped " + path + ": " + error.Message;
				}
			}
			foreach (var match in matches.OrderByDescending(m => m.Item2).ThenBy(m => m.Item1, StringComparer.OrdinalIgnoreCase))
				Console.WriteLine(match.Item1 + "\t" + match.Item2 + " actions");
			CLHelper.Log = String.Format("Found {0} match(es); checked {1} pair(s), {2} missing SPR, {3} empty idle, {4} failed; {5:0.00}s.", matches.Count, checkedPairs, missingPairs, empty, failed, timer.Elapsed.TotalSeconds);
		}
	}
}