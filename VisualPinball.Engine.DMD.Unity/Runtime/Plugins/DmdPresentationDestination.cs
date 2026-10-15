// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Threading;
using System.Diagnostics;
using LibDmd;
using LibDmd.Frame;
using LibDmd.Output;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	/// <summary>LibDmd writes on its worker; the owner publishes the latest complete presentation frame.</summary>
	internal sealed class DmdPresentationDestination : IRgb24Destination
	{
		private static long _instances;
		// per destination, so each one gets to report its first faults
		private readonly VisualPinball.Unity.LogThrottle _faultThrottle = new VisualPinball.Unity.LogThrottle(3, TimeSpan.FromSeconds(30));
		private readonly DisplayResourceSource _source;
		private readonly IDisposable _publication;
		private readonly long _instance;
		private readonly object _gate = new object();
		private byte[] _pending = Array.Empty<byte>();
		private byte[] _displayed = Array.Empty<byte>();
		private int _width, _height, _length;
		private long _sequence;
		private bool _dirty;
		private bool _disposed;
		internal DmdPresentationDestination(PluginApplicationContext application, PluginResourceDescriptor original)
		{
			_instance = Interlocked.Increment(ref _instances);
			_source = new DisplayResourceSource(fault: error => {
				// a consumer of the presentation frames threw: a bug, and it would throw on every frame
				// plugin messages are plain text and reports group them by text, so keep it constant
				if (_faultThrottle.ShouldLog()) {
					application.Logger.Error(error, "A consumer of the DMD presentation failed.");
				}
			});
			_publication = application.Resources.Publish(new PluginResourceDescriptor(original.Id + "/presentation/" + application.Plugin.Id, application.Plugin.Id,
				original.Controller, original.Label + " · DMD presentation", original.Role, ResourceKind.Display, original.Session, _instance, ResourceView.Presentation, DisplayEncoding.Rgb24, original.Id), _source);
		}
		public string Name => "VPE Presentation Resource";
		public bool IsAvailable => !_disposed;
		public bool NeedsDuplicateFrames => false;
		public bool NeedsIdentificationFrames => false;
		// RGB24 frames already contain the graph's color and palette decisions.
		public void SetColor(DmdColor color) { }
		public void ClearColor() { }
		public void SetPalette(DmdColor[] colors) { }
		public void ClearPalette() { }
		public void RenderRgb24(DmdFrame frame)
		{
			lock (_gate) {
				if (_disposed) return;
				int length = checked(frame.Dimensions.Width * frame.Dimensions.Height * 3);
				if (_pending.Length < length) _pending = new byte[length];
				Buffer.BlockCopy(frame.Data, 0, _pending, 0, length); _length = length; _width = frame.Dimensions.Width; _height = frame.Dimensions.Height; _dirty = true;
			}
		}
		internal void Pump()
		{
			int width, height, length;
			lock (_gate) {
				if (_disposed || !_dirty) return;
				var swap = _displayed; _displayed = _pending; _pending = swap;
				width = _width; height = _height; length = _length; _dirty = false;
			}
			_source.Publish(new PluginDisplayFrame(_displayed.AsMemory(0, length), width, height, width * 3, DisplayEncoding.Rgb24, ++_sequence, (long)(Stopwatch.GetTimestamp() * (1000000d / Stopwatch.Frequency)), _instance));
		}
		public void ClearDisplay() { lock (_gate) { if (_pending.Length < _length) _pending = new byte[_length]; Array.Clear(_pending, 0, _pending.Length); _dirty = _length > 0; } }
		public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _publication.Dispose(); _source.Dispose(); }
	}
}
