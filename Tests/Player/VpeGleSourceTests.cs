// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using LibDmd;
using LibDmd.Frame;
using LibDmd.Output;
using NUnit.Framework;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Tests
{
	/// <summary>
	/// Frames from an uncolorized source go through a real render graph, into a destination taking
	/// every format (like the in-scene DMD) and one taking RGB only (like the presentation output).
	/// </summary>
	public sealed class VpeGleSourceTests
	{
		private const int Width = 4, Height = 2;

		[TestCase(DisplayFrameFormat.Dmd2, 3)]
		[TestCase(DisplayFrameFormat.Dmd4, 15)]
		[TestCase(DisplayFrameFormat.Dmd8, 255)]
		public void GrayFramesReachBothDestinations(DisplayFrameFormat format, int max)
		{
			var source = VpeGleSource.Create(new DisplayConfig("dmd0", Width, Height), colorize: false, rgb: false);
			var everyFormat = new EveryFormatDestination("every format");
			var rgbOnly = new RgbOnlyDestination("rgb only");
			using (Render(source, everyFormat, rgbOnly)) {
				var frame = new byte[Width * Height];
				frame[0] = (byte)max;
				source.Push(format, frame, frame.Length);
			}

			Assert.That(everyFormat.Frames, Has.Count.EqualTo(1), "in-scene");
			Assert.That(everyFormat.Frames[0].Format, Is.EqualTo("Gray8"));
			Assert.That(everyFormat.Frames[0].Data[0], Is.EqualTo(255));
			Assert.That(rgbOnly.Frames, Has.Count.EqualTo(1), "presentation");
			Assert.That(rgbOnly.Frames[0].Format, Is.EqualTo("Rgb24"));
		}

		[Test]
		public void RgbFramesReachBothDestinations()
		{
			var source = VpeGleSource.Create(new DisplayConfig("dmd0", Width, Height), colorize: false, rgb: true);
			var everyFormat = new EveryFormatDestination("every format");
			var rgbOnly = new RgbOnlyDestination("rgb only");
			using (Render(source, everyFormat, rgbOnly)) {
				var frame = new byte[Width * Height * 3];
				frame[0] = 200;
				source.Push(DisplayFrameFormat.Dmd24, frame, frame.Length);
			}

			Assert.That(everyFormat.Frames, Has.Count.EqualTo(1));
			Assert.That(everyFormat.Frames[0].Format, Is.EqualTo("Rgb24"));
			Assert.That(everyFormat.Frames[0].Data[0], Is.EqualTo(200));
			Assert.That(rgbOnly.Frames, Has.Count.EqualTo(1));
			Assert.That(rgbOnly.Frames[0].Format, Is.EqualTo("Rgb24"));
		}

		private static IDisposable Render(VpeGleSource source, params IDestination[] destinations)
		{
			var graph = new RenderGraph(new UndisposedReferences(), runOnMainThread: true) {
				Name = "test",
				Source = source,
				Destinations = new List<IDestination>(destinations),
			};
			return graph.Init().StartRendering(null, error => Assert.Fail(error.ToString()));
		}

		private class RgbOnlyDestination : IRgb24Destination
		{
			internal readonly List<(string Format, byte[] Data)> Frames = new List<(string, byte[])>();

			internal RgbOnlyDestination(string name) => Name = name;

			public string Name { get; }
			public bool IsAvailable => true;
			public bool NeedsDuplicateFrames => false;
			public bool NeedsIdentificationFrames => false;

			public void RenderRgb24(DmdFrame frame) => Record("Rgb24", frame);
			public void SetColor(DmdColor color) { }
			public void SetPalette(DmdColor[] colors) { }
			public void ClearColor() { }
			public void ClearPalette() { }
			public void ClearDisplay() { }
			public void Dispose() { }

			protected void Record(string format, DmdFrame frame) => Frames.Add((format, (byte[])frame.Data.Clone()));
		}

		// the formats of the in-scene DMD
		private sealed class EveryFormatDestination : RgbOnlyDestination, IGray2Destination, IGray4Destination, IGray8Destination, IRgb565Destination, IFixedSizeDestination
		{
			internal EveryFormatDestination(string name) : base(name) { }

			public Dimensions FixedSize => new Dimensions(Width, Height);
			public bool DmdAllowHdScaling => false;

			public void RenderGray2(DmdFrame frame) => Record("Gray2", frame);
			public void RenderGray4(DmdFrame frame) => Record("Gray4", frame);
			public void RenderGray8(DmdFrame frame) => Record("Gray8", frame);
			public void RenderRgb565(DmdFrame frame) => Record("Rgb565", frame);
		}
	}
}
