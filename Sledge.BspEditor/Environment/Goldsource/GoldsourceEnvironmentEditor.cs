using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Logging;
using Sledge.Common.Translations;
using Sledge.DataStructures.GameData;
using Sledge.FileSystem;
using Sledge.Providers.GameData;
using Sledge.Providers.Texture;
using Sledge.Shell;

namespace Sledge.BspEditor.Environment.Goldsource
{
	public partial class GoldsourceEnvironmentEditor : UserControl, IEnvironmentEditor
	{
		public event EventHandler EnvironmentChanged;
		public Control Control => this;

		private readonly IGameDataProvider _fgdProvider = Common.Container.Get<IGameDataProvider>("Fgd");

		private Dictionary<string, bool> _initialObjectCollection = null;

		public IEnvironment Environment
		{
			get => GetEnvironment();
			set => SetEnvironment(value as GoldsourceEnvironment);
		}

		public GoldsourceEnvironmentEditor()
		{
			InitializeComponent();

			txtGameDir.TextChanged += OnEnvironmentChanged;
			cmbBaseGame.SelectedIndexChanged += OnEnvironmentChanged;
			cmbGameMod.SelectedIndexChanged += OnEnvironmentChanged;
			cmbGameExe.SelectedIndexChanged += OnEnvironmentChanged;
			chkLoadHdModels.CheckedChanged += OnEnvironmentChanged;
			chkIncludeDownloads.CheckedChanged += OnEnvironmentChanged;

			cmbDefaultPointEntity.SelectedIndexChanged += OnEnvironmentChanged;
			cmbDefaultBrushEntity.SelectedIndexChanged += OnEnvironmentChanged;
			chkOverrideMapSize.CheckedChanged += OnEnvironmentChanged;
			cmbMapSizeOverrideLow.SelectedIndexChanged += OnEnvironmentChanged;
			cmbMapSizeOverrideHigh.SelectedIndexChanged += OnEnvironmentChanged;
			chkIncludeFgdDirectories.CheckedChanged += OnEnvironmentChanged;

			txtBuildToolsDirectory.TextChanged += OnEnvironmentChanged;
			chkIncludeToolsDirectory.CheckedChanged += OnEnvironmentChanged;
			cmbBspExe.SelectedIndexChanged += OnEnvironmentChanged;
			cmbCsgExe.SelectedIndexChanged += OnEnvironmentChanged;
			cmbVisExe.SelectedIndexChanged += OnEnvironmentChanged;
			cmbRadExe.SelectedIndexChanged += OnEnvironmentChanged;

			chkCopyBsp.CheckedChanged += OnEnvironmentChanged;
			chkRunGame.CheckedChanged += OnEnvironmentChanged;
			chkAskRunGame.CheckedChanged += OnEnvironmentChanged;

			chkMapCopyBsp.CheckedChanged += OnEnvironmentChanged;
			chkCopyMap.CheckedChanged += OnEnvironmentChanged;
			chkCopyLog.CheckedChanged += OnEnvironmentChanged;
			chkCopyErr.CheckedChanged += OnEnvironmentChanged;
			chkCopyRes.CheckedChanged += OnEnvironmentChanged;

			gridUpDown.ValueChanged += OnEnvironmentChanged;

			nudDefaultTextureScale.ValueChanged += OnEnvironmentChanged;
		}

