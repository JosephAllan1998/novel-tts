using Microsoft.Extensions.DependencyInjection;
using novel_tts.Applications.Factories;
using novel_tts.Applications.Services;
using novel_tts.Core.Interfaces;
using novel_tts.Infrastructure.Crawlers;
using novel_tts.Infrastructure.Parsers;
using novel_tts.Infrastructure.Repositories;
using novel_tts.Infrastructure.Resilience;
using novel_tts.Infrastructure.Services;
using novel_tts.Infrastructure.TtsEngines;
using novel_tts.Presentation.ViewModels;
using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace novel_tts
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            LoadMissingDLL();

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // Hiển thị MainWindow và tiêm ViewModel vào
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Infrastructure (Singleton cho DB và Logger)
            services.AddSingleton(new LoggerService(baseDir));
            services.AddSingleton<PolicyProvider>();
            services.AddSingleton<INovelRepository>(provider =>
                new SqliteNovelRepository(baseDir, provider.GetRequiredService<LoggerService>()));

            // 2. Engines & Http (Transient)
            services.AddTransient<ICrawlerEngine, TruyenFullCrawler>();
            services.AddTransient<IHtmlParser, TruyenFullParser>();

            // Đăng ký các TTS Engines (Có thể thêm EdgeTtsEngine sau)
            services.AddTransient<ITtsEngine, SystemSpeechTtsEngine>();
            services.AddTransient<TtsEngineFactory>();

            // 3. Application Services (Pipelines)
            services.AddTransient<CrawlPipelineManager>();
            services.AddTransient<MergePipelineManager>();
            services.AddTransient<TtsPipelineManager>();

            // 4. UI: ViewModels & Views
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
        }

        #region Events

        private void LoadMissingDLL()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var requestedAssembly = new AssemblyName(args.Name);
                    string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                    string dllRequire = requestedAssembly.Name + ".dll";
                    string dllPath = Path.Combine(assemblyPath, dllRequire);
                    if (File.Exists(dllPath))
                    {
                        var _assembly = Assembly.LoadFrom(dllPath);
                        return _assembly;
                    }
                    else
                    {
                        Console.WriteLine($"DLL [{dllRequire}] not found in path [{assemblyPath}].");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in AssemblyResolve handler.", ex);
                }
                return null;
            };
        }
        #endregion Events
    }
}
