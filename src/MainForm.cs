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
        private readonly Dictionary<string, bool> _selectedState = new Dictionary<string, bool>();
        private bool _isInstalling;
        private bool _wingetAvailable;
        private bool _logVisible = true;

        // UI Controls
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

            BuildUi();
            LoadCatalogAndRender();
            CheckEnvironmentAsync();
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
            _txtSearch.Size = new Size(240, 26);
            _txtSearch.BackColor = _bgControl;
            _txtSearch.ForeColor = Color.White;
            _txtSearch.BorderStyle = BorderStyle.FixedSingle;
            _txtSearch.TextChanged += delegate { RenderCategories(_txtSearch.Text); };
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

            Button btnAddApp = CreateFlatButton("+ Agregar app", 112, 30, Color.FromArgb(46, 125, 246));
            btnAddApp.Margin = new Padding(0, 0, 8, 0);
            btnAddApp.Click += BtnAddApp_Click;
            pnlActions.Controls.Add(btnAddApp);

            Button btnEditJson = CreateFlatButton("apps.json", 84, 30, _bgControl);
            btnEditJson.Margin = new Padding(0, 0, 8, 0);
            btnEditJson.Click += BtnEditJson_Click;
            pnlActions.Controls.Add(btnEditJson);

            Button btnReload = CreateFlatButton("Recargar", 80, 30, _bgControl);
            btnReload.Click += delegate { LoadCatalogAndRender(); AppendLog("[*] Catalogo recargado desde apps.json."); };
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

            _btnToggleLog = CreateFlatButton("Ocultar consola", 118, 26, _bgControl);
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

            // 4. LOG PANEL (encima del footer)
            _pnlLogContainer = new Panel();
            _pnlLogContainer.Dock = DockStyle.Bottom;
            _pnlLogContainer.Height = 135;
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

            // Asegurar orden correcto de Docking (Fill al frente)
            _flowCategories.BringToFront();
        }

        private async void CheckEnvironmentAsync()
        {
            string version = await Task.Run(delegate { return InstallerEngine.GetWingetVersion(); });
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
                _btnInstallWinget.Visible = true;
                AppendLog("[!] Winget no esta disponible en este equipo. Puedes instalarlo con el boton 'Instalar Winget' o usar apps por URL/Local.");
            }
        }

        private void LoadCatalogAndRender()
        {
            _catalog = AppCatalog.LoadOrCreateDefault();
            RenderCategories(_txtSearch != null ? _txtSearch.Text : string.Empty);
        }

        private void RenderCategories(string filterText)
        {
            _flowCategories.SuspendLayout();
            _flowCategories.Controls.Clear();
            _appCheckboxes.Clear();

            string query = (filterText ?? string.Empty).Trim();

            Dictionary<string, List<AppItem>> grouped = new Dictionary<string, List<AppItem>>(StringComparer.OrdinalIgnoreCase);
            List<string> categoryOrder = new List<string>();

            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                AppItem app = _catalog.Apps[i];
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
                Panel card = CreateCategoryCard(catName, apps);
                _flowCategories.Controls.Add(card);
            }

            _flowCategories.ResumeLayout();
            UpdateSelectedCounter();
        }

        private Panel CreateCategoryCard(string categoryName, List<AppItem> apps)
        {
            int cardWidth = 315;
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
            accent.BackColor = _accentBlue;
            card.Controls.Add(accent);

            Label lblCat = new Label();
            lblCat.Text = categoryName.ToUpperInvariant();
            lblCat.Font = new Font("Segoe UI Semibold", 9.5f);
            lblCat.ForeColor = Color.FromArgb(140, 185, 255);
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
                cb.Size = new Size(220, 24);
                cb.ForeColor = _textPrimary;
                cb.Font = new Font("Segoe UI", 9.3f);
                cb.Cursor = Cursors.Hand;
                bool prevChecked;
                if (_selectedState.TryGetValue(key, out prevChecked))
                {
                    cb.Checked = prevChecked;
                }
                cb.CheckedChanged += delegate(object s, EventArgs ev)
                {
                    CheckBox box = (CheckBox)s;
                    AppItem item = (AppItem)box.Tag;
                    _selectedState[GetAppKey(item)] = box.Checked;
                    UpdateSelectedCounter();
                };

                string tip = string.IsNullOrWhiteSpace(app.Description)
                    ? app.Target
                    : app.Description + "\nOrigen: " + app.Source.ToUpperInvariant() + " (" + app.Target + ")";
                _toolTip.SetToolTip(cb, tip);

                card.Controls.Add(cb);
                _appCheckboxes[key] = cb;

                // Etiqueta de origen (WINGET / URL / LOCAL)
                Label lblTag = new Label();
                string src = (app.Source ?? "winget").ToUpperInvariant();
                lblTag.Text = src == "WINGET" ? "ONLINE" : src;
                lblTag.Font = new Font("Segoe UI Semibold", 7.2f);
                lblTag.TextAlign = ContentAlignment.MiddleCenter;
                lblTag.Size = new Size(56, 18);
                lblTag.Location = new Point(cardWidth - 68, y + 3);

                if (src == "WINGET")
                {
                    lblTag.BackColor = Color.FromArgb(30, 56, 95);
                    lblTag.ForeColor = Color.FromArgb(145, 195, 255);
                }
                else if (src == "URL")
                {
                    lblTag.BackColor = Color.FromArgb(75, 52, 95);
                    lblTag.ForeColor = Color.FromArgb(215, 175, 255);
                }
                else
                {
                    lblTag.BackColor = Color.FromArgb(36, 80, 56);
                    lblTag.ForeColor = Color.FromArgb(155, 235, 185);
                }

                card.Controls.Add(lblTag);
                y += rowHeight;
            }

            return card;
        }

        private List<AppItem> GetSelectedApps()
        {
            List<AppItem> selected = new List<AppItem>();
            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                AppItem app = _catalog.Apps[i];
                bool isSel;
                if (_selectedState.TryGetValue(GetAppKey(app), out isSel) && isSel)
                {
                    selected.Add(app);
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
            for (int i = 0; i < _catalog.Apps.Count; i++)
            {
                AppItem app = _catalog.Apps[i];
                _selectedState[GetAppKey(app)] = app.Essential;
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
                    RenderCategories(_txtSearch.Text);
                    AppendLog("[+] Aplicacion agregada y guardada en apps.json: " + dlg.CreatedApp.Name);
                }
            }
        }

        private void BtnEditJson_Click(object sender, EventArgs e)
        {
            string path = AppCatalog.GetCatalogPath();
            if (!File.Exists(path))
            {
                AppCatalog.Save(_catalog);
            }
            try
            {
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir apps.json: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                _engine.CancelCurrent();
                _btnInstall.Enabled = false;
                _lblStatus.Text = "Cancelando instalacion...";
                return;
            }

            List<AppItem> selected = GetSelectedApps();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos una aplicacion para instalar.", "Qinstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
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

                if (res.Status == InstallStatus.Cancelled)
                {
                    break;
                }

                _progressBar.Value = (i + 1) * 100;
            }

            _isInstalling = false;
            SetUiBusyState(false);

            ShowSummary(results);
        }

        private void SetUiBusyState(bool busy)
        {
            _flowCategories.Enabled = !busy;
            _txtSearch.Enabled = !busy;
            _chkSilent.Enabled = !busy;
            _chkUpgrade.Enabled = !busy;

            if (busy)
            {
                _btnInstall.Text = "Cancelar";
                _btnInstall.BackColor = _accentRed;
                _btnInstall.Enabled = true;
            }
            else
            {
                _btnInstall.Text = "Instalar seleccionadas";
                _btnInstall.BackColor = _accentGreen;
                _btnInstall.Enabled = true;
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
