using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SchoolBell.App;

internal static class Native
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    /// <summary>Не даємо Windows заснути, поки програма працює (екран при цьому може гаснути).</summary>
    public static void PreventSleep()
    {
        try { SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED); }
        catch { /* не критично */ }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr hwndCallback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

    /// <summary>Виконує MCI-команду. Повертає 0 при успіху.</summary>
    public static int Mci(string command, out string result)
    {
        var sb = new StringBuilder(256);
        int code;
        try { code = MciSendString(command, sb, sb.Capacity, IntPtr.Zero); }
        catch (Exception ex) { result = ex.Message; return -1; }
        result = sb.ToString();
        return code;
    }

    public static string MciError(int code)
    {
        try
        {
            var sb = new StringBuilder(256);
            return MciGetErrorString(code, sb, sb.Capacity) ? sb.ToString() : $"MCI error {code}";
        }
        catch
        {
            return $"MCI error {code}";
        }
    }
}
