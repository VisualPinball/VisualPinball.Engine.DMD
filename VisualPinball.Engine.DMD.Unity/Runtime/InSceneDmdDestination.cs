using System;
using System.Collections.Generic;
using LibDmd;
using LibDmd.Frame;
using LibDmd.Output;
using NLog;
using UnityEngine;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	internal sealed class InSceneDmdDestination : IGray2Destination, IGray4Destination, IGray8Destination, IRgb24Destination, IRgb565Destination, IFixedSizeDestination
	{
		private readonly DisplayComponent _display;
		private readonly InSceneDmdFramePump _framePump;
		private readonly Dimensions _size;
		private readonly bool _previousReceiveGamelogicFrames;
		private bool _disposed;

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private InSceneDmdDestination(DisplayComponent display, DisplayConfig config)
		{
			_display = display;
			_framePump = display.GetComponent<InSceneDmdFramePump>() ?? display.gameObject.AddComponent<InSceneDmdFramePump>();
			_framePump.Initialize(display);
			_size = new Dimensions(config.Width, config.Height);
			_previousReceiveGamelogicFrames = display.ReceiveGamelogicFrames;
			_display.ReceiveGamelogicFrames = false;
			_display.UpdateDimensions(config.Width, config.Height, config.FlipX);
			if (config.LitColor.HasValue) {
				_display.UpdateColor(config.LitColor.Value);
			}
			if (config.UnlitColor.HasValue) {
				_display.UnlitColor = config.UnlitColor.Value;
			}
		}

		public string Name => "VPE In-Scene DMD";
		public bool IsAvailable => !_disposed && _display != null;
		public bool NeedsDuplicateFrames => false;
		public bool NeedsIdentificationFrames => false;
		public Dimensions FixedSize => _size;
		public bool DmdAllowHdScaling => false;

		public static InSceneDmdDestination TryCreate(DisplayConfig config)
		{
			var display = FindDisplay(config.Id);
			if (display == null) {
				Logger.Info($"[DMD] No in-scene display found for \"{config.Id}\".");
				return null;
			}

			Logger.Info($"[DMD] In-scene display destination connected for \"{config.Id}\".");
			return new InSceneDmdDestination(display, config);
		}

		public void RenderGray2(DmdFrame frame)
		{
			Render(DisplayFrameFormat.Dmd2, frame);
		}

		public void RenderGray4(DmdFrame frame)
		{
			Render(DisplayFrameFormat.Dmd4, frame);
		}

		public void RenderGray8(DmdFrame frame)
		{
			Render(DisplayFrameFormat.Dmd8, frame);
		}

		public void RenderRgb24(DmdFrame frame)
		{
			Render(DisplayFrameFormat.Dmd24, frame);
		}

		public void RenderRgb565(DmdFrame frame)
		{
			RenderRgb24(frame.ConvertRgb565ToRgb24());
		}

		public void SetColor(DmdColor color)
		{
			_framePump.EnqueueColor(new Color(color.R / 255f, color.G / 255f, color.B / 255f));
		}

		public void SetPalette(DmdColor[] colors)
		{
			if (colors != null && colors.Length > 0) {
				SetColor(colors[colors.Length - 1]);
			}
		}

		public void ClearColor()
		{
		}

		public void ClearPalette()
		{
		}

		public void ClearDisplay()
		{
			_framePump.Enqueue(DisplayFrameFormat.Dmd2, new byte[_size.Surface]);
		}

		public void Dispose()
		{
			if (_display != null) {
				_display.ReceiveGamelogicFrames = _previousReceiveGamelogicFrames;
			}
			_framePump.Clear();
			_disposed = true;
		}

		private void Render(DisplayFrameFormat format, DmdFrame frame)
		{
			if (_disposed || _display == null || frame?.Data == null) {
				return;
			}

			_framePump.Enqueue(format, frame.Data);
		}

		private static DisplayComponent FindDisplay(string id)
		{
			foreach (var display in UnityEngine.Object.FindObjectsByType<DisplayComponent>(FindObjectsInactive.Include)) {
				if (string.Equals(display.Id, id, StringComparison.OrdinalIgnoreCase)) {
					return display;
				}
			}

			return null;
		}

		private sealed class InSceneDmdFramePump : MonoBehaviour
		{
			private readonly object _syncRoot = new object();

			private DisplayComponent _display;
			private DisplayFrameFormat _pendingFormat;

			// Double buffer: the worker thread writes the latest frame into _back, the main thread
			// swaps it into _front and applies it. The DMD size is fixed for the pump's lifetime, so
			// these are allocated once and reused (no per-frame allocation on the in-scene hop).
			private byte[] _back;
			private byte[] _front;
			private int _pendingLength;
			private Color _pendingColor;
			private bool _hasPendingFrame;
			private bool _hasPendingColor;

			public void Initialize(DisplayComponent display)
			{
				_display = display;
			}

			public void Enqueue(DisplayFrameFormat format, byte[] frame)
			{
				if (frame == null) {
					return;
				}

				lock (_syncRoot) {
					if (_back == null || _back.Length != frame.Length) {
						_back = new byte[frame.Length];
					}
					Buffer.BlockCopy(frame, 0, _back, 0, frame.Length);
					_pendingLength = frame.Length;
					_pendingFormat = format;
					_hasPendingFrame = true;
				}
			}

			public void Clear()
			{
				lock (_syncRoot) {
					_hasPendingFrame = false;
					_hasPendingColor = false;
				}
			}

			public void EnqueueColor(Color color)
			{
				lock (_syncRoot) {
					_pendingColor = color;
					_hasPendingColor = true;
				}
			}

			private void Update()
			{
				DisplayFrameFormat format;
				int length;
				Color color;
				bool hasColor;
				bool hasFrame;

				lock (_syncRoot) {
					if (!_hasPendingFrame && !_hasPendingColor) {
						return;
					}

					hasColor = _hasPendingColor;
					color = _pendingColor;
					_hasPendingColor = false;

					hasFrame = _hasPendingFrame;
					format = _pendingFormat;
					length = _pendingLength;
					_hasPendingFrame = false;

					if (hasFrame) {
						var swap = _front;
						_front = _back;
						_back = swap;
					}
				}

				if (_display == null) {
					return;
				}

				if (hasColor) {
					_display.UpdateColor(color);
				}

				if (!hasFrame || _front == null || _front.Length != length) {
					return;
				}

				_display.UpdateFrame(format, _front);
			}
		}
	}
}
