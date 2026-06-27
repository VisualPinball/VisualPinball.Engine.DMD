using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using LibDmd.Frame;
using LibDmd.Input;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity
{
	internal sealed class VpeGleSource : AbstractSource, IGray8Source, IRgb24Source
	{
		public override string Name { get; }
		public IObservable<Unit> OnResume => _onResume;
		public IObservable<Unit> OnPause => _onPause;

		private readonly Dimensions _dimensions;
		private readonly Subject<Unit> _onResume = new Subject<Unit>();
		private readonly Subject<Unit> _onPause = new Subject<Unit>();
		private readonly Subject<DmdFrame> _gray8Frames = new Subject<DmdFrame>();
		private readonly Subject<DmdFrame> _rgb24Frames = new Subject<DmdFrame>();

		public VpeGleSource(string displayId, int width, int height)
		{
			Name = $"VPE GLE Display ({displayId})";
			_dimensions = new Dimensions(width, height);
		}

		public void Push(DisplayFrameData frame)
		{
			switch (frame.Format) {
				case DisplayFrameFormat.Dmd2:
					_gray8Frames.OnNext(CreateGray8Frame(frame, 3));
					break;
				case DisplayFrameFormat.Dmd4:
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

		public IObservable<DmdFrame> GetGray8Frames(bool dedupe) => _gray8Frames;

		public IObservable<DmdFrame> GetRgb24Frames() => _rgb24Frames;

		private DmdFrame CreateFrame(DisplayFrameData frame, int bitLength)
		{
			var data = new byte[frame.Data.Length];
			Buffer.BlockCopy(frame.Data, 0, data, 0, data.Length);
			return new DmdFrame(_dimensions, data, bitLength);
		}

		private DmdFrame CreateGray8Frame(DisplayFrameData frame, int maxValue)
		{
			var data = new byte[frame.Data.Length];
			for (var i = 0; i < data.Length; i++) {
				data[i] = (byte)(frame.Data[i] * 255 / maxValue);
			}

			return new DmdFrame(_dimensions, data, 8);
		}
	}
}
