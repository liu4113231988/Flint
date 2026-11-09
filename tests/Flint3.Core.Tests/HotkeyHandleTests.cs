using Flint3.Core.Input;
using Xunit;

namespace Flint3.Core.Tests
{
    public class HotkeyHandleTests
    {
        [Fact]
        public void GetHotkeyHandle_DefaultAltSpace()
        {
            // Alt=1<<11, Space=0x20
            Assert.Equal(0x0820, HotkeyService.GetHotkeyHandle(new HotkeySettings(false, false, true, false, 0x20)));
        }

        [Fact]
        public void GetHotkeyHandle_EncodesAllModifiers()
        {
            Assert.Equal(0x0F41, HotkeyService.GetHotkeyHandle(new HotkeySettings(true, true, true, true, 0x41)));
        }

        [Theory]
        [InlineData(true, false, false, false, 0x41, 0x0141)]
        [InlineData(false, true, false, false, 0x41, 0x0241)]
        [InlineData(false, false, false, true, 0x41, 0x0441)]
        [InlineData(false, false, true, false, 0x41, 0x0841)]
        public void GetHotkeyHandle_EncodesSingleModifier(bool win, bool ctrl, bool alt, bool shift, int code, int expected)
        {
            Assert.Equal(expected, HotkeyService.GetHotkeyHandle(new HotkeySettings(win, ctrl, alt, shift, code)));
        }

        [Fact]
        public void GetHotkeyHandle_KeepsLowByteOfCode()
        {
            Assert.Equal(0x0000, HotkeyService.GetHotkeyHandle(new HotkeySettings(false, false, false, false, 0x100)));
        }

        [Fact]
        public void GetHotkeyHandle_IgnoresModifierOrderInConstruction()
        {
            var a = new HotkeySettings(true, false, true, true, 0x70);
            var b = new HotkeySettings(win: true, ctrl: false, alt: true, shift: true, code: 0x70);

            Assert.Equal(HotkeyService.GetHotkeyHandle(a), HotkeyService.GetHotkeyHandle(b));
        }
    }
}
