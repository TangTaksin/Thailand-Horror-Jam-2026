using System;
using System.Runtime.InteropServices;

namespace FullscreenEditor.MacOS {

    /// <summary>Minimal Objective-C runtime / AppKit interop.</summary>
    /// <remarks>
    /// We show the fullscreen container with <c>ShowMode.PopupMenu</c> because that mode is created
    /// as an opaque native window (unlike <c>ShowMode.NoShadow</c>, which recent Unity versions make
    /// transparent since it doubles as the drag-preview window). PopupMenu however comes with a
    /// shadow, rounded corners and a frame, so <see cref="StripWindowDecorations"/> grabs the
    /// underlying NSWindow and turns it borderless and shadowless to match the old NoShadow look.
    /// </remarks>
    internal static class Cocoa {

        private const string OBJC = "/usr/lib/libobjc.dylib";

        // NSWindowStyleMaskBorderless: no titlebar, no frame, square corners.
        private const long NSWindowStyleMaskBorderless = 0;

        // Sit above the menu bar (NSMainMenuWindowLevel = 24) and the Dock (kCGDockWindowLevel = 20)
        // so the window fully covers the screen, like the old NoShadow window did. NSStatusWindowLevel
        // (25) is the first level above the menu bar.
        private const long NSStatusWindowLevel = 25;

        [DllImport(OBJC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(OBJC)] private static extern IntPtr sel_registerName(string name);

        [DllImport(OBJC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr msg(IntPtr receiver, IntPtr selector);

        [DllImport(OBJC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr msg_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(OBJC, EntryPoint = "objc_msgSend")]
        private static extern void msg_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect {
            public double x, y, width, height;
        }

        // NSRect return: Apple Silicon returns it in registers (plain objc_msgSend); Intel returns
        // structs larger than 16 bytes via a hidden pointer (objc_msgSend_stret).
        [DllImport(OBJC, EntryPoint = "objc_msgSend")]
        private static extern CGRect msg_rect(IntPtr receiver, IntPtr selector);

        [DllImport(OBJC, EntryPoint = "objc_msgSend_stret")]
        private static extern void msg_rect_stret(out CGRect result, IntPtr receiver, IntPtr selector);

        // NSRect argument is passed the same way on both architectures.
        [DllImport(OBJC, EntryPoint = "objc_msgSend")]
        private static extern void msg_setFrame(IntPtr receiver, IntPtr selector, CGRect frame, [MarshalAs(UnmanagedType.I1)] bool display);

        private static readonly bool IsAppleSilicon =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        private static CGRect GetRect(IntPtr receiver, IntPtr selector) {
            if (IsAppleSilicon)
                return msg_rect(receiver, selector);

            msg_rect_stret(out var result, receiver, selector);
            return result;
        }

        private static IntPtr Sel(string name) {
            return sel_registerName(name);
        }

        /// <summary>Find the NSWindow whose title equals <paramref name="windowTitle"/> and strip its
        /// shadow, frame and rounded corners so it looks like a borderless fullscreen window.</summary>
        /// <param name="warnIfMissing">Log a warning when no matching window is found. Disable this when
        /// calling every frame to avoid spamming the console.</param>
        /// <returns>True if a matching window was found and patched.</returns>
        public static bool StripWindowDecorations(string windowTitle, bool warnIfMissing = true) {
            if (!FullscreenUtility.IsMacOS || string.IsNullOrEmpty(windowTitle))
                return false;

            try {
                var window = FindWindowByTitle(windowTitle);

                if (window != IntPtr.Zero) {
                    StripDecorations(window);
                    return true;
                }

                if (warnIfMissing)
                    Logger.Warning("Could not find NSWindow titled '{0}' to strip decorations", windowTitle);
            } catch (Exception e) {
                Logger.Warning("Failed to strip macOS window decorations: {0}", e);
            }

            return false;
        }

        private static IntPtr FindWindowByTitle(string windowTitle) {
            var app = msg(objc_getClass("NSApplication"), Sel("sharedApplication"));
            var windows = msg(app, Sel("windows"));
            var count = msg(windows, Sel("count")).ToInt64();

            var selObjectAtIndex = Sel("objectAtIndex:");
            var selTitle = Sel("title");
            var selUTF8 = Sel("UTF8String");

            for (var i = 0L; i < count; i++) {
                var window = msg_ptr(windows, selObjectAtIndex, new IntPtr(i));
                var title = msg(window, selTitle);

                if (title == IntPtr.Zero)
                    continue;

                var cstr = msg(title, selUTF8);
                var managed = cstr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(cstr);

                if (string.Equals(managed, windowTitle, StringComparison.Ordinal))
                    return window;
            }

            return IntPtr.Zero;
        }

        private static void StripDecorations(IntPtr window) {
            // Only assign when the value actually changes: setStyleMask: triggers a relayout, so doing
            // it unconditionally every frame would cause churn/flicker.
            if (msg(window, Sel("styleMask")).ToInt64() != NSWindowStyleMaskBorderless)
                msg_ptr(window, Sel("setStyleMask:"), new IntPtr(NSWindowStyleMaskBorderless));

            if (ReadBool(window, "hasShadow"))
                msg_bool(window, Sel("setHasShadow:"), false);

            if (!ReadBool(window, "isOpaque"))
                msg_bool(window, Sel("setOpaque:"), true);

            // Raise above the menu bar / Dock so the window covers the whole screen.
            if (msg(window, Sel("level")).ToInt64() < NSStatusWindowLevel)
                msg_ptr(window, Sel("setLevel:"), new IntPtr(NSStatusWindowLevel));

            CoverScreen(window);
        }

        // macOS clamps the window to the screen's *visible* frame (excluding the Dock and menu bar),
        // so even with a full-display rect the window stays clipped. Force it to the screen's full
        // frame instead. Applied only when it differs from the current frame to avoid relayout churn.
        private static void CoverScreen(IntPtr window) {
            var screen = msg(window, Sel("screen"));
            if (screen == IntPtr.Zero)
                return;

            var target = GetRect(screen, Sel("frame"));
            var current = GetRect(window, Sel("frame"));

            if (!RectsEqual(current, target))
                msg_setFrame(window, Sel("setFrame:display:"), target, true);
        }

        private static bool RectsEqual(CGRect a, CGRect b) {
            const double epsilon = 0.5;
            return Math.Abs(a.x - b.x) < epsilon
                && Math.Abs(a.y - b.y) < epsilon
                && Math.Abs(a.width - b.width) < epsilon
                && Math.Abs(a.height - b.height) < epsilon;
        }

        private static bool ReadBool(IntPtr receiver, string selector) {
            // BOOL is returned in the low byte of the result register.
            return (msg(receiver, Sel(selector)).ToInt64() & 0xFF) != 0;
        }
    }
}
