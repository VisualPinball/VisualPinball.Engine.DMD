// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT
// Licensed under MIT. See https://opensource.org/license/mit.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualPinball.Engine.DMD.Unity;
using VisualPinball.Plugins;

namespace VisualPinball.Engine.DMD.Tests
{
	[TestFixture]
	public sealed class ManagedDmdInputTests
	{
		private OwnerScheduler _owner;
		private ManagedDmdInput _input;
		private List<Pipeline> _created;
		private bool _sourceIsCurrent;
		private bool _failCreation;
		private List<Exception> _errors;
		[SetUp]
		public void SetUp()
		{
			_owner = new OwnerScheduler();
			_created = new List<Pipeline>();
			_sourceIsCurrent = true;
			_failCreation = false;
			_errors = new List<Exception>();
			_input = new ManagedDmdInput(_owner, (id, width, height) => {
				_owner.VerifyOwner();
				if (_failCreation) throw new InvalidOperationException("Graph initialization failed.");
				var pipeline = new Pipeline(id, width, height);
				_created.Add(pipeline);
				return pipeline;
			}, instance => _sourceIsCurrent, _errors.Add);
		}
		[TearDown]
		public void TearDown()
		{
			foreach (var pipeline in _created) pipeline.FinishStop();
			var shutdown = _input.ShutdownAsync();
			PumpUntil(() => shutdown.IsCompleted);
			shutdown.GetAwaiter().GetResult();
		}
		private void PumpUntil(Func<bool> complete)
		{
			Assert.That(SpinWait.SpinUntil(() => { _owner.Pump(); return complete(); }, TimeSpan.FromSeconds(5)), Is.True, "Owner-thread work did not finish.");
		}
		private static PluginDisplayFrame Frame(byte[] pixels, int width = 2, int height = 1, int stride = 2, long instance = 1, long sequence = 1)
			=> new PluginDisplayFrame(pixels, width, height, stride, DisplayEncoding.Gray4, sequence, sequence * 1000, instance);

		[TestCase(false)]
		[TestCase(true)]
		public void SingleChangedFrameSurvivesWithoutAnyFurtherSourceEvents(bool sourceChange)
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			var previous = _created[0];
			var pixels = new byte[] { 3, 4, 5, 6 };
			var changed = sourceChange ? Frame(pixels, instance: 2) : Frame(pixels, width: 4, stride: 4);
			_input.Feed(changed, "dmd0");
			Array.Fill(pixels, (byte)0); // The producer reuses its borrowed memory as soon as Feed returns.
			Assert.That(_created.Count, Is.EqualTo(1));
			Assert.That(previous.Disposed, Is.False);
			previous.FinishStop();
			PumpUntil(() => _created.Count == 2);
			Assert.That(previous.Disposed, Is.True);
			Assert.That(_created[1].Frames[0].Pixels.ToArray(), Is.EqualTo(sourceChange ? new byte[] { 3, 4 } : new byte[] { 3, 4, 5, 6 }));
			Assert.That(_created[1].Frames[0].Instance, Is.EqualTo(changed.Instance));
		}

