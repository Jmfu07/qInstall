using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Qinstall
{
    public class WebViewForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private readonly InstallerEngine _engine;
        private readonly JavaScriptSerializer _json;
        private WebView2 _webView;
        private AppCatalog _catalog;
        private readonly List<AppItem> _autoDiscoveredLocals = new List<AppItem>();
        private readonly HashSet<string> _installedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OfflineCacheUpdateInfo> _outdatedCaches = new Dictionary<string, OfflineCacheUpdateInfo>(StringComparer.OrdinalIgnoreCase);
        private bool _hasInternet = true;
        private bool _wingetAvailable;
        private string _wingetVersion = string.Empty;
        private bool _isBusy;
        private int _searchSeq;

        public WebViewForm()
        {
            _engine = new InstallerEngine();
            _engine.LogMessage += OnEngineLog;
            _engine.DownloadProgress += OnDownloadProgress;
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 16 * 1024 * 1024;

            this.Text = "qInstall";
            this.Size = new Size(1280, 820);
            this.MinimumSize = new Size(860, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(9, 11, 16);
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _webView = new WebView2();
            _webView.Dock = DockStyle.Fill;
            _webView.DefaultBackgroundColor = Color.FromArgb(9, 11, 16);
            this.Controls.Add(_webView);

            this.Load += WebViewForm_Load;
            this.FormClosing += WebViewForm_FormClosing;
        }

        private void ApplyNativeTitlebarTheme(bool dark)
        {
            try
            {
                int val = dark ? 1 : 0;
                if (DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref val, sizeof(int));
                }
                this.BackColor = dark ? Color.FromArgb(9, 11, 16) : Color.FromArgb(216, 220, 230);
                if (_webView != null)
                {
                    _webView.DefaultBackgroundColor = this.BackColor;
                }
            }
            catch
            {
            }
        }

        private void WebViewForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isBusy)
            {
                _engine.CancelCurrent();
            }
        }

        private async void WebViewForm_Load(object sender, EventArgs e)
        {
            ApplyNativeTitlebarTheme(true);
            try
            {
                string userDataDir = Path.Combine(Path.GetTempPath(), "QinstallWebView2");
                Directory.CreateDirectory(userDataDir);

                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(
                    "--disable-extensions --disable-background-networking --disable-sync --disable-default-apps --disable-component-update");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataDir, options);
                await _webView.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;

                string uiDir = Path.Combine(AppCatalog.GetAppBaseDirectory(), "ui");
                if (!Directory.Exists(uiDir))
                {
                    throw new DirectoryNotFoundException("No se encontro la carpeta ui\\ junto a Qinstall.exe.");
                }

                core.SetVirtualHostNameToFolderMapping(
                    "qinstall.local",
                    uiDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += Core_WebMessageReceived;
                core.Navigate("https://qinstall.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "No se pudo inicializar WebView2 (" + ex.Message + "). Se abrira la interfaz clasica de respaldo.",
                    "qInstall",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                MainForm fallback = new MainForm();
                fallback.FormClosed += delegate { this.Close(); };
                this.Hide();
                fallback.Show();
            }
        }

        private async void Core_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;
                Dictionary<string, object> msg = _json.Deserialize<Dictionary<string, object>>(raw);
                if (msg == null) return;
                string action = GetStr(msg, "action");

                switch (action)
                {
                    case "init":
                        await HandleInitAsync();
                        break;
                    case "refresh":
                        await HandleRefreshAsync();
                        break;
                    case "searchWinget":
                        HandleSearchWingetAsync(GetStr(msg, "query"), GetInt(msg, "seq"));
                        break;
                    case "quickSaveOnline":
                        HandleQuickSaveOnline(msg);
                        break;
                    case "removeApp":
                        HandleRemoveApp(GetStr(msg, "key"));
                        break;
                    case "addLocalInstaller":
                        HandleAddLocalInstaller();
                        break;
                    case "installSelected":
                        await HandleInstallSelectedAsync(msg);
                        break;
                    case "downloadOffline":
                        await HandleDownloadOfflineAsync(msg);
                        break;
                    case "replaceOutdated":
                        await HandleReplaceOutdatedAsync(msg);
                        break;
                    case "cancel":
                        _engine.CancelCurrent();
                        break;
                    case "installWinget":
                        await HandleInstallWingetAsync();
                        break;
                    case "runAsAdmin":
                        if (InstallerEngine.RelaunchAsAdministrator())
                        {
                            Application.Exit();
                        }
                        break;
                    case "openLocalFolder":
                        OpenLocalFolder();
                        break;
                    case "purgeCache":
                        HandlePurgeCache(GetBool(msg, "includeOfflineCache"));
                        break;
                    case "purgeOutdated":
                        HandlePurgeOutdated(msg);
                        break;
                    case "setWindowTheme":
                        ApplyNativeTitlebarTheme(GetBool(msg, "dark"));
                        break;
                }
            }
            catch (Exception ex)
            {
                PostLog("[X] Error interno UI: " + ex.Message, "warn");
            }
        }

        private async Task HandleInitAsync()
        {
            PostMessage(new Dictionary<string, object>
            {
                { "type", "loaderStep" },
                { "pct", 20 },
                { "dot", 0 },
                { "text", "Verificando red y Winget runtime..." }
            });

            Task<bool> netTask = InstallerEngine.CheckInternetAsync();
            Task<string> wingetTask = Task.Run(delegate { return InstallerEngine.GetWingetVersion(); });

            _hasInternet = await netTask;
            string ver = await wingetTask;
            _wingetAvailable = !string.IsNullOrEmpty(ver);
            _wingetVersion = ver ?? string.Empty;

            PostMessage(new Dictionary<string, object>
            {
                { "type", "loaderStep" },
                { "pct", 50 },
                { "dot", 1 },
                { "text", "Cargando catalogo apps.json y carpeta local\\..." }
            });

            ReloadCatalogInternal();

            PostMessage(new Dictionary<string, object>
            {
                { "type", "loaderStep" },
                { "pct", 78 },
                { "dot", 2 },
                { "text", "Escaneando aplicaciones instaladas en Windows..." }
            });

            List<AppItem> allApps = GetAllApps();
            HashSet<string> detected = await _engine.DetectInstalledAppKeysAsync(allApps);
            _installedKeys.Clear();
            foreach (string k in detected) _installedKeys.Add(k);

            PostMessage(new Dictionary<string, object>
            {
                { "type", "loaderStep" },
                { "pct", 94 },
                { "dot", 3 },
                { "text", "Verificando instaladores locales y actualizaciones..." }
            });

            _outdatedCaches.Clear();
            if (_hasInternet && _wingetAvailable)
            {
                Dictionary<string, OfflineCacheUpdateInfo> outdated = await _engine.CheckOutdatedOfflineCachesAsync(allApps);
                foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in outdated)
                {
                    _outdatedCaches[kv.Key] = kv.Value;
                }
            }

            PostStateUpdate();

            PostMessage(new Dictionary<string, object>
            {
                { "type", "loaderStep" },
                { "pct", 100 },
                { "dot", 3 },
                { "text", "¡Todo listo! Iniciando interfaz..." }
            });

            PostLog(string.Format(
                "Entorno listo: Internet={0}, Winget={1}, {2} apps en catalogo, {3} detectadas instaladas.",
                _hasInternet ? "OK" : "OFFLINE",
                _wingetAvailable ? _wingetVersion : "No disponible",
                allApps.Count,
                _installedKeys.Count), "success");

            if (_outdatedCaches.Count > 0)
            {
                PostOutdatedAlert(_outdatedCaches);
            }
        }

        private async Task HandleRefreshAsync()
        {
            if (_isBusy) return;
            PostLog("Re-escaneando red, carpeta local\\ y aplicaciones instaladas...", "info");

            Task<bool> netTask = InstallerEngine.CheckInternetAsync();
            Task<string> wingetTask = Task.Run(delegate { return InstallerEngine.GetWingetVersion(); });
            _hasInternet = await netTask;
            string ver = await wingetTask;
            _wingetAvailable = !string.IsNullOrEmpty(ver);
            _wingetVersion = ver ?? string.Empty;

            ReloadCatalogInternal();
            List<AppItem> allApps = GetAllApps();
            HashSet<string> detected = await _engine.DetectInstalledAppKeysAsync(allApps);
            _installedKeys.Clear();
            foreach (string k in detected) _installedKeys.Add(k);

            _outdatedCaches.Clear();
            if (_hasInternet && _wingetAvailable)
            {
                Dictionary<string, OfflineCacheUpdateInfo> outdated = await _engine.CheckOutdatedOfflineCachesAsync(allApps);
                foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in outdated)
                {
                    _outdatedCaches[kv.Key] = kv.Value;
                }
            }

            PostStateUpdate();
            PostToast(string.Format("Actualizado: {0} apps en catalogo, {1} instaladas.", allApps.Count, _installedKeys.Count), "success");

            if (_outdatedCaches.Count > 0)
            {
                PostOutdatedAlert(_outdatedCaches);
            }
        }

        private async void HandleSearchWingetAsync(string query, int seq)
        {
            int mySeq = ++_searchSeq;
            if (!_hasInternet || !_wingetAvailable || string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                PostMessage(new Dictionary<string, object>
                {
                    { "type", "onlineResults" },
                    { "seq", seq },
                    { "query", query ?? string.Empty },
                    { "items", new List<object>() }
                });
                return;
            }

            List<WingetSearchEntry> entries = await _engine.SearchWingetAsync(query.Trim(), 12);
            if (this.IsDisposed || mySeq != _searchSeq) return;

            HashSet<string> existingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<AppItem> all = GetAllApps();
            for (int i = 0; i < all.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(all[i].Target)) existingTargets.Add(all[i].Target.Trim());
            }

            List<object> resultDtos = new List<object>();
            for (int i = 0; i < entries.Count; i++)
            {
                WingetSearchEntry e = entries[i];
                if (existingTargets.Contains(e.Id)) continue;
                string id = "winget_" + e.Id.ToLowerInvariant().Replace(".", "_");
                string key = id + "|" + e.Id;
                resultDtos.Add(new Dictionary<string, object>
                {
                    { "key", key },
                    { "id", id },
                    { "name", e.Name },
                    { "category", "Resultados Online (Winget)" },
                    { "description", "Paquete Winget: " + e.Id + (string.IsNullOrEmpty(e.Version) ? string.Empty : " (v" + e.Version + ")") },
                    { "source", "winget" },
                    { "target", e.Id },
                    { "version", e.Version ?? string.Empty },
                    { "silentArgs", string.Empty },
                    { "essential", false },
                    { "installed", false },
                    { "hasOffline", false },
                    { "requiresInternet", true },
                    { "isOnlineSearch", true }
                });
            }

            PostMessage(new Dictionary<string, object>
            {
                { "type", "onlineResults" },
                { "seq", seq },
                { "query", query },
                { "items", resultDtos }
            });
        }

        private void HandleQuickSaveOnline(Dictionary<string, object> msg)
        {
            if (_isBusy) return;
            Dictionary<string, object> appDict = msg.ContainsKey("app") ? msg["app"] as Dictionary<string, object> : null;
            if (appDict == null) return;

            AppItem item = new AppItem();
            item.Id = GetStr(appDict, "id");
            item.Name = GetStr(appDict, "name");
            item.Category = "Utilidades";
            item.Description = GetStr(appDict, "description");
            item.Source = "winget";
            item.Target = GetStr(appDict, "target");
            item.SilentArgs = string.Empty;
            item.Essential = false;

            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Target)) return;

            _catalog.Apps.Add(item);
            AppCatalog.Save(_catalog);
            ReloadCatalogInternal();
            PostStateUpdate();
            PostToast("'" + item.Name + "' guardada en tu catalogo (Utilidades).", "success");
            PostLog("Guardada en apps.json desde busqueda online: " + item.Name + " (" + item.Target + ")", "success");
        }

        private void HandleRemoveApp(string key)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(key)) return;
            AppItem target = null;
            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                if (string.Equals(GetAppKey(_catalog.Apps[i]), key, StringComparison.OrdinalIgnoreCase))
                {
                    target = _catalog.Apps[i];
                    break;
                }
            }
            if (target == null) return;

            _catalog.Apps.Remove(target);
            AppCatalog.Save(_catalog);
            ReloadCatalogInternal();
            PostStateUpdate();
            PostToast("'" + target.Name + "' quitada del catalogo.", "info");
        }

        private void HandleAddLocalInstaller()
        {
            if (_isBusy) return;
            List<string> cats = new List<string>();
            List<AppItem> all = GetAllApps();
            for (int i = 0; i < all.Count; i++)
            {
                string c = all[i].Category;
                if (!string.IsNullOrWhiteSpace(c) && !cats.Contains(c)) cats.Add(c);
            }

            using (AddAppDialog dlg = new AddAppDialog(_engine, cats))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CreatedApp != null)
                {
                    _catalog.Apps.Add(dlg.CreatedApp);
                    AppCatalog.Save(_catalog);
                    ReloadCatalogInternal();
                    PostStateUpdate();
                    PostToast("Instalador local agregado: " + dlg.CreatedApp.Name, "success");
                    PostLog("Instalador local agregado a apps.json: " + dlg.CreatedApp.Name + " (" + dlg.CreatedApp.Target + ")", "success");
                }
            }
        }

        private async Task HandleInstallSelectedAsync(Dictionary<string, object> msg)
        {
            if (_isBusy) return;

            List<AppItem> selected = ResolveRequestedApps(msg);
            if (selected.Count == 0)
            {
                PostToast("Selecciona al menos una aplicacion para instalar.", "warn");
                return;
            }

            bool silent = GetBool(msg, "silent", true);
            bool upgrade = GetBool(msg, "upgrade", true);
            bool skipOnline = GetBool(msg, "skipOnline", false);
            bool forceReplace = GetBool(msg, "forceReplace", false);

            PostProgress(false, "idle", 0, "Verificando conexion a Internet...");
            bool onlineNow = await InstallerEngine.CheckInternetAsync();
            _hasInternet = onlineNow;

            if (!onlineNow)
            {
                List<AppItem> needsNet = new List<AppItem>();
                List<AppItem> offlineReady = new List<AppItem>();
                for (int i = 0; i < selected.Count; i++)
                {
                    if (InstallerEngine.AppRequiresInternet(selected[i])) needsNet.Add(selected[i]);
                    else offlineReady.Add(selected[i]);
                }

                if (needsNet.Count > 0 && !skipOnline)
                {
                    PostStateUpdate();
                    List<string> needNames = new List<string>();
                    for (int i = 0; i < needsNet.Count; i++) needNames.Add(needsNet[i].Name);
                    PostMessage(new Dictionary<string, object>
                    {
                        { "type", "offlinePrompt" },
                        { "needsInternet", needNames },
                        { "offlineCount", offlineReady.Count }
                    });
                    PostProgress(false, "idle", 0, "Sin conexion a Internet.");
                    return;
                }

                if (skipOnline)
                {
                    selected = offlineReady;
                    if (selected.Count == 0)
                    {
                        PostToast("No hay aplicaciones offline seleccionadas.", "warn");
                        return;
                    }
                }
            }
            else if (_wingetAvailable)
            {
                PostProgress(false, "idle", 0, "Verificando versiones de instaladores locales...");
                Dictionary<string, OfflineCacheUpdateInfo> stale = await _engine.CheckOutdatedOfflineCachesAsync(selected);
                if (stale.Count > 0 && !forceReplace)
                {
                    List<object> staleList = new List<object>();
                    foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in stale)
                    {
                        _outdatedCaches[kv.Key] = kv.Value;
                        staleList.Add(new Dictionary<string, object>
                        {
                            { "key", kv.Key },
                            { "name", kv.Value.App.Name },
                            { "localVersion", kv.Value.LocalVersion },
                            { "latestVersion", kv.Value.LatestVersion }
                        });
                    }
                    PostStateUpdate();
                    PostMessage(new Dictionary<string, object>
                    {
                        { "type", "mandatoryReplacePrompt" },
                        { "items", staleList },
                        { "silent", silent },
                        { "upgrade", upgrade }
                    });
                    PostProgress(false, "idle", 0, "Instalacion bloqueada: debes reemplazar instaladores locales desactualizados.");
                    return;
                }

                if (stale.Count > 0 && forceReplace)
                {
                    _isBusy = true;
                    _engine.ResetCancellation();
                    List<AppItem> toReplace = new List<AppItem>();
                    foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in stale) toReplace.Add(kv.Value.App);

                    for (int r = 0; r < toReplace.Count; r++)
                    {
                        AppItem sApp = toReplace[r];
                        int pct = (int)Math.Round(((double)r / toReplace.Count) * 100.0);
                        PostProgress(true, "download", pct, string.Format("Reemplazando instalador ({0}/{1}): {2}...", r + 1, toReplace.Count, sApp.Name));
                        InstallResult dlRes = await _engine.DownloadForOfflineAsync(sApp);
                        string rKey = GetAppKey(sApp);
                        if (dlRes.Status == InstallStatus.Success)
                        {
                            _outdatedCaches.Remove(rKey);
                            PostAppUpdated(sApp, _installedKeys.Contains(rKey), true, false, false);
                        }
                        else
                        {
                            selected.Remove(sApp);
                        }
                        if (dlRes.Status == InstallStatus.Cancelled)
                        {
                            _isBusy = false;
                            PostProgress(false, "idle", 0, "Reemplazo cancelado.");
                            return;
                        }
                    }
                    _isBusy = false;
                    if (selected.Count == 0)
                    {
                        PostToast("No se pudieron reemplazar los instaladores desactualizados.", "warn");
                        PostProgress(false, "idle", 0, "Instalacion detenida.");
                        return;
                    }
                }
            }

            // Verificar si necesita Winget y no esta disponible
            bool needsWinget = false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (string.Equals(selected[i].Source, "winget", StringComparison.OrdinalIgnoreCase) && !InstallerEngine.HasOfflineCache(selected[i]))
                {
                    needsWinget = true;
                    break;
                }
            }
            if (needsWinget && !_wingetAvailable)
            {
                PostToast("Winget no esta instalado en este equipo. Usa el boton 'Instalar Winget' arriba.", "warn");
                return;
            }

            _isBusy = true;
            _engine.ResetCancellation();
            List<InstallResult> results = new List<InstallResult>();

            for (int i = 0; i < selected.Count; i++)
            {
                AppItem app = selected[i];
                int basePct = (int)Math.Round(((double)i / selected.Count) * 100.0);
                PostProgress(true, "install", basePct, string.Format("Instalando ({0}/{1}): {2}...", i + 1, selected.Count, app.Name));

                InstallResult res = await _engine.InstallAppAsync(app, silent, upgrade);
                results.Add(res);

                if (res.Status == InstallStatus.Success || res.Status == InstallStatus.AlreadyInstalled)
                {
                    string key = GetAppKey(app);
                    bool isTool = string.Equals(app.Source, "powershell", StringComparison.OrdinalIgnoreCase);
                    if (!isTool) _installedKeys.Add(key);
                    PostAppUpdated(app, !isTool, InstallerEngine.HasOfflineCache(app), _outdatedCaches.ContainsKey(key), true);
                }

                if (res.Status == InstallStatus.Cancelled) break;

                int donePct = (int)Math.Round(((double)(i + 1) / selected.Count) * 100.0);
                PostProgress(true, "install", donePct, string.Format("Completado ({0}/{1}): {2}", i + 1, selected.Count, app.Name));
            }

            _isBusy = false;
            PostSummary(results, false);
        }

        private async Task HandleDownloadOfflineAsync(Dictionary<string, object> msg)
        {
            if (_isBusy) return;
            List<AppItem> selected = ResolveRequestedApps(msg);
            if (selected.Count == 0)
            {
                PostToast("Marca las aplicaciones que deseas descargar para tu pendrive.", "warn");
                return;
            }

            PostProgress(false, "idle", 0, "Verificando conexion a Internet...");
            bool onlineNow = await InstallerEngine.CheckInternetAsync();
            _hasInternet = onlineNow;
            if (!onlineNow)
            {
                PostStateUpdate();
                PostToast("Necesitas conexion a Internet para descargar instaladores offline.", "warn");
                PostProgress(false, "idle", 0, "Sin conexion a Internet.");
                return;
            }

            List<AppItem> downloadable = new List<AppItem>();
            List<string> skipped = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (InstallerEngine.CanDownloadOffline(selected[i])) downloadable.Add(selected[i]);
                else skipped.Add(selected[i].Name);
            }

            if (downloadable.Count == 0)
            {
                PostToast("Las opciones marcadas requieren red interactiva o ya son instaladores locales.", "info");
                return;
            }

            if (skipped.Count > 0)
            {
                PostLog("Omitidas de descarga offline (herramientas interactivas/locales): " + string.Join(", ", skipped.ToArray()), "warn");
            }

            await RunOfflineDownloadBatchAsync(downloadable);
        }

        private async Task HandleReplaceOutdatedAsync(Dictionary<string, object> msg)
        {
            if (_isBusy) return;
            List<AppItem> list = new List<AppItem>();
            List<string> keys = GetStringList(msg, "keys");
            if (keys.Count > 0)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    OfflineCacheUpdateInfo info;
                    if (_outdatedCaches.TryGetValue(keys[i], out info)) list.Add(info.App);
                }
            }
            else
            {
                foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in _outdatedCaches) list.Add(kv.Value.App);
            }

            if (list.Count == 0) return;
            await RunOfflineDownloadBatchAsync(list);
        }

        private async Task RunOfflineDownloadBatchAsync(List<AppItem> downloadable)
        {
            _isBusy = true;
            _engine.ResetCancellation();
            List<InstallResult> results = new List<InstallResult>();

            for (int i = 0; i < downloadable.Count; i++)
            {
                AppItem app = downloadable[i];
                int pct = (int)Math.Round(((double)i / downloadable.Count) * 100.0);
                PostProgress(true, "download", pct, string.Format("Descargando offline ({0}/{1}): {2}...", i + 1, downloadable.Count, app.Name));

                InstallResult res = await _engine.DownloadForOfflineAsync(app);
                results.Add(res);

                if (res.Status == InstallStatus.Success || res.Status == InstallStatus.AlreadyInstalled)
                {
                    string key = GetAppKey(app);
                    _outdatedCaches.Remove(key);
                    PostAppUpdated(app, _installedKeys.Contains(key), true, false, true);
                }

                if (res.Status == InstallStatus.Cancelled) break;
                int donePct = (int)Math.Round(((double)(i + 1) / downloadable.Count) * 100.0);
                PostProgress(true, "download", donePct, string.Format("Guardado offline ({0}/{1}): {2}", i + 1, downloadable.Count, app.Name));
            }

            _isBusy = false;
            PostSummary(results, true);
        }

        private async Task HandleInstallWingetAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            PostProgress(true, "install", 30, "Descargando e instalando Winget desde Microsoft...");
            bool ok = await _engine.InstallWingetAsync();
            _isBusy = false;
            PostProgress(false, "idle", ok ? 100 : 0, ok ? "Winget instalado correctamente." : "No se pudo instalar Winget.");
            if (ok)
            {
                await HandleRefreshAsync();
            }
            else
            {
                PostToast("No se pudo instalar Winget automaticamente.", "warn");
            }
        }

        private void OpenLocalFolder()
        {
            try
            {
                string dir = AppCatalog.GetLocalInstallersDirectory();
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                PostToast("No se pudo abrir la carpeta local\\: " + ex.Message, "warn");
            }
        }

        private void HandlePurgeOutdated(Dictionary<string, object> msg)
        {
            if (_isBusy) return;
            List<string> keys = GetStringList(msg, "keys");
            if (keys.Count == 0)
            {
                foreach (string k in _outdatedCaches.Keys) keys.Add(k);
            }

            if (keys.Count == 0)
            {
                PostToast("No hay instaladores descargados obsoletos para purgar.", "info");
                return;
            }

            long freedBytes = 0;
            int purgedCount = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                OfflineCacheUpdateInfo info;
                if (_outdatedCaches.TryGetValue(keys[i], out info))
                {
                    string cacheDir = InstallerEngine.GetOfflineCacheDirForApp(info.App);
                    freedBytes += DeleteDirectoryContents(cacheDir);
                    try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
                    _outdatedCaches.Remove(keys[i]);
                    purgedCount++;
                }
            }

            double mb = freedBytes / (1024.0 * 1024.0);
            PostStateUpdate();
            string text = string.Format("Se purgaron {0} instalador(es) obsoleto(s) ({1:0.0} MB liberados).", purgedCount, mb);
            PostLog(text, "success");
            PostToast(text, "success");
        }

        private void HandlePurgeCache(bool includeOfflineCache)
        {
            if (_isBusy) return;
            long freedBytes = 0;

            freedBytes += DeleteDirectoryContents(Path.Combine(Path.GetTempPath(), "Qinstall"));
            if (includeOfflineCache)
            {
                freedBytes += DeleteDirectoryContents(Path.Combine(AppCatalog.GetLocalInstallersDirectory(), "cache"));
                _outdatedCaches.Clear();
            }

            double mb = freedBytes / (1024.0 * 1024.0);
            PostStateUpdate();
            string msg = string.Format("Cache limpiada correctamente ({0:0.0} MB liberados).", mb);
            PostLog(msg, "success");
            PostToast(msg, "success");
        }

        private static long DeleteDirectoryContents(string dir)
        {
            long bytes = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        bytes += fi.Length;
                        fi.Delete();
                    }
                    catch { }
                }
                string[] dirs = Directory.GetDirectories(dir);
                for (int i = 0; i < dirs.Length; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
            return bytes;
        }

        private void ReloadCatalogInternal()
        {
            _catalog = AppCatalog.LoadOrCreateDefault();
            _autoDiscoveredLocals.Clear();
            List<AppItem> extra = AppCatalog.DiscoverFolderLocalInstallers(_catalog);
            for (int i = 0; i < extra.Count; i++) _autoDiscoveredLocals.Add(extra[i]);
        }

        private List<AppItem> GetAllApps()
        {
            List<AppItem> all = new List<AppItem>();
            if (_catalog != null && _catalog.Apps != null) all.AddRange(_catalog.Apps);
            if (_autoDiscoveredLocals.Count > 0) all.AddRange(_autoDiscoveredLocals);
            return all;
        }

        private List<AppItem> ResolveRequestedApps(Dictionary<string, object> msg)
        {
            List<AppItem> selected = new List<AppItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> keys = GetStringList(msg, "keys");

            Dictionary<string, AppItem> map = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);
            List<AppItem> all = GetAllApps();
            for (int i = 0; i < all.Count; i++)
            {
                map[GetAppKey(all[i])] = all[i];
            }

            for (int i = 0; i < keys.Count; i++)
            {
                AppItem found;
                if (map.TryGetValue(keys[i], out found) && seen.Add(keys[i]))
                {
                    selected.Add(found);
                }
            }

            System.Collections.IEnumerable onlineRaw = msg.ContainsKey("onlineApps") ? msg["onlineApps"] as System.Collections.IEnumerable : null;
            if (onlineRaw != null && !(onlineRaw is string))
            {
                foreach (object entry in onlineRaw)
                {
                    Dictionary<string, object> d = entry as Dictionary<string, object>;
                    if (d == null) continue;
                    AppItem oa = new AppItem();
                    oa.Id = GetStr(d, "id");
                    oa.Name = GetStr(d, "name");
                    oa.Category = "Resultados Online (Winget)";
                    oa.Description = GetStr(d, "description");
                    oa.Source = "winget";
                    oa.Target = GetStr(d, "target");
                    oa.SilentArgs = string.Empty;
                    oa.Essential = false;
                    string k = GetAppKey(oa);
                    if (!string.IsNullOrWhiteSpace(oa.Target) && seen.Add(k))
                    {
                        selected.Add(oa);
                    }
                }
            }

            return selected;
        }

        private void PostStateUpdate()
        {
            List<AppItem> all = GetAllApps();
            List<object> appDtos = new List<object>();

            for (int i = 0; i < all.Count; i++)
            {
                AppItem app = all[i];
                string key = GetAppKey(app);
                bool installed = _installedKeys.Contains(key);
                bool hasOffline = InstallerEngine.HasOfflineCache(app);
                bool reqNet = InstallerEngine.AppRequiresInternet(app);
                string cachedVer = hasOffline ? InstallerEngine.GetCachedOfflineVersion(app) : string.Empty;

                OfflineCacheUpdateInfo upd;
                bool isOutdated = _outdatedCaches.TryGetValue(key, out upd);

                appDtos.Add(new Dictionary<string, object>
                {
                    { "key", key },
                    { "id", app.Id ?? string.Empty },
                    { "name", app.Name ?? string.Empty },
                    { "category", string.IsNullOrWhiteSpace(app.Category) ? "Otros" : app.Category },
                    { "description", app.Description ?? string.Empty },
                    { "source", (app.Source ?? "winget").ToLowerInvariant() },
                    { "target", app.Target ?? string.Empty },
                    { "silentArgs", app.SilentArgs ?? string.Empty },
                    { "essential", app.Essential },
                    { "installed", installed },
                    { "hasOffline", hasOffline },
                    { "requiresInternet", reqNet },
                    { "cachedVersion", cachedVer ?? string.Empty },
                    { "outdated", isOutdated },
                    { "latestVersion", isOutdated ? upd.LatestVersion : string.Empty }
                });
            }

            PostMessage(new Dictionary<string, object>
            {
                { "type", "stateUpdate" },
                { "apps", appDtos },
                { "env", new Dictionary<string, object>
                    {
                        { "hasInternet", _hasInternet },
                        { "wingetAvailable", _wingetAvailable },
                        { "wingetVersion", _wingetVersion },
                        { "isAdmin", InstallerEngine.IsAdministrator() },
                        { "localDir", AppCatalog.GetLocalInstallersDirectory() },
                        { "installedCount", _installedKeys.Count },
                        { "outdatedCount", _outdatedCaches.Count }
                    }
                }
            });
        }

        private void PostAppUpdated(AppItem app, bool installed, bool hasOffline, bool outdated, bool uncheck)
        {
            string key = GetAppKey(app);
            PostMessage(new Dictionary<string, object>
            {
                { "type", "appUpdated" },
                { "key", key },
                { "installed", installed },
                { "hasOffline", hasOffline },
                { "requiresInternet", InstallerEngine.AppRequiresInternet(app) },
                { "cachedVersion", hasOffline ? InstallerEngine.GetCachedOfflineVersion(app) : string.Empty },
                { "outdated", outdated },
                { "uncheck", uncheck }
            });
        }

        private void PostOutdatedAlert(Dictionary<string, OfflineCacheUpdateInfo> outdated)
        {
            List<object> items = new List<object>();
            foreach (KeyValuePair<string, OfflineCacheUpdateInfo> kv in outdated)
            {
                items.Add(new Dictionary<string, object>
                {
                    { "key", kv.Key },
                    { "name", kv.Value.App.Name },
                    { "localVersion", kv.Value.LocalVersion },
                    { "latestVersion", kv.Value.LatestVersion }
                });
            }
            PostMessage(new Dictionary<string, object>
            {
                { "type", "outdatedAlert" },
                { "items", items }
            });
        }

        private void PostSummary(List<InstallResult> results, bool isOfflineDownload)
        {
            int okCount = 0;
            int alreadyCount = 0;
            int failCount = 0;
            List<object> rows = new List<object>();

            for (int i = 0; i < results.Count; i++)
            {
                InstallResult r = results[i];
                string statusStr = "failed";
                if (r.Status == InstallStatus.Success) { okCount++; statusStr = "ok"; }
                else if (r.Status == InstallStatus.AlreadyInstalled) { alreadyCount++; statusStr = "already"; }
                else if (r.Status == InstallStatus.Cancelled) { statusStr = "cancelled"; }
                else { failCount++; }

                rows.Add(new Dictionary<string, object>
                {
                    { "name", r.App.Name },
                    { "status", statusStr },
                    { "message", r.Message }
                });
            }

            string headline = isOfflineDownload
                ? string.Format("Descarga offline finalizada — Guardadas: {0} | Fallidas: {1}", okCount + alreadyCount, failCount)
                : string.Format("Finalizado — Instaladas: {0} | Ya al dia: {1} | Fallidas: {2}", okCount, alreadyCount, failCount);

            PostProgress(false, "idle", 100, headline);
            PostMessage(new Dictionary<string, object>
            {
                { "type", "summary" },
                { "title", isOfflineDownload ? "Resumen de descarga offline" : "Resumen de instalacion" },
                { "headline", headline },
                { "hasFailures", failCount > 0 },
                { "items", rows }
            });
        }

        private void PostProgress(bool busy, string mode, int pct, string statusText)
        {
            PostMessage(new Dictionary<string, object>
            {
                { "type", "progress" },
                { "busy", busy },
                { "mode", mode },
                { "pct", Math.Max(0, Math.Min(100, pct)) },
                { "statusText", statusText ?? string.Empty }
            });
        }

        private void PostToast(string message, string level)
        {
            PostMessage(new Dictionary<string, object>
            {
                { "type", "toast" },
                { "message", message },
                { "level", level }
            });
        }

        private void OnEngineLog(string message)
        {
            string level = "info";
            if (message.StartsWith("[OK]")) level = "success";
            else if (message.StartsWith("[X]") || message.StartsWith("[!]")) level = "warn";
            PostLog(message, level);
        }

        private void OnDownloadProgress(int percent)
        {
            PostMessage(new Dictionary<string, object>
            {
                { "type", "downloadPct" },
                { "pct", percent }
            });
        }

        private void PostLog(string message, string level)
        {
            PostMessage(new Dictionary<string, object>
            {
                { "type", "log" },
                { "time", DateTime.Now.ToString("HH:mm:ss") },
                { "message", message ?? string.Empty },
                { "level", level ?? "info" }
            });
        }

        private void PostMessage(Dictionary<string, object> payload)
        {
            if (this.IsDisposed || _webView == null || _webView.IsDisposed) return;
            string jsonStr = _json.Serialize(payload);
            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action<string>(SendRawJson), jsonStr);
                }
                catch { }
            }
            else
            {
                SendRawJson(jsonStr);
            }
        }

        private void SendRawJson(string jsonStr)
        {
            try
            {
                if (_webView != null && !_webView.IsDisposed && _webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.PostWebMessageAsJson(jsonStr);
                }
            }
            catch
            {
            }
        }

        private static string GetAppKey(AppItem app)
        {
            return (app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty);
        }

        private static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null) ? v.ToString() : string.Empty;
        }

        private static int GetInt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                int n;
                if (int.TryParse(v.ToString(), out n)) return n;
            }
            return 0;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool defaultVal = false)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                bool b;
                if (bool.TryParse(v.ToString(), out b)) return b;
            }
            return defaultVal;
        }

        private static List<string> GetStringList(Dictionary<string, object> d, string key)
        {
            List<string> res = new List<string>();
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null && !(v is string))
            {
                System.Collections.IEnumerable seq = v as System.Collections.IEnumerable;
                if (seq != null)
                {
                    foreach (object item in seq)
                    {
                        if (item != null) res.Add(item.ToString());
                    }
                }
            }
            return res;
        }
    }
}
