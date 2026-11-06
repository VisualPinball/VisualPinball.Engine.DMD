// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	// What the DMD takes from DmdDevice.ini and VPinMAME's altcolor folder while its own settings are empty.
	internal sealed partial class DmdPluginFactory : IPluginSystemDefaults
	{
		private static readonly (string Section, string Name)[] IniHardware = {
			("zedmd", "ZeDMD"), ("pindmd1", "PinDMD 1"), ("pindmd2", "PinDMD 2"), ("pindmd3", "PinDMD 3"), ("pin2dmd", "PIN2DMD"), ("pixelcade", "Pixelcade"),
		};

		public IReadOnlyList<PluginSystemDefault> FindSystemDefaults(PluginSettingsSnapshot settings, CancellationToken cancellation)
		{
			var found = new List<PluginSystemDefault>();
			string ini = null;
			try { ini = DmdDeviceIni(); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not locate DmdDevice.ini."); }
			var dmd = new PluginSystemDefault("DMD settings", "DmdDevice.ini") { Path = ini ?? "" };
			if (ini == null) {
				dmd.Description = "No DmdDevice.ini. The DMD uses the player's own settings.";
			} else {
				var hardware = IniHardware.Where(entry => IsTrue(DmdBridgeConfig.ReadValue(ini, entry.Section, "enabled"))).Select(entry => entry.Name).ToArray();
				var devices = hardware.Length <= 1 ? string.Join("", hardware) : string.Join(", ", hardware.Take(hardware.Length - 1)) + " and " + hardware[hardware.Length - 1];
				dmd.Description = (hardware.Length > 0 ? $"Colors, the DMD window and {devices}" : "Colors and the DMD window")
					+ ", where Settings leaves them open. Read, never rewritten.";
				// DmdDevice's own window: where VPinMAME's DMD sits on the desktop
				if (IsTrue(DmdBridgeConfig.ReadValue(ini, "virtualdmd", "enabled")) && !IsTrue(DmdBridgeConfig.ReadValue(ini, "virtualdmd", "useregistry"))
					&& Int(ini, "left", out int left) && Int(ini, "top", out int top) && Int(ini, "width", out int width) && Int(ini, "height", out int height)
					&& width > 0 && height > 0) {
					dmd.Outputs = new[] { new PluginSuggestedOutput(CabinetOutputRole.Dmd, left, top, width, height) };
				}
			}
			found.Add(dmd);

			cancellation.ThrowIfCancellationRequested();
			string library = "";
			try { library = ColorizationLibrary(""); } catch (Exception error) { Logger.Debug(error, "[DMD] Could not locate the colorization library."); }
			int games = 0;
			if (library.Length > 0) {
				try { games = Directory.GetDirectories(library).Length; } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
			}
			found.Add(new PluginSystemDefault("Colorization", "VPinMAME") {
				Path = library,
				FieldId = "AltColorRoot",
				Description = library.Length == 0
					? "No colorizations found. Only needed to color a game's DMD."
					: games == 1 ? "Serum, PAC and VNI colorizations for 1 game." : $"Serum, PAC and VNI colorizations for {games} games.",
			});
			return found;
		}

		private static bool IsTrue(string value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || value?.Trim() == "1";

		private static bool Int(string ini, string key, out int value)
			=> int.TryParse(DmdBridgeConfig.ReadValue(ini, "virtualdmd", key)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}
}
