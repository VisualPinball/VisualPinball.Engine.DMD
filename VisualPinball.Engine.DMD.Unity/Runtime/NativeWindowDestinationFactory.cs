using LibDmd.Output;
using LibDmd.Output.NativeWindow;
using NLog;
using VisualPinball.Unity;
using Logger = NLog.Logger;

namespace VisualPinball.Engine.DMD.Unity
{
	/// <summary>
	/// Creates the cross-platform native-window destination through the strongly-typed
	/// <see cref="NativeDmdWindow"/> factory in LibDmd.Core. The platform implementation
	/// (e.g. LibDmd.Core.Windows) is resolved there; if it isn't present for the current OS,
	/// <see cref="NativeDmdWindow.TryCreate"/> returns null and the window is simply skipped.
	/// </summary>
	internal static class NativeWindowDestinationFactory
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static IDestination TryCreate(DisplayConfig display, DmdBridgeSettings settings)
		{
			var window = NativeDmdWindow.TryCreate(display.Width, display.Height, CreateLayout(settings), CreateStyle(settings));
			if (window == null) {
				return null;
			}

			Logger.Info($"[DMD] Native-window destination created for {display.Width}x{display.Height} at {settings.NativeWindowLeft},{settings.NativeWindowTop} ({settings.NativeWindowWidth}x{settings.NativeWindowHeight}).");
			return window;
		}

		public static DmdWindowLayout CreateLayout(DmdBridgeSettings settings)
		{
			return new DmdWindowLayout(
				settings.NativeWindowLeft,
				settings.NativeWindowTop,
				settings.NativeWindowWidth,
				settings.NativeWindowHeight,
				settings.NativeWindowStayOnTop);
		}

		public static DmdWindowStyle CreateStyle(DmdBridgeSettings settings)
		{
			return new DmdWindowStyle {
				DotSize = settings.DotSize,
				DotRounding = settings.DotRounding,
				DotSharpness = settings.DotSharpness,
				UnlitDotR = settings.UnlitDot.r,
				UnlitDotG = settings.UnlitDot.g,
				UnlitDotB = settings.UnlitDot.b,
				Brightness = settings.Brightness,
				DotGlow = settings.DotGlow,
				BackGlow = settings.BackGlow,
				Gamma = settings.Gamma,
				GlassR = settings.GlassColor.r,
				GlassG = settings.GlassColor.g,
				GlassB = settings.GlassColor.b,
				GlassLighting = settings.GlassLighting,
			};
		}
	}
}
