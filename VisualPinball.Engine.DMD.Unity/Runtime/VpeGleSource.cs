using System;
using System.Reactive;
using System.Reactive.Subjects;
using LibDmd.Frame;
using LibDmd.Input;
using NLog;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	internal abstract class VpeGleSource : AbstractSource, ISource
	{
		public override string Name { get; }
		public IObservable<Unit> OnResume => _onResume;
		public IObservable<Unit> OnPause => _onPause;

		protected readonly Dimensions Dimensions;

		private readonly Subject<Unit> _onResume = new Subject<Unit>();
		private readonly Subject<Unit> _onPause = new Subject<Unit>();
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		protected VpeGleSource(string displayId, int width, int height)
		{
			Name = $"VPE GLE Display ({displayId})";
			Dimensions = new Dimensions(width, height);
		}

		public static VpeGleSource Create(DisplayConfig display, bool colorize)
		{
			if (colorize) {
				return new ColorizableVpeGleSource(display.Id, display.Width, display.Height);
			}

			return new PassthroughVpeGleSource(display.Id, display.Width, display.Height);
		}

		/// <summary>
		/// Pushes a frame from <paramref name="data"/> (the pipeline's reused worker buffer) into
		/// the reactive graph. Called on the DMD worker thread; the graph runs synchronously here.
		/// </summary>
		public abstract void Push(DisplayFrameFormat format, byte[] data, int length);

		// A fresh buffer is allocated per frame on purpose: when a converter is active it retains the
		// last frame for palette-rotation ticks (which fire on a separate scheduler thread), so the
		// frame's backing array must not be the pipeline's reused worker buffer. This is the only
		// remaining per-frame allocation on the DMD path; the hand-off and the in-scene pump reuse buffers.
		protected DmdFrame CreateFrame(byte[] data, int length, int bitLength)
		{
			var copy = new byte[length];
			Buffer.BlockCopy(data, 0, copy, 0, length);
			return new DmdFrame(Dimensions, copy, bitLength);
		}

		protected DmdFrame CreateGray8Frame(byte[] data, int length, int maxValue)
		{
			var copy = new byte[length];
			for (var i = 0; i < length; i++) {
				copy[i] = (byte)(data[i] * 255 / maxValue);
			}

			return new DmdFrame(Dimensions, copy, 8);
		}

		private sealed class PassthroughVpeGleSource : VpeGleSource, IGray2Source, IGray4Source, IGray8Source, IRgb24Source
		{
			private readonly Subject<DmdFrame> _gray2Frames = new Subject<DmdFrame>();
			private readonly Subject<DmdFrame> _gray4Frames = new Subject<DmdFrame>();
			private readonly Subject<DmdFrame> _gray8Frames = new Subject<DmdFrame>();
			private readonly Subject<DmdFrame> _rgb24Frames = new Subject<DmdFrame>();

			public PassthroughVpeGleSource(string displayId, int width, int height) : base(displayId, width, height)
			{
			}

			public override void Push(DisplayFrameFormat format, byte[] data, int length)
			{
				switch (format) {
					case DisplayFrameFormat.Dmd2:
						_gray2Frames.OnNext(CreateFrame(data, length, 2));
						_gray8Frames.OnNext(CreateGray8Frame(data, length, 3));
						break;
					case DisplayFrameFormat.Dmd4:
						_gray4Frames.OnNext(CreateFrame(data, length, 4));
						_gray8Frames.OnNext(CreateGray8Frame(data, length, 15));
						break;
					case DisplayFrameFormat.Dmd8:
						_gray8Frames.OnNext(CreateFrame(data, length, 8));
						break;
					case DisplayFrameFormat.Dmd24:
						_rgb24Frames.OnNext(CreateFrame(data, length, 24));
						break;
				}
			}

			public IObservable<DmdFrame> GetGray2Frames(bool dedupe, bool skipIdentificationFrames) => _gray2Frames;

			public IObservable<DmdFrame> GetGray4Frames(bool dedupe, bool skipIdentificationFrames) => _gray4Frames;

			public IObservable<DmdFrame> GetGray8Frames(bool dedupe) => _gray8Frames;

			public IObservable<DmdFrame> GetRgb24Frames() => _rgb24Frames;
		}

		private sealed class ColorizableVpeGleSource : VpeGleSource, IGray2Source, IGray4Source
		{
			private readonly Subject<DmdFrame> _gray2Frames = new Subject<DmdFrame>();
			private readonly Subject<DmdFrame> _gray4Frames = new Subject<DmdFrame>();
			private bool _unsupportedFormatWarningLogged;

			public ColorizableVpeGleSource(string displayId, int width, int height) : base(displayId, width, height)
			{
			}

			public override void Push(DisplayFrameFormat format, byte[] data, int length)
			{
				switch (format) {
					case DisplayFrameFormat.Dmd2:
						_gray2Frames.OnNext(CreateFrame(data, length, 2));
						break;
					case DisplayFrameFormat.Dmd4:
						_gray4Frames.OnNext(CreateFrame(data, length, 4));
						break;
					default:
						if (!_unsupportedFormatWarningLogged) {
							Logger.Warn($"[DMD] Colorization source cannot consume {format}; frame ignored while the main-thread bridge switches to passthrough.");
							_unsupportedFormatWarningLogged = true;
						}
						break;
				}
			}

			public IObservable<DmdFrame> GetGray2Frames(bool dedupe, bool skipIdentificationFrames) => _gray2Frames;

			public IObservable<DmdFrame> GetGray4Frames(bool dedupe, bool skipIdentificationFrames) => _gray4Frames;
		}
	}
}
