using novel_tts.Core.Enums;
using novel_tts.Core.Interfaces;
using novel_tts.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace novel_tts.Applications.Services
{
    public class CrawlPipelineManager
    {
        private readonly ICrawlerEngine _crawler;
        private readonly IHtmlParser _parser;
        private readonly INovelRepository _repository;

        public CrawlPipelineManager(ICrawlerEngine crawler, IHtmlParser parser, INovelRepository repository)
        {
            _crawler = crawler;
            _parser = parser;
            _repository = repository;
        }

        public async Task StartPipelineAsync(List<Chapter> chaptersToProcess, CancellationToken cancellationToken)
        {
            // Block 1: Tải HTML (Giới hạn 3 kết nối đồng thời để không bị ban IP)
            var downloadBlock = new TransformBlock<Chapter, Chapter>(async chapter =>
            {
                chapter.DownloadStatus = JobStatus.Processing;
                await _repository.UpdateChapterStatusAsync(chapter.Id, JobStatus.Processing);

                bool success = await _crawler.DownloadChapterHtmlAsync(chapter, cancellationToken);

                if (success)
                {
                    chapter.DownloadStatus = JobStatus.Completed;
                    await _repository.UpdateChapterStatusAsync(chapter.Id, JobStatus.Completed);
                }
                else
                {
                    chapter.DownloadStatus = JobStatus.Failed;
                    await _repository.UpdateChapterStatusAsync(chapter.Id, JobStatus.Failed, chapter.LastErrorMessage);
                }

                return chapter;
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 3, // Quan trọng: Multi-thread an toàn
                CancellationToken = cancellationToken
            });

            // Block 2: Parse HTML sang TXT (Xử lý chuỗi nhẹ nên có thể chạy đa luồng cao hơn)
            var parseBlock = new TransformBlock<Chapter, Chapter>(async chapter =>
            {
                // Chỉ parse nếu download thành công
                if (chapter.DownloadStatus == JobStatus.Completed)
                {
                    chapter.ParseStatus = JobStatus.Processing;
                    bool success = await _parser.ParseHtmlToTxtAsync(chapter);

                    chapter.ParseStatus = success ? JobStatus.Completed : JobStatus.Failed;

                    // Cập nhật DB trạng thái Parse
                    var finalStatus = success ? JobStatus.Completed : JobStatus.Failed;
                    await _repository.UpdateChapterStatusAsync(chapter.Id, finalStatus, chapter.LastErrorMessage);
                }
                return chapter;
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 5,
                CancellationToken = cancellationToken
            });

            // Block 3: Báo cáo Progress (Hứng dữ liệu đầu ra)
            var actionBlock = new ActionBlock<Chapter>(chapter =>
            {
                // Ở Giai đoạn 5 (UI), chúng ta sẽ trigger Event IProgress<T> ở đây để update ProgressBar.
                // Console.WriteLine($"Chapter {chapter.Index} processed.");
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                CancellationToken = cancellationToken
            });

            // Liên kết các Block lại với nhau thành Pipeline
            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
            downloadBlock.LinkTo(parseBlock, linkOptions);
            parseBlock.LinkTo(actionBlock, linkOptions);

            // Đưa dữ liệu (Producer) vào Pipeline
            foreach (var chapter in chaptersToProcess)
            {
                await downloadBlock.SendAsync(chapter, cancellationToken);
            }

            // Báo hiệu không còn dữ liệu mới đưa vào
            downloadBlock.Complete();

            // Đợi toàn bộ Pipeline xử lý xong
            await actionBlock.Completion;
        }
    }
}
