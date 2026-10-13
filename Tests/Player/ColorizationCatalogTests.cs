// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualPinball.Engine.DMD.Player;

namespace VisualPinball.Engine.DMD.Tests
{
	[TestFixture]
	public sealed class ColorizationCatalogTests
	{
		private string _root;
		[SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "vpe-colorization-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
		[TearDown] public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
		private string Asset(string path)
		{
			string file = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, path); return file;
		}
		[Test]
		public void VariantsHaveStableIdsAndKeepPaletteAnimationsTogether()
		{
			Asset("game/first/pin2dmd.pal"); Asset("game/first/pin2dmd.vni"); Asset("game/second/game.crz"); Asset("game/third/pin2dmd.pac");
			var assets = ColorizationCatalog.Read(_root, "game");
			Assert.That(assets.Count, Is.EqualTo(3));
			Assert.That(assets.Single(asset => asset.Id == "game/first/pin2dmd.pal").Files.Length, Is.EqualTo(2));
			Assert.That(assets.Single(asset => asset.Id == "game/third/pin2dmd.pac").RequiresKey, Is.True);
		}
		[Test]
		public void AmbiguousAutomaticAndMissingManualChoicesNeverFallBack()
		{
			Asset("game/one.crz"); Asset("game/two.crz");
			Assert.Throws<InvalidOperationException>(() => ColorizationCatalog.Select(_root, "game", "auto"));
			Assert.Throws<FileNotFoundException>(() => ColorizationCatalog.Select(_root, "game", "game/missing.crz"));
			Assert.That(ColorizationCatalog.Select(_root, "game", "none"), Is.Null);
		}
		[Test]
		public void StagingContainsOnlySelectedVariantAndReleasesOwnedCopies()
		{
			string original = Asset("game/one/game.crz"); Asset("game/two/game.crz");
			var selected = ColorizationCatalog.Select(_root, "game", "game/one/game.crz");
			var lease = ColorizationCatalog.Stage(selected, Path.Combine(_root, "cache"), "game", out string staged);
			Assert.That(File.ReadAllText(Path.Combine(staged, "game", "game.cRZ")), Is.EqualTo("game/one/game.crz"));
			Assert.That(Directory.GetFiles(staged, "*", SearchOption.AllDirectories).Length, Is.EqualTo(1));
			lease.Dispose(); lease.Dispose();
			Assert.That(Directory.Exists(staged), Is.False); Assert.That(File.Exists(original), Is.True);
		}
	}
}
