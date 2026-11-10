using System.Collections.Generic;
using System.Text.Json.Serialization;
using Flint3.Core.Input;
using Flint3.Core.Wordle;

namespace Flint3.Core.Serialization
{
    /// <summary>
    /// 源生成的 JSON 上下文，避免 AOT/裁剪下的反射序列化
    /// </summary>
    [JsonSerializable(typeof(HotkeySettings))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(WordleWordList))]
    [JsonSerializable(typeof(WordleState))]
    public partial class FlintJsonContext : JsonSerializerContext
    {
    }
}
