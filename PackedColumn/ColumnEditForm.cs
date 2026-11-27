using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PackingLibrary;

namespace PackedColumn
{
    internal sealed class EditField
    {
        public string Name, Description;
        public double Min, Max, Value;
    }

    internal sealed class ColumnEditForm : Form
    {
        private readonly List<EditField> _fields;
        private readonly Dictionary<EditField, TextBox> _boxes = new Dictionary<EditField, TextBox>();
        private readonly ErrorProvider _err = new ErrorProvider();

        public IList<EditField> Fields { get { return _fields; } }

        private readonly PackingManager _manager;
        private ComboBox _familyBox, _typeBox;
        private DataGridView _infoGrid;

        // The packing chosen by the user (null if none selected)
        public Packing SelectedPacking { get; private set; }

        public ColumnEditForm(string title, List<EditField> fields, string report,
                      PackingManager manager, string family, string packingName)
        {
            _fields = fields;
            _manager = manager;
            Text = title;
            Size = new Size(560, 480);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            TopMost = true;   // the PME gives us no parent window, so avoid hiding behind it

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ---- Parameters tab ----
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoScroll = true,
                Padding = new Padding(10)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            foreach (var f in _fields)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label
                {
                    Text = f.Description,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    MaximumSize = new Size(250, 0),
                    Margin = new Padding(3, 8, 3, 8)
                });
                var tb = new TextBox
                {
                    Text = f.Value.ToString("G6", CultureInfo.CurrentCulture),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right
                };
                _boxes[f] = tb;
                grid.Controls.Add(tb);
            }
            var pageParams = new TabPage("Parameters");
            pageParams.Controls.Add(grid);

            // ---- Packing tab ----
            var pagePacking = new TabPage("Packing");
            var pl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10)
            };
            pl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            pl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            pl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _familyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _typeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            _typeBox.FormattingEnabled = true;
            // items are Packing objects; show their name
            _typeBox.Format += (s, e) =>
            {
                var p = e.ListItem as Packing;
                if (p != null) e.Value = p.Identity.Name;   // adjust if your property differs
            };

            _infoGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _infoGrid.Columns.Add("prop", "Property");
            _infoGrid.Columns.Add("val", "Value");

            pl.Controls.Add(new Label { Text = "Packing family", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, 0);
            pl.Controls.Add(_familyBox, 1, 0);
            pl.Controls.Add(new Label { Text = "Packing type", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, 1);
            pl.Controls.Add(_typeBox, 1, 1);
            pl.Controls.Add(_infoGrid, 0, 2);
            pl.SetColumnSpan(_infoGrid, 2);
            pagePacking.Controls.Add(pl);

            // events first, then initial selection (so the handlers fill everything in)
            _familyBox.SelectedIndexChanged += (s, e) => FillTypes(null);
            _typeBox.SelectedIndexChanged += (s, e) => ShowPackingInfo();

            if (_manager != null)
            {
                foreach (string fam in _manager.GetFamilies()) _familyBox.Items.Add(fam);
                int fi = string.IsNullOrEmpty(family) ? -1 : _familyBox.FindStringExact(family);
                if (fi < 0 && _familyBox.Items.Count > 0) fi = 0;
                _familyBox.SelectedIndex = fi;          // triggers FillTypes
                if (fi >= 0) FillTypes(packingName);    // re-run to restore saved type
            }

            // ---- Results tab (read-only report) ----
            var pageRes = new TabPage("Results");
            pageRes.Controls.Add(new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                Text = report.Replace("\r\n", "\n").Replace("\n", "\r\n")
            });

            tabs.TabPages.Add(pageParams);
            tabs.TabPages.Add(pagePacking);
            tabs.TabPages.Add(pageRes);

            // ---- buttons ----
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(5)
            };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "OK" };
            ok.Click += OnOk;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(tabs);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void FillTypes(string preferredName)
        {
            _typeBox.Items.Clear();
            string fam = _familyBox.SelectedItem as string;
            if (fam == null || _manager == null) { ShowPackingInfo(); return; }

            var packings = _manager.GetPackingsByFamily(fam);
            foreach (Packing p in packings) _typeBox.Items.Add(p);

            int idx = 0;
            if (preferredName != null)
                for (int i = 0; i < packings.Count; i++)
                    if (string.Equals(packings[i].Identity.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }

            if (_typeBox.Items.Count > 0) _typeBox.SelectedIndex = idx;  // triggers ShowPackingInfo
            else ShowPackingInfo();
        }

        private void ShowPackingInfo()
        {
            _infoGrid.Rows.Clear();
            var p = _typeBox.SelectedItem as Packing;
            if (p == null) return;

            var rows = new List<KeyValuePair<string, string>>();
            Flatten(p, "", rows, 0);
            foreach (var r in rows) _infoGrid.Rows.Add(r.Key, r.Value);
        }

        // Lists simple properties, descending into nested objects (e.g. Identity) up to 2 levels
        private static void Flatten(object obj, string prefix, List<KeyValuePair<string, string>> rows, int depth)
        {
            foreach (var prop in obj.GetType().GetProperties())
            {
                if (prop.GetIndexParameters().Length > 0) continue;

                object value;
                try { value = prop.GetValue(obj, null); } catch { continue; }

                string name = prefix + prop.Name;

                // unwrap double? -> double, int? -> int, etc.
                Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (value == null)
                {
                    rows.Add(new KeyValuePair<string, string>(name, "n/a"));
                }
                else if (IsLeaf(t))
                {
                    string text = value is IFormattable f
                        ? f.ToString(null, CultureInfo.InvariantCulture)
                        : Convert.ToString(value, CultureInfo.InvariantCulture);
                    rows.Add(new KeyValuePair<string, string>(name, text));
                }
                else if (value is IEnumerable)
                {
                    continue;                                   // skip collections
                }
                else if (depth < 2)
                {
                    Flatten(value, name + ".", rows, depth + 1);
                }
            }
        }

        private static bool IsLeaf(Type t)
        {
            return t.IsPrimitive || t.IsEnum
                || t == typeof(string) || t == typeof(decimal)
                || t == typeof(DateTime) || t == typeof(Guid);
        }

        private void OnOk(object sender, EventArgs e)
        {
            bool allValid = true;
            var parsed = new Dictionary<EditField, double>();

            foreach (var kv in _boxes)
            {
                EditField f = kv.Key; TextBox tb = kv.Value;
                double v;
                string msg = null;

                if (!double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out v) &&
                    !double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    msg = "Not a valid number.";
                else if (v < f.Min || v > f.Max)
                    msg = string.Format(CultureInfo.CurrentCulture,
                          "Value must be between {0:G6} and {1:G6}.", f.Min, f.Max);

                _err.SetError(tb, msg ?? "");
                if (msg != null) allValid = false; else parsed[f] = v;
            }

            if (!allValid) return;   // stay open so the user can fix the highlighted fields

            foreach (var kv in parsed) kv.Key.Value = kv.Value;
            SelectedPacking = _typeBox.SelectedItem as Packing;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}