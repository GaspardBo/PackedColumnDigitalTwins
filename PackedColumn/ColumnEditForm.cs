using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Security.Policy;
using System.Windows.Forms;

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

        public ColumnEditForm(string title, List<EditField> fields, string report)
        {
            _fields = fields;
            Text = title;
            Size = new Size(520, 420);
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
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}