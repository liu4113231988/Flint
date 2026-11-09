using Flint3.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Xunit;

namespace Flint3.Core.Tests
{
    public class KeyNameResolverTests
    {
        [Theory]
        [InlineData(0x20, "Space")]
        [InlineData(0x0D, "Enter")]
        [InlineData(0x09, "Tab")]
        [InlineData(0x1B, "Esc")]
        [InlineData(0x25, "Left")]
        [InlineData(0x26, "Up")]
        [InlineData(0x27, "Right")]
        [InlineData(0x28, "Down")]
        [InlineData(0x14, "Caps Lock")]
        [InlineData(0x11, "Ctrl")]
        [InlineData(0x12, "Alt")]
        [InlineData(0x10, "Shift")]
        [InlineData(0x5B, "Win (Left)")]
        [InlineData(0x70, "F1")]
        [InlineData(0x87, "F24")]
        [InlineData(0x60, "NumPad 0")]
        [InlineData(0xFF, "Undefined")]
        public void GetKeyName_UsesFixedNamesForSpecialKeys(uint key, string expected)
        {
            var resolver = new KeyNameResolver();

            Assert.Equal(expected, resolver.GetKeyName(key));
        }

        [Fact]
        public void GetKeyName_ReturnsUndefinedForUnmappedKey()
        {
            var resolver = new KeyNameResolver();

            Assert.Equal("Undefined", resolver.GetKeyName(0));
        }

        [Fact]
        public void GetKeyName_ResolvesCharactersWithFixedUsLayout()
        {
            // KLF_NOTELLSHELL：只加载不激活 en-US 布局，用于稳定断言；Dispose 时卸载
            using var layoutHandle = Flint3CorePInvoke.LoadKeyboardLayout("00000409", ACTIVATE_KEYBOARD_LAYOUT_FLAGS.KLF_NOTELLSHELL);
            if (layoutHandle.IsInvalid)
            {
                // 系统未安装 en-US 布局时跳过字符断言（特殊键用例已覆盖）
                return;
            }

            var layout = new HKL(layoutHandle.DangerousGetHandle());
            var resolver = new KeyNameResolver();

            Assert.Equal("A", resolver.GetKeyName(0x41, layout));
            Assert.Equal("Z", resolver.GetKeyName(0x5A, layout));
            Assert.Equal("1", resolver.GetKeyName(0x31, layout));
            Assert.Equal("Space", resolver.GetKeyName(0x20, layout));
        }
    }
}
