using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using static Sledge.BspEditor.Tools.Displacement.DisplacementTool;

namespace Sledge.BspEditor.Tools.Displacement
{
    [Export(typeof(ISidebarComponent))]
    [OrderHint("K")]
    public class DisplacementSidebarPanel : UserControl, ISidebarComponent
    {
        public string Title => "Displacements";
        public object Control => this;

        [Import] private DisplacementTool _tool;

        private ComboBox _powerCombo;
        private ComboBox _modeCombo;
        private Button _btnCreate;
        private Button _btnDestroy;
        private CheckBox _chkPaint;
        private NumericUpDown _numRadius;
        private NumericUpDown _numAmount;
        private ComboBox _axisCombo;

        public DisplacementSidebarPanel()
        {
            InitializeComponent();
            Oy.Subscribe<DisplacementTool>("DisplacementTool:FaceSelected", t => UpdateState());
        }

        private void InitializeComponent()
        {
            var lblPower = new Label { Text = "Power:", Top = 10, Left = 10, Width = 50 };
            _powerCombo = new ComboBox { Top = 10, Left = 60, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _powerCombo.Items.AddRange(new object[] { "1", "2", "3", "4", "5" });
            _powerCombo.SelectedIndex = 1;

            _btnCreate = new Button { Text = "Create", Top = 40, Left = 10, Width = 80 };
            _btnCreate.Click += BtnCreate_Click;

            _btnDestroy = new Button { Text = "Destroy", Top = 40, Left = 100, Width = 80 };
            _btnDestroy.Click += BtnDestroy_Click;

            _chkPaint = new CheckBox { Text = "Paint Mode", Top = 80, Left = 10, Width = 120 };
            _chkPaint.CheckedChanged += (s, e) => { if (_tool != null) _tool.IsPainting = _chkPaint.Checked; };

            var lblMode = new Label { Text = "Mode:", Top = 110, Left = 10, Width = 50 };
            _modeCombo = new ComboBox { Top = 110, Left = 60, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _modeCombo.Items.AddRange(Enum.GetNames(typeof(DisplacementSculptMode)));
            _modeCombo.SelectedIndex = 0;
            _modeCombo.SelectedIndexChanged += (s, e) => { if (_tool != null) _tool.SculptMode = (DisplacementSculptMode)_modeCombo.SelectedIndex; };

            var lblRadius = new Label { Text = "Radius:", Top = 140, Left = 10, Width = 50 };
            _numRadius = new NumericUpDown { Top = 140, Left = 60, Width = 120, Minimum = 1, Maximum = 4096, Value = 64 };
            _numRadius.ValueChanged += (s, e) => { if (_tool != null) _tool.PaintRadius = (int)_numRadius.Value; };

            var lblAmount = new Label { Text = "Amt/Dist:", Top = 170, Left = 10, Width = 60 };
            _numAmount = new NumericUpDown { Top = 170, Left = 70, Width = 110, Minimum = -8192, Maximum = 8192, Value = 5 };
            _numAmount.ValueChanged += (s, e) => { if (_tool != null) _tool.PaintAmount = (float)_numAmount.Value; };

            var lblAxis = new Label { Text = "Axis:", Top = 200, Left = 10, Width = 50 };
            _axisCombo = new ComboBox { Top = 200, Left = 60, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _axisCombo.Items.AddRange(Enum.GetNames(typeof(DisplacementPaintAxis)));
            _axisCombo.SelectedIndex = 0;
            _axisCombo.SelectedIndexChanged += (s, e) => { if (_tool != null) _tool.PaintAxis = (DisplacementPaintAxis)_axisCombo.SelectedIndex; };

            Controls.Add(lblPower); Controls.Add(_powerCombo);
            Controls.Add(_btnCreate); Controls.Add(_btnDestroy);
            Controls.Add(_chkPaint);
            Controls.Add(lblMode); Controls.Add(_modeCombo);
            Controls.Add(lblRadius); Controls.Add(_numRadius);
            Controls.Add(lblAmount); Controls.Add(_numAmount);
            Controls.Add(lblAxis); Controls.Add(_axisCombo);

            Height = 240;
            UpdateState();
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out DisplacementTool _);
        }

        private void UpdateState()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateState));
                return;
            }

            bool hasFace = _tool?.SelectedFace != null;
            bool hasDisp = hasFace && _tool.SelectedFace.Displacement != null;

            _btnCreate.Enabled = hasFace && !hasDisp && _tool.SelectedFace.Vertices.Count >= 4;
            _btnDestroy.Enabled = hasDisp;
            _chkPaint.Enabled = hasDisp;
            _modeCombo.Enabled = hasDisp;
            _numRadius.Enabled = hasDisp;
            _numAmount.Enabled = hasDisp;
            _axisCombo.Enabled = hasDisp;

            if (!_chkPaint.Enabled)
            {
                _chkPaint.Checked = false;
                if (_tool != null) _tool.IsPainting = false;
            }
        }

        private void BtnCreate_Click(object sender, EventArgs e)
        {
            if (_tool.SelectedFace == null || _tool.SelectedSolid == null)
                return;

            var clone = (Face)_tool.SelectedFace.Clone();
            clone.Displacement = new Primitives.MapObjectData.Displacement(_powerCombo.SelectedIndex + 2, _tool.SelectedFace.Vertices.ToArray());

            MapDocumentOperation.Perform(_tool.GetDocument(), new Transaction(
                new RemoveMapObjectData(_tool.SelectedSolid.ID, _tool.SelectedFace),
                new AddMapObjectData(_tool.SelectedSolid.ID, clone)
            ));
            _tool.SelectedFace = clone;
            UpdateState();
        }

        private void BtnDestroy_Click(object sender, EventArgs e)
        {
            if (_tool.SelectedFace == null || _tool.SelectedSolid == null)
                return;

            var clone = (Face)_tool.SelectedFace.Clone();
            clone.Displacement = null;

            MapDocumentOperation.Perform(_tool.GetDocument(), new Transaction(
                new RemoveMapObjectData(_tool.SelectedSolid.ID, _tool.SelectedFace),
                new AddMapObjectData(_tool.SelectedSolid.ID, clone)
            ));
            _tool.SelectedFace = clone;
            UpdateState();
        }
    }
}