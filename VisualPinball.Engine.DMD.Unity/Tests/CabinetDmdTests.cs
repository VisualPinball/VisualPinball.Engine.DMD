// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LibDmd.Frame;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using VisualPinball.Unity;
using Object = UnityEngine.Object;

namespace VisualPinball.Engine.DMD.Tests
{
	public sealed class CabinetDmdTests
	{
		private const string Provider = "org.visualpinball.dmd";
		private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
		private static Type Type(string name) => Assembly.Load("VisualPinball.Engine.DMD.Player").GetType("VisualPinball.Engine.DMD.Player." + name, true);
		private static object New(string name, params object[] args) => Activator.CreateInstance(Type(name), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
		private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(obj, args);
		private static PluginApplicationContext Application(PluginResourceRegistry registry, PluginCapabilities capabilities, OwnerScheduler scheduler, ResourceScope lifetime)
			=> new(new PluginDescriptor(Provider, "DMD", "Fixture"), new PluginConfigurationStore(System.IO.Path.Combine(ApplicationPath, "unused")), scheduler, capabilities, registry, lifetime, _ => { });
		private static string ApplicationPath => System.IO.Path.GetTempPath();
		private static DmdBridgeSettings Style(float gamma = 1, float glow = 0) => new() { DotSize = .5f, DotRounding = 0, DotSharpness = 1, Brightness = 1, Gamma = gamma, DotGlow = glow };
		[UnityTearDown] public IEnumerator Exit() { if (EditorApplication.isPlaying) yield return new ExitPlayMode(); }

		[UnityTest]
		public IEnumerator ProcessedFrameSurfaceKeepsColorOrientationStyleAndRecoversLostTargets()
		{
			yield return new EnterPlayMode();
			bool running = true; int changes = 0;
			var targets = new[] { new CabinetDisplayTarget("fixture", SurfaceRoles.Dmd, CabinetEndpointKind.DesktopRegion, CabinetEndpointState.Available, width: 64, height: 64) };
			var surface = New("ManagedDmdSurface", Style(), targets, (Action)(() => changes++), (Func<bool>)(() => running));
			using var owned = (IDisposable)surface;
			var desktop = (IUnityDesktopSurface)surface;
			Assert.That(((ICabinetSurface)surface).IsReady, Is.False);
			var rgb = new byte[] { 128,0,0, 0,255,0, 0,0,255, 255,255,255 }; // top-left red, top-right green
			Call(surface, "Upload", rgb, 2, 2, rgb.Length); Call(surface, "Render");
			Assert.That(((ICabinetSurface)surface).IsReady, Is.True); Assert.That(desktop.Texture, Is.Null);
			var rt = (RenderTexture)desktop.DesktopTexture;
			Color[] Read() {
				var previous = RenderTexture.active; RenderTexture.active = rt;
				var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
				try { read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); read.Apply(); return new[] { read.GetPixel(rt.width/4,rt.height*3/4), read.GetPixel(rt.width*3/4,rt.height*3/4), read.GetPixel(rt.width/4,rt.height/4) }; }
				finally { RenderTexture.active = previous; Object.Destroy(read); }
			}
			var colors = Read(); Assert.That(colors[0].r, Is.EqualTo(128/255f).Within(.03)); Assert.That(colors[0].g, Is.LessThan(.02));
			Assert.That(colors[1].g, Is.GreaterThan(.95)); Assert.That(colors[2].b, Is.GreaterThan(.95));
			rt.Release(); Assert.That(((ICabinetSurface)surface).IsReady, Is.False); Call(surface,"Render"); Assert.That(((ICabinetSurface)surface).IsReady, Is.True);
			Assert.That(Read()[0].r, Is.EqualTo(colors[0].r).Within(.01));
			using var styled = (IDisposable)New("ManagedDmdSurface", Style(2, .5f), targets, (Action)(() => { }), (Func<bool>)(() => true));
			Call(styled, "Upload", rgb, 2, 2, rgb.Length); Call(styled, "Render");
			var old = rt; rt = (RenderTexture)((IUnityDesktopSurface)styled).DesktopTexture;
			Assert.That(Read()[0].r, Is.GreaterThan(colors[0].r + .15f), "Gamma/glow must affect desktop rendering after processed RGB."); rt = old;
			Call(surface,"Upload",rgb,0,2,0); Call(surface,"Render"); Assert.That(((ICabinetSurface)surface).IsReady,Is.False,"Rejected frames cannot re-show old pixels.");
			running = false; Assert.That(desktop.DesktopTexture, Is.Null); Assert.That(changes, Is.GreaterThanOrEqualTo(3));
		}

