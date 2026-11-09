using System.Collections.Generic;
using System.Text.Json.Serialization;
using Flint3.Core.Input;

namespace Flint3.Core.Serialization
{
    /// <summary>
    /// 源生成的 JSON 上下文，避免 AOT/裁剪下的反射序列化
    /// </summary>
    [JsonSerializable(typeof(HotkeySettings))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class FlintJsonContext : JsonSerializerContext
    {
    }
}
