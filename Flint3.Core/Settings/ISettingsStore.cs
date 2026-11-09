namespace Flint3.Core.Settings
{
    /// <summary>
    /// 设置项读写抽象，屏蔽打包/免打包的存储差异
    /// </summary>
    public interface ISettingsStore
    {
        object? this[string key] { get; set; }
    }
}
