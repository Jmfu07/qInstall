using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Qinstall
{
    public class AddAppDialog : Form
    {
        private TextBox _txtFilePath;
        private Button _btnBrowse;
        private CheckBox _chkCopyToLocal;
        private TextBox _txtName;
        private ComboBox _cmbCategory;
        private TextBox _txtDescription;
        private ComboBox _cmbArgs;
        private CheckBox _chkEssential;

        public AppItem CreatedApp { get; private set; }

        public AddAppDialog(InstallerEngine engine, List<string> existingCategories)
        {
            BuildUi(existingCategories);
        }

        private void BuildUi(List<string> existingCategories)
        {
            this.Text = "Agregar instalador local (.exe / .msi)";
            this.Size = new Size(580, 430);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(24, 27, 34);
            this.ForeColor = Color.FromArgb(235, 240, 248);
            this.Font = new Font("Segoe UI", 9.5f);

            int pad = 20;
            int y = 16;

            Label lblHeader = new Label();
            lblHeader.Text = "Agregar instalador local (Offline)";
            lblHeader.Font = new Font("Segoe UI Semibold", 13f);
            lblHeader.ForeColor = Color.White;
            lblHeader.Location = new Point(pad, y);
            lblHeader.AutoSize = true;
            this.Controls.Add(lblHeader);
            y += 28;

            Label lblSub = new Label();
            lblSub.Text = "Ideal para programas que deseas llevar en tu pendrive y usar en PCs sin Internet.";
            lblSub.Font = new Font("Segoe UI", 8.8f);
            lblSub.ForeColor = Color.FromArgb(150, 165, 190);
            lblSub.Location = new Point(pad, y);
            lblSub.AutoSize = true;
            this.Controls.Add(lblSub);
            y += 28;

            // Archivo .exe / .msi
            AddLabel("Archivo instalador (.exe o .msi):", pad, y);
            _txtFilePath = CreateTextBox(pad, y + 20, 430);
            _txtFilePath.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _txtFilePath.AutoCompleteSource = AutoCompleteSource.FileSystem;
            this.Controls.Add(_txtFilePath);

            _btnBrowse = CreateButton("Examinar", pad + 438, y + 19, 86, 27, Color.FromArgb(46, 125, 246));
            _btnBrowse.Click += BtnBrowse_Click;
            this.Controls.Add(_btnBrowse);
            y += 52;

            _chkCopyToLocal = new CheckBox();
            _chkCopyToLocal.Text = "Copiar archivo a la carpeta 'local\\' de Qinstall (recomendado para pendrive)";
            _chkCopyToLocal.Checked = true;
            _chkCopyToLocal.AutoSize = true;
            _chkCopyToLocal.Location = new Point(pad, y);
            _chkCopyToLocal.ForeColor = Color.FromArgb(155, 235, 185);
            this.Controls.Add(_chkCopyToLocal);
            y += 32;

            // Nombre y Categoria
            AddLabel("Nombre para mostrar:", pad, y);
            _txtName = CreateTextBox(pad, y + 20, 250);
            this.Controls.Add(_txtName);

            AddLabel("Categoria:", pad + 268, y);
            _cmbCategory = new ComboBox();
            _cmbCategory.Location = new Point(pad + 268, y + 20);
            _cmbCategory.Size = new Size(256, 26);
            _cmbCategory.BackColor = Color.FromArgb(35, 40, 52);
            _cmbCategory.ForeColor = Color.White;
            _cmbCategory.FlatStyle = FlatStyle.Flat;
            _cmbCategory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _cmbCategory.AutoCompleteSource = AutoCompleteSource.ListItems;
            _cmbCategory.Items.Add("Instaladores Locales");
            if (existingCategories != null)
            {
                for (int i = 0; i < existingCategories.Count; i++)
                {
                    if (!string.Equals(existingCategories[i], "Instaladores Locales", StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbCategory.Items.Add(existingCategories[i]);
                    }
                }
            }
            _cmbCategory.SelectedIndex = 0;
            this.Controls.Add(_cmbCategory);
            y += 56;

            // Descripcion
            AddLabel("Descripcion corta (opcional):", pad, y);
            _txtDescription = CreateTextBox(pad, y + 20, 524);
            this.Controls.Add(_txtDescription);
            y += 56;

            // Argumentos silenciosos + Esencial
            AddLabel("Parametros silenciosos (opcional, ej. /S, /VERYSILENT o vacio):", pad, y);
            _cmbArgs = new ComboBox();
            _cmbArgs.Location = new Point(pad, y + 20);
            _cmbArgs.Size = new Size(320, 26);
            _cmbArgs.BackColor = Color.FromArgb(35, 40, 52);
            _cmbArgs.ForeColor = Color.White;
            _cmbArgs.FlatStyle = FlatStyle.Flat;
            _cmbArgs.Items.Add("");
            _cmbArgs.Items.Add("/S");
            _cmbArgs.Items.Add("/VERYSILENT /NORESTART");
            _cmbArgs.Items.Add("/qn /norestart");
            _cmbArgs.Items.Add("/quiet /norestart");
            _cmbArgs.SelectedIndex = 0;
            this.Controls.Add(_cmbArgs);

            _chkEssential = new CheckBox();
            _chkEssential.Text = "Marcar como Esencial";
            _chkEssential.Location = new Point(pad + 340, y + 22);
            _chkEssential.AutoSize = true;
            _chkEssential.ForeColor = Color.FromArgb(220, 228, 240);
            this.Controls.Add(_chkEssential);
            y += 64;

            // Botones finales
            Button btnCancel = CreateButton("Cancelar", 346, y, 95, 34, Color.FromArgb(58, 65, 80));
            btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            Button btnSave = CreateButton("Agregar", 449, y, 95, 34, Color.FromArgb(35, 165, 90));
            btnSave.Click += BtnSave_Click;
            this.Controls.Add(btnSave);
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Seleccionar instalador local (.exe o .msi)";
                ofd.Filter = "Instaladores (*.exe;*.msi)|*.exe;*.msi|Todos los archivos (*.*)|*.*";
                string localDir = AppCatalog.GetLocalInstallersDirectory();
                ofd.InitialDirectory = Directory.Exists(localDir) ? localDir : AppCatalog.GetAppBaseDirectory();

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtFilePath.Text = ofd.FileName;
                    if (string.IsNullOrWhiteSpace(_txtName.Text))
                    {
                        _txtName.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
                    }
                    if (Path.GetExtension(ofd.FileName).Equals(".msi", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(_cmbArgs.Text))
                    {
                        _cmbArgs.Text = "/qn /norestart";
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string rawPath = (_txtFilePath.Text ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                MessageBox.Show(this, "Selecciona un archivo instalador (.exe o .msi).", "Archivo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string baseDir = AppCatalog.GetAppBaseDirectory();
            string resolvedSource = Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(baseDir, rawPath);
            if (!File.Exists(resolvedSource))
            {
                MessageBox.Show(this, "No se encontro el archivo:\n" + resolvedSource, "Archivo no encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = string.IsNullOrWhiteSpace(_txtName.Text)
                ? Path.GetFileNameWithoutExtension(resolvedSource)
                : _txtName.Text.Trim();

            string finalTarget;
            if (_chkCopyToLocal.Checked)
            {
                try
                {
                    string localDir = AppCatalog.GetLocalInstallersDirectory();
                    Directory.CreateDirectory(localDir);
                    string fileName = Path.GetFileName(resolvedSource);
                    string destPath = Path.Combine(localDir, fileName);

                    if (!string.Equals(Path.GetFullPath(resolvedSource), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(resolvedSource, destPath, true);
                    }
                    finalTarget = Path.Combine("local", fileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo copiar el instalador a la carpeta 'local\\':\n" + ex.Message, "Error al copiar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                if (resolvedSource.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    finalTarget = resolvedSource.Substring(baseDir.Length).TrimStart('\\', '/');
                }
                else
                {
                    finalTarget = resolvedSource;
                }
            }

            AppItem item = new AppItem();
            item.Id = "local_" + name.ToLowerInvariant().Replace(" ", "_");
            item.Name = name;
            item.Category = string.IsNullOrWhiteSpace(_cmbCategory.Text) ? "Instaladores Locales" : _cmbCategory.Text.Trim();
            item.Description = string.IsNullOrWhiteSpace(_txtDescription.Text)
                ? "Instalador local offline (" + Path.GetFileName(finalTarget) + ")"
                : _txtDescription.Text.Trim();
            item.Source = "local";
            item.Target = finalTarget;
            item.SilentArgs = (_cmbArgs.Text ?? string.Empty).Trim();
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
