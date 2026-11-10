using System;
using System.Text;

namespace Flint3.Dict
{
    /// <summary>
    /// 把 mdx 记录里的 HTML 转成纯文本（首期只做纯文本展示，不渲染样式与图片）
    /// </summary>
    public static class MdxTextExtractor
    {
        private static readonly string[] BlockTags =
        [
            "br", "p", "div", "li", "tr", "ul", "ol", "table", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "blockquote",
        ];

        public static string Extract(string? content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            StringBuilder builder = new(content.Length);
            int index = 0;
            while (index < content.Length)
            {
                char current = content[index];

                if (current == '<')
                {
                    if (content.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
                    {
                        int commentEnd = content.IndexOf("-->", index + 4, StringComparison.Ordinal);
                        index = commentEnd < 0 ? content.Length : commentEnd + 3;
                        continue;
                    }

                    int tagEnd = content.IndexOf('>', index + 1);
                    if (tagEnd < 0)
                    {
                        break;
                    }

                    ReadOnlySpan<char> tag = content.AsSpan(index + 1, tagEnd - index - 1);
                    if (IsSkippedBlock(tag, out string tagName) && !tag.StartsWith("/"))
                    {
                        index = SkipUntilClosingTag(content, tagEnd + 1, tagName);
                        continue;
                    }

                    if (IsBlockTag(tag))
                    {
                        builder.Append('\n');
                    }

                    index = tagEnd + 1;
                    continue;
                }

                if (current == '&')
                {
                    int entityEnd = content.IndexOf(';', index + 1);
                    if (entityEnd > index && entityEnd - index <= 10)
                    {
                        string entity = content[(index + 1)..entityEnd];
                        if (TryDecodeEntity(entity, out char decoded))
                        {
                            builder.Append(decoded);
                            index = entityEnd + 1;
                            continue;
                        }
                    }
                }

                builder.Append(current);
                index++;
            }

            return Normalize(builder.ToString());
        }

        private static bool IsSkippedBlock(ReadOnlySpan<char> tag, out string tagName)
        {
            tagName = "script";
            if (tag.Length >= 6 && tag[..6].Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            tagName = "style";
            if (tag.Length >= 5 && tag[..5].Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            tagName = "";
            return false;
        }

        private static bool IsBlockTag(ReadOnlySpan<char> tag)
        {
            ReadOnlySpan<char> name = tag.StartsWith("/") ? tag[1..] : tag;
            int end = 0;
            while (end < name.Length && char.IsLetterOrDigit(name[end]))
            {
                end++;
            }

            name = name[..end];
            foreach (string blockTag in BlockTags)
            {
                if (name.Equals(blockTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // <br/> 这类自闭合写法也算换行
            return name.Equals("br", StringComparison.OrdinalIgnoreCase);
        }

        private static int SkipUntilClosingTag(string content, int from, string tagName)
        {
            string closing = "</" + tagName;
            int closingIndex = content.IndexOf(closing, from, StringComparison.OrdinalIgnoreCase);
            if (closingIndex < 0)
            {
                return content.Length;
            }

            int tagEnd = content.IndexOf('>', closingIndex);
            return tagEnd < 0 ? content.Length : tagEnd + 1;
        }

        private static bool TryDecodeEntity(string entity, out char value)
        {
            value = '\0';
            switch (entity.ToLowerInvariant())
            {
                case "amp": value = '&'; return true;
                case "lt": value = '<'; return true;
                case "gt": value = '>'; return true;
                case "quot": value = '"'; return true;
                case "apos": value = '\''; return true;
                case "nbsp": value = ' '; return true;
            }

            if (entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.TryParse(entity.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out ushort code)
                    && TryConvert(code, out value);
            }

            if (entity.StartsWith('#'))
            {
                return ushort.TryParse(entity.AsSpan(1), out ushort code) && TryConvert(code, out value);
            }

            return false;
        }

        private static bool TryConvert(ushort code, out char value)
        {
            value = (char)code;
            return true;
        }

        private static string Normalize(string text)
        {
            StringBuilder builder = new(text.Length);
            bool lastWasSpace = false;
            int newlineRun = 0;

            foreach (char c in text.Replace("\r", "", StringComparison.Ordinal))
            {
                if (c == '\n')
                {
                    newlineRun++;
                    lastWasSpace = false;
                    if (newlineRun <= 2)
                    {
                        builder.Append('\n');
                    }

                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && newlineRun == 0)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }

                    continue;
                }

                newlineRun = 0;
                lastWasSpace = false;
                builder.Append(c);
            }

            return builder.ToString().Trim();
        }
    }
}
