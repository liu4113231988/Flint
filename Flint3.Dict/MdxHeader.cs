using System.Collections.Generic;
using System.Text;

namespace Flint3.Dict
{
    /// <summary>
    /// 词典格式不支持时抛出（加密、LZO 压缩、非 2.0 版本等），消息可直接展示给用户
    /// </summary>
    public sealed class MdxUnsupportedException : System.Exception
    {
        public MdxUnsupportedException(string message) : base(message) { }
    }

    /// <summary>
    /// mdx 文件头里的 XML 属性
    /// </summary>
    public sealed class MdxHeader
    {
        public string EngineVersion { get; private init; } = "";

        public int Encrypted { get; private init; }

        public string EncodingName { get; private init; } = "";

        public string Format { get; private init; } = "";

        public string Title { get; private init; } = "";

        public string Description { get; private init; } = "";

        /// <summary>
        /// 词条文本的编码，同时决定“基本单元”宽度（UTF-16 为 2 字节）
        /// </summary>
        public Encoding Encoding { get; private init; } = Encoding.UTF8;

        public int UnitSize { get; private init; } = 1;

        internal static MdxHeader Parse(string xml)
        {
            Dictionary<string, string> attributes = ParseAttributes(xml);
            string encodingName = Get(attributes, "Encoding");
            Encoding encoding = ResolveEncoding(encodingName);

            return new MdxHeader
            {
                EngineVersion = Get(attributes, "GeneratedByEngineVersion"),
                Encrypted = int.TryParse(Get(attributes, "Encrypted"), out int encrypted) ? encrypted : 0,
                EncodingName = encodingName,
                Format = Get(attributes, "Format"),
                Title = Get(attributes, "Title"),
                Description = Get(attributes, "Description"),
                Encoding = encoding,
                UnitSize = encoding is UnicodeEncoding ? 2 : 1,
            };
        }

        private static string Get(Dictionary<string, string> attributes, string name)
        {
            return attributes.TryGetValue(name, out string? value) ? value : "";
        }

        private static Encoding ResolveEncoding(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "UTF-8" or "UTF8" => new UTF8Encoding(false),
                "UTF-16" or "UTF16" => new UnicodeEncoding(false, false),
                _ => throw new MdxUnsupportedException($"暂不支持 {name} 编码的词库，请使用 UTF-8 或 UTF-16"),
            };
        }

        /// <summary>
        /// 只解析形如 name="value" 的属性，值里的引号在 XML 中已转义
        /// </summary>
        private static Dictionary<string, string> ParseAttributes(string xml)
        {
            Dictionary<string, string> attributes = new(System.StringComparer.OrdinalIgnoreCase);
            int index = 0;
            while (true)
            {
                int equals = xml.IndexOf('=', index);
                if (equals < 0)
                {
                    break;
                }

                int valueStart = equals + 1;
                if (valueStart >= xml.Length || xml[valueStart] != '"')
                {
                    index = valueStart;
                    continue;
                }

                int nameEnd = equals;
                int nameStart = nameEnd;
                while (nameStart > 0 && IsNameChar(xml[nameStart - 1]))
                {
                    nameStart--;
                }

                int valueEnd = xml.IndexOf('"', valueStart + 1);
                if (valueEnd < 0)
                {
                    break;
                }

                if (nameEnd > nameStart)
                {
                    attributes[xml[nameStart..nameEnd]] = xml[(valueStart + 1)..valueEnd];
                }

                index = valueEnd + 1;
            }

            return attributes;
        }

        private static bool IsNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ':';
        }
    }
}
