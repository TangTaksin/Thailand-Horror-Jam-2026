// using UnityEditor;
// using System;
// using System.Runtime.InteropServices;

// namespace FullscreenEditor.Windows {
//     internal static class NativeFullscreenHookWin {
//         [InitializeOnLoadMethod]
//         private static void Init() {
//             if (!FullscreenUtility.IsWindows)
//                 return;

//             FullscreenCallbacks.afterFullscreenOpen += (fs) => {
//                 After.Frames(1, ()=> ForceFullscreen(GetForegroundWindow()));
//             };
//         }

//         [DllImport("user32.dll")]
//         private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

//         [DllImport("user32.dll")]
//         private static extern IntPtr GetForegroundWindow();

//         // Constants for window positioning
//         private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
//         private const uint SWP_NOMOVE = 0x0002;
//         private const uint SWP_NOSIZE = 0x0001;
//         private const uint SWP_SHOWWINDOW = 0x0040;

//         public static void ForceFullscreen(IntPtr hWnd) {
//             SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE |  SWP_SHOWWINDOW);
//         }
//     }
// }
