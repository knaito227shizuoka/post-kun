using System.Runtime.InteropServices;

namespace PostKun;

internal static class Program
{
    /// <summary>メインウィンドウのタイトル。2個目の起動時に既存ウィンドウを探す鍵にもなるので変えないこと。</summary>
    public const string WindowTitle = "post-kun";

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main()
    {
        // 多重起動防止。2個目は既存ウィンドウを前面に出して終了する。
        using var mutex = new Mutex(true, @"Local\post-kun", out var isFirstInstance);
        if (!isFirstInstance)
        {
            BringExistingWindowToFront();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void BringExistingWindowToFront()
    {
        var hWnd = FindWindow(null, WindowTitle);
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
    }
}
