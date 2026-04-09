using System.Text;
using System.Windows;

namespace WinTabber.UI.Common.Hints;

public class GeneratedHintsProvider : IHintsProvider
{
    private const string Chars = "ASDFGHJKL";
    private static readonly int Base = Chars.Length;
    public IEnumerable<DecoratedElementInfo> GetHints(IEnumerable<FrameworkElement> frameworkElements)
    {

        StringBuilder prefix = new();
        int current = 0;
        foreach (var frameworkElement in frameworkElements)
        {
            if (prefix.Length > 0)
            {
                prefix.Remove(prefix.Length - 1, 1);
            }
            if (current < Base)
            {

            }
            else
            {
                current = 0;
                prefix.Append(Chars[current]);
            }

            prefix.Append(Chars[current]);
            yield return new DecoratedElementInfo
            {
                Element = frameworkElement,
                HintText = prefix.ToString()
            };
            current++;

        }

    }
}
