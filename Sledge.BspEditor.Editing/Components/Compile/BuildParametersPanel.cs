using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using Sledge.BspEditor.Editing.Components.Compile.Specification;
using Sledge.Common.Extensions;

namespace Sledge.BspEditor.Editing.Components.Compile
{
	public partial class BuildParametersPanel : UserControl
	{
		private CompileTool _tool;

		public CompileTool Tool
		{
			get => _tool;
			set => SetTool(value);
		}

		public string Arguments
		{
			get => GetArguments();
			set => SetArguments(value);
		}
		public BuildParametersPanel(bool editable = false)
		{
			InitializeComponent();
		}

		private void SetTool(CompileTool tool)
		{
			_tool = tool;
		}

        private string GetArguments() => txtPreviewText.Text;

        private void SetArguments(string arguments) => txtPreviewText.Text = arguments;
	}
}
