using System;
using Flint3.Core.Wordle;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Flint3.Converters
{
    /// <summary>
    /// 把 Wordle 的字母判定转成底色：未判定透明，灰/黄/绿对应不存在 / 位置不对 / 完全正确
    /// </summary>
    internal class WordleState2BrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Unjudged = new(Colors.Transparent);

        // 键盘按键未判定时给一层浅灰底，避免在浅色/深色主题下"看不见"键帽
        private static readonly SolidColorBrush KeyIdle = new(Color.FromArgb(28, 128, 128, 128));

        private static readonly SolidColorBrush Absent = new(Color.FromArgb(255, 120, 124, 126));

        private static readonly SolidColorBrush Present = new(Color.FromArgb(255, 201, 180, 88));

        private static readonly SolidColorBrush Correct = new(Color.FromArgb(255, 106, 170, 100));

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not WordleLetterState state)
            {
                return parameter as string == "key" ? KeyIdle : Unjudged;
            }

            return state switch
            {
                WordleLetterState.Correct => Correct,
                WordleLetterState.Present => Present,
                _ => Absent,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return null;
        }
    }
}