		public void Translate(ITranslationStringProvider strings)
		{
			CreateHandle();
			var prefix = GetType().FullName;

			grpDirectories.Text = strings.GetString(prefix, "Directories");
			grpFgds.Text = strings.GetString(prefix, "GameDataFiles");
			grpBuildTools.Text = strings.GetString(prefix, "BuildTools");
			grpTextures.Text = strings.GetString(prefix, "Textures");

			btnBuildToolsBrowse.Text = btnGameDirBrowse.Text = strings.GetString(prefix, "Browse");
            btnAddFgd.Text = strings.GetString(prefix, "Add");
            btnRemoveFgd.Text = strings.GetString(prefix, "Remove");

            colFgdName.Text = strings.GetString(prefix, "Name");
            colFgdPath.Text = strings.GetString(prefix, "Path");

            lblGameDir.Text = strings.GetString(prefix, "GameDirectory");
			lblBaseGame.Text = strings.GetString(prefix, "BaseDirectory");
			lblGameMod.Text = strings.GetString(prefix, "ModDirectory");
			lblGameExe.Text = strings.GetString(prefix, "GameExecutable");
			chkLoadHdModels.Text = strings.GetString(prefix, "LoadHDModels");
			chkIncludeDownloads.Text = strings.GetString(prefix, "LoadFromDownloads");

			lblDefaultPointEntity.Text = strings.GetString(prefix, "DefaultPointEntity");
			lblDefaultBrushEntity.Text = strings.GetString(prefix, "DefaultBrushEntity");
			lblMapSizeOverrideLow.Text = strings.GetString(prefix, "Low");
			lblMapSizeOverrideHigh.Text = strings.GetString(prefix, "High");
			chkOverrideMapSize.Text = strings.GetString(prefix, "OverrideMapSize");
			chkIncludeFgdDirectories.Text = strings.GetString(prefix, "IncludeFgdDirectories");

			lblBuildExeFolder.Text = strings.GetString(prefix, "BuildDirectory");
			chkIncludeToolsDirectory.Text = strings.GetString(prefix, "IncludeToolsDirectory");
			chkCopyBsp.Text = strings.GetString(prefix, "CopyBspToGameFolder");
			chkRunGame.Text = strings.GetString(prefix, "RunGame");
			chkAskRunGame.Text = strings.GetString(prefix, "AskToRunGame");
			lblCopyToMapFolder.Text = strings.GetString(prefix, "CopyToMapFolder");

			lblDefaultTextureScale.Text = strings.GetString(prefix, "DefaultTextureScale");

		}

		private void OnEnvironmentChanged(object sender, EventArgs e)
		{
			EnvironmentChanged?.Invoke(this, e);
		}

		public void SetEnvironment(GoldsourceEnvironment env)
		{
			if (env == null) env = new GoldsourceEnvironment();

			txtGameDir.Text = env.BaseDirectory;
			cmbBaseGame.SelectedItem = env.GameDirectory;
			cmbGameMod.SelectedItem = env.ModDirectory;
			cmbGameExe.SelectedItem = env.GameExe;
			chkLoadHdModels.Checked = env.LoadHdModels;
			chkIncludeDownloads.Checked = env.LoadFromDownloads;

			lstFgds.Items.Clear();
			foreach (var fileName in env.FgdFiles)
			{
				lstFgds.Items.Add(new ListViewItem(new[] { Path.GetFileName(fileName), fileName }) { ToolTipText = fileName });
			}
			UpdateFgdList();

			cmbDefaultPointEntity.SelectedItem = env.DefaultPointEntity;
			cmbDefaultBrushEntity.SelectedItem = env.DefaultBrushEntity;
			chkOverrideMapSize.Checked = env.OverrideMapSize;
			cmbMapSizeOverrideLow.SelectedItem = Convert.ToString(env.MapSizeLow, CultureInfo.InvariantCulture);
			cmbMapSizeOverrideHigh.SelectedItem = Convert.ToString(env.MapSizeHigh, CultureInfo.InvariantCulture);
			chkIncludeFgdDirectories.Checked = env.IncludeFgdDirectoriesInEnvironment;

			txtBuildToolsDirectory.Text = env.ToolsDirectory;
			chkIncludeToolsDirectory.Checked = env.IncludeToolsDirectoryInEnvironment;
			cmbBspExe.SelectedItem = env.BspExe;
			cmbCsgExe.SelectedItem = env.CsgExe;
			cmbVisExe.SelectedItem = env.VisExe;
			cmbRadExe.SelectedItem = env.RadExe;

			chkCopyBsp.Checked = env.GameCopyBsp;
			chkRunGame.Checked = env.GameRun;
			chkAskRunGame.Checked = env.GameAsk;

			chkMapCopyBsp.Checked = env.MapCopyBsp;
			chkCopyMap.Checked = env.MapCopyMap;
			chkCopyLog.Checked = env.MapCopyLog;
			chkCopyErr.Checked = env.MapCopyErr;
			chkCopyRes.Checked = env.MapCopyRes;

			gridUpDown.Value += (decimal)env.DefaultGridSize;

			nudDefaultTextureScale.Value = env.DefaultTextureScale;

			cordonTextureText.Text = env.CordonTexture;
			nonRendTextBox.Text = env.NonRenderableTextures?.Aggregate("", (current, next) => current + (next + ";")).Trim();
		}

