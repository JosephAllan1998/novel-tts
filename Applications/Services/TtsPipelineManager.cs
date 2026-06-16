using novel_tts.Applications.Factories;
using novel_tts.Core.Enums;
using novel_tts.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace novel_tts.Applications.Services
{
    public class TtsPipelineManager
    {
        private readonly TtsEngineFactory _engineFactory;

        public TtsPipelineManager(TtsEngineFactory engineFactory)
        {
            _engineFactory = engineFactory;
        }

        public async Task StartTtsPipelineAsync(List<MergeJob> mergeJobs, string audioDirectory, TtsEngineType selectedEngine, CancellationToken cancellationToken)
        {
            var engine = _engineFactory.GetEngine(selectedEngine);

            var ttsBlock = new TransformBlock<MergeJob, AudioJob>(async job =>
            {
                string outputWav = Path.Combine(audioDirectory, Path.GetFileNameWithoutExtension(job.OutputTxtPath) + ".wav");

                var audioJob = new AudioJob
                {
                    MergeJobId = job.Id,
                    OutputAudioPath = outputWav,
                    EngineUsed = selectedEngine,
                    Status = JobStatus.Processing
                };

                // Kiểm tra Resume: Nếu file Audio đã tồn tại và size > 0 thì bỏ qua
                if (File.Exists(outputWav) && new FileInfo(outputWav).Length > 0)
                {
                    audioJob.Status = JobStatus.Completed;
                    return audioJob;
                }

                bool success = await engine.ConvertTextToAudioAsync(job.OutputTxtPath, outputWav, cancellationToken);
                audioJob.Status = success ? JobStatus.Completed : JobStatus.Failed;

                return audioJob;

            }, new ExecutionDataflowBlockOptions
            {
                // RẤT QUAN TRỌNG: TTS ngốn rất nhiều CPU. Chỉ để 2-3 luồng chạy song song.
                MaxDegreeOfParallelism = Environment.ProcessorCount > 4 ? 3 : 2,
                CancellationToken = cancellationToken
            });

            var actionBlock = new ActionBlock<AudioJob>(audioJob =>
            {
                // TODO: Gọi DB cập nhật trạng thái AudioJob
                // Trigger IProgress để update thanh Progress Bar trên UI
            }, new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1,
                CancellationToken = cancellationToken
            });

            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
            ttsBlock.LinkTo(actionBlock, linkOptions);

            foreach (var job in mergeJobs)
            {
                await ttsBlock.SendAsync(job, cancellationToken);
            }

            ttsBlock.Complete();
            await actionBlock.Completion;
        }
    }
}
