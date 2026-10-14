// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Threading.Tasks;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Unity
{
	internal interface IManagedDmdPipeline : IDisposable
	{
		bool Matches(string displayId, int width, int height);
		void Push(in PluginDisplayFrame frame);
		Task StopWorkerAsync();
	}

	/// <summary>Replaces a display graph after its worker stops, retaining the latest frame during the handoff.</summary>
	internal sealed class ManagedDmdInput
	{
		private readonly OwnerScheduler _owner;
		private readonly Func<string, int, int, IManagedDmdPipeline> _create;
		private readonly Func<long, bool> _sourceIsCurrent;
		private readonly Action<Exception> _reportError;
		private long _instance;
		private Task _replacement;
		private Task _shutdownTask;
		private bool _shutdown;
		private bool _replacementRequired;
		private byte[] _pendingPixels;
		private PluginDisplayFrame _pendingFrame;
		private string _pendingDisplayId;
		internal IManagedDmdPipeline Pipeline { get; private set; }

		internal ManagedDmdInput(OwnerScheduler owner, Func<string, int, int, IManagedDmdPipeline> create, Func<long, bool> sourceIsCurrent = null, Action<Exception> reportError = null)
		{
			_owner = owner ?? throw new ArgumentNullException(nameof(owner));
			_create = create ?? throw new ArgumentNullException(nameof(create));
			_sourceIsCurrent = sourceIsCurrent;
			_reportError = reportError;
		}

		internal void Feed(in PluginDisplayFrame frame, string displayId)
		{
			_owner.VerifyOwner();
			if (_shutdown) return;
			if (_replacement != null && !_replacement.IsCompleted) {
				Retain(frame, displayId);
				return;
			}
			_replacement = null;
			if (Pipeline != null && (_replacementRequired || _instance != frame.Instance || !Pipeline.Matches(displayId, frame.Width, frame.Height))) {
				Retain(frame, displayId);
				_replacement = ReplaceAsync(Pipeline);
				return;
			}
			StartOrPush(frame, displayId);
		}

		private void Retain(in PluginDisplayFrame frame, string displayId)
		{
			int length = checked(frame.Stride * frame.Height);
			if (_pendingPixels == null || _pendingPixels.Length < length) _pendingPixels = new byte[length];
			frame.Pixels.Span.Slice(0, length).CopyTo(_pendingPixels);
			_pendingFrame = new PluginDisplayFrame(_pendingPixels.AsMemory(0, length), frame.Width, frame.Height, frame.Stride,
				frame.Encoding, frame.Sequence, frame.TimestampUs, frame.Instance, frame.ColorSpace, frame.Alpha);
			_pendingDisplayId = displayId;
		}

		private void StartOrPush(in PluginDisplayFrame frame, string displayId)
		{
			Pipeline ??= _create(displayId, frame.Width, frame.Height);
			_instance = frame.Instance;
			Pipeline?.Push(frame);
		}

		private async Task ReplaceAsync(IManagedDmdPipeline previous)
		{
			try {
				await previous.StopWorkerAsync().ConfigureAwait(false);
				await _owner.Switch();
				previous.Dispose();
				Pipeline = null;
				_replacementRequired = false;
				if (_shutdown) return;
				// Push copies synchronously into the replacement's worker buffer. No further source event is needed.
				if (_sourceIsCurrent == null || _sourceIsCurrent(_pendingFrame.Instance)) StartOrPush(_pendingFrame, _pendingDisplayId);
			} catch (Exception error) {
				await _owner.Switch();
				// A failed stop is not proof of worker completion. Keep its graph until a later stop succeeds.
				_replacementRequired = Pipeline != null;
				_reportError?.Invoke(error);
			} finally {
				await _owner.Switch();
				_pendingFrame = default;
				_pendingDisplayId = null;
			}
		}

		internal Task ShutdownAsync()
		{
			_owner.VerifyOwner();
			if (_shutdownTask != null) return _shutdownTask;
			_shutdown = true;
			_pendingFrame = default;
			_pendingDisplayId = null;
			_pendingPixels = null;
			return _shutdownTask = StopAsync();
		}

		private async Task StopAsync()
		{
			if (_replacement != null) await _replacement.ConfigureAwait(false);
			await _owner.Switch();
			var pipeline = Pipeline;
			if (pipeline == null) return;
			await pipeline.StopWorkerAsync().ConfigureAwait(false);
			await _owner.Switch();
			pipeline.Dispose();
			Pipeline = null;
		}
	}
}
