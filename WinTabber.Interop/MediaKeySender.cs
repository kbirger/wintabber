using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
namespace WinTabber.Interop;

public static class MediaKeySender
{
    private static void SendKey(VIRTUAL_KEY key)
    {
        INPUT[] inputs = new INPUT[2];

        // Key down
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = key;
        inputs[0].Anonymous.ki.dwFlags = 0;

        // Key up
        inputs[1].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[1].Anonymous.ki.wVk = key;
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;

        PInvoke.SendInput(inputs.AsSpan(), Marshal.SizeOf(typeof(INPUT)));
    }

    public static void PlayPause() => SendKey(VIRTUAL_KEY.VK_MEDIA_PLAY_PAUSE);
    public static void Prev() => SendKey(VIRTUAL_KEY.VK_MEDIA_PREV_TRACK);
    public static void Next() => SendKey(VIRTUAL_KEY.VK_MEDIA_NEXT_TRACK);
}
