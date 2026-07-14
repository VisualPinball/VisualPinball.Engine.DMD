using System;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity
{
	/// <summary>
	/// Tracks whether the selected display must bypass a colorization source that cannot consume
	/// the format emitted by its gamelogic engine.
	/// </summary>
	internal sealed class DmdColorizationPolicy
	{
		private DisplayConfig _display;
		private bool _warningIssued;

		public bool BypassColorization { get; private set; }

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
			_warningIssued = false;
		}

		/// <summary>
		/// Returns whether the active colorizing pipeline must be rebuilt as passthrough.
		/// </summary>
		public bool ObserveColorizedFrame(DisplayFrameFormat format, out bool shouldWarn)
		{
			if (SupportsColorization(format)) {
				shouldWarn = false;
				return false;
			}

			BypassColorization = true;
			shouldWarn = !_warningIssued;
			_warningIssued = true;
			return true;
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
