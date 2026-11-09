using System;
using Flint3.Core.Settings;
using Microsoft.Windows.Storage;

namespace Flint3.Helpers
{
    /// <summary>
    /// 优先使用带包标识的 ApplicationData；免打包时退化为 LocalAppData 下的 json 文件
    /// </summary>
    internal sealed class ApplicationDataSettingsStore : ISettingsStore
    {
        private readonly ApplicationDataContainer? _container;
        private readonly ISettingsStore _fallback = new JsonFileSettingsStore();
        private readonly bool _useContainer;

        public ApplicationDataSettingsStore()
        {
            try
            {
                _container = ApplicationData.GetDefault().LocalSettings;
                _useContainer = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                _useContainer = false;
            }
        }

        public object? this[string key]
        {
            get => _useContainer ? _container!.Values[key] : _fallback[key];
            set
            {
                if (_useContainer)
                {
                    _container!.Values[key] = value;
                }
                else
                {
                    _fallback[key] = value;
                }
            }
        }
    }
}
