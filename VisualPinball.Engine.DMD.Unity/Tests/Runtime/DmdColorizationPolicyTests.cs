using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity.Test
{
	public class DmdColorizationPolicyTests
	{
		[Test]
		public void ColorizingAndPassthroughSourcesAdvertiseMatchingCapabilities()
		{
			var display = new DisplayConfig("dmd0", 128, 32);

			var colorizing = CreateSource(display, true);
			Assert.That(Advertises(colorizing, "IGray2Source"), Is.True);
			Assert.That(Advertises(colorizing, "IGray4Source"), Is.True);
			Assert.That(Advertises(colorizing, "IGray8Source"), Is.False);
			Assert.That(Advertises(colorizing, "IRgb24Source"), Is.False);

			var passthrough = CreateSource(display, false);
			Assert.That(Advertises(passthrough, "IGray2Source"), Is.True);
			Assert.That(Advertises(passthrough, "IGray4Source"), Is.True);
			Assert.That(Advertises(passthrough, "IGray8Source"), Is.True);
			Assert.That(Advertises(passthrough, "IRgb24Source"), Is.True);
		}

		[TestCase(DisplayFrameFormat.Dmd8, 128 * 16, 128, 16)]
		[TestCase(DisplayFrameFormat.Dmd8, 128 * 32, 128, 32)]
		[TestCase(DisplayFrameFormat.Dmd8, 192 * 64, 192, 64)]
		[TestCase(DisplayFrameFormat.Dmd8, 256 * 64, 256, 64)]
		[TestCase(DisplayFrameFormat.Dmd24, 128 * 32 * 3, 128, 32)]
		public void BridgeInfersSupportedStandardFrameSizes(DisplayFrameFormat format, int length,
			int width, int height)
		{
			var frame = new DisplayFrameData("dmd0", format, new byte[length]);

			Assert.That(DmdBridgePlayer.TryInferDisplayConfig(frame, out var display), Is.True);
			Assert.That(display.Id, Is.EqualTo(frame.Id));
			Assert.That(display.Width, Is.EqualTo(width));
			Assert.That(display.Height, Is.EqualTo(height));
		}

		[Test]
		public void BridgeNeedsAnnouncementForNonStandardOrMalformedFrames()
		{
			Assert.That(DmdBridgePlayer.TryInferDisplayConfig(
				new DisplayFrameData("dmd0", DisplayFrameFormat.Dmd8, new byte[140 * 36]), out _), Is.False);
			Assert.That(DmdBridgePlayer.TryInferDisplayConfig(
				new DisplayFrameData("dmd0", DisplayFrameFormat.Dmd24, new byte[128 * 32 * 3 + 1]), out _),
				Is.False);
		}

		[TestCase(DisplayFrameFormat.Dmd2, true)]
		[TestCase(DisplayFrameFormat.Dmd4, true)]
		[TestCase(DisplayFrameFormat.Dmd8, false)]
		[TestCase(DisplayFrameFormat.Dmd24, false)]
		[TestCase(DisplayFrameFormat.Segment, false)]
		public void ReportsFormatsSupportedByTheColorizationSource(DisplayFrameFormat format, bool supported)
		{
			Assert.That(DmdColorizationPolicy.SupportsColorization(format), Is.EqualTo(supported));
		}

		[Test]
		public void Rgb24LatchesStickyBypassAndWarnsOnce()
		{
			var policy = new DmdColorizationPolicy();
			policy.SelectDisplay(new DisplayConfig("dmd0", 128, 32));

			Assert.That(policy.ObserveFrame(DisplayFrameFormat.Dmd24, true, out var firstWarning),
				Is.EqualTo(DmdColorizationPipelineAction.Bypass));
			Assert.That(policy.BypassColorization, Is.True);
			Assert.That(policy.AwaitingSupportedColorizedFrame, Is.False);
			Assert.That(firstWarning, Is.True);
			Assert.That(policy.ObserveFrame(DisplayFrameFormat.Dmd4, false, out var secondWarning),
				Is.EqualTo(DmdColorizationPipelineAction.None));
			Assert.That(policy.BypassColorization, Is.True);
			Assert.That(secondWarning, Is.False);
		}

		[Test]
		public void TransientDmd8BypassKeepsPreferenceAndRestoresOnSupportedFrame()
		{
			var policy = new DmdColorizationPolicy();
			policy.SelectDisplay(new DisplayConfig("dmd0", 128, 32));

			Assert.That(policy.ObserveFrame(DisplayFrameFormat.Dmd8, true, out var warning),
				Is.EqualTo(DmdColorizationPipelineAction.Bypass));
			Assert.That(warning, Is.True);
			Assert.That(policy.BypassColorization, Is.True);
			Assert.That(policy.AwaitingSupportedColorizedFrame, Is.True);

			Assert.That(policy.ObserveFrame(DisplayFrameFormat.Dmd4, false, out warning),
				Is.EqualTo(DmdColorizationPipelineAction.Restore));
			Assert.That(warning, Is.False);
			Assert.That(policy.BypassColorization, Is.False);
			Assert.That(policy.AwaitingSupportedColorizedFrame, Is.False);
		}

		[Test]
		public void BridgeKeepsRequestingColorizableFramesDuringTransientBypass()
		{
			var policy = new DmdColorizationPolicy();
			policy.SelectDisplay(new DisplayConfig("dmd0", 128, 32));
			policy.ObserveFrame(DisplayFrameFormat.Dmd8, true, out _);

			Assert.That(DmdBridgePlayer.PreferredSourceFormat(false, policy),
				Is.EqualTo(DisplayFrameFormat.Dmd4));

			policy.SelectDisplay(new DisplayConfig("dmd1", 128, 32));
			policy.ObserveFrame(DisplayFrameFormat.Dmd24, true, out _);
			Assert.That(DmdBridgePlayer.PreferredSourceFormat(false, policy),
				Is.EqualTo(DisplayFrameFormat.Dmd8));
		}

		[Test]
		public void IdenticalTopologyReannouncementPreservesBypass()
		{
			var policy = new DmdColorizationPolicy();
			policy.SelectDisplay(new DisplayConfig("DMD0", 140, 36, true, Color.red, Color.black));
			policy.ObserveFrame(DisplayFrameFormat.Dmd24, true, out _);

			policy.SelectDisplay(new DisplayConfig("dmd0", 140, 36, true, Color.green, Color.white));

			Assert.That(policy.BypassColorization, Is.True);
			policy.ObserveFrame(DisplayFrameFormat.Dmd8, false, out var shouldWarn);
			Assert.That(shouldWarn, Is.False);
		}

		[Test]
		public void ChangedDisplayTopologyResetsBypassAndWarning()
		{
			AssertTopologyChangeResets(new DisplayConfig("dmd1", 140, 36, true));
			AssertTopologyChangeResets(new DisplayConfig("dmd0", 141, 36, true));
			AssertTopologyChangeResets(new DisplayConfig("dmd0", 140, 37, true));
			AssertTopologyChangeResets(new DisplayConfig("dmd0", 140, 36, false));
		}

		private static void AssertTopologyChangeResets(DisplayConfig changed)
		{
			var policy = new DmdColorizationPolicy();
			policy.SelectDisplay(new DisplayConfig("dmd0", 140, 36, true));
			policy.ObserveFrame(DisplayFrameFormat.Dmd24, true, out _);

			policy.SelectDisplay(changed);

			Assert.That(policy.BypassColorization, Is.False);
			policy.ObserveFrame(DisplayFrameFormat.Dmd24, true, out var shouldWarn);
			Assert.That(shouldWarn, Is.True);
		}

		private static bool Advertises(object source, string interfaceName)
		{
			return source.GetType().GetInterfaces().Any(type => type.Name == interfaceName);
		}

		private static object CreateSource(DisplayConfig display, bool colorize)
		{
			var create = typeof(VpeGleSource).GetMethod("Create", BindingFlags.Static | BindingFlags.Public);
			Assert.That(create, Is.Not.Null);
			return create.Invoke(null, new object[] { display, colorize });
		}
	}
}
