// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed partial class DmdPluginFactory
	{
		public IReadOnlyList<CabinetDisplayImportCandidate> GetDisplayImportCandidates(CabinetDisplayImportRequest request)
		{
			var settings = WithDmdDeviceIni(request.Settings, new HashSet<string>(request.SavedKeys, StringComparer.Ordinal), request.GameKey, out _, out _);
			var result = new List<CabinetDisplayImportCandidate>();
			// NativeWindowDestinationFactory forwards these pixels directly to the native backend, with no WPF DIP scaling.
			if (settings.Boolean("EnableNativeWindow")) result.Add(new CabinetDisplayImportCandidate(SurfaceRoles.Dmd, settings.Integer("NativeWindowLeft"), settings.Integer("NativeWindowTop"), settings.Integer("NativeWindowWidth"), settings.Integer("NativeWindowHeight"), "Placement read from effective settings, including DmdDevice.ini."));
			foreach (var profile in DmdDeviceProfiles.Create(settings, request.Platform)) {
				var driver = DmdDeviceProfiles.DeviceDrivers.First(d => Id + "/" + d.Id == profile.Id);
				if (settings.Boolean(driver.Enable)) result.Add(new CabinetDisplayImportCandidate(SurfaceRoles.Dmd, profile.Id, profile.Available ? "Uses the adapter's global hardware profile." : profile.Reason, profile.Available));
			}
			return result.AsReadOnly();
		}
	}
}
