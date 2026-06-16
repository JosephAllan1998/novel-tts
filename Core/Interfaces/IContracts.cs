using novel_tts.Core.Enums;
using novel_tts.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace novel_tts.Core.Interfaces
{
    public interface INovelRepository
    {
        Task SaveNovelAsync(Novel novel);
        Task SaveChapterAsync(Chapter chapter);
        Task UpdateChapterStatusAsync(string chapterId, JobStatus status, string errorMessage = null);
        Task<List<Chapter>> GetPendingChaptersAsync(string novelId);
        // Các hàm khác sẽ thêm khi cần...
    }

    // Interface cho Engine Cào dữ liệu
    public interface ICrawlerEngine
    {
        // Lấy tổng số trang từ trang chính của truyện
        Task<int> GetTotalPaginationAsync(string novelUrl, CancellationToken cancellationToken);

        // Cào URL các chương từ một trang Pagination
        Task<List<Chapter>> ScrapeChapterUrlsAsync(string paginationUrl, string novelId, CancellationToken cancellationToken);

        // Tải HTML của một chương
        Task<bool> DownloadChapterHtmlAsync(Chapter chapter, CancellationToken cancellationToken);
    }

    // Interface cho Engine bóc tách HTML -> TXT
    public interface IHtmlParser
    {
        Task<bool> ParseHtmlToTxtAsync(Chapter chapter);
    }

    // Interface cho Engine Text-to-Speech (Strategy Pattern)
    public interface ITtsEngine
    {
        TtsEngineType EngineType { get; }
        Task<bool> ConvertTextToAudioAsync(string inputTxtPath, string outputAudioPath, CancellationToken cancellationToken);
    }
}
