using System;
using Windows.ApplicationModel;

namespace Flint3.Helpers
{
    public static class AppVersionUtil
    {
        /// <summary>
        /// 获取app版本号；免打包时没有包版本，回退到程序集版本
        /// </summary>
        /// <returns></returns>
        public static string GetAppVersion()
        {
            try
            {
                Package package = Package.Current;
                PackageId packageId = package.Id;
                PackageVersion version = packageId.Version;
                return string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
            }
            catch (Exception) { }

            Version? assemblyVersion = typeof(AppVersionUtil).Assembly.GetName().Version;
            return assemblyVersion is null
                ? string.Empty
                : string.Format("{0}.{1}.{2}", assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }
    }
}
