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
		private readonly Dimensions _size;
		private readonly bool _previousReceiveGamelogicFrames;
		private int _renderFrameCount;
		private bool _disposed;

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private InSceneDmdDestination(DisplayComponent display, DisplayConfig config)
		{
			_display = display;
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
			_display.UpdateColor(new Color(color.R / 255f, color.G / 255f, color.B / 255f));
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
			_display.Clear();
		}

		public void Dispose()
		{
			if (_display != null) {
				_display.ReceiveGamelogicFrames = _previousReceiveGamelogicFrames;
			}
			_disposed = true;
		}

		private void Render(DisplayFrameFormat format, DmdFrame frame)
		{
			if (_disposed || _display == null || frame?.Data == null) {
				return;
			}

			_renderFrameCount++;
			if (_renderFrameCount % 60 == 1) {
				Logger.Info($"[DMD] In-scene render #{_renderFrameCount}: {format} {frame.Data.Length} byte(s).");
			}

			_display.UpdateFrame(format, frame.Data);
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
	}
}
