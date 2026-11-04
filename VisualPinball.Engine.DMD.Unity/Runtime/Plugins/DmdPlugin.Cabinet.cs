// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed partial class DmdPlugin
	{
		private CabinetDisplaySnapshot _cabinet;
		private CabinetOutputMode _monitorMode;
		private volatile bool _stopped; // Instances are single-use per game, as is the native-resource scope.
		private readonly List<string> _deviceIssues = new List<string>();

		private void ConfigureCabinetOutputs(PluginGameContext game, PluginSettingsSnapshot effective, DmdBridgeSettings settings)
		{
			_cabinet = game.Capabilities.Find<CabinetDisplaySnapshot>(CabinetDisplayCapability.Configuration);
			_monitorMode = _cabinet?.ModeFor(_application.Plugin.Id, CabinetOutputRole.Dmd) ?? CabinetOutputMode.Custom;
			if (_monitorMode != CabinetOutputMode.Custom) settings.EnableNativeWindow = false; // Override INI inheritance after effective settings resolve.
			// Hardware panels open from this plug-in's settings, whoever places its monitor output. A busy one stays off.
			var leases = game.Capabilities.Require<PluginOutputLeases>(UnityPluginCapability.Outputs);
			foreach (var driver in DmdHardware.Drivers) {
				if (effective.Boolean(driver.Enable) && !Reserve(driver, effective, leases)) SetDeviceEnabled(settings, driver.Id, false);
			}
		}

		private bool Reserve(DmdHardware.Driver driver, PluginSettingsSnapshot settings, PluginOutputLeases leases)
		{
			var scope = new ResourceScope();
			try {
				// Use the exact canonical keys; the lease service never normalizes identities.
				foreach (string key in DmdHardware.LeaseKeys(driver, settings)) scope.Own(leases.Acquire(key, _application.Plugin.Id));
				_nativeResources.Own(scope); return true;
			} catch (Exception error) { scope.Dispose(); DeviceIssue(DmdHardware.Label(driver, settings) + ": " + error.Message); return false; }
		}
		private void DeviceIssue(string message) { _deviceIssues.Add(message); _application.Logger.Warn("DMD hardware output unavailable: " + message); }
		private static void SetDeviceEnabled(DmdBridgeSettings settings, string driver, bool enabled)
		{
			switch (driver) {
				case "zedmd": settings.EnableZeDmd = enabled; break;
				case "pindmd3": settings.EnablePinDmd3 = enabled; break;
				case "pixelcade": settings.EnablePixelcade = enabled; break;
				case "pin2dmd": settings.EnablePin2Dmd = enabled; break;
				case "pindmd1": settings.EnablePinDmd1 = enabled; break;
				case "pindmd2": settings.EnablePinDmd2 = enabled; break;
			}
		}
		public void BeforeRender() { if (!_stopped) foreach (var presentation in _dispatch) presentation.RenderDesktop(); }
		private void WithdrawManagedOutputs()
		{
			if (_stopped) return;
			_application.Scheduler.VerifyOwner(); // PluginHost verifies owner affinity for emergency/normal stop.
			_stopped = true;
			foreach (var presentation in _dispatch) try { presentation.Dispose(); } catch (Exception error) { _application.Logger.Warn("DMD presentation cleanup failed.", error); }
			_dispatch = Array.Empty<DmdPresentationDestination>(); _presentations.Clear();
		}
	}
}
