using SpecsView.App.Native;

namespace SpecsView.App.Overlay;

internal static class LayeredWindow
{
    /// <summary>Pushes a 32bpp ARGB bitmap to a WS_EX_LAYERED window with per-pixel alpha.</summary>
    public static void Update(nint hwnd, Bitmap bitmap, Point location)
    {
        var screenDc = User32.GetDC(0);
        var memDc = Gdi32.CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = Gdi32.SelectObject(memDc, hBitmap);
        try
        {
            var size = new User32.Size { Cx = bitmap.Width, Cy = bitmap.Height };
            var source = new User32.Point();
            var destination = new User32.Point { X = location.X, Y = location.Y };
            var blend = new User32.BlendFunction
            {
                BlendOp = User32.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = User32.AcSrcAlpha,
            };
            User32.UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size, memDc, ref source, 0, ref blend, User32.UlwAlpha);
        }
        finally
        {
            Gdi32.SelectObject(memDc, previous);
            Gdi32.DeleteObject(hBitmap);
            Gdi32.DeleteDC(memDc);
            User32.ReleaseDC(0, screenDc);
        }
    }
}
