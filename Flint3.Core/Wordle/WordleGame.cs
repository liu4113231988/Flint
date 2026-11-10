using System;
using System.Collections.Generic;

namespace Flint3.Core.Wordle
{
    /// <summary>
    /// Wordle 的纯状态机：只负责判定与轮次，不依赖词典查询和界面
    /// </summary>
    public sealed class WordleGame
    {
        public const int WordLength = 5;
        public const int MaxAttempts = 6;

        private readonly List<WordleGuess> _guesses = [];

        public WordleGame(string answer)
        {
            if (!IsValidWord(answer))
            {
                throw new ArgumentException("答案必须是 5 个小写字母", nameof(answer));
            }

            Answer = answer;
        }

        public string Answer { get; }

        public IReadOnlyList<WordleGuess> Guesses => _guesses;

        public bool IsWon { get; private set; }

        public bool IsFinished => IsWon || _guesses.Count >= MaxAttempts;

        public int AttemptsUsed => _guesses.Count;

        public static bool IsValidWord(string? word)
        {
            if (word is null || word.Length != WordLength)
            {
                return false;
            }

            foreach (char c in word)
            {
                if (c < 'a' || c > 'z')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 提交一次猜测；形如 "apple" 的小写 5 字母，是否在允许词表内由调用方校验
        /// </summary>
        public WordleGuess Submit(string guess)
        {
            if (IsFinished)
            {
                throw new InvalidOperationException("本局已结束");
            }

            if (!IsValidWord(guess))
            {
                throw new ArgumentException("猜测必须是 5 个小写字母", nameof(guess));
            }

            WordleGuess result = new(guess, Evaluate(Answer, guess));
            _guesses.Add(result);
            IsWon = result.IsCorrect;
            return result;
        }

        /// <summary>
        /// 两遍判定：先定位完全正确的字母，再用剩余计数判定“存在但位置不对”，
        /// 保证重复字母不会被多判为 Present
        /// </summary>
        public static WordleLetterState[] Evaluate(string answer, string guess)
        {
            WordleLetterState[] states = new WordleLetterState[WordLength];
            Dictionary<char, int> remaining = [];

            for (int i = 0; i < WordLength; i++)
            {
                if (answer[i] == guess[i])
                {
                    states[i] = WordleLetterState.Correct;
                    continue;
                }

                remaining[answer[i]] = remaining.GetValueOrDefault(answer[i]) + 1;
            }

            for (int i = 0; i < WordLength; i++)
            {
                if (states[i] == WordleLetterState.Correct)
                {
                    continue;
                }

                if (remaining.TryGetValue(guess[i], out int count) && count > 0)
                {
                    states[i] = WordleLetterState.Present;
                    remaining[guess[i]] = count - 1;
                }
            }

            return states;
        }

        public static bool IsAllCorrect(WordleLetterState[] states)
        {
            foreach (WordleLetterState state in states)
            {
                if (state != WordleLetterState.Correct)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 按日期取当日答案：同一天、同一份词表得到同一个词，便于离线一致
        /// </summary>
        public static string AnswerOfDay(DateOnly date, IReadOnlyList<string> answers)
        {
            ArgumentNullException.ThrowIfNull(answers);
            if (answers.Count == 0)
            {
                throw new ArgumentException("答案词表为空", nameof(answers));
            }

            int index = date.DayNumber % answers.Count;
            if (index < 0)
            {
                index += answers.Count;
            }

            return answers[index];
        }
    }
}
