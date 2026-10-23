using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MCTL.Core;

/// <summary>Windows 10/11 亚克力（Acrylic）与云母效果封装。</summary>
public static class AcrylicHelper
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentAcrylicBlurBehind = 4;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>应用亚克力着色。alphaPercent 越大越不透明。</summary>
    public static void Apply(Window window, bool enabled, int alphaPercent, Color tint)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Win11 圆角
            int corner = 2;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            // 深色标题栏
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            if (!enabled)
            {
                SetAccent(hwnd, AccentDisabled, 0);
                return;
            }

            // Win11 系统背景（失败无副作用，Win10 会返回错误码）
            int backdrop = 3; // DWMSBT_TRANSIENTWINDOW
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

            int alpha = Math.Clamp(alphaPercent, 0, 100) * 255 / 100;
            int color = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R; // AABBGGRR
            SetAccent(hwnd, AccentAcrylicBlurBehind, color);
        }
        catch (Exception ex)
        {
            Logger.Warn("亚克力效果应用失败：" + ex.Message);
        }
    }

    private static void SetAccent(IntPtr hwnd, int state, int gradientColor)
    {
        var policy = new AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = gradientColor };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
