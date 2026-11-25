using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Qinstall
{
    public class WingetSearchEntry
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
    }

    public class OfflineCacheUpdateInfo
    {
        public AppItem App { get; set; }
        public string LocalVersion { get; set; }
        public string LatestVersion { get; set; }
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

        public static Task<bool> CheckInternetAsync()
        {
            return Task.Run(delegate
            {
                try
                {
                    if (!NetworkInterface.GetIsNetworkAvailable())
                    {
                        return false;
                    }
                }
                catch
                {
                }

                string[] hosts = new[] { "www.msftconnecttest.com", "1.1.1.1" };
                int[] ports = new[] { 80, 443 };

                for (int i = 0; i < hosts.Length; i++)
                {
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(hosts[i], ports[i], null, null);
                            if (ar.AsyncWaitHandle.WaitOne(2000, false) && client.Connected)
                            {
                                client.EndConnect(ar);
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                return false;
            });
        }

        public static bool AppRequiresInternet(AppItem app)
        {
            if (app == null) return false;
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "winget" || src == "url")
            {
                return !HasOfflineCache(app);
            }
            if (src == "powershell")
            {
                string cmd = app.Target ?? string.Empty;
                return cmd.IndexOf("http://", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       cmd.IndexOf("https://", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       cmd.IndexOf("wsreset", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (src == "local")
            {
                // Office Deployment Tool (/configure) requiere Internet a menos que exista la carpeta offline Office\Data
                string args = app.SilentArgs ?? string.Empty;
                if (args.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return !HasOfflineOfficePayload(app);
                }
                return false;
            }
            return true;
        }

        public static bool CanDownloadOffline(AppItem app)
        {
            if (app == null) return false;
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "winget" || src == "url") return true;
            if (src == "local")
            {
                string args = app.SilentArgs ?? string.Empty;
                return args.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        public static string GetOfflineCacheDirForApp(AppItem app)
        {
            string safeKey = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
            return Path.Combine(AppCatalog.GetLocalInstallersDirectory(), "cache", safeKey);
        }

        public static bool HasOfflineCache(AppItem app)
        {
            if (app == null) return false;
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "local")
            {
                string args = app.SilentArgs ?? string.Empty;
                if (args.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return HasOfflineOfficePayload(app);
                }
                return !string.IsNullOrEmpty(ResolveLocalPath((app.Target ?? string.Empty).Trim()));
            }
            return !string.IsNullOrEmpty(FindCachedInstallerPath(app));
        }

        private static bool HasOfflineOfficePayload(AppItem app)
        {
            string resolved = ResolveLocalPath((app.Target ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(resolved)) return false;
            string dir = Path.GetDirectoryName(resolved);
            string offlineData = Path.Combine(dir, "Office", "Data");
            return Directory.Exists(offlineData);
        }

        public static string FindCachedInstallerPath(AppItem app)
        {
            if (app == null) return null;
            string safeKey = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
            string relDir = Path.Combine("local", "cache", safeKey);

            // Buscar primero en carpeta local del ejecutable, luego raiz y unidades USB
            List<string> candidateDirs = new List<string>();
            string baseDir = AppCatalog.GetAppBaseDirectory();
            candidateDirs.Add(Path.Combine(baseDir, relDir));
            try
            {
                string root = Path.GetPathRoot(baseDir);
                if (!string.IsNullOrEmpty(root)) candidateDirs.Add(Path.Combine(root, relDir));
            }
            catch { }
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    if (drives[i].DriveType == DriveType.Removable && drives[i].IsReady)
                    {
                        candidateDirs.Add(Path.Combine(drives[i].RootDirectory.FullName, relDir));
                    }
                }
            }
            catch { }

            for (int d = 0; d < candidateDirs.Count; d++)
            {
                string dir = candidateDirs[d];
                if (!Directory.Exists(dir)) continue;
                try
                {
                    string[] files = Directory.GetFiles(dir);
                    string fallbackExe = null;
                    for (int f = 0; f < files.Length; f++)
                    {
                        string ext = Path.GetExtension(files[f]).ToLowerInvariant();
                        if (ext == ".msi" || ext == ".exe")
                        {
                            FileInfo fi = new FileInfo(files[f]);
                            if (fi.Length > 0)
                            {
                                if (ext == ".msi") return files[f];
                                fallbackExe = files[f];
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(fallbackExe)) return fallbackExe;
                }
                catch { }
            }
            return null;
        }

        public static string GetCachedOfflineVersion(AppItem app)
        {
            string installerPath = FindCachedInstallerPath(app);
            if (string.IsNullOrEmpty(installerPath)) return string.Empty;

            // 1. Leer PackageVersion del manifiesto .yaml guardado por winget download
            try
            {
                string dir = Path.GetDirectoryName(installerPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string[] yamls = Directory.GetFiles(dir, "*.yaml");
                    for (int i = 0; i < yamls.Length; i++)
                    {
                        string[] lines = File.ReadAllLines(yamls[i], Encoding.UTF8);
                        for (int l = 0; l < lines.Length; l++)
                        {
                            string trimmed = lines[l].Trim();
                            if (trimmed.StartsWith("PackageVersion:", StringComparison.OrdinalIgnoreCase))
                            {
                                string val = trimmed.Substring(15).Trim().Trim('\'', '"');
                                if (!string.IsNullOrEmpty(val)) return val;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 2. Respaldo: FileVersionInfo del binario .exe/.msi
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(installerPath);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion)) return fvi.ProductVersion.Trim();
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion)) return fvi.FileVersion.Trim();
            }
            catch
            {
            }

            return string.Empty;
        }

        public async Task<string> GetWingetLatestVersionAsync(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return string.Empty;
            string safeId = packageId.Replace("\"", string.Empty).Trim();
            string args = string.Format(
                "show --id \"{0}\" --exact --source winget --accept-source-agreements --disable-interactivity",
                safeId);

            string output = await RunQuickQueryAsync("winget", args, 8000);
            if (string.IsNullOrEmpty(output)) return string.Empty;

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Versi", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    if (colon >= 0)
                    {
                        string v = line.Substring(colon + 1).Trim();
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
            return string.Empty;
        }

        public static bool IsVersionOlder(string localVersion, string latestVersion)
        {
            if (string.IsNullOrWhiteSpace(localVersion) || string.IsNullOrWhiteSpace(latestVersion))
            {
                return false;
            }

            string a = localVersion.Trim();
            string b = latestVersion.Trim();
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;

            string[] partsA = a.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] partsB = b.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int max = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < max; i++)
            {
                string pa = i < partsA.Length ? partsA[i] : "0";
                string pb = i < partsB.Length ? partsB[i] : "0";

                long na;
                long nb;
                if (long.TryParse(pa, out na) && long.TryParse(pb, out nb))
                {
                    if (na < nb) return true;
                    if (na > nb) return false;
                }
                else
                {
                    int cmp = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                    if (cmp < 0) return true;
                    if (cmp > 0) return false;
                }
            }

            return false;
        }

        public async Task<Dictionary<string, OfflineCacheUpdateInfo>> CheckOutdatedOfflineCachesAsync(List<AppItem> apps)
        {
            Dictionary<string, OfflineCacheUpdateInfo> outdated = new Dictionary<string, OfflineCacheUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            if (apps == null || apps.Count == 0) return outdated;

            for (int i = 0; i < apps.Count; i++)
            {
                AppItem app = apps[i];
                string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
                if (src != "winget" || string.IsNullOrWhiteSpace(app.Target)) continue;
                if (!HasOfflineCache(app)) continue;

                string localVer = GetCachedOfflineVersion(app);
                if (string.IsNullOrEmpty(localVer)) continue;

                string latestVer = await GetWingetLatestVersionAsync(app.Target);
                if (IsVersionOlder(localVer, latestVer))
                {
                    string key = (app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty);
                    OfflineCacheUpdateInfo info = new OfflineCacheUpdateInfo();
                    info.App = app;
                    info.LocalVersion = localVer;
                    info.LatestVersion = latestVer;
                    outdated[key] = info;
                }
            }

            return outdated;
        }

        private static string ExtractSilentArgsFromCachedYaml(string installerPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(installerPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return string.Empty;
                string[] yamls = Directory.GetFiles(dir, "*.yaml");
                for (int i = 0; i < yamls.Length; i++)
                {
                    string[] lines = File.ReadAllLines(yamls[i], Encoding.UTF8);
                    for (int l = 0; l < lines.Length; l++)
                    {
                        string trimmed = lines[l].Trim();
                        if (trimmed.StartsWith("Silent:", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = trimmed.Substring(7).Trim().Trim('\'', '"');
                            if (!string.IsNullOrEmpty(val)) return val;
                        }
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        public async Task<InstallResult> DownloadForOfflineAsync(AppItem app)
        {
            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Cancelado");
            }

            string source = (app.Source ?? "winget").Trim().ToLowerInvariant();
            EmitLog("--------------------------------------------------");
            EmitLog(string.Format("[*] Descargando para modo offline: {0}", app.Name));

            try
            {
                if (source == "winget")
                {
                    string cacheDir = GetOfflineCacheDirForApp(app);
                    Directory.CreateDirectory(cacheDir);
                    ClearDirectoryFiles(cacheDir);

                    string pkgSource = GetWingetSourceName(app.Target);
                    string extraMsStore = pkgSource == "msstore" ? " --skip-microsoft-store-package-license" : string.Empty;
                    string args = string.Format(
                        "download --id \"{0}\" --exact --source {1} -d \"{2}\" --accept-package-agreements --accept-source-agreements --skip-dependencies{3} --disable-interactivity",
                        app.Target.Trim(),
                        pkgSource,
                        cacheDir,
                        extraMsStore);

                    StringBuilder output = new StringBuilder();
                    int code = await RunProcessAsync("winget", args, output);
                    if (_cancelRequested)
                    {
                        return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                    }

                    string downloadedFile = FindCachedInstallerPath(app);
                    if (code == 0 && !string.IsNullOrEmpty(downloadedFile))
                    {
                        EmitLog("[OK] Guardado offline en: " + downloadedFile);
                        return MakeResult(app, InstallStatus.Success, "Guardado en local\\cache (" + Path.GetFileName(downloadedFile) + ")");
                    }

                    EmitLog("[X] No se pudo descargar instalador offline para " + app.Name + " (codigo " + code + ").");
                    return MakeResult(app, InstallStatus.Failed, "Winget no pudo descargar el instalador (codigo " + code + ")");
                }

                if (source == "url")
                {
                    string cacheDir = GetOfflineCacheDirForApp(app);
                    Directory.CreateDirectory(cacheDir);
                    ClearDirectoryFiles(cacheDir);

                    string ext = ".exe";
                    try
                    {
                        Uri uri = new Uri(app.Target.Trim());
                        string urlExt = Path.GetExtension(uri.AbsolutePath);
                        if (!string.IsNullOrWhiteSpace(urlExt) && urlExt.Length <= 5) ext = urlExt;
                    }
                    catch
                    {
                        return MakeResult(app, InstallStatus.Failed, "URL invalida");
                    }

                    string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
                    string destFile = Path.Combine(cacheDir, safeId + ext);
                    bool ok = await DownloadFileAsync(app.Target.Trim(), destFile);
                    if (_cancelRequested)
                    {
                        TryDeleteFile(destFile);
                        return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                    }
                    if (ok && File.Exists(destFile))
                    {
                        EmitLog("[OK] Guardado offline en: " + destFile);
                        return MakeResult(app, InstallStatus.Success, "Guardado en local\\cache (" + Path.GetFileName(destFile) + ")");
                    }
                    return MakeResult(app, InstallStatus.Failed, "Fallo la descarga desde URL");
                }

                if (source == "local")
                {
                    string args = (app.SilentArgs ?? string.Empty).Trim();
                    if (args.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string setupPath = ResolveLocalPath((app.Target ?? string.Empty).Trim());
                        if (string.IsNullOrEmpty(setupPath))
                        {
                            return MakeResult(app, InstallStatus.Failed, "No se encontro office\\setup.exe");
                        }
                        string dlArgs = args.Replace("/configure", "/download").Replace("/CONFIGURE", "/download");
                        EmitLog("[*] Descargando archivos offline de Office (puede tardar varios minutos)...");
                        StringBuilder outBuf = new StringBuilder();
                        int code = await RunProcessAsync(setupPath, dlArgs, outBuf, true, true);
                        if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                        if (code == 0)
                        {
                            EmitLog("[OK] Archivos offline de Office guardados en office\\Office\\Data.");
                            return MakeResult(app, InstallStatus.Success, "Paquete offline de Office descargado");
                        }
                        return MakeResult(app, InstallStatus.Failed, "Error descargando Office (codigo " + code + ")");
                    }

                    return MakeResult(app, InstallStatus.AlreadyInstalled, "Ya es un instalador local offline");
                }

                return MakeResult(app, InstallStatus.Failed, "Esta herramienta requiere Internet y no se descarga como instalador offline");
            }
            catch (Exception ex)
            {
                EmitLog("[X] Error descargando " + app.Name + ": " + ex.Message);
                return MakeResult(app, InstallStatus.Failed, ex.Message);
            }
        }

        private static void ClearDirectoryFiles(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch
            {
            }
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

        public async Task<HashSet<string>> DetectInstalledAppKeysAsync(List<AppItem> apps)
        {
            HashSet<string> installedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (apps == null || apps.Count == 0) return installedKeys;

            // 1. Recolectar nombres e IDs desde el Registro de Windows (HKLM 64/32 y HKCU)
            HashSet<string> installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectRegistryDisplayNames(installedNames);

            // 2. Recolectar TODOS los paquetes instalados desde Winget (incluye Winget, ARP y Microsoft Store/MSIX)
            try
            {
                StringBuilder output = new StringBuilder();
                int code = await RunProcessAsync("winget", "list --accept-source-agreements --disable-interactivity", output, false);
                if (code == 0 || output.Length > 0)
                {
                    string[] lines = output.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int headerIdx = -1;
                    int idCol = -1;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (line.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Versi", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            headerIdx = i;
                            idCol = FindColumnStart(line, "Id");
                            break;
                        }
                    }

                    if (headerIdx >= 0 && idCol > 0)
                    {
                        for (int i = headerIdx + 2; i < lines.Length; i++)
                        {
                            string line = lines[i];
                            if (line.Length <= idCol || line.StartsWith("---")) continue;

                            string namePart = line.Substring(0, Math.Min(idCol, line.Length)).Trim();
                            if (!string.IsNullOrWhiteSpace(namePart))
                            {
                                installedNames.Add(namePart);
                            }

                            string rest = line.Substring(idCol).Trim();
                            string[] tokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length > 0)
                            {
                                installedIds.Add(tokens[0].Trim());
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 3. Detectar ediciones de Microsoft Office Click-to-Run en el Registro de Windows
            string officeReleaseIds = ReadOfficeReleaseIds();
            bool hasDirectX9 = File.Exists(Path.Combine(Environment.SystemDirectory, "d3dx9_43.dll"));

            // 4. Evaluar cada aplicacion del catalogo sin depender de archivos de logs
            for (int i = 0; i < apps.Count; i++)
            {
                AppItem app = apps[i];
                if (IsAppDetectedAsInstalled(app, installedIds, installedNames, officeReleaseIds, hasDirectX9))
                {
                    installedKeys.Add((app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty));
                }
            }

            return installedKeys;
        }

        private static bool IsAppDetectedAsInstalled(
            AppItem app,
            HashSet<string> installedIds,
            HashSet<string> installedNames,
            string officeReleaseIds,
            bool hasDirectX9)
        {
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            string target = (app.Target ?? string.Empty).Trim();
            string id = (app.Id ?? string.Empty).Trim().ToLowerInvariant();
            string name = (app.Name ?? string.Empty).Trim();

            // Coincidencia exacta por ID de Winget
            if (src == "winget" && !string.IsNullOrEmpty(target) && installedIds.Contains(target))
            {
                return true;
            }

            // Office local (Click-to-Run)
            if (src == "local")
            {
                string args = (app.SilentArgs ?? string.Empty).ToLowerInvariant();
                if (!string.IsNullOrEmpty(officeReleaseIds))
                {
                    if (args.IndexOf("office2024", StringComparison.OrdinalIgnoreCase) >= 0)
                        return officeReleaseIds.IndexOf("2024", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (args.IndexOf("office2021", StringComparison.OrdinalIgnoreCase) >= 0)
                        return officeReleaseIds.IndexOf("2021", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (args.IndexOf("office2019", StringComparison.OrdinalIgnoreCase) >= 0)
                        return officeReleaseIds.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (args.IndexOf("office2016", StringComparison.OrdinalIgnoreCase) >= 0)
                        return officeReleaseIds.IndexOf("ProPlusRetail", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                return false;
            }

            // Reglas especificas seguras para apps de Store / ARP / versiones
            if (id == "directx") return hasDirectX9;
            if (id == "chrome") return AnyNameStartsWith(installedNames, "Google Chrome");
            if (id == "firefox") return AnyNameStartsWith(installedNames, "Mozilla Firefox");
            if (id == "brave") return AnyNameStartsWith(installedNames, "Brave");
            if (id == "operagx") return AnyNameStartsWith(installedNames, "Opera GX");
            if (id == "7zip") return AnyNameStartsWith(installedNames, "7-Zip");
            if (id == "winrar") return AnyNameStartsWith(installedNames, "WinRAR");
            if (id == "utorrent") return AnyNameStartsWith(installedNames, "uTorrent") || AnyNameStartsWith(installedNames, "\u00b5Torrent");
            if (id == "qbittorrent") return AnyNameStartsWith(installedNames, "qBittorrent");
            if (id == "everything") return AnyNameEqualsOrPrefix(installedNames, "Everything");
            if (id == "powertoys") return AnyNameStartsWith(installedNames, "PowerToys") || AnyNameStartsWith(installedNames, "Microsoft PowerToys");
            if (id == "rufus") return AnyNameEqualsOrPrefix(installedNames, "Rufus");
            if (id == "crystaldisk") return AnyNameStartsWith(installedNames, "CrystalDiskInfo");
            if (id == "cpuz") return AnyNameStartsWith(installedNames, "CPUID CPU-Z") || AnyNameEqualsOrPrefix(installedNames, "CPU-Z");
            if (id == "treesize") return AnyNameStartsWith(installedNames, "TreeSize Free");
            if (id == "wiztree") return AnyNameStartsWith(installedNames, "WizTree");
            if (id == "shutup10") return AnyNameStartsWith(installedNames, "O&O ShutUp10");
            if (id == "winaero") return AnyNameStartsWith(installedNames, "Winaero Tweaker");
            if (id == "bleachbit") return AnyNameStartsWith(installedNames, "BleachBit");
            if (id == "bcuninstaller") return AnyNameStartsWith(installedNames, "Bulk Crap Uninstaller") || AnyNameStartsWith(installedNames, "BCUninstaller");
            if (id == "revo") return AnyNameStartsWith(installedNames, "Revo Uninstaller");
            if (id == "autoruns") return AnyNameStartsWith(installedNames, "Autoruns");
            if (id == "processlasso") return AnyNameStartsWith(installedNames, "Process Lasso");
            if (id == "windhawk") return AnyNameEqualsOrPrefix(installedNames, "Windhawk");
            if (id == "efibooteditor") return AnyNameStartsWith(installedNames, "EFI Boot Editor");
            if (id == "partboot") return AnyNameEqualsOrPrefix(installedNames, "PartBoot");
            if (id == "diskgenius") return AnyNameStartsWith(installedNames, "DiskGenius");
            if (id == "testdisk") return AnyNameStartsWith(installedNames, "TestDisk");
            if (id == "aomei_partition") return AnyNameStartsWith(installedNames, "AOMEI Partition Assistant");
            if (id == "minitool_partition") return AnyNameStartsWith(installedNames, "MiniTool Partition Wizard");
            if (id == "ventoy") return AnyNameEqualsOrPrefix(installedNames, "Ventoy");
            if (src == "powershell") return false;
            if (id == "vlc") return AnyNameStartsWith(installedNames, "VLC media player");
            if (id == "spotify") return AnyNameEqualsOrPrefix(installedNames, "Spotify") || AnyIdContains(installedIds, "SpotifyAB.SpotifyMusic");
            if (id == "obs") return AnyNameStartsWith(installedNames, "OBS Studio");
            if (id == "sharex") return AnyNameEqualsOrPrefix(installedNames, "ShareX");
            if (id == "gimp") return AnyNameEqualsOrPrefix(installedNames, "GIMP");
            if (id == "paintnet") return AnyNameEqualsOrPrefix(installedNames, "paint.net");
            if (id == "discord") return AnyNameEqualsOrPrefix(installedNames, "Discord") || AnyIdContains(installedIds, "XPDC2RH70K22MN");
            if (id == "whatsapp") return AnyNameEqualsOrPrefix(installedNames, "WhatsApp") || AnyIdContains(installedIds, "5319275A.WhatsAppDesktop") || AnyIdContains(installedIds, "9NKSQGP7F2NH");
            if (id == "telegram") return AnyNameStartsWith(installedNames, "Telegram Desktop");
            if (id == "zoom") return AnyNameEqualsOrPrefix(installedNames, "Zoom");
            if (id == "libreoffice") return AnyNameStartsWith(installedNames, "LibreOffice");
            if (id == "sumatrapdf") return AnyNameStartsWith(installedNames, "SumatraPDF");
            if (id == "adobereader") return AnyNameStartsWith(installedNames, "Adobe Acrobat") || AnyNameStartsWith(installedNames, "Adobe Reader");
            if (id == "obsidian") return AnyNameEqualsOrPrefix(installedNames, "Obsidian");
            if (id == "gemini") return AnyNameEqualsOrPrefix(installedNames, "Google Gemini") || AnyNameEqualsOrPrefix(installedNames, "Gemini");
            if (id == "antigravity") return AnyNameEqualsOrPrefix(installedNames, "Antigravity") && !AnyExactPrefixOnly(installedNames, "Antigravity IDE", "Antigravity CLI");
            if (id == "antigravity_ide") return AnyNameStartsWith(installedNames, "Antigravity IDE");
            if (id == "antigravity_cli") return AnyNameStartsWith(installedNames, "Antigravity CLI");
            if (id == "opencode") return AnyNameEqualsOrPrefix(installedNames, "OpenCode") && !AnyExactPrefixOnly(installedNames, "OpenCode CLI", "OpenCode CLI");
            if (id == "opencode_cli") return AnyNameStartsWith(installedNames, "OpenCode CLI");
            if (id == "copilot_app") return AnyNameEqualsOrPrefix(installedNames, "GitHub Copilot") && !AnyExactPrefixOnly(installedNames, "GitHub Copilot CLI", "GitHub Copilot CLI");
            if (id == "copilot_cli") return AnyNameStartsWith(installedNames, "GitHub Copilot CLI");
            if (id == "vscode") return AnyNameStartsWith(installedNames, "Microsoft Visual Studio Code") || AnyNameStartsWith(installedNames, "Visual Studio Code");
            if (id == "github_desktop") return AnyNameStartsWith(installedNames, "GitHub Desktop");
            if (id == "git") return AnyNameEqualsOrPrefix(installedNames, "Git");
            if (id == "notepadpp") return AnyNameStartsWith(installedNames, "Notepad++");
            if (id == "python") return AnyNameStartsWith(installedNames, "Python 3.");
            if (id == "nodejs") return AnyNameStartsWith(installedNames, "Node.js");
            if (id == "steam") return installedNames.Contains("Steam");
            if (id == "epic") return AnyNameStartsWith(installedNames, "Epic Games Launcher");
            if (id == "vcredist") return AnyNameMatchesAll(installedNames, "Visual C++", "(x64)", "14.") || AnyNameMatchesAll(installedNames, "Visual C++", "2015-2022", "x64");
            if (id == "vcredist_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "(x86)", "14.") || AnyNameMatchesAll(installedNames, "Visual C++", "2015-2022", "x86");
            if (id == "vcredist_2013_x64") return AnyNameMatchesAll(installedNames, "Visual C++", "2013", "x64");
            if (id == "vcredist_2013_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "2013", "x86");
            if (id == "vcredist_2012_x64") return AnyNameMatchesAll(installedNames, "Visual C++", "2012", "x64");
            if (id == "vcredist_2012_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "2012", "x86");
            if (id == "vcredist_2010_x64") return AnyNameMatchesAll(installedNames, "Visual C++", "2010", "x64");
            if (id == "vcredist_2010_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "2010", "x86");
            if (id == "vcredist_2008_x64") return AnyNameMatchesAll(installedNames, "Visual C++", "2008", "x64");
            if (id == "vcredist_2008_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "2008", "x86");
            if (id == "vcredist_2005_x64") return AnyNameMatchesAll(installedNames, "Visual C++", "2005", "x64");
            if (id == "vcredist_2005_x86") return AnyNameMatchesAll(installedNames, "Visual C++", "2005", "x86");
            if (id == "xna4") return AnyNameStartsWith(installedNames, "Microsoft XNA Framework");
            if (id == "openal") return AnyNameEqualsOrPrefix(installedNames, "OpenAL");
            if (id == "webview2") return AnyNameMatchesAll(installedNames, "WebView2", "Runtime", "Edge");
            if (id == "dotnet6") return AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime - 6.") || AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime 6.");
            if (id == "dotnet8") return AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime - 8.") || AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime 8.");
            if (id == "dotnet9") return AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime - 9.") || AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime 9.");
            if (id == "dotnet10") return AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime - 10.") || AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime 10.");
            if (id == "dotnet11") return AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime - 11.") || AnyNameStartsWith(installedNames, "Microsoft Windows Desktop Runtime 11.");
            if (id == "java8") return AnyNameStartsWith(installedNames, "Java 8");
            if (id == "java11_jre") return AnyNameMatchesAll(installedNames, "Temurin", "11", "JRE");
            if (id == "java11_jdk") return AnyNameMatchesAll(installedNames, "Temurin", "11", "JDK") || AnyNameStartsWith(installedNames, "Java(TM) SE Development Kit 11");
            if (id == "java17_jre") return AnyNameMatchesAll(installedNames, "Temurin", "17", "JRE");
            if (id == "java17") return AnyNameMatchesAll(installedNames, "Temurin", "17", "JDK") || AnyNameStartsWith(installedNames, "Java(TM) SE Development Kit 17");
            if (id == "java21_jre") return AnyNameMatchesAll(installedNames, "Temurin", "21", "JRE");
            if (id == "java21") return AnyNameMatchesAll(installedNames, "Temurin", "21", "JDK") || AnyNameStartsWith(installedNames, "Java(TM) SE Development Kit 21");
            if (id == "java25") return AnyNameStartsWith(installedNames, "Java(TM) SE Development Kit 25") || AnyNameMatchesAll(installedNames, "Temurin", "25", "JDK");

            // Para apps personalizadas agregadas por el usuario
            if (!string.IsNullOrEmpty(name))
            {
                return AnyNameEqualsOrPrefix(installedNames, name);
            }

            return false;
        }

        private static void CollectRegistryDisplayNames(HashSet<string> targetSet)
        {
            RegistryKey[] roots = new[] { Registry.LocalMachine, Registry.CurrentUser };
            string[] subPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            for (int r = 0; r < roots.Length; r++)
            {
                for (int p = 0; p < subPaths.Length; p++)
                {
                    try
                    {
                        using (RegistryKey parent = roots[r].OpenSubKey(subPaths[p]))
                        {
                            if (parent == null) continue;
                            string[] subKeys = parent.GetSubKeyNames();
                            for (int i = 0; i < subKeys.Length; i++)
                            {
                                try
                                {
                                    using (RegistryKey sk = parent.OpenSubKey(subKeys[i]))
                                    {
                                        if (sk == null) continue;
                                        object dn = sk.GetValue("DisplayName");
                                        if (dn != null)
                                        {
                                            string s = dn.ToString().Trim();
                                            if (!string.IsNullOrEmpty(s)) targetSet.Add(s);
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool AnyNameStartsWith(HashSet<string> names, string prefix)
        {
            foreach (string n in names)
            {
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool AnyNameEqualsOrPrefix(HashSet<string> names, string exactOrPrefix)
        {
            string withSpace = exactOrPrefix + " ";
            string withDash = exactOrPrefix + "-";
            foreach (string n in names)
            {
                if (n.Equals(exactOrPrefix, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(withSpace, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(withDash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyExactPrefixOnly(HashSet<string> names, string exclude1, string exclude2)
        {
            // Devuelve true solo si TODOS los nombres que empiezan por el prefijo pertenecen a los excluidos
            bool anyMatch = false;
            foreach (string n in names)
            {
                if (n.StartsWith(exclude1, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(exclude2, StringComparison.OrdinalIgnoreCase))
                {
                    anyMatch = true;
                }
            }
            return anyMatch && !names.Contains("Antigravity") && !names.Contains("OpenCode") && !names.Contains("GitHub Copilot");
        }

        private static bool AnyNameMatchesAll(HashSet<string> names, string part1, string part2, string part3)
        {
            foreach (string n in names)
            {
                if (n.IndexOf(part1, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    n.IndexOf(part2, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    n.IndexOf(part3, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyIdContains(HashSet<string> ids, string fragment)
        {
            foreach (string id in ids)
            {
                if (id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string ReadOfficeReleaseIds()
        {
            string[] paths = new[]
            {
                @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
                @"SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(paths[i]))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("ProductReleaseIds");
                            if (val != null)
                            {
                                string s = val.ToString();
                                if (!string.IsNullOrWhiteSpace(s)) return s;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return string.Empty;
        }

        public async Task<List<WingetSearchEntry>> SearchWingetAsync(string query, int maxResults = 15)
        {
            List<WingetSearchEntry> results = new List<WingetSearchEntry>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            string safeQuery = query.Replace("\"", string.Empty).Trim();
            string args = string.Format(
                "search \"{0}\" --source winget -n {1} --accept-source-agreements --disable-interactivity",
                safeQuery,
                maxResults);

            string output = await RunQuickQueryAsync("winget", args, 8000);
            if (string.IsNullOrEmpty(output)) return results;

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;
            int idCol = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (line.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Versi", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    headerIdx = i;
                    idCol = FindColumnStart(line, "Id");
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
                if (line.StartsWith("---") || line.StartsWith("<")) continue;

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

                if (results.Count >= maxResults) break;
            }

            return results;
        }

        public async Task<string> GetWingetDescriptionAsync(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return string.Empty;
            string safeId = packageId.Replace("\"", string.Empty).Trim();
            string args = string.Format(
                "show --id \"{0}\" --exact --source winget --accept-source-agreements --disable-interactivity",
                safeId);

            string output = await RunQuickQueryAsync("winget", args, 6000);
            if (string.IsNullOrEmpty(output)) return string.Empty;

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Descripci", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = line.IndexOf(':');
                    string sameLine = colon >= 0 ? line.Substring(colon + 1).Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(sameLine))
                    {
                        return TruncateText(sameLine, 120);
                    }
                    if (i + 1 < lines.Length)
                    {
                        string nextLine = lines[i + 1].Trim();
                        if (!string.IsNullOrEmpty(nextLine) && !nextLine.EndsWith(":"))
                        {
                            return TruncateText(nextLine, 120);
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static string TruncateText(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
            return text.Substring(0, maxLen - 3).TrimEnd() + "...";
        }

        private static Task<string> RunQuickQueryAsync(string fileName, string arguments, int timeoutMs)
        {
            return Task.Run(delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = fileName;
                    psi.Arguments = arguments;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;

                    using (Process p = Process.Start(psi))
                    {
                        if (p == null) return string.Empty;
                        Task<string> readTask = p.StandardOutput.ReadToEndAsync();
                        if (!p.WaitForExit(timeoutMs))
                        {
                            try { p.Kill(); } catch { }
                            return string.Empty;
                        }
                        return readTask.Wait(1000) ? readTask.Result : string.Empty;
                    }
                }
                catch
                {
                    return string.Empty;
                }
            });
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
                // Si ya existe un instalador descargado en local\cache\, instalar directo desde ahi (rapido y 100% offline)
                if (source == "winget" || source == "url")
                {
                    string cachedFile = FindCachedInstallerPath(app);
                    if (!string.IsNullOrEmpty(cachedFile))
                    {
                        EmitLog("[*] Instalador offline encontrado en cache local: " + cachedFile);
                        AppItem cachedApp = new AppItem();
                        cachedApp.Id = app.Id;
                        cachedApp.Name = app.Name;
                        cachedApp.Source = "local";
                        cachedApp.Target = cachedFile;
                        string yamlArgs = ExtractSilentArgsFromCachedYaml(cachedFile);
                        cachedApp.SilentArgs = !string.IsNullOrWhiteSpace(yamlArgs) ? yamlArgs : app.SilentArgs;
                        return await ExecuteInstallerFileAsync(cachedApp, cachedFile, silent);
                    }
                }

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
                if (source == "powershell")
                {
                    return await RunPowerShellToolAsync(app);
                }

                return MakeResult(app, InstallStatus.Failed, "Origen desconocido: " + source);
            }
            catch (Exception ex)
            {
                EmitLog("[X] Excepcion en " + app.Name + ": " + ex.Message);
                return MakeResult(app, InstallStatus.Failed, ex.Message);
            }
        }

        private static string GetWingetSourceName(string targetId)
        {
            string t = (targetId ?? string.Empty).Trim();
            // Los paquetes de Microsoft Store (ej. 9NKSQGP7F2NH) no contienen punto
            return (t.Length > 0 && t.IndexOf('.') < 0) ? "msstore" : "winget";
        }

        private async Task<InstallResult> InstallViaWingetAsync(AppItem app, bool silent, bool upgradeIfExists)
        {
            if (string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "ID de Winget vacio");
            }

            string pkgSource = GetWingetSourceName(app.Target);
            string silentFlag = silent ? " --silent" : string.Empty;
            string args = string.Format(
                "install --id \"{0}\" --exact --source {1}{2} --accept-package-agreements --accept-source-agreements --disable-interactivity",
                app.Target.Trim(),
                pkgSource,
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
                        "upgrade --id \"{0}\" --exact --source {1}{2} --accept-package-agreements --accept-source-agreements --disable-interactivity",
                        app.Target.Trim(),
                        pkgSource,
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

        private async Task<InstallResult> RunPowerShellToolAsync(AppItem app)
        {
            if (string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "Comando PowerShell vacio");
            }

            EmitLog("[*] Lanzando herramienta interactiva en PowerShell (Administrador): " + app.Name);
            string encodedCmd = Convert.ToBase64String(Encoding.Unicode.GetBytes(app.Target.Trim()));
            string psArgs = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCmd;

            int exitCode = await Task.Run(delegate
            {
                try
                {
                    return RunElevatedShell("powershell.exe", psArgs);
                }
                catch (Exception ex)
                {
                    EmitLog("[X] No se pudo iniciar PowerShell elevado: " + ex.Message);
                    return -1;
                }
            });

            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Cancelado por el usuario");
            }

            if (exitCode == 0)
            {
                EmitLog("[OK] Herramienta finalizada: " + app.Name);
                return MakeResult(app, InstallStatus.Success, "Ejecutada correctamente");
            }

            return MakeResult(app, InstallStatus.Failed, "Finalizo con codigo: " + exitCode);
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
