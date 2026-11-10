namespace Flint3.Core.Wordle
{
    /// <summary>
    /// 单个字母在猜测中的判定结果
    /// </summary>
    public enum WordleLetterState
    {
        /// <summary>词库中没有该字母（或已被前面的正确/存在占用）</summary>
        Absent = 0,

        /// <summary>字母在答案中但位置不对</summary>
        Present = 1,

        /// <summary>字母与位置都正确</summary>
        Correct = 2,
    }
}
