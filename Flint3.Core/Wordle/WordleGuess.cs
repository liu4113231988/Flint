namespace Flint3.Core.Wordle
{
    /// <summary>
    /// 一次已提交的猜测及其逐字母判定
    /// </summary>
    public sealed class WordleGuess
    {
        public WordleGuess(string word, WordleLetterState[] states)
        {
            Word = word;
            States = states;
        }

        public string Word { get; }

        public WordleLetterState[] States { get; }

        public bool IsCorrect => WordleGame.IsAllCorrect(States);
    }
}
