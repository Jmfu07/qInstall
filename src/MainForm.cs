using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Qinstall
{
    public class MainForm : Form
    {
        private readonly Color _bgMain = Color.FromArgb(18, 21, 28);
        private readonly Color _bgHeader = Color.FromArgb(24, 28, 38);
        private readonly Color _bgCard = Color.FromArgb(28, 33, 45);
        private readonly Color _bgControl = Color.FromArgb(38, 44, 58);
        private readonly Color _accentBlue = Color.FromArgb(46, 125, 246);
        private readonly Color _accentGreen = Color.FromArgb(34, 175, 96);
        private readonly Color _accentRed = Color.FromArgb(215, 65, 75);
        private readonly Color _accentAmber = Color.FromArgb(224, 155, 40);
        private readonly Color _textPrimary = Color.FromArgb(240, 244, 250);
        private readonly Color _textMuted = Color.FromArgb(150, 164, 188);

        private readonly InstallerEngine _engine;
        private AppCatalog _catalog;
        private readonly Dictionary<string, CheckBox> _appCheckboxes = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Label> _appTagLabels = new Dictionary<string, Label>();
        private readonly Dictionary<string, bool> _selectedState = new Dictionary<string, bool>();
        private readonly Dictionary<string, AppItem> _onlineSelectedApps = new Dictionary<string, AppItem>();
        private readonly List<AppItem> _currentOnlineResults = new List<AppItem>();
        private readonly List<AppItem> _autoDiscoveredLocalApps = new List<AppItem>();
        private readonly HashSet<string> _installedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _searchDebounceTimer;
        private int _onlineSearchSeq;
        private bool _isSearchingOnline;
        private bool _isInstalling;
        private bool _cancelPending;
        private bool _wingetAvailable;
        private bool _hasInternet = true;
        private bool _logVisible = false;

        // UI Controls
        private Label _lblInternetStatus;
        private Label _lblWingetStatus;
        private Button _btnInstallWinget;
        private Button _btnAdmin;
        private TextBox _txtSearch;
        private FlowLayoutPanel _flowCategories;
        private Panel _pnlLogContainer;
        private TextBox _txtLog;
        private Label _lblSelectedCount;
        private CheckBox _chkSilent;
        private CheckBox _chkUpgrade;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnInstall;
        private Button _btnToggleLog;
        private ToolTip _toolTip;

        public MainForm()
        {
            _engine = new InstallerEngine();
            _engine.LogMessage += OnEngineLog;
            _engine.DownloadProgress += OnDownloadProgress;
            _toolTip = new ToolTip();

            _searchDebounceTimer = new Timer();
            _searchDebounceTimer.Interval = 400;
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            BuildUi();
            LoadCatalogAndRender();
            CheckEnvironmentAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            this.Text = "Qinstall — Instalador Rapido de Aplicaciones";
            this.Size = new Size(1060, 740);
            this.MinimumSize = new Size(880, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = _bgMain;
            this.ForeColor = _textPrimary;
            this.Font = new Font("Segoe UI", 9.5f);

            // 1. HEADER
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 76;
            pnlHeader.BackColor = _bgHeader;
            pnlHeader.Padding = new Padding(20, 12, 20, 10);
            this.Controls.Add(pnlHeader);

            Label lblTitle = new Label();
            lblTitle.Text = "Qinstall";
            lblTitle.Font = new Font("Segoe UI Bold", 18f);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(20, 10);
            lblTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Alternativa rapida y portable a Ninite · Online (Winget + URL) y archivos del pendrive";
            lblSubtitle.Font = new Font("Segoe UI", 9f);
            lblSubtitle.ForeColor = _textMuted;
            lblSubtitle.Location = new Point(22, 44);
            lblSubtitle.AutoSize = true;
            pnlHeader.Controls.Add(lblSubtitle);

            FlowLayoutPanel pnlStatusBadges = new FlowLayoutPanel();
            pnlStatusBadges.Dock = DockStyle.Right;
            pnlStatusBadges.AutoSize = true;
            pnlStatusBadges.FlowDirection = FlowDirection.RightToLeft;
            pnlStatusBadges.WrapContents = false;
            pnlStatusBadges.Padding = new Padding(0, 12, 0, 0);
            pnlHeader.Controls.Add(pnlStatusBadges);

            _btnAdmin = CreateFlatButton(
                InstallerEngine.IsAdministrator() ? "Administrador: SI" : "Ejecutar como Admin",
                148,
                30,
                InstallerEngine.IsAdministrator() ? Color.FromArgb(30, 95, 62) : Color.FromArgb(62, 68, 86));
            _btnAdmin.Enabled = !InstallerEngine.IsAdministrator();
            _btnAdmin.Margin = new Padding(8, 0, 0, 0);
            _btnAdmin.Click += BtnAdmin_Click;
            pnlStatusBadges.Controls.Add(_btnAdmin);

            _btnInstallWinget = CreateFlatButton("Instalar Winget", 118, 30, _accentAmber);
            _btnInstallWinget.ForeColor = Color.FromArgb(20, 20, 20);
            _btnInstallWinget.Font = new Font("Segoe UI Semibold", 8.8f);
            _btnInstallWinget.Visible = false;
            _btnInstallWinget.Margin = new Padding(8, 0, 0, 0);
            _btnInstallWinget.Click += BtnInstallWinget_Click;
            pnlStatusBadges.Controls.Add(_btnInstallWinget);

            _lblWingetStatus = new Label();
            _lblWingetStatus.Text = "Verificando Winget...";
            _lblWingetStatus.AutoSize = true;
            _lblWingetStatus.Padding = new Padding(10, 7, 10, 7);
            _lblWingetStatus.BackColor = _bgControl;
            _lblWingetStatus.ForeColor = _textMuted;
            _lblWingetStatus.Font = new Font("Segoe UI Semibold", 8.5f);
            _lblWingetStatus.Margin = new Padding(8, 0, 0, 0);
            pnlStatusBadges.Controls.Add(_lblWingetStatus);

            _lblInternetStatus = new Label();
            _lblInternetStatus.Text = "Red...";
            _lblInternetStatus.AutoSize = true;
            _lblInternetStatus.Padding = new Padding(10, 7, 10, 7);
            _lblInternetStatus.BackColor = _bgControl;
            _lblInternetStatus.ForeColor = _textMuted;
            _lblInternetStatus.Font = new Font("Segoe UI Semibold", 8.5f);
            _lblInternetStatus.Margin = new Padding(8, 0, 0, 0);
            pnlStatusBadges.Controls.Add(_lblInternetStatus);

            // 2. TOOLBAR
            Panel pnlToolbar = new Panel();
            pnlToolbar.Dock = DockStyle.Top;
            pnlToolbar.Height = 52;
            pnlToolbar.BackColor = Color.FromArgb(22, 26, 35);
            pnlToolbar.Padding = new Padding(20, 10, 20, 10);
            this.Controls.Add(pnlToolbar);

            Label lblSearchIcon = new Label();
            lblSearchIcon.Text = "Buscar:";
            lblSearchIcon.ForeColor = _textMuted;
            lblSearchIcon.Location = new Point(20, 16);
            lblSearchIcon.AutoSize = true;
            pnlToolbar.Controls.Add(lblSearchIcon);

            _txtSearch = new TextBox();
            _txtSearch.Location = new Point(74, 13);
            _txtSearch.Size = new Size(300, 26);
            _txtSearch.BackColor = _bgControl;
            _txtSearch.ForeColor = Color.White;
            _txtSearch.BorderStyle = BorderStyle.FixedSingle;
            _txtSearch.TextChanged += TxtSearch_TextChanged;
            _txtSearch.KeyDown += TxtSearch_KeyDown;
            _toolTip.SetToolTip(_txtSearch, "Filtra tus apps o busca cualquier programa online en Winget (presiona Enter para buscar al instante)");
            pnlToolbar.Controls.Add(_txtSearch);

            FlowLayoutPanel pnlActions = new FlowLayoutPanel();
            pnlActions.Dock = DockStyle.Right;
            pnlActions.AutoSize = true;
            pnlActions.FlowDirection = FlowDirection.LeftToRight;
            pnlActions.WrapContents = false;
            pnlToolbar.Controls.Add(pnlActions);

            Button btnEssentials = CreateFlatButton("Esenciales", 92, 30, Color.FromArgb(40, 95, 175));
            btnEssentials.Margin = new Padding(0, 0, 8, 0);
            btnEssentials.Click += BtnEssentials_Click;
            pnlActions.Controls.Add(btnEssentials);

            Button btnSelectAll = CreateFlatButton("Todo", 66, 30, _bgControl);
            btnSelectAll.Margin = new Padding(0, 0, 8, 0);
            btnSelectAll.Click += BtnSelectAll_Click;
            pnlActions.Controls.Add(btnSelectAll);

            Button btnClear = CreateFlatButton("Limpiar", 76, 30, _bgControl);
            btnClear.Margin = new Padding(0, 0, 8, 0);
            btnClear.Click += BtnClear_Click;
            pnlActions.Controls.Add(btnClear);

            Button btnAddLocal = CreateFlatButton("+ Instalador local", 132, 30, Color.FromArgb(36, 120, 82));
            btnAddLocal.Margin = new Padding(0, 0, 8, 0);
            _toolTip.SetToolTip(btnAddLocal, "Agrega un .exe o .msi a la carpeta local\\ para instalar sin Internet");
            btnAddLocal.Click += BtnAddApp_Click;
            pnlActions.Controls.Add(btnAddLocal);

            Button btnReload = CreateFlatButton("\u21bb", 34, 30, _bgControl);
            btnReload.Font = new Font("Segoe UI Bold", 11f);
            _toolTip.SetToolTip(btnReload, "Re-escanear conexion a Internet, carpeta local\\ y apps instaladas");
            btnReload.Click += delegate { LoadCatalogAndRender(); CheckEnvironmentAsync(); };
            pnlActions.Controls.Add(btnReload);

            // 3. FOOTER (barra de progreso + boton instalar)
            Panel pnlFooter = new Panel();
            pnlFooter.Dock = DockStyle.Bottom;
            pnlFooter.Height = 96;
            pnlFooter.BackColor = _bgHeader;
            pnlFooter.Padding = new Padding(20, 10, 20, 12);
            this.Controls.Add(pnlFooter);

            _lblSelectedCount = new Label();
            _lblSelectedCount.Text = "0 aplicaciones seleccionadas";
            _lblSelectedCount.Font = new Font("Segoe UI Semibold", 10f);
            _lblSelectedCount.ForeColor = Color.White;
            _lblSelectedCount.Location = new Point(20, 12);
            _lblSelectedCount.AutoSize = true;
            pnlFooter.Controls.Add(_lblSelectedCount);

            _chkSilent = new CheckBox();
            _chkSilent.Text = "Instalacion silenciosa (sin ventanas)";
            _chkSilent.Checked = true;
            _chkSilent.AutoSize = true;
            _chkSilent.ForeColor = _textMuted;
            _chkSilent.Location = new Point(240, 13);
            pnlFooter.Controls.Add(_chkSilent);

            _chkUpgrade = new CheckBox();
            _chkUpgrade.Text = "Actualizar si ya existe";
            _chkUpgrade.Checked = true;
            _chkUpgrade.AutoSize = true;
            _chkUpgrade.ForeColor = _textMuted;
            _chkUpgrade.Location = new Point(490, 13);
            pnlFooter.Controls.Add(_chkUpgrade);

            _btnToggleLog = CreateFlatButton("Mostrar consola", 118, 26, _bgControl);
            _btnToggleLog.Font = new Font("Segoe UI", 8.5f);
            _btnToggleLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnToggleLog.Location = new Point(pnlFooter.Width - 360, 10);
            _btnToggleLog.Click += BtnToggleLog_Click;
            pnlFooter.Controls.Add(_btnToggleLog);

            _lblStatus = new Label();
            _lblStatus.Text = "Listo. Marca los programas que deseas instalar y presiona Instalar.";
            _lblStatus.ForeColor = _textMuted;
            _lblStatus.Font = new Font("Segoe UI", 8.8f);
            _lblStatus.Location = new Point(20, 42);
            _lblStatus.Size = new Size(760, 20);
            _lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pnlFooter.Controls.Add(_lblStatus);

            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(20, 65);
            _progressBar.Size = new Size(760, 18);
            _progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progressBar.Style = ProgressBarStyle.Continuous;
            pnlFooter.Controls.Add(_progressBar);

            _btnInstall = CreateFlatButton("Instalar seleccionadas", 210, 46, _accentGreen);
            _btnInstall.Font = new Font("Segoe UI Semibold", 10.5f);
            _btnInstall.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnInstall.Location = new Point(pnlFooter.Width - 230, 34);
            _btnInstall.Click += BtnInstall_Click;
            pnlFooter.Controls.Add(_btnInstall);

            // 4. LOG PANEL (encima del footer, oculto por defecto para mantener la interfaz limpia)
            _pnlLogContainer = new Panel();
            _pnlLogContainer.Dock = DockStyle.Bottom;
            _pnlLogContainer.Height = 135;
            _pnlLogContainer.Visible = _logVisible;
            _pnlLogContainer.BackColor = Color.FromArgb(14, 16, 22);
            _pnlLogContainer.Padding = new Padding(20, 6, 20, 6);
            this.Controls.Add(_pnlLogContainer);

            Label lblLogTitle = new Label();
            lblLogTitle.Text = "REGISTRO EN TIEMPO REAL";
            lblLogTitle.Font = new Font("Segoe UI Semibold", 8f);
            lblLogTitle.ForeColor = Color.FromArgb(115, 130, 155);
            lblLogTitle.Dock = DockStyle.Top;
            lblLogTitle.Height = 18;
            _pnlLogContainer.Controls.Add(lblLogTitle);

            _txtLog = new TextBox();
            _txtLog.Dock = DockStyle.Fill;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(14, 16, 22);
            _txtLog.ForeColor = Color.FromArgb(165, 225, 180);
            _txtLog.BorderStyle = BorderStyle.None;
            _txtLog.Font = new Font("Consolas", 9f);
            _pnlLogContainer.Controls.Add(_txtLog);
            _txtLog.BringToFront();

            // 5. MAIN SCROLLABLE CATEGORIES AREA
            _flowCategories = new FlowLayoutPanel();
            _flowCategories.Dock = DockStyle.Fill;
            _flowCategories.AutoScroll = true;
            _flowCategories.BackColor = _bgMain;
            _flowCategories.Padding = new Padding(16, 12, 16, 12);
            this.Controls.Add(_flowCategories);

            // Orden de acoplamiento: Header arriba del todo, Toolbar debajo, Footer abajo del todo, Log encima del Footer, Categorias en el centro
            pnlHeader.SendToBack();
            pnlFooter.SendToBack();
            _flowCategories.BringToFront();
        }

        private async void CheckEnvironmentAsync()
        {
            Task<bool> netTask = InstallerEngine.CheckInternetAsync();
            Task<string> wingetTask = Task.Run(delegate { return InstallerEngine.GetWingetVersion(); });

            bool online = await netTask;
            string version = await wingetTask;
            if (this.IsDisposed) return;

            UpdateInternetBadge(online);

            if (!string.IsNullOrEmpty(version))
            {
                _wingetAvailable = true;
                _lblWingetStatus.Text = "Winget OK (" + version + ")";
                _lblWingetStatus.BackColor = Color.FromArgb(24, 78, 52);
                _lblWingetStatus.ForeColor = Color.FromArgb(165, 240, 195);
                _btnInstallWinget.Visible = false;
                AppendLog("[OK] Winget detectado: " + version);
            }
            else
            {
                _wingetAvailable = false;
                _lblWingetStatus.Text = "Winget no encontrado";
                _lblWingetStatus.BackColor = Color.FromArgb(110, 42, 46);
                _lblWingetStatus.ForeColor = Color.FromArgb(255, 200, 200);
                _btnInstallWinget.Visible = online;
                AppendLog("[!] Winget no esta disponible en este equipo. Puedes instalarlo con el boton 'Instalar Winget' o usar instaladores locales.");
            }

            RenderCategories(_txtSearch != null ? _txtSearch.Text : string.Empty);
            RefreshInstalledAppsAsync();
        }

        private void UpdateInternetBadge(bool online)
        {
            _hasInternet = online;
            if (online)
            {
                _lblInternetStatus.Text = "Internet: OK";
                _lblInternetStatus.BackColor = Color.FromArgb(24, 78, 52);
                _lblInternetStatus.ForeColor = Color.FromArgb(165, 240, 195);
            }
            else
            {
                _lblInternetStatus.Text = "Sin Internet (Offline)";
                _lblInternetStatus.BackColor = Color.FromArgb(135, 78, 22);
                _lblInternetStatus.ForeColor = Color.FromArgb(255, 225, 165);
            }
        }

        private async void RefreshInstalledAppsAsync()
        {
            if (_catalog == null || _catalog.Apps == null) return;
            if (!_isInstalling)
            {
                _lblStatus.Text = "Comprobando aplicaciones ya instaladas en este equipo...";
            }

            HashSet<string> detected = await _engine.DetectInstalledAppKeysAsync(_catalog.Apps);
            if (this.IsDisposed) return;

            _installedApps.Clear();
            foreach (string key in detected)
            {
                _installedApps.Add(key);
                // Desmarcar automaticamente si ya esta instalada
                _selectedState[key] = false;
            }

            foreach (KeyValuePair<string, CheckBox> kv in _appCheckboxes)
            {
                AppItem item = kv.Value.Tag as AppItem;
                if (item == null) continue;
                bool isInst = _installedApps.Contains(kv.Key);
                if (isInst && kv.Value.Checked)
                {
                    kv.Value.Checked = false;
                }
                Label tagLbl;
                if (_appTagLabels.TryGetValue(kv.Key, out tagLbl))
                {
                    ApplyTagStyle(tagLbl, item, isInst);
                }
            }

            UpdateSelectedCounter();
            if (!_isInstalling)
            {
                _lblStatus.Text = string.Format(
                    "Listo ({0} apps detectadas como instaladas en este equipo).",
                    _installedApps.Count);
            }
        }

        private void LoadCatalogAndRender()
        {
            _catalog = AppCatalog.LoadOrCreateDefault();
            _autoDiscoveredLocalApps.Clear();
            List<AppItem> extraLocals = AppCatalog.DiscoverFolderLocalInstallers(_catalog);
            for (int i = 0; i < extraLocals.Count; i++)
            {
                _autoDiscoveredLocalApps.Add(extraLocals[i]);
            }
            RenderCategories(_txtSearch != null ? _txtSearch.Text : string.Empty);
        }

        private List<AppItem> GetAllCatalogApps()
        {
            List<AppItem> all = new List<AppItem>();
            if (_catalog != null && _catalog.Apps != null)
            {
                all.AddRange(_catalog.Apps);
            }
            if (_autoDiscoveredLocalApps.Count > 0)
            {
                all.AddRange(_autoDiscoveredLocalApps);
            }
            return all;
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            string query = (_txtSearch.Text ?? string.Empty).Trim();

            if (query.Length < 2)
            {
                _onlineSearchSeq++;
                _isSearchingOnline = false;
                _currentOnlineResults.Clear();
                RenderCategories(query);
                return;
            }

            _isSearchingOnline = _wingetAvailable && _hasInternet;
            RenderCategories(query);

            if (_wingetAvailable && _hasInternet)
            {
                _searchDebounceTimer.Start();
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _searchDebounceTimer.Stop();
                string query = (_txtSearch.Text ?? string.Empty).Trim();
                if (query.Length >= 2 && _wingetAvailable && _hasInternet)
                {
                    SearchOnlineWingetAsync(query);
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _txtSearch.Text = string.Empty;
            }
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            string query = (_txtSearch.Text ?? string.Empty).Trim();
            if (query.Length >= 2 && _wingetAvailable && _hasInternet)
            {
                SearchOnlineWingetAsync(query);
            }
        }

        private async void SearchOnlineWingetAsync(string query)
        {
            int mySeq = ++_onlineSearchSeq;
            _isSearchingOnline = true;

            List<WingetSearchEntry> entries = await _engine.SearchWingetAsync(query, 12);
            if (this.IsDisposed || mySeq != _onlineSearchSeq) return;

            HashSet<string> existingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(_catalog.Apps[i].Target))
                {
                    existingTargets.Add(_catalog.Apps[i].Target.Trim());
                }
            }

            _currentOnlineResults.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                WingetSearchEntry e = entries[i];
                if (existingTargets.Contains(e.Id)) continue;

                AppItem item = new AppItem();
                item.Id = "winget_" + e.Id.ToLowerInvariant().Replace(".", "_");
                item.Name = e.Name;
                item.Category = "Resultados Online (Winget)";
                item.Description = "Paquete Winget: " + e.Id + (string.IsNullOrEmpty(e.Version) ? string.Empty : " (v" + e.Version + ")");
                item.Source = "winget";
                item.Target = e.Id;
                item.SilentArgs = string.Empty;
                item.Essential = false;
                _currentOnlineResults.Add(item);
            }

            _isSearchingOnline = false;
            RenderCategories(_txtSearch.Text);
        }

        private void RenderCategories(string filterText)
        {
            _flowCategories.SuspendLayout();
            _flowCategories.Controls.Clear();
            _appCheckboxes.Clear();
            _appTagLabels.Clear();

            string query = (filterText ?? string.Empty).Trim();

            Dictionary<string, List<AppItem>> grouped = new Dictionary<string, List<AppItem>>(StringComparer.OrdinalIgnoreCase);
            List<string> categoryOrder = new List<string>();

            List<AppItem> allApps = GetAllCatalogApps();
            for (int i = 0; i < allApps.Count; i++)
            {
                AppItem app = allApps[i];
                if (!string.IsNullOrEmpty(query))
                {
                    bool match =
                        (app.Name != null && app.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (app.Category != null && app.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (app.Description != null && app.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (app.Target != null && app.Target.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                string cat = string.IsNullOrWhiteSpace(app.Category) ? "Otros" : app.Category;
                if (!grouped.ContainsKey(cat))
                {
                    grouped[cat] = new List<AppItem>();
                    categoryOrder.Add(cat);
                }
                grouped[cat].Add(app);
            }

            for (int c = 0; c < categoryOrder.Count; c++)
            {
                string catName = categoryOrder[c];
                List<AppItem> apps = grouped[catName];
                Panel card = CreateCategoryCard(catName, apps, false);
                _flowCategories.Controls.Add(card);
            }

            // Tarjeta de resultados online desde Winget cuando el usuario busca
            if (query.Length >= 2 && _wingetAvailable && _hasInternet)
            {
                if (_currentOnlineResults.Count > 0)
                {
                    Panel onlineCard = CreateCategoryCard("Resultados Online (Winget)", _currentOnlineResults, true);
                    _flowCategories.Controls.Add(onlineCard);
                }
                else if (_isSearchingOnline)
                {
                    Panel loadingCard = CreateOnlineStatusCard("Buscando '" + query + "' en Winget...");
                    _flowCategories.Controls.Add(loadingCard);
                }
                else if (categoryOrder.Count == 0)
                {
                    Panel emptyCard = CreateOnlineStatusCard("Sin resultados para '" + query + "' en tu catalogo ni en Winget.");
                    _flowCategories.Controls.Add(emptyCard);
                }
            }

            _flowCategories.ResumeLayout();
            UpdateSelectedCounter();
        }

        private Panel CreateOnlineStatusCard(string message)
        {
            Panel card = new Panel();
            card.Size = new Size(340, 84);
            card.BackColor = _bgCard;
            card.Margin = new Padding(8);

            Panel accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 3;
            accent.BackColor = _accentGreen;
            card.Controls.Add(accent);

            Label lblCat = new Label();
            lblCat.Text = "BUSQUEDA ONLINE (WINGET)";
            lblCat.Font = new Font("Segoe UI Semibold", 9.5f);
            lblCat.ForeColor = Color.FromArgb(145, 235, 185);
            lblCat.Location = new Point(12, 12);
            lblCat.AutoSize = true;
            card.Controls.Add(lblCat);

            Label lblMsg = new Label();
            lblMsg.Text = message;
            lblMsg.Font = new Font("Segoe UI", 9f);
            lblMsg.ForeColor = _textMuted;
            lblMsg.Location = new Point(12, 42);
            lblMsg.Size = new Size(316, 30);
            card.Controls.Add(lblMsg);

            return card;
        }

        private Panel CreateCategoryCard(string categoryName, List<AppItem> apps, bool isOnlineSearchCard)
        {
            int cardWidth = isOnlineSearchCard ? 360 : 315;
            int rowHeight = 32;
            int headerHeight = 36;
            int cardHeight = headerHeight + (apps.Count * rowHeight) + 14;

            Panel card = new Panel();
            card.Size = new Size(cardWidth, cardHeight);
            card.BackColor = _bgCard;
            card.Margin = new Padding(8);
            card.Padding = new Padding(12, 8, 12, 8);

            // Linea superior de acento
            Panel accent = new Panel();
            accent.Dock = DockStyle.Top;
            accent.Height = 3;
            accent.BackColor = isOnlineSearchCard ? _accentGreen : _accentBlue;
            card.Controls.Add(accent);

            Label lblCat = new Label();
            lblCat.Text = categoryName.ToUpperInvariant();
            lblCat.Font = new Font("Segoe UI Semibold", 9.5f);
            lblCat.ForeColor = isOnlineSearchCard ? Color.FromArgb(145, 235, 185) : Color.FromArgb(140, 185, 255);
            lblCat.Location = new Point(12, 12);
            lblCat.AutoSize = true;
            card.Controls.Add(lblCat);

            int y = headerHeight + 4;
            for (int i = 0; i < apps.Count; i++)
            {
                AppItem app = apps[i];
                string key = GetAppKey(app);

                CheckBox cb = new CheckBox();
                cb.Text = app.Name;
                cb.Tag = app;
                cb.Location = new Point(12, y);
                cb.Size = new Size(isOnlineSearchCard ? 235 : 220, 24);
                cb.ForeColor = _textPrimary;
                cb.Font = new Font("Segoe UI", 9.3f);
                cb.Cursor = _isInstalling ? Cursors.Default : Cursors.Hand;
                cb.AutoCheck = !_isInstalling;
                bool prevChecked;
                if (_selectedState.TryGetValue(key, out prevChecked))
                {
                    cb.Checked = prevChecked;
                }
                bool captureOnline = isOnlineSearchCard;
                cb.CheckedChanged += delegate(object s, EventArgs ev)
                {
                    CheckBox box = (CheckBox)s;
                    AppItem item = (AppItem)box.Tag;
                    string itemKey = GetAppKey(item);
                    _selectedState[itemKey] = box.Checked;
                    if (captureOnline)
                    {
                        if (box.Checked) _onlineSelectedApps[itemKey] = item;
                        else _onlineSelectedApps.Remove(itemKey);
                    }
                    UpdateSelectedCounter();
                };

                string tip = string.IsNullOrWhiteSpace(app.Description)
                    ? app.Target
                    : app.Description + "\nOrigen: " + app.Source.ToUpperInvariant() + " (" + app.Target + ")";
                if (!isOnlineSearchCard)
                {
                    tip += "\n(Clic derecho para quitar del catalogo)";
                    ContextMenuStrip menu = new ContextMenuStrip();
                    AppItem removeTarget = app;
                    ToolStripMenuItem miRemove = new ToolStripMenuItem("Quitar '" + app.Name + "' del catalogo");
                    miRemove.Click += delegate { RemoveAppFromCatalog(removeTarget); };
                    menu.Items.Add(miRemove);
                    cb.ContextMenuStrip = menu;
                }
                _toolTip.SetToolTip(cb, tip);

                card.Controls.Add(cb);
                _appCheckboxes[key] = cb;

                if (isOnlineSearchCard)
                {
                    // Boton rapido "+" para guardar esta app online permanentemente en el catalogo
                    AppItem saveTarget = app;
                    Button btnQuickAdd = CreateFlatButton("+", 26, 20, Color.FromArgb(46, 125, 246));
                    btnQuickAdd.Font = new Font("Segoe UI Bold", 9f);
                    btnQuickAdd.Location = new Point(cardWidth - 104, y + 2);
                    _toolTip.SetToolTip(btnQuickAdd, "Guardar '" + app.Name + "' permanentemente en tu catalogo");
                    btnQuickAdd.Click += delegate { QuickSaveOnlineApp(saveTarget); };
                    card.Controls.Add(btnQuickAdd);
                }

                // Etiqueta de estado / origen (INSTALADO / ONLINE / URL / LOCAL / TOOL)
                Label lblTag = new Label();
                lblTag.Font = new Font("Segoe UI Semibold", 7.0f);
                lblTag.TextAlign = ContentAlignment.MiddleCenter;
                lblTag.Size = new Size(64, 18);
                lblTag.Location = new Point(cardWidth - 76, y + 3);
                ApplyTagStyle(lblTag, app, _installedApps.Contains(key));

                card.Controls.Add(lblTag);
                _appTagLabels[key] = lblTag;
                y += rowHeight;
            }

            return card;
        }

        private void QuickSaveOnlineApp(AppItem onlineApp)
        {
            if (_isInstalling || onlineApp == null) return;
            AppItem copy = new AppItem();
            copy.Id = onlineApp.Id;
            copy.Name = onlineApp.Name;
            copy.Category = "Utilidades";
            copy.Description = onlineApp.Description;
            copy.Source = "winget";
            copy.Target = onlineApp.Target;
            copy.SilentArgs = string.Empty;
            copy.Essential = false;

            _catalog.Apps.Add(copy);
            AppCatalog.Save(_catalog);

            string key = GetAppKey(copy);
            _onlineSelectedApps.Remove(key);
            _selectedState[key] = true;
            _currentOnlineResults.Remove(onlineApp);

            RenderCategories(_txtSearch.Text);
            _lblStatus.Text = "'" + copy.Name + "' guardada en tu catalogo (Utilidades).";
            AppendLog("[+] Guardada en catalogo desde busqueda online: " + copy.Name + " (" + copy.Target + ")");
        }

        private void RemoveAppFromCatalog(AppItem app)
        {
            if (_isInstalling || app == null) return;
            DialogResult confirm = MessageBox.Show(
                this,
                "¿Quitar '" + app.Name + "' de tu catalogo?",
                "Quitar aplicacion",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            _catalog.Apps.Remove(app);
            AppCatalog.Save(_catalog);
            _selectedState.Remove(GetAppKey(app));
            RenderCategories(_txtSearch.Text);
            _lblStatus.Text = "'" + app.Name + "' quitada del catalogo.";
        }

        private void ApplyTagStyle(Label lblTag, AppItem app, bool installed)
        {
            if (installed)
            {
                lblTag.Text = "INSTALADO";
                lblTag.BackColor = Color.FromArgb(26, 98, 60);
                lblTag.ForeColor = Color.FromArgb(175, 250, 205);
                return;
            }

            bool needsNet = InstallerEngine.AppRequiresInternet(app);
            if (!_hasInternet && needsNet)
            {
                lblTag.Text = "SIN RED";
                lblTag.BackColor = Color.FromArgb(72, 46, 48);
                lblTag.ForeColor = Color.FromArgb(225, 165, 170);
                return;
            }

            string src = (app.Source ?? "winget").ToUpperInvariant();
            if (src == "WINGET")
            {
                lblTag.Text = "ONLINE";
                lblTag.BackColor = Color.FromArgb(30, 56, 95);
                lblTag.ForeColor = Color.FromArgb(145, 195, 255);
            }
            else if (src == "URL")
            {
                lblTag.Text = "URL";
                lblTag.BackColor = Color.FromArgb(75, 52, 95);
                lblTag.ForeColor = Color.FromArgb(215, 175, 255);
            }
            else if (src == "POWERSHELL")
            {
                lblTag.Text = "TOOL";
                lblTag.BackColor = Color.FromArgb(95, 68, 22);
                lblTag.ForeColor = Color.FromArgb(255, 220, 140);
            }
            else if (!needsNet)
            {
                lblTag.Text = "OFFLINE";
                lblTag.BackColor = Color.FromArgb(22, 92, 82);
                lblTag.ForeColor = Color.FromArgb(150, 245, 225);
            }
            else
            {
                lblTag.Text = "ONLINE";
                lblTag.BackColor = Color.FromArgb(36, 80, 56);
                lblTag.ForeColor = Color.FromArgb(155, 235, 185);
            }
        }

        private List<AppItem> GetSelectedApps()
        {
            List<AppItem> selected = new List<AppItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<AppItem> allApps = GetAllCatalogApps();
            for (int i = 0; i < allApps.Count; i++)
            {
                AppItem app = allApps[i];
                string key = GetAppKey(app);
                bool isSel;
                if (_selectedState.TryGetValue(key, out isSel) && isSel && seen.Add(key))
                {
                    selected.Add(app);
                }
            }

            foreach (KeyValuePair<string, AppItem> kv in _onlineSelectedApps)
            {
                bool isSel;
                if (_selectedState.TryGetValue(kv.Key, out isSel) && isSel && seen.Add(kv.Key))
                {
                    selected.Add(kv.Value);
                }
            }

            return selected;
        }

        private void UpdateSelectedCounter()
        {
            int count = GetSelectedApps().Count;
            _lblSelectedCount.Text = count == 1
                ? "1 aplicacion seleccionada"
                : string.Format("{0} aplicaciones seleccionadas", count);
        }

        private void BtnEssentials_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            _selectedState.Clear();
            List<AppItem> allApps = GetAllCatalogApps();
            for (int i = 0; i < allApps.Count; i++)
            {
                AppItem app = allApps[i];
                string key = GetAppKey(app);
                // Seleccionar esenciales que aun no estan instaladas
                _selectedState[key] = app.Essential && !_installedApps.Contains(key);
            }
            SyncCheckboxesFromState();
        }

        private void BtnSelectAll_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            foreach (KeyValuePair<string, CheckBox> kv in _appCheckboxes)
            {
                _selectedState[kv.Key] = true;
                kv.Value.Checked = true;
            }
            UpdateSelectedCounter();
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            _selectedState.Clear();
            _onlineSelectedApps.Clear();
            SyncCheckboxesFromState();
        }

        private void SyncCheckboxesFromState()
        {
            foreach (KeyValuePair<string, CheckBox> kv in _appCheckboxes)
            {
                bool val;
                _selectedState.TryGetValue(kv.Key, out val);
                kv.Value.Checked = val;
            }
            UpdateSelectedCounter();
        }

        private void BtnAddApp_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            List<string> cats = new List<string>();
            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                string c = _catalog.Apps[i].Category;
                if (!string.IsNullOrWhiteSpace(c) && !cats.Contains(c))
                {
                    cats.Add(c);
                }
            }

            using (AddAppDialog dlg = new AddAppDialog(_engine, cats))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CreatedApp != null)
                {
                    _catalog.Apps.Add(dlg.CreatedApp);
                    AppCatalog.Save(_catalog);
                    _selectedState[GetAppKey(dlg.CreatedApp)] = true;
                    LoadCatalogAndRender();
                    AppendLog("[+] Instalador local agregado: " + dlg.CreatedApp.Name);
                }
            }
        }

        private void BtnAdmin_Click(object sender, EventArgs e)
        {
            if (InstallerEngine.RelaunchAsAdministrator())
            {
                Application.Exit();
            }
        }

        private async void BtnInstallWinget_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            _btnInstallWinget.Enabled = false;
            _lblStatus.Text = "Instalando Winget...";
            _progressBar.Style = ProgressBarStyle.Marquee;

            bool ok = await _engine.InstallWingetAsync();

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;
            _btnInstallWinget.Enabled = true;

            if (ok)
            {
                CheckEnvironmentAsync();
                _lblStatus.Text = "Winget instalado correctamente.";
            }
            else
            {
                _lblStatus.Text = "No se pudo instalar Winget automaticamente.";
            }
        }

        private void BtnToggleLog_Click(object sender, EventArgs e)
        {
            _logVisible = !_logVisible;
            _pnlLogContainer.Visible = _logVisible;
            _btnToggleLog.Text = _logVisible ? "Ocultar consola" : "Mostrar consola";
        }

        private async void BtnInstall_Click(object sender, EventArgs e)
        {
            if (_isInstalling)
            {
                if (_cancelPending) return;
                _cancelPending = true;
                _btnInstall.Text = "Cancelando...";
                _btnInstall.BackColor = Color.FromArgb(140, 55, 62);
                _lblStatus.Text = "Cancelando instalacion...";
                _engine.CancelCurrent();
                return;
            }

            List<AppItem> selected = GetSelectedApps();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos una aplicacion para instalar.", "Qinstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Validar conexion a Internet en tiempo real antes de instalar
            _lblStatus.Text = "Verificando conexion a Internet...";
            bool onlineNow = await InstallerEngine.CheckInternetAsync();
            UpdateInternetBadge(onlineNow);

            if (!onlineNow)
            {
                List<AppItem> needsNet = new List<AppItem>();
                List<AppItem> offlineReady = new List<AppItem>();
                for (int i = 0; i < selected.Count; i++)
                {
                    if (InstallerEngine.AppRequiresInternet(selected[i]))
                        needsNet.Add(selected[i]);
                    else
                        offlineReady.Add(selected[i]);
                }

                if (needsNet.Count > 0)
                {
                    StringBuilder sbNet = new StringBuilder();
                    for (int i = 0; i < needsNet.Count; i++)
                    {
                        sbNet.AppendLine(" - " + needsNet[i].Name);
                    }

                    if (offlineReady.Count == 0)
                    {
                        _lblStatus.Text = "Sin conexion a Internet. Solo puedes instalar apps OFFLINE de la carpeta local\\.";
                        MessageBox.Show(
                            this,
                            "No se detecto conexion a Internet en este equipo.\n\nLas siguientes aplicaciones requieren Internet para descargarse/instalarse:\n" +
                            sbNet.ToString() +
                            "\nConecta el equipo a Internet o usa instaladores locales (etiqueta OFFLINE en la carpeta local\\).",
                            "Sin conexion a Internet — Qinstall",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        RenderCategories(_txtSearch.Text);
                        return;
                    }

                    DialogResult askOffline = MessageBox.Show(
                        this,
                        "No hay conexion a Internet.\n\nEstas aplicaciones requieren Internet y se omitiran:\n" +
                        sbNet.ToString() +
                        string.Format("\n¿Deseas continuar e instalar las {0} aplicaciones locales/offline seleccionadas?", offlineReady.Count),
                        "Modo sin Internet — Qinstall",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (askOffline != DialogResult.Yes)
                    {
                        _lblStatus.Text = "Instalacion cancelada (sin conexion a Internet).";
                        RenderCategories(_txtSearch.Text);
                        return;
                    }
                    selected = offlineReady;
                }
            }

            bool needsWinget = false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (string.Equals(selected[i].Source, "winget", StringComparison.OrdinalIgnoreCase))
                {
                    needsWinget = true;
                    break;
                }
            }

            if (needsWinget && !_wingetAvailable)
            {
                DialogResult ask = MessageBox.Show(
                    this,
                    "Algunas apps seleccionadas usan Winget pero no se detecto en este equipo.\n¿Deseas intentar instalar Winget ahora?",
                    "Winget no detectado",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (ask == DialogResult.Yes)
                {
                    bool installed = await _engine.InstallWingetAsync();
                    if (installed)
                    {
                        _wingetAvailable = true;
                        CheckEnvironmentAsync();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            _isInstalling = true;
            _cancelPending = false;
            _engine.ResetCancellation();
            SetUiBusyState(true);

            _progressBar.Minimum = 0;
            _progressBar.Maximum = selected.Count * 100;
            _progressBar.Value = 0;

            List<InstallResult> results = new List<InstallResult>();
            bool silent = _chkSilent.Checked;
            bool upgrade = _chkUpgrade.Checked;

            for (int i = 0; i < selected.Count; i++)
            {
                AppItem app = selected[i];
                _lblStatus.Text = string.Format("Instalando ({0}/{1}): {2}...", i + 1, selected.Count, app.Name);
                _progressBar.Value = i * 100;

                InstallResult res = await _engine.InstallAppAsync(app, silent, upgrade);
                results.Add(res);

                if (res.Status == InstallStatus.Success || res.Status == InstallStatus.AlreadyInstalled)
                {
                    MarkAppInstalledAndUncheck(app);
                }

                if (res.Status == InstallStatus.Cancelled)
                {
                    break;
                }

                _progressBar.Value = (i + 1) * 100;
            }

            _isInstalling = false;
            _cancelPending = false;
            SetUiBusyState(false);

            ShowSummary(results);
        }

        private void MarkAppInstalledAndUncheck(AppItem app)
        {
            string key = GetAppKey(app);
            bool isTool = string.Equals(app.Source, "powershell", StringComparison.OrdinalIgnoreCase);
            if (!isTool)
            {
                _installedApps.Add(key);
            }
            _selectedState[key] = false;

            CheckBox cb;
            if (_appCheckboxes.TryGetValue(key, out cb))
            {
                cb.Checked = false;
            }

            if (!isTool)
            {
                Label tagLbl;
                if (_appTagLabels.TryGetValue(key, out tagLbl))
                {
                    ApplyTagStyle(tagLbl, app, true);
                }
            }

            UpdateSelectedCounter();
        }

        private void SetUiBusyState(bool busy)
        {
            // Evitar Enabled=false en controles con tema oscuro (WinForms dibuja texto negro ilegible al deshabilitar)
            foreach (KeyValuePair<string, CheckBox> kv in _appCheckboxes)
            {
                kv.Value.AutoCheck = !busy;
                kv.Value.Cursor = busy ? Cursors.Default : Cursors.Hand;
            }

            _txtSearch.ReadOnly = busy;
            _chkSilent.AutoCheck = !busy;
            _chkUpgrade.AutoCheck = !busy;
            _chkSilent.ForeColor = busy ? Color.FromArgb(105, 118, 140) : _textMuted;
            _chkUpgrade.ForeColor = busy ? Color.FromArgb(105, 118, 140) : _textMuted;

            if (busy)
            {
                _btnInstall.Text = "Cancelar";
                _btnInstall.BackColor = _accentRed;
                _btnInstall.ForeColor = Color.White;
            }
            else
            {
                _btnInstall.Text = "Instalar seleccionadas";
                _btnInstall.BackColor = _accentGreen;
                _btnInstall.ForeColor = Color.White;
            }
        }

        private void ShowSummary(List<InstallResult> results)
        {
            int okCount = 0;
            int alreadyCount = 0;
            int failCount = 0;
            int cancelCount = 0;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                InstallResult r = results[i];
                string icon;
                if (r.Status == InstallStatus.Success) { okCount++; icon = "[OK]"; }
                else if (r.Status == InstallStatus.AlreadyInstalled) { alreadyCount++; icon = "[=]"; }
                else if (r.Status == InstallStatus.Cancelled) { cancelCount++; icon = "[!]"; }
                else { failCount++; icon = "[X]"; }

                sb.AppendLine(string.Format("{0} {1}: {2}", icon, r.App.Name, r.Message));
            }

            string headline = string.Format(
                "Finalizado — Instaladas: {0} | Ya al dia: {1} | Fallidas: {2}",
                okCount, alreadyCount, failCount);

            _lblStatus.Text = headline;
            AppendLog("==================================================");
            AppendLog(headline);

            MessageBox.Show(
                this,
                headline + "\n\n" + sb.ToString(),
                "Resumen de instalacion — Qinstall",
                MessageBoxButtons.OK,
                failCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void OnEngineLog(string message)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action<string>(AppendLog), message); } catch { }
            }
            else
            {
                AppendLog(message);
            }
        }

        private void OnDownloadProgress(int percent)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action<int>(OnDownloadProgress), percent); } catch { }
                return;
            }
            _lblStatus.Text = string.Format("Descargando instalador... {0}%", percent);
        }

        private void AppendLog(string line)
        {
            if (_txtLog == null || _txtLog.IsDisposed) return;
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText("[" + ts + "] " + line + Environment.NewLine);
        }

        private static string GetAppKey(AppItem app)
        {
            return (app.Id ?? app.Name ?? string.Empty) + "|" + (app.Target ?? string.Empty);
        }

        private static Button CreateFlatButton(string text, int width, int height, Color bg)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Size = new Size(width, height);
            btn.BackColor = bg;
            btn.ForeColor = Color.White;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Cursor = Cursors.Hand;
            return btn;
        }
    }
}
