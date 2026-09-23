using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CatPrint.Views
{
    /// <summary>
    /// Тёмный/светлый заголовок окна через DWM.
    /// Родные кнопки/ресайз/снап остаются на месте.
    /// </summary>
    public static class DarkTitleBar
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int value, int size);

        public static void Apply(Window window, bool dark)
        {
            if (window == null)
                return;

            void DoApply()
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero)
                        return;
                    int value = dark ? 1 : 0;
                    DwmSetWindowAttribute(
                        hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                        ref value, sizeof(int));
                }
                catch { }
            }

            if (!window.IsLoaded)
                window.SourceInitialized += (s, e) => DoApply();
            else
                DoApply();
        }
    }
}