		[UnityTest]
		public IEnumerator DerivedPublicationKeepsIdentificationRawAndWithdrawsOnWorkerDispose()
		{
			yield return new EnterPlayMode();
			var registry = new PluginResourceRegistry(); var scheduler = new OwnerScheduler(); using var life = new ResourceScope();
			using var pixels = new DisplayResourceSource();
			var raw = new PluginResourceDescriptor("fixture/raw", "gle", "fixture", "Raw", SurfaceRoles.Dmd, ResourceKind.Display, 1, 1, ResourceView.Identification, DisplayEncoding.Gray4);
			using var publication = registry.Publish(raw, pixels);
			var destination = New("DmdPresentationDestination", Application(registry,new PluginCapabilities(),scheduler,life), raw, Style(), Array.Empty<CabinetDisplayTarget>());
			try {
				Call(destination,"RenderRgb24",new DmdFrame(2,2,new byte[] {255,0,0,0,255,0,0,0,255,255,255,255},24)); Call(destination,"Pump"); Call(destination,"RenderDesktop");
				var derived = registry.Snapshot().Single(d=>d.Kind==ResourceKind.Surface);
				Assert.That(derived.Parents.Single(), Does.Contain("/presentation/"));
				Assert.That(registry.Snapshot(new PluginResourceSelection(ResourceKind.Display) { Controller="fixture", Intent=ResourceView.Identification }).Single(), Is.SameAs(raw));
				Assert.That(registry.TryGetSource<IUnityDesktopSurface>(derived,out var source),Is.True); Assert.That(source.DesktopTexture,Is.Not.Null);
				var dispose = Task.Run(()=>((IDisposable)destination).Dispose()); while(!dispose.IsCompleted) yield return null;
				Assert.That(((ICabinetSurface)source).IsReady, Is.False, "Worker shutdown immediately guards borrowed surfaces.");
				// A replacement may occur before the scheduler drains worker cleanup.
				Call(destination,"ReleaseIfDisposed");
				using (var replacement = (IDisposable)New("DmdPresentationDestination", Application(registry,new PluginCapabilities(),scheduler,life), raw, Style(), Array.Empty<CabinetDisplayTarget>())) Assert.That(registry.Snapshot().Count,Is.EqualTo(3));
				scheduler.Pump(); Assert.That(registry.Snapshot().Count,Is.EqualTo(1));
			} finally { ((IDisposable)destination).Dispose(); scheduler.Pump(); }
		}

