using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

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

            // Habilitar TLS 1.2 para descargas HTTPS modernas en .NET Framework 4.8
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

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
