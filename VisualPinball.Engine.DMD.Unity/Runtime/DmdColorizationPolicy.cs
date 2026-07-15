using System;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity
{
	internal enum DmdColorizationPipelineAction
	{
		None,
		Bypass,
		Restore,
	}

	/// <summary>
	/// Tracks whether the selected display must bypass a colorization source that cannot consume
	/// the format emitted by its gamelogic engine.
	/// </summary>
	internal sealed class DmdColorizationPolicy
	{
		private DisplayConfig _display;
		private bool _warningIssued;

		public bool BypassColorization { get; private set; }
		public bool AwaitingSupportedColorizedFrame { get; private set; }

		/// <summary>
		/// Selects a display topology. An identical re-announcement deliberately preserves the
		/// bypass latch; selecting a different id, geometry, or horizontal orientation resets it.
		/// </summary>
		public void SelectDisplay(DisplayConfig display)
		{
			if (SameTopology(_display, display)) {
				return;
			}

			_display = display;
			BypassColorization = false;
			AwaitingSupportedColorizedFrame = false;
			_warningIssued = false;
		}

		/// <summary>
		/// Decides whether the bridge must rebuild its pipeline. A Dmd8 frame may be the transient
		/// pre-preference frame from a missed display announcement, so its bypass recovers when the
		/// requested Dmd2/Dmd4 stream arrives. Dmd24 is RGB-authored and remains a sticky bypass.
		/// </summary>
		public DmdColorizationPipelineAction ObserveFrame(DisplayFrameFormat format,
			bool pipelineUsesColorization, out bool shouldWarn)
		{
			if (!pipelineUsesColorization) {
				if (BypassColorization && AwaitingSupportedColorizedFrame && SupportsColorization(format)) {
					BypassColorization = false;
					AwaitingSupportedColorizedFrame = false;
					_warningIssued = false;
					shouldWarn = false;
					return DmdColorizationPipelineAction.Restore;
				}
				shouldWarn = false;
				return DmdColorizationPipelineAction.None;
			}

			if (SupportsColorization(format)) {
				shouldWarn = false;
				return DmdColorizationPipelineAction.None;
			}

			BypassColorization = true;
			AwaitingSupportedColorizedFrame = format == DisplayFrameFormat.Dmd8;
			shouldWarn = !_warningIssued;
			_warningIssued = true;
			return DmdColorizationPipelineAction.Bypass;
		}

		public static bool SupportsColorization(DisplayFrameFormat format)
		{
			return format == DisplayFrameFormat.Dmd2 || format == DisplayFrameFormat.Dmd4;
		}

		private static bool SameTopology(DisplayConfig first, DisplayConfig second)
		{
			if (ReferenceEquals(first, second)) {
				return true;
			}
			return first != null && second != null &&
			       string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase) &&
			       first.Width == second.Width && first.Height == second.Height && first.FlipX == second.FlipX;
		}
	}
}