		[Test]
		public void RebuildUsesLatestFrameAndItsShapeEncodingAndMetadata()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			_input.Feed(Frame(new byte[] { 3, 4, 5, 6 }, width: 4, stride: 4), "dmd0");
			var pixels = new byte[] { 10, 11, 12, 0, 20, 21, 22, 0 };
			var latest = new PluginDisplayFrame(pixels, 1, 2, 4, DisplayEncoding.Rgb24, 17, 23000, 5, FrameColorSpace.Linear);
			_input.Feed(latest, "dmd1");
			Array.Fill(pixels, (byte)0);
			_created[0].FinishStop();
			PumpUntil(() => _created.Count == 2);
			var current = _created[1];
			Assert.That(current.Id, Is.EqualTo("dmd1"));
			Assert.That(current.Width, Is.EqualTo(1));
			Assert.That(current.Height, Is.EqualTo(2));
			Assert.That(current.Frames.Count, Is.EqualTo(1));
			var received = current.Frames[0];
			Assert.That(received.Pixels.ToArray(), Is.EqualTo(new byte[] { 10, 11, 12, 0, 20, 21, 22, 0 }));
			Assert.That(received.Stride, Is.EqualTo(4));
			Assert.That(received.Encoding, Is.EqualTo(DisplayEncoding.Rgb24));
			Assert.That(received.Instance, Is.EqualTo(5));
			Assert.That(received.Sequence, Is.EqualTo(17));
			Assert.That(received.TimestampUs, Is.EqualTo(23000));
			Assert.That(received.ColorSpace, Is.EqualTo(FrameColorSpace.Linear));
		}

		[Test]
		public void ShutdownDuringRebuildWaitsForWorkerAndDoesNotCreateAnOutput()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			_input.Feed(Frame(new byte[] { 3, 4, 5, 6 }, width: 4, stride: 4), "dmd0");
			var shutdown = _input.ShutdownAsync();
			Assert.That(_input.ShutdownAsync(), Is.SameAs(shutdown));
			Assert.That(shutdown.IsCompleted, Is.False);
			Assert.That(_created[0].Disposed, Is.False);
			_input.Feed(Frame(new byte[] { 7, 8 }, instance: 3), "dmd0");
			_created[0].FinishStop();
			PumpUntil(() => shutdown.IsCompleted);
			shutdown.GetAwaiter().GetResult();
			Assert.That(_created.Count, Is.EqualTo(1));
			Assert.That(_created[0].Disposed, Is.True);
			Assert.That(_input.Pipeline, Is.Null);
		}

		[Test]
		public void FailedReplacementCanRetryOnTheNextFrameAndStillShutDown()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			_failCreation = true;
			_input.Feed(Frame(new byte[] { 3, 4 }, instance: 2), "dmd0");
			_created[0].FinishStop();
			PumpUntil(() => _errors.Count == 1);
			Assert.That(_input.Pipeline, Is.Null);
			Assert.That(_created[0].Disposed, Is.True);
			_failCreation = false;
			_input.Feed(Frame(new byte[] { 5, 6 }, instance: 2), "dmd0");
			Assert.That(_created.Count, Is.EqualTo(2));
			Assert.That(_created[1].Frames[0].Pixels.ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
			var shutdown = _input.ShutdownAsync();
			Assert.That(shutdown.IsCompleted, Is.False);
			_created[1].FinishStop();
			PumpUntil(() => shutdown.IsCompleted);
			Assert.That(shutdown.IsCompletedSuccessfully, Is.True);
		}

		[Test]
		public void FailedWorkerStopDoesNotReleaseItsGraphAndIsRetriedEvenForOriginalShape()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			var previous = _created[0];
			previous.FailNextStop = true;
			_input.Feed(Frame(new byte[] { 3, 4, 5, 6 }, width: 4, stride: 4), "dmd0");
			Assert.That(_errors.Count, Is.EqualTo(1));
			Assert.That(previous.Disposed, Is.False);
			_input.Feed(Frame(new byte[] { 5, 6 }), "dmd0");
			Assert.That(previous.StopRequests, Is.EqualTo(2));
			Assert.That(previous.Disposed, Is.False);
			previous.FinishStop();
			PumpUntil(() => _created.Count == 2);
			Assert.That(previous.Disposed, Is.True);
			Assert.That(_created[1].Frames[0].Pixels.ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
		}

		[Test]
		public void RemovedSourceDoesNotRecreateAnOutputFromAPendingFrame()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			_input.Feed(Frame(new byte[] { 3, 4, 5, 6 }, width: 4, stride: 4), "dmd0");
			_sourceIsCurrent = false;
			_created[0].FinishStop();
			PumpUntil(() => _input.Pipeline == null);
			Assert.That(_created.Count, Is.EqualTo(1));
			Assert.That(_created[0].Disposed, Is.True);
		}

		[Test]
		public void AlreadyStoppedWorkerCanReplaceSynchronously()
		{
			_input.Feed(Frame(new byte[] { 1, 2 }), "dmd0");
			_created[0].FinishStop();
			_input.Feed(Frame(new byte[] { 3, 4 }, instance: 2), "dmd0");
			Assert.That(_created.Count, Is.EqualTo(2));
			Assert.That(_created[1].Frames[0].Pixels.ToArray(), Is.EqualTo(new byte[] { 3, 4 }));
			_input.Feed(Frame(new byte[] { 5, 6 }, instance: 2), "dmd0");
			Assert.That(_created.Count, Is.EqualTo(2));
			Assert.That(_created[1].Frames.Count, Is.EqualTo(2));
		}

		[Test]
		public void ExistingPipelineReceivesBorrowedMemoryDirectlyWithoutAllocating()
		{
			var frame = Frame(new byte[] { 1, 2 });
			_input.Feed(frame, "dmd0");
			_created[0].Capture = false;
			for (int i = 0; i < 1000; i++) _input.Feed(frame, "dmd0");
			long before = GC.GetAllocatedBytesForCurrentThread();
			for (int i = 0; i < 1000; i++) _input.Feed(frame, "dmd0");
			long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
			Assert.That(allocated, Is.Zero);
			Assert.That(_created.Count, Is.EqualTo(1));
			Assert.That(_created[0].LastBorrowedPixels, Is.EqualTo(frame.Pixels));
		}

		private sealed class Pipeline : IManagedDmdPipeline
		{
			internal readonly string Id;
			internal readonly int Width, Height;
			internal readonly List<PluginDisplayFrame> Frames = new List<PluginDisplayFrame>();
			private readonly TaskCompletionSource<bool> _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			internal bool Disposed;
			internal bool Capture = true;
			internal bool FailNextStop;
			internal int StopRequests;
			internal ReadOnlyMemory<byte> LastBorrowedPixels;
			internal Pipeline(string id, int width, int height) { Id = id; Width = width; Height = height; }
			public bool Matches(string id, int width, int height) => Id == id && Width == width && Height == height;
			public void Push(in PluginDisplayFrame frame)
			{
				if (Disposed) throw new ObjectDisposedException(nameof(Pipeline));
				LastBorrowedPixels = frame.Pixels;
				if (Capture) Frames.Add(new PluginDisplayFrame(frame.Pixels.ToArray(), frame.Width, frame.Height, frame.Stride, frame.Encoding, frame.Sequence, frame.TimestampUs, frame.Instance, frame.ColorSpace, frame.Alpha));
			}
			public Task StopWorkerAsync()
			{
				StopRequests++;
				if (FailNextStop) { FailNextStop = false; return Task.FromException(new InvalidOperationException("Worker stop failed.")); }
				return _stopped.Task;
			}
			internal void FinishStop() => _stopped.TrySetResult(true);
			public void Dispose() { Assert.That(_stopped.Task.IsCompleted, Is.True); Disposed = true; }
		}
	}
}