		public GoldsourceEnvironment GetEnvironment()
		{
			return new GoldsourceEnvironment()
			{
				BaseDirectory = txtGameDir.Text,
				GameDirectory = Convert.ToString(cmbBaseGame.SelectedItem, CultureInfo.InvariantCulture),
				ModDirectory = Convert.ToString(cmbGameMod.SelectedItem, CultureInfo.InvariantCulture),
				GameExe = Convert.ToString(cmbGameExe.SelectedItem, CultureInfo.InvariantCulture),
				LoadHdModels = chkLoadHdModels.Checked,
				LoadFromDownloads = chkIncludeDownloads.Checked,

				FgdFiles = lstFgds.Items.OfType<ListViewItem>().Select(x => x.SubItems[1].Text).Where(File.Exists).ToList(),
				DefaultPointEntity = Convert.ToString(cmbDefaultPointEntity.SelectedItem, CultureInfo.InvariantCulture),
				DefaultBrushEntity = Convert.ToString(cmbDefaultBrushEntity.SelectedItem, CultureInfo.InvariantCulture),
				OverrideMapSize = chkOverrideMapSize.Checked,
				MapSizeLow = decimal.TryParse(Convert.ToString(cmbMapSizeOverrideLow.SelectedItem, CultureInfo.InvariantCulture), out decimal l) ? l : 0,
				MapSizeHigh = decimal.TryParse(Convert.ToString(cmbMapSizeOverrideHigh.SelectedItem, CultureInfo.InvariantCulture), out decimal h) ? h : 0,
				IncludeFgdDirectoriesInEnvironment = chkIncludeFgdDirectories.Checked,

				ToolsDirectory = txtBuildToolsDirectory.Text,
				IncludeToolsDirectoryInEnvironment = chkIncludeToolsDirectory.Checked,
				BspExe = Convert.ToString(cmbBspExe.SelectedItem, CultureInfo.InvariantCulture),
				CsgExe = Convert.ToString(cmbCsgExe.SelectedItem, CultureInfo.InvariantCulture),
				VisExe = Convert.ToString(cmbVisExe.SelectedItem, CultureInfo.InvariantCulture),
				RadExe = Convert.ToString(cmbRadExe.SelectedItem, CultureInfo.InvariantCulture),

				GameCopyBsp = chkCopyBsp.Checked,
				GameRun = chkRunGame.Checked,
				GameAsk = chkAskRunGame.Checked,

				MapCopyBsp = chkMapCopyBsp.Checked,
				MapCopyMap = chkCopyMap.Checked,
				MapCopyLog = chkCopyLog.Checked,
				MapCopyErr = chkCopyErr.Checked,
				MapCopyRes = chkCopyRes.Checked,

				DefaultGridSize = (float)gridUpDown.Value,

				DefaultTextureScale = nudDefaultTextureScale.Value,

				CordonTexture = cordonTextureText.Text,
				NonRenderableTextures = nonRendTextBox.Text.Split(';').Select(texture => texture.Trim()).Where(texture => !string.IsNullOrEmpty(texture)).ToArray()

			};
		}

		// Directories

		private void BrowseGameDirectory(object sender, EventArgs e)
		{
			using (var fbd = new FolderBrowserDialog())
			{
				if (Directory.Exists(txtGameDir.Text)) fbd.SelectedPath = txtGameDir.Text;
				if (fbd.ShowDialog() == DialogResult.OK)
				{
					txtGameDir.Text = fbd.SelectedPath;
				}
			}
		}

		private void GameDirectoryTextChanged(object sender, EventArgs e)
		{
			UpdateGameDirectory();
		}

