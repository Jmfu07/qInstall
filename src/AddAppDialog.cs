using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Qinstall
{
    public class AddAppDialog : Form
    {
        private readonly InstallerEngine _engine;
        private readonly Timer _debounceTimer;
        private int _searchSeq;
        private int _descSeq;
        private string _lastAutoDesc = string.Empty;

        private TextBox _txtName;
        private ComboBox _cmbCategory;
        private TextBox _txtDescription;
        private ComboBox _cmbSource;
        private TextBox _txtTarget;
        private Button _btnBrowse;
        private TextBox _txtArgs;
        private CheckBox _chkEssential;

        private TextBox _txtWingetQuery;
        private Button _btnSearchWinget;
        private ListView _lstResults;
        private Label _lblTargetHint;
        private Label _lblSearchStatus;

        public AppItem CreatedApp { get; private set; }

        public AddAppDialog(InstallerEngine engine, List<string> existingCategories)
        {
            _engine = engine;
            _debounceTimer = new Timer();
            _debounceTimer.Interval = 350;
            _debounceTimer.Tick += DebounceTimer_Tick;

            BuildUi(existingCategories);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BuildUi(List<string> existingCategories)
        {
            this.Text = "Agregar aplicacion a Qinstall";
            this.Size = new Size(640, 610);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 27, 34);
            this.ForeColor = Color.FromArgb(235, 240, 248);
            this.Font = new Font("Segoe UI", 9.5f);

            int pad = 18;
            int y = 16;

            Label lblHeader = new Label();
            lblHeader.Text = "Nueva aplicacion";
            lblHeader.Font = new Font("Segoe UI Semibold", 13f);
            lblHeader.ForeColor = Color.White;
            lblHeader.Location = new Point(pad, y);
            lblHeader.AutoSize = true;
            this.Controls.Add(lblHeader);
            y += 34;

            // Buscador con autocompletado en tiempo real en Winget
            Panel pnlSearch = new Panel();
            pnlSearch.Location = new Point(pad, y);
            pnlSearch.Size = new Size(586, 198);
            pnlSearch.BackColor = Color.FromArgb(32, 36, 46);
            this.Controls.Add(pnlSearch);

            Label lblSearchTitle = new Label();
            lblSearchTitle.Text = "Autocompletar desde Winget — escribe el nombre y haz clic en una sugerencia:";
            lblSearchTitle.Location = new Point(10, 8);
            lblSearchTitle.AutoSize = true;
            lblSearchTitle.ForeColor = Color.FromArgb(170, 185, 210);
            pnlSearch.Controls.Add(lblSearchTitle);

            _txtWingetQuery = CreateTextBox(10, 30, 442);
            _txtWingetQuery.TextChanged += TxtWingetQuery_TextChanged;
            _txtWingetQuery.KeyDown += TxtWingetQuery_KeyDown;
            pnlSearch.Controls.Add(_txtWingetQuery);

            _btnSearchWinget = CreateButton("Buscar", 460, 29, 116, 28, Color.FromArgb(46, 118, 224));
            _btnSearchWinget.Click += BtnSearchWinget_Click;
            pnlSearch.Controls.Add(_btnSearchWinget);

            _lstResults = new ListView();
            _lstResults.Location = new Point(10, 62);
            _lstResults.Size = new Size(566, 108);
            _lstResults.View = View.Details;
            _lstResults.FullRowSelect = true;
            _lstResults.MultiSelect = false;
            _lstResults.GridLines = false;
            _lstResults.BackColor = Color.FromArgb(20, 23, 30);
            _lstResults.ForeColor = Color.FromArgb(230, 236, 245);
            _lstResults.BorderStyle = BorderStyle.FixedSingle;
            _lstResults.Columns.Add("Nombre", 230);
            _lstResults.Columns.Add("ID Winget", 230);
            _lstResults.Columns.Add("Version", 85);
            _lstResults.Click += LstResults_ApplySelection;
            _lstResults.DoubleClick += LstResults_ApplySelection;
            _lstResults.KeyDown += LstResults_KeyDown;
            pnlSearch.Controls.Add(_lstResults);

            _lblSearchStatus = new Label();
            _lblSearchStatus.Text = "Escribe al menos 2 letras para ver sugerencias automaticas...";
            _lblSearchStatus.Location = new Point(10, 175);
            _lblSearchStatus.Size = new Size(566, 18);
            _lblSearchStatus.Font = new Font("Segoe UI", 8.4f);
            _lblSearchStatus.ForeColor = Color.FromArgb(145, 160, 185);
            pnlSearch.Controls.Add(_lblSearchStatus);

            y += 210;

            // Nombre y Categoria (con autocompletado nativo)
            AddLabel("Nombre:", pad, y);
            _txtName = CreateTextBox(pad, y + 20, 280);
            this.Controls.Add(_txtName);

            AddLabel("Categoria:", pad + 298, y);
            _cmbCategory = new ComboBox();
            _cmbCategory.Location = new Point(pad + 298, y + 20);
            _cmbCategory.Size = new Size(288, 26);
            _cmbCategory.BackColor = Color.FromArgb(35, 40, 52);
            _cmbCategory.ForeColor = Color.White;
            _cmbCategory.FlatStyle = FlatStyle.Flat;
            _cmbCategory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _cmbCategory.AutoCompleteSource = AutoCompleteSource.ListItems;
            if (existingCategories != null)
            {
                for (int i = 0; i < existingCategories.Count; i++)
                {
                    _cmbCategory.Items.Add(existingCategories[i]);
                }
            }
            if (_cmbCategory.Items.Count == 0)
            {
                _cmbCategory.Items.Add("Utilidades");
            }
            _cmbCategory.SelectedIndex = 0;
            this.Controls.Add(_cmbCategory);
            y += 56;

            // Descripcion
            AddLabel("Descripcion corta:", pad, y);
            _txtDescription = CreateTextBox(pad, y + 20, 586);
            this.Controls.Add(_txtDescription);
            y += 56;

            // Origen
            AddLabel("Origen:", pad, y);
            _cmbSource = new ComboBox();
            _cmbSource.Location = new Point(pad, y + 20);
            _cmbSource.Size = new Size(175, 26);
            _cmbSource.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbSource.BackColor = Color.FromArgb(35, 40, 52);
            _cmbSource.ForeColor = Color.White;
            _cmbSource.FlatStyle = FlatStyle.Flat;
            _cmbSource.Items.Add("winget");
            _cmbSource.Items.Add("url");
            _cmbSource.Items.Add("local");
            _cmbSource.Items.Add("powershell");
            _cmbSource.SelectedIndex = 0;
            _cmbSource.SelectedIndexChanged += CmbSource_SelectedIndexChanged;
            this.Controls.Add(_cmbSource);

            _lblTargetHint = AddLabel("ID de Winget (ej. 7zip.7zip):", pad + 190, y);
            _txtTarget = CreateTextBox(pad + 190, y + 20, 306);
            this.Controls.Add(_txtTarget);

            _btnBrowse = CreateButton("Examinar", pad + 502, y + 19, 84, 27, Color.FromArgb(58, 66, 84));
            _btnBrowse.Enabled = false;
            _btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(_btnBrowse);
            y += 56;

            // Argumentos silenciosos + Esencial
            AddLabel("Argumentos silenciosos (solo para URL o local, ej. /S o /VERYSILENT):", pad, y);
            _txtArgs = CreateTextBox(pad, y + 20, 396);
            _txtArgs.Enabled = false;
            this.Controls.Add(_txtArgs);

            _chkEssential = new CheckBox();
            _chkEssential.Text = "Marcar como Esencial";
            _chkEssential.Location = new Point(pad + 414, y + 22);
            _chkEssential.AutoSize = true;
            _chkEssential.ForeColor = Color.FromArgb(220, 228, 240);
            this.Controls.Add(_chkEssential);
            y += 62;

            // Botones finales
            Button btnCancel = CreateButton("Cancelar", 406, y, 95, 34, Color.FromArgb(58, 65, 80));
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            Button btnSave = CreateButton("Guardar", 509, y, 95, 34, Color.FromArgb(35, 165, 90));
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
        }

        private void TxtWingetQuery_TextChanged(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            string q = _txtWingetQuery.Text.Trim();
            if (q.Length < 2)
            {
                _searchSeq++;
                _lstResults.Items.Clear();
                _lblSearchStatus.Text = "Escribe al menos 2 letras para ver sugerencias automaticas...";
                return;
            }
            _lblSearchStatus.Text = "Buscando sugerencias...";
            _debounceTimer.Start();
        }

        private void TxtWingetQuery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _debounceTimer.Stop();
                TriggerWingetSearchAsync(_txtWingetQuery.Text.Trim());
            }
            else if (e.KeyCode == Keys.Down && _lstResults.Items.Count > 0)
            {
                e.SuppressKeyPress = true;
                _lstResults.Focus();
                _lstResults.Items[0].Selected = true;
                _lstResults.Items[0].Focused = true;
            }
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            TriggerWingetSearchAsync(_txtWingetQuery.Text.Trim());
        }

        private void BtnSearchWinget_Click(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            TriggerWingetSearchAsync(_txtWingetQuery.Text.Trim());
        }

        private async void TriggerWingetSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return;

            int mySeq = ++_searchSeq;
            _lblSearchStatus.Text = "Buscando '" + query + "' en Winget...";

            List<WingetSearchEntry> results = await _engine.SearchWingetAsync(query, 15);
            if (this.IsDisposed || mySeq != _searchSeq) return;

            _lstResults.BeginUpdate();
            _lstResults.Items.Clear();
            for (int i = 0; i < results.Count; i++)
            {
                WingetSearchEntry r = results[i];
                ListViewItem lvi = new ListViewItem(r.Name);
                lvi.SubItems.Add(r.Id);
                lvi.SubItems.Add(r.Version);
                lvi.Tag = r;
                _lstResults.Items.Add(lvi);
            }
            _lstResults.EndUpdate();

            _lblSearchStatus.Text = results.Count > 0
                ? string.Format("{0} sugerencias encontradas — haz clic o presiona Enter para autocompletar.", results.Count)
                : "Sin resultados para '" + query + "'. Prueba con otro nombre.";
        }

        private void LstResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                LstResults_ApplySelection(sender, EventArgs.Empty);
            }
        }

        private async void LstResults_ApplySelection(object sender, EventArgs e)
        {
            if (_lstResults.SelectedItems.Count == 0) return;
            WingetSearchEntry entry = _lstResults.SelectedItems[0].Tag as WingetSearchEntry;
            if (entry == null) return;

            _txtName.Text = entry.Name;
            _cmbSource.SelectedItem = "winget";
            _txtTarget.Text = entry.Id;

            string suggestedCat = GuessCategory(entry.Name, entry.Id);
            SelectCategoryIfAvailable(suggestedCat);

            string fallbackDesc = "Instalacion online via Winget (" + entry.Id + ")";
            if (string.IsNullOrWhiteSpace(_txtDescription.Text) || _txtDescription.Text == _lastAutoDesc)
            {
                _txtDescription.Text = fallbackDesc;
                _lastAutoDesc = fallbackDesc;
            }

            // Obtener descripcion oficial real de Winget en segundo plano
            int myDescSeq = ++_descSeq;
            string officialDesc = await _engine.GetWingetDescriptionAsync(entry.Id);
            if (this.IsDisposed || myDescSeq != _descSeq) return;

            if (!string.IsNullOrWhiteSpace(officialDesc) &&
                (string.IsNullOrWhiteSpace(_txtDescription.Text) || _txtDescription.Text == _lastAutoDesc))
            {
                _txtDescription.Text = officialDesc;
                _lastAutoDesc = officialDesc;
            }
        }

        private void SelectCategoryIfAvailable(string preferred)
        {
            if (string.IsNullOrEmpty(preferred)) return;
            for (int i = 0; i < _cmbCategory.Items.Count; i++)
            {
                string item = _cmbCategory.Items[i].ToString();
                if (string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbCategory.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string GuessCategory(string name, string id)
        {
            string combined = ((name ?? string.Empty) + " " + (id ?? string.Empty)).ToLowerInvariant();

            if (ContainsAny(combined, "chrome", "firefox", "brave", "opera", "vivaldi", "edge", "browser", "zen", "librewolf"))
                return "Navegadores";
            if (ContainsAny(combined, "gemini", "antigravity", "opencode", "copilot", "chatgpt", "claude", "ollama", "lmstudio", "cursor", "windsurf"))
                return "IA y Agentes";
            if (ContainsAny(combined, "code", "studio", "git", "python", "node", "rust", "golang", "docker", "postman", "dbeaver", "jetbrains", "notepad++", "terminal", "cmake", "mingw"))
                return "Desarrollo";
            if (ContainsAny(combined, "steam", "epic", "ubisoft", "playnite", "heroic", "gog", "minecraft", "roblox", "eaapp", "battlenet"))
                return "Juegos";
            if (ContainsAny(combined, "vlc", "spotify", "obs", "sharex", "gimp", "paint", "audacity", "blender", "handbrake", "mpv", "kdenlive", "ffmpeg", "music", "media"))
                return "Multimedia";
            if (ContainsAny(combined, "discord", "telegram", "whatsapp", "zoom", "slack", "teams", "signal", "skype", "thunderbird"))
                return "Comunicacion";
            if (ContainsAny(combined, "office", "libreoffice", "pdf", "obsidian", "notion", "acrobat", "onlyoffice", "wps", "evernote"))
                return "Oficina";
            if (ContainsAny(combined, "java", "temurin", "jdk", "jre", "dotnet", "vcredist", "directx", "runtime"))
                return "Runtimes y Java";
            if (ContainsAny(combined, "boot", "efi", "uefi", "disk", "partition", "ventoy", "testdisk", "aomei", "minitool", "bcd"))
                return "Arranque y Reparacion";
            if (ContainsAny(combined, "clean", "tweak", "shutup", "optimizer", "uninstaller", "autoruns", "lasso", "windhawk", "revi", "bleachbit"))
                return "Optimizacion";

            return "Utilidades";
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (text.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void CmbSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            string src = _cmbSource.SelectedItem as string;
            if (src == "winget")
            {
                _lblTargetHint.Text = "ID de Winget (ej. 7zip.7zip):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = false;
                _txtTarget.AutoCompleteMode = AutoCompleteMode.None;
            }
            else if (src == "url")
            {
                _lblTargetHint.Text = "URL directa del instalador (.exe o .msi):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = true;
                _txtTarget.AutoCompleteMode = AutoCompleteMode.None;
            }
            else if (src == "powershell")
            {
                _lblTargetHint.Text = "Comando PowerShell (ej. irm 'https://christitus.com/win' | iex):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = false;
                _txtTarget.AutoCompleteMode = AutoCompleteMode.None;
            }
            else
            {
                _lblTargetHint.Text = "Ruta del archivo (relativa al pendrive o completa):";
                _btnBrowse.Enabled = true;
                _txtArgs.Enabled = true;
                _txtTarget.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                _txtTarget.AutoCompleteSource = AutoCompleteSource.FileSystem;
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Seleccionar instalador local";
                ofd.Filter = "Instaladores (*.exe;*.msi)|*.exe;*.msi|Todos los archivos (*.*)|*.*";
                ofd.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string selected = ofd.FileName;
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (selected.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = selected.Substring(baseDir.Length);
                    }
                    _txtTarget.Text = selected;
                    if (string.IsNullOrWhiteSpace(_txtName.Text))
                    {
                        _txtName.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                MessageBox.Show(this, "Ingresa el nombre de la aplicacion.", "Campo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtTarget.Text))
            {
                MessageBox.Show(this, "Ingresa el ID de Winget, URL, ruta local o comando.", "Campo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppItem item = new AppItem();
            item.Id = _txtName.Text.Trim().ToLowerInvariant().Replace(" ", "_");
            item.Name = _txtName.Text.Trim();
            item.Category = string.IsNullOrWhiteSpace(_cmbCategory.Text) ? "Otros" : _cmbCategory.Text.Trim();
            item.Description = _txtDescription.Text.Trim();
            item.Source = _cmbSource.SelectedItem as string;
            item.Target = _txtTarget.Text.Trim();
            item.SilentArgs = _txtArgs.Text.Trim();
            item.Essential = _chkEssential.Checked;

            CreatedApp = item;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            lbl.ForeColor = Color.FromArgb(180, 192, 212);
            this.Controls.Add(lbl);
            return lbl;
        }

        private static TextBox CreateTextBox(int x, int y, int width)
        {
            TextBox tb = new TextBox();
            tb.Location = new Point(x, y);
            tb.Size = new Size(width, 26);
            tb.BackColor = Color.FromArgb(35, 40, 52);
            tb.ForeColor = Color.White;
            tb.BorderStyle = BorderStyle.FixedSingle;
            return tb;
        }

        private static Button CreateButton(string text, int x, int y, int width, int height, Color bg)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, y);
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
