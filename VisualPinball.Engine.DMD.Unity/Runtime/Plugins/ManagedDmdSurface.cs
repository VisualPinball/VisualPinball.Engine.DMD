// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using UnityEngine;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;
using VisualPinball.Unity;
using Math = System.Math;

namespace VisualPinball.Engine.DMD.Player
{
	/// <summary>Adapter-owned desktop rendering of processed RGB frames; it never creates a native window.</summary>
	internal sealed class ManagedDmdSurface : IUnityDesktopSurface, IDisposable
	{
		private readonly Material _material;
		private readonly Action _changed;
		private readonly Func<bool> _running;
		private readonly int _requestedWidth, _requestedHeight;
		private Texture2D _pixels;
		private RenderTexture _smallGlow, _largeGlow, _temporary, _output;
		private byte[] _upload;
		private bool _dirty, _ready, _disposed, _valid;
		public ManagedDmdSurface(DmdBridgeSettings settings, IReadOnlyList<CabinetDisplayTarget> targets, Action changed, Func<bool> running)
		{
			_changed = changed; _running = running;
			foreach (var target in targets) if (target.Kind == CabinetEndpointKind.DesktopRegion && target.State == CabinetEndpointState.Available) {
				_requestedWidth = Math.Max(_requestedWidth, target.Width); _requestedHeight = Math.Max(_requestedHeight, target.Height);
			}
			var shader = UnityEngine.Resources.Load<Shader>("CabinetDmd");
			if (shader == null || !shader.isSupported) throw new PlatformNotSupportedException("The managed DMD rendering shader is unavailable.");
			_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			_material.SetFloat("_DotSize", settings.DotSize); _material.SetFloat("_Rounding", settings.DotRounding);
			_material.SetFloat("_Edge", .01f + settings.DotSize * (1 - settings.DotSharpness));
			_material.SetFloat("_Brightness", settings.Brightness); _material.SetFloat("_Gamma", settings.Gamma);
			_material.SetFloat("_Glow", settings.DotGlow); _material.SetFloat("_BackGlow", settings.BackGlow);
			_material.SetVector("_Unlit", new Vector4(settings.UnlitDot.r, settings.UnlitDot.g, settings.UnlitDot.b, 0) / Math.Max(.0001f, settings.Brightness));
		}
		public CabinetSurfaceConsumers Consumers => CabinetSurfaceConsumers.ManagedDesktop;
		public bool IsReady => !_disposed && _running() && _ready && _output != null && _output.IsCreated();
		public Texture DesktopTexture => IsReady ? _output : null;
		public Texture Texture => null;
		public FrameColorSpace SampleColorSpace => FrameColorSpace.Srgb;
		public FrameAlpha DesktopAlpha => FrameAlpha.None;
		public bool DesktopFlipY => false;
		public ResourceKind Kind => ResourceKind.Surface;
		public int Width => _output != null ? _output.width : 0;
		public int Height => _output != null ? _output.height : 0;
		public string Backend => "unity-dmd-desktop";
		public event Action<Texture> Changed { add { } remove { } }

		public void Upload(byte[] rgb, int width, int height, int length)
		{
			if (_disposed || !_running()) return;
			if (width < 1 || height < 1 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize || (long)width * height > 4194304 || (long)width * height * 3 != length || length > rgb.Length) { _dirty = _valid = false; SetReady(false); return; }
			if (_pixels == null || _pixels.width != width || _pixels.height != height) {
				SetReady(false); ReleaseTargets();
				if (_pixels != null) UnityEngine.Object.Destroy(_pixels);
				_pixels = new Texture2D(width, height, TextureFormat.RGB24, false, true) { name = "Processed DMD frame", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
				_upload = new byte[length];
				_smallGlow = Target(width, height); _largeGlow = Target(width, height); _temporary = Target(width, height);
				double aspect = width / (double)height;
				double desired = Math.Max(width * 4, Math.Max(_requestedWidth, _requestedHeight * aspect));
				double outputWidth = Math.Min(Math.Min(SystemInfo.maxTextureSize, 4096), desired);
				double outputHeight = outputWidth / aspect;
				double scale = Math.Min(Math.Min(1, SystemInfo.maxTextureSize / outputHeight), Math.Sqrt(4194304d / (outputWidth * outputHeight)));
				_output = Target(Math.Max(1, (int)(outputWidth * scale)), Math.Max(1, (int)(outputHeight * scale)));
				_material.SetVector("_FrameSize", new Vector4(width, height, 1f / width, 1f / height));
			}
			Buffer.BlockCopy(rgb, 0, _upload, 0, length);
			_pixels.LoadRawTextureData(_upload); _pixels.Apply(false, false); _dirty = _valid = true;
		}

		public void Render()
		{
			if (_disposed || !_running() || !_valid || _pixels == null || _output == null) return;
			bool lost = !_output.IsCreated() || !_smallGlow.IsCreated() || !_largeGlow.IsCreated() || !_temporary.IsCreated();
			if (!_dirty && !lost) return;
			if (lost) { SetReady(false); foreach (var target in new[] { _output, _smallGlow, _largeGlow, _temporary }) if (!target.IsCreated() && !target.Create()) throw new InvalidOperationException("Cannot restore DMD rendering target."); _pixels.Apply(false, false); }
			var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
			try {
				GL.sRGBWrite = false;
				_material.SetVector("_Direction", new Vector4(1f / _pixels.width, 0, 0, 0)); Graphics.Blit(_pixels, _temporary, _material, 1);
				_material.SetVector("_Direction", new Vector4(0, 1f / _pixels.height, 0, 0)); Graphics.Blit(_temporary, _smallGlow, _material, 1);
				_material.SetVector("_Direction", new Vector4(1f / _pixels.width, 0, 0, 0)); Graphics.Blit(_smallGlow, _temporary, _material, 2);
				_material.SetVector("_Direction", new Vector4(0, 1f / _pixels.height, 0, 0)); Graphics.Blit(_temporary, _largeGlow, _material, 2);
				_material.SetTexture("_DotGlow", _smallGlow); _material.SetTexture("_Back", _largeGlow);
				Graphics.Blit(_pixels, _output, _material, 0);
				_dirty = false; SetReady(true);
			} finally { _material.SetTexture("_MainTex", null); GL.sRGBWrite = srgb; RenderTexture.active = active; }
		}
		private static RenderTexture Target(int width, int height) {
			var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Managed DMD", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
			if (!target.Create()) { UnityEngine.Object.Destroy(target); throw new InvalidOperationException("Cannot allocate DMD rendering target."); }
			return target;
		}
		private void SetReady(bool ready) { if (_ready == ready) return; _ready = ready; _changed(); }
		public void Withdraw() => SetReady(false);
		private void ReleaseTargets() { foreach (var target in new[] { _output, _smallGlow, _largeGlow, _temporary }) if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); } _output = _smallGlow = _largeGlow = _temporary = null; }
		public void Dispose() { if (_disposed) return; Withdraw(); _disposed = true; ReleaseTargets(); if (_pixels != null) UnityEngine.Object.Destroy(_pixels); _pixels = null; _upload = null; UnityEngine.Object.Destroy(_material); }
	}
}