		private void UpdateGameDirectory()
		{
			var dir = txtGameDir.Text;
			if (!Directory.Exists(dir)) return;

			// Set game/mod directories
			var mod = cmbGameMod.SelectedItem ?? "";
			var bse = cmbBaseGame.SelectedItem ?? "";

			cmbGameMod.Items.Clear();
			cmbBaseGame.Items.Clear();

			var mods = Directory.GetDirectories(dir).Select(Path.GetFileName);
			var ignored = new[] { "gldrv", "logos", "logs", "errorlogs", "platform", "config" };

			var range = mods.Where(x => !ignored.Contains(x.ToLowerInvariant())).OfType<object>().ToArray();
			cmbGameMod.Items.AddRange(range);
			cmbBaseGame.Items.AddRange(range);

			if (cmbGameMod.Items.Contains(mod)) cmbGameMod.SelectedItem = mod;
			else if (cmbGameMod.Items.Count > 0) cmbGameMod.SelectedIndex = 0;

			if (cmbBaseGame.Items.Contains(bse)) cmbBaseGame.SelectedItem = bse;
			else if (cmbBaseGame.Items.Count > 0) cmbBaseGame.SelectedIndex = 0;

			// Set game executable

			var exe = cmbGameExe.SelectedItem ?? "";

			cmbGameExe.Items.Clear();

			var exes = Directory.GetFiles(dir, "*.exe").Select(Path.GetFileName);
			ignored = new[] { "sxuninst.exe", "utdel32.exe", "upd.exe", "hlds.exe", "hltv.exe" };

			range = exes.Where(x => !ignored.Contains(x.ToLowerInvariant())).OfType<object>().ToArray();
			cmbGameExe.Items.AddRange(range);

			if (cmbGameExe.Items.Contains(exe)) cmbGameExe.SelectedItem = exe;
			else if (cmbGameExe.Items.Count > 0) cmbGameExe.SelectedIndex = 0;
		}

		// Game data files

		public string FgdFilesLabel { get; set; } = "Forge Game Data files";

		private void BrowseFgd(object sender, EventArgs e)
		{
			using (var ofd = new OpenFileDialog { Filter = FgdFilesLabel + @" (*.fgd)|*.fgd", Multiselect = true })
			{
				if (ofd.ShowDialog() == DialogResult.OK)
				{
					foreach (var fileName in ofd.FileNames)
					{
						lstFgds.Items.Add(new ListViewItem(new[]
						{
							Path.GetFileName(fileName),
							fileName
						})
						{ ToolTipText = fileName });
					}
					UpdateFgdList();
					OnEnvironmentChanged(this, EventArgs.Empty);
				}
			}
		}

		private void RemoveFgd(object sender, EventArgs e)
		{
			if (lstFgds.SelectedItems.Count > 0)
			{
				foreach (var i in lstFgds.SelectedItems.OfType<ListViewItem>().ToList())
				{
					lstFgds.Items.Remove(i);
				}
				UpdateFgdList();
				OnEnvironmentChanged(this, EventArgs.Empty);
			}
		}

		private void UpdateFgdList()
		{
			lstFgds.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

			var entities = new List<GameDataObject>();
			if (_fgdProvider != null)
			{
				var files = lstFgds.Items.OfType<ListViewItem>().Select(x => x.SubItems[1].Text).Where(File.Exists).Where(_fgdProvider.IsValidForFile);
				try
				{
					var gd = _fgdProvider.GetGameDataFromFiles(files);
					entities.AddRange(gd.Classes);
				}
				catch
				{
					//
				}
			}

			var selPoint = cmbDefaultPointEntity.SelectedItem as string;
			var selBrush = cmbDefaultBrushEntity.SelectedItem as string;

			cmbDefaultPointEntity.BeginUpdate();
			cmbDefaultBrushEntity.BeginUpdate();

			cmbDefaultPointEntity.Items.Clear();
			cmbDefaultBrushEntity.Items.Clear();

			cmbDefaultPointEntity.Items.Add("");
			cmbDefaultBrushEntity.Items.Add("");

			foreach (var gdo in entities.OrderBy(x => x.Name, StringComparer.InvariantCultureIgnoreCase))
			{
				if (gdo.ClassType == ClassType.Solid) cmbDefaultBrushEntity.Items.Add(gdo.Name);
				else if (gdo.ClassType != ClassType.Base) cmbDefaultPointEntity.Items.Add(gdo.Name);
			}

			var idx = cmbDefaultBrushEntity.Items.IndexOf(selBrush ?? "");
			if (idx >= 0) cmbDefaultBrushEntity.SelectedIndex = idx;
			idx = cmbDefaultPointEntity.Items.IndexOf(selPoint ?? "");
			if (idx >= 0) cmbDefaultPointEntity.SelectedIndex = idx;

			cmbDefaultPointEntity.EndUpdate();
			cmbDefaultBrushEntity.EndUpdate();
		}

