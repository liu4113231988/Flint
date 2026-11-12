using CommunityToolkit.Mvvm.ComponentModel;

namespace Flint3.Data.Models
{
    public enum AiTranslationPhase
    {
        /// <summary>尚未请求</summary>
        Idle,
        /// <summary>请求进行中</summary>
        Running,
        /// <summary>成功返回</summary>
        Done,
        /// <summary>失败</summary>
        Failed,
    }

    /// <summary>
    /// 单个词条/整句的 AI 翻译状态。挂在 StarDictWordItem 上随词条生成，
    /// 所有属性经 SetProperty 通知，流式/异步更新由 ViewModel 在 UI 线程回填。
    /// </summary>
    public partial class AiTranslationItem : ObservableObject
    {
        private AiTranslationPhase _phase = AiTranslationPhase.Idle;

        public AiTranslationPhase Phase
        {
            get => _phase;
            set
            {
                if (SetProperty(ref _phase, value))
                {
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
        }

        private string _resultText = "";

        public string ResultText { get => _resultText; set => SetProperty(ref _resultText, value); }

        /// <summary>脚注：方向 · 耗时 · 模型 · 缓存/截断</summary>
        private string _detailText = "";

        public string DetailText { get => _detailText; set => SetProperty(ref _detailText, value); }

        private string _errorText = "";

        public string ErrorText { get => _errorText; set => SetProperty(ref _errorText, value); }

        private bool _canRetry;

        public bool CanRetry { get => _canRetry; set => SetProperty(ref _canRetry, value); }

        private bool _isVisible;

        public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

        public bool IsRunning => Phase == AiTranslationPhase.Running;
    }
}
