using UnityEditor;

namespace FullscreenEditor.MacOS {

    /// <summary>Keeps the macOS fullscreen windows borderless while they are open.</summary>
    /// <remarks>
    /// The container is shown with <c>ShowMode.PopupMenu</c> (opaque, but with a shadow/frame/rounded
    /// corners). <see cref="FullscreenContainer.CreateFullscreenViewPyramid"/> strips those right
    /// after creating the window. <see cref="Cocoa.StripWindowDecorations"/> is a no-op once the
    /// window is already borderless, so this is cheap.
    /// </remarks>
    internal static class NativeDecorationFix {

        [InitializeOnLoadMethod]
        private static void Init() {
            if (!FullscreenUtility.IsMacOS)
                return;


            // EditorApplication.update += ReapplyAll;
            FullscreenCallbacks.afterFullscreenOpen += (fs) => {
                ReapplyAll();
            };
        }

        private static void ReapplyAll() {
            var all = Fullscreen.GetAllFullscreen();

            for (var i = 0; i < all.Length; i++)
                ApplyNow(all[i]);
        }

        private static void ApplyNow(FullscreenContainer fs) {
            if (fs == null)
                return;

            var cw = fs.m_dst.Container;

            if (!cw || !cw.HasProperty("title"))
                return;

            var title = cw.GetPropertyValue<string>("title");

            if (!string.IsNullOrEmpty(title))
                Cocoa.StripWindowDecorations(title, false);
        }
    }
}
