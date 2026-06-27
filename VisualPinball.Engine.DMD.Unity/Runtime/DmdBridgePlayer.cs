using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using LibDmd;
using LibDmd.Common;
using LibDmd.Converter;
using LibDmd.Converter.Serum;
using LibDmd.Converter.Vni;
using LibDmd.Input;
using LibDmd.Output;
using LibDmd.Output.ZeDMD;
using NLog;
using UnityEngine;
using UnityEngine.Serialization;
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

		[Header("Config")]
		[SerializeField] private string _dmdDeviceIniPath = "DmdDevice.ini";
		[SerializeField] private bool _reloadIniOnChange = true;

		[Header("Real Hardware")]
		[SerializeField] private bool _enableZeDmd;
		[SerializeField] private string _zeDmdPort;
		[SerializeField] [Range(0, 15)] private int _zeDmdBrightness = 8;
		[SerializeField] private bool _zeDmdDebug;

		[Header("Native Window")]
		[SerializeField] private bool _enableNativeWindow;

		[Header("In-Scene Display")]
		[SerializeField] private bool _enableInSceneDisplay = true;

		[Header("Colorization")]
		[FormerlySerializedAs("_enableSerum")]
		[SerializeField] private bool _enableColorization;
		[SerializeField] private string _altColorPath;
		[SerializeField] private string _romName;
		[SerializeField] private ScalerMode _colorizationScalerMode = ScalerMode.None;
		[SerializeField] private string _vniKey;

		[Header("Resolved Colorization")]
		[SerializeField] private string _resolvedAltColorPath;
		[SerializeField] private string _resolvedRomName;
		[SerializeField] private string _resolvedConverter;

		[Header("Diagnostics")]
		[SerializeField] private string _lastInputFrame;
		[SerializeField] private int _inputFrameCount;

		[Header("Native Window Layout")]
		[SerializeField] private int _nativeWindowLeft = 100;
		[SerializeField] private int _nativeWindowTop = 100;
		[SerializeField] private int _nativeWindowWidth = 512;
		[SerializeField] private int _nativeWindowHeight = 128;
		[SerializeField] private bool _nativeWindowStayOnTop;

		[Header("DMD Shader")]
		[SerializeField] [Range(0.05f, 1.5f)] private float _dotSize = 0.92f;
		[SerializeField] [Range(0.0f, 1.0f)] private float _dotRounding = 1.0f;
		[SerializeField] [Range(0.0f, 1.0f)] private float _dotSharpness = 0.8f;
		[SerializeField] private Color _unlitDot = Color.black;
		[SerializeField] [Range(0.0f, 4.0f)] private float _brightness = 0.95f;
		[SerializeField] [Range(0.0f, 4.0f)] private float _dotGlow;
		[SerializeField] [Range(0.0f, 4.0f)] private float _backGlow;
		[SerializeField] [Range(0.1f, 4.0f)] private float _gamma = 1.0f;
		[SerializeField] private Color _glassColor = Color.black;
		[SerializeField] [Range(0.0f, 4.0f)] private float _glassLighting;

		private Player _player;
		private IGamelogicEngine _gamelogicEngine;
		private DmdPipeline _pipeline;
		private DisplayConfig _currentDisplay;
		private DmdBridgeSettings _settings;
		private DmdBridgeSettings _appliedSettings;
		private DmdBridgeSettings _lastFallbackSettings;
		private DateTime _appliedConfigWriteTimeUtc;
		private string _appliedConfigPath;
		private bool _missingDisplayWarningLogged;
		private bool _nativeWindowWarningLogged;
		private bool _missingConfigWarningLogged;
		private float _nextNativeWindowLayoutSyncTime;
		private float _suppressNativeWindowLayoutSyncUntil;
		private bool _defaultAltColorPathResolved;
		private string _defaultAltColorPath;

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

			_settings = LoadSettings();
			_gamelogicEngine.OnDisplaysRequested += HandleDisplaysRequested;
			_gamelogicEngine.OnDisplayClear += HandleDisplayClear;
			_gamelogicEngine.OnDisplayUpdateFrame += HandleDisplayUpdateFrame;
			Logger.Info("[DMD] Bridge connected to gamelogic display events.");
		}

		private void OnDestroy()
		{
			RequestSourceFrameFormat(DisplayFrameFormat.Dmd8);

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
			var currentSettings = LoadSettings();
			var settingsChanged = _settings == null || !_settings.Equals(currentSettings);
			if (settingsChanged) {
				_settings = currentSettings;
			}

			if (_currentDisplay == null) {
				return;
			}

			if (!settingsChanged) {
				SyncNativeWindowLayout();
				return;
			}

			if (!PipelineSettingsChanged()) {
				_pipeline?.ApplySettings(_settings);
				CaptureAppliedSettings();
				_suppressNativeWindowLayoutSyncUntil = Time.unscaledTime + 0.35f;
				SyncNativeWindowLayout();
				return;
			}

			EnsurePipeline(_currentDisplay, force: true);
			SyncNativeWindowLayout();
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

			TrackInputFrame(frame);
			_pipeline.Push(frame);
		}

		private void TrackInputFrame(DisplayFrameData frame)
		{
			_inputFrameCount++;
			if (_inputFrameCount % 60 != 1) {
				return;
			}

			var nonZeroBytes = 0;
			var max = 0;
			if (frame.Data != null) {
				foreach (var value in frame.Data) {
					if (value != 0) {
						nonZeroBytes++;
					}
					if (value > max) {
						max = value;
					}
				}
			}

			_lastInputFrame = $"{frame.Format} {frame.Data?.Length ?? 0} byte(s), nonzero={nonZeroBytes}, max={max}";
			Logger.Info($"[DMD] Input frame #{_inputFrameCount}: {_lastInputFrame}");
		}

		private bool IsTargetDisplay(string id)
		{
			var targetDisplayId = _settings?.TargetDisplayId ?? _targetDisplayId;
			return string.IsNullOrWhiteSpace(targetDisplayId)
				? id != null && id.StartsWith("dmd", StringComparison.OrdinalIgnoreCase)
				: string.Equals(id, targetDisplayId, StringComparison.OrdinalIgnoreCase);
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
			CaptureAppliedSettings();
			var destinations = CreateDestinations(display);
			if (destinations.Count == 0) {
				Logger.Warn("[DMD] No DMD destinations are available; bridge will ignore frames.");
				_pipeline = null;
				return;
			}

			var converter = CreateConverter();
			_resolvedConverter = converter?.Name ?? "none";
			RequestSourceFrameFormat(converter != null ? DisplayFrameFormat.Dmd4 : DisplayFrameFormat.Dmd8);
			_pipeline = new DmdPipeline(display, destinations, converter, _settings.FlipHorizontally);
			Logger.Info($"[DMD] Pipeline for \"{display.Id}\" created with {destinations.Count} destination(s).");
		}

		private void RequestSourceFrameFormat(DisplayFrameFormat format)
		{
			if (_gamelogicEngine is IDisplayFrameFormatPreference frameFormatPreference) {
				frameFormatPreference.RequestDisplayFrameFormat(_settings?.TargetDisplayId ?? _targetDisplayId, format);
			}
		}

		private bool PipelineSettingsChanged()
		{
			return _settings != null && !_settings.TopologyEquals(_appliedSettings);
		}

		private void CaptureAppliedSettings()
		{
			_appliedSettings = _settings.Clone();
		}

		private List<IDestination> CreateDestinations(DisplayConfig display)
		{
			var destinations = new List<IDestination>();

			if (_settings.EnableZeDmd) {
				try {
					var zeDmd = ZeDMD.GetInstance(_settings.ZeDmdDebug, _settings.ZeDmdBrightness, string.IsNullOrWhiteSpace(_settings.ZeDmdPort) ? null : _settings.ZeDmdPort);
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

			if (_settings.EnableNativeWindow) {
				var nativeWindow = NativeWindowDestinationFactory.TryCreate(display, _settings);
				if (nativeWindow != null) {
					destinations.Add(nativeWindow);
				} else if (!_nativeWindowWarningLogged) {
					Logger.Warn("[DMD] Native-window destination is enabled, but no backend is present yet.");
					_nativeWindowWarningLogged = true;
				}
			}

			if (_settings.EnableInSceneDisplay) {
				var inSceneDisplay = InSceneDmdDestination.TryCreate(display);
				if (inSceneDisplay != null) {
					destinations.Add(inSceneDisplay);
				}
			}

			return destinations;
		}

		private AbstractConverter CreateConverter()
		{
			if (!_settings.EnableColorization) {
				Logger.Info("[DMD] Colorization disabled.");
				return null;
			}

			if (string.IsNullOrWhiteSpace(_settings.AltColorPath) || string.IsNullOrWhiteSpace(_settings.RomName)) {
				Logger.Info($"[DMD] Colorization enabled, but missing altcolor path or ROM name. AltColorPath=\"{_settings.AltColorPath}\", RomName=\"{_settings.RomName}\".");
				return null;
			}

			LogColorizationSearchPath(_settings.AltColorPath, _settings.RomName);

			if (HasSerumColorization(_settings.AltColorPath, _settings.RomName)) {
				try {
					var serum = new Serum(_settings.AltColorPath, _settings.RomName, _settings.ColorizationScalerMode);
					if (serum.IsLoaded) {
						Logger.Info($"[DMD] Serum colorization loaded ({serum.ColorizationVersion}).");
						return serum;
					}

					serum.Dispose();
					Logger.Info($"[DMD] Serum files were found for \"{_settings.RomName}\", but no Serum colorization was loaded.");
				} catch (Exception exception) {
					Logger.Warn(exception, "[DMD] Could not initialize Serum colorization.");
				}
			} else {
				Logger.Info($"[DMD] No Serum files found for \"{_settings.RomName}\".");
			}

			try {
				var loader = new VniLoader(_settings.AltColorPath, _settings.RomName);
				if (!loader.FilesExist) {
					Logger.Info($"[DMD] Colorization was requested, but no Serum/VNI/PAL/PAC files were found for \"{_settings.RomName}\".");
					return null;
				}

				loader.Load(string.IsNullOrWhiteSpace(_settings.VniKey) ? null : _settings.VniKey);
				if (loader.Pal == null) {
					Logger.Info($"[DMD] VNI/PAL/PAC colorization was found for \"{_settings.RomName}\" but no palette was loaded.");
					return null;
				}

				Logger.Info($"[DMD] VNI/PAL/PAC colorization loaded for \"{_settings.RomName}\".");
				return new VniColorizer(loader.Pal, loader.Vni) {
					ScalerMode = _settings.ColorizationScalerMode
				};
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Could not initialize VNI/PAL/PAC colorization.");
				return null;
			}
		}

		private static bool HasSerumColorization(string altColorPath, string romName)
		{
			var gamePath = Path.Combine(altColorPath, romName);
			if (!Directory.Exists(gamePath)) {
				return false;
			}

			var altColorDir = new DirectoryInfo(gamePath);
			return PathUtil.GetLastCreatedFile(altColorDir, "cRZ") != null
				|| PathUtil.GetLastCreatedFile(altColorDir, "cROM") != null
				|| PathUtil.GetLastCreatedFile(altColorDir, "cROMc") != null;
		}

		private static void LogColorizationSearchPath(string altColorPath, string romName)
		{
			var gamePath = Path.Combine(altColorPath, romName);
			if (!Directory.Exists(gamePath)) {
				Logger.Info($"[DMD] Colorization folder not found: \"{gamePath}\".");
				return;
			}

			try {
				var files = Directory.GetFiles(gamePath);
				Logger.Info($"[DMD] Looking for colorization in \"{gamePath}\" ({files.Length} file(s)).");
				foreach (var file in files) {
					var extension = Path.GetExtension(file);
					if (string.Equals(extension, ".crz", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(extension, ".crom", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(extension, ".cromc", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(extension, ".pal", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(extension, ".vni", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(extension, ".pac", StringComparison.OrdinalIgnoreCase)) {
						Logger.Info($"[DMD] Colorization candidate: {Path.GetFileName(file)}");
					}
				}
			} catch (Exception exception) {
				Logger.Warn(exception, $"[DMD] Could not inspect colorization folder \"{gamePath}\".");
			}
		}

		private DmdBridgeSettings LoadSettings()
		{
			_resolvedAltColorPath = ResolveAltColorPath(_altColorPath);
			_resolvedRomName = ResolveRomName(_romName);

			var fallback = new DmdBridgeSettings {
				TargetDisplayId = _targetDisplayId,
				EnableZeDmd = _enableZeDmd,
				ZeDmdPort = _zeDmdPort,
				ZeDmdBrightness = _zeDmdBrightness,
				ZeDmdDebug = _zeDmdDebug,
				EnableNativeWindow = _enableNativeWindow,
				EnableInSceneDisplay = _enableInSceneDisplay,
				NativeWindowLeft = _nativeWindowLeft,
				NativeWindowTop = _nativeWindowTop,
				NativeWindowWidth = _nativeWindowWidth,
				NativeWindowHeight = _nativeWindowHeight,
				NativeWindowStayOnTop = _nativeWindowStayOnTop,
				DotSize = _dotSize,
				DotRounding = _dotRounding,
				DotSharpness = _dotSharpness,
				UnlitDot = _unlitDot,
				Brightness = _brightness,
				DotGlow = _dotGlow,
				BackGlow = _backGlow,
				Gamma = _gamma,
				GlassColor = _glassColor,
				GlassLighting = _glassLighting,
				EnableColorization = _enableColorization,
				AltColorPath = _resolvedAltColorPath,
				RomName = _resolvedRomName,
				ColorizationScalerMode = _colorizationScalerMode,
				VniKey = _vniKey,
				FlipHorizontally = false,
			};

			var configPath = DmdBridgeConfig.ResolveConfigPath(_dmdDeviceIniPath);
			var writeTimeUtc = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
			if (!File.Exists(configPath)) {
				if (!_missingConfigWarningLogged && !DmdBridgeConfig.IsDefaultConfigPath(_dmdDeviceIniPath)) {
					Logger.Info($"[DMD] No DmdDevice.ini found at \"{configPath}\"; using component settings.");
					_missingConfigWarningLogged = true;
				}
				_lastFallbackSettings = fallback.Clone();
				return fallback;
			}

			_missingConfigWarningLogged = false;
			if (!_reloadIniOnChange && _settings != null && fallback.Equals(_lastFallbackSettings)) {
				return _settings;
			}

			if (_settings != null
				&& string.Equals(_appliedConfigPath, configPath, StringComparison.Ordinal)
				&& _appliedConfigWriteTimeUtc == writeTimeUtc
				&& fallback.Equals(_lastFallbackSettings)) {
				return _settings;
			}

			_appliedConfigPath = configPath;
			_appliedConfigWriteTimeUtc = writeTimeUtc;
			var fallbackChanged = _lastFallbackSettings != null && !fallback.Equals(_lastFallbackSettings);
			var settings = DmdBridgeConfig.Load(configPath, fallback);
			if (fallbackChanged) {
				CopyPresentationSettings(fallback, settings);
			}
			_lastFallbackSettings = fallback.Clone();
			return settings;
		}

		private string ResolveRomName(string configuredRomName)
		{
			if (!string.IsNullOrWhiteSpace(configuredRomName)) {
				return configuredRomName.Trim();
			}

			if (TryReadPinMameRomName(_gamelogicEngine, out var romName)) {
				return romName;
			}

			if (_player == null) {
				return configuredRomName;
			}

			foreach (var component in _player.GetComponentsInChildren<Component>(true)) {
				if (TryReadPinMameRomName(component, out romName)) {
					return romName;
				}
			}

			return configuredRomName;
		}

		private static bool TryReadPinMameRomName(object instance, out string romName)
		{
			romName = null;
			if (instance == null) {
				return false;
			}

			var type = instance.GetType();
			if (type.FullName == null || type.FullName.IndexOf("PinMame", StringComparison.OrdinalIgnoreCase) < 0) {
				return false;
			}

			return TryReadStringField(instance, type, "romId", out romName)
				|| TryReadStringProperty(instance, type, "RomId", out romName)
				|| TryReadStringProperty(instance, type, "RomName", out romName)
				|| TryReadGameRomName(instance, type, out romName);
		}

		private static bool TryReadGameRomName(object instance, Type type, out string romName)
		{
			romName = null;
			var game = ReadProperty(instance, type, "Game");
			if (game == null) {
				return false;
			}

			var gameType = game.GetType();
			return TryReadStringProperty(game, gameType, "RomId", out romName)
				|| TryReadStringProperty(game, gameType, "RomName", out romName);
		}

		private static object ReadProperty(object instance, Type type, string name)
		{
			try {
				return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
			} catch {
				return null;
			}
		}

		private static bool TryReadStringProperty(object instance, Type type, string name, out string value)
		{
			value = null;
			try {
				var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property?.PropertyType != typeof(string)) {
					return false;
				}

				value = (property.GetValue(instance) as string)?.Trim();
				return !string.IsNullOrWhiteSpace(value);
			} catch {
				return false;
			}
		}

		private static bool TryReadStringField(object instance, Type type, string name, out string value)
		{
			value = null;
			try {
				var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field?.FieldType != typeof(string)) {
					return false;
				}

				value = (field.GetValue(instance) as string)?.Trim();
				return !string.IsNullOrWhiteSpace(value);
			} catch {
				return false;
			}
		}

		private string ResolveAltColorPath(string configuredAltColorPath)
		{
			if (!string.IsNullOrWhiteSpace(configuredAltColorPath)) {
				return configuredAltColorPath.Trim();
			}

			if (_defaultAltColorPathResolved) {
				return _defaultAltColorPath;
			}

			var configPath = DmdBridgeConfig.ResolveEnvConfigPath();
			if (!string.IsNullOrWhiteSpace(configPath)) {
				var altColorPath = Path.Combine(Path.GetDirectoryName(configPath), "altcolor");
				if (Directory.Exists(altColorPath)) {
					_defaultAltColorPath = altColorPath;
					_defaultAltColorPathResolved = true;
					return _defaultAltColorPath;
				}
			}

			_defaultAltColorPath = PathUtil.GetVpmFolder("altcolor", "[DMD]");
			_defaultAltColorPathResolved = true;
			return _defaultAltColorPath;
		}

		private void SyncNativeWindowLayout()
		{
			if (_pipeline == null
				|| Time.unscaledTime < _nextNativeWindowLayoutSyncTime
				|| Time.unscaledTime < _suppressNativeWindowLayoutSyncUntil) {
				return;
			}

			_nextNativeWindowLayoutSyncTime = Time.unscaledTime + 0.15f;
			if (!_pipeline.TryReadNativeWindowLayout(out var left, out var top, out var width, out var height, out var stayOnTop, out var isMovingOrSizing)
				|| isMovingOrSizing) {
				return;
			}

			if (_nativeWindowLeft == left
				&& _nativeWindowTop == top
				&& _nativeWindowWidth == width
				&& _nativeWindowHeight == height
				&& _nativeWindowStayOnTop == stayOnTop) {
				return;
			}

			_nativeWindowLeft = left;
			_nativeWindowTop = top;
			_nativeWindowWidth = width;
			_nativeWindowHeight = height;
			_nativeWindowStayOnTop = stayOnTop;
			CopyPresentationSettingsTo(_settings);
			CopyPresentationSettingsTo(_appliedSettings);
			CopyPresentationSettingsTo(_lastFallbackSettings);
		}

		private void CopyPresentationSettingsTo(DmdBridgeSettings settings)
		{
			if (settings == null) {
				return;
			}

			settings.NativeWindowLeft = _nativeWindowLeft;
			settings.NativeWindowTop = _nativeWindowTop;
			settings.NativeWindowWidth = _nativeWindowWidth;
			settings.NativeWindowHeight = _nativeWindowHeight;
			settings.NativeWindowStayOnTop = _nativeWindowStayOnTop;
		}

		private static void CopyPresentationSettings(DmdBridgeSettings source, DmdBridgeSettings target)
		{
			target.NativeWindowLeft = source.NativeWindowLeft;
			target.NativeWindowTop = source.NativeWindowTop;
			target.NativeWindowWidth = source.NativeWindowWidth;
			target.NativeWindowHeight = source.NativeWindowHeight;
			target.NativeWindowStayOnTop = source.NativeWindowStayOnTop;
			target.DotSize = source.DotSize;
			target.DotRounding = source.DotRounding;
			target.DotSharpness = source.DotSharpness;
			target.UnlitDot = source.UnlitDot;
			target.Brightness = source.Brightness;
			target.DotGlow = source.DotGlow;
			target.BackGlow = source.BackGlow;
			target.Gamma = source.Gamma;
			target.GlassColor = source.GlassColor;
			target.GlassLighting = source.GlassLighting;
		}
	}

	internal sealed class DmdBridgeSettings : IEquatable<DmdBridgeSettings>
	{
		public string TargetDisplayId;
		public bool EnableZeDmd;
		public string ZeDmdPort;
		public int ZeDmdBrightness;
		public bool ZeDmdDebug;
		public bool EnableNativeWindow;
		public bool EnableInSceneDisplay;
		public int NativeWindowLeft;
		public int NativeWindowTop;
		public int NativeWindowWidth;
		public int NativeWindowHeight;
		public bool NativeWindowStayOnTop;
		public float DotSize;
		public float DotRounding;
		public float DotSharpness;
		public Color UnlitDot;
		public float Brightness;
		public float DotGlow;
		public float BackGlow;
		public float Gamma;
		public Color GlassColor;
		public float GlassLighting;
		public bool EnableColorization;
		public string AltColorPath;
		public string RomName;
		public ScalerMode ColorizationScalerMode;
		public string VniKey;
		public bool FlipHorizontally;

		public DmdBridgeSettings Clone()
		{
			return (DmdBridgeSettings)MemberwiseClone();
		}

		public bool Equals(DmdBridgeSettings other)
		{
			return other != null
				&& string.Equals(TargetDisplayId, other.TargetDisplayId, StringComparison.Ordinal)
				&& EnableZeDmd == other.EnableZeDmd
				&& string.Equals(ZeDmdPort, other.ZeDmdPort, StringComparison.Ordinal)
				&& ZeDmdBrightness == other.ZeDmdBrightness
				&& ZeDmdDebug == other.ZeDmdDebug
				&& EnableNativeWindow == other.EnableNativeWindow
				&& EnableInSceneDisplay == other.EnableInSceneDisplay
				&& NativeWindowLeft == other.NativeWindowLeft
				&& NativeWindowTop == other.NativeWindowTop
				&& NativeWindowWidth == other.NativeWindowWidth
				&& NativeWindowHeight == other.NativeWindowHeight
				&& NativeWindowStayOnTop == other.NativeWindowStayOnTop
				&& DotSize.Equals(other.DotSize)
				&& DotRounding.Equals(other.DotRounding)
				&& DotSharpness.Equals(other.DotSharpness)
				&& UnlitDot.Equals(other.UnlitDot)
				&& Brightness.Equals(other.Brightness)
				&& DotGlow.Equals(other.DotGlow)
				&& BackGlow.Equals(other.BackGlow)
				&& Gamma.Equals(other.Gamma)
				&& GlassColor.Equals(other.GlassColor)
				&& GlassLighting.Equals(other.GlassLighting)
				&& EnableColorization == other.EnableColorization
				&& string.Equals(AltColorPath, other.AltColorPath, StringComparison.Ordinal)
				&& string.Equals(RomName, other.RomName, StringComparison.Ordinal)
				&& ColorizationScalerMode == other.ColorizationScalerMode
				&& string.Equals(VniKey, other.VniKey, StringComparison.Ordinal)
				&& FlipHorizontally == other.FlipHorizontally;
		}

		public bool TopologyEquals(DmdBridgeSettings other)
		{
			return other != null
				&& string.Equals(TargetDisplayId, other.TargetDisplayId, StringComparison.Ordinal)
				&& EnableZeDmd == other.EnableZeDmd
				&& string.Equals(ZeDmdPort, other.ZeDmdPort, StringComparison.Ordinal)
				&& ZeDmdBrightness == other.ZeDmdBrightness
				&& ZeDmdDebug == other.ZeDmdDebug
				&& EnableNativeWindow == other.EnableNativeWindow
				&& EnableInSceneDisplay == other.EnableInSceneDisplay
				&& EnableColorization == other.EnableColorization
				&& string.Equals(AltColorPath, other.AltColorPath, StringComparison.Ordinal)
				&& string.Equals(RomName, other.RomName, StringComparison.Ordinal)
				&& ColorizationScalerMode == other.ColorizationScalerMode
				&& string.Equals(VniKey, other.VniKey, StringComparison.Ordinal)
				&& FlipHorizontally == other.FlipHorizontally;
		}
	}

	internal static class DmdBridgeConfig
	{
		private const string EnvConfig = "DMDDEVICE_CONFIG";
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static DmdBridgeSettings Load(string path, DmdBridgeSettings fallback)
		{
			var resolvedPath = ResolveConfigPath(path);
			if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)) {
				Logger.Warn($"[DMD] No DmdDevice.ini found at \"{resolvedPath}\"; using component settings.");
				return fallback.Clone();
			}

			try {
				var ini = Parse(resolvedPath);
				var settings = fallback.Clone();
				ApplyGlobal(ini, settings);
				ApplyVirtualDmd(ini, settings);
				ApplyZeDmd(ini, settings);
				ApplyGame(ini, settings, Path.GetDirectoryName(resolvedPath));
				Logger.Info($"[DMD] Loaded DmdDevice.ini from \"{resolvedPath}\".");
				return settings;
			} catch (Exception exception) {
				Logger.Warn(exception, $"[DMD] Could not load DmdDevice.ini at \"{resolvedPath}\"; using component settings.");
				return fallback.Clone();
			}
		}

		public static string ResolveConfigPath(string path)
		{
			var envPath = ResolveEnvConfigPath();
			if (!string.IsNullOrWhiteSpace(envPath)) {
				return envPath;
			}

			if (string.IsNullOrWhiteSpace(path)) {
				path = "DmdDevice.ini";
			}

			return Path.IsPathRooted(path) ? path : Path.Combine(Application.persistentDataPath, path);
		}

		public static bool IsDefaultConfigPath(string path)
		{
			return string.IsNullOrWhiteSpace(path)
				|| string.Equals(path.Trim(), "DmdDevice.ini", StringComparison.OrdinalIgnoreCase);
		}

		public static string ResolveEnvConfigPath()
		{
			var envValue = Environment.GetEnvironmentVariable(EnvConfig);
			if (string.IsNullOrWhiteSpace(envValue)) {
				return null;
			}

			foreach (var rawPath in envValue.Split(';')) {
				var path = Unquote(rawPath);
				if (File.Exists(path)) {
					return path;
				}
			}

			return null;
		}

		private static Dictionary<string, Dictionary<string, string>> Parse(string path)
		{
			var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
			var section = string.Empty;
			ini[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var rawLine in File.ReadAllLines(path)) {
				var line = StripComment(rawLine).Trim();
				if (line.Length == 0) {
					continue;
				}

				if (line[0] == '[' && line[line.Length - 1] == ']') {
					section = line.Substring(1, line.Length - 2).Trim();
					if (!ini.ContainsKey(section)) {
						ini[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					}
					continue;
				}

				var separator = line.IndexOf('=');
				if (separator <= 0) {
					continue;
				}

				ini[section][line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
			}

			return ini;
		}

		private static string StripComment(string line)
		{
			var quoted = false;
			for (var i = 0; i < line.Length; i++) {
				if (line[i] == '"') {
					quoted = !quoted;
				} else if (!quoted && (line[i] == ';' || line[i] == '#')) {
					return line.Substring(0, i);
				}
			}

			return line;
		}

		private static void ApplyGlobal(Dictionary<string, Dictionary<string, string>> ini, DmdBridgeSettings settings)
		{
			if (!ini.TryGetValue("global", out var global)) {
				return;
			}

			settings.FlipHorizontally = GetBool(global, "fliphorizontally", settings.FlipHorizontally);
			settings.EnableColorization = GetBool(global, "colorize", settings.EnableColorization);
			settings.ColorizationScalerMode = GetEnum(global, "vni.scalermode", settings.ColorizationScalerMode);
			settings.VniKey = GetString(global, "vni.key", settings.VniKey);
		}

		private static void ApplyVirtualDmd(Dictionary<string, Dictionary<string, string>> ini, DmdBridgeSettings settings)
		{
			if (!ini.TryGetValue("virtualdmd", out var virtualDmd)) {
				return;
			}

			settings.EnableNativeWindow = GetBool(virtualDmd, "enabled", settings.EnableNativeWindow);
			settings.NativeWindowLeft = GetInt(virtualDmd, "left", settings.NativeWindowLeft);
			settings.NativeWindowTop = GetInt(virtualDmd, "top", settings.NativeWindowTop);
			settings.NativeWindowWidth = GetInt(virtualDmd, "width", settings.NativeWindowWidth);
			settings.NativeWindowHeight = GetInt(virtualDmd, "height", settings.NativeWindowHeight);
			settings.NativeWindowStayOnTop = GetBool(virtualDmd, "stayontop", settings.NativeWindowStayOnTop);

			var styleName = GetString(virtualDmd, "style", "default");
			var stylePrefix = $"style.{styleName}.";
			settings.DotSize = GetFloat(virtualDmd, stylePrefix + "dotsize", GetFloat(virtualDmd, "dotsize", settings.DotSize));
			settings.DotRounding = GetFloat(virtualDmd, stylePrefix + "dotrounding", GetFloat(virtualDmd, "dotrounding", settings.DotRounding));
			settings.DotSharpness = GetFloat(virtualDmd, stylePrefix + "dotsharpness", GetFloat(virtualDmd, "dotsharpness", settings.DotSharpness));
			settings.UnlitDot = GetColor(virtualDmd, stylePrefix + "unlitdot", GetColor(virtualDmd, "unlitdot", settings.UnlitDot));
			settings.Brightness = GetFloat(virtualDmd, stylePrefix + "brightness", GetFloat(virtualDmd, "brightness", settings.Brightness));
			settings.DotGlow = GetFloat(virtualDmd, stylePrefix + "dotglow", GetFloat(virtualDmd, "dotglow", settings.DotGlow));
			settings.BackGlow = GetFloat(virtualDmd, stylePrefix + "backglow", GetFloat(virtualDmd, "backglow", settings.BackGlow));
			settings.Gamma = GetFloat(virtualDmd, stylePrefix + "gamma", GetFloat(virtualDmd, "gamma", settings.Gamma));
			settings.GlassColor = GetColor(virtualDmd, stylePrefix + "glass", GetColor(virtualDmd, "glass", settings.GlassColor));
			settings.GlassLighting = GetFloat(virtualDmd, stylePrefix + "glasslighting", GetFloat(virtualDmd, "glasslighting", settings.GlassLighting));
		}

		private static void ApplyZeDmd(Dictionary<string, Dictionary<string, string>> ini, DmdBridgeSettings settings)
		{
			if (!ini.TryGetValue("zedmd", out var zeDmd)) {
				return;
			}

			settings.EnableZeDmd = GetBool(zeDmd, "enabled", settings.EnableZeDmd);
			settings.ZeDmdPort = GetString(zeDmd, "port", settings.ZeDmdPort);
			settings.ZeDmdBrightness = GetInt(zeDmd, "brightness", settings.ZeDmdBrightness);
			settings.ZeDmdDebug = GetBool(zeDmd, "debug", settings.ZeDmdDebug);
		}

		private static void ApplyGame(Dictionary<string, Dictionary<string, string>> ini, DmdBridgeSettings settings, string configDirectory)
		{
			if (string.IsNullOrWhiteSpace(settings.RomName) || !ini.TryGetValue(settings.RomName, out var game)) {
				return;
			}

			settings.EnableColorization = GetBool(game, "colorize", settings.EnableColorization);
			var altColorPath = GetString(game, "altcolor", null);
			if (!string.IsNullOrWhiteSpace(altColorPath)) {
				settings.AltColorPath = Path.IsPathRooted(altColorPath)
					? altColorPath
					: Path.GetFullPath(Path.Combine(configDirectory, altColorPath));
			}
		}

		private static string GetString(Dictionary<string, string> section, string key, string fallback)
		{
			return section.TryGetValue(key, out var value) ? Unquote(value) : fallback;
		}

		private static int GetInt(Dictionary<string, string> section, string key, int fallback)
		{
			if (!section.TryGetValue(key, out var value)) {
				return fallback;
			}

			return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
		}

		private static bool GetBool(Dictionary<string, string> section, string key, bool fallback)
		{
			if (!section.TryGetValue(key, out var value)) {
				return fallback;
			}

			switch (Unquote(value).Trim().ToLowerInvariant()) {
				case "1":
				case "yes":
				case "true":
				case "on":
					return true;
				case "0":
				case "no":
				case "false":
				case "off":
					return false;
				default:
					return fallback;
			}
		}

		private static float GetFloat(Dictionary<string, string> section, string key, float fallback)
		{
			if (!section.TryGetValue(key, out var value)) {
				return fallback;
			}

			return float.TryParse(Unquote(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
		}

		private static ScalerMode GetEnum(Dictionary<string, string> section, string key, ScalerMode fallback)
		{
			if (!section.TryGetValue(key, out var value)) {
				return fallback;
			}

			return Enum.TryParse(Unquote(value), true, out ScalerMode parsed) ? parsed : fallback;
		}

		private static Color GetColor(Dictionary<string, string> section, string key, Color fallback)
		{
			if (!section.TryGetValue(key, out var value)) {
				return fallback;
			}

			value = Unquote(value);
			if (string.IsNullOrWhiteSpace(value)) {
				return fallback;
			}

			if (ColorUtility.TryParseHtmlString(value, out var color)) {
				return color;
			}

			var parts = value.Split(',');
			if (parts.Length < 3) {
				return fallback;
			}

			if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
				|| !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
				|| !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) {
				return fallback;
			}

			if (r > 1f || g > 1f || b > 1f) {
				r /= 255f;
				g /= 255f;
				b /= 255f;
			}

			return new Color(r, g, b);
		}

		private static string Unquote(string value)
		{
			value = value?.Trim();
			if (value != null && value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') {
				return value.Substring(1, value.Length - 2);
			}

			return value;
		}
	}
}
