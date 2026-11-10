using System.Text;

namespace Flint3.Core.Query
{
    /// <summary>
    /// 通配符查词语法的翻译结果：<c>?</c> / <c>_</c> 表示单个字母，<c>*</c> 表示任意多个字母，
    /// <c>^</c> / <c>$</c> 分别限定词首 / 词尾；两者都不写或都写时按整词匹配。
    /// </summary>
    public readonly record struct WordPattern(string Like, int? ExactLength, bool IsLiteral, string Literal)
    {
        /// <summary>
        /// 尝试把用户输入翻译成 LIKE 模式；输入不是合法通配符语法时返回 false，调用方应回退普通查词
        /// </summary>
        public static bool TryTranslate(string? input, out WordPattern pattern)
        {
            pattern = default;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim();
            bool startsAnchored = text.StartsWith('^');
            bool endsAnchored = text.EndsWith('$');

            if (startsAnchored)
            {
                text = text[1..];
            }

            if (endsAnchored)
            {
                text = text[..^1];
            }

            if (text.Length == 0)
            {
                return false;
            }

            // 两个锚点都写与都不写都表示整词匹配，只有一个锚点时放宽到前缀 / 后缀
            bool wholeWord = startsAnchored == endsAnchored;
            bool hasWildcard = false;
            bool hasStar = false;
            int literalLength = 0;

            StringBuilder like = new(text.Length + 2);
            if (!startsAnchored && !wholeWord)
            {
                like.Append('%');
            }

            foreach (char c in text)
            {
                if (c == '?' || c == '_')
                {
                    like.Append('_');
                    hasWildcard = true;
                    literalLength++;
                }
                else if (c == '*')
                {
                    like.Append('%');
                    hasWildcard = true;
                    hasStar = true;
                }
                else if (char.IsAsciiLetter(c))
                {
                    like.Append(c);
                    literalLength++;
                }
                else
                {
                    // 其它字符不支持，交给普通查词逻辑
                    return false;
                }
            }

            if (!endsAnchored && !wholeWord)
            {
                like.Append('%');
            }

            pattern = new WordPattern(
                like.ToString(),
                wholeWord && !hasStar ? literalLength : null,
                !hasWildcard && wholeWord,
                text);
            return true;
        }
    }
}
