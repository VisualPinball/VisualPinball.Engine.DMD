using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VisualPinball.Plugins;
using VisualPinball.Engine.DMD.Player;

namespace VisualPinball.Engine.DMD.Tests
{
	public sealed class DmdDeviceProfileTests
	{
		[TestCase(" com003 ", "COM3")]
		[TestCase(@"\\.\COM3", "COM3")]
		[TestCase("COM0", "COM0")]
		[TestCase("", "")]
		public void CanonicalPortAliasesReserveTheSameDevice(string input, string expected)
		{
			Assert.That(DmdDeviceProfiles.CanonicalPort(input), Is.EqualTo(expected));
		}
		[Test]
		public void ProfilesUseOnlyHardwareFieldsAndReserveSerialAndFamilyAcrossDrivers()
		{
			var settings = new PluginSettingsSnapshot(1, JObject.Parse("{\"ZeDmdPort\":\"com003\",\"PinDmd3Port\":\"COM3\",\"Secret\":\"hidden\",\"ZeDmdBrightness\":80}"));
			var profiles = DmdDeviceProfiles.Create(settings, "windows");
			Assert.That(profiles.Count, Is.EqualTo(6));
			var zed = profiles.Single(p => p.Id.EndsWith("/zedmd")); var pin = profiles.Single(p => p.Id.EndsWith("/pindmd3"));
			Assert.That(zed.LeaseKey, Is.EqualTo("serial:COM3")); Assert.That(pin.LeaseKey, Is.EqualTo(zed.LeaseKey));
			Assert.That(zed.FamilyLeaseKey, Is.EqualTo("usb:zedmd:auto")); Assert.That(zed.Configuration, Does.Not.Contain("hidden"));
			settings.Values["ZeDmdPort"] = "";
			Assert.That(DmdDeviceProfiles.Create(settings, "windows")[0].LeaseKey, Is.EqualTo(zed.FamilyLeaseKey));
			Assert.That(zed.Configuration, Does.Contain("COM3"), "Profile payload must be detached.");
		}
		[Test]
		public void UnsupportedPlatformAndUnboundedPortsAreUnavailableWithoutProbing()
		{
			var settings = new PluginSettingsSnapshot(1);
			Assert.That(DmdDeviceProfiles.Create(settings, "macos").All(p => !p.Available), Is.True);
			settings.Values["ZeDmdPort"] = new string('x', 257);
			var profile = DmdDeviceProfiles.Create(settings, "windows")[0];
			Assert.That(profile.Available, Is.False); Assert.That(profile.Reason, Does.Contain("long"));
		}
	}
}
