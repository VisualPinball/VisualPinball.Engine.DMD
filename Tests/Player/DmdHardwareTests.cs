using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VisualPinball.Plugins;
using VisualPinball.Engine.DMD.Player;

namespace VisualPinball.Engine.DMD.Tests
{
	public sealed class DmdHardwareTests
	{
		[TestCase(" com003 ", "COM3")]
		[TestCase(@"\\.\COM3", "COM3")]
		[TestCase("COM0", "COM0")]
		[TestCase("", "")]
		public void CanonicalPortAliasesReserveTheSameDevice(string input, string expected)
		{
			Assert.That(DmdHardware.CanonicalPort(input), Is.EqualTo(expected));
		}
		[Test]
		public void LeaseKeysReserveTheSerialPortAndTheDriverFamily()
		{
			var settings = new PluginSettingsSnapshot(1, JObject.Parse("{\"ZeDmdPort\":\"com003\",\"PinDmd3Port\":\"COM3\"}"));
			var zed = DmdHardware.LeaseKeys(Driver("zedmd"), settings); var pin = DmdHardware.LeaseKeys(Driver("pindmd3"), settings);
			Assert.That(zed, Is.EqualTo(new[] { "serial:COM3", "usb:zedmd:auto" }));
			Assert.That(pin[0], Is.EqualTo(zed[0]), "Two drivers on one port reserve the same device.");
			Assert.That(DmdHardware.Label(Driver("zedmd"), settings), Is.EqualTo("ZeDMD (COM3)"));
			settings.Values["ZeDmdPort"] = "";
			Assert.That(DmdHardware.LeaseKeys(Driver("zedmd"), settings), Is.EqualTo(new[] { "usb:zedmd:auto" }));
			Assert.That(DmdHardware.LeaseKeys(Driver("pin2dmd"), settings), Is.EqualTo(new[] { "usb:pin2dmd:auto" }));
		}
		[Test]
		public void UnboundedPortsReserveOnlyTheFamily()
		{
			var settings = new PluginSettingsSnapshot(1);
			settings.Values["ZeDmdPort"] = new string('x', 257);
			Assert.That(DmdHardware.LeaseKeys(Driver("zedmd"), settings), Is.EqualTo(new[] { "usb:zedmd:auto" }));
			Assert.That(DmdHardware.Label(Driver("zedmd"), settings), Is.EqualTo("ZeDMD"));
		}
		private static DmdHardware.Driver Driver(string id) => System.Array.Find(DmdHardware.Drivers, driver => driver.Id == id);
	}
}
