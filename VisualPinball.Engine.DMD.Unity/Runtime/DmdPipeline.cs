using System;
using System.Collections.Generic;
using System.Threading;
using LibDmd;
using LibDmd.Converter;
using LibDmd.Output;
using LibDmd.Output.NativeWindow;
using NLog;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	/// <summary>
	/// Owns a LibDmd <see cref="RenderGraph"/> and drives it from a dedicated worker thread.
	/// </summary>
	/// <remarks>
	/// Frames arrive from Unity's main thread (the gamelogic engine marshals them there) via
	/// <see cref="Push"/>, which only copies the bytes into a reused hand-off buffer and signals
	/// the worker. The worker thread then pushes into the source, so the graph — created with
	/// <c>runOnMainThread: true</c>, i.e. it runs synchronously on whatever thread calls the
	/// source — executes colorization (Serum/VNI) and hardware output (ZeDMD) entirely OFF the
	/// Unity main thread. Destinations that must touch Unity (the in-scene texture) or the OS UI
	/// thread (a host-pumped native window) re-marshal back to the main thread themselves:
	/// the in-scene destination via its own frame pump, the host-pumped window via <see cref="PumpMainThread"/>.
	/// </remarks>
	internal sealed class DmdPipeline : IDisposable
	{
		private readonly DisplayConfig _display;
		private readonly VpeGleSource _source;
		private readonly List<IDestination> _destinations;
		private readonly RenderGraph _renderGraph;
		private readonly IDisposable _renderer;
		private readonly INativeDmdWindow _hostPumpedWindow;

		// Worker-thread hand-off. _pending is written by the main thread and swapped into _work by
		// the worker; both grow once to the frame size and are then reused (no per-frame allocation
		// on the hand-off hop). Latest-frame-wins: if the worker falls behind, intermediate frames
		// are dropped, which is correct for a DMD.
		private readonly object _gate = new object();
		private readonly AutoResetEvent _signal = new AutoResetEvent(false);
		private readonly Thread _worker;
		private byte[] _pending;
		private byte[] _work;
		private int _pendingLength;
		private DisplayFrameFormat _pendingFormat;
		private bool _hasPending;
		private bool _clearRequested;
		private volatile bool _running = true;

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		public bool UsesColorization { get; }

		public DmdPipeline(DisplayConfig display, List<IDestination> destinations, AbstractConverter converter, bool flipHorizontally)
		{
			_display = display;
			_destinations = destinations;
			UsesColorization = converter != null;
			_source = VpeGleSource.Create(display, converter != null);
			_renderGraph = new RenderGraph(new UndisposedReferences(), runOnMainThread: true) {
				Name = $"VPE DMD ({display.Id})",
				Source = _source,
				Destinations = destinations,
				Converter = converter,
				FlipHorizontally = display.FlipX ^ flipHorizontally,
			};
			_renderer = _renderGraph.Init().StartRendering(null, exception => Logger.Warn(exception, "[DMD] RenderGraph reported an error."));

			foreach (var destination in destinations) {
				if (destination is INativeDmdWindow window && window.RequiresHostPump) {
					_hostPumpedWindow = window;
					break;
				}
			}

			_worker = new Thread(WorkerLoop) {
				IsBackground = true,
				Name = $"VPE DMD ({display.Id})"
			};
			_worker.Start();
		}

		public bool Matches(DisplayConfig display)
		{
			return string.Equals(_display.Id, display.Id, StringComparison.OrdinalIgnoreCase)
				&& _display.Width == display.Width
				&& _display.Height == display.Height
				&& _display.FlipX == display.FlipX;
		}

		/// <summary>Called on the Unity main thread. Copies the frame and signals the worker.</summary>
		public void Push(DisplayFrameData frame)
		{
			if (frame?.Data == null) {
				return;
			}

			lock (_gate) {
				if (_pending == null || _pending.Length < frame.Data.Length) {
					_pending = new byte[frame.Data.Length];
				}
				Buffer.BlockCopy(frame.Data, 0, _pending, 0, frame.Data.Length);
				_pendingLength = frame.Data.Length;
				_pendingFormat = frame.Format;
				_hasPending = true;
			}
			_signal.Set();
		}

		public void Clear()
		{
			lock (_gate) {
				_clearRequested = true;
			}
			_signal.Set();
		}

		/// <summary>Drives a host-pumped native window from the Unity main thread. No-op otherwise.</summary>
		public void PumpMainThread()
		{
			_hostPumpedWindow?.Pump();
		}

		public void ApplySettings(DmdBridgeSettings settings)
		{
			foreach (var destination in _destinations) {
				if (destination is INativeDmdWindow window) {
					window.ConfigureWindow(NativeWindowDestinationFactory.CreateLayout(settings));
					window.ConfigureStyle(NativeWindowDestinationFactory.CreateStyle(settings));
				}
			}
		}

		public bool TryReadNativeWindowLayout(out int left, out int top, out int width, out int height, out bool stayOnTop, out bool isMovingOrSizing)
		{
			foreach (var destination in _destinations) {
				if (destination is INativeDmdWindow window) {
					left = window.WindowLeft;
					top = window.WindowTop;
					width = window.WindowWidth;
					height = window.WindowHeight;
					stayOnTop = window.WindowStayOnTop;
					isMovingOrSizing = window.IsMovingOrSizing;
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

		private void WorkerLoop()
		{
			while (_running) {
				_signal.WaitOne();

				while (_running) {
					bool doClear;
					bool doFrame;
					DisplayFrameFormat format = default;
					int length = 0;

					lock (_gate) {
						doClear = _clearRequested;
						_clearRequested = false;

						doFrame = _hasPending;
						_hasPending = false;
						if (doFrame) {
							// Swap the just-written buffer in; the old work buffer becomes the next
							// pending slot. No allocation, no copy held under the lock beyond the swap.
							var swap = _work;
							_work = _pending;
							_pending = swap;
							format = _pendingFormat;
							length = _pendingLength;
						}
					}

					if (!_running) {
						return;
					}

					try {
						if (doClear) {
							_renderGraph.ClearDisplay();
						}
						if (doFrame) {
							_source.Push(format, _work, length);
						}
					} catch (Exception exception) {
						Logger.Warn(exception, "[DMD] DMD worker failed to render a frame.");
					}

					if (!doClear && !doFrame) {
						break;
					}
				}
			}
		}

		public void Dispose()
		{
			_running = false;
			_signal.Set();
			if (_worker != null && _worker.IsAlive && !_worker.Join(TimeSpan.FromSeconds(1))) {
				Logger.Warn("[DMD] DMD worker thread did not stop within 1s.");
			}

			_renderer?.Dispose();
			_renderGraph?.Dispose();
			_signal.Dispose();
		}
	}
}
