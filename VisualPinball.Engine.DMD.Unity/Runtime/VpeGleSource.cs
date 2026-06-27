using System;
using System.Reactive;
using System.Reactive.Subjects;
using LibDmd.Frame;
using LibDmd.Input;
using VisualPinball.Unity;

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

		public abstract void Push(DisplayFrameData frame);

		protected DmdFrame CreateFrame(DisplayFrameData frame, int bitLength)
		{
			var data = new byte[frame.Data.Length];
			Buffer.BlockCopy(frame.Data, 0, data, 0, data.Length);
			return new DmdFrame(Dimensions, data, bitLength);
		}

		protected DmdFrame CreateGray4Frame(DisplayFrameData frame)
		{
			var data = new byte[frame.Data.Length];
			for (var i = 0; i < data.Length; i++) {
				data[i] = (byte)(frame.Data[i] >> 4);
			}

			return new DmdFrame(Dimensions, data, 4);
		}

		protected DmdFrame CreateGray8Frame(DisplayFrameData frame, int maxValue)
		{
			var data = new byte[frame.Data.Length];
			for (var i = 0; i < data.Length; i++) {
				data[i] = (byte)(frame.Data[i] * 255 / maxValue);
			}

			return new DmdFrame(Dimensions, data, 8);
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

			public override void Push(DisplayFrameData frame)
			{
				switch (frame.Format) {
					case DisplayFrameFormat.Dmd2:
						_gray2Frames.OnNext(CreateFrame(frame, 2));
						_gray8Frames.OnNext(CreateGray8Frame(frame, 3));
						break;
					case DisplayFrameFormat.Dmd4:
						_gray4Frames.OnNext(CreateFrame(frame, 4));
						_gray8Frames.OnNext(CreateGray8Frame(frame, 15));
						break;
					case DisplayFrameFormat.Dmd8:
						_gray8Frames.OnNext(CreateFrame(frame, 8));
						break;
					case DisplayFrameFormat.Dmd24:
						_rgb24Frames.OnNext(CreateFrame(frame, 24));
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

			public ColorizableVpeGleSource(string displayId, int width, int height) : base(displayId, width, height)
			{
			}

			public override void Push(DisplayFrameData frame)
			{
				switch (frame.Format) {
					case DisplayFrameFormat.Dmd2:
						_gray2Frames.OnNext(CreateFrame(frame, 2));
						break;
					case DisplayFrameFormat.Dmd4:
						_gray4Frames.OnNext(CreateFrame(frame, 4));
						break;
					case DisplayFrameFormat.Dmd8:
						_gray4Frames.OnNext(CreateGray4Frame(frame));
						break;
				}
			}

			public IObservable<DmdFrame> GetGray2Frames(bool dedupe, bool skipIdentificationFrames) => _gray2Frames;

			public IObservable<DmdFrame> GetGray4Frames(bool dedupe, bool skipIdentificationFrames) => _gray4Frames;
		}
	}
}
