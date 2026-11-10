using System;
using System.Collections.Generic;
using Flint3.Core.Wordle;
using Xunit;

namespace Flint3.Core.Tests
{
    public class WordleGameTests
    {
        [Fact]
        public void Evaluate_先判定完全匹配()
        {
            WordleLetterState[] states = WordleGame.Evaluate("abbey", "babes");

            Assert.Equal(new[]
            {
                WordleLetterState.Present,
                WordleLetterState.Present,
                WordleLetterState.Correct,
                WordleLetterState.Correct,
                WordleLetterState.Absent
            }, states);
        }

        [Fact]
        public void Evaluate_重复字母不会多判为存在()
        {
            // 答案里只有一个 b，guess 里两个 b：位置正确的那个优先，另一个判 Absent
            WordleLetterState[] states = WordleGame.Evaluate("abbey", "bobby");

            Assert.Equal(new[]
            {
                WordleLetterState.Present,
                WordleLetterState.Absent,
                WordleLetterState.Correct,
                WordleLetterState.Absent,
                WordleLetterState.Correct
            }, states);
        }

        [Fact]
        public void Evaluate_全错时全部为Absent()
        {
            WordleLetterState[] states = WordleGame.Evaluate("apple", "wrong");

            Assert.All(states, state => Assert.Equal(WordleLetterState.Absent, state));
        }

        [Fact]
        public void Submit_猜中后本局结束()
        {
            WordleGame game = new("apple");

            WordleGuess guess = game.Submit("apple");

            Assert.True(guess.IsCorrect);
            Assert.True(game.IsWon);
            Assert.True(game.IsFinished);
            Assert.Throws<InvalidOperationException>(() => game.Submit("apple"));
        }

        [Fact]
        public void Submit_六次未中后结束()
        {
            WordleGame game = new("apple");

            for (int i = 0; i < WordleGame.MaxAttempts; i++)
            {
                game.Submit("brick");
            }

            Assert.False(game.IsWon);
            Assert.True(game.IsFinished);
            Assert.Equal(WordleGame.MaxAttempts, game.AttemptsUsed);
        }

        [Theory]
        [InlineData("app")]
        [InlineData("apples")]
        [InlineData("Apple")]
        [InlineData("app1e")]
        public void Submit_拒绝非5个小写字母(string guess)
        {
            WordleGame game = new("apple");

            Assert.Throws<ArgumentException>(() => game.Submit(guess));
            Assert.Equal(0, game.AttemptsUsed);
        }

        [Fact]
        public void AnswerOfDay_同一天稳定且随日期变化()
        {
            IReadOnlyList<string> answers = ["aaaaa", "bbbbb", "ccccc", "ddddd"];

            string first = WordleGame.AnswerOfDay(new DateOnly(2026, 11, 10), answers);
            string second = WordleGame.AnswerOfDay(new DateOnly(2026, 11, 10), answers);
            string next = WordleGame.AnswerOfDay(new DateOnly(2026, 11, 11), answers);

            Assert.Equal(first, second);
            Assert.NotEqual(first, next);
            Assert.Contains(first, answers);
        }

        [Fact]
        public void AnswerOfDay_空词表抛异常()
        {
            Assert.Throws<ArgumentException>(() => WordleGame.AnswerOfDay(new DateOnly(2026, 11, 10), Array.Empty<string>()));
        }

        [Theory]
        [InlineData("apple", true)]
        [InlineData("APPLE", false)]
        [InlineData("app", false)]
        [InlineData(null, false)]
        public void IsValidWord_只接受5个小写字母(string word, bool expected)
        {
            Assert.Equal(expected, WordleGame.IsValidWord(word));
        }
    }
}
