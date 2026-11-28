using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyTitle("qInstall")]
[assembly: AssemblyProduct("qInstall")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace Qinstall
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            CleanupOldSelfUpdateBinary();

            // Habilitar TLS 1.2 y conexiones paralelas rapidas para descargas HTTPS modernas en .NET Framework 4.8
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = 32;
            ServicePointManager.Expect100Continue = false;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (CanUseWebView2())
            {
                Application.Run(new WebViewForm());
            }
            else
            {
                Application.Run(new MainForm());
            }
        }

        private static void CleanupOldSelfUpdateBinary()
        {
            try
            {
                string baseDir = AppCatalog.GetAppBaseDirectory();
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return;
                string[] oldFiles = Directory.GetFiles(baseDir, "*.exe.old");
                for (int i = 0; i < oldFiles.Length; i++)
                {
                    try { File.Delete(oldFiles[i]); } catch { }
                }
            }
            catch
            {
            }
        }

        private static bool CanUseWebView2()
        {
            try
            {
                string uiIndex = Path.Combine(AppCatalog.GetAppBaseDirectory(), "ui", "index.html");
                if (!File.Exists(uiIndex)) return false;

                string ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrWhiteSpace(ver);
            }
            catch
            {
                return false;
            }
        }
    }
}
