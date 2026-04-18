using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;
using WinTabber.Common.Util;

namespace WinTabber.UI.Common.Hints;

public class HintChordState
{
    public enum KeyResult
    {
        Accepted,
        Ignored,
        Reset
    }
    private StringBuilder _chord = new StringBuilder(3);

    public void Reset()
    {
        _chord.Clear();
    }

    public string Value => _chord.ToString();

    public KeyResult AddKey(KeyEventArgs keyEvent)
    {
        var key = keyEvent.SystemKey != Key.None ? keyEvent.SystemKey : keyEvent.Key;

        if (key == Key.Back)
        {
            Back();
            return KeyResult.Accepted;
        }

        if (
            key.In(
                Key.LeftCtrl,
                Key.RightCtrl,
                Key.LeftAlt,
                Key.RightAlt,
                Key.LeftShift,
                Key.RightShift,
                Key.LWin,
                Key.RWin
            )
        )
        {
            return KeyResult.Ignored;
        }

        var text = GetText(key);
        if (text.Length > 0)
        {
            Append(text);
            return KeyResult.Accepted;
        }

        Reset();
        return KeyResult.Reset;
    }

    private void Back()
    {
        if (_chord.Length > 0)
        {
            _chord.Remove(_chord.Length - 1, 1);
        }
    }

    private void Append(string text)
    {
        _chord.Append(text);
    }


    private static string GetText(Key key)
    {
        // Letters A–Z
        if (key >= Key.A && key <= Key.Z)
            return key.ToString(); // already "A"…"Z"

        // Top-row digits D0–D9
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)(key - Key.D0)).ToString();

        // Numpad digits NumPad0–NumPad9
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)(key - Key.NumPad0)).ToString();

        return string.Empty;
    }
}
