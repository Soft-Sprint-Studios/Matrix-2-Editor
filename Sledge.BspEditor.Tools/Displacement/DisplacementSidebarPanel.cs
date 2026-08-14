using System;
using System.Collections.Generic;
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
        private Button _btnSew;
        private Button _btnInvertAlpha;
        private CheckBox _chkPaint;
        private NumericUpDown _numRadius;
        private NumericUpDown _numAmount;
        private ComboBox _axisCombo;
        private TextBox _txtTexture2;
        private Button _btnBrowseTexture2;
        private NumericUpDown _numNoiseMin;
        private NumericUpDown _numNoiseMax;
        private Button _btnNoise;

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

            _btnCreate = new Button { Text = "Create", Top = 40, Left = 5, Width = 55, Height = 25 };
            _btnCreate.Click += BtnCreate_Click;

            _btnDestroy = new Button { Text = "Destroy", Top = 40, Left = 65, Width = 65, Height = 25 };
            _btnDestroy.Click += BtnDestroy_Click;

            _btnSew = new Button { Text = "Sew", Top = 40, Left = 135, Width = 45, Height = 25 };
            _btnSew.Click += BtnSew_Click;

            _btnInvertAlpha = new Button { Text = "Inv Alpha", Top = 40, Left = 185, Width = 95, Height = 25 };
            _btnInvertAlpha.Click += BtnInvertAlpha_Click;

            _chkPaint = new CheckBox { Text = "Paint Mode", Top = 80, Left = 10, Width = 115 };
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

            var lblTex2 = new Label { Text = "2nd Tex:", Top = 230, Left = 10, Width = 50 };
            _txtTexture2 = new TextBox { Top = 230, Left = 60, Width = 90 };

            _txtTexture2.TextChanged += (s, e) => {
                if (_tool?.SelectedFaces.Count > 0)
                {
                    var newTex = _txtTexture2.Text.Trim();
                    var doc = _tool.GetDocument();
                    if (doc == null) return;

                    bool anyChanged = false;
                    foreach (var (solid, face) in _tool.SelectedFaces)
                    {
                        if (face.Displacement != null && face.Displacement.Texture2Name != newTex)
                        {
                            anyChanged = true; break;
                        }
                    }

                    if (!anyChanged) return;

                    var transaction = new Transaction();
                    var newSelectedFaces = new List<(Primitives.MapObjects.Solid Solid, Face Face)>();

                    foreach (var (solid, face) in _tool.SelectedFaces.ToList())
                    {
                        if (face.Displacement == null)
                        {
                            newSelectedFaces.Add((solid, face));
                            continue;
                        }

                        if (!solid.Faces.Contains(face)) continue;

                        if (face.Displacement.Texture2Name != newTex)
                        {
                            var clone = (Face)face.Clone();
                            clone.Displacement.Texture2Name = newTex;

                            transaction.Add(new RemoveMapObjectData(solid.ID, face));
                            transaction.Add(new AddMapObjectData(solid.ID, clone));
                            newSelectedFaces.Add((solid, clone));
                        }
                        else
                        {
                            newSelectedFaces.Add((solid, face));
                        }
                    }

                    if (!transaction.IsEmpty)
                    {
                        MapDocumentOperation.Perform(doc, transaction);
                        _tool.SelectedFaces = newSelectedFaces;
                    }
                }
            };

            _btnBrowseTexture2 = new Button { Text = "...", Top = 230, Left = 155, Width = 25 };
            _btnBrowseTexture2.Click += async (s, e) => {
                var doc = _tool?.GetDocument();
                if (doc != null)
                {
                    using (var tb = new Sledge.BspEditor.Tools.Texture.TextureBrowser(doc))
                    {
                        var t = Sledge.Common.Container.Get<Sledge.Common.Translations.ITranslationStringProvider>();
                        await tb.Initialise(t);
                        if (tb.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(tb.SelectedTexture))
                        {
                            _txtTexture2.Text = tb.SelectedTexture;
                        }
                    }
                }
            };

            var lblNoiseMin = new Label { Text = "Noise Min:", Top = 260, Left = 10, Width = 60 };
            _numNoiseMin = new NumericUpDown { Top = 260, Left = 70, Width = 50, Minimum = -1024, Maximum = 1024, Value = -10 };
            var lblNoiseMax = new Label { Text = "Max:", Top = 260, Left = 125, Width = 30 };
            _numNoiseMax = new NumericUpDown { Top = 260, Left = 155, Width = 50, Minimum = -1024, Maximum = 1024, Value = 10 };
            _btnNoise = new Button { Text = "Noise", Top = 260, Left = 210, Width = 60 };
            _btnNoise.Click += (s, e) => {
                if (_tool != null) _tool.ApplyNoise((float)_numNoiseMin.Value, (float)_numNoiseMax.Value);
            };

            Controls.Add(lblNoiseMin); Controls.Add(_numNoiseMin);
            Controls.Add(lblNoiseMax); Controls.Add(_numNoiseMax);
            Controls.Add(_btnNoise);

            Controls.Add(lblTex2); Controls.Add(_txtTexture2); Controls.Add(_btnBrowseTexture2);

            Controls.Add(lblPower); Controls.Add(_powerCombo);
            Controls.Add(_btnCreate); Controls.Add(_btnDestroy); Controls.Add(_btnSew); Controls.Add(_btnInvertAlpha);
            Controls.Add(_chkPaint);
            Controls.Add(lblMode); Controls.Add(_modeCombo);
            Controls.Add(lblRadius); Controls.Add(_numRadius);
            Controls.Add(lblAmount); Controls.Add(_numAmount);
            Controls.Add(lblAxis); Controls.Add(_axisCombo);

            Height = 300;
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

            bool hasFace = _tool?.SelectedFaces.Count > 0;
            bool hasDisp = hasFace && _tool.SelectedFaces.Any(x => x.Face.Displacement != null);

            _btnCreate.Enabled = hasFace && _tool.SelectedFaces.Any(x => x.Face.Displacement == null && x.Face.Vertices.Count >= 4);
            _btnDestroy.Enabled = hasDisp;
            _btnSew.Enabled = hasDisp && _tool.SelectedFaces.Count(x => x.Face.Displacement != null) > 1;
            _btnInvertAlpha.Enabled = hasDisp;

            _chkPaint.Enabled = hasDisp;
            _modeCombo.Enabled = hasDisp;
            _numRadius.Enabled = hasDisp;
            _numAmount.Enabled = hasDisp;
            _axisCombo.Enabled = hasDisp;

            _txtTexture2.Enabled = hasDisp;
            _btnBrowseTexture2.Enabled = hasDisp;
            _numNoiseMin.Enabled = hasDisp;
            _numNoiseMax.Enabled = hasDisp;
            _btnNoise.Enabled = hasDisp;
            if (hasDisp)
            {
                var firstDispFace = _tool.SelectedFaces.FirstOrDefault(x => x.Face.Displacement != null).Face;
                if (firstDispFace != null)
                {
                    _txtTexture2.Text = firstDispFace.Displacement.Texture2Name ?? "";
                }
            }

            if (!_chkPaint.Enabled)
            {
                _chkPaint.Checked = false;
                if (_tool != null) _tool.IsPainting = false;
            }
        }

        private void BtnCreate_Click(object sender, EventArgs e)
        {
            if (_tool?.SelectedFaces.Count == 0)
                return;

            var doc = _tool.GetDocument();
            if (doc == null) return;

            var transaction = new Transaction();
            var power = _powerCombo.SelectedIndex + 2;
            var newSelectedFaces = new List<(Primitives.MapObjects.Solid Solid, Face Face)>();

            foreach (var (solid, face) in _tool.SelectedFaces.ToList())
            {
                if (face.Displacement != null || face.Vertices.Count < 4)
                {
                    newSelectedFaces.Add((solid, face));
                    continue;
                }

                if (!solid.Faces.Contains(face)) continue;

                var clone = (Face)face.Clone();
                clone.Displacement = new Primitives.MapObjectData.Displacement(power, face.Vertices.ToArray());

                transaction.Add(new RemoveMapObjectData(solid.ID, face));
                transaction.Add(new AddMapObjectData(solid.ID, clone));
                newSelectedFaces.Add((solid, clone));
            }

            if (!transaction.IsEmpty)
            {
                MapDocumentOperation.Perform(doc, transaction);
                _tool.SelectedFaces = newSelectedFaces;
            }
            UpdateState();
        }

        private void BtnDestroy_Click(object sender, EventArgs e)
        {
            if (_tool?.SelectedFaces.Count == 0)
                return;

            var doc = _tool.GetDocument();
            if (doc == null) return;

            var transaction = new Transaction();
            var newSelectedFaces = new List<(Primitives.MapObjects.Solid Solid, Face Face)>();

            foreach (var (solid, face) in _tool.SelectedFaces.ToList())
            {
                if (face.Displacement == null)
                {
                    newSelectedFaces.Add((solid, face));
                    continue;
                }

                if (!solid.Faces.Contains(face)) continue;

                var clone = (Face)face.Clone();
                clone.Displacement = null;

                transaction.Add(new RemoveMapObjectData(solid.ID, face));
                transaction.Add(new AddMapObjectData(solid.ID, clone));
                newSelectedFaces.Add((solid, clone));
            }

            if (!transaction.IsEmpty)
            {
                MapDocumentOperation.Perform(doc, transaction);
                _tool.SelectedFaces = newSelectedFaces;
            }
            UpdateState();
        }

        private void BtnSew_Click(object sender, EventArgs e)
        {
            if (_tool != null)
            {
                _tool.SewSelectedDisplacements();
                UpdateState();
            }
        }
        private void BtnInvertAlpha_Click(object sender, EventArgs e)
        {
            if (_tool != null)
            {
                _tool.InvertSelectedDisplacementAlphas();
                UpdateState();
            }
        }
    }
}
