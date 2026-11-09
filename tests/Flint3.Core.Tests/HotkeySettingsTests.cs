using Flint3.Core.Input;
using Xunit;

namespace Flint3.Core.Tests
{
    public class HotkeySettingsTests
    {
        [Theory]
        [InlineData(false, false, true, false, 0x20)]
        [InlineData(true, false, false, false, 0x0D)]
        [InlineData(false, true, true, true, 0x70)]
        public void IsValid_WithModifierAndKey(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            Assert.True(new HotkeySettings(win, ctrl, alt, shift, code).IsValid());
        }

        [Theory]
        [InlineData(false, false, false, false, 0x20)] // 只有按键没有修饰键
        [InlineData(false, false, true, false, 0)]      // 只有修饰键没有按键
        [InlineData(false, false, false, false, 0)]     // 空
        public void IsValid_WithoutModifierOrKey(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            Assert.False(new HotkeySettings(win, ctrl, alt, shift, code).IsValid());
        }

        [Theory]
        [InlineData(false, false, false, true, 0x09)] // Shift+Tab
        [InlineData(false, false, false, false, 0x09)] // Tab
        public void IsAccessibleShortcut_Tab(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            var settings = new HotkeySettings(win, ctrl, alt, shift, code);

            Assert.True(settings.IsAccessibleShortcut());
            Assert.False(settings.IsValid());
        }

        [Theory]
        [InlineData(false, false, true, false, 0x09)]
        [InlineData(false, true, false, false, 0x09)]
        [InlineData(true, false, false, false, 0x09)]
        [InlineData(false, false, false, true, 0x20)]
        public void IsAccessibleShortcut_OtherCombinations(bool win, bool ctrl, bool alt, bool shift, int code)
        {
            Assert.False(new HotkeySettings(win, ctrl, alt, shift, code).IsAccessibleShortcut());
        }

        [Fact]
        public void IsEmpty_OnlyForNoModifierAndNoKey()
        {
            Assert.True(new HotkeySettings().IsEmpty());
            Assert.False(new HotkeySettings(false, false, false, false, 0x20).IsEmpty());
            Assert.False(new HotkeySettings(false, false, true, false, 0).IsEmpty());
        }

        [Fact]
        public void ToString_UsesReadableKeyNames()
        {
            Assert.Equal("Alt + Space", new HotkeySettings(false, false, true, false, 0x20).ToString());
            Assert.Equal("Ctrl + Shift + F1", new HotkeySettings(false, true, false, true, 0x70).ToString());
        }

        [Fact]
        public void Clone_CopiesAllFields()
        {
            var settings = new HotkeySettings(true, false, true, true, 0x70);
            var clone = settings.Clone();

            Assert.NotSame(settings, clone);
            Assert.Equal(settings.Win, clone.Win);
            Assert.Equal(settings.Ctrl, clone.Ctrl);
            Assert.Equal(settings.Alt, clone.Alt);
            Assert.Equal(settings.Shift, clone.Shift);
            Assert.Equal(settings.Code, clone.Code);
        }
    }
}
