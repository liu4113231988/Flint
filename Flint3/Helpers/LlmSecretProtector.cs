using System;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;

namespace Flint3.Helpers
{
    /// <summary>
    /// API Key 保护：DPAPI（CurrentUser 作用域）加解密。
    /// 加密后的 Base64 存入 ISettingsStore；跨机器/跨用户无法解密，属预期行为（密钥不随配置迁移）。
    /// 用 CsWin32 直接绑定 CryptProtectData/CryptUnprotectData，避免引入 System.Security.Cryptography.ProtectedData 包。
    /// </summary>
    public static class LlmSecretProtector
    {
        public static string? Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
            {
                return null;
            }

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plain);
                byte[] cipherBytes;
                unsafe
                {
                    fixed (byte* pPlain = plainBytes)
                    {
                        CRYPT_INTEGER_BLOB input = new() { cbData = (uint)plainBytes.Length, pbData = pPlain };
                        CRYPT_INTEGER_BLOB output = default;
                        if (!PInvoke.CryptProtectData(&input, default, null, null, null, (uint)PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
                        {
                            return null;
                        }

                        cipherBytes = CopyAndFreeBlob(in output);
                    }
                }

                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"DPAPI protect failed: {ex.GetType().Name}");
                return null;
            }
        }

        public static string? Unprotect(string cipherBase64)
        {
            if (string.IsNullOrEmpty(cipherBase64))
            {
                return null;
            }

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherBase64);
                byte[] plainBytes;
                unsafe
                {
                    fixed (byte* pCipher = cipherBytes)
                    {
                        CRYPT_INTEGER_BLOB input = new() { cbData = (uint)cipherBytes.Length, pbData = pCipher };
                        CRYPT_INTEGER_BLOB output = default;
                        PWSTR descr;
                        if (!PInvoke.CryptUnprotectData(&input, &descr, null, null, null, (uint)PInvoke.CRYPTPROTECT_UI_FORBIDDEN, &output))
                        {
                            return null;
                        }

                        plainBytes = CopyAndFreeBlob(in output);
                    }
                }

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"DPAPI unprotect failed: {ex.GetType().Name}");
                return null;
            }
        }

        private static unsafe byte[] CopyAndFreeBlob(in CRYPT_INTEGER_BLOB blob)
        {
            byte[] bytes = new byte[blob.cbData];
            Marshal.Copy((nint)blob.pbData, bytes, 0, (int)blob.cbData);
            // DPAPI 输出用 LocalAlloc 分配，Marshal.FreeHGlobal 内部即 LocalFree
            Marshal.FreeHGlobal((nint)blob.pbData);
            return bytes;
        }
    }
}
