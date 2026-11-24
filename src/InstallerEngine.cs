using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Qinstall
{
    public class WingetSearchEntry
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
    }

    public class InstallerEngine
    {
        private readonly object _lock = new object();
        private Process _currentProcess;
        private WebClient _currentWebClient;
        private volatile bool _cancelRequested;

        public event Action<string> LogMessage;
        public event Action<int> DownloadProgress;

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool RelaunchAsAdministrator()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetWingetVersion()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("winget", "--version");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using (Process p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        return output.Trim();
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public void ResetCancellation()
        {
            _cancelRequested = false;
        }

        public void CancelCurrent()
        {
            _cancelRequested = true;
            bool killDenied = false;
            lock (_lock)
            {
                if (_currentWebClient != null)
                {
                    try { _currentWebClient.CancelAsync(); } catch { }
                }
                if (_currentProcess != null)
                {
                    try
                    {
                        if (!_currentProcess.HasExited)
                        {
                            _currentProcess.Kill();
                        }
                    }
                    catch (Win32Exception)
                    {
                        killDenied = true;
                    }
                    catch
                    {
                    }
                }
            }
            EmitLog("[!] Cancelacion solicitada por el usuario.");
            if (killDenied)
            {
                EmitLog("[!] El instalador se esta ejecutando con permisos de Administrador; si su ventana sigue abierta, cierrala manualmente (o ejecuta Qinstall como Admin).");
            }
        }

        public async Task<bool> InstallWingetAsync()
        {
            EmitLog("[*] Descargando App Installer (Winget) desde Microsoft...");
            string tempDir = Path.Combine(Path.GetTempPath(), "Qinstall");
            Directory.CreateDirectory(tempDir);
            string bundlePath = Path.Combine(tempDir, "Microsoft.DesktopAppInstaller.msixbundle");

            bool downloaded = await DownloadFileAsync("https://aka.ms/getwinget", bundlePath);
            if (!downloaded)
            {
                EmitLog("[X] No se pudo descargar Winget.");
                return false;
            }

            EmitLog("[*] Instalando Winget con PowerShell...");
            string psArgs = "-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '" + bundlePath.Replace("'", "''") + "'\"";
            StringBuilder outBuf = new StringBuilder();
            int code = await RunProcessAsync("powershell.exe", psArgs, outBuf);
            TryDeleteFile(bundlePath);

            if (code == 0)
            {
                EmitLog("[OK] Winget instalado correctamente.");
                return true;
            }

            EmitLog("[X] Error instalando Winget (codigo " + code + ").");
            return false;
        }

        public async Task<List<WingetSearchEntry>> SearchWingetAsync(string query)
        {
            List<WingetSearchEntry> results = new List<WingetSearchEntry>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            string safeQuery = query.Replace("\"", string.Empty).Trim();
            string args = "search \"" + safeQuery + "\" --source winget --accept-source-agreements";

            StringBuilder output = new StringBuilder();
            await RunProcessAsync("winget", args, output, false);

            string[] lines = output.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;
            int idCol = -1;
            int verCol = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (line.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Versi", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    headerIdx = i;
                    idCol = FindColumnStart(line, "Id");
                    verCol = FindColumnStart(line, "Version");
                    if (verCol < 0) verCol = FindColumnStart(line, "Versi");
                    break;
                }
            }

            if (headerIdx < 0 || idCol <= 0)
            {
                return results;
            }

            for (int i = headerIdx + 2; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length <= idCol) continue;
                if (line.StartsWith("---")) continue;

                string namePart = line.Substring(0, Math.Min(idCol, line.Length)).Trim();
                string rest = line.Substring(idCol).Trim();
                string[] tokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                string id = tokens[0].Trim();
                string version = tokens.Length > 1 ? tokens[1].Trim() : string.Empty;

                if (id.IndexOf('.') < 0) continue;

                WingetSearchEntry entry = new WingetSearchEntry();
                entry.Name = namePart;
                entry.Id = id;
                entry.Version = version;
                results.Add(entry);

                if (results.Count >= 30) break;
            }

            return results;
        }

        private static int FindColumnStart(string line, string headerWord)
        {
            int idx = line.IndexOf(headerWord, StringComparison.OrdinalIgnoreCase);
            while (idx > 0 && line[idx - 1] != ' ')
            {
                idx = line.IndexOf(headerWord, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
            return idx;
        }

        public async Task<InstallResult> InstallAppAsync(AppItem app, bool silent, bool upgradeIfExists)
        {
            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Cancelado");
            }

            string source = (app.Source ?? "winget").Trim().ToLowerInvariant();
            EmitLog("--------------------------------------------------");
            EmitLog(string.Format("[*] Iniciando: {0} ({1})", app.Name, source.ToUpperInvariant()));

            try
            {
                if (source == "winget")
                {
                    return await InstallViaWingetAsync(app, silent, upgradeIfExists);
                }
                if (source == "url")
                {
                    return await InstallViaUrlAsync(app, silent);
                }
                if (source == "local")
                {
                    return await InstallViaLocalAsync(app, silent);
                }

                return MakeResult(app, InstallStatus.Failed, "Origen desconocido: " + source);
            }
            catch (Exception ex)
            {
                EmitLog("[X] Excepcion en " + app.Name + ": " + ex.Message);
                return MakeResult(app, InstallStatus.Failed, ex.Message);
            }
        }

        private async Task<InstallResult> InstallViaWingetAsync(AppItem app, bool silent, bool upgradeIfExists)
        {
            if (string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "ID de Winget vacio");
            }

            string silentFlag = silent ? " --silent" : string.Empty;
            string args = string.Format(
                "install --id \"{0}\" --exact --source winget{1} --accept-package-agreements --accept-source-agreements --disable-interactivity",
                app.Target.Trim(),
                silentFlag);

            StringBuilder output = new StringBuilder();
            int exitCode = await RunProcessAsync("winget", args, output);

            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Cancelado por el usuario");
            }

            string text = output.ToString();

            // Codigos tipicos de Winget:
            // 0 = Exito
            // -1978335189 (0x8A15002B) = No applicable update / ya actualizado
            // -1978335135 (0x8A150061) = Package already installed
            // -1978335212 (0x8A150014) = Package not found
            if (exitCode == 0)
            {
                EmitLog("[OK] " + app.Name + " instalado correctamente.");
                return MakeResult(app, InstallStatus.Success, "Instalado correctamente");
            }

            bool looksAlreadyInstalled =
                exitCode == -1978335189 ||
                exitCode == -1978335135 ||
                text.IndexOf("already installed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ya est", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("No available upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("No applicable update", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksAlreadyInstalled)
            {
                if (upgradeIfExists && exitCode == -1978335135)
                {
                    EmitLog("[*] Ya estaba instalado. Comprobando actualizaciones para " + app.Name + "...");
                    string upArgs = string.Format(
                        "upgrade --id \"{0}\" --exact --source winget{1} --accept-package-agreements --accept-source-agreements --disable-interactivity",
                        app.Target.Trim(),
                        silentFlag);
                    StringBuilder upOutput = new StringBuilder();
                    int upCode = await RunProcessAsync("winget", upArgs, upOutput);
                    if (_cancelRequested)
                    {
                        return MakeResult(app, InstallStatus.Cancelled, "Cancelado por el usuario");
                    }
                    if (upCode == 0)
                    {
                        EmitLog("[OK] " + app.Name + " actualizado correctamente.");
                        return MakeResult(app, InstallStatus.Success, "Actualizado a la ultima version");
                    }
                }

                EmitLog("[=] " + app.Name + " ya esta instalado y al dia.");
                return MakeResult(app, InstallStatus.AlreadyInstalled, "Ya instalado y actualizado");
            }

            if (exitCode == -1978335212)
            {
                EmitLog("[X] No se encontro el paquete '" + app.Target + "' en Winget.");
                return MakeResult(app, InstallStatus.Failed, "Paquete no encontrado en Winget (" + app.Target + ")");
            }

            EmitLog(string.Format("[X] Error instalando {0} (codigo {1}).", app.Name, exitCode));
            return MakeResult(app, InstallStatus.Failed, "Error de Winget (codigo " + exitCode + ")");
        }

        private async Task<InstallResult> InstallViaUrlAsync(AppItem app, bool silent)
        {
            if (string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "URL vacia");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "Qinstall");
            Directory.CreateDirectory(tempDir);

            string ext = ".exe";
            try
            {
                Uri uri = new Uri(app.Target.Trim());
                string urlExt = Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(urlExt) && urlExt.Length <= 5)
                {
                    ext = urlExt;
                }
            }
            catch
            {
                return MakeResult(app, InstallStatus.Failed, "URL invalida: " + app.Target);
            }

            string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
            string localFile = Path.Combine(tempDir, safeId + ext);

            EmitLog("[*] Descargando desde: " + app.Target);
            bool ok = await DownloadFileAsync(app.Target.Trim(), localFile);
            if (_cancelRequested)
            {
                TryDeleteFile(localFile);
                return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
            }
            if (!ok)
            {
                TryDeleteFile(localFile);
                return MakeResult(app, InstallStatus.Failed, "Fallo la descarga desde URL");
            }

            InstallResult res = await ExecuteInstallerFileAsync(app, localFile, silent);
            TryDeleteFile(localFile);
            return res;
        }

        private async Task<InstallResult> InstallViaLocalAsync(AppItem app, bool silent)
        {
            if (string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "Ruta local vacia");
            }

            string path = ResolveLocalPath(app.Target.Trim());
            if (string.IsNullOrEmpty(path))
            {
                EmitLog("[X] Archivo no encontrado en la carpeta local ni en unidades USB conectadas: " + app.Target);
                return MakeResult(app, InstallStatus.Failed, "No se encontro en el pendrive: " + app.Target);
            }

            EmitLog("[*] Instalador local detectado: " + path);
            return await ExecuteInstallerFileAsync(app, path, silent);
        }

        private static string ResolveLocalPath(string target)
        {
            if (Path.IsPathRooted(target))
            {
                return File.Exists(target) ? target : null;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    baseDir = Path.GetDirectoryName(loc);
                }
            }
            catch
            {
            }

            // 1. Relativo a la carpeta donde esta Qinstall.exe
            string candidate = Path.Combine(baseDir, target);
            if (File.Exists(candidate)) return candidate;

            // 2. Relativo a la raiz de la unidad donde esta Qinstall.exe (ej. E:\)
            try
            {
                string root = Path.GetPathRoot(baseDir);
                if (!string.IsNullOrEmpty(root))
                {
                    candidate = Path.Combine(root, target);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch
            {
            }

            // 3. Buscar automaticamente en cualquier pendrive USB conectado
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    if (d.DriveType == DriveType.Removable && d.IsReady)
                    {
                        candidate = Path.Combine(d.RootDirectory.FullName, target);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task<InstallResult> ExecuteInstallerFileAsync(AppItem app, string filePath, bool silent)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string fileToRun;
            string args;

            if (ext == ".msi")
            {
                fileToRun = "msiexec.exe";
                string extra = !string.IsNullOrWhiteSpace(app.SilentArgs)
                    ? app.SilentArgs.Trim()
                    : (silent ? "/qn /norestart" : "/qb /norestart");
                args = string.Format("/i \"{0}\" {1}", filePath, extra);
            }
            else
            {
                fileToRun = filePath;
                string configuredArgs = (app.SilentArgs ?? string.Empty).Trim();
                bool isRequiredConfigArg = configuredArgs.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0;
                args = (silent || isRequiredConfigArg) ? configuredArgs : string.Empty;
            }

            EmitLog(string.Format("[*] Ejecutando: {0} {1}", Path.GetFileName(fileToRun), args));
            StringBuilder output = new StringBuilder();
            int exitCode = await RunProcessAsync(fileToRun, args, output, true, ext != ".msi");

            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Instalacion cancelada");
            }

            // 0 = OK, 3010 = OK (reinicio requerido)
            if (exitCode == 0 || exitCode == 3010)
            {
                string msg = exitCode == 3010 ? "Instalado (requiere reiniciar)" : "Instalado correctamente";
                EmitLog("[OK] " + app.Name + " - " + msg);
                return MakeResult(app, InstallStatus.Success, msg);
            }

            EmitLog(string.Format("[X] El instalador de {0} devolvio codigo {1}.", app.Name, exitCode));
            return MakeResult(app, InstallStatus.Failed, "Codigo de salida: " + exitCode);
        }

        private Task<bool> DownloadFileAsync(string url, string destinationPath)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            WebClient client = new WebClient();
            client.Headers.Add("User-Agent", "Qinstall/1.0 (Windows)");

            lock (_lock)
            {
                _currentWebClient = client;
            }

            client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
            {
                Action<int> handler = DownloadProgress;
                if (handler != null)
                {
                    handler(e.ProgressPercentage);
                }
            };

            client.DownloadFileCompleted += delegate(object sender, AsyncCompletedEventArgs e)
            {
                lock (_lock)
                {
                    _currentWebClient = null;
                }
                client.Dispose();

                if (e.Cancelled)
                {
                    tcs.TrySetResult(false);
                }
                else if (e.Error != null)
                {
                    EmitLog("[X] Error de descarga: " + e.Error.Message);
                    tcs.TrySetResult(false);
                }
                else
                {
                    EmitLog("[OK] Descarga finalizada.");
                    tcs.TrySetResult(true);
                }
            };

            try
            {
                client.DownloadFileAsync(new Uri(url), destinationPath);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _currentWebClient = null;
                }
                client.Dispose();
                EmitLog("[X] No se pudo iniciar la descarga: " + ex.Message);
                tcs.TrySetResult(false);
            }

            return tcs.Task;
        }

        private Task<int> RunProcessAsync(string fileName, string arguments, StringBuilder outputBuffer, bool logOutput = true, bool allowShellElevateFallback = false)
        {
            return Task.Run(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = fileName;
                    psi.Arguments = arguments;
                    if (Path.IsPathRooted(fileName) && File.Exists(fileName))
                    {
                        psi.WorkingDirectory = Path.GetDirectoryName(fileName);
                    }
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;

                    using (Process proc = new Process())
                    {
                        proc.StartInfo = psi;
                        proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data))
                            {
                                string cleaned = CleanWingetLine(e.Data);
                                if (!string.IsNullOrWhiteSpace(cleaned))
                                {
                                    outputBuffer.AppendLine(cleaned);
                                    if (logOutput) EmitLog("    " + cleaned);
                                }
                            }
                        };
                        proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data))
                            {
                                string cleaned = CleanWingetLine(e.Data);
                                if (!string.IsNullOrWhiteSpace(cleaned))
                                {
                                    outputBuffer.AppendLine(cleaned);
                                    if (logOutput) EmitLog("    " + cleaned);
                                }
                            }
                        };

                        try
                        {
                            proc.Start();
                        }
                        catch (Win32Exception wex)
                        {
                            // 740 = requiere elevacion UAC
                            if (allowShellElevateFallback && wex.NativeErrorCode == 740)
                            {
                                EmitLog("[!] El instalador requiere permisos de Administrador (UAC)...");
                                return RunElevatedShell(fileName, arguments);
                            }
                            throw;
                        }

                        lock (_lock)
                        {
                            _currentProcess = proc;
                        }

                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        while (!proc.WaitForExit(250))
                        {
                            if (_cancelRequested)
                            {
                                try { proc.Kill(); } catch { }
                                break;
                            }
                        }

                        lock (_lock)
                        {
                            _currentProcess = null;
                        }

                        if (_cancelRequested) return -1;
                        return proc.ExitCode;
                    }
                }
                catch (Exception ex)
                {
                    if (logOutput) EmitLog("[X] Error al ejecutar proceso: " + ex.Message);
                    return -1;
                }
            });
        }

        private int RunElevatedShell(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            {
                psi.WorkingDirectory = Path.GetDirectoryName(fileName);
            }
            psi.UseShellExecute = true;
            psi.Verb = "runas";

            using (Process proc = Process.Start(psi))
            {
                if (proc == null) return -1;
                lock (_lock)
                {
                    _currentProcess = proc;
                }
                while (!proc.WaitForExit(250))
                {
                    if (_cancelRequested)
                    {
                        try { proc.Kill(); } catch { }
                        break;
                    }
                }
                lock (_lock)
                {
                    _currentProcess = null;
                }
                if (_cancelRequested) return -1;
                return proc.ExitCode;
            }
        }

        private static string CleanWingetLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            string trimmed = line.Trim();
            // Filtrar spinners de progreso de winget (-, \, |, /)
            if (trimmed.Length == 1 && (trimmed == "-" || trimmed == "\\" || trimmed == "|" || trimmed == "/"))
            {
                return string.Empty;
            }
            return trimmed;
        }

        private static string SanitizeFileName(string raw)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j]) { bad = true; break; }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private void EmitLog(string message)
        {
            Action<string> handler = LogMessage;
            if (handler != null)
            {
                handler(message);
            }
        }

        private static InstallResult MakeResult(AppItem app, InstallStatus status, string msg)
        {
            InstallResult r = new InstallResult();
            r.App = app;
            r.Status = status;
            r.Message = msg;
            return r;
        }
    }
}
