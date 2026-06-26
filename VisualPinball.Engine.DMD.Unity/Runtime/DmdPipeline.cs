using System;
using System.Collections.Generic;
using LibDmd;
using LibDmd.Converter;
using LibDmd.Output;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity
{
	internal sealed class DmdPipeline : IDisposable
	{
		private readonly DisplayConfig _display;
		private readonly VpeGleSource _source;
		private readonly RenderGraph _renderGraph;
		private readonly IDisposable _renderer;

		public DmdPipeline(DisplayConfig display, List<IDestination> destinations, AbstractConverter converter)
		{
			_display = display;
			_source = new VpeGleSource(display.Id, display.Width, display.Height);
			_renderGraph = new RenderGraph(new UndisposedReferences(), runOnMainThread: true) {
				Name = $"VPE DMD ({display.Id})",
				Source = _source,
				Destinations = destinations,
				Converter = converter,
				FlipHorizontally = display.FlipX,
			};
			_renderer = _renderGraph.Init().StartRendering(null);
		}

		public bool Matches(DisplayConfig display)
		{
			return string.Equals(_display.Id, display.Id, StringComparison.OrdinalIgnoreCase)
				&& _display.Width == display.Width
				&& _display.Height == display.Height
				&& _display.FlipX == display.FlipX;
		}

		public void Push(DisplayFrameData frame)
		{
			_source.Push(frame);
		}

		public void Clear()
		{
			_renderGraph.ClearDisplay();
		}

		public void Dispose()
		{
			_renderer?.Dispose();
			_renderGraph?.Dispose();
		}
	}
}
