using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
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

    public class SelfUpdateInfo
    {
        public bool Available { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string TagName { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseUrl { get; set; }
        public string Notes { get; set; }
    }

    public class InstallerEngine
    {
        public const string CurrentAppVersion = "1.1.0";
        public const string GithubRepoSlug = "Jmfu07/qInstall";

        private static readonly object _wingetResolveLock = new object();
        private static string _resolvedWingetExe;
        private static string _resolvedWingetExtraPath;

        private readonly object _lock = new object();
        private readonly List<Process> _activeProcesses = new List<Process>();
        private readonly List<HttpWebRequest> _activeRequests = new List<HttpWebRequest>();
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

        public static void InvalidateWingetCache()
        {
            lock (_wingetResolveLock)
            {
                _resolvedWingetExe = null;
                _resolvedWingetExtraPath = null;
            }
        }

        private static string BuildWindowsAppsRuntimeExtraPath(string wingetExePath)
        {
            List<string> dirs = new List<string>();
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    string userWinApps = Path.Combine(localAppData, "Microsoft", "WindowsApps");
                    if (Directory.Exists(userWinApps)) dirs.Add(userWinApps);
                }

                if (!string.IsNullOrEmpty(wingetExePath) && Path.IsPathRooted(wingetExePath))
                {
                    string exeDir = Path.GetDirectoryName(wingetExePath);
                    if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                    {
                        dirs.Add(exeDir);
                    }
                }

                string pf = Environment.GetEnvironmentVariable("ProgramFiles");
                if (string.IsNullOrEmpty(pf)) pf = @"C:\Program Files";
                string sysWinApps = Path.Combine(pf, "WindowsApps");
                if (Directory.Exists(sysWinApps))
                {
                    string[] patterns = new[]
                    {
                        "Microsoft.VCLibs.140.00.UWPDesktop_*_x64__*",
                        "Microsoft.VCLibs.140.00_*_x64__*",
                        "Microsoft.WindowsAppRuntime.*_x64__*",
                        "Microsoft.UI.Xaml.2.8_*_x64__*"
                    };
                    for (int p = 0; p < patterns.Length; p++)
                    {
                        try
                        {
                            string[] matches = Directory.GetDirectories(sysWinApps, patterns[p]);
                            Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
                            for (int m = matches.Length - 1; m >= 0; m--)
                            {
                                dirs.Add(matches[m]);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return dirs.Count > 0 ? string.Join(";", dirs.ToArray()) : string.Empty;
        }

        private static void ConfigureWingetProcessStartInfo(ProcessStartInfo psi)
        {
            if (psi == null) return;
            string fn = (psi.FileName ?? string.Empty).Trim();
            if (!string.Equals(fn, "winget", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fn, "winget.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string resolvedExe = null;
            string extraPath = null;
            lock (_wingetResolveLock)
            {
                resolvedExe = _resolvedWingetExe;
                extraPath = _resolvedWingetExtraPath;
            }

            if (!string.IsNullOrEmpty(resolvedExe))
            {
                psi.FileName = resolvedExe;
            }
            if (!string.IsNullOrEmpty(extraPath))
            {
                try
                {
                    string curPath = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    psi.EnvironmentVariables["PATH"] = extraPath + ";" + curPath;
                }
                catch { }
            }
        }

        private static string ProbeWingetCandidate(string candidateExe, string extraPath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(candidateExe, "--version");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                if (!string.IsNullOrEmpty(extraPath))
                {
                    try
                    {
                        string curPath = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                        psi.EnvironmentVariables["PATH"] = extraPath + ";" + curPath;
                    }
                    catch { }
                }

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
                        string ver = output.Trim();
                        if (ver.IndexOf('\n') >= 0) ver = ver.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        return ver;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static string GetWingetVersion()
        {
            string cachedExe = null;
            string cachedExtra = null;
            lock (_wingetResolveLock)
            {
                cachedExe = _resolvedWingetExe;
                cachedExtra = _resolvedWingetExtraPath;
            }
            if (!string.IsNullOrEmpty(cachedExe))
            {
                string quickVer = ProbeWingetCandidate(cachedExe, cachedExtra);
                if (!string.IsNullOrEmpty(quickVer)) return quickVer;
            }

            List<string> candidates = new List<string>();
            candidates.Add("winget");

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    string aliasPath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
                    candidates.Add(aliasPath);
                }
            }
            catch { }

            try
            {
                string pf = Environment.GetEnvironmentVariable("ProgramFiles");
                if (string.IsNullOrEmpty(pf)) pf = @"C:\Program Files";
                string sysWinApps = Path.Combine(pf, "WindowsApps");
                if (Directory.Exists(sysWinApps))
                {
                    string[] dirs = Directory.GetDirectories(sysWinApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe");
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    for (int i = dirs.Length - 1; i >= 0; i--)
                    {
                        string wg = Path.Combine(dirs[i], "winget.exe");
                        if (File.Exists(wg)) candidates.Add(wg);
                        string legacyCli = Path.Combine(dirs[i], "AppInstallerCLI.exe");
                        if (File.Exists(legacyCli)) candidates.Add(legacyCli);
                    }
                }
            }
            catch { }

            try
            {
                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive");
                if (string.IsNullOrEmpty(sysDrive)) sysDrive = "C:";
                string usersRoot = Path.Combine(sysDrive + "\\", "Users");
                if (Directory.Exists(usersRoot))
                {
                    string[] userDirs = Directory.GetDirectories(usersRoot);
                    for (int i = 0; i < userDirs.Length; i++)
                    {
                        string uAlias = Path.Combine(userDirs[i], "AppData", "Local", "Microsoft", "WindowsApps", "winget.exe");
                        if (File.Exists(uAlias)) candidates.Add(uAlias);
                    }
                }
            }
            catch { }

            HashSet<string> tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                string cand = candidates[i];
                if (string.IsNullOrEmpty(cand) || !tried.Add(cand)) continue;
                string extra = BuildWindowsAppsRuntimeExtraPath(cand);
                string ver = ProbeWingetCandidate(cand, extra);
                if (!string.IsNullOrEmpty(ver))
                {
                    lock (_wingetResolveLock)
                    {
                        _resolvedWingetExe = cand;
                        _resolvedWingetExtraPath = extra;
                    }
                    return ver;
                }
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

                string[] hosts = new[] { "1.1.1.1", "8.8.8.8", "www.msftconnecttest.com" };
                int[] ports = new[] { 443, 53, 80 };

                for (int i = 0; i < hosts.Length; i++)
                {
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(hosts[i], ports[i], null, null);
                            try
                            {
                                if (ar.AsyncWaitHandle.WaitOne(1200, false) && client.Connected)
                                {
                                    client.EndConnect(ar);
                                    return true;
                                }
                            }
                            finally
                            {
                                try { ar.AsyncWaitHandle.Close(); } catch { }
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

        public static string GetActiveNetworkSummary()
        {
            bool hasWifi = false;
            bool hasEthernet = false;
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    NetworkInterface nic = nics[i];
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string desc = (nic.Description ?? string.Empty).ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vpn") || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("virtualbox")) continue;

                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        hasWifi = true;
                    }
                    else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                             nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
                    {
                        hasEthernet = true;
                    }
                }
            }
            catch { }

            if (hasWifi)
            {
                string ssid = ReadActiveWifiSsid();
                if (!string.IsNullOrEmpty(ssid)) return "Wi-Fi: " + ssid;
                return hasEthernet ? "Ethernet + Wi-Fi" : "Wi-Fi";
            }
            if (hasEthernet) return "Ethernet";
            return string.Empty;
        }

        private static string ReadActiveWifiSsid()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "wlan show interfaces");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return string.Empty;
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(1500))
                    {
                        try { p.Kill(); } catch { }
                        return string.Empty;
                    }
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string trimmed = lines[i].Trim();
                        if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                            !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                        {
                            int colon = trimmed.IndexOf(':');
                            if (colon >= 0)
                            {
                                string val = trimmed.Substring(colon + 1).Trim();
                                if (!string.IsNullOrEmpty(val)) return val;
                            }
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
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

        public const long MinPortableDriveBytes = 2L * 1024L * 1024L * 1024L;
        public const long RecommendedPortableDriveBytes = 4L * 1024L * 1024L * 1024L;
        public const long MinFreeSpaceForOfflineDownloadBytes = 512L * 1024L * 1024L;

        public static string GetOfflineBlockReason(AppItem app)
        {
            if (app == null) return string.Empty;
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            string id = (app.Id ?? string.Empty).Trim().ToLowerInvariant();

            if (src == "local")
            {
                string args = app.SilentArgs ?? string.Empty;
                if (args.IndexOf("/configure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Solo instalación online: Office offline pesa ~3-4 GB (excede espacio portable de pendrive)";
                }
                return string.Empty;
            }

            if (src == "winget" && GetWingetSourceName(app.Target) == "msstore")
            {
                return "Solo instalación online: Microsoft Store bloquea descargas offline sin cuenta empresarial Entra ID (instálalo online con 'Instalar en esta PC')";
            }

            if (id == "vs2022_community")
                return "Solo instalación online: Visual Studio requiere varios GB de componentes";
            if (id == "android_studio")
                return "Solo instalación online: supera 1.2 GB con SDKs (no apto para cache local en pendrive)";
            if (id == "docker_desktop")
                return "Solo instalación online: instalador pesado (> 700 MB) restringido en pendrive";
            if (id == "comfyui" || id == "lmstudio")
                return "Solo instalación online: suite de IA pesada (> 1 GB) restringida en pendrive";
            if (id == "adobe_cc")
                return "Solo instalación online: Adobe Creative Cloud descarga sus suites desde la nube";
            if (id == "intellij" || id == "intellij_community" ||
                id == "pycharm" || id == "pycharm_community" ||
                id == "webstorm" || id == "clion" || id == "rider" ||
                id == "phpstorm" || id == "datagrip" || id == "goland" || id == "rustrover")
                return "Solo instalación online: IDE pesado (~1 GB); lleva JetBrains Toolbox en el pendrive";

            return string.Empty;
        }

        public static bool CanDownloadOffline(AppItem app)
        {
            if (app == null) return false;
            if (!string.IsNullOrEmpty(GetOfflineBlockReason(app))) return false;
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "winget" || src == "url") return true;
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
                    string fallbackPkg = null;
                    string fallbackZip = null;
                    for (int f = 0; f < files.Length; f++)
                    {
                        string ext = Path.GetExtension(files[f]).ToLowerInvariant();
                        bool isSupported = ext == ".msi" || ext == ".exe" ||
                                           ext == ".msix" || ext == ".msixbundle" ||
                                           ext == ".appx" || ext == ".appxbundle" ||
                                           ext == ".zip";
                        if (isSupported)
                        {
                            FileInfo fi = new FileInfo(files[f]);
                            if (fi.Length > 0)
                            {
                                if (ext == ".msi") return files[f];
                                if (ext == ".exe") fallbackExe = files[f];
                                else if (ext == ".zip") fallbackZip = files[f];
                                else fallbackPkg = files[f];
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(fallbackExe)) return fallbackExe;
                    if (!string.IsNullOrEmpty(fallbackPkg)) return fallbackPkg;
                    if (!string.IsNullOrEmpty(fallbackZip)) return fallbackZip;
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
                    // Priorizar qinstall_meta.yaml si existe, luego cualquier manifiesto .yaml de winget download
                    string metaYaml = Path.Combine(dir, "qinstall_meta.yaml");
                    List<string> yamlFiles = new List<string>();
                    if (File.Exists(metaYaml)) yamlFiles.Add(metaYaml);
                    string[] otherYamls = Directory.GetFiles(dir, "*.yaml");
                    for (int y = 0; y < otherYamls.Length; y++)
                    {
                        if (!string.Equals(otherYamls[y], metaYaml, StringComparison.OrdinalIgnoreCase))
                        {
                            yamlFiles.Add(otherYamls[y]);
                        }
                    }

                    for (int i = 0; i < yamlFiles.Count; i++)
                    {
                        string[] lines = File.ReadAllLines(yamlFiles[i], Encoding.UTF8);
                        for (int l = 0; l < lines.Length; l++)
                        {
                            string trimmed = lines[l].Trim();
                            if (trimmed.StartsWith("PackageVersion:", StringComparison.OrdinalIgnoreCase))
                            {
                                string val = trimmed.Substring(15).Trim().Trim('\'', '"');
                                if (!string.IsNullOrEmpty(val)) return NormalizeVersionDisplay(val);
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
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion)) return NormalizeVersionDisplay(fvi.ProductVersion);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion)) return NormalizeVersionDisplay(fvi.FileVersion);
            }
            catch
            {
            }

            return string.Empty;
        }

        public static string NormalizeVersionDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();
            if (s.StartsWith("<")) s = s.Substring(1).Trim();
            if ((s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) && s.Length > 1 && char.IsDigit(s[1]))
            {
                s = s.Substring(1).Trim();
            }
            int plusIdx = s.IndexOf('+');
            if (plusIdx > 0) s = s.Substring(0, plusIdx).Trim();
            // Convertir formato Win32 "1, 2, 3, 0" -> "1.2.3.0"
            if (s.IndexOf(',') >= 0)
            {
                s = s.Replace(", ", ".").Replace(",", ".");
            }
            int spIdx = s.IndexOf(' ');
            if (spIdx > 0)
            {
                string firstTok = s.Substring(0, spIdx).Trim();
                if (firstTok.Length > 0 && char.IsDigit(firstTok[0])) s = firstTok;
            }
            return s;
        }

        public static bool IsUnknownVersionToken(string ver)
        {
            if (string.IsNullOrWhiteSpace(ver)) return true;
            string t = ver.Trim();
            return t.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("Desconocid", StringComparison.OrdinalIgnoreCase) ||
                   t.Equals("?", StringComparison.Ordinal) ||
                   t.Equals("-", StringComparison.Ordinal);
        }

        public async Task<string> GetWingetLatestVersionAsync(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return string.Empty;
            string safeId = packageId.Replace("\"", string.Empty).Trim();
            string pkgSource = GetWingetSourceName(safeId);
            string args = string.Format(
                "show --id \"{0}\" --exact --source {1} --accept-source-agreements --disable-interactivity",
                safeId,
                pkgSource);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0) await Task.Delay(600);
                string output = await RunQuickQueryAsync("winget", args, 20000);
                if (string.IsNullOrEmpty(output)) continue;

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
                            if (!string.IsNullOrEmpty(v) && !IsUnknownVersionToken(v)) return v;
                        }
                    }
                }
            }
            return string.Empty;
        }

        public static bool IsVersionOlder(string localVersion, string latestVersion)
        {
            if (IsUnknownVersionToken(localVersion) || IsUnknownVersionToken(latestVersion))
            {
                return false;
            }

            string a = NormalizeVersionDisplay(localVersion);
            string b = NormalizeVersionDisplay(latestVersion);
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
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
                    // Si un segmento es "0" implicito y el otro es pre-release (ej. "beta", "rc1"),
                    // la version base sin sufijo NO es mas antigua que su pre-release (1.0.0 vs 1.0.0-beta)
                    if (i >= partsA.Length && !long.TryParse(pb, out nb)) return false;
                    if (i >= partsB.Length && !long.TryParse(pa, out na)) return true;

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

            List<AppItem> candidates = new List<AppItem>();
            List<string> localVersions = new List<string>();
            for (int i = 0; i < apps.Count; i++)
            {
                AppItem app = apps[i];
                string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
                if (src != "winget" || string.IsNullOrWhiteSpace(app.Target)) continue;
                if (!HasOfflineCache(app)) continue;

                string localVer = GetCachedOfflineVersion(app);
                if (string.IsNullOrEmpty(localVer)) continue;
                candidates.Add(app);
                localVersions.Add(localVer);
            }

            if (candidates.Count == 0) return outdated;

            // Consultar hasta 3 paquetes Winget en paralelo evitando contencion excesiva de la base SQLite de Winget
            object dictLock = new object();
            using (SemaphoreSlim sem = new SemaphoreSlim(3, 3))
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    AppItem app = candidates[i];
                    string localVer = localVersions[i];
                    tasks.Add(Task.Run(async delegate
                    {
                        await sem.WaitAsync();
                        try
                        {
                            string latestVer = await GetWingetLatestVersionAsync(app.Target);
                            if (IsVersionOlder(localVer, latestVer))
                            {
                                string key = (app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty);
                                OfflineCacheUpdateInfo info = new OfflineCacheUpdateInfo();
                                info.App = app;
                                info.LocalVersion = localVer;
                                info.LatestVersion = latestVer;
                                lock (dictLock)
                                {
                                    outdated[key] = info;
                                }
                            }
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }));
                }
                await Task.WhenAll(tasks.ToArray());
            }

            return outdated;
        }

        public static long GetDirectorySizeBytes(string dir)
        {
            long total = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { total += new FileInfo(files[i]).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public static long GetOfflineCacheSizeBytes(AppItem app)
        {
            string installerPath = FindCachedInstallerPath(app);
            if (string.IsNullOrEmpty(installerPath)) return 0;
            string dir = Path.GetDirectoryName(installerPath);
            return GetDirectorySizeBytes(dir);
        }

        public static long DeleteSingleOfflineCache(AppItem app)
        {
            long freed = 0;
            string installerPath = FindCachedInstallerPath(app);
            string cacheDir = !string.IsNullOrEmpty(installerPath)
                ? Path.GetDirectoryName(installerPath)
                : GetOfflineCacheDirForApp(app);
            if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir)) return 0;
            freed = GetDirectorySizeBytes(cacheDir);
            try { Directory.Delete(cacheDir, true); } catch { }
            return freed;
        }

        public async Task<InstallResult> UninstallAppAsync(AppItem app, bool silent)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Name))
            {
                return MakeResult(app, InstallStatus.Failed, "Aplicacion invalida para desinstalar");
            }
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "powershell")
            {
                return MakeResult(app, InstallStatus.Failed, "Las herramientas/enlaces PowerShell no se desinstalan");
            }

            EmitLog("--------------------------------------------------");
            EmitLog("[*] Desinstalando: " + app.Name + " (" + (app.Target ?? string.Empty) + ")");

            // 1. Limpiar herramienta portable .zip instalada en %LOCALAPPDATA%\Qinstall\Tools\<id>
            bool removedPortable = TryRemovePortableZipInstall(app);
            if (removedPortable)
            {
                EmitLog("[OK] " + app.Name + " (herramienta portable) eliminada correctamente.");
                return MakeResult(app, InstallStatus.Success, "Desinstalada correctamente (herramienta portable)");
            }

            // 2. Intentar desinstalacion directa por ID de Winget SIN forzar --source (funciona con winget, msstore y paquetes locales)
            int lastCode = -1;
            if (src == "winget" && !string.IsNullOrWhiteSpace(app.Target))
            {
                int wgCode = await TryWingetUninstallByIdAsync(app.Target.Trim(), silent, true);
                if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Desinstalacion cancelada");
                if (wgCode == 0)
                {
                    EmitLog("[OK] " + app.Name + " desinstalada correctamente.");
                    return MakeResult(app, InstallStatus.Success, "Desinstalada correctamente");
                }
                lastCode = wgCode;
            }

            // 3. Buscar ID REAL instalado en `winget list` (cubre ARP\..., MSIX\..., Store IDs como Discord XPDC2RH70K22MN, Chrome ARP, Spotify MSIX, Dev-C++ URL, etc.)
            string realInstalledId = await FindInstalledWingetIdAsync(app);
            if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Desinstalacion cancelada");

            if (!string.IsNullOrEmpty(realInstalledId))
            {
                if (!string.Equals(realInstalledId, (app.Target ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    EmitLog("[*] Paquete instalado real detectado: " + realInstalledId);
                    int realCode = await TryWingetUninstallByIdAsync(realInstalledId, silent, false);
                    if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Desinstalacion cancelada");
                    if (realCode == 0)
                    {
                        EmitLog("[OK] " + app.Name + " desinstalada correctamente.");
                        return MakeResult(app, InstallStatus.Success, "Desinstalada correctamente");
                    }
                    lastCode = realCode;
                }

                // 4. Respaldo MSIX / Microsoft Store via Remove-AppxPackage
                if (realInstalledId.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
                {
                    string pkgFullName = realInstalledId.Substring(5).Trim();
                    if (!string.IsNullOrEmpty(pkgFullName))
                    {
                        EmitLog("[*] Desinstalando paquete MSIX/Store con Remove-AppxPackage: " + pkgFullName);
                        string ps = string.Format("Remove-AppxPackage -Package '{0}' -ErrorAction Stop", pkgFullName.Replace("'", "''"));
                        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                        StringBuilder appxOut = new StringBuilder();
                        int appxCode = await RunProcessAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, appxOut);
                        if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Desinstalacion cancelada");
                        if (appxCode == 0)
                        {
                            EmitLog("[OK] " + app.Name + " (MSIX/Store) desinstalada correctamente.");
                            return MakeResult(app, InstallStatus.Success, "Desinstalada correctamente");
                        }
                        lastCode = appxCode;
                    }
                }
            }

            // 5. Respaldo nativo del Registro de Windows (HKLM 64/32 y HKCU UninstallString / QuietUninstallString) — funciona 100% offline
            int regCode = await TryRegistryUninstallAsync(app, silent);
            if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Desinstalacion cancelada");
            if (regCode == 0 || regCode == 3010)
            {
                EmitLog("[OK] " + app.Name + " desinstalada correctamente (desinstalador nativo de Windows).");
                return MakeResult(app, InstallStatus.Success, regCode == 3010 ? "Desinstalada (puede requerir reinicio)" : "Desinstalada correctamente");
            }
            if (regCode != -999) lastCode = regCode;

            // 6. Verificacion final: algunos desinstaladores NSIS/Inno se autoejecutan en un proceso hijo y salen con codigo distinto de 0
            await Task.Delay(1200);
            HashSet<string> stillInstalled = await DetectInstalledAppKeysAsync(new List<AppItem> { app });
            string appKey = (app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty);
            if (!stillInstalled.Contains(appKey))
            {
                EmitLog("[OK] " + app.Name + " verificada como desinstalada.");
                return MakeResult(app, InstallStatus.Success, "Desinstalada correctamente");
            }

            EmitLog(string.Format("[X] No se pudo desinstalar {0} (codigo {1}).", app.Name, lastCode));
            return MakeResult(app, InstallStatus.Failed, "No se pudo desinstalar automaticamente (codigo " + lastCode + ")");
        }

        private async Task<int> TryWingetUninstallByIdAsync(string packageId, bool silent, bool allowAllVersions)
        {
            string silentFlag = silent ? " --silent" : string.Empty;
            string allVerFlag = allowAllVersions ? " --all-versions" : string.Empty;
            string args = string.Format(
                "uninstall --id \"{0}\" --exact{1}{2} --accept-source-agreements --disable-interactivity",
                packageId.Replace("\"", string.Empty),
                allVerFlag,
                silentFlag);

            StringBuilder output = new StringBuilder();
            int code = await RunProcessAsync("winget", args, output);
            if (_cancelRequested || code == 0) return code;

            string text = output.ToString();
            bool notFound =
                code == -1978335212 ||
                text.IndexOf("No installed package found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("No se encontr", StringComparison.OrdinalIgnoreCase) >= 0;

            // Si fallo en modo silencioso por culpa del instalador (NO por "no encontrado"), reintentar en modo interactivo
            if (silent && !notFound)
            {
                EmitLog("[!] El desinstalador silencioso fallo; reintentando en modo interactivo...");
                string interactiveArgs = string.Format(
                    "uninstall --id \"{0}\" --exact{1} --accept-source-agreements",
                    packageId.Replace("\"", string.Empty),
                    allVerFlag);
                StringBuilder intOut = new StringBuilder();
                int intCode = await RunProcessAsync("winget", interactiveArgs, intOut);
                return intCode;
            }

            return code;
        }

        private async Task<string> FindInstalledWingetIdAsync(AppItem app)
        {
            try
            {
                StringBuilder output = new StringBuilder();
                int code = await RunProcessAsync("winget", "list --accept-source-agreements --disable-interactivity", output, false);
                if (code != 0 && output.Length == 0) return string.Empty;

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
                if (headerIdx < 0 || idCol <= 0) return string.Empty;

                string bestId = string.Empty;
                int bestScore = 0;
                for (int i = headerIdx + 2; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length <= idCol || line.StartsWith("---")) continue;
                    string namePart = line.Substring(0, Math.Min(idCol, line.Length)).Trim();
                    string rest = line.Substring(idCol).Trim();
                    string[] tokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) continue;

                    // Para IDs con espacios (ej. "ARP\Machine\X86\Google Chrome"), reconstruir hasta el token de version
                    string entryId = tokens[0].Trim();
                    if (entryId.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
                    {
                        StringBuilder arpId = new StringBuilder(entryId);
                        for (int t = 1; t < tokens.Length; t++)
                        {
                            string tk = tokens[t];
                            if (char.IsDigit(tk[0]) || tk.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || tk.StartsWith("<")) break;
                            arpId.Append(' ').Append(tk);
                        }
                        entryId = arpId.ToString().Trim();
                    }

                    int score = ScoreUninstallMatch(app, namePart, entryId);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = entryId;
                    }
                }

                return bestScore > 0 ? bestId : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ScoreUninstallMatch(AppItem app, string installedName, string installedId)
        {
            string appId = (app.Id ?? string.Empty).Trim().ToLowerInvariant();
            string appTarget = (app.Target ?? string.Empty).Trim();
            string appName = (app.Name ?? string.Empty).Trim();
            string normName = (installedName ?? string.Empty).Trim();
            string normId = (installedId ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(appTarget) && !string.IsNullOrEmpty(normId) &&
                string.Equals(normId, appTarget, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            // Casos especiales conocidos (Store / MSIX / nombres distintos al catalogo)
            if (appId == "discord" && (normId.IndexOf("XPDC2RH70K22MN", StringComparison.OrdinalIgnoreCase) >= 0 || NameEqualsOrPrefix(normName, "Discord"))) return 95;
            if (appId == "spotify" && (normId.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0 || NameEqualsOrPrefix(normName, "Spotify"))) return 95;
            if (appId == "whatsapp" && (normId.IndexOf("WhatsApp", StringComparison.OrdinalIgnoreCase) >= 0 || normId.IndexOf("9NKSQGP7F2NH", StringComparison.OrdinalIgnoreCase) >= 0 || NameEqualsOrPrefix(normName, "WhatsApp"))) return 95;
            if (appId == "chrome" && NameEqualsOrPrefix(normName, "Google Chrome")) return 95;
            if (appId == "operagx" && NameEqualsOrPrefix(normName, "Opera GX")) return 95;
            if (appId == "vscode" && (NameEqualsOrPrefix(normName, "Microsoft Visual Studio Code") || NameEqualsOrPrefix(normName, "Visual Studio Code"))) return 95;
            if (appId == "devcpp_592" && normName.IndexOf("Dev-C++", StringComparison.OrdinalIgnoreCase) >= 0 && normName.IndexOf("Embarcadero", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "devcpp" && normName.IndexOf("Embarcadero Dev-C++", StringComparison.OrdinalIgnoreCase) >= 0) return 95;
            if (appId == "windsurf" && (NameEqualsOrPrefix(normName, "Windsurf") || NameEqualsOrPrefix(normName, "Devin"))) return 92;
            if (appId == "copilot_app" && NameEqualsOrPrefix(normName, "GitHub Copilot") && normName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "copilot_cli" && NameEqualsOrPrefix(normName, "GitHub Copilot CLI")) return 95;
            if (appId == "claude" && NameEqualsOrPrefix(normName, "Claude") && normName.IndexOf("Code", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "claude_code" && NameEqualsOrPrefix(normName, "Claude Code")) return 95;
            if (appId == "antigravity" && (NameEqualsOrPrefix(normName, "Antigravity") || NameEqualsOrPrefix(normName, "Google Antigravity")) && normName.IndexOf("IDE", StringComparison.OrdinalIgnoreCase) < 0 && normName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "antigravity_ide" && NameEqualsOrPrefix(normName, "Antigravity IDE")) return 95;
            if (appId == "antigravity_cli" && NameEqualsOrPrefix(normName, "Antigravity CLI")) return 95;
            if (appId == "opencode" && NameEqualsOrPrefix(normName, "OpenCode") && normName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "opencode_cli" && NameEqualsOrPrefix(normName, "OpenCode CLI")) return 95;
            if (appId == "gemini" && (NameEqualsOrPrefix(normName, "Google Gemini") || NameEqualsOrPrefix(normName, "Gemini")) && normName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) < 0) return 95;
            if (appId == "gemini_cli" && (NameEqualsOrPrefix(normName, "Gemini API CLI") || NameEqualsOrPrefix(normName, "Gemini CLI"))) return 95;

            // Coincidencia segura por nombre base (sin colisionar Desktop vs CLI)
            string cleanAppName = appName;
            int parenIdx = cleanAppName.IndexOf('(');
            if (parenIdx > 2) cleanAppName = cleanAppName.Substring(0, parenIdx).Trim();

            if (string.Equals(normName, appName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normName, cleanAppName, StringComparison.OrdinalIgnoreCase))
            {
                return 90;
            }
            if (cleanAppName.Length >= 4 && NameEqualsOrPrefix(normName, cleanAppName))
            {
                bool appIsCli = appName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) >= 0;
                bool instIsCli = normName.IndexOf("CLI", StringComparison.OrdinalIgnoreCase) >= 0;
                if (appIsCli == instIsCli) return 80;
            }

            return 0;
        }

        private static bool NameEqualsOrPrefix(string candidate, string prefix)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(prefix)) return false;
            if (string.Equals(candidate, prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (candidate.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private bool TryRemovePortableZipInstall(AppItem app)
        {
            try
            {
                string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qinstall", "Tools", safeId);
                if (!Directory.Exists(installDir)) return false;

                Directory.Delete(installDir, true);
                try
                {
                    string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
                    string[] parts = userPath.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    List<string> kept = new List<string>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (!string.Equals(parts[i].Trim().TrimEnd('\\'), installDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        {
                            kept.Add(parts[i]);
                        }
                    }
                    Environment.SetEnvironmentVariable("Path", string.Join(";", kept.ToArray()), EnvironmentVariableTarget.User);
                }
                catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<int> TryRegistryUninstallAsync(AppItem app, bool silent)
        {
            string quietCmd = string.Empty;
            string normalCmd = string.Empty;
            string matchedDisplay = string.Empty;
            int bestScore = 0;

            RegistryKey[] roots = new[] { Registry.LocalMachine, Registry.CurrentUser };
            string[] subPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            for (int r = 0; r < roots.Length; r++)
            {
                for (int s = 0; s < subPaths.Length; s++)
                {
                    try
                    {
                        using (RegistryKey baseKey = roots[r].OpenSubKey(subPaths[s]))
                        {
                            if (baseKey == null) continue;
                            string[] names = baseKey.GetSubKeyNames();
                            for (int i = 0; i < names.Length; i++)
                            {
                                try
                                {
                                    using (RegistryKey sub = baseKey.OpenSubKey(names[i]))
                                    {
                                        if (sub == null) continue;
                                        string disp = sub.GetValue("DisplayName") as string;
                                        if (string.IsNullOrWhiteSpace(disp)) continue;
                                        int score = ScoreUninstallMatch(app, disp.Trim(), names[i]);
                                        if (score > bestScore)
                                        {
                                            string q = (sub.GetValue("QuietUninstallString") as string) ?? string.Empty;
                                            string u = (sub.GetValue("UninstallString") as string) ?? string.Empty;
                                            if (!string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(u))
                                            {
                                                bestScore = score;
                                                quietCmd = q.Trim();
                                                normalCmd = u.Trim();
                                                matchedDisplay = disp.Trim();
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (bestScore <= 0) return -999;

            EmitLog("[*] Desinstalador nativo encontrado en Registro para: " + matchedDisplay);
            string chosen = (silent && !string.IsNullOrWhiteSpace(quietCmd)) ? quietCmd : normalCmd;
            if (string.IsNullOrWhiteSpace(chosen)) chosen = quietCmd;

            string fileName;
            string args;
            ParseUninstallCommand(chosen, silent, out fileName, out args);
            if (string.IsNullOrWhiteSpace(fileName)) return -999;

            EmitLog(string.Format("[*] Ejecutando: {0} {1}", fileName, args));
            StringBuilder outBuf = new StringBuilder();
            int code = await RunProcessAsync(fileName, args, outBuf, true, true);
            if (_cancelRequested || code == 0 || code == 3010) return code;

            // Si fallo QuietUninstallString, reintentar con UninstallString interactivo
            if (silent && !string.IsNullOrWhiteSpace(quietCmd) && !string.IsNullOrWhiteSpace(normalCmd) &&
                !string.Equals(quietCmd, normalCmd, StringComparison.OrdinalIgnoreCase))
            {
                EmitLog("[!] El desinstalador silencioso del registro fallo; intentando modo interactivo...");
                ParseUninstallCommand(normalCmd, false, out fileName, out args);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    StringBuilder out2 = new StringBuilder();
                    code = await RunProcessAsync(fileName, args, out2, true, true);
                }
            }

            return code;
        }

        private static void ParseUninstallCommand(string rawCmd, bool silent, out string fileName, out string args)
        {
            fileName = string.Empty;
            args = string.Empty;
            if (string.IsNullOrWhiteSpace(rawCmd)) return;

            string cmd = Environment.ExpandEnvironmentVariables(rawCmd.Trim());

            // Normalizar MsiExec.exe /I{GUID} -> msiexec.exe /x {GUID} /qn /norestart
            if (cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int braceStart = cmd.IndexOf('{');
                int braceEnd = braceStart >= 0 ? cmd.IndexOf('}', braceStart) : -1;
                if (braceStart >= 0 && braceEnd > braceStart)
                {
                    string guid = cmd.Substring(braceStart, braceEnd - braceStart + 1);
                    fileName = "msiexec.exe";
                    args = "/x " + guid + (silent ? " /qn /norestart" : " /qb /norestart");
                    return;
                }
            }

            if (cmd.StartsWith("\""))
            {
                int close = cmd.IndexOf('\"', 1);
                if (close > 1)
                {
                    fileName = cmd.Substring(1, close - 1).Trim();
                    args = cmd.Substring(close + 1).Trim();
                    return;
                }
            }

            // Buscar .exe en la cadena (soporta rutas con espacios sin comillas como C:\Program Files\App\unins000.exe /S)
            int exeIdx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
            {
                fileName = cmd.Substring(0, exeIdx + 4).Trim();
                args = cmd.Substring(exeIdx + 4).Trim();
                return;
            }

            int sp = cmd.IndexOf(' ');
            if (sp > 0)
            {
                fileName = cmd.Substring(0, sp).Trim();
                args = cmd.Substring(sp + 1).Trim();
            }
            else
            {
                fileName = cmd;
                args = string.Empty;
            }
        }

        public async Task<InstallResult> UpgradeSingleAppAsync(AppItem app, bool silent)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Target))
            {
                return MakeResult(app, InstallStatus.Failed, "Aplicacion invalida para actualizar");
            }
            string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
            if (src == "url")
            {
                return await InstallViaUrlAsync(app, silent);
            }
            if (src != "winget")
            {
                return await InstallAppAsync(app, silent, true);
            }

            string targetId = app.Target.Trim();
            if (string.Equals(targetId, "Microsoft.EdgeWebView2Runtime", StringComparison.OrdinalIgnoreCase))
            {
                EmitLog("[!] Omitiendo actualizacion en caliente de WebView2 Runtime porque qInstall lo esta usando actualmente.");
                return MakeResult(app, InstallStatus.AlreadyInstalled, "En uso por qInstall (actualizalo desde Windows Update)");
            }

            EmitLog("--------------------------------------------------");
            EmitLog("[*] Actualizando en su sitio (conservando tus datos): " + app.Name + " (" + targetId + ")");
            string pkgSource = GetWingetSourceName(targetId);
            string silentFlag = silent ? " --silent" : string.Empty;
            string sourceFlag = pkgSource == "msstore" ? " --source msstore" : string.Empty;

            string args = string.Format(
                "upgrade --id \"{0}\" --exact{1}{2} --include-unknown --accept-package-agreements --accept-source-agreements --disable-interactivity",
                targetId,
                sourceFlag,
                silentFlag);

            StringBuilder output = new StringBuilder();
            int exitCode = await RunProcessAsync("winget", args, output);
            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Actualizacion cancelada");
            }

            string outText = output.ToString();
            // Si Winget reporta ambiguedad entre multiples origenes, reintentar especificando --source winget
            if (exitCode != 0 && exitCode != -1978335189 && string.IsNullOrEmpty(sourceFlag) &&
                (exitCode == -1978335211 ||
                 outText.IndexOf("Multiple installed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 outText.IndexOf("Varios paquetes", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string retrySourceArgs = string.Format(
                    "upgrade --id \"{0}\" --exact --source {1}{2} --include-unknown --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    targetId,
                    pkgSource,
                    silentFlag);
                output = new StringBuilder();
                exitCode = await RunProcessAsync("winget", retrySourceArgs, output);
                if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Actualizacion cancelada");
                outText = output.ToString();
            }

            if (exitCode == 0)
            {
                EmitLog("[OK] " + app.Name + " actualizada correctamente (datos y configuraciones conservados).");
                return MakeResult(app, InstallStatus.Success, "Actualizada a la ultima version");
            }
            if (exitCode == -1978335189 ||
                outText.IndexOf("No available upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                outText.IndexOf("No applicable update", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EmitLog("[=] " + app.Name + " ya esta en su ultima version.");
                return MakeResult(app, InstallStatus.AlreadyInstalled, "Ya esta al dia");
            }

            // Respaldo 1: si fue instalada fuera de Winget (ARP no correlacionado: -1978335212), instalar encima con --force conservando datos
            if (exitCode == -1978335212 ||
                outText.IndexOf("No installed package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                outText.IndexOf("No se encontr", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EmitLog("[*] Paquete instalado fuera de Winget; aplicando ultima version encima con winget install --force...");
                string forceInstallArgs = string.Format(
                    "install --id \"{0}\" --exact --source {1}{2} --force --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    targetId,
                    pkgSource,
                    silentFlag);
                StringBuilder forceOut = new StringBuilder();
                int forceCode = await RunProcessAsync("winget", forceInstallArgs, forceOut);
                if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Actualizacion cancelada");
                if (forceCode == 0)
                {
                    EmitLog("[OK] " + app.Name + " actualizada correctamente mediante instalacion directa.");
                    return MakeResult(app, InstallStatus.Success, "Actualizada a la ultima version");
                }
            }

            // Respaldo 2: si fallo en modo silencioso, reintentar sin --silent para instaladores que exigen ventana
            if (silent && !_cancelRequested)
            {
                EmitLog("[!] El actualizador rechazo el modo silencioso; reintentando actualizacion asistida...");
                string interactiveArgs = string.Format(
                    "upgrade --id \"{0}\" --exact --source {1} --include-unknown --accept-package-agreements --accept-source-agreements",
                    targetId,
                    pkgSource);
                StringBuilder intOut = new StringBuilder();
                int intCode = await RunProcessAsync("winget", interactiveArgs, intOut);
                if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Actualizacion cancelada");
                if (intCode == 0)
                {
                    EmitLog("[OK] " + app.Name + " actualizada correctamente.");
                    return MakeResult(app, InstallStatus.Success, "Actualizada a la ultima version");
                }
            }

            EmitLog(string.Format("[X] Error actualizando {0} (codigo {1}).", app.Name, exitCode));
            return MakeResult(app, InstallStatus.Failed, "Error de Winget al actualizar (codigo " + exitCode + ")");
        }

        private static int FindColumnStartAny(string headerLine, string[] candidates)
        {
            if (string.IsNullOrEmpty(headerLine) || candidates == null) return -1;
            for (int i = 0; i < candidates.Length; i++)
            {
                int idx = FindColumnStart(headerLine, candidates[i]);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        private static string SliceColumnSafe(string line, int startCol, int nextCol)
        {
            if (string.IsNullOrEmpty(line) || startCol < 0 || startCol >= line.Length) return string.Empty;
            if (nextCol > startCol)
            {
                int len = Math.Min(nextCol - startCol, line.Length - startCol);
                return len > 0 ? line.Substring(startCol, len).Trim() : string.Empty;
            }
            return line.Substring(startCol).Trim();
        }

        private static Dictionary<string, string> CollectRegistryDisplayVersions()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                                        object dnObj = sk.GetValue("DisplayName");
                                        object dvObj = sk.GetValue("DisplayVersion");
                                        if (dnObj == null || dvObj == null) continue;
                                        string dn = dnObj.ToString().Trim();
                                        string dv = dvObj.ToString().Trim();
                                        if (!string.IsNullOrEmpty(dn) && !string.IsNullOrEmpty(dv))
                                        {
                                            string existing;
                                            if (!map.TryGetValue(dn, out existing) || IsVersionOlder(existing, dv))
                                            {
                                                map[dn] = dv;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return map;
        }

        private static string FindRegistryVersionForApp(AppItem app, string wingetRowName, Dictionary<string, string> registryVersions)
        {
            if (registryVersions == null || registryVersions.Count == 0) return string.Empty;
            string exact;
            if (!string.IsNullOrWhiteSpace(wingetRowName))
            {
                string cleanRow = wingetRowName.TrimEnd('\u2026', '.').Trim();
                if (registryVersions.TryGetValue(cleanRow, out exact)) return exact;
                foreach (KeyValuePair<string, string> kv in registryVersions)
                {
                    if (cleanRow.Length >= 4 && kv.Key.StartsWith(cleanRow, StringComparison.OrdinalIgnoreCase))
                    {
                        return kv.Value;
                    }
                }
            }
            if (app != null && !string.IsNullOrWhiteSpace(app.Name))
            {
                string appName = app.Name.Trim();
                if (registryVersions.TryGetValue(appName, out exact)) return exact;
                foreach (KeyValuePair<string, string> kv in registryVersions)
                {
                    if (kv.Key.StartsWith(appName + " ", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.StartsWith(appName + "-", StringComparison.OrdinalIgnoreCase))
                    {
                        return kv.Value;
                    }
                }
            }
            return string.Empty;
        }

        public static List<InstalledUpdateInfo> ParseRawWingetUpgradeRows(string output)
        {
            List<InstalledUpdateInfo> rows = new List<InstalledUpdateInfo>();
            if (string.IsNullOrWhiteSpace(output)) return rows;

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int headerIdx = -1;
            int idCol = -1;
            int verCol = -1;
            int availCol = -1;
            int srcCol = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int candId = FindColumnStart(line, "Id");
                int candAvail = FindColumnStartAny(line, new[] { "Available", "Disponible" });
                if (candId > 0 && candAvail > candId)
                {
                    headerIdx = i;
                    idCol = candId;
                    verCol = FindColumnStartAny(line, new[] { "Version", "Versi\u00f3n", "Versi" });
                    availCol = candAvail;
                    srcCol = FindColumnStartAny(line, new[] { "Source", "Origen" });
                    break;
                }
            }

            if (headerIdx < 0 || idCol <= 0) return rows;

            int startRow = headerIdx + 1;
            for (int i = headerIdx + 1; i < Math.Min(lines.Length, headerIdx + 4); i++)
            {
                if (lines[i].TrimStart().StartsWith("---", StringComparison.Ordinal))
                {
                    startRow = i + 1;
                    break;
                }
            }

            for (int i = startRow; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("---", StringComparison.Ordinal)) continue;
                if (trimmed.IndexOf("upgrades available", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("actualizaciones disponibles", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("upgrade available", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("actualizaci\u00f3n disponible", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("package(s) have version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf("tienen n\u00fameros de versi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (line.Length <= idCol) continue;

                string rowName = line.Substring(0, Math.Min(idCol, line.Length)).Trim();
                string pkgId = string.Empty;
                string curVer = string.Empty;
                string availVer = string.Empty;

                // Intentar extraccion exacta por columnas fijas si los indices son consistentes
                if (verCol > idCol && availCol > verCol && line.Length > availCol)
                {
                    pkgId = SliceColumnSafe(line, idCol, verCol);
                    curVer = SliceColumnSafe(line, verCol, availCol);
                    availVer = SliceColumnSafe(line, availCol, srcCol > availCol ? srcCol : -1);
                    if (srcCol <= 0 && availVer.IndexOf(' ') > 0)
                    {
                        string[] avParts = availVer.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (avParts.Length >= 2 &&
                            (string.Equals(avParts[avParts.Length - 1], "winget", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(avParts[avParts.Length - 1], "msstore", StringComparison.OrdinalIgnoreCase)))
                        {
                            availVer = avParts[0];
                        }
                    }
                }

                // Respaldo por tokens si caracteres anchos desplazaron las columnas
                if (string.IsNullOrEmpty(pkgId) || string.IsNullOrEmpty(availVer) || pkgId.IndexOf(' ') >= 0)
                {
                    string rest = line.Substring(idCol).Trim();
                    string[] tokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 3) continue;
                    pkgId = tokens[0].Trim();
                    int idx = 1;
                    if (tokens[idx] == "<" && tokens.Length >= 4) idx++;
                    curVer = tokens[idx].Trim();
                    availVer = (idx + 1 < tokens.Length) ? tokens[idx + 1].Trim() : string.Empty;
                }

                if (curVer.StartsWith("<")) curVer = curVer.Substring(1).Trim();
                if (string.IsNullOrEmpty(pkgId) || string.IsNullOrEmpty(availVer)) continue;
                string cleanCheck = pkgId.TrimEnd('\u2026', '.');
                if (cleanCheck.IndexOf('.') < 0 && cleanCheck.Length < 8) continue;

                InstalledUpdateInfo info = new InstalledUpdateInfo();
                info.Name = rowName;
                info.Id = pkgId;
                info.CurrentVersion = curVer;
                info.AvailableVersion = availVer;
                rows.Add(info);
            }

            return rows;
        }

        public static Dictionary<string, InstalledUpdateInfo> ParseWingetUpgradeOutput(
            string output,
            List<AppItem> apps,
            Dictionary<string, string> registryVersions)
        {
            Dictionary<string, InstalledUpdateInfo> result = new Dictionary<string, InstalledUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            if (apps == null || apps.Count == 0 || string.IsNullOrWhiteSpace(output)) return result;

            List<InstalledUpdateInfo> rawRows = ParseRawWingetUpgradeRows(output);
            if (rawRows.Count == 0) return result;

            Dictionary<string, InstalledUpdateInfo> byExactId = new Dictionary<string, InstalledUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            List<InstalledUpdateInfo> truncatedRows = new List<InstalledUpdateInfo>();

            for (int i = 0; i < rawRows.Count; i++)
            {
                InstalledUpdateInfo r = rawRows[i];
                if (r.Id.EndsWith("\u2026", StringComparison.Ordinal) || r.Id.EndsWith("...", StringComparison.Ordinal))
                {
                    truncatedRows.Add(r);
                }
                else
                {
                    byExactId[r.Id] = r;
                }
            }

            for (int i = 0; i < apps.Count; i++)
            {
                AppItem app = apps[i];
                if (app == null || string.IsNullOrWhiteSpace(app.Target)) continue;
                string src = (app.Source ?? "winget").Trim().ToLowerInvariant();
                if (src != "winget") continue;

                string target = app.Target.Trim();
                InstalledUpdateInfo matched = null;
                if (!byExactId.TryGetValue(target, out matched))
                {
                    for (int t = 0; t < truncatedRows.Count; t++)
                    {
                        string prefix = truncatedRows[t].Id.TrimEnd('\u2026', '.');
                        if (prefix.Length >= 10 && target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = truncatedRows[t];
                            break;
                        }
                    }
                }

                if (matched == null) continue;

                string curVer = matched.CurrentVersion;
                string availVer = matched.AvailableVersion;
                if (IsUnknownVersionToken(availVer)) continue;

                if (IsUnknownVersionToken(curVer))
                {
                    string regVer = FindRegistryVersionForApp(app, matched.Name, registryVersions);
                    if (!string.IsNullOrEmpty(regVer))
                    {
                        // Evitar falso positivo de --include-unknown cuando la version instalada en Registro ya esta al dia
                        if (!IsVersionOlder(regVer, availVer))
                        {
                            continue;
                        }
                        curVer = NormalizeVersionDisplay(regVer);
                    }
                    else
                    {
                        curVer = "?";
                    }
                }
                else
                {
                    curVer = NormalizeVersionDisplay(curVer);
                    if (!IsVersionOlder(curVer, availVer))
                    {
                        continue;
                    }
                }

                InstalledUpdateInfo finalUpd = new InstalledUpdateInfo();
                finalUpd.Name = matched.Name;
                finalUpd.Id = target;
                finalUpd.CurrentVersion = curVer;
                finalUpd.AvailableVersion = NormalizeVersionDisplay(availVer);

                string key = (app.Id ?? app.Name ?? string.Empty) + "|" + target;
                result[key] = finalUpd;
            }

            return result;
        }

        public async Task<Dictionary<string, InstalledUpdateInfo>> DetectAvailableUpdatesAsync(List<AppItem> apps)
        {
            Dictionary<string, InstalledUpdateInfo> result = new Dictionary<string, InstalledUpdateInfo>(StringComparer.OrdinalIgnoreCase);
            if (apps == null || apps.Count == 0) return result;

            try
            {
                string output = await RunQuickQueryAsync(
                    "winget",
                    "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                    45000);
                if (string.IsNullOrWhiteSpace(output)) return result;

                Dictionary<string, string> regVersions = CollectRegistryDisplayVersions();
                return ParseWingetUpgradeOutput(output, apps, regVersions);
            }
            catch
            {
            }

            return result;
        }

        public async Task<int> UpgradeAllInstalledAsync(bool silent)
        {
            EmitLog("--------------------------------------------------");
            EmitLog("[*] Escaneando actualizaciones pendientes del sistema en Winget...");
            string listOutput = await RunQuickQueryAsync(
                "winget",
                "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                45000);

            if (_cancelRequested)
            {
                EmitLog("[!] Actualizacion masiva cancelada por el usuario.");
                return -2;
            }

            List<InstalledUpdateInfo> rawRows = ParseRawWingetUpgradeRows(listOutput);
            Dictionary<string, string> regVersions = CollectRegistryDisplayVersions();
            List<InstalledUpdateInfo> safeTargets = new List<InstalledUpdateInfo>();

            for (int i = 0; i < rawRows.Count; i++)
            {
                InstalledUpdateInfo row = rawRows[i];
                if (string.IsNullOrEmpty(row.Id)) continue;
                // No matar WebView2 ni el propio DesktopAppInstaller mientras qInstall esta abierto
                if (row.Id.IndexOf("EdgeWebView2Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.Id.IndexOf("DesktopAppInstaller", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }
                if (row.Id.EndsWith("\u2026", StringComparison.Ordinal) || row.Id.EndsWith("...", StringComparison.Ordinal))
                {
                    continue;
                }
                if (IsUnknownVersionToken(row.AvailableVersion)) continue;
                if (IsUnknownVersionToken(row.CurrentVersion))
                {
                    string regVer = FindRegistryVersionForApp(null, row.Name, regVersions);
                    if (string.IsNullOrEmpty(regVer) || !IsVersionOlder(regVer, row.AvailableVersion))
                    {
                        continue;
                    }
                }
                else if (!IsVersionOlder(row.CurrentVersion, row.AvailableVersion))
                {
                    continue;
                }
                safeTargets.Add(row);
            }

            if (safeTargets.Count == 0)
            {
                EmitLog("[=] Todos los paquetes compatibles del sistema estan al dia.");
                return 0;
            }

            int failed = 0;
            string silentFlag = silent ? " --silent" : string.Empty;
            for (int i = 0; i < safeTargets.Count; i++)
            {
                if (_cancelRequested)
                {
                    EmitLog("[!] Actualizacion masiva cancelada por el usuario.");
                    return -2;
                }
                InstalledUpdateInfo item = safeTargets[i];
                EmitLog(string.Format("[*] Actualizando paquete del sistema ({0}/{1}): {2} ({3})", i + 1, safeTargets.Count, string.IsNullOrEmpty(item.Name) ? item.Id : item.Name, item.Id));
                string uArgs = string.Format(
                    "upgrade --id \"{0}\" --exact{1} --include-unknown --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    item.Id,
                    silentFlag);
                StringBuilder uOut = new StringBuilder();
                int uCode = await RunProcessAsync("winget", uArgs, uOut);
                if (_cancelRequested) return -2;
                if (uCode != 0 && uCode != -1978335189)
                {
                    failed++;
                }
            }

            if (failed == 0)
            {
                EmitLog("[OK] Actualizacion masiva del sistema finalizada sin errores.");
                return 0;
            }
            EmitLog(string.Format("[!] Actualizacion masiva finalizada ({0} paquete(s) no pudieron actualizarse silenciosamente).", failed));
            return failed;
        }

        public async Task<bool> CreateSystemRestorePointAsync(string description)
        {
            string safeDesc = string.IsNullOrWhiteSpace(description)
                ? ("Qinstall_" + DateTime.Now.ToString("yyyyMMdd_HHmm"))
                : description.Replace("'", string.Empty).Replace("\"", string.Empty).Trim();

            EmitLog("--------------------------------------------------");
            EmitLog("[*] Creando punto de restauracion de Windows: " + safeDesc);
            string script = string.Format(
                "Enable-ComputerRestore -Drive ($env:SystemDrive + '\\') -ErrorAction SilentlyContinue; Checkpoint-Computer -Description '{0}' -RestorePointType 'MODIFY_SETTINGS'",
                safeDesc);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            StringBuilder output = new StringBuilder();
            int code = await RunProcessAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, output);
            if (code == 0)
            {
                EmitLog("[OK] Punto de restauracion '" + safeDesc + "' creado correctamente.");
                return true;
            }
            EmitLog("[X] No se pudo crear el punto de restauracion (codigo " + code + "). Verifica proteccion del sistema o frecuencia de 24h de Windows.");
            return false;
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

        private async Task EnsureOfflineVersionMetadataAsync(AppItem app, string cacheDir, string knownVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir)) return;
                string existingVer = GetCachedOfflineVersion(app);
                if (!string.IsNullOrEmpty(existingVer) && File.Exists(Path.Combine(cacheDir, "qinstall_meta.yaml")))
                {
                    return;
                }
                string verToSave = !string.IsNullOrWhiteSpace(knownVersion) ? knownVersion.Trim() : existingVer;
                if (string.IsNullOrEmpty(verToSave) && app != null && !string.IsNullOrWhiteSpace(app.Target))
                {
                    verToSave = await GetWingetLatestVersionAsync(app.Target);
                }
                if (!string.IsNullOrEmpty(verToSave))
                {
                    string metaPath = Path.Combine(cacheDir, "qinstall_meta.yaml");
                    if (!File.Exists(metaPath))
                    {
                        File.WriteAllText(metaPath, "PackageVersion: " + NormalizeVersionDisplay(verToSave) + Environment.NewLine, new UTF8Encoding(false));
                    }
                }
            }
            catch { }
        }

        public Task<InstallResult> DownloadForOfflineAsync(AppItem app)
        {
            return DownloadForOfflineAsync(app, null);
        }

        public async Task<InstallResult> DownloadForOfflineAsync(AppItem app, Action<int> onProgress)
        {
            if (_cancelRequested)
            {
                return MakeResult(app, InstallStatus.Cancelled, "Cancelado");
            }

            string source = (app.Source ?? "winget").Trim().ToLowerInvariant();
            string blockReason = GetOfflineBlockReason(app);
            if (!string.IsNullOrEmpty(blockReason))
            {
                EmitLog("[!] Omitido de descarga offline: " + app.Name + " — " + blockReason);
                return MakeResult(app, InstallStatus.Failed, blockReason);
            }
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

                    // Via rapida: obtener Installer Url + SHA256 con winget show y descargar en 4 conexiones paralelas
                    if (pkgSource == "winget")
                    {
                        string fastFile = await TryFastDirectWingetDownloadAsync(app, cacheDir, onProgress);
                        if (_cancelRequested)
                        {
                            ClearDirectoryFiles(cacheDir);
                            TryDeleteDirectoryIfNoInstaller(cacheDir);
                            return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                        }
                        if (!string.IsNullOrEmpty(fastFile) && File.Exists(fastFile))
                        {
                            EmitLog("[OK] Descarga turbo completada en: " + fastFile);
                            return MakeResult(app, InstallStatus.Success, "Descargado rapido en local\\cache (" + Path.GetFileName(fastFile) + ")");
                        }
                    }

                    // Respaldo universal: winget download oficial
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
                        TryDeleteDirectoryIfNoInstaller(cacheDir);
                        return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                    }

                    string downloadedFile = FindCachedInstallerPath(app);
                    if (code == 0 && !string.IsNullOrEmpty(downloadedFile))
                    {
                        await EnsureOfflineVersionMetadataAsync(app, cacheDir, null);
                        EmitLog("[OK] Guardado offline en: " + downloadedFile);
                        return MakeResult(app, InstallStatus.Success, "Guardado en local\\cache (" + Path.GetFileName(downloadedFile) + ")");
                    }

                    // Si la ultima version del manifiesto Winget devolvio HTTP 404 (-2145844844), probar automaticamente las 2 versiones anteriores estables
                    string outText = output.ToString();
                    bool isNotFound404 =
                        code == -2145844844 ||
                        outText.IndexOf("0x80190194", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        outText.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isNotFound404 && pkgSource == "winget" && !_cancelRequested)
                    {
                        EmitLog("[!] La ultima version de " + app.Name + " devolvio HTTP 404 en su servidor; probando version anterior estable en Winget...");
                        await Task.Delay(500);
                        List<string> prevVersions = await GetWingetPreviousVersionsAsync(app.Target.Trim(), 2);
                        if (prevVersions.Count == 0)
                        {
                            EmitLog("[!] No se pudieron listar versiones anteriores en Winget para " + app.Name + ".");
                        }
                        for (int pv = 0; pv < prevVersions.Count && !_cancelRequested; pv++)
                        {
                            string fallbackVer = prevVersions[pv];
                            EmitLog("[*] Intentando version anterior estable: " + app.Name + " v" + fallbackVer);
                            ClearDirectoryFiles(cacheDir);

                            string fastPrev = await TryFastDirectWingetDownloadAsync(app, cacheDir, onProgress, fallbackVer);
                            if (_cancelRequested)
                            {
                                ClearDirectoryFiles(cacheDir);
                                TryDeleteDirectoryIfNoInstaller(cacheDir);
                                return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                            }
                            if (!string.IsNullOrEmpty(fastPrev) && File.Exists(fastPrev))
                            {
                                EmitLog("[OK] Descarga turbo completada (v" + fallbackVer + "): " + fastPrev);
                                return MakeResult(app, InstallStatus.Success, "Descargado rapido en local\\cache (" + Path.GetFileName(fastPrev) + " · v" + fallbackVer + ")");
                            }

                            string prevArgs = string.Format(
                                "download --id \"{0}\" --exact --version \"{1}\" --source winget -d \"{2}\" --accept-package-agreements --accept-source-agreements --skip-dependencies --disable-interactivity",
                                app.Target.Trim(),
                                fallbackVer,
                                cacheDir);
                            StringBuilder prevOut = new StringBuilder();
                            int prevCode = await RunProcessAsync("winget", prevArgs, prevOut);
                            if (_cancelRequested)
                            {
                                TryDeleteDirectoryIfNoInstaller(cacheDir);
                                return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                            }
                            string prevFile = FindCachedInstallerPath(app);
                            if (prevCode == 0 && !string.IsNullOrEmpty(prevFile))
                            {
                                await EnsureOfflineVersionMetadataAsync(app, cacheDir, fallbackVer);
                                EmitLog("[OK] Guardado offline (v" + fallbackVer + ") en: " + prevFile);
                                return MakeResult(app, InstallStatus.Success, "Guardado en local\\cache (" + Path.GetFileName(prevFile) + " · v" + fallbackVer + ")");
                            }
                        }
                    }

                    TryDeleteDirectoryIfNoInstaller(cacheDir);
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
                        TryDeleteDirectoryIfNoInstaller(cacheDir);
                        return MakeResult(app, InstallStatus.Failed, "URL invalida");
                    }

                    string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
                    string destFile = Path.Combine(cacheDir, safeId + ext);
                    bool ok = await DownloadFileAsync(app.Target.Trim(), destFile, onProgress);
                    if (_cancelRequested)
                    {
                        TryDeleteFile(destFile);
                        TryDeleteDirectoryIfNoInstaller(cacheDir);
                        return MakeResult(app, InstallStatus.Cancelled, "Descarga cancelada");
                    }
                    if (ok && File.Exists(destFile))
                    {
                        EmitLog("[OK] Guardado offline en: " + destFile);
                        return MakeResult(app, InstallStatus.Success, "Guardado en local\\cache (" + Path.GetFileName(destFile) + ")");
                    }
                    TryDeleteDirectoryIfNoInstaller(cacheDir);
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

        private static void TryDeleteDirectoryIfNoInstaller(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]);
                    bool isPkg =
                        string.Equals(ext, ".msi", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".msix", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".appx", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".appxbundle", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase);
                    if (isPkg && new FileInfo(files[i]).Length > 0)
                    {
                        return;
                    }
                }
                Directory.Delete(dir, true);
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
                for (int i = 0; i < _activeRequests.Count; i++)
                {
                    try { _activeRequests[i].Abort(); } catch { }
                }
                for (int i = 0; i < _activeProcesses.Count; i++)
                {
                    Process p = _activeProcesses[i];
                    if (p == null) continue;
                    try
                    {
                        if (!p.HasExited) p.Kill();
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

        public static string GetWingetOfflineKitDirectory()
        {
            return Path.Combine(AppCatalog.GetLocalInstallersDirectory(), "winget");
        }

        public static string FindExistingWingetKitDirectory()
        {
            string relDir = Path.Combine("local", "winget");
            List<string> candidates = new List<string>();
            string baseDir = AppCatalog.GetAppBaseDirectory();
            candidates.Add(Path.Combine(baseDir, relDir));
            try
            {
                string root = Path.GetPathRoot(baseDir);
                if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, relDir));
            }
            catch { }
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    if (drives[i].DriveType == DriveType.Removable && drives[i].IsReady)
                    {
                        candidates.Add(Path.Combine(drives[i].RootDirectory.FullName, relDir));
                    }
                }
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsWingetKitDirectoryComplete(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return null;
        }

        private static bool IsWingetKitDirectoryComplete(string kitDir)
        {
            try
            {
                if (string.IsNullOrEmpty(kitDir) || !Directory.Exists(kitDir)) return false;
                string bundle = Path.Combine(kitDir, "Microsoft.DesktopAppInstaller.msixbundle");
                if (!File.Exists(bundle) || new FileInfo(bundle).Length < 1024 * 1024) return false;

                string depsDir = Path.Combine(kitDir, "deps");
                if (!Directory.Exists(depsDir)) return false;
                string[] deps = Directory.GetFiles(depsDir);
                int validDeps = 0;
                for (int i = 0; i < deps.Length; i++)
                {
                    string ext = Path.GetExtension(deps[i]).ToLowerInvariant();
                    if ((ext == ".appx" || ext == ".msix") && new FileInfo(deps[i]).Length > 50 * 1024)
                    {
                        validDeps++;
                    }
                }
                return validDeps >= 2;
            }
            catch
            {
                return false;
            }
        }

        public static bool HasOfflineWingetKit()
        {
            return !string.IsNullOrEmpty(FindExistingWingetKitDirectory());
        }

        public static long GetOfflineWingetKitSizeBytes()
        {
            string dir = FindExistingWingetKitDirectory();
            return !string.IsNullOrEmpty(dir) ? GetDirectorySizeBytes(dir) : 0;
        }

        private string FetchHttpStringQuick(string url, int timeoutMs)
        {
            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Qinstall/" + CurrentAppVersion;
                req.Accept = "application/vnd.github+json, application/json, */*";
                req.AllowAutoRedirect = true;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                RegisterRequest(req);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                UnregisterRequest(req);
            }
        }

        private string ResolveRedirectFinalUrl(string url, int timeoutMs)
        {
            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Qinstall/" + CurrentAppVersion;
                req.AllowAutoRedirect = true;
                req.Timeout = timeoutMs;
                RegisterRequest(req);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return resp.ResponseUri != null ? resp.ResponseUri.AbsoluteUri : null;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                UnregisterRequest(req);
            }
        }

        private static int ExtractX64DependenciesFromZip(string zipPath, string targetDepsDir)
        {
            int count = 0;
            Directory.CreateDirectory(targetDepsDir);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    string fullName = (entry.FullName ?? string.Empty).Replace('\\', '/');
                    string ext = Path.GetExtension(fullName).ToLowerInvariant();
                    if (ext != ".appx" && ext != ".msix") continue;

                    bool isX64 = fullName.IndexOf("x64/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 fullName.IndexOf("_x64", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 fullName.IndexOf(".x64.", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isX64) continue;

                    string fileName = Path.GetFileName(fullName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    string destFile = Path.Combine(targetDepsDir, fileName);
                    entry.ExtractToFile(destFile, true);
                    count++;
                }
            }
            return count;
        }

        public async Task<bool> DownloadWingetOfflineKitAsync(Action<int> onProgress)
        {
            string stageRoot = Path.Combine(Path.GetTempPath(), "Qinstall_winget_dl_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string stageDeps = Path.Combine(stageRoot, "deps");
            try
            {
                Directory.CreateDirectory(stageDeps);
                EmitLog("[*] Preparando kit completo de Winget + dependencias oficiales (VCLibs + UI.Xaml / WindowsAppRuntime)...");
                ReportDownloadPct(5, onProgress);

                string bundleUrl = "https://github.com/microsoft/winget-cli/releases/latest/download/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
                string depsZipUrl = "https://github.com/microsoft/winget-cli/releases/latest/download/DesktopAppInstaller_Dependencies.zip";
                string licenseUrl = null;

                // Consultar API oficial de microsoft/winget-cli para obtener tambien License1.xml (clave para Windows LTSC / Server)
                string releaseJson = await Task.Run(delegate
                {
                    return FetchHttpStringQuick("https://api.github.com/repos/microsoft/winget-cli/releases/latest", 8000);
                });
                if (!string.IsNullOrEmpty(releaseJson))
                {
                    try
                    {
                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        Dictionary<string, object> rel = ser.Deserialize<Dictionary<string, object>>(releaseJson);
                        if (rel != null && rel.ContainsKey("assets"))
                        {
                            System.Collections.IEnumerable assets = rel["assets"] as System.Collections.IEnumerable;
                            if (assets != null && !(assets is string))
                            {
                                foreach (object aObj in assets)
                                {
                                    Dictionary<string, object> a = aObj as Dictionary<string, object>;
                                    if (a == null) continue;
                                    string name = a.ContainsKey("name") && a["name"] != null ? a["name"].ToString() : string.Empty;
                                    string dl = a.ContainsKey("browser_download_url") && a["browser_download_url"] != null ? a["browser_download_url"].ToString() : string.Empty;
                                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dl)) continue;

                                    if (name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)) bundleUrl = dl;
                                    else if (name.Equals("DesktopAppInstaller_Dependencies.zip", StringComparison.OrdinalIgnoreCase)) depsZipUrl = dl;
                                    else if (name.EndsWith("License1.xml", StringComparison.OrdinalIgnoreCase)) licenseUrl = dl;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (_cancelRequested) return false;

                // 1. Descargar dependencias oficiales x64
                string depsZipPath = Path.Combine(stageRoot, "DesktopAppInstaller_Dependencies.zip");
                EmitLog("[*] Descargando dependencias base de Winget (x64)...");
                bool gotDepsZip = await DownloadFileAsync(depsZipUrl, depsZipPath, delegate(int p)
                {
                    ReportDownloadPct(5 + (p * 45) / 100, onProgress);
                });

                int extractedDeps = 0;
                if (gotDepsZip && File.Exists(depsZipPath))
                {
                    try
                    {
                        extractedDeps = ExtractX64DependenciesFromZip(depsZipPath, stageDeps);
                    }
                    catch (Exception zex)
                    {
                        EmitLog("[!] Aviso extrayendo Dependencies.zip: " + zex.Message);
                    }
                    TryDeleteFile(depsZipPath);
                }

                // Respaldo directo Microsoft + NuGet si GitHub Dependencies.zip fallo
                if (extractedDeps < 2 && !_cancelRequested)
                {
                    EmitLog("[*] Usando enlaces de respaldo directos para VCLibs 14 y Microsoft.UI.Xaml 2.8...");
                    string vcPath = Path.Combine(stageDeps, "Microsoft.VCLibs.x64.14.00.Desktop.appx");
                    bool okVc = await DownloadFileAsync("https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx", vcPath);
                    string nugetZip = Path.Combine(stageRoot, "uixaml.zip");
                    bool okXamlZip = await DownloadFileAsync("https://www.nuget.org/api/v2/package/Microsoft.UI.Xaml/2.8.6", nugetZip);
                    if (okXamlZip && File.Exists(nugetZip))
                    {
                        try { extractedDeps += ExtractX64DependenciesFromZip(nugetZip, stageDeps); } catch { }
                        TryDeleteFile(nugetZip);
                    }
                    if (okVc && File.Exists(vcPath)) extractedDeps++;
                }

                if (_cancelRequested) return false;

                // 2. Descargar paquete principal Microsoft.DesktopAppInstaller.msixbundle
                string stageBundle = Path.Combine(stageRoot, "Microsoft.DesktopAppInstaller.msixbundle");
                EmitLog("[*] Descargando paquete principal Microsoft.DesktopAppInstaller (.msixbundle)...");
                bool gotBundle = await DownloadFileAsync(bundleUrl, stageBundle, delegate(int p)
                {
                    ReportDownloadPct(50 + (p * 42) / 100, onProgress);
                });
                if ((!gotBundle || !File.Exists(stageBundle)) && !_cancelRequested)
                {
                    EmitLog("[*] Probando enlace oficial alternativo aka.ms/getwinget...");
                    gotBundle = await DownloadFileAsync("https://aka.ms/getwinget", stageBundle, delegate(int p)
                    {
                        ReportDownloadPct(50 + (p * 42) / 100, onProgress);
                    });
                }

                if (!gotBundle || !File.Exists(stageBundle) || _cancelRequested)
                {
                    EmitLog("[X] No se pudo descargar Microsoft.DesktopAppInstaller.msixbundle.");
                    return false;
                }

                // 3. Descargar archivo de licencia oficial si esta disponible (para aprovisionamiento offline / LTSC)
                string stageLicense = Path.Combine(stageRoot, "License1.xml");
                if (!string.IsNullOrEmpty(licenseUrl) && !_cancelRequested)
                {
                    await DownloadFileAsync(licenseUrl, stageLicense);
                }

                // 4. Guardar copia reutilizable en local\winget\ del pendrive/unidad si hay espacio suficiente (>= 200 MB libres)
                string targetKitDir = GetWingetOfflineKitDirectory();
                bool canPersistToUsb = true;
                try
                {
                    string root = Path.GetPathRoot(targetKitDir);
                    if (!string.IsNullOrEmpty(root))
                    {
                        DriveInfo di = new DriveInfo(root);
                        if (di.IsReady && di.AvailableFreeSpace < (200L * 1024L * 1024L))
                        {
                            canPersistToUsb = false;
                            EmitLog("[!] Espacio libre ajustado en el USB (< 200 MB): instalando Winget desde %TEMP% sin ocupar espacio permanente.");
                        }
                    }
                }
                catch { }

                string finalKitDir = canPersistToUsb ? targetKitDir : Path.Combine(Path.GetTempPath(), "Qinstall", "winget_kit");
                try
                {
                    string finalDeps = Path.Combine(finalKitDir, "deps");
                    if (Directory.Exists(finalKitDir)) Directory.Delete(finalKitDir, true);
                    Directory.CreateDirectory(finalDeps);

                    File.Copy(stageBundle, Path.Combine(finalKitDir, "Microsoft.DesktopAppInstaller.msixbundle"), true);
                    if (File.Exists(stageLicense))
                    {
                        File.Copy(stageLicense, Path.Combine(finalKitDir, "License1.xml"), true);
                    }
                    string[] depFiles = Directory.GetFiles(stageDeps);
                    for (int i = 0; i < depFiles.Length; i++)
                    {
                        File.Copy(depFiles[i], Path.Combine(finalDeps, Path.GetFileName(depFiles[i])), true);
                    }
                    if (canPersistToUsb)
                    {
                        EmitLog("[OK] Kit completo de Winget guardado en local\\winget\\ (listo para reutilizar sin Internet en otros equipos).");
                    }
                }
                catch (Exception pex)
                {
                    EmitLog("[!] No se pudo guardar copia en local\\winget\\ (" + pex.Message + "); usando copia temporal.");
                    string fallbackKit = Path.Combine(Path.GetTempPath(), "Qinstall", "winget_kit");
                    string fallbackDeps = Path.Combine(fallbackKit, "deps");
                    Directory.CreateDirectory(fallbackDeps);
                    File.Copy(stageBundle, Path.Combine(fallbackKit, "Microsoft.DesktopAppInstaller.msixbundle"), true);
                    if (File.Exists(stageLicense)) File.Copy(stageLicense, Path.Combine(fallbackKit, "License1.xml"), true);
                    string[] depFiles = Directory.GetFiles(stageDeps);
                    for (int i = 0; i < depFiles.Length; i++)
                    {
                        File.Copy(depFiles[i], Path.Combine(fallbackDeps, Path.GetFileName(depFiles[i])), true);
                    }
                }

                ReportDownloadPct(100, onProgress);
                return true;
            }
            finally
            {
                try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true); } catch { }
            }
        }

        public async Task<bool> InstallWingetAsync()
        {
            InvalidateWingetCache();
            string kitDir = FindExistingWingetKitDirectory();
            bool online = await CheckInternetAsync();

            if (string.IsNullOrEmpty(kitDir))
            {
                if (!online)
                {
                    EmitLog("[X] No hay conexion a Internet ni existe todavia el kit offline en local\\winget\\.");
                    return false;
                }
                bool okKit = await DownloadWingetOfflineKitAsync(null);
                if (!okKit || _cancelRequested) return false;
                kitDir = FindExistingWingetKitDirectory();
                if (string.IsNullOrEmpty(kitDir))
                {
                    string tempKit = Path.Combine(Path.GetTempPath(), "Qinstall", "winget_kit");
                    if (Directory.Exists(tempKit)) kitDir = tempKit;
                }
            }
            else
            {
                EmitLog("[*] Kit offline de Winget detectado en: " + kitDir);
            }

            if (string.IsNullOrEmpty(kitDir) || !Directory.Exists(kitDir))
            {
                EmitLog("[X] No se encontraron los archivos de instalacion de Winget.");
                return false;
            }

            // Copiar siempre a %TEMP% local en C:\ para evitar bloqueos 0x80070005 de AppxDeploymentServer sobre unidades USB FAT32/exFAT
            string localStage = Path.Combine(Path.GetTempPath(), "Qinstall_wg_stage_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                string stageDeps = Path.Combine(localStage, "deps");
                Directory.CreateDirectory(stageDeps);
                string srcBundle = Path.Combine(kitDir, "Microsoft.DesktopAppInstaller.msixbundle");
                string dstBundle = Path.Combine(localStage, "Microsoft.DesktopAppInstaller.msixbundle");
                File.Copy(srcBundle, dstBundle, true);

                string srcLicense = Path.Combine(kitDir, "License1.xml");
                string dstLicense = Path.Combine(localStage, "License1.xml");
                if (File.Exists(srcLicense)) File.Copy(srcLicense, dstLicense, true);

                string srcDeps = Path.Combine(kitDir, "deps");
                if (Directory.Exists(srcDeps))
                {
                    string[] dFiles = Directory.GetFiles(srcDeps);
                    for (int i = 0; i < dFiles.Length; i++)
                    {
                        File.Copy(dFiles[i], Path.Combine(stageDeps, Path.GetFileName(dFiles[i])), true);
                    }
                }

                EmitLog("[*] Instalando dependencias (VCLibs / UI.Xaml / AppRuntime) y App Installer (Winget)...");
                string escapedStage = localStage.Replace("'", "''");
                string psScript =
                    "$ErrorActionPreference = 'Continue'; " +
                    "$stage = '" + escapedStage + "'; " +
                    "$bundle = Join-Path $stage 'Microsoft.DesktopAppInstaller.msixbundle'; " +
                    "$lic = Join-Path $stage 'License1.xml'; " +
                    "$deps = @(Get-ChildItem -Path (Join-Path $stage 'deps') -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.appx','.msix' } | Sort-Object Name | ForEach-Object { $_.FullName }); " +
                    "foreach ($d in $deps) { " +
                    "  Write-Output ('Instalando dependencia: ' + [System.IO.Path]::GetFileName($d)); " +
                    "  try { Add-AppxPackage -Path $d -ForceApplicationShutdown -ErrorAction Stop } catch { Write-Output ('Aviso dep: ' + $_.Exception.Message) } " +
                    "} " +
                    "$appxOk = $false; " +
                    "try { " +
                    "  if ($deps.Count -gt 0) { Add-AppxPackage -Path $bundle -DependencyPath $deps -ForceApplicationShutdown -ErrorAction Stop } " +
                    "  else { Add-AppxPackage -Path $bundle -ForceApplicationShutdown -ErrorAction Stop } " +
                    "  $appxOk = $true; Write-Output 'Add-AppxPackage completado.'; " +
                    "} catch { Write-Output ('Aviso Add-AppxPackage: ' + $_.Exception.Message) } " +
                    "$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); " +
                    "if ($isAdmin -and (-not $appxOk -or (Test-Path $lic))) { " +
                    "  Write-Output 'Aprovisionando Winget a nivel de sistema (DISM / LTSC / Admin)...'; " +
                    "  $dismArgs = @('/Online', '/Add-ProvisionedAppxPackage', ('/PackagePath:' + $bundle)); " +
                    "  foreach ($d in $deps) { $dismArgs += ('/DependencyPackagePath:' + $d) } " +
                    "  if (Test-Path $lic) { $dismArgs += ('/LicensePath:' + $lic); $dismArgs += '/Region:all' } else { $dismArgs += '/SkipLicense' } " +
                    "  $p = Start-Process -FilePath 'dism.exe' -ArgumentList $dismArgs -Wait -NoNewWindow -PassThru; " +
                    "  Write-Output ('DISM finalizo con codigo: ' + $p.ExitCode); " +
                    "  try { Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction SilentlyContinue } catch {} " +
                    "}";

                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
                StringBuilder outBuf = new StringBuilder();
                await RunProcessAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, outBuf);

                InvalidateWingetCache();
                string installedVer = GetWingetVersion();
                if (!string.IsNullOrEmpty(installedVer))
                {
                    EmitLog("[OK] Winget instalado y verificado correctamente (" + installedVer + ").");
                    return true;
                }

                // Respaldo final online con Microsoft.WinGet.Client (Repair-WinGetPackageManager) si el sistema bloqueo MSIX directo
                if (online && !_cancelRequested)
                {
                    EmitLog("[*] Aplicando reparacion oficial Microsoft.WinGet.Client (Repair-WinGetPackageManager)...");
                    string repairScript =
                        "$ProgressPreference = 'SilentlyContinue'; " +
                        "Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -ErrorAction SilentlyContinue | Out-Null; " +
                        "Install-Module -Name Microsoft.WinGet.Client -Force -AllowClobber -Repository PSGallery -ErrorAction SilentlyContinue; " +
                        "Repair-WinGetPackageManager -AllUsers -Force -Latest -ErrorAction SilentlyContinue";
                    string encRepair = Convert.ToBase64String(Encoding.Unicode.GetBytes(repairScript));
                    StringBuilder repOut = new StringBuilder();
                    await RunProcessAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encRepair, repOut);

                    InvalidateWingetCache();
                    installedVer = GetWingetVersion();
                    if (!string.IsNullOrEmpty(installedVer))
                    {
                        EmitLog("[OK] Winget reparado y verificado correctamente (" + installedVer + ").");
                        return true;
                    }
                }

                EmitLog("[X] Windows completo el despliegue pero winget.exe aun no responde. Verifica que Windows 10 sea 1809+ y que el servicio AppXSvc este activo.");
                return false;
            }
            finally
            {
                try { if (Directory.Exists(localStage)) Directory.Delete(localStage, true); } catch { }
            }
        }

        public Task<SelfUpdateInfo> CheckSelfUpdateAsync()
        {
            return Task.Run(delegate
            {
                SelfUpdateInfo info = new SelfUpdateInfo();
                info.Available = false;
                info.CurrentVersion = CurrentAppVersion;
                info.LatestVersion = CurrentAppVersion;
                info.TagName = "v" + CurrentAppVersion;
                info.ReleaseUrl = "https://github.com/" + GithubRepoSlug + "/releases/latest";
                info.Notes = string.Empty;

                try
                {
                    string apiJson = FetchHttpStringQuick("https://api.github.com/repos/" + GithubRepoSlug + "/releases/latest", 8000);
                    if (!string.IsNullOrEmpty(apiJson))
                    {
                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        Dictionary<string, object> rel = ser.Deserialize<Dictionary<string, object>>(apiJson);
                        if (rel != null)
                        {
                            string tag = rel.ContainsKey("tag_name") && rel["tag_name"] != null ? rel["tag_name"].ToString().Trim() : string.Empty;
                            string htmlUrl = rel.ContainsKey("html_url") && rel["html_url"] != null ? rel["html_url"].ToString().Trim() : string.Empty;
                            string body = rel.ContainsKey("body") && rel["body"] != null ? rel["body"].ToString().Trim() : string.Empty;
                            string zipUrl = null;

                            if (rel.ContainsKey("assets"))
                            {
                                System.Collections.IEnumerable assets = rel["assets"] as System.Collections.IEnumerable;
                                if (assets != null && !(assets is string))
                                {
                                    foreach (object aObj in assets)
                                    {
                                        Dictionary<string, object> a = aObj as Dictionary<string, object>;
                                        if (a == null) continue;
                                        string aName = a.ContainsKey("name") && a["name"] != null ? a["name"].ToString() : string.Empty;
                                        string aUrl = a.ContainsKey("browser_download_url") && a["browser_download_url"] != null ? a["browser_download_url"].ToString() : string.Empty;
                                        if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(aUrl))
                                        {
                                            zipUrl = aUrl;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(tag))
                            {
                                string cleanLatest = NormalizeVersionDisplay(tag);
                                info.TagName = tag;
                                info.LatestVersion = cleanLatest;
                                if (!string.IsNullOrEmpty(htmlUrl)) info.ReleaseUrl = htmlUrl;
                                info.Notes = body;
                                info.DownloadUrl = !string.IsNullOrEmpty(zipUrl)
                                    ? zipUrl
                                    : string.Format("https://github.com/{0}/releases/download/{1}/qInstall-{1}-win-x64-portable.zip", GithubRepoSlug, tag);
                                info.Available = IsVersionOlder(CurrentAppVersion, cleanLatest);
                                return info;
                            }
                        }
                    }

                    // Respaldo sin limite de cuota API: resolver redireccion HTTP de /releases/latest -> /releases/tag/vX.Y.Z
                    string finalUrl = ResolveRedirectFinalUrl("https://github.com/" + GithubRepoSlug + "/releases/latest", 8000);
                    if (!string.IsNullOrEmpty(finalUrl))
                    {
                        int tagIdx = finalUrl.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
                        if (tagIdx >= 0)
                        {
                            string tag = finalUrl.Substring(tagIdx + 14).Trim().Trim('/');
                            if (!string.IsNullOrEmpty(tag))
                            {
                                string cleanLatest = NormalizeVersionDisplay(tag);
                                info.TagName = tag;
                                info.LatestVersion = cleanLatest;
                                info.ReleaseUrl = finalUrl;
                                info.DownloadUrl = string.Format("https://github.com/{0}/releases/download/{1}/qInstall-{1}-win-x64-portable.zip", GithubRepoSlug, tag);
                                info.Available = IsVersionOlder(CurrentAppVersion, cleanLatest);
                            }
                        }
                    }
                }
                catch
                {
                }

                return info;
            });
        }

        public async Task<bool> PerformSelfUpdateAsync(SelfUpdateInfo info, Action<int> onProgress)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                EmitLog("[X] No se encontro la URL del paquete de actualizacion de qInstall.");
                return false;
            }

            string stageRoot = Path.Combine(Path.GetTempPath(), "Qinstall_selfupd_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string zipPath = Path.Combine(stageRoot, "qInstall_update.zip");
            string extractDir = Path.Combine(stageRoot, "extracted");

            try
            {
                Directory.CreateDirectory(extractDir);
                EmitLog("[*] Descargando nueva version de qInstall (" + info.TagName + ") desde GitHub Releases...");
                ReportDownloadPct(5, onProgress);

                bool downloaded = await DownloadFileAsync(info.DownloadUrl, zipPath, delegate(int p)
                {
                    ReportDownloadPct(5 + (p * 75) / 100, onProgress);
                });

                if (!downloaded || !File.Exists(zipPath) || new FileInfo(zipPath).Length < 50 * 1024)
                {
                    EmitLog("[X] No se pudo descargar el paquete portable de qInstall desde GitHub Releases.");
                    return false;
                }

                ReportDownloadPct(85, onProgress);
                EmitLog("[*] Extrayendo y verificando archivos de qInstall " + info.TagName + "...");
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                string payloadRoot = extractDir;
                string nestedDir = Path.Combine(extractDir, "qInstall");
                if (Directory.Exists(nestedDir) && File.Exists(Path.Combine(nestedDir, "Qinstall.exe")))
                {
                    payloadRoot = nestedDir;
                }
                else if (!File.Exists(Path.Combine(payloadRoot, "Qinstall.exe")))
                {
                    string[] subDirs = Directory.GetDirectories(extractDir);
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        if (File.Exists(Path.Combine(subDirs[i], "Qinstall.exe")))
                        {
                            payloadRoot = subDirs[i];
                            break;
                        }
                    }
                }

                string newExePath = Path.Combine(payloadRoot, "Qinstall.exe");
                if (!File.Exists(newExePath))
                {
                    EmitLog("[X] El paquete descargado no contiene Qinstall.exe.");
                    return false;
                }

                // Fusion inteligente de apps.json: actualizar catalogo oficial conservando apps personalizadas del usuario
                string appBaseDir = AppCatalog.GetAppBaseDirectory();
                MergeUpdatedCatalogPreservingCustomApps(Path.Combine(payloadRoot, "apps.json"), Path.Combine(appBaseDir, "apps.json"));

                // Copiar ui\ y office\ nuevos sin tocar profiles.json ni local\
                string newUi = Path.Combine(payloadRoot, "ui");
                if (Directory.Exists(newUi))
                {
                    CopyUpdateDirectoryOverwrite(newUi, Path.Combine(appBaseDir, "ui"));
                }
                string newOffice = Path.Combine(payloadRoot, "office");
                if (Directory.Exists(newOffice))
                {
                    CopyUpdateDirectoryOverwrite(newOffice, Path.Combine(appBaseDir, "office"));
                }
                string[] dlls = new[] { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };
                for (int i = 0; i < dlls.Length; i++)
                {
                    string sDll = Path.Combine(payloadRoot, dlls[i]);
                    if (File.Exists(sDll))
                    {
                        try { File.Copy(sDll, Path.Combine(appBaseDir, dlls[i]), true); } catch { }
                    }
                }

                ReportDownloadPct(96, onProgress);

                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string targetExe = Path.Combine(appBaseDir, Path.GetFileName(currentExe));
                string oldBackupExe = targetExe + ".old";

                // Intentar hot-swap atomico en caliente (renombrar ejecutable actual a .old y copiar el nuevo)
                bool swappedInPlace = false;
                try
                {
                    TryDeleteFile(oldBackupExe);
                    File.Move(targetExe, oldBackupExe);
                    File.Copy(newExePath, targetExe, true);
                    swappedInPlace = true;
                }
                catch
                {
                    swappedInPlace = false;
                }

                ReportDownloadPct(100, onProgress);

                if (swappedInPlace)
                {
                    EmitLog("[OK] qInstall actualizado a " + info.TagName + ". Reiniciando aplicacion...");
                    try { Directory.Delete(stageRoot, true); } catch { }
                    ProcessStartInfo relaunchPsi = new ProcessStartInfo();
                    relaunchPsi.FileName = targetExe;
                    relaunchPsi.WorkingDirectory = appBaseDir;
                    relaunchPsi.UseShellExecute = true;
                    Process.Start(relaunchPsi);
                    return true;
                }

                // Respaldo para unidades FAT32 que bloquean renombrar un .exe en uso: script PowerShell que espera el cierre y relanza
                int myPid = Process.GetCurrentProcess().Id;
                string psSwap = string.Format(
                    "Wait-Process -Id {0} -Timeout 15 -ErrorAction SilentlyContinue; " +
                    "Start-Sleep -Milliseconds 400; " +
                    "Copy-Item -Path '{1}' -Destination '{2}' -Force; " +
                    "Remove-Item -Path '{3}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                    "Start-Process -FilePath '{2}' -WorkingDirectory '{4}'",
                    myPid,
                    newExePath.Replace("'", "''"),
                    targetExe.Replace("'", "''"),
                    stageRoot.Replace("'", "''"),
                    appBaseDir.Replace("'", "''"));
                string encSwap = Convert.ToBase64String(Encoding.Unicode.GetBytes(psSwap));
                ProcessStartInfo swapPsi = new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " + encSwap);
                swapPsi.UseShellExecute = false;
                swapPsi.CreateNoWindow = true;
                Process.Start(swapPsi);
                return true;
            }
            catch (Exception ex)
            {
                EmitLog("[X] Error aplicando auto-actualizacion de qInstall: " + ex.Message);
                try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true); } catch { }
                return false;
            }
        }

        private static void MergeUpdatedCatalogPreservingCustomApps(string newAppsJsonPath, string localAppsJsonPath)
        {
            try
            {
                if (!File.Exists(newAppsJsonPath)) return;
                JavaScriptSerializer ser = new JavaScriptSerializer();
                AppCatalog incoming = ser.Deserialize<AppCatalog>(File.ReadAllText(newAppsJsonPath, Encoding.UTF8));
                if (incoming == null || incoming.Apps == null || incoming.Apps.Count == 0) return;

                if (File.Exists(localAppsJsonPath))
                {
                    try
                    {
                        AppCatalog existing = ser.Deserialize<AppCatalog>(File.ReadAllText(localAppsJsonPath, Encoding.UTF8));
                        if (existing != null && existing.Apps != null)
                        {
                            HashSet<string> officialIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < incoming.Apps.Count; i++)
                            {
                                if (!string.IsNullOrEmpty(incoming.Apps[i].Id)) officialIds.Add(incoming.Apps[i].Id);
                            }
                            for (int i = 0; i < existing.Apps.Count; i++)
                            {
                                AppItem userApp = existing.Apps[i];
                                if (userApp != null && !string.IsNullOrEmpty(userApp.Id) && !officialIds.Contains(userApp.Id))
                                {
                                    incoming.Apps.Add(userApp);
                                }
                            }
                        }
                    }
                    catch { }
                }

                AppCatalog.Save(incoming, localAppsJsonPath);
            }
            catch { }
        }

        private static void CopyUpdateDirectoryOverwrite(string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            string[] files = Directory.GetFiles(srcDir);
            for (int i = 0; i < files.Length; i++)
            {
                string destFile = Path.Combine(dstDir, Path.GetFileName(files[i]));
                try { File.Copy(files[i], destFile, true); } catch { }
            }
            string[] subDirs = Directory.GetDirectories(srcDir);
            for (int i = 0; i < subDirs.Length; i++)
            {
                CopyUpdateDirectoryOverwrite(subDirs[i], Path.Combine(dstDir, Path.GetFileName(subDirs[i])));
            }
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
            if (id == "nzxt_cam") return AnyNameStartsWith(installedNames, "NZXT CAM");
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
            if (id == "hwinfo") return AnyNameStartsWith(installedNames, "HWiNFO");
            if (id == "gpuz") return AnyNameStartsWith(installedNames, "TechPowerUp GPU-Z") || AnyNameEqualsOrPrefix(installedNames, "GPU-Z");
            if (id == "ddu") return AnyNameStartsWith(installedNames, "Display Driver Uninstaller");
            if (id == "nvcleanstall") return AnyNameStartsWith(installedNames, "NVCleanstall");
            if (id == "nvidia_app") return AnyNameStartsWith(installedNames, "NVIDIA App") || AnyNameStartsWith(installedNames, "NVIDIA GeForce Experience");
            if (id == "nvidia_physx") return AnyNameStartsWith(installedNames, "NVIDIA PhysX");
            if (id == "sdio") return AnyNameStartsWith(installedNames, "Snappy Driver Installer");
            if (id == "msi_afterburner") return AnyNameStartsWith(installedNames, "MSI Afterburner");
            if (id == "intel_dsa") return AnyNameStartsWith(installedNames, "Intel") && AnyNameMatchesAll(installedNames, "Intel", "Driver", "Support");
            if (id == "malwarebytes") return AnyNameStartsWith(installedNames, "Malwarebytes");
            if (id == "anydesk") return AnyNameEqualsOrPrefix(installedNames, "AnyDesk");
            if (id == "teamviewer") return AnyNameEqualsOrPrefix(installedNames, "TeamViewer");
            if (id == "parsec") return AnyNameEqualsOrPrefix(installedNames, "Parsec");
            if (id == "bitwarden") return AnyNameEqualsOrPrefix(installedNames, "Bitwarden");
            if (id == "keepassxc") return AnyNameStartsWith(installedNames, "KeePassXC");
            if (src == "powershell") return false;
            if (id == "vlc") return AnyNameStartsWith(installedNames, "VLC media player");
            if (id == "spotify") return AnyNameEqualsOrPrefix(installedNames, "Spotify") || AnyIdContains(installedIds, "SpotifyAB.SpotifyMusic");
            if (id == "obs") return AnyNameStartsWith(installedNames, "OBS Studio");
            if (id == "sharex") return AnyNameEqualsOrPrefix(installedNames, "ShareX");
            if (id == "gimp") return AnyNameEqualsOrPrefix(installedNames, "GIMP");
            if (id == "paintnet") return AnyNameEqualsOrPrefix(installedNames, "paint.net");
            if (id == "handbrake") return AnyNameStartsWith(installedNames, "HandBrake");
            if (id == "kdenlive") return AnyNameStartsWith(installedNames, "Kdenlive");
            if (id == "shotcut") return AnyNameEqualsOrPrefix(installedNames, "Shotcut");
            if (id == "capcut") return AnyNameEqualsOrPrefix(installedNames, "CapCut");
            if (id == "adobe_cc") return AnyNameStartsWith(installedNames, "Adobe Creative Cloud");
            if (id == "inkscape") return AnyNameEqualsOrPrefix(installedNames, "Inkscape");
            if (id == "krita") return AnyNameEqualsOrPrefix(installedNames, "Krita");
            if (id == "blender") return AnyNameEqualsOrPrefix(installedNames, "Blender");
            if (id == "darktable") return AnyNameEqualsOrPrefix(installedNames, "darktable");
            if (id == "scribus") return AnyNameStartsWith(installedNames, "Scribus");
            if (id == "discord") return AnyNameEqualsOrPrefix(installedNames, "Discord") || AnyIdContains(installedIds, "XPDC2RH70K22MN");
            if (id == "whatsapp") return AnyNameEqualsOrPrefix(installedNames, "WhatsApp") || AnyIdContains(installedIds, "5319275A.WhatsAppDesktop") || AnyIdContains(installedIds, "9NKSQGP7F2NH");
            if (id == "telegram") return AnyNameStartsWith(installedNames, "Telegram Desktop");
            if (id == "zoom") return AnyNameEqualsOrPrefix(installedNames, "Zoom");
            if (id == "libreoffice") return AnyNameStartsWith(installedNames, "LibreOffice");
            if (id == "sumatrapdf") return AnyNameStartsWith(installedNames, "SumatraPDF");
            if (id == "adobereader") return AnyNameStartsWith(installedNames, "Adobe Acrobat") || AnyNameStartsWith(installedNames, "Adobe Reader");
            if (id == "obsidian") return AnyNameEqualsOrPrefix(installedNames, "Obsidian");
            if (id == "google_drive") return AnyNameStartsWith(installedNames, "Google Drive");
            if (id == "pdfgear") return AnyNameEqualsOrPrefix(installedNames, "PDFgear");
            if (id == "chatgpt") return AnyNameEqualsOrPrefix(installedNames, "ChatGPT");
            if (id == "grammarly") return AnyNameStartsWith(installedNames, "Grammarly");
            if (id == "claude") return HasBaseAppExcluding(installedNames, "Claude", "Claude Code", "Claude Science");
            if (id == "claude_code") return AnyNameStartsWith(installedNames, "Claude Code");
            if (id == "cursor") return AnyNameEqualsOrPrefix(installedNames, "Cursor");
            if (id == "windsurf") return AnyNameEqualsOrPrefix(installedNames, "Windsurf") || AnyNameEqualsOrPrefix(installedNames, "Devin");
            if (id == "trae") return AnyNameEqualsOrPrefix(installedNames, "Trae");
            if (id == "perplexity_comet") return AnyNameEqualsOrPrefix(installedNames, "Comet") || AnyNameStartsWith(installedNames, "Perplexity");
            if (id == "ollama") return AnyNameEqualsOrPrefix(installedNames, "Ollama");
            if (id == "lmstudio") return AnyNameStartsWith(installedNames, "LM Studio");
            if (id == "jan_ai") return AnyNameEqualsOrPrefix(installedNames, "Jan");
            if (id == "cherry_studio") return AnyNameStartsWith(installedNames, "Cherry Studio");
            if (id == "comfyui") return AnyNameStartsWith(installedNames, "ComfyUI");
            if (id == "gemini") return HasBaseAppExcluding(installedNames, "Google Gemini", "Google Gemini CLI", "Gemini CLI") || HasBaseAppExcluding(installedNames, "Gemini", "Gemini CLI", "Gemini API CLI");
            if (id == "antigravity") return HasBaseAppExcluding(installedNames, "Antigravity", "Antigravity IDE", "Antigravity CLI") || HasBaseAppExcluding(installedNames, "Google Antigravity", "Antigravity IDE", "Antigravity CLI");
            if (id == "antigravity_ide") return AnyNameStartsWith(installedNames, "Antigravity IDE");
            if (id == "antigravity_cli") return AnyNameStartsWith(installedNames, "Antigravity CLI");
            if (id == "opencode") return HasBaseAppExcluding(installedNames, "OpenCode", "OpenCode CLI", "OpenCode CLI");
            if (id == "opencode_cli") return AnyNameStartsWith(installedNames, "OpenCode CLI");
            if (id == "copilot_app") return HasBaseAppExcluding(installedNames, "GitHub Copilot", "GitHub Copilot CLI", "GitHub Copilot CLI");
            if (id == "copilot_cli") return AnyNameStartsWith(installedNames, "GitHub Copilot CLI");
            if (id == "jetbrains_toolbox") return AnyNameStartsWith(installedNames, "JetBrains Toolbox");
            if (id == "intellij") return AnyNameMatchesWithout(installedNames, "IntelliJ IDEA", "Community");
            if (id == "intellij_community") return AnyNameMatchesAll(installedNames, "IntelliJ IDEA", "Community", "");
            if (id == "pycharm") return AnyNameMatchesWithout(installedNames, "PyCharm", "Community");
            if (id == "pycharm_community") return AnyNameMatchesAll(installedNames, "PyCharm", "Community", "");
            if (id == "webstorm") return AnyNameStartsWith(installedNames, "WebStorm");
            if (id == "clion") return AnyNameStartsWith(installedNames, "CLion");
            if (id == "rider") return AnyNameStartsWith(installedNames, "JetBrains Rider") || AnyNameEqualsOrPrefix(installedNames, "Rider");
            if (id == "phpstorm") return AnyNameStartsWith(installedNames, "PhpStorm");
            if (id == "datagrip") return AnyNameStartsWith(installedNames, "DataGrip");
            if (id == "goland") return AnyNameStartsWith(installedNames, "GoLand");
            if (id == "rustrover") return AnyNameStartsWith(installedNames, "RustRover");
            if (id == "vs2022_community") return AnyNameStartsWith(installedNames, "Visual Studio Community 2022");
            if (id == "android_studio") return AnyNameStartsWith(installedNames, "Android Studio");
            if (id == "eclipse_jee") return AnyNameMatchesAll(installedNames, "Eclipse", "Enterprise", "Java") || AnyNameMatchesAll(installedNames, "Eclipse", "JEE", "");
            if (id == "eclipse_cpp") return AnyNameMatchesAll(installedNames, "Eclipse", "C/C++", "");
            if (id == "netbeans") return AnyNameStartsWith(installedNames, "Apache NetBeans");
            if (id == "vscodium") return AnyNameEqualsOrPrefix(installedNames, "VSCodium");
            if (id == "sublime_text") return AnyNameStartsWith(installedNames, "Sublime Text");
            if (id == "zed") return AnyNameEqualsOrPrefix(installedNames, "Zed");
            if (id == "neovim") return AnyNameEqualsOrPrefix(installedNames, "Neovim");
            if (id == "devcpp_592") return AnyNameMatchesAll(installedNames, "Dev-C++", "5.9", "");
            if (id == "devcpp") return AnyNameStartsWith(installedNames, "Embarcadero Dev-C++") || AnyNameMatchesAll(installedNames, "Dev-C++", "6.", "");
            if (id == "codeblocks") return AnyNameStartsWith(installedNames, "Code::Blocks") || AnyNameStartsWith(installedNames, "CodeBlocks");
            if (id == "arduino_ide") return AnyNameStartsWith(installedNames, "Arduino IDE");
            if (id == "godot") return AnyNameStartsWith(installedNames, "Godot Engine");
            if (id == "unity_hub") return AnyNameStartsWith(installedNames, "Unity Hub");
            if (id == "vscode") return AnyNameStartsWith(installedNames, "Microsoft Visual Studio Code") || AnyNameStartsWith(installedNames, "Visual Studio Code");
            if (id == "github_desktop") return AnyNameStartsWith(installedNames, "GitHub Desktop");
            if (id == "git") return AnyNameEqualsOrPrefix(installedNames, "Git");
            if (id == "notepadpp") return AnyNameStartsWith(installedNames, "Notepad++");
            if (id == "windows_terminal") return AnyNameStartsWith(installedNames, "Windows Terminal") || AnyIdContains(installedIds, "Microsoft.WindowsTerminal");
            if (id == "python") return AnyNameStartsWith(installedNames, "Python 3.");
            if (id == "nodejs") return AnyNameStartsWith(installedNames, "Node.js");
            if (id == "postman") return AnyNameEqualsOrPrefix(installedNames, "Postman");
            if (id == "dbeaver") return AnyNameStartsWith(installedNames, "DBeaver");
            if (id == "docker_desktop") return AnyNameStartsWith(installedNames, "Docker Desktop");
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

        private static bool HasBaseAppExcluding(HashSet<string> names, string baseName, string exclude1, string exclude2)
        {
            string withSpace = baseName + " ";
            string withDash = baseName + "-";
            foreach (string n in names)
            {
                if (!string.IsNullOrEmpty(exclude1) && n.StartsWith(exclude1, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(exclude2) && n.StartsWith(exclude2, StringComparison.OrdinalIgnoreCase)) continue;
                if (n.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(withSpace, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(withDash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
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

        private static bool AnyNameMatchesWithout(HashSet<string> names, string requiredPrefix, string forbiddenSubstring)
        {
            foreach (string n in names)
            {
                if (n.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) &&
                    n.IndexOf(forbiddenSubstring, StringComparison.OrdinalIgnoreCase) < 0)
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
                    ConfigureWingetProcessStartInfo(psi);

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
                if (upgradeIfExists && exitCode != -1978335189)
                {
                    EmitLog("[*] Ya estaba instalado. Comprobando actualizaciones para " + app.Name + "...");
                    return await UpgradeSingleAppAsync(app, silent);
                }

                EmitLog("[=] " + app.Name + " ya esta instalado y al dia.");
                return MakeResult(app, InstallStatus.AlreadyInstalled, "Ya instalado y actualizado");
            }

            if (exitCode == -2145844844 && pkgSource == "winget" && !_cancelRequested)
            {
                EmitLog("[!] La ultima version de " + app.Name + " devolvio HTTP 404; intentando version anterior estable...");
                List<string> prevVersions = await GetWingetPreviousVersionsAsync(app.Target.Trim(), 2);
                for (int pv = 0; pv < prevVersions.Count && !_cancelRequested; pv++)
                {
                    string fallbackVer = prevVersions[pv];
                    string prevArgs = string.Format(
                        "install --id \"{0}\" --exact --version \"{1}\" --source winget{2} --accept-package-agreements --accept-source-agreements --disable-interactivity",
                        app.Target.Trim(),
                        fallbackVer,
                        silentFlag);
                    StringBuilder prevOut = new StringBuilder();
                    int prevCode = await RunProcessAsync("winget", prevArgs, prevOut);
                    if (_cancelRequested) return MakeResult(app, InstallStatus.Cancelled, "Cancelado por el usuario");
                    if (prevCode == 0)
                    {
                        EmitLog("[OK] " + app.Name + " v" + fallbackVer + " instalado correctamente.");
                        return MakeResult(app, InstallStatus.Success, "Instalado correctamente (v" + fallbackVer + ")");
                    }
                }
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
            else if (ext == ".msix" || ext == ".msixbundle" || ext == ".appx" || ext == ".appxbundle")
            {
                fileToRun = "powershell.exe";
                string ps = string.Format("Add-AppxPackage -Path '{0}'", filePath.Replace("'", "''"));
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                args = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
            }
            else if (ext == ".zip")
            {
                string safeId = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qinstall", "Tools", safeId);
                string msiFlags = silent ? "/qn /norestart" : "/qb /norestart";
                fileToRun = "powershell.exe";
                string ps = string.Format(
                    "$dst = '{0}'; New-Item -ItemType Directory -Path $dst -Force | Out-Null; Expand-Archive -Path '{1}' -DestinationPath $dst -Force; $msi = Get-ChildItem -Path $dst -Filter '*.msi' -File | Select-Object -First 1; if ($msi) {{ $p = Start-Process msiexec.exe -ArgumentList ('/i \"' + $msi.FullName + '\" {2}') -Wait -PassThru; exit $p.ExitCode }} else {{ $p = [Environment]::GetEnvironmentVariable('Path','User'); if ($p -notlike ('*' + $dst + '*')) {{ [Environment]::SetEnvironmentVariable('Path', ($p.TrimEnd(';') + ';' + $dst), 'User') }} }}",
                    installDir.Replace("'", "''"),
                    filePath.Replace("'", "''"),
                    msiFlags);
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));
                args = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
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

        private static async Task<List<string>> GetWingetPreviousVersionsAsync(string packageId, int maxFallback)
        {
            List<string> result = new List<string>();
            try
            {
                string safeId = (packageId ?? string.Empty).Replace("\"", string.Empty).Trim();
                if (string.IsNullOrEmpty(safeId)) return result;

                string args = string.Format(
                    "show --id \"{0}\" --exact --source winget --versions --accept-source-agreements --disable-interactivity",
                    safeId);

                for (int attempt = 0; attempt < 2 && result.Count == 0; attempt++)
                {
                    if (attempt > 0) await Task.Delay(800);
                    string output = await RunQuickQueryAsync("winget", args, 10000);
                    if (string.IsNullOrWhiteSpace(output)) continue;

                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    bool afterRule = false;
                    bool skippedLatest = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = CleanWingetLine(lines[i]).Trim();
                        if (string.IsNullOrEmpty(t)) continue;
                        if (!afterRule)
                        {
                            if (t.IndexOf("---", StringComparison.Ordinal) >= 0) afterRule = true;
                            continue;
                        }
                        string verToken = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        if (string.IsNullOrEmpty(verToken) || !char.IsDigit(verToken[0])) continue;
                        if (!skippedLatest)
                        {
                            skippedLatest = true;
                            continue;
                        }
                        result.Add(verToken);
                        if (result.Count >= maxFallback) break;
                    }
                }
            }
            catch { }
            return result;
        }

        private async Task<string> TryFastDirectWingetDownloadAsync(AppItem app, string cacheDir, Action<int> onProgress, string specificVersion = null)
        {
            try
            {
                string safeId = (app.Target ?? string.Empty).Replace("\"", string.Empty).Trim();
                if (string.IsNullOrEmpty(safeId)) return null;

                string verFlag = !string.IsNullOrWhiteSpace(specificVersion)
                    ? (" --version \"" + specificVersion.Replace("\"", string.Empty).Trim() + "\"")
                    : string.Empty;
                string args = string.Format(
                    "show --id \"{0}\" --exact{1} --source winget --accept-source-agreements --disable-interactivity",
                    safeId,
                    verFlag);
                string info = await RunQuickQueryAsync("winget", args, 8000);
                if (string.IsNullOrEmpty(info) || _cancelRequested) return null;

                string version = string.Empty;
                string installerUrl = string.Empty;
                string installerType = string.Empty;
                string expectedSha256 = string.Empty;

                string[] lines = info.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    string key = line.Substring(0, colon).Trim();
                    string val = line.Substring(colon + 1).Trim();

                    if (string.IsNullOrEmpty(version) &&
                        (key.Equals("Version", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Versi", StringComparison.OrdinalIgnoreCase)))
                    {
                        version = val;
                    }
                    else if (key.Equals("Installer Url", StringComparison.OrdinalIgnoreCase) ||
                             key.IndexOf("URL del instalador", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        installerUrl = val;
                    }
                    else if (key.Equals("Installer Type", StringComparison.OrdinalIgnoreCase) ||
                             key.IndexOf("Tipo de instalador", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        installerType = val.ToLowerInvariant();
                    }
                    else if (key.Equals("Installer SHA256", StringComparison.OrdinalIgnoreCase) ||
                             key.IndexOf("SHA256", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        expectedSha256 = val.Trim().ToLowerInvariant();
                    }
                }

                if (string.IsNullOrWhiteSpace(installerUrl) ||
                    (!installerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                     !installerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                // Permitir via directa para .exe, .msi, .msix/.appx y .zip portables
                string ext = string.Empty;
                try
                {
                    Uri uri = new Uri(installerUrl);
                    ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                }
                catch { return null; }

                bool allowedExt =
                    ext == ".exe" || ext == ".msi" ||
                    ext == ".msix" || ext == ".msixbundle" ||
                    ext == ".appx" || ext == ".appxbundle" ||
                    ext == ".zip";

                if (!allowedExt)
                {
                    if (installerType == "msi" || installerType == "wix") ext = ".msi";
                    else if (installerType == "exe" || installerType == "nullsoft" || installerType == "inno" || installerType == "burn") ext = ".exe";
                    else if (installerType == "msix") ext = ".msix";
                    else if (installerType == "appx") ext = ".appx";
                    else if (installerType == "zip") ext = ".zip";
                    else return null;
                }

                string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id) + ext;
                string destFile = Path.Combine(cacheDir, safeName);

                EmitLog("[*] Descarga turbo (" + app.Name + "): " + installerUrl);
                bool ok = await DownloadFileAsync(installerUrl, destFile, onProgress);
                if (!ok || _cancelRequested || !File.Exists(destFile))
                {
                    TryDeleteFile(destFile);
                    return null;
                }

                if (!string.IsNullOrEmpty(expectedSha256) && expectedSha256.Length == 64)
                {
                    string actualHash = ComputeFileSha256(destFile);
                    if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        EmitLog("[!] Hash SHA256 no coincide en descarga directa (" + app.Name + "); usando respaldo winget download...");
                        TryDeleteFile(destFile);
                        return null;
                    }
                }

                // Guardar metadatos compatibles con deteccion de versiones obsoletas y parametros silenciosos
                try
                {
                    string inferredSilent = string.Empty;
                    if (installerType == "nullsoft") inferredSilent = "/S";
                    else if (installerType == "inno") inferredSilent = "/VERYSILENT /NORESTART";
                    else if (installerType == "msi" || installerType == "wix") inferredSilent = "/qn /norestart";
                    else if (installerType == "burn") inferredSilent = "/quiet /norestart";

                    StringBuilder yaml = new StringBuilder();
                    if (!string.IsNullOrEmpty(version)) yaml.AppendLine("PackageVersion: " + version);
                    if (!string.IsNullOrEmpty(inferredSilent)) yaml.AppendLine("Silent: " + inferredSilent);
                    File.WriteAllText(Path.Combine(cacheDir, "qinstall_meta.yaml"), yaml.ToString(), new UTF8Encoding(false));
                }
                catch { }

                return destFile;
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeFileSha256(string filePath)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RegisterRequest(HttpWebRequest req)
        {
            if (req == null) return;
            lock (_lock) { _activeRequests.Add(req); }
        }

        private void UnregisterRequest(HttpWebRequest req)
        {
            if (req == null) return;
            lock (_lock) { _activeRequests.Remove(req); }
        }

        private void ReportDownloadPct(int pct, Action<int> onProgress)
        {
            if (onProgress != null)
            {
                onProgress(pct);
                return;
            }
            Action<int> handler = DownloadProgress;
            if (handler != null) handler(pct);
        }

        private Task<bool> DownloadFileAsync(string url, string destinationPath)
        {
            return DownloadFileAsync(url, destinationPath, null);
        }

        private Task<bool> DownloadFileAsync(string url, string destinationPath, Action<int> onProgress)
        {
            return Task.Run(delegate
            {
                string stageDir = Path.Combine(Path.GetTempPath(), "Qinstall_dl");
                string stageFile = Path.Combine(stageDir, Guid.NewGuid().ToString("N") + ".part");
                try
                {
                    Directory.CreateDirectory(stageDir);

                    // 1. Sondar soporte de descarga segmentada (Accept-Ranges + Content-Length)
                    long totalLength = -1;
                    bool supportsRanges = false;
                    string resolvedUrl = url;
                    HttpWebRequest headReq = null;
                    try
                    {
                        headReq = (HttpWebRequest)WebRequest.Create(url);
                        headReq.Method = "HEAD";
                        headReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Qinstall/1.0";
                        headReq.AllowAutoRedirect = true;
                        headReq.AutomaticDecompression = DecompressionMethods.None;
                        headReq.Timeout = 8000;
                        RegisterRequest(headReq);
                        using (HttpWebResponse headResp = (HttpWebResponse)headReq.GetResponse())
                        {
                            totalLength = headResp.ContentLength;
                            resolvedUrl = headResp.ResponseUri != null ? headResp.ResponseUri.AbsoluteUri : url;
                            string ar = headResp.Headers["Accept-Ranges"] ?? string.Empty;
                            supportsRanges = ar.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0 && totalLength >= (8L * 1024L * 1024L);
                        }
                    }
                    catch
                    {
                        supportsRanges = false;
                    }
                    finally
                    {
                        UnregisterRequest(headReq);
                    }

                    if (_cancelRequested) return false;

                    bool ok = false;
                    if (supportsRanges)
                    {
                        ok = DownloadSegmented(resolvedUrl, stageFile, totalLength, 4, onProgress);
                        if (!ok && !_cancelRequested)
                        {
                            // Si el servidor ignoro Range (200 OK) o fallo un segmento, reintentar limpio en 1 flujo directo
                            TryDeleteFile(stageFile);
                            ok = DownloadSingleStream(resolvedUrl, stageFile, totalLength, onProgress);
                        }
                    }
                    else
                    {
                        ok = DownloadSingleStream(resolvedUrl, stageFile, totalLength, onProgress);
                    }

                    if (!ok || _cancelRequested || !File.Exists(stageFile))
                    {
                        TryDeleteFile(stageFile);
                        return false;
                    }

                    string destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(stageFile, destinationPath, true);
                    TryDeleteFile(stageFile);
                    ReportDownloadPct(100, onProgress);
                    return true;
                }
                catch (Exception ex)
                {
                    TryDeleteFile(stageFile);
                    if (!_cancelRequested) EmitLog("[X] Error de descarga: " + ex.Message);
                    return false;
                }
            });
        }

        private bool DownloadSegmented(string url, string stageFile, long totalLength, int parts, Action<int> onProgress)
        {
            List<HttpWebRequest> localReqs = new List<HttpWebRequest>();
            object localLock = new object();
            int localAborted = 0;
            try
            {
                using (FileStream fs = new FileStream(stageFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.SetLength(totalLength);
                }

                long downloaded = 0;
                int lastPct = -1;
                object pctLock = new object();
                long chunkSize = totalLength / parts;
                Task[] workers = new Task[parts];

                Action abortSiblings = delegate
                {
                    Interlocked.Exchange(ref localAborted, 1);
                    lock (localLock)
                    {
                        for (int k = 0; k < localReqs.Count; k++)
                        {
                            try { localReqs[k].Abort(); } catch { }
                        }
                    }
                };

                for (int p = 0; p < parts; p++)
                {
                    long start = p * chunkSize;
                    long end = (p == parts - 1) ? (totalLength - 1) : (start + chunkSize - 1);
                    long expectedLen = (end - start) + 1;
                    workers[p] = Task.Run(delegate
                    {
                        HttpWebRequest req = null;
                        try
                        {
                            if (_cancelRequested || Thread.VolatileRead(ref localAborted) != 0) return;
                            req = (HttpWebRequest)WebRequest.Create(url);
                            req.Method = "GET";
                            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Qinstall/1.0";
                            req.AllowAutoRedirect = true;
                            req.AutomaticDecompression = DecompressionMethods.None;
                            req.Timeout = 20000;
                            req.ReadWriteTimeout = 45000;
                            req.AddRange(start, end);

                            RegisterRequest(req);
                            lock (localLock) { localReqs.Add(req); }

                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                            {
                                // CRITICO: Si el servidor devolvio 200 OK en lugar de 206 Partial Content, abortar y usar 1 flujo
                                if (resp.StatusCode != HttpStatusCode.PartialContent)
                                {
                                    abortSiblings();
                                    return;
                                }

                                using (Stream netStream = resp.GetResponseStream())
                                using (FileStream fs = new FileStream(stageFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 262144))
                                {
                                    fs.Seek(start, SeekOrigin.Begin);
                                    byte[] buffer = new byte[262144];
                                    long chunkRead = 0;
                                    int read;
                                    while (!_cancelRequested && Thread.VolatileRead(ref localAborted) == 0 && chunkRead < expectedLen &&
                                           (read = netStream.Read(buffer, 0, (int)Math.Min(buffer.Length, expectedLen - chunkRead))) > 0)
                                    {
                                        fs.Write(buffer, 0, read);
                                        chunkRead += read;
                                        long nowTotal = Interlocked.Add(ref downloaded, read);
                                        int pct = (int)Math.Min(99, (nowTotal * 100L) / totalLength);
                                        bool shouldReport = false;
                                        lock (pctLock)
                                        {
                                            if (pct > lastPct)
                                            {
                                                lastPct = pct;
                                                shouldReport = true;
                                            }
                                        }
                                        if (shouldReport)
                                        {
                                            ReportDownloadPct(pct, onProgress);
                                        }
                                    }
                                    if (chunkRead != expectedLen && !_cancelRequested)
                                    {
                                        abortSiblings();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            if (!_cancelRequested) abortSiblings();
                        }
                        finally
                        {
                            UnregisterRequest(req);
                        }
                    });
                }

                Task.WaitAll(workers);
                if (Thread.VolatileRead(ref localAborted) != 0 || _cancelRequested) return false;
                return downloaded == totalLength;
            }
            catch
            {
                return false;
            }
        }

        private bool DownloadSingleStream(string url, string stageFile, long knownLength, Action<int> onProgress)
        {
            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Qinstall/1.0";
                req.AllowAutoRedirect = true;
                req.AutomaticDecompression = DecompressionMethods.None;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 45000;
                RegisterRequest(req);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream netStream = resp.GetResponseStream())
                using (FileStream fs = new FileStream(stageFile, FileMode.Create, FileAccess.Write, FileShare.None, 262144))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : knownLength;
                    long downloaded = 0;
                    int lastPct = -1;
                    byte[] buffer = new byte[262144];
                    int read;
                    while (!_cancelRequested && (read = netStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fs.Write(buffer, 0, read);
                        downloaded += read;
                        if (total > 0)
                        {
                            int pct = (int)Math.Min(99, (downloaded * 100L) / total);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                ReportDownloadPct(pct, onProgress);
                            }
                        }
                    }
                    if (!_cancelRequested && total > 0 && downloaded < total)
                    {
                        return false;
                    }
                }
                return !_cancelRequested;
            }
            catch
            {
                return false;
            }
            finally
            {
                UnregisterRequest(req);
            }
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
                    ConfigureWingetProcessStartInfo(psi);

                    object bufLock = new object();
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
                                    lock (bufLock) { outputBuffer.AppendLine(cleaned); }
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
                                    lock (bufLock) { outputBuffer.AppendLine(cleaned); }
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
                            _activeProcesses.Add(proc);
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
                            _activeProcesses.Remove(proc);
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
                    _activeProcesses.Add(proc);
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
                    _activeProcesses.Remove(proc);
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

    public class InstalledUpdateInfo
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string CurrentVersion { get; set; }
        public string AvailableVersion { get; set; }
    }
}
