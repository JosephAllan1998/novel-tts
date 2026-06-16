using novel_tts.Core.Enums;
using novel_tts.Core.Interfaces;
using novel_tts.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace novel_tts.Infrastructure.TtsEngines
{
    public class SystemSpeechTtsEngine : ITtsEngine
    {
        public TtsEngineType EngineType => TtsEngineType.SystemSpeech;
        private readonly LoggerService _logger;

        public SystemSpeechTtsEngine(LoggerService logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConvertTextToAudioAsync(string inputTxtPath, string outputAudioPath, CancellationToken cancellationToken)
        {
            string inputLog = $"Input: {inputTxtPath}, Output: {outputAudioPath}";
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(inputTxtPath)) throw new FileNotFoundException("TXT file not found.");

                    // Đảm bảo thư mục Audio tồn tại
                    string dir = Path.GetDirectoryName(outputAudioPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string textContent = File.ReadAllText(inputTxtPath, System.Text.Encoding.UTF8);

                    using (var synthesizer = new SpeechSynthesizer())
                    {
                        // Set giọng đọc tiếng Việt (Nếu máy tính có cài Voice Pack tiếng Việt)
                        // Nếu không có, nó sẽ dùng giọng mặc định của Windows (thường là tiếng Anh)
                        synthesizer.SelectVoiceByHints(VoiceGender.Female, VoiceAge.Adult, 0, new System.Globalization.CultureInfo("vi-VN"));

                        // Cấu hình đầu ra thành file WAV
                        synthesizer.SetOutputToWaveFile(outputAudioPath);

                        // Xử lý CancellationToken để dừng khẩn cấp
                        cancellationToken.Register(() => synthesizer.SpeakAsyncCancelAll());

                        synthesizer.Speak(textContent);
                    }

                    _logger.LogInfo("tts.log", "ConvertTextToAudioAsync", "Conversion successful.", inputLog);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInfo("tts.log", "ConvertTextToAudioAsync", "TTS Conversion was cancelled by user.", inputLog);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError("SystemSpeechTtsEngine.Convert", ex, inputLog);
                    return false;
                }
            }, cancellationToken);
        }
    }
}
