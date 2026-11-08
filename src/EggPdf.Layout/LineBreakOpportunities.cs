using System.Collections.Generic;
using System.Text;

namespace EggPdf.Layout;

internal static class LineBreakOpportunities
{
    internal const char SoftHyphen = (char)0x00AD;
    internal const char ZeroWidthSpace = (char)0x200B;
    internal const string VisibleHyphen = "-";

    internal static bool HasBreaksInsideWords(string text)
    {
        if (text.IndexOf(SoftHyphen) >= 0 || text.IndexOf(ZeroWidthSpace) >= 0) return true;
        for (int i = 1; i < text.Length - 1; i++)
            if (IsBreakableHyphen(text, i)) return true;
        return false;
    }

    internal static void AppendSegments(string word, bool firstJoinsPrevious,
        List<(string Text, bool JoinsPrevious, bool EndsAtSoftHyphen)> segments)
    {
        var current = new StringBuilder(word.Length);
        bool joins = firstJoinsPrevious;
        bool hyphenBreaksAllowed = !ContainsPrivateUseCharacter(word);
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            if (c == SoftHyphen || c == ZeroWidthSpace)
            {
                if (current.Length > 0)
                {
                    segments.Add((current.ToString(), joins, c == SoftHyphen));
                    current.Clear();
                }
                joins = true;
                continue;
            }

            current.Append(c);
            if (hyphenBreaksAllowed && IsBreakableHyphen(word, i))
            {
                segments.Add((current.ToString(), joins, false));
                current.Clear();
                joins = true;
            }
        }
        if (current.Length > 0)
            segments.Add((current.ToString(), joins, false));
    }

    internal static string RemoveInvisibleBreaks(string text)
    {
        if (text.IndexOf(SoftHyphen) < 0 && text.IndexOf(ZeroWidthSpace) < 0) return text;
        var visible = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c != SoftHyphen && c != ZeroWidthSpace) visible.Append(c);
        return visible.ToString();
    }

    private const char FirstPrivateUseCharacter = (char)0xE000;
    private const char LastPrivateUseCharacter = (char)0xF8FF;

    private static bool ContainsPrivateUseCharacter(string word)
    {
        foreach (char c in word)
            if (c >= FirstPrivateUseCharacter && c <= LastPrivateUseCharacter) return true;
        return false;
    }

    private static bool IsBreakableHyphen(string text, int index) =>
        text[index] == '-' && index > 0 && index + 1 < text.Length &&
        char.IsLetterOrDigit(text[index - 1]) && char.IsLetter(text[index + 1]);
}
