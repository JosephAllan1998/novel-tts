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
        protected override void OnStartup(StartupEventArgs e)
        {
            LoadMissingDLL();
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
