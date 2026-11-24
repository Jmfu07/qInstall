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
            BuildUi(existingCategories);
        }

        private void BuildUi(List<string> existingCategories)
        {
            this.Text = "Agregar aplicacion a Qinstall";
            this.Size = new Size(620, 560);
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

            // Buscador rapido en Winget
            Panel pnlSearch = new Panel();
            pnlSearch.Location = new Point(pad, y);
            pnlSearch.Size = new Size(566, 155);
            pnlSearch.BackColor = Color.FromArgb(32, 36, 46);
            this.Controls.Add(pnlSearch);

            Label lblSearchTitle = new Label();
            lblSearchTitle.Text = "Buscar en Winget (online) — haz doble clic en un resultado para rellenar:";
            lblSearchTitle.Location = new Point(10, 8);
            lblSearchTitle.AutoSize = true;
            lblSearchTitle.ForeColor = Color.FromArgb(170, 185, 210);
            pnlSearch.Controls.Add(lblSearchTitle);

            _txtWingetQuery = CreateTextBox(10, 30, 420);
            pnlSearch.Controls.Add(_txtWingetQuery);

            _btnSearchWinget = CreateButton("Buscar", 438, 29, 118, 28, Color.FromArgb(46, 118, 224));
            _btnSearchWinget.Click += BtnSearchWinget_Click;
            pnlSearch.Controls.Add(_btnSearchWinget);

            _lblSearchStatus = new Label();
            _lblSearchStatus.Text = string.Empty;
            _lblSearchStatus.Location = new Point(10, 134);
            _lblSearchStatus.Size = new Size(540, 18);
            _lblSearchStatus.Font = new Font("Segoe UI", 8.5f);
            _lblSearchStatus.ForeColor = Color.FromArgb(145, 160, 185);
            pnlSearch.Controls.Add(_lblSearchStatus);

            _lstResults = new ListView();
            _lstResults.Location = new Point(10, 62);
            _lstResults.Size = new Size(546, 70);
            _lstResults.View = View.Details;
            _lstResults.FullRowSelect = true;
            _lstResults.GridLines = false;
            _lstResults.BackColor = Color.FromArgb(20, 23, 30);
            _lstResults.ForeColor = Color.FromArgb(230, 236, 245);
            _lstResults.BorderStyle = BorderStyle.FixedSingle;
            _lstResults.Columns.Add("Nombre", 220);
            _lstResults.Columns.Add("ID Winget", 220);
            _lstResults.Columns.Add("Version", 85);
            _lstResults.DoubleClick += LstResults_DoubleClick;
            pnlSearch.Controls.Add(_lstResults);

            y += 168;

            // Nombre y Categoria
            AddLabel("Nombre:", pad, y);
            _txtName = CreateTextBox(pad, y + 20, 270);
            this.Controls.Add(_txtName);

            AddLabel("Categoria:", pad + 290, y);
            _cmbCategory = new ComboBox();
            _cmbCategory.Location = new Point(pad + 290, y + 20);
            _cmbCategory.Size = new Size(276, 26);
            _cmbCategory.BackColor = Color.FromArgb(35, 40, 52);
            _cmbCategory.ForeColor = Color.White;
            _cmbCategory.FlatStyle = FlatStyle.Flat;
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
            _txtDescription = CreateTextBox(pad, y + 20, 566);
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
            _txtTarget = CreateTextBox(pad + 190, y + 20, 286);
            this.Controls.Add(_txtTarget);

            _btnBrowse = CreateButton("Examinar", pad + 482, y + 19, 84, 27, Color.FromArgb(58, 66, 84));
            _btnBrowse.Enabled = false;
            _btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(_btnBrowse);
            y += 56;

            // Argumentos silenciosos + Esencial
            AddLabel("Argumentos silenciosos (solo para URL o local, ej. /S o /VERYSILENT):", pad, y);
            _txtArgs = CreateTextBox(pad, y + 20, 380);
            this.Controls.Add(_txtArgs);

            _chkEssential = new CheckBox();
            _chkEssential.Text = "Marcar como Esencial";
            _chkEssential.Location = new Point(pad + 400, y + 22);
            _chkEssential.AutoSize = true;
            _chkEssential.ForeColor = Color.FromArgb(220, 228, 240);
            this.Controls.Add(_chkEssential);
            y += 62;

            // Botones finales
            Button btnCancel = CreateButton("Cancelar", 386, y, 95, 34, Color.FromArgb(58, 65, 80));
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            Button btnSave = CreateButton("Guardar", 489, y, 95, 34, Color.FromArgb(35, 165, 90));
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
        }

        private async void BtnSearchWinget_Click(object sender, EventArgs e)
        {
            string query = _txtWingetQuery.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            _btnSearchWinget.Enabled = false;
            _lblSearchStatus.Text = "Buscando en Winget...";
            _lstResults.Items.Clear();

            List<WingetSearchEntry> results = await _engine.SearchWingetAsync(query);

            for (int i = 0; i < results.Count; i++)
            {
                WingetSearchEntry r = results[i];
                ListViewItem lvi = new ListViewItem(r.Name);
                lvi.SubItems.Add(r.Id);
                lvi.SubItems.Add(r.Version);
                lvi.Tag = r;
                _lstResults.Items.Add(lvi);
            }

            _lblSearchStatus.Text = results.Count > 0
                ? string.Format("{0} resultados encontrados. Doble clic para seleccionar.", results.Count)
                : "Sin resultados. Verifica el nombre o que Winget este disponible.";
            _btnSearchWinget.Enabled = true;
        }

        private void LstResults_DoubleClick(object sender, EventArgs e)
        {
            if (_lstResults.SelectedItems.Count == 0) return;
            WingetSearchEntry entry = _lstResults.SelectedItems[0].Tag as WingetSearchEntry;
            if (entry == null) return;

            _txtName.Text = entry.Name;
            _cmbSource.SelectedItem = "winget";
            _txtTarget.Text = entry.Id;
            if (string.IsNullOrWhiteSpace(_txtDescription.Text))
            {
                _txtDescription.Text = "Instalacion online via Winget (" + entry.Id + ")";
            }
        }

        private void CmbSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            string src = _cmbSource.SelectedItem as string;
            if (src == "winget")
            {
                _lblTargetHint.Text = "ID de Winget (ej. 7zip.7zip):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = false;
            }
            else if (src == "url")
            {
                _lblTargetHint.Text = "URL directa del instalador (.exe o .msi):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = true;
            }
            else if (src == "powershell")
            {
                _lblTargetHint.Text = "Comando PowerShell (ej. irm 'https://christitus.com/win' | iex):";
                _btnBrowse.Enabled = false;
                _txtArgs.Enabled = false;
            }
            else
            {
                _lblTargetHint.Text = "Ruta del archivo (relativa al pendrive o completa):";
                _btnBrowse.Enabled = true;
                _txtArgs.Enabled = true;
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
                MessageBox.Show(this, "Ingresa el ID de Winget, URL o ruta local.", "Campo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
