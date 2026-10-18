// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using LibDmd.Converter;
using LibDmd.Common;
using LibDmd.Output.Pixelcade;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NLog;
using UnityEngine;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed class DmdPluginFactory : IPlayerPluginFactory, IPluginSettingsProvider
	{
		internal const string Id = "org.visualpinball.dmd";
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		public PluginDescriptor Descriptor { get; } = new PluginDescriptor(Id, "DMD", "Colorizations, your in-scene DMD and native displays, with optional cabinet hardware.") { EnabledByDefault = true, Category = "Displays", Order = 20 };
		internal static readonly PluginSettingsSchema SettingsSchema = BuildSchema();
		public PluginSettingsSchema Schema => SettingsSchema;
		public IPluginSettingsProvider SettingsProvider => this;
		public IPlayerPlugin Create(PluginApplicationContext context) => new DmdPlugin(context);
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register() => PlayerPluginRegistry.Register(new DmdPluginFactory());
		private static PluginField Field(string id, string label, SettingKind kind, JToken value, string provider = "", double? minimum = null, double? maximum = null)
			=> new PluginField(id, label, kind) { Default = value, Provider = provider, Scope = ConfigurationScope.GlobalAndGame, Minimum = minimum, Maximum = maximum };
		private static PluginField Hardware(string id, string label, SettingKind kind, JToken value, string provider = "", double? minimum = null, double? maximum = null)
		{
			var field = Field(id, label, kind, value, provider, minimum, maximum); field.Scope = ConfigurationScope.GlobalOnly; return field;
		}
		private static PluginField Coordinate(string id, string label, string row, int value, double? minimum = null, double? maximum = null)
		{
			var field = Field(id, label, SettingKind.Integer, value, minimum: minimum, maximum: maximum); field.Row = row; return field;
		}
		private static PluginSettingsSchema BuildSchema() => new PluginSettingsSchema(1,
			new PluginSettingsGroup("Colorizations", "Choose an available version for each game. The original library stays unchanged.",
				new PluginField("AltColorRoot", "Colorization library", SettingKind.Folder) { Default = "", Scope = ConfigurationScope.GlobalAndGame, FullWidth = true, Description = "The altcolor folder containing one folder per ROM. Leave empty to use the altcolor folder next to DmdDevice.ini or VPinMAME's. Games can override the global library." },
				Field("Colorization", "Colorization", SettingKind.Choice, "auto", "colorizations"),
				Field("ColorizationScalerMode", "Scaler", SettingKind.Choice, "None", "scalers"),
				new PluginField("VniKey", "PAC key", SettingKind.Text) { Default = "", FullWidth = true, Description = "Leave empty to use vni.key from DmdDevice.ini." }),
			new PluginSettingsGroup("Display outputs", "Native placement uses desktop pixels and allows negative coordinates on any monitor.",
				new PluginField("Source", "DMD source", SettingKind.Choice) { Default = "auto", Scope = ConfigurationScope.GameOnly, Provider = "sources" },
				Field("EnableInSceneDisplay", "Use the table's DMD", SettingKind.Boolean, true),
				Field("EnableNativeWindow", "Native borderless window", SettingKind.Boolean, false),
				Coordinate("NativeWindowLeft", "Left", "native-position", 100), Coordinate("NativeWindowTop", "Top", "native-position", 100),
				Coordinate("NativeWindowWidth", "Width", "native-size", 512, minimum: 1, maximum: 16384), Coordinate("NativeWindowHeight", "Height", "native-size", 128, minimum: 1, maximum: 16384),
				Field("NativeWindowStayOnTop", "Keep on top", SettingKind.Boolean, false), Field("FlipHorizontally", "Mirror horizontally", SettingKind.Boolean, false)),
			new PluginSettingsGroup("Cabinet hardware", "Outputs are acquired exclusively. A disabled plugin opens no device.",
				Hardware("EnableZeDmd", "ZeDMD", SettingKind.Boolean, false), Hardware("ZeDmdPort", "ZeDMD port", SettingKind.Text, ""), Hardware("ZeDmdBrightness", "ZeDMD brightness", SettingKind.Integer, 8, minimum: 0, maximum: 15), Hardware("ZeDmdDebug", "ZeDMD diagnostics", SettingKind.Boolean, false),
				Hardware("EnablePinDmd3", "PinDMD 3", SettingKind.Boolean, false), Hardware("PinDmd3Port", "PinDMD 3 port", SettingKind.Text, ""),
				Hardware("EnablePixelcade", "Pixelcade", SettingKind.Boolean, false), Hardware("PixelcadePort", "Pixelcade port", SettingKind.Text, ""), Hardware("PixelcadeColorMatrix", "Pixelcade color order", SettingKind.Choice, "Rgb", "matrices"),
				Hardware("EnablePin2Dmd", "PIN2DMD", SettingKind.Boolean, false), Hardware("Pin2DmdOutputDelay", "PIN2DMD delay (ms)", SettingKind.Integer, 25, minimum: 0, maximum: 1000),
				Hardware("EnablePinDmd1", "PinDMD 1", SettingKind.Boolean, false), Hardware("EnablePinDmd2", "PinDMD 2", SettingKind.Boolean, false)),
			new PluginSettingsGroup("Appearance", "Presentation settings apply to native DMD windows.",
				Field("DotSize", "Dot size", SettingKind.Number, 0.92, minimum: 0.05, maximum: 1.5), Field("DotRounding", "Rounding", SettingKind.Number, 1, minimum: 0, maximum: 1), Field("DotSharpness", "Sharpness", SettingKind.Number, 0.8, minimum: 0, maximum: 1),
				Field("Brightness", "Brightness", SettingKind.Number, 0.95, minimum: 0, maximum: 4), Field("DotGlow", "Dot glow", SettingKind.Number, 0, minimum: 0, maximum: 4), Field("BackGlow", "Background glow", SettingKind.Number, 0, minimum: 0, maximum: 4), Field("Gamma", "Gamma", SettingKind.Number, 1, minimum: 0.1, maximum: 4),
				Field("UnlitDot", "Unlit dot color", SettingKind.Color, "#000000"), Field("GlassColor", "Glass color", SettingKind.Color, "#000000"), Field("GlassLighting", "Glass lighting", SettingKind.Number, 0, minimum: 0, maximum: 4)),
			new PluginSettingsGroup("Import existing settings", "DmdDevice.ini from VPinMAME's folder applies automatically to everything not set here. Import copies an INI into the draft to review and Apply. The INI is never rewritten.",
				new PluginField("ImportPath", "DmdDevice.ini", SettingKind.File) { Default = "", FullWidth = true }, new PluginField("Import", "Import INI", SettingKind.Action) { Provider = "import-ini" }));
		internal static DmdBridgeSettings Settings(PluginSettingsSnapshot snapshot)
		{
			var values = (JObject)snapshot.Values.DeepClone(); values.Remove("UnlitDot"); values.Remove("GlassColor");
			var settings = values.ToObject<DmdBridgeSettings>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None }));
			ColorUtility.TryParseHtmlString(snapshot.String("UnlitDot", "#000000"), out settings.UnlitDot);
			ColorUtility.TryParseHtmlString(snapshot.String("GlassColor", "#000000"), out settings.GlassColor);
			return settings;
		}
		public Task<PluginChoiceCatalog> DiscoverAsync(string provider, PluginChoiceRequest request, CancellationToken cancellation)
			=> Task.Run(() => Discover(provider, request, cancellation), cancellation);
		private PluginChoiceCatalog Discover(string provider, PluginChoiceRequest request, CancellationToken cancellation)
		{
			cancellation.ThrowIfCancellationRequested();
			if (provider == "sources") return PluginResourceChoices.Displays(request, DisplayEncoding.Gray2 | DisplayEncoding.Gray4 | DisplayEncoding.Luminance8 | DisplayEncoding.Rgb24);
			var choices = new List<PluginChoice>();
			switch (provider) {
				case "colorizations":
					choices.Add(new PluginChoice("auto", "Automatic", "Uses the sole available colorization, or what DmdDevice loads from the game folder: Serum first, then the newest .pac, which needs a PAC key (without one, no colorization), then pin2dmd.pal and .vni. Otherwise select one.")); choices.Add(new PluginChoice("none", "No colorization"));
					foreach (var asset in ColorizationCatalog.Read(DmdPluginFactory.ColorizationLibrary(request.Settings.String("AltColorRoot")), request.Game?.Key ?? "")) choices.Add(new PluginChoice(asset.Id, asset.Label, available: !asset.RequiresKey || PacKey(request.Settings).Length > 0, unavailableReason: "A PAC key is required."));
					break;
				case "scalers": foreach (string name in Enum.GetNames(typeof(ScalerMode))) choices.Add(new PluginChoice(name, name)); break;
				case "matrices": foreach (string name in Enum.GetNames(typeof(ColorMatrix))) choices.Add(new PluginChoice(name, name)); break;
			}
			return new PluginChoiceCatalog(string.Join("|", choices.Select(choice => choice.Id)), choices);
		}
		/// <summary>The colorization library to use: the configured folder, or the first default altcolor folder that exists.</summary>
		internal static string ColorizationLibrary(string configured)
		{
			if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
			foreach (string candidate in DefaultColorizationLibraries()) {
				try { if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) return candidate; } catch (Exception error) { Logger.Debug(error, "[DMD] Skipping colorization library candidate {Path:l}.", candidate); }
			}
			return "";
		}
		/// <summary>
		/// Where DmdDevice and the standalone DMD bridge look: next to DmdDevice.ini, in VPinMAME's folder (VPM_HOME, its COM registration,
		/// or next to a ROM folder, since VPinMAME keeps roms and altcolor side by side), then where LibDmd finds it.
		/// </summary>
		private static IEnumerable<string> DefaultColorizationLibraries()
		{
			string config = null;
			try { config = DmdBridgeConfig.ResolveEnvConfigPath(); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not resolve DMDDEVICE_CONFIG."); }
			if (!string.IsNullOrWhiteSpace(config)) yield return Path.Combine(Path.GetDirectoryName(config) ?? "", "altcolor");
			foreach (string folder in VpinMameFolders()) yield return Path.Combine(folder, "altcolor");
			string libDmd = null;
			try { libDmd = PathUtil.GetVpmFolder("altcolor", "[DMD]"); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not locate VPinMAME's altcolor folder."); }
			yield return libDmd;
		}
		public IReadOnlyList<PluginValidationIssue> Validate(PluginChoiceRequest request)
		{
			var issues = new List<PluginValidationIssue>();
			foreach (string field in new[] { "UnlitDot", "GlassColor" }) if (!ColorUtility.TryParseHtmlString(request.Settings.String(field), out _)) issues.Add(new PluginValidationIssue(field, "Enter a color such as #FFAA00."));
			if (request.Game.HasValue) try {
				bool hasKey = PacKey(request.Settings).Length > 0;
				var asset = ColorizationCatalog.Select(DmdPluginFactory.ColorizationLibrary(request.Settings.String("AltColorRoot")), request.Game.Value.Key, request.Settings.String("Colorization", "auto"), hasKey);
				if (asset?.RequiresKey == true && !hasKey) issues.Add(new PluginValidationIssue("VniKey", "A PAC key is required."));
			} catch (Exception error) { issues.Add(new PluginValidationIssue("Colorization", error.Message)); }
			return issues;
		}
		public Task<PluginActionResult> RunActionAsync(string action, PluginChoiceRequest request, CancellationToken cancellation)
		{
			if (action != "import-ini") return Task.FromResult(new PluginActionResult(false, "Unknown DMD action."));
			string path = request.Settings.String("ImportPath");
			if (!File.Exists(path)) return Task.FromResult(new PluginActionResult(false, "Choose an existing DmdDevice.ini."));
			// only what the ini carries, over the current values, so the source and library the user chose stay
			var values = IniValues(path, request.Settings, request.Game?.Key);
			return Task.FromResult(new PluginActionResult(true, "Imported into the draft. Review the values and Apply to save.", new PluginSettingsSnapshot(1, values)));
		}

		/// <summary>
		/// The settings a DmdDevice.ini carries, as field values: the ini applied over <paramref name="current"/>, with DmdDevice's
		/// own default (colorization on) where the ini says nothing. Only schema fields; the import and source fields are left out.
		/// </summary>
		internal static JObject IniValues(string path, PluginSettingsSnapshot current, string romName)
		{
			var fallback = Settings(current); fallback.RomName = romName; fallback.EnableColorization = true; fallback.AltColorPath = null;
			var settings = DmdBridgeConfig.Load(path, fallback, true);
			var values = new JObject();
			foreach (var field in SettingsSchema.Fields) {
				if (field.Id == "ImportPath" || field.Id == "Import" || field.Id == "Source") continue;
				var member = typeof(DmdBridgeSettings).GetField(field.Id, BindingFlags.Public | BindingFlags.Instance);
				if (member == null) continue;
				object value = member.GetValue(settings);
				values[field.Id] = value switch {
					Color color => "#" + ColorUtility.ToHtmlStringRGB(color),
					Enum choice => choice.ToString(),
					// floats as their shortest decimal, so 0.92 stays 0.92 rather than 0.9200000166893005
					float number => double.Parse(number.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
					null => "",
					_ => JToken.FromObject(value),
				};
			}
			if (!string.IsNullOrWhiteSpace(settings.AltColorPath)) values["AltColorRoot"] = settings.AltColorPath;
			if (!settings.EnableColorization) values["Colorization"] = "none";
			return values;
		}

		/// <summary>
		/// The settings a game runs with: DmdDevice.ini, when there is one, replaces every value the user hasn't saved in the global
		/// settings or the game's profile, so the ini sits between the defaults and the user's own settings.
		/// </summary>
		/// <param name="effective">The resolved settings the host passed.</param>
		/// <param name="saved">The fields stored in the global or the game layer, which keep their values.</param>
		internal static PluginSettingsSnapshot WithDmdDeviceIni(PluginSettingsSnapshot effective, ISet<string> saved, string romName, out string iniPath, out int imported)
		{
			iniPath = DmdDeviceIni(); imported = 0;
			if (iniPath == null) return effective;
			var result = effective.Copy();
			foreach (var value in IniValues(iniPath, effective, romName).Properties()) {
				// an empty PAC key means "use the ini's", as the field says, also when it was saved empty
				bool keptByUser = saved.Contains(value.Name) && !(value.Name == "VniKey" && effective.String("VniKey").Trim().Length == 0);
				if (keptByUser || Same(result.Values[value.Name], value.Value)) continue;
				result.Values[value.Name] = value.Value; imported++;
			}
			return result;
		}

		// 1 and 1.0 are the same setting, though not the same token
		private static bool Same(JToken current, JToken imported) => JToken.DeepEquals(current, imported)
			|| current is JValue a && imported is JValue b && (a.Type == JTokenType.Integer || a.Type == JTokenType.Float) && (b.Type == JTokenType.Integer || b.Type == JTokenType.Float)
				&& a.Value<double>() == b.Value<double>();

		/// <summary>The PAC key: the configured one, or vni.key from DmdDevice.ini.</summary>
		internal static string PacKey(PluginSettingsSnapshot settings)
		{
			string key = settings.String("VniKey").Trim();
			if (key.Length > 0) return key;
			string ini = DmdDeviceIni();
			return ini == null ? "" : (DmdBridgeConfig.ReadValue(ini, "global", "vni.key") ?? "").Trim();
		}

		/// <summary>
		/// The DmdDevice.ini that DmdDevice would use: DMDDEVICE_CONFIG, then the one in VPinMAME's folder, where DmdDevice.dll and its
		/// ini live (see <see cref="VpinMameFolders"/>). Null if there is none.
		/// </summary>
		internal static string DmdDeviceIni()
		{
			string config = null;
			try { config = DmdBridgeConfig.ResolveEnvConfigPath(); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not resolve DMDDEVICE_CONFIG."); }
			if (!string.IsNullOrWhiteSpace(config)) return config;
			foreach (string folder in VpinMameFolders()) {
				try { string ini = Path.Combine(folder, "DmdDevice.ini"); if (File.Exists(ini)) return ini; } catch (Exception error) { Logger.Debug(error, "[DMD] Skipping DmdDevice.ini candidate in {Path:l}.", folder); }
			}
			return null;
		}

		/// <summary>VPinMAME's folder candidates: VPM_HOME, the folder of VPinMAME's COM registration, and the parent of each ROM folder.</summary>
		private static IEnumerable<string> VpinMameFolders()
		{
			string home = Environment.GetEnvironmentVariable("VPM_HOME");
			if (!string.IsNullOrWhiteSpace(home)) yield return home.Trim().Trim('"');
			string registered = null;
			try { registered = RegisteredVpinMameFolder(); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not read VPinMAME's COM registration."); }
			if (!string.IsNullOrWhiteSpace(registered)) yield return registered;
			foreach (string roms in (VisualPinball.Unity.GamelogicPaths.RomFolder ?? "").Split(';')) {
				if (string.IsNullOrWhiteSpace(roms)) continue;
				string parent = null;
				try { parent = Path.GetDirectoryName(Path.GetFullPath(roms.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); } catch (Exception error) { Logger.Debug(error, "[DMD] Skipping ROM folder {Path:l}.", roms); }
				if (parent != null) yield return parent;
			}
		}

		/// <summary>The folder of the VPinMAME DLL registered for VPinMAME.Controller, where LibDmd finds it on Windows.</summary>
		private static string RegisteredVpinMameFolder()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
			string clsid = ReadClassesRoot(@"VPinMAME.Controller\CLSID");
			if (string.IsNullOrWhiteSpace(clsid)) return null;
			// the ProgID is shared by both registry views, the CLSID isn't: a 32-bit-only VPinMAME is under WOW6432Node
			string dll = ReadClassesRoot(@"CLSID\" + clsid.Trim() + @"\InprocServer32") ?? ReadClassesRoot(@"WOW6432Node\CLSID\" + clsid.Trim() + @"\InprocServer32");
			return string.IsNullOrWhiteSpace(dll) ? null : Path.GetDirectoryName(dll.Trim().Trim('"'));
		}

		private static string ReadClassesRoot(string subKey)
		{
			var classesRoot = new IntPtr(unchecked((int)0x80000000));
			uint size = 0;
			if (RegGetValueW(classesRoot, subKey, null, RrfRtRegSz, out _, null, ref size) != 0 || size == 0) return null;
			var buffer = new StringBuilder((int)(size / 2) + 1);
			return RegGetValueW(classesRoot, subKey, null, RrfRtRegSz, out _, buffer, ref size) == 0 ? buffer.ToString() : null;
		}

		private const uint RrfRtRegSz = 0x00000002;
		[DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
		private static extern int RegGetValueW(IntPtr key, string subKey, string value, uint flags, out uint type, StringBuilder data, ref uint size);
	}
}
