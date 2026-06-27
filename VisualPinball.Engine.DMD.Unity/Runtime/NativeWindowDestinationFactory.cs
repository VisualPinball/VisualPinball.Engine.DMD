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

		public static IDestination TryCreate(DisplayConfig display, DmdBridgeSettings settings)
		{
			var destinationType = Type.GetType("LibDmd.Output.NativeWindow.NativeWindowDestination, LibDmd.Core.Windows");
			if (destinationType == null) {
				Logger.Warn("[DMD] Native-window backend type was not found in LibDmd.Core.Windows.");
				return null;
			}

			try {
				var renderStyle = CreateRenderStyle(settings);
				var destination = Activator.CreateInstance(
					destinationType,
					display.Width,
					display.Height,
					settings.NativeWindowLeft,
					settings.NativeWindowTop,
					settings.NativeWindowWidth,
					settings.NativeWindowHeight,
					settings.NativeWindowStayOnTop,
					renderStyle) as IDestination;
				if (destination == null) {
					Logger.Warn("[DMD] Native-window backend did not create a destination.");
				} else {
					Logger.Info($"[DMD] Native-window destination created for {display.Width}x{display.Height} at {settings.NativeWindowLeft},{settings.NativeWindowTop} ({settings.NativeWindowWidth}x{settings.NativeWindowHeight}).");
				}

				return destination;
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Native-window backend exists but could not be instantiated.");
				return null;
			}
		}

		public static void TryConfigure(IDestination destination, DmdBridgeSettings settings)
		{
			if (destination == null || settings == null || destination.GetType().FullName != "LibDmd.Output.NativeWindow.NativeWindowDestination") {
				return;
			}

			try {
				destination.GetType()
					.GetMethod("ConfigureWindow")
					?.Invoke(destination, new object[] {
						settings.NativeWindowLeft,
						settings.NativeWindowTop,
						settings.NativeWindowWidth,
						settings.NativeWindowHeight,
						settings.NativeWindowStayOnTop
					});

				destination.GetType()
					.GetMethod("ConfigureRenderStyle")
					?.Invoke(destination, new[] { CreateRenderStyle(settings) });
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Native-window backend exists but could not be configured.");
			}
		}

		public static bool TryReadLayout(IDestination destination, out int left, out int top, out int width, out int height, out bool stayOnTop, out bool isMovingOrSizing)
		{
			left = 0;
			top = 0;
			width = 0;
			height = 0;
			stayOnTop = false;
			isMovingOrSizing = false;

			if (destination == null || destination.GetType().FullName != "LibDmd.Output.NativeWindow.NativeWindowDestination") {
				return false;
			}

			try {
				var type = destination.GetType();
				left = (int)type.GetProperty("WindowLeft").GetValue(destination);
				top = (int)type.GetProperty("WindowTop").GetValue(destination);
				width = (int)type.GetProperty("WindowWidth").GetValue(destination);
				height = (int)type.GetProperty("WindowHeight").GetValue(destination);
				stayOnTop = (bool)type.GetProperty("WindowStayOnTop").GetValue(destination);
				isMovingOrSizing = (bool)type.GetProperty("IsMovingOrSizing").GetValue(destination);
				return true;
			} catch (Exception exception) {
				Logger.Warn(exception, "[DMD] Native-window backend exists but its layout could not be read.");
				return false;
			}
		}

		private static object CreateRenderStyle(DmdBridgeSettings settings)
		{
			var styleType = Type.GetType("LibDmd.Output.Virtual.Dmd.VirtualDmdRenderStyle, LibDmd.Core.Windows");
			if (styleType == null) {
				return null;
			}

			var style = Activator.CreateInstance(styleType);
			Set(styleType, style, "DotSize", settings.DotSize);
			Set(styleType, style, "DotRounding", settings.DotRounding);
			Set(styleType, style, "DotSharpness", settings.DotSharpness);
			Set(styleType, style, "UnlitDotR", settings.UnlitDot.r);
			Set(styleType, style, "UnlitDotG", settings.UnlitDot.g);
			Set(styleType, style, "UnlitDotB", settings.UnlitDot.b);
			Set(styleType, style, "Brightness", settings.Brightness);
			Set(styleType, style, "DotGlow", settings.DotGlow);
			Set(styleType, style, "BackGlow", settings.BackGlow);
			Set(styleType, style, "Gamma", settings.Gamma);
			Set(styleType, style, "GlassR", settings.GlassColor.r);
			Set(styleType, style, "GlassG", settings.GlassColor.g);
			Set(styleType, style, "GlassB", settings.GlassColor.b);
			Set(styleType, style, "GlassLighting", settings.GlassLighting);
			return style;
		}

		private static void Set(Type type, object instance, string property, float value)
		{
			type.GetProperty(property)?.SetValue(instance, value);
		}
	}
}
