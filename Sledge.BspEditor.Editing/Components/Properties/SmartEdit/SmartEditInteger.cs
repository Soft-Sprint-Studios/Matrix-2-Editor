using System.ComponentModel.Composition;
using System.Globalization;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.DataStructures.GameData;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
    [Export(typeof(IObjectPropertyEditor))]
    public class SmartEditInteger : SmartEditControl
    {
        private readonly NumericUpDown _numericUpDown;
        public SmartEditInteger()
        {
            _numericUpDown = new NumericUpDown
            {
                Width = 50,
                Minimum = -1000000,
                Maximum = 1000000,
                Value = 0,
                DecimalPlaces = 4,
                Increment = 0.1m
            };
            _numericUpDown.ValueChanged += (sender, e) => OnValueChanged();
            Controls.Add(_numericUpDown);
        }

        public override string PriorityHint => "H";

        public override bool SupportsType(VariableType type)
        {
            return type == VariableType.Integer || type == VariableType.Float;
        }

        protected override string GetName()
        {
            return OriginalName;
        }

        protected override string GetValue()
        {
            return _numericUpDown.Value.ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnSetProperty(MapDocument document)
        {
            _numericUpDown.Text = PropertyValue;
        }
    }
}