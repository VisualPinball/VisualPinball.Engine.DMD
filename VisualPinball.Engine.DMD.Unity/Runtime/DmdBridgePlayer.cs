using System;
using System.Collections;
using System.Collections.Generic;
using LibDmd;
using LibDmd.Common;
using LibDmd.Converter;
using LibDmd.Converter.Serum;
using LibDmd.Input;
using LibDmd.Output;
using LibDmd.Output.ZeDMD;
using NLog;
using UnityEngine;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	/// <summary>
	/// Bridges VPE gamelogic display events into LibDmd's RenderGraph.
	/// </summary>
	[AddComponentMenu("Pinball/DMD/DMD Bridge Player")]
	public class DmdBridgePlayer : MonoBehaviour
	{
		[Header("Source")]
		[SerializeField] private string _targetDisplayId = "dmd0";

		[Header("Real Hardware")]
		[SerializeField] private bool _enableZeDmd;
		[SerializeField] private string _zeDmdPort;
		[SerializeField] [Range(0, 15)] private int _zeDmdBrightness = 8;
		[SerializeField] private bool _zeDmdDebug;

		[Header("Native Window")]
		[SerializeField] private bool _enableNativeWindow;

		[Header("Colorization")]
		[SerializeField] private bool _enableSerum;
		[SerializeField] private string _altColorPath;
		[SerializeField] private string _romName;

		private Player _player;
		private IGamelogicEngine _gamelogicEngine;
		private DmdPipeline _pipeline;
		private DisplayConfig _currentDisplay;
		private bool _appliedEnableZeDmd;
		private bool _appliedEnableNativeWindow;
		private bool _appliedEnableSerum;
		private bool _missingDisplayWarningLogged;
		private bool _nativeWindowWarningLogged;

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private IEnumerator Start()
		{
			_player = GetComponent<Player>();
			if (_player == null) {
				Logger.Warn("[DMD] DmdBridgePlayer must be attached to the same GameObject as Player.");
				yield break;
			}

			for (var i = 0; i < 120 && _player.GamelogicEngine == null; i++) {
				yield return null;
			}

			_gamelogicEngine = _player.GamelogicEngine;
			if (_gamelogicEngine == null) {
				Logger.Warn("[DMD] No gamelogic engine is available; DMD bridge disabled.");
				yield break;
			}

			_gamelogicEngine.OnDisplaysRequested += HandleDisplaysRequested;
			_gamelogicEngine.OnDisplayClear += HandleDisplayClear;
			_gamelogicEngine.OnDisplayUpdateFrame += HandleDisplayUpdateFrame;
			Logger.Info("[DMD] Bridge connected to gamelogic display events.");
		}

		private void OnDestroy()
		{
			if (_gamelogicEngine != null) {
				_gamelogicEngine.OnDisplaysRequested -= HandleDisplaysRequested;
				_gamelogicEngine.OnDisplayClear -= HandleDisplayClear;
				_gamelogicEngine.OnDisplayUpdateFrame -= HandleDisplayUpdateFrame;
			}

			_pipeline?.Dispose();
			_pipeline = null;
		}

		private void Update()
		{
			if (_currentDisplay == null) {
				return;
			}

			if (_appliedEnableZeDmd == _enableZeDmd
				&& _appliedEnableNativeWindow == _enableNativeWindow
				&& _appliedEnableSerum == _enableSerum) {
				return;
			}

			EnsurePipeline(_currentDisplay, force: true);
		}

		private void HandleDisplaysRequested(object sender, RequestedDisplays requestedDisplays)
		{
			foreach (var display in requestedDisplays.Displays) {
				if (!IsTargetDisplay(display.Id)) {
					continue;
				}

				EnsurePipeline(display);
				_pipeline?.Clear();
				return;
			}
		}

		private void HandleDisplayClear(object sender, string id)
		{
			if (IsTargetDisplay(id)) {
				_pipeline?.Clear();
			}
		}

		private void HandleDisplayUpdateFrame(object sender, DisplayFrameData frame)
		{
			if (!IsTargetDisplay(frame.Id)) {
				return;
			}

			if (_pipeline == null) {
				if (_currentDisplay == null) {
					if (TryInferDisplayConfig(frame, out var inferredDisplay)) {
						Logger.Info($"[DMD] Inferred display \"{inferredDisplay.Id}\" as {inferredDisplay.Width}x{inferredDisplay.Height} from frame stream.");
						EnsurePipeline(inferredDisplay);
					} else if (!_missingDisplayWarningLogged) {
						Logger.Warn($"[DMD] Got frame for \"{frame.Id}\" before display dimensions were requested and could not infer dimensions from {frame.Data?.Length ?? 0} byte(s).");
						_missingDisplayWarningLogged = true;
					}
				}

				if (_pipeline == null) {
					return;
				}
			}

			_pipeline.Push(frame);
		}

		private bool IsTargetDisplay(string id)
		{
			return string.IsNullOrWhiteSpace(_targetDisplayId)
				? id != null && id.StartsWith("dmd", StringComparison.OrdinalIgnoreCase)
				: string.Equals(id, _targetDisplayId, StringComparison.OrdinalIgnoreCase);
		}

		private static bool TryInferDisplayConfig(DisplayFrameData frame, out DisplayConfig display)
		{
			display = null;
			if (frame?.Data == null) {
				return false;
			}

			var pixelCount = frame.Format == DisplayFrameFormat.Dmd24
				? frame.Data.Length / 3
				: frame.Data.Length;
			if (frame.Format == DisplayFrameFormat.Dmd24 && frame.Data.Length % 3 != 0) {
				return false;
			}

			if (!TryInferDimensions(pixelCount, out var width, out var height)) {
				return false;
			}

			display = new DisplayConfig(frame.Id, width, height);
			return true;
		}

		private static bool TryInferDimensions(int pixelCount, out int width, out int height)
		{
			switch (pixelCount) {
				case 128 * 16:
					width = 128;
					height = 16;
					return true;
				case 128 * 32:
					width = 128;
					height = 32;
					return true;
				case 192 * 64:
					width = 192;
					height = 64;
					return true;
				case 256 * 64:
					width = 256;
					height = 64;
					return true;
				default:
					width = 0;
					height = 0;
					return false;
			}
		}

		private void EnsurePipeline(DisplayConfig display, bool force = false)
		{
			_currentDisplay = display;
			if (!force && _pipeline != null && _pipeline.Matches(display)) {
				return;
			}

			_pipeline?.Dispose();
			_appliedEnableZeDmd = _enableZeDmd;
			_appliedEnableNativeWindow = _enableNativeWindow;
			_appliedEnableSerum = _enableSerum;
			var destinations = CreateDestinations(display);
			if (destinations.Count == 0) {
				Logger.Warn("[DMD] No DMD destinations are available; bridge will ignore frames.");
				_pipeline = null;
				return;
			}

			_pipeline = new DmdPipeline(display, destinations, CreateConverter());
			Logger.Info($"[DMD] Pipeline for \"{display.Id}\" created with {destinations.Count} destination(s).");
		}

		private List<IDestination> CreateDestinations(DisplayConfig display)
		{
			var destinations = new List<IDestination>();

			if (_enableZeDmd) {
				try {
					var zeDmd = ZeDMD.GetInstance(_zeDmdDebug, _zeDmdBrightness, string.IsNullOrWhiteSpace(_zeDmdPort) ? null : _zeDmdPort);
					if (zeDmd.IsAvailable) {
						destinations.Add(zeDmd);
					} else {
						Logger.Info("[DMD] ZeDMD destination not available.");
						zeDmd.Dispose();
					}
				} catch (Exception exception) {
					Logger.Warn(exception, "[DMD] Could not initialize ZeDMD destination.");
				}
			}

			if (_enableNativeWindow) {
				var nativeWindow = NativeWindowDestinationFactory.TryCreate(display);
				if (nativeWindow != null) {
					destinations.Add(nativeWindow);
				} else if (!_nativeWindowWarningLogged) {
					Logger.Warn("[DMD] Native-window destination is enabled, but no backend is present yet.");
					_nativeWindowWarningLogged = true;
				}
			}

			return destinations;
		}

		private AbstractConverter CreateConverter()
		{
			if (!_enableSerum || string.IsNullOrWhiteSpace(_altColorPath) || string.IsNullOrWhiteSpace(_romName)) {
				return null;
			}

			try {
				var serum = new Serum(_altColorPath, _romName, ScalerMode.None);
				if (serum.IsLoaded) {
					Logger.Info($"[DMD] Serum colorization loaded ({serum.ColorizationVersion}).");
					return serum;
				}

				serum.Dispose();
				Logger.Info("[DMD] Serum colorization was requested, but no colorization was loaded.");
				return null;
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Could not initialize Serum colorization.");
				return null;
			}
		}
	}
}
