// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Player
{
	internal sealed class DmdPlugin : PlayerPlugin, IPluginTick, IPluginEmergencyStop, IPluginDeferredCleanup
	{
		private readonly PluginApplicationContext _application;
		private readonly List<DmdPresentationDestination> _presentations = new List<DmdPresentationDestination>();
		private readonly ResourceScope _nativeResources = new ResourceScope();
		private bool _releasePending;
		private DmdPresentationDestination[] _dispatch = Array.Empty<DmdPresentationDestination>();
		public Task Quiescence { get; private set; } = Task.CompletedTask;
		private DmdBridgePlayer _bridge;
		private GameObject _root;
		private PluginResourceBinding<IPluginDisplaySource> _binding;
		internal DmdPlugin(PluginApplicationContext application) { _application = application; }
		public override Task PrepareGameAsync(PluginGameContext game, CancellationToken cancellation)
		{
			var scene = game.Capabilities.Require<IUnityGameScene>(UnityPluginCapability.GameScene);
			var settings = DmdPluginFactory.Settings(game.Settings);
			settings.RomName = game.Identity.Key;
			var asset = ColorizationCatalog.Select(game.Settings.String("AltColorRoot"), game.Identity.Key, game.Settings.String("Colorization", "auto"));
			settings.EnableColorization = asset != null;
			if (asset != null) {
				_nativeResources.Own(ColorizationCatalog.Stage(asset, Path.Combine(_application.Configuration.DirectoryFor(_application.Plugin), "cache", "colorizations"), game.Identity.Key, out string staged));
				settings.AltColorPath = staged;
			}
			var outputs = game.Capabilities.Require<PluginOutputLeases>(UnityPluginCapability.Outputs);
			foreach (var output in OutputIds(settings)) _nativeResources.Own(outputs.Acquire(output, _application.Plugin.Id));
			_root = new GameObject("DMD plugin"); _root.SetActive(false); _root.transform.SetParent(scene.Table.transform, false);
			game.Lifetime.Own(new Subscription(() => { if (_root != null) UnityEngine.Object.Destroy(_root); }));
			_bridge = _root.AddComponent<DmdBridgePlayer>();
			var selection = new PluginResourceSelection(ResourceKind.Display) { Controller = game.ControllerId, Session = game.Session, Role = "dmd",
				Intent = asset == null ? ResourceView.Original : ResourceView.Identification,
				Accepts = asset == null ? DisplayEncoding.Gray2 | DisplayEncoding.Gray4 | DisplayEncoding.Luminance8 | DisplayEncoding.Rgb24 : DisplayEncoding.Gray2 | DisplayEncoding.Gray4 };
			string source = game.Settings.String("Source", "auto"); if (source != "auto") selection.ResourceId = PluginResourceChoices.Resolve(game.ControllerId, source);
			_binding = game.Lifetime.Own(game.Resources.Bind<IPluginDisplaySource>(selection));
			_bridge.ConfigureManaged(settings, scene.Table, () => {
				_application.Scheduler.VerifyOwner();
				var destination = new DmdPresentationDestination(_application, _binding.Descriptor);
				_presentations.RemoveAll(presentation => !presentation.IsAvailable); _presentations.Add(destination); _dispatch = _presentations.ToArray(); return destination;
			}, _application.Scheduler);
			game.Lifetime.Own(_binding.SubscribeFrames(frame => {
				if (_bridge != null) _bridge.FeedFrame(frame, _binding.Descriptor.Id.Substring(_binding.Descriptor.Id.LastIndexOf('/') + 1));
			}));
			_binding.Changed += ReportBinding;
			game.Lifetime.Own(new Subscription(() => _binding.Changed -= ReportBinding));
			_root.SetActive(true);
			return Task.CompletedTask;
		}
		private static IEnumerable<string> OutputIds(DmdBridgeSettings settings)
		{
			if (settings.EnableZeDmd) yield return string.IsNullOrWhiteSpace(settings.ZeDmdPort) ? "usb:zedmd:auto" : "serial:" + settings.ZeDmdPort.ToUpperInvariant();
			if (settings.EnablePinDmd3) yield return string.IsNullOrWhiteSpace(settings.PinDmd3Port) ? "usb:pindmd3:auto" : "serial:" + settings.PinDmd3Port.ToUpperInvariant();
			if (settings.EnablePixelcade) yield return string.IsNullOrWhiteSpace(settings.PixelcadePort) ? "usb:pixelcade:auto" : "serial:" + settings.PixelcadePort.ToUpperInvariant();
			if (settings.EnablePin2Dmd) yield return "usb:pin2dmd:auto";
			if (settings.EnablePinDmd1) yield return "usb:pindmd1:auto";
			if (settings.EnablePinDmd2) yield return "usb:pindmd2:auto";
		}
		private void ReportBinding() { if (_binding.State != ResourceBindingState.Bound) _application.Log(_binding.Diagnostic); }
		public void Tick(double unscaledSeconds) { foreach (var presentation in _dispatch) presentation.Pump(); }
		public override async Task StopGameAsync(CancellationToken cancellation)
		{
			try { if (_bridge != null) await _bridge.ShutdownAsync(cancellation); }
			finally {
				await _application.Scheduler.Switch();
				if (_bridge != null) Quiescence = _bridge.ShutdownCompletion;
				ReleaseWhenStopped(); _presentations.Clear(); _dispatch = Array.Empty<DmdPresentationDestination>();
			}
		}
		public void EmergencyStop() { if (_bridge != null) { _bridge.Shutdown(); Quiescence = _bridge.ShutdownCompletion; } ReleaseWhenStopped(); }
		public override void Dispose() => EmergencyStop();
		private void ReleaseWhenStopped()
		{
			if (_releasePending) return;
			var stopped = _bridge != null ? _bridge.ShutdownCompletion : Task.CompletedTask;
			_releasePending = true;
			if (stopped.IsCompleted) _nativeResources.Dispose();
			else _ = stopped.ContinueWith(task => {
				var ignored = task.Exception;
				_application.Scheduler.Post(_nativeResources.Dispose);
			}, TaskScheduler.Default);
		}
	}
}
