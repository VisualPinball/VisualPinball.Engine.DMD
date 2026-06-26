using System;
using LibDmd.Output;
using NLog;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	internal static class NativeWindowDestinationFactory
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static IDestination TryCreate(DisplayConfig display)
		{
			var destinationType = Type.GetType("LibDmd.Output.NativeWindow.NativeWindowDestination, LibDmd.Core");
			if (destinationType == null) {
				Logger.Warn("[DMD] Native-window backend type was not found in LibDmd.Core.");
				return null;
			}

			try {
				var destination = Activator.CreateInstance(destinationType, display.Width, display.Height) as IDestination;
				if (destination == null) {
					Logger.Warn("[DMD] Native-window backend did not create a destination.");
				} else {
					Logger.Info($"[DMD] Native-window destination created for {display.Width}x{display.Height}.");
				}

				return destination;
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Native-window backend exists but could not be instantiated.");
				return null;
			}
		}
	}
}
