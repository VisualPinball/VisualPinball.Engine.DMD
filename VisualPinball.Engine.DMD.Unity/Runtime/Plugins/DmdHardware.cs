// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	/// <summary>The hardware panels the plug-in opens from its own settings, and the keys that reserve each one.</summary>
	internal static class DmdHardware
	{
		internal sealed class Driver
		{
			public readonly string Id, Name, Enable, Port, Family;
			public Driver(string id, string name, string enable, string port) {
				Id = id; Name = name; Enable = enable; Port = port; Family = "usb:" + id + ":auto";
			}
		}
		internal static readonly Driver[] Drivers = {
			new Driver("zedmd", "ZeDMD", "EnableZeDmd", "ZeDmdPort"),
			new Driver("pindmd3", "PinDMD 3", "EnablePinDmd3", "PinDmd3Port"),
			new Driver("pixelcade", "Pixelcade", "EnablePixelcade", "PixelcadePort"),
			new Driver("pin2dmd", "PIN2DMD", "EnablePin2Dmd", ""),
			new Driver("pindmd1", "PinDMD 1", "EnablePinDmd1", ""),
			new Driver("pindmd2", "PinDMD 2", "EnablePinDmd2", "")
		};

		/// <summary>
		/// The keys a driver's panel is reserved with: its serial port when one is set, then its family, which every
		/// auto-detected panel of the driver shares. Two plug-ins therefore never open the same device.
		/// </summary>
		public static string[] LeaseKeys(Driver driver, PluginSettingsSnapshot settings)
		{
			string port = Port(driver, settings);
			return port.Length > 0 ? new[] { "serial:" + port, driver.Family } : new[] { driver.Family };
		}

		/// <summary>The driver's name and port, as diagnostics show a panel.</summary>
		public static string Label(Driver driver, PluginSettingsSnapshot settings)
		{
			string port = Port(driver, settings);
			return port.Length > 0 ? driver.Name + " (" + port + ")" : driver.Name;
		}

		// The configured port, canonical; empty when none is set or the name is too long to be one.
		private static string Port(Driver driver, PluginSettingsSnapshot settings)
		{
			string port = driver.Port.Length > 0 ? CanonicalPort(settings.String(driver.Port)) : "";
			return port.Length <= 256 ? port : "";
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
