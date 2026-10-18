// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed class ColorizationAsset
	{
		internal string Id { get; }
		internal string Label { get; }
		internal string[] Files { get; }
		internal bool RequiresKey => Files.Any(file => Path.GetExtension(file).Equals(".pac", StringComparison.OrdinalIgnoreCase));
		internal ColorizationAsset(string id, string label, params string[] files) { Id = id; Label = label; Files = files; }
	}
	internal static class ColorizationCatalog
	{
		internal static IReadOnlyList<ColorizationAsset> Read(string root, string game)
		{
			if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(game)) return Array.Empty<ColorizationAsset>();
			if (game != Path.GetFileName(game) || game == "." || game == "..") throw new ArgumentException("Invalid game key.");
			root = Path.GetFullPath(root);
			string directory = Path.Combine(root, game);
			if (!Directory.Exists(directory)) return Array.Empty<ColorizationAsset>();
			var files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(5001).ToArray();
			if (files.Length > 5000) throw new InvalidOperationException("The game's colorization folder contains too many files.");
			var assets = new List<ColorizationAsset>();
			foreach (string file in files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase)) {
				if (Path.GetFileName(file).StartsWith("._", StringComparison.Ordinal)) continue; // macOS resource forks
				string extension = Path.GetExtension(file).ToLowerInvariant();
				if (extension == ".crz" || extension == ".crom" || extension == ".cromc" || extension == ".pac") assets.Add(new ColorizationAsset(Relative(root, file), Path.GetFileNameWithoutExtension(file) + " · " + (extension == ".pac" ? "PAC" : "Serum"), file));
				if (extension != ".pal") continue;
				string stem = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file));
				var animation = files.FirstOrDefault(candidate => string.Equals(Path.Combine(Path.GetDirectoryName(candidate), Path.GetFileNameWithoutExtension(candidate)), stem, StringComparison.OrdinalIgnoreCase) && (Path.GetExtension(candidate).Equals(".vni", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(candidate).Equals(".fsq", StringComparison.OrdinalIgnoreCase)));
				assets.Add(new ColorizationAsset(Relative(root, file), Path.GetFileNameWithoutExtension(file) + " · " + (animation == null ? "Palette" : "VNI/PAL"), animation == null ? new[] { file } : new[] { file, animation }));
			}
			return assets;
		}
		private static bool InGameFolder(ColorizationAsset asset, string game) => asset.Id.StartsWith(game + "/", StringComparison.OrdinalIgnoreCase) && asset.Id.IndexOf('/', game.Length + 1) < 0;
		private static bool IsSerum(string file) => Path.GetExtension(file).ToLowerInvariant() is ".crz" or ".crom" or ".cromc";
		private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
		/// <param name="hasPacKey">Whether a PAC key is configured. Without one, automatic selection skips PAC files and doesn't fall
		/// back to pin2dmd.pal next to a PAC, since DmdDevice fails to load such a folder and shows the uncolorized frames.</param>
		internal static ColorizationAsset Select(string root, string game, string selection, bool hasPacKey = false)
		{
			if (selection == "none") return null;
			var assets = Read(root, game);
			if (selection == "auto") {
				if (assets.Count <= 1) return assets.FirstOrDefault(asset => hasPacKey || !asset.RequiresKey);
				// Several: what DmdDevice itself loads from the game folder, like the standalone bridge did. Serum first (the newest
				// .cRZ, .cROM or .cROMc), then VNI, which loads the newest PAC whatever its name, and pin2dmd.pal only without a PAC.
				var serum = assets.Where(asset => InGameFolder(asset, game) && IsSerum(asset.Files[0]))
					.OrderByDescending(asset => File.GetCreationTimeUtc(asset.Files[0])).FirstOrDefault();
				if (serum != null) return serum;
				var pac = assets.Where(asset => InGameFolder(asset, game) && asset.RequiresKey)
					.OrderByDescending(asset => File.GetCreationTimeUtc(asset.Files[0])).FirstOrDefault();
				if (pac != null) return hasPacKey ? pac : null;
				var pal = assets.FirstOrDefault(asset => string.Equals(asset.Id, game + "/pin2dmd.pal", StringComparison.OrdinalIgnoreCase));
				if (pal != null) return pal;
				throw new InvalidOperationException("Several colorizations are available. Select one for this game.");
			}
			return assets.FirstOrDefault(asset => asset.Id == selection) ?? throw new FileNotFoundException("Selected colorization is missing: " + selection);
		}
		/// <summary>LibDmd accepts a game folder. An owned cache presents exactly the selected files, leaving the library unchanged.</summary>
		internal static IDisposable Stage(ColorizationAsset asset, string cache, string game, out string root)
		{
			if (game != Path.GetFileName(game) || game == "." || game == "..") throw new ArgumentException("Invalid game key.");
			root = Path.GetFullPath(Path.Combine(cache, Guid.NewGuid().ToString("N")));
			string owned = root;
			if (!owned.StartsWith(Path.GetFullPath(cache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cache location.");
			Directory.CreateDirectory(Path.Combine(owned, game));
			try {
				foreach (string source in asset.Files) {
					string extension = Path.GetExtension(source).ToLowerInvariant();
					string filename = extension == ".crz" ? game + ".cRZ" : extension == ".crom" ? game + ".cROM" : extension == ".cromc" ? game + ".cROMc" : "pin2dmd" + extension;
					File.Copy(source, Path.Combine(owned, game, filename));
				}
			} catch { Directory.Delete(owned, true); throw; }
			return new Subscription(() => { if (Directory.Exists(owned)) Directory.Delete(owned, true); });
		}
	}
}
