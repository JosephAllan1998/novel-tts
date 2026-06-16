using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using novel_tts.Applications.Services;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace novel_tts.Presentation.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly CrawlPipelineManager _crawlManager;
        private readonly INovelRepository _repository;

        public bool IsProcessing { get; private set; }

        private CancellationTokenSource _cts;

        [ObservableProperty]
        private string _novelUrl;

        [ObservableProperty]
        private string _saveDirectory;

        [ObservableProperty]
        private string _statusLog;

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private int _progressMaximum = 100;

        [ObservableProperty]
        private bool _isProcessing;

        public string StatusLog { get; private set; }
        public int ProgressValue { get; private set; }
        public string SaveDirectory { get; private set; }
        public int ProgressMaximum { get; private set; }
        public string NovelUrl { get; private set; }

        public MainViewModel(CrawlPipelineManager crawlManager, INovelRepository repository)
        {
            _crawlManager = crawlManager;
            _repository = repository;
        }

        [RelayCommand]
        private void BrowseDirectory()
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = "Chọn thư mục lưu trữ dự án truyện",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == true)
            {
                SaveDirectory = dialog.SelectedPath;
            }
        }

        [RelayCommand]
        private async Task StartProcessAsync()
        {
            if (string.IsNullOrWhiteSpace(NovelUrl) || string.IsNullOrWhiteSpace(SaveDirectory))
            {
                StatusLog = "Vui lòng nhập URL và chọn thư mục lưu!";
                return;
            }

            IsProcessing = true;
            _cts = new CancellationTokenSource();
            StatusLog = "Đang khởi tạo tiến trình Crawler...";
            ProgressValue = 0;

            try
            {
                // TODO: Gọi Crawler để lấy danh sách chapters
                // Ví dụ giả lập:
                var fakeChapters = new List<Chapter>();
                for (int i = 1; i <= 100; i++)
                    fakeChapters.Add(new Chapter { Index = i, Url = "..." });

                ProgressMaximum = fakeChapters.Count;

                // Setup IProgress để nhận tín hiệu cập nhật UI từ Pipeline
                var progress = new Progress<int>(percent =>
                {
                    ProgressValue++;
                    StatusLog = $"Đang xử lý: {ProgressValue}/{ProgressMaximum} chương...";
                });

                // Chạy Pipeline (Code thực tế bạn cần truyền IProgress vào Pipeline của GĐ 3)
                await Task.Run(() => FakeRunPipeline(progress, _cts.Token));

                StatusLog = "Hoàn thành toàn bộ tiến trình!";
            }
            catch (OperationCanceledException)
            {
                StatusLog = "Tiến trình đã bị người dùng hủy.";
            }
            catch (Exception ex)
            {
                StatusLog = $"Lỗi: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        [RelayCommand]
        private void CancelProcess()
        {
            if (IsProcessing)
            {
                _cts?.Cancel();
                StatusLog = "Đang dừng hệ thống, vui lòng chờ...";
            }
        }

        private void FakeRunPipeline(IProgress<int> progress, CancellationToken token)
        {
            for (int i = 0; i < 100; i++)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(50); // Giả lập parse file
                progress.Report(1);
            }
        }
    }
}
