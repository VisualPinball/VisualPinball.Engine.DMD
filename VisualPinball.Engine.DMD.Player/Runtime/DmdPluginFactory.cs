// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// See LICENSE for the terms of use and distribution.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibDmd.Converter;
using LibDmd.Common;
using LibDmd.Output.Pixelcade;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEngine;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed class DmdPluginFactory : IPlayerPluginFactory, IPluginSettingsProvider
	{
		internal const string Id = "org.visualpinball.dmd";
		public PluginDescriptor Descriptor { get; } = new PluginDescriptor(Id, "DMD", "Colorizations, your in-scene DMD and native displays, with optional cabinet hardware.") { EnabledByDefault = true, Category = "Displays", Order = 20 };
		public PluginSettingsSchema Schema { get; } = BuildSchema();
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
		private static PluginSettingsSchema BuildSchema() => new PluginSettingsSchema(1,
			new PluginSettingsGroup("Colorizations", "Choose an available version for each game. The original library stays unchanged.",
				new PluginField("AltColorRoot", "Colorization library", SettingKind.Folder) { Default = "", Description = "The altcolor folder containing one folder per ROM." },
				Field("Colorization", "Colorization", SettingKind.Choice, "auto", "colorizations"),
				Field("ColorizationScalerMode", "Scaler", SettingKind.Choice, "None", "scalers"),
				new PluginField("VniKey", "PAC key", SettingKind.Text) { Default = "" }),
			new PluginSettingsGroup("Display outputs", "Native placement uses desktop pixels and allows negative coordinates on any monitor.",
				new PluginField("Source", "DMD source", SettingKind.Choice) { Default = "auto", Scope = ConfigurationScope.GameOnly, Provider = "sources" },
				Field("EnableInSceneDisplay", "Use the table's DMD", SettingKind.Boolean, true),
				Field("EnableNativeWindow", "Native borderless window", SettingKind.Boolean, false),
				Field("NativeWindowLeft", "Left", SettingKind.Integer, 100), Field("NativeWindowTop", "Top", SettingKind.Integer, 100),
				Field("NativeWindowWidth", "Width", SettingKind.Integer, 512, minimum: 1, maximum: 16384), Field("NativeWindowHeight", "Height", SettingKind.Integer, 128, minimum: 1, maximum: 16384),
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
			new PluginSettingsGroup("Import existing settings", "Import once into the draft, review it, then Apply. The INI is never rewritten.",
				new PluginField("ImportPath", "DmdDevice.ini", SettingKind.File) { Default = "" }, new PluginField("Import", "Import INI", SettingKind.Action) { Provider = "import-ini" }));
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
					choices.Add(new PluginChoice("auto", "Automatic", "Uses the sole available colorization; multiple versions require a selection.")); choices.Add(new PluginChoice("none", "No colorization"));
					foreach (var asset in ColorizationCatalog.Read(request.Settings.String("AltColorRoot"), request.Game?.Key ?? "")) choices.Add(new PluginChoice(asset.Id, asset.Label, available: !asset.RequiresKey || request.Settings.String("VniKey").Length > 0, unavailableReason: "A PAC key is required."));
					break;
				case "scalers": foreach (string name in Enum.GetNames(typeof(ScalerMode))) choices.Add(new PluginChoice(name, name)); break;
				case "matrices": foreach (string name in Enum.GetNames(typeof(ColorMatrix))) choices.Add(new PluginChoice(name, name)); break;
			}
			return new PluginChoiceCatalog(string.Join("|", choices.Select(choice => choice.Id)), choices);
		}
		public IReadOnlyList<PluginValidationIssue> Validate(PluginChoiceRequest request)
		{
			var issues = new List<PluginValidationIssue>();
			foreach (string field in new[] { "UnlitDot", "GlassColor" }) if (!ColorUtility.TryParseHtmlString(request.Settings.String(field), out _)) issues.Add(new PluginValidationIssue(field, "Enter a color such as #FFAA00."));
			if (request.Game.HasValue) try {
				var asset = ColorizationCatalog.Select(request.Settings.String("AltColorRoot"), request.Game.Value.Key, request.Settings.String("Colorization", "auto"));
				if (asset?.RequiresKey == true && request.Settings.String("VniKey").Length == 0) issues.Add(new PluginValidationIssue("VniKey", "A PAC key is required."));
			} catch (Exception error) { issues.Add(new PluginValidationIssue("Colorization", error.Message)); }
			return issues;
		}
		public Task<PluginActionResult> RunActionAsync(string action, PluginChoiceRequest request, CancellationToken cancellation)
		{
			if (action != "import-ini") return Task.FromResult(new PluginActionResult(false, "Unknown DMD action."));
			string path = request.Settings.String("ImportPath");
			if (!File.Exists(path)) return Task.FromResult(new PluginActionResult(false, "Choose an existing DmdDevice.ini."));
			var settings = DmdBridgeConfig.Load(path, Settings(Schema.Defaults()), true);
			var values = JObject.FromObject(settings); values["UnlitDot"] = "#" + ColorUtility.ToHtmlStringRGB(settings.UnlitDot); values["GlassColor"] = "#" + ColorUtility.ToHtmlStringRGB(settings.GlassColor);
			values["AltColorRoot"] = settings.AltColorPath ?? ""; values["Colorization"] = settings.EnableColorization ? "auto" : "none";
			return Task.FromResult(new PluginActionResult(true, "Imported into the draft. Review the values and Apply to save.", new PluginSettingsSnapshot(1, values)));
		}
	}
}
