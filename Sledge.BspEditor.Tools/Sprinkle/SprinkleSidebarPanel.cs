using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Translations;
using Sledge.Common.Transport;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Sprinkle
{
    [Export(typeof(ISidebarComponent))]
    [OrderHint("U")]
    [AutoTranslate]
    public partial class SprinkleSidebarPanel : UserControl, ISidebarComponent
    {
        public string Title { get; set; } = "Entity Sprinkle";
        public object Control => this;

        [Import] private SprinkleTool _tool;
        [Import] private SerialisedObjectFormatter _formatter;

        private ComboBox _typeCombo;
        private TrackBar _densitySlider;
        private NumericUpDown _radiusNum;
        private CheckBox _randomYaw;
        private Button _btnRefresh;

        public SprinkleSidebarPanel()
        {
            InitializeComponent();

            Oy.Subscribe<float>("SprinkleTool:RadiusChanged", r => {
                this.InvokeLater(() => {
                    if (_radiusNum != null && _radiusNum.Value != (decimal)r)
                    {
                        _radiusNum.Value = (decimal)Math.Clamp(r, (float)_radiusNum.Minimum, (float)_radiusNum.Maximum);
                    }
                });
            });
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            var lblType = new Label { Text = "Type:", Dock = DockStyle.Top, Height = 15 };
            _typeCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 10) };
            _typeCombo.SelectedIndexChanged += (s, e) => {
                if (_typeCombo.SelectedItem is SprinkleDefinition def) _tool.ActiveDefinition = def;
            };

            _btnRefresh = new Button { Text = "Refresh Definitions", Dock = DockStyle.Top, Height = 25 };
            _btnRefresh.Click += (s, e) => RefreshDefinitions();

            var lblDensity = new Label { Text = "Density:", Dock = DockStyle.Top, Height = 15, Margin = new Padding(0, 10, 0, 0) };
            _densitySlider = new TrackBar { Minimum = 1, Maximum = 100, Value = 25, Dock = DockStyle.Top, TickFrequency = 10 };
            _densitySlider.ValueChanged += (s, e) => _tool.Density = _densitySlider.Value / 100f;

            var lblRadius = new Label { Text = "Radius:", Dock = DockStyle.Top, Height = 15 };
            _radiusNum = new NumericUpDown { Minimum = 16, Maximum = 4096, Value = 256, Dock = DockStyle.Top, Increment = 32 };
            _radiusNum.ValueChanged += (s, e) => _tool.Radius = (float)_radiusNum.Value;

            _randomYaw = new CheckBox { Text = "Random Yaw", Checked = true, Dock = DockStyle.Top, Height = 25 };
            _randomYaw.CheckedChanged += (s, e) => _tool.RandomYaw = _randomYaw.Checked;

            Controls.Add(_randomYaw);
            Controls.Add(_radiusNum);
            Controls.Add(lblRadius);
            Controls.Add(_densitySlider);
            Controls.Add(lblDensity);
            Controls.Add(_btnRefresh);
            Controls.Add(_typeCombo);
            Controls.Add(lblType);

            Padding = new Padding(5);
            Height = 220;

            this.ResumeLayout(false);

            this.Load += (s, e) => RefreshDefinitions();
        }

        private void RefreshDefinitions()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "sprinkle");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                return;
            }

            _typeCombo.BeginUpdate();
            _typeCombo.Items.Clear();

            foreach (var file in Directory.GetFiles(path, "*.txt"))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var objects = _formatter.Deserialize(stream);
                        foreach (var so in objects)
                        {
                            var def = new SprinkleDefinition(so);
                            _typeCombo.Items.Add(def);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading sprinkle script {file}: {ex.Message}");
                }
            }

            if (_typeCombo.Items.Count > 0)
            {
                _typeCombo.SelectedIndex = 0;
            }
            _typeCombo.EndUpdate();
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out SprinkleTool _);
        }
    }
    public partial class SprinkleDefinition
    {
        public override string ToString() => Name;
    }
}