		[TestCase(CabinetOutputMode.Player)]
		[TestCase(CabinetOutputMode.Custom)]
		[TestCase(CabinetOutputMode.Off)]
		public void ProfilesApplyTypedOptionsAndStopReleasesOnlySuccessfullyAcquiredKeys(CabinetOutputMode mode)
		{
			var registry = new PluginResourceRegistry(); var caps = new PluginCapabilities(); var scheduler = new OwnerScheduler(); using var life = new ResourceScope(); using var gameLife = new ResourceScope();
			var leases = new PluginOutputLeases(); using var service = caps.Publish(UnityPluginCapability.Outputs,leases);
			var pixel = new CabinetDeviceProfile(Provider+"/pixelcade",Provider,SurfaceRoles.Dmd,"Pixel","serial:COM5","usb:pixelcade:auto","{\"driver\":\"pixelcade\",\"PixelcadePort\":\"COM5\",\"PixelcadeColorMatrix\":\"Rgb\"}");
			var pin = new CabinetDeviceProfile(Provider+"/pin2dmd",Provider,SurfaceRoles.Dmd,"Pin","usb:pin2dmd:auto","usb:pin2dmd:auto","{\"driver\":\"pin2dmd\",\"Pin2DmdOutputDelay\":17}");
			using var layout = caps.Publish(CabinetDisplayCapability.Configuration,new CabinetDisplaySnapshot(1,1,1,new[] { CabinetDisplayTarget.Device("pixel",SurfaceRoles.Dmd,CabinetEndpointState.Available,pixel), CabinetDisplayTarget.Device("pin",SurfaceRoles.Dmd,CabinetEndpointState.Available,pin) },new[] { new CabinetOutputAssignment(Provider,SurfaceRoles.Dmd,mode,CabinetEndpointKind.Device) }));
			using var busy = mode == CabinetOutputMode.Custom ? leases.Acquire(pixel.FamilyLeaseKey,"fixture-holder") : null;
			using var plugin = (IDisposable)New("DmdPlugin", Application(registry,caps,scheduler,life));
			var settings=Style(); settings.EnablePixelcade=settings.EnablePin2Dmd=settings.EnableNativeWindow=true;
			var effective=new PluginSettingsSnapshot(1); effective.Values["EnablePixelcade"]=true; effective.Values["EnablePin2Dmd"]=true; effective.Values["PixelcadePort"]="COM5";
			var game=new PluginGameContext(new GameIdentity("test","fixture"),"","","fixture",1,effective,caps,registry,gameLife,CancellationToken.None);
			Call(plugin,"ConfigureCabinetOutputs",game,effective,settings);
			Assert.That(settings.EnableNativeWindow,Is.True,"Hardware adoption cannot change Custom monitor output.");
			Assert.That(settings.EnablePixelcade,Is.EqualTo(mode==CabinetOutputMode.Player)); Assert.That(settings.EnablePin2Dmd,Is.EqualTo(mode!=CabinetOutputMode.Off));
			if(mode==CabinetOutputMode.Player) { Assert.That(settings.PixelcadePort,Is.EqualTo("COM5")); Assert.That(settings.PixelcadeColorMatrix.ToString(),Is.EqualTo("Rgb")); Assert.That(settings.Pin2DmdOutputDelay,Is.EqualTo(17)); }
			Assert.That(leases.Owner(pixel.LeaseKey),Is.EqualTo(mode==CabinetOutputMode.Player ? Provider : null));
			((Task)Call(plugin,"StopGameAsync",CancellationToken.None)).GetAwaiter().GetResult();
			Assert.That(leases.Owner(pixel.LeaseKey),Is.Null); Assert.That(leases.Owner(pin.LeaseKey),Is.Null);
			Assert.That(leases.Owner(pixel.FamilyLeaseKey),Is.EqualTo(mode==CabinetOutputMode.Custom ? "fixture-holder" : null));
		}

		[Test]
		public void BusyHardwareIsDisabledWithoutChangingMonitorOrColorizationAndPartialLeasesRollBack()
		{
			var registry = new PluginResourceRegistry(); var caps = new PluginCapabilities(); var scheduler = new OwnerScheduler(); using var life = new ResourceScope(); using var gameLife = new ResourceScope();
			var leases = new PluginOutputLeases(); using var service = caps.Publish(UnityPluginCapability.Outputs,leases);
			var profile = new CabinetDeviceProfile(Provider+"/zedmd",Provider,SurfaceRoles.Dmd,"Fixture","serial:COM3","usb:zedmd:auto","{\"driver\":\"zedmd\",\"ZeDmdPort\":\"COM3\"}");
			using var layout = caps.Publish(CabinetDisplayCapability.Configuration,new CabinetDisplaySnapshot(1,1,1,new[] {CabinetDisplayTarget.Device("fixture",SurfaceRoles.Dmd,CabinetEndpointState.Available,profile)},new[] { new CabinetOutputAssignment(Provider,SurfaceRoles.Dmd,CabinetOutputMode.Player), new CabinetOutputAssignment(Provider,SurfaceRoles.Dmd,CabinetOutputMode.Player,CabinetEndpointKind.Device) }));
			using var busy = leases.Acquire(profile.FamilyLeaseKey,"fixture-holder");
			using var plugin = (IDisposable)New("DmdPlugin", Application(registry,caps,scheduler,life));
			var settings = Style(); settings.EnableNativeWindow=true; settings.EnableColorization=true; settings.EnablePinDmd2=true;
			var effective = new PluginSettingsSnapshot(1); var game = new PluginGameContext(new GameIdentity("test","fixture"),"","","fixture",1,effective,caps,registry,gameLife,CancellationToken.None);
			Call(plugin,"ConfigureCabinetOutputs",game,effective,settings);
			Assert.That(settings.EnableZeDmd,Is.False); Assert.That(settings.EnablePinDmd2,Is.False); Assert.That(settings.EnableNativeWindow,Is.False); Assert.That(settings.EnableColorization,Is.True);
			Assert.That(leases.Owner(profile.LeaseKey),Is.Null,"A failed family lease must release the already acquired serial key.");
		}
	}
}
