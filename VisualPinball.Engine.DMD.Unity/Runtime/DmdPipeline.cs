using System;
using System.Collections.Generic;
using LibDmd;
using LibDmd.Converter;
using LibDmd.Output;
using NLog;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	internal sealed class DmdPipeline : IDisposable
	{
		private readonly DisplayConfig _display;
		private readonly VpeGleSource _source;
		private readonly List<IDestination> _destinations;
		private readonly RenderGraph _renderGraph;
		private readonly IDisposable _renderer;
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public DmdPipeline(DisplayConfig display, List<IDestination> destinations, AbstractConverter converter, bool flipHorizontally)
		{
			_display = display;
			_destinations = destinations;
			_source = VpeGleSource.Create(display, converter != null);
			_renderGraph = new RenderGraph(new UndisposedReferences(), runOnMainThread: true) {
				Name = $"VPE DMD ({display.Id})",
				Source = _source,
				Destinations = destinations,
				Converter = converter,
				FlipHorizontally = display.FlipX ^ flipHorizontally,
			};
			_renderer = _renderGraph.Init().StartRendering(null, exception => Logger.Warn(exception, "[DMD] RenderGraph reported an error."));
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

		public void ApplySettings(DmdBridgeSettings settings)
		{
			foreach (var destination in _destinations) {
				NativeWindowDestinationFactory.TryConfigure(destination, settings);
			}
		}

		public bool TryReadNativeWindowLayout(out int left, out int top, out int width, out int height, out bool stayOnTop, out bool isMovingOrSizing)
		{
			foreach (var destination in _destinations) {
				if (NativeWindowDestinationFactory.TryReadLayout(destination, out left, out top, out width, out height, out stayOnTop, out isMovingOrSizing)) {
					return true;
				}
			}

			left = 0;
			top = 0;
			width = 0;
			height = 0;
			stayOnTop = false;
			isMovingOrSizing = false;
			return false;
		}

		public void Dispose()
		{
			_renderer?.Dispose();
			_renderGraph?.Dispose();
		}
	}
}
