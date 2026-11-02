// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed partial class DmdPlugin
	{
		private CabinetDisplaySnapshot _cabinet;
		private CabinetOutputMode _monitorMode, _deviceMode;
		private volatile bool _stopped; // Instances are single-use per game, as is the native-resource scope.
		private readonly List<string> _deviceIssues = new List<string>();

		private void ConfigureCabinetOutputs(PluginGameContext game, PluginSettingsSnapshot effective, DmdBridgeSettings settings)
		{
			_cabinet = game.Capabilities.Find<CabinetDisplaySnapshot>(CabinetDisplayCapability.Configuration);
			_monitorMode = _cabinet?.ModeFor(_application.Plugin.Id, CabinetOutputRole.Dmd) ?? CabinetOutputMode.Custom;
			_deviceMode = _cabinet?.ModeFor(_application.Plugin.Id, CabinetOutputRole.Dmd, CabinetEndpointKind.Device) ?? CabinetOutputMode.Custom;
			if (_monitorMode != CabinetOutputMode.Custom) settings.EnableNativeWindow = false; // Override INI inheritance after effective settings resolve.
			var leases = game.Capabilities.Require<PluginOutputLeases>(UnityPluginCapability.Outputs);
			if (_deviceMode == CabinetOutputMode.Custom) {
				// Existing drivers retain their platform support; this catalog call supplies only canonical reservation keys.
				foreach (var profile in DmdDeviceProfiles.Create(effective, "windows")) {
					var driver = Driver(profile);
					if (effective.Boolean(driver.Enable) && !Reserve(profile, leases)) SetDeviceEnabled(settings, driver.Id, false);
				}
				return;
			}
			settings.EnableZeDmd = settings.EnablePinDmd3 = settings.EnablePixelcade = settings.EnablePin2Dmd = settings.EnablePinDmd1 = settings.EnablePinDmd2 = false;
			if (_deviceMode != CabinetOutputMode.Player || _cabinet == null) return;
			foreach (var target in _cabinet.Targets) {
				if (target.Kind != CabinetEndpointKind.Device || target.Role != CabinetOutputRole.Dmd) continue;
				var profile = target.DeviceProfile;
				if (profile?.Provider != _application.Plugin.Id) continue;
				if (target.State != CabinetEndpointState.Available) { if (target.State == CabinetEndpointState.Unavailable) DeviceIssue(profile.Label + ": " + target.Reason); continue; }
				try {
					if (!profile.Available || target.DeviceLeaseKey != profile.LeaseKey) throw new InvalidOperationException("The resolved hardware profile is unavailable or inconsistent.");
					var driver = Driver(profile);
					using var reader = new JsonTextReader(new StringReader(profile.Configuration)) { DateParseHandling = DateParseHandling.None };
					var values = JObject.Load(reader);
					if ((string)values["driver"] != driver.Id || values.Properties().Any(p => p.Name != "driver" && p.Name != driver.Port && !driver.Options.Contains(p.Name))) throw new InvalidOperationException("The hardware profile has incompatible options.");
					values.Remove("driver");
					JsonConvert.PopulateObject(values.ToString(Formatting.None), settings, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error });
					if (!Reserve(profile, leases)) continue;
					SetDeviceEnabled(settings, driver.Id, true);
				} catch (Exception error) { DeviceIssue(profile.Label + ": " + error.Message); }
			}
		}

		private static DmdDeviceProfiles.DeviceDriver Driver(CabinetDeviceProfile profile)
			=> DmdDeviceProfiles.DeviceDrivers.FirstOrDefault(driver => profile.Id == DmdPluginFactory.Id + "/" + driver.Id && profile.Provider == DmdPluginFactory.Id && profile.Role == CabinetOutputRole.Dmd)
				?? throw new InvalidOperationException("Unknown DMD hardware profile.");

		private bool Reserve(CabinetDeviceProfile profile, PluginOutputLeases leases)
		{
			var scope = new ResourceScope();
			try {
				// Use the exact canonical key resolved by the provider; the lease service never normalizes identities.
				scope.Own(leases.Acquire(profile.LeaseKey, _application.Plugin.Id));
				if (profile.FamilyLeaseKey.Length > 0 && profile.FamilyLeaseKey != profile.LeaseKey) scope.Own(leases.Acquire(profile.FamilyLeaseKey, _application.Plugin.Id));
				_nativeResources.Own(scope); return true;
			} catch (Exception error) { scope.Dispose(); DeviceIssue(profile.Label + ": " + error.Message); return false; }
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
