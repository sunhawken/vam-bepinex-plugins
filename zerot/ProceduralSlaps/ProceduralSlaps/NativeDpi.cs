using System;
using System.Runtime.InteropServices;

namespace ProceduralSlaps;

internal static class NativeDpi
{
	[DllImport("user32.dll")]
	private static extern IntPtr GetActiveWindow();

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);

	public static float TryGetScale()
	{
		try
		{
			IntPtr activeWindow = GetActiveWindow();
			if (activeWindow == IntPtr.Zero)
			{
				return 0f;
			}
			uint dpiForWindow = GetDpiForWindow(activeWindow);
			return (dpiForWindow >= 48) ? ((float)dpiForWindow / 96f) : 0f;
		}
		catch (DllNotFoundException)
		{
			return 0f;
		}
		catch (EntryPointNotFoundException)
		{
			return 0f;
		}
		catch
		{
			return 0f;
		}
	}
}
