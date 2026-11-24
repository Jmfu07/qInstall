using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
            Application.Run(new MainForm());
        }
    }
}
