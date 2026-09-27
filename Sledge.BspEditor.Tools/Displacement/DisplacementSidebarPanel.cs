using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
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
        private Face _lastSelectedFace;
        private bool _syncingPower;

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
            _powerCombo.SelectedIndexChanged += (s, e) => { if (!_syncingPower) UpdateState(); };

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

            if (hasDisp && !_powerCombo.DroppedDown)
            {
                var currentPrimary = _tool.SelectedFaces.FirstOrDefault(x => x.Face.Displacement != null).Face;
                if (currentPrimary != null && _lastSelectedFace != currentPrimary)
                {
                    _syncingPower = true;
                    _powerCombo.SelectedItem = currentPrimary.Displacement.Power.ToString();
                    _syncingPower = false;
                    _lastSelectedFace = currentPrimary;
                }
            }
            else if (!hasDisp)
            {
                _lastSelectedFace = null;
            }

            int selectedPower = int.TryParse(_powerCombo.SelectedItem?.ToString(), out var sp) ? sp : 3;

            _btnCreate.Enabled = hasFace && _tool.SelectedFaces.Any(x =>
                  x.Face.Vertices.Count >= 4 &&
                  (x.Face.Displacement == null || x.Face.Displacement.Power != selectedPower)
              );
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
            var power = int.TryParse(_powerCombo.SelectedItem?.ToString(), out var pVal) ? pVal : 3;
            var newSelectedFaces = new List<(Primitives.MapObjects.Solid Solid, Face Face)>();

            foreach (var (solid, face) in _tool.SelectedFaces.ToList())
            {
                if (face.Vertices.Count < 4 || !solid.Faces.Contains(face))
                {
                    newSelectedFaces.Add((solid, face));
                    continue;
                }

                if (face.Displacement == null)
                {
                    var clone = (Face)face.Clone();
                    clone.Displacement = new Primitives.MapObjectData.Displacement(power, face.Vertices.ToArray());

                    transaction.Add(new RemoveMapObjectData(solid.ID, face));
                    transaction.Add(new AddMapObjectData(solid.ID, clone));
                    newSelectedFaces.Add((solid, clone));
                }
                else if (face.Displacement.Power != power)
                {
                    var clone = (Face)face.Clone();
                    var corners = (face.Displacement.Corners != null && face.Displacement.Corners.Length == 4)
                        ? face.Displacement.Corners
                        : face.Vertices.ToArray();
                    clone.Displacement = ResampleDisplacement(face.Displacement, power, corners);

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
            _lastSelectedFace = null;
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

        private static Primitives.MapObjectData.Displacement ResampleDisplacement(Primitives.MapObjectData.Displacement origDisp, int newPower, Vector3[] corners)
        {
            var newDisp = new Primitives.MapObjectData.Displacement(newPower, corners);
            newDisp.Texture2Name = origDisp.Texture2Name ?? "";

            int oldSide = (1 << origDisp.Power) + 1;
            int newSide = (1 << newPower) + 1;

            for (int ny = 0; ny < newSide; ny++)
            {
                float v = (float)ny / (newSide - 1);
                float gy = v * (oldSide - 1);
                int y0 = (int)MathF.Floor(gy);
                int y1 = Math.Min(y0 + 1, oldSide - 1);
                float ry = gy - y0;

                for (int nx = 0; nx < newSide; nx++)
                {
                    float u = (float)nx / (newSide - 1);
                    float gx = u * (oldSide - 1);
                    int x0 = (int)MathF.Floor(gx);
                    int x1 = Math.Min(x0 + 1, oldSide - 1);
                    float rx = gx - x0;

                    Vector3 o00 = GetDispOffset(origDisp, oldSide, x0, y0);
                    Vector3 o10 = GetDispOffset(origDisp, oldSide, x1, y0);
                    Vector3 o01 = GetDispOffset(origDisp, oldSide, x0, y1);
                    Vector3 o11 = GetDispOffset(origDisp, oldSide, x1, y1);
                    Vector3 interpOffset = Vector3.Lerp(Vector3.Lerp(o00, o10, rx), Vector3.Lerp(o01, o11, rx), ry);

                    float a00 = GetDispAlpha(origDisp, oldSide, x0, y0);
                    float a10 = GetDispAlpha(origDisp, oldSide, x1, y0);
                    float a01 = GetDispAlpha(origDisp, oldSide, x0, y1);
                    float a11 = GetDispAlpha(origDisp, oldSide, x1, y1);
                    float interpAlpha = (1 - ry) * ((1 - rx) * a00 + rx * a10) + ry * ((1 - rx) * a01 + rx * a11);

                    int nidx = ny * newSide + nx;
                    float dist = interpOffset.Length();
                    newDisp.Distances[nidx] = dist;
                    newDisp.Vectors[nidx] = dist > 0.0001f ? Vector3.Normalize(interpOffset) : Vector3.UnitZ;
                    newDisp.Alphas[nidx] = Math.Clamp(interpAlpha, 0f, 255f);
                }
            }

            return newDisp;
        }

        private static Vector3 GetDispOffset(Primitives.MapObjectData.Displacement disp, int side, int x, int y)
        {
            int idx = y * side + x;
            if (disp.Vectors == null || disp.Distances == null || idx >= disp.Vectors.Length || idx >= disp.Distances.Length)
                return Vector3.Zero;
            return disp.Vectors[idx] * disp.Distances[idx];
        }

        private static float GetDispAlpha(Primitives.MapObjectData.Displacement disp, int side, int x, int y)
        {
            int idx = y * side + x;
            if (disp.Alphas == null || idx >= disp.Alphas.Length)
                return 0f;
            return disp.Alphas[idx];
        }
    }
}
