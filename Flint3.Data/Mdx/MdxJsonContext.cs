using System.Text.Json.Serialization;

namespace Flint3.Data.Mdx
{
    /// <summary>
    /// AOT 下用于 dictionarySources.json 的源生成上下文
    /// </summary>
    [JsonSerializable(typeof(MdxDictionarySettings))]
    internal partial class MdxJsonContext : JsonSerializerContext
    {
    }
}
