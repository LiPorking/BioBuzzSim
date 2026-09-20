using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Puts the build version in the player's title bar: "BIOBUZZ Simulator v1.1".
//
// PlayerSettings.productName is deliberately left alone. It is what Unity uses to build
// Application.persistentDataPath (%USERPROFILE%\AppData\LocalLow\BIOBUZZ Sim\BIOBUZZ
// Simulator), so baking the version into it would strand every saved AUTO plan on each
// release. Renaming the window afterwards gets the same result and keeps that path fixed.
public static class WindowTitle
{
    public static string Text => $"{Application.productName} v{Application.version}";

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowTextW(IntPtr hWnd, string text);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
#endif

    public static void Apply()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            uint me = GetCurrentProcessId();
            string title = Text;
            // The player has one visible top-level window; rename every visible window this
            // process owns rather than guessing at the class name, which Unity changes.
            EnumWindows((h, _) =>
            {
                if (IsWindowVisible(h) && GetWindowThreadProcessId(h, out uint pid) != 0 && pid == me)
                    SetWindowTextW(h, title);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not set the window title: {e.Message}");
        }
#endif
    }
}