		// Build tools

		private void BrowseBuildToolsDirectory(object sender, EventArgs e)
		{
			using (var fbd = new FolderBrowserDialog())
			{
				if (Directory.Exists(txtBuildToolsDirectory.Text)) fbd.SelectedPath = txtBuildToolsDirectory.Text;
				if (fbd.ShowDialog() == DialogResult.OK)
				{
					txtBuildToolsDirectory.Text = fbd.SelectedPath;
				}
			}
		}

		private void BuildToolsDirectoryTextChanged(object sender, EventArgs e)
		{
			UpdateBuildToolsDirectory();
		}

		private void UpdateBuildToolsDirectory()
		{
			var dir = txtBuildToolsDirectory.Text;
			if (!Directory.Exists(dir)) return;

			var selBsp = cmbBspExe.SelectedItem ?? "";
			var selCsg = cmbCsgExe.SelectedItem ?? "";
			var selVis = cmbVisExe.SelectedItem ?? "";
			var selRad = cmbRadExe.SelectedItem ?? "";

			cmbBspExe.Items.Clear();
			cmbCsgExe.Items.Clear();
			cmbVisExe.Items.Clear();
			cmbRadExe.Items.Clear();

			var range = Directory.GetFiles(dir, "*.exe").Select(Path.GetFileName).ToList();
			var rangeArr = range.OfType<object>().ToArray();

			cmbBspExe.Items.AddRange(rangeArr);
			cmbCsgExe.Items.AddRange(rangeArr);
			cmbVisExe.Items.AddRange(rangeArr);
			cmbRadExe.Items.AddRange(rangeArr);

			cmbBspExe.SelectedIndex = -1;
			cmbCsgExe.SelectedIndex = -1;
			cmbVisExe.SelectedIndex = -1;
			cmbRadExe.SelectedIndex = -1;

			if (cmbBspExe.Items.Contains(selBsp)) cmbBspExe.SelectedItem = selBsp;
			else if (cmbBspExe.Items.Count > 0) cmbBspExe.SelectedIndex = Math.Max(0, range.FindIndex(x => x.ToLower().Contains("bsp")));

			if (cmbCsgExe.Items.Contains(selCsg)) cmbCsgExe.SelectedItem = selCsg;
			else if (cmbCsgExe.Items.Count > 0) cmbCsgExe.SelectedIndex = Math.Max(0, range.FindIndex(x => x.ToLower().Contains("csg")));

			if (cmbVisExe.Items.Contains(selVis)) cmbVisExe.SelectedItem = selVis;
			else if (cmbVisExe.Items.Count > 0) cmbVisExe.SelectedIndex = Math.Max(0, range.FindIndex(x => x.ToLower().Contains("vis")));

			if (cmbRadExe.Items.Contains(selRad)) cmbRadExe.SelectedItem = selRad;
			else if (cmbRadExe.Items.Count > 0) cmbRadExe.SelectedIndex = Math.Max(0, range.FindIndex(x => x.ToLower().Contains("rad")));
		}

		private void BaseGameDirectoryChanged(object sender, EventArgs e)
		{
			OnEnvironmentChanged(sender, e);
		}

		private void ModDirectoryChanged(object sender, EventArgs e)
		{
			OnEnvironmentChanged(sender, e);
		}

		private void IncludeBuildToolsChanged(object sender, EventArgs e)
		{
			OnEnvironmentChanged(sender, e);
		}
		private void cordonTextureText_TextChanged(object sender, EventArgs e)
		{
			OnEnvironmentChanged(sender, e);
		}

		private void nonRendTextBox_TextChanged(object sender, EventArgs e)
		{
			OnEnvironmentChanged(sender, e);
		}
	}
}
