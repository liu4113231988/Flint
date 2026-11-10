using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Wordle;

namespace Flint3.ViewModels
{
    /// <summary>
    /// Wordle 的一格：Letter 是当前输入的字母，State 为 null 表示尚未判定
    /// </summary>
    public class WordleCellViewModel : ObservableObject
    {
        private string _letter = string.Empty;

        private WordleLetterState? _state;

        public string Letter
        {
            get => _letter;
            set => SetProperty(ref _letter, value);
        }

        public WordleLetterState? State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }
    }

    /// <summary>
    /// Wordle 的一行猜测
    /// </summary>
    public sealed class WordleRowViewModel
    {
        public WordleRowViewModel()
        {
            for (int i = 0; i < WordleGame.WordLength; i++)
            {
                Cells.Add(new WordleCellViewModel());
            }
        }

        public ObservableCollection<WordleCellViewModel> Cells { get; } = new();

        public void SetLetter(int index, string letter)
        {
            Cells[index].Letter = letter;
        }

        public void ApplyStates(WordleLetterState[] states)
        {
            for (int i = 0; i < states.Length && i < Cells.Count; i++)
            {
                Cells[i].State = states[i];
            }
        }

        public void Clear()
        {
            foreach (WordleCellViewModel cell in Cells)
            {
                cell.Letter = string.Empty;
                cell.State = null;
            }
        }
    }

    /// <summary>
    /// 屏幕键盘上的字母键，State 取该字母在所有猜测中的最优判定
    /// </summary>
    public class WordleKeyViewModel : ObservableObject
    {
        private WordleLetterState? _state;

        public WordleKeyViewModel(char letter)
        {
            Letter = letter;
        }

        public char Letter { get; }

        public string Display => char.ToUpperInvariant(Letter).ToString();

        public WordleLetterState? State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }
    }

    /// <summary>
    /// Wordle 页面的状态机视图模型：判定逻辑全部委托给 <see cref="WordleGame"/>，这里只负责输入、展示与进度落盘
    /// </summary>
    public class WordleViewModel : ObservableObject
    {
        private readonly WordleWordList _wordList;

        private readonly WordleStateStore _stateStore;

        private readonly DateOnly _date;

        private readonly Dictionary<char, WordleKeyViewModel> _keys = new();

        private WordleGame _game;

        private string _current = string.Empty;

        private string _statusText = string.Empty;

        private bool _isFinished;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsFinished
        {
            get => _isFinished;
            set => SetProperty(ref _isFinished, value);
        }

        public WordleViewModel()
            : this(WordleWordList.Default, new WordleStateStore(), DateOnly.FromDateTime(DateTime.Now))
        {
        }

        internal WordleViewModel(WordleWordList wordList, WordleStateStore stateStore, DateOnly date)
        {
            _wordList = wordList;
            _stateStore = stateStore;
            _date = date;
            _game = new WordleGame(WordleGame.AnswerOfDay(date, wordList.Answers));

            for (int i = 0; i < WordleGame.MaxAttempts; i++)
            {
                Rows.Add(new WordleRowViewModel());
            }

            BuildKeyboard("qwertyuiop", KeyboardRow1);
            BuildKeyboard("asdfghjkl", KeyboardRow2);
            BuildKeyboard("zxcvbnm", KeyboardRow3);

            RestoreProgress();
            UpdateStatus();
        }

        public string DateText => _date.ToString("yyyy-MM-dd");

        public ObservableCollection<WordleRowViewModel> Rows { get; } = new();

        public ObservableCollection<WordleKeyViewModel> KeyboardRow1 { get; } = new();

        public ObservableCollection<WordleKeyViewModel> KeyboardRow2 { get; } = new();

        public ObservableCollection<WordleKeyViewModel> KeyboardRow3 { get; } = new();

        private string DateKey => _date.ToString("yyyy-MM-dd");

        private void BuildKeyboard(string letters, ObservableCollection<WordleKeyViewModel> target)
        {
            foreach (char letter in letters)
            {
                WordleKeyViewModel key = new(letter);
                _keys[letter] = key;
                target.Add(key);
            }
        }

        /// <summary>
        /// 输入一个字母：只在当前行未填满时生效
        /// </summary>
        public void InputLetter(char letter)
        {
            if (_game.IsFinished)
            {
                return;
            }

            char lower = char.ToLowerInvariant(letter);
            if (lower < 'a' || lower > 'z' || _current.Length >= WordleGame.WordLength)
            {
                return;
            }

            StatusText = string.Empty;
            Rows[_game.AttemptsUsed].SetLetter(_current.Length, char.ToUpperInvariant(lower).ToString());
            _current += lower;
        }

        public void Backspace()
        {
            if (_game.IsFinished || _current.Length == 0)
            {
                return;
            }

            StatusText = string.Empty;
            _current = _current[..^1];
            Rows[_game.AttemptsUsed].SetLetter(_current.Length, string.Empty);
        }

        /// <summary>
        /// 提交当前行：长度、词表任一不满足时只给出提示，不计入次数
        /// </summary>
        public void Submit()
        {
            if (_game.IsFinished)
            {
                return;
            }

            if (_current.Length != WordleGame.WordLength)
            {
                StatusText = "需要 5 个字母";
                return;
            }

            if (!_wordList.IsAllowed(_current))
            {
                StatusText = "不在词表中";
                return;
            }

            WordleGuess guess = _game.Submit(_current);
            Rows[_game.AttemptsUsed - 1].ApplyStates(guess.States);
            UpdateKeyboard(guess);

            _current = string.Empty;
            SaveProgress();
            UpdateStatus();
        }

        /// <summary>
        /// 重置当日进度，答案仍是当天的词
        /// </summary>
        public void Reset()
        {
            _game = new WordleGame(WordleGame.AnswerOfDay(_date, _wordList.Answers));
            _current = string.Empty;
            foreach (WordleRowViewModel row in Rows)
            {
                row.Clear();
            }
            foreach (WordleKeyViewModel key in _keys.Values)
            {
                key.State = null;
            }

            SaveProgress();
            UpdateStatus();
        }

        private void UpdateKeyboard(WordleGuess guess)
        {
            for (int i = 0; i < guess.Word.Length; i++)
            {
                if (_keys.TryGetValue(guess.Word[i], out WordleKeyViewModel? key))
                {
                    WordleLetterState state = guess.States[i];
                    if (key.State is null || state > key.State.Value)
                    {
                        key.State = state;
                    }
                }
            }
        }

        private void UpdateStatus()
        {
            IsFinished = _game.IsFinished;

            if (_game.IsWon)
            {
                StatusText = $"猜对了，用了 {_game.AttemptsUsed} 次";
            }
            else if (_game.IsFinished)
            {
                StatusText = $"本局结束，答案是 {_game.Answer.ToUpperInvariant()}";
            }
            else
            {
                StatusText = string.Empty;
            }
        }

        /// <summary>
        /// 恢复当天已提交的猜测；跨天或词表变化导致的脏数据直接忽略
        /// </summary>
        private void RestoreProgress()
        {
            WordleState? state = _stateStore.Load();
            if (state is null || state.Date != DateKey)
            {
                return;
            }

            foreach (string word in state.Guesses)
            {
                if (_game.IsFinished)
                {
                    break;
                }

                string lower = word?.ToLowerInvariant() ?? string.Empty;
                if (!WordleGame.IsValidWord(lower) || !_wordList.IsAllowed(lower))
                {
                    continue;
                }

                WordleGuess guess = _game.Submit(lower);
                Rows[_game.AttemptsUsed - 1].ApplyStates(guess.States);
                UpdateKeyboard(guess);
            }
        }

        private void SaveProgress()
        {
            _stateStore.Save(new WordleState
            {
                Date = DateKey,
                Guesses = _game.Guesses.Select(guess => guess.Word).ToList(),
            });
        }
    }
}
