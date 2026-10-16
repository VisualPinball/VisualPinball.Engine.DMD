// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

// The two VisualPinball.Unity types VpeGleSource uses, so it compiles here without Unity. Same
// members and values as in VisualPinball.Unity's IGamelogicEngine.cs.
namespace VisualPinball.Unity
{
	public enum DisplayFrameFormat
	{
		Dmd2,
		Dmd4,
		Dmd8,
		Dmd24,
		Segment,
		AlphaNumeric,
		Numeric,
		Video24
	}

	public class DisplayConfig
	{
		public readonly string Id;
		public readonly int Width;
		public readonly int Height;

		public DisplayConfig(string id, int width, int height)
		{
			Id = id;
			Width = width;
			Height = height;
		}
	}
}
