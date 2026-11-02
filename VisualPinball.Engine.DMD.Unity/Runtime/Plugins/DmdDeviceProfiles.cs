// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	internal static class DmdDeviceProfiles
	{
		internal sealed class DeviceDriver
		{
			public readonly string Id, Name, Enable, Port, Family;
			public readonly string[] Options;
			public DeviceDriver(string id, string name, string enable, string port, params string[] options) {
				Id = id; Name = name; Enable = enable; Port = port; Family = "usb:" + id + ":auto"; Options = options;
			}
		}
		internal static readonly DeviceDriver[] DeviceDrivers = {
			new DeviceDriver("zedmd", "ZeDMD", "EnableZeDmd", "ZeDmdPort", "ZeDmdBrightness", "ZeDmdDebug"),
			new DeviceDriver("pindmd3", "PinDMD 3", "EnablePinDmd3", "PinDmd3Port"),
			new DeviceDriver("pixelcade", "Pixelcade", "EnablePixelcade", "PixelcadePort", "PixelcadeColorMatrix"),
			new DeviceDriver("pin2dmd", "PIN2DMD", "EnablePin2Dmd", "", "Pin2DmdOutputDelay"),
			new DeviceDriver("pindmd1", "PinDMD 1", "EnablePinDmd1", ""),
			new DeviceDriver("pindmd2", "PinDMD 2", "EnablePinDmd2", "")
		};

		/// <summary>One global profile per driver, using committed values over the INI's global defaults. No hardware discovery.</summary>
		public static IReadOnlyList<CabinetDeviceProfile> Create(PluginSettingsSnapshot settings, string platform)
		{
			var result = new List<CabinetDeviceProfile>();
			foreach (var driver in DeviceDrivers) {
				string port = driver.Port.Length > 0 ? CanonicalPort(settings.String(driver.Port)) : "";
				bool validPort = port.Length <= 256;
				string key = validPort && port.Length > 0 ? "serial:" + port : driver.Family;
				var values = new JObject { ["driver"] = driver.Id };
				if (driver.Port.Length > 0) values[driver.Port] = validPort ? port : "";
				foreach (string option in driver.Options) if (settings.Value(option) is JToken value) values[option] = value.DeepClone();
				bool available = platform == "windows" && port.Length <= 256;
				result.Add(new CabinetDeviceProfile("org.visualpinball.dmd/" + driver.Id, "org.visualpinball.dmd", CabinetOutputRole.Dmd, driver.Name + (!validPort ? " (invalid port)" : port.Length > 0 ? " (" + port + ")" : " (auto)"), key, driver.Family,
					values.ToString(Formatting.None), available, platform != "windows" ? "Central hardware profiles are not certified on this platform." : port.Length > 256 ? "The port name is too long." : ""));
			}
			return result.AsReadOnly();
		}

		internal static string CanonicalPort(string port)
		{
			port = (port ?? "").Trim();
			if (port.StartsWith(@"\\.\", StringComparison.Ordinal)) port = port.Substring(4);
			if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && uint.TryParse(port.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out uint number) && number > 0) return "COM" + number.ToString(CultureInfo.InvariantCulture);
			return port;
		}
	}
}
