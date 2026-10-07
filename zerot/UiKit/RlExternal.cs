using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace ZeroT.UiKit
{
	// A real, separate Windows window (own title bar, taskbar button, resizable, movable) that shows a picture of a uGUI
	// canvas and forwards mouse input back to it. The window lives on its own thread with its own message loop; the Unity
	// side hands it finished BGRA frames and reads queued mouse events.
	internal sealed class RlExternalWindow
	{
		internal struct Mouse
		{
			public int Kind;   // 0 move, 1 left down, 2 left up, 3 wheel
			public int X;
			public int Y;      // client pixels, origin top-left
			public int Wheel;
		}

		private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WNDCLASS
		{
			public uint style;
			public WndProcDelegate lpfnWndProc;
			public int cbClsExtra;
			public int cbWndExtra;
			public IntPtr hInstance;
			public IntPtr hIcon;
			public IntPtr hCursor;
			public IntPtr hbrBackground;
			public string lpszMenuName;
			public string lpszClassName;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT { public int X; public int Y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

		[StructLayout(LayoutKind.Sequential)]
		private struct MSG
		{
			public IntPtr hwnd;
			public uint message;
			public IntPtr wParam;
			public IntPtr lParam;
			public uint time;
			public POINT pt;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PAINTSTRUCT
		{
			public IntPtr hdc;
			public int fErase;
			public RECT rcPaint;
			public int fRestore;
			public int fIncUpdate;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
			public byte[] rgbReserved;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAPINFOHEADER
		{
			public uint biSize;
			public int biWidth;
			public int biHeight;
			public ushort biPlanes;
			public ushort biBitCount;
			public uint biCompression;
			public uint biSizeImage;
			public int biXPelsPerMeter;
			public int biYPelsPerMeter;
			public uint biClrUsed;
			public uint biClrImportant;
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern ushort RegisterClassW(ref WNDCLASS wc);
		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
		[DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
		[DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
		[DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
		[DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
		[DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
		[DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
		[DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
		[DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
		[DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT r);
		[DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
		[DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
		[DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
		[DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
		[DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
		[DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
		[DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
		[DllImport("user32.dll")] private static extern bool ReleaseCapture();
		[DllImport("user32.dll")] private static extern bool SetWindowTextW(IntPtr hwnd, string text);
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
		[DllImport("gdi32.dll")]
		private static extern int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, uint w, uint h, int xSrc, int ySrc, uint startScan, uint cLines, byte[] bits, ref BITMAPINFOHEADER bmi, uint colorUse);

		private const uint WM_DESTROY = 0x0002;
		private const uint WM_SIZE = 0x0005;
		private const uint WM_PAINT = 0x000F;
		private const uint WM_CLOSE = 0x0010;
		private const uint WM_ERASEBKGND = 0x0014;
		private const uint WM_MOUSEMOVE = 0x0200;
		private const uint WM_LBUTTONDOWN = 0x0201;
		private const uint WM_LBUTTONUP = 0x0202;
		private const uint WM_MOUSEWHEEL = 0x020A;
		private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
		private const uint WS_VISIBLE = 0x10000000;

		private static readonly WndProcDelegate proc = StaticProc;
		private static readonly Dictionary<IntPtr, RlExternalWindow> windows = new Dictionary<IntPtr, RlExternalWindow>();
		private static bool classRegistered;
		private static int counter;

		private IntPtr hwnd;
		private Thread thread;
		private readonly object bufferLock = new object();
		private byte[] buffer;
		private int bufferW;
		private int bufferH;
		private readonly Queue<Mouse> input = new Queue<Mouse>();
		private volatile int clientW;
		private volatile int clientH;
		private volatile bool closed;
		private volatile bool created;
		private string title;
		private int startX, startY, startW, startH;

		internal RlExternalWindow(string title, int x, int y, int w, int h)
		{
			this.title = title;
			startX = x;
			startY = y;
			startW = w;
			startH = h;
			clientW = w;
			clientH = h;
			thread = new Thread(Run);
			thread.IsBackground = true;
			thread.Name = "RlExternalWindow";
			thread.Start();
		}

		internal bool Closed { get { return closed; } }
		internal bool Created { get { return created; } }
		internal int ClientW { get { return clientW; } }
		internal int ClientH { get { return clientH; } }

		internal bool Minimized
		{
			get { return hwnd != IntPtr.Zero && IsIconic(hwnd); }
		}

		internal string RectString()
		{
			RECT r;
			if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r))
			{
				return "";
			}
			return r.Left + "," + r.Top + "," + (r.Right - r.Left) + "," + (r.Bottom - r.Top);
		}

		internal void SetTitle(string t)
		{
			if (hwnd != IntPtr.Zero)
			{
				SetWindowTextW(hwnd, t);
			}
		}

		internal void SetVisible(bool visible)
		{
			if (hwnd != IntPtr.Zero)
			{
				ShowWindow(hwnd, visible ? 5 : 0);
			}
		}

		internal void Close()
		{
			if (hwnd != IntPtr.Zero && !closed)
			{
				PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
			}
		}

		internal bool TryDequeue(out Mouse m)
		{
			lock (input)
			{
				if (input.Count > 0)
				{
					m = input.Dequeue();
					return true;
				}
			}
			m = default(Mouse);
			return false;
		}

		// rgba: Unity RGBA32 bytes, rows bottom-up (which is also the layout a positive-height DIB expects).
		internal void Submit(byte[] rgba, int w, int h)
		{
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			lock (bufferLock)
			{
				int n = w * h * 4;
				if (buffer == null || buffer.Length != n)
				{
					buffer = new byte[n];
				}
				for (int i = 0; i < n; i += 4)
				{
					buffer[i] = rgba[i + 2];
					buffer[i + 1] = rgba[i + 1];
					buffer[i + 2] = rgba[i];
					buffer[i + 3] = 255;
				}
				bufferW = w;
				bufferH = h;
			}
			InvalidateRect(hwnd, IntPtr.Zero, false);
		}

		// ---------------------------------------------------------------- window thread

		private void Run()
		{
			try
			{
				IntPtr inst = GetModuleHandleW(null);
				lock (windows)
				{
					if (!classRegistered)
					{
						WNDCLASS wc = new WNDCLASS();
						wc.lpfnWndProc = proc;
						wc.hInstance = inst;
						wc.hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512));
						wc.lpszClassName = "ZeroT.RlExternal";
						RegisterClassW(ref wc);
						classRegistered = true;
					}
				}
				// Client size is what we want to show, so ask for a slightly larger outer window.
				int outerW = startW + 16;
				int outerH = startH + 39;
				IntPtr h = CreateWindowExW(0u, "ZeroT.RlExternal", title, WS_OVERLAPPEDWINDOW | WS_VISIBLE, startX, startY, outerW, outerH, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
				if (h == IntPtr.Zero)
				{
					closed = true;
					return;
				}
				lock (windows)
				{
					windows[h] = this;
				}
				hwnd = h;
				RECT cr;
				if (GetClientRect(h, out cr))
				{
					clientW = Math.Max(50, cr.Right - cr.Left);
					clientH = Math.Max(50, cr.Bottom - cr.Top);
				}
				created = true;
				MSG msg;
				while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
				{
					TranslateMessage(ref msg);
					DispatchMessageW(ref msg);
				}
			}
			catch
			{
			}
			closed = true;
		}

		private static int Lo(IntPtr v)
		{
			return (short)((long)v & 0xFFFF);
		}

		private static int Hi(IntPtr v)
		{
			return (short)(((long)v >> 16) & 0xFFFF);
		}

		private void Enqueue(int kind, int x, int y, int wheel)
		{
			Mouse m = new Mouse();
			m.Kind = kind;
			m.X = x;
			m.Y = y;
			m.Wheel = wheel;
			lock (input)
			{
				if (kind == 0 && input.Count > 0)
				{
					// coalesce pending moves
					Mouse[] all = input.ToArray();
					if (all[all.Length - 1].Kind == 0)
					{
						all[all.Length - 1] = m;
						input.Clear();
						for (int i = 0; i < all.Length; i++)
						{
							input.Enqueue(all[i]);
						}
						return;
					}
				}
				if (input.Count < 256)
				{
					input.Enqueue(m);
				}
			}
		}

		private static IntPtr StaticProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
		{
			RlExternalWindow w;
			lock (windows)
			{
				windows.TryGetValue(hwnd, out w);
			}
			if (w == null)
			{
				return DefWindowProcW(hwnd, msg, wParam, lParam);
			}
			switch (msg)
			{
				case WM_ERASEBKGND:
					return new IntPtr(1);
				case WM_SIZE:
					w.clientW = Math.Max(50, Lo(lParam));
					w.clientH = Math.Max(50, Hi(lParam));
					return IntPtr.Zero;
				case WM_PAINT:
					w.Paint(hwnd);
					return IntPtr.Zero;
				case WM_MOUSEMOVE:
					w.Enqueue(0, Lo(lParam), Hi(lParam), 0);
					return IntPtr.Zero;
				case WM_LBUTTONDOWN:
					SetCapture(hwnd);
					w.Enqueue(1, Lo(lParam), Hi(lParam), 0);
					return IntPtr.Zero;
				case WM_LBUTTONUP:
					ReleaseCapture();
					w.Enqueue(2, Lo(lParam), Hi(lParam), 0);
					return IntPtr.Zero;
				case WM_MOUSEWHEEL:
					w.Enqueue(3, 0, 0, Hi(wParam));
					return IntPtr.Zero;
				case WM_CLOSE:
					w.closed = true;
					DestroyWindow(hwnd);
					return IntPtr.Zero;
				case WM_DESTROY:
					lock (windows)
					{
						windows.Remove(hwnd);
					}
					PostQuitMessage(0);
					return IntPtr.Zero;
			}
			return DefWindowProcW(hwnd, msg, wParam, lParam);
		}

		private void Paint(IntPtr hwnd)
		{
			PAINTSTRUCT ps;
			IntPtr dc = BeginPaint(hwnd, out ps);
			try
			{
				lock (bufferLock)
				{
					if (buffer != null && bufferW > 0 && bufferH > 0)
					{
						BITMAPINFOHEADER bmi = new BITMAPINFOHEADER();
						bmi.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
						bmi.biWidth = bufferW;
						bmi.biHeight = bufferH;
						bmi.biPlanes = 1;
						bmi.biBitCount = 32;
						bmi.biCompression = 0;
						SetDIBitsToDevice(dc, 0, 0, (uint)bufferW, (uint)bufferH, 0, 0, 0, (uint)bufferH, buffer, ref bmi, 0);
					}
				}
			}
			finally
			{
				EndPaint(hwnd, ref ps);
			}
		}
	}
}
