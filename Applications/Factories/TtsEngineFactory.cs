using novel_tts.Core.Enums;
using novel_tts.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace novel_tts.Applications.Factories
{
    public class TtsEngineFactory
    {
        private readonly IEnumerable<ITtsEngine> _engines;

        // Các engines sẽ được inject thông qua DI Container (Dependency Injection)
        public TtsEngineFactory(IEnumerable<ITtsEngine> engines)
        {
            _engines = engines;
        }

        public ITtsEngine GetEngine(TtsEngineType engineType)
        {
            foreach (var engine in _engines)
            {
                if (engine.EngineType == engineType)
                    return engine;
            }
            throw new NotSupportedException($"TTS Engine {engineType} is not supported or injected.");
        }
    }
}
