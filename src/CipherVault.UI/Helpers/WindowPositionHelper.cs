namespace CipherVault.UI.Helpers;

using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

public static class WindowPositionHelper
{
    /// <summary>
    /// Centers the given window on the monitor where the mouse cursor currently resides.
    /// </summary>
    public static void CenterOnCurrentScreen(Window window)
    {
        try
        {
            var mousePosition = GetMousePosition();
            var screen = Screen.FromPoint(mousePosition);
            var workingArea = screen.WorkingArea;

            var helper = new WindowInteropHelper(window);
            var source = PresentationSource.FromVisual(window);

            double dpiX = 1.0, dpiY = 1.0;
            if (source?.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformToDevice;
                dpiX = matrix.M11;
                dpiY = matrix.M22;
            }

            var screenWidth = workingArea.Width / dpiX;
            var screenHeight = workingArea.Height / dpiY;
            var screenLeft = workingArea.Left / dpiX;
            var screenTop = workingArea.Top / dpiY;

            window.Left = screenLeft + (screenWidth - window.Width) / 2;
            window.Top = screenTop + (screenHeight - window.Height) / 2;
        }
        catch
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static System.Drawing.Point GetMousePosition()
    {
        var point = System.Windows.Forms.Cursor.Position;
        return new System.Drawing.Point(point.X, point.Y);
    }
}
