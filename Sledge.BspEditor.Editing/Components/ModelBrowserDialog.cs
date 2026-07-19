using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Controls.FileSystem;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.Common.Logging;
using Sledge.FileSystem;
using Sledge.Providers.Model.Mdl10;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Editing.Components
{
    public class ModelBrowserDialog : Form
    {
        private readonly FileSystemBrowserControl Browser;
        private readonly Panel PreviewPanel;
        private IViewport _viewport;
        private PerspectiveCamera _camera;
        private MapViewport _mapViewport;
        private IModel _model;
        private IModelRenderable _renderable;

        public List<IFile> SelectedFiles { get; private set; }

        public ModelBrowserDialog(IFile root)
        {
            Text = "Model Browser";
            Size = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 550,
                FixedPanel = FixedPanel.Panel1
            };
            Controls.Add(splitContainer);

            Browser = new FileSystemBrowserControl
            {
                Dock = DockStyle.Fill,
                Filter = "*.mdl",
                FilterText = "Models (*.mdl)",
                File = root
            };
            Browser.Cancelled += s => { DialogResult = DialogResult.Cancel; Close(); };
            Browser.Confirmed += (s, files) => { SelectedFiles = files.ToList(); DialogResult = DialogResult.OK; Close(); };
            Browser.FileListView.SelectedIndexChanged += FileSelectionChanged;

            splitContainer.Panel1.Controls.Add(Browser);

            PreviewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };
            splitContainer.Panel2.Controls.Add(PreviewPanel);

            InitializeViewport();
        }

        private void InitializeViewport()
        {
            try
            {
                _viewport = Sledge.Rendering.Engine.Engine.Interface.CreateViewport();
                _viewport.Control.Tag = "ModelPreview";

                _camera = new PerspectiveCamera { FOV = 60 };
                _viewport.Camera = _camera;

                _viewport.Control.Dock = DockStyle.Fill;
                PreviewPanel.Controls.Add(_viewport.Control);

                _mapViewport = new MapViewport(_viewport);
                _mapViewport.Listeners.Add(new PerspectiveCameraNavigationViewportListener(_mapViewport));
            }
            catch (Exception ex)
            {
                Log.Error(nameof(ModelBrowserDialog), "Failed to initialize model preview viewport", ex);
            }
        }

        private async void FileSelectionChanged(object sender, EventArgs e)
        {
            if (Browser.FileListView.SelectedItems.Count != 1) return;
            var file = Browser.FileListView.SelectedItems[0].Tag as IFile;
            if (file == null || file.IsContainer || !file.Name.EndsWith(".mdl", StringComparison.InvariantCultureIgnoreCase)) return;

            await LoadPreviewModel(file);
        }

        private async Task LoadPreviewModel(IFile file)
        {
            if (_renderable != null)
            {
                Sledge.Rendering.Engine.Engine.Interface.Remove((IRenderable)_renderable);
                Sledge.Rendering.Engine.Engine.Interface.Remove((IUpdateable)_renderable);
                Sledge.Rendering.Engine.Engine.Interface.DestroyResource(_renderable);
                _renderable.Dispose();
                _renderable = null;
            }

            if (_model != null)
            {
                Sledge.Rendering.Engine.Engine.Interface.DestroyResource(_model);
                _model.Dispose();
                _model = null;
            }

            try
            {
                var provider = new MdlModelProvider();
                if (provider.CanLoadModel(file))
                {
                    _model = await provider.LoadModel(file);
                    _renderable = provider.CreateRenderable(_model);
                    _renderable.Origin = Vector3.Zero;
                    _renderable.Angles = Vector3.Zero;
                    _renderable.Sequence = 0;
                    _renderable.IsPreview = true;

                    Sledge.Rendering.Engine.Engine.Interface.CreateResource(_model);
                    Sledge.Rendering.Engine.Engine.Interface.CreateResource(_renderable);
                    Sledge.Rendering.Engine.Engine.Interface.Add((IRenderable)_renderable);
                    Sledge.Rendering.Engine.Engine.Interface.Add((IUpdateable)_renderable);

                    var (min, max) = _renderable.GetBoundingBox();
                    var box = new Sledge.DataStructures.Geometric.Box(min, max);
                    var center = box.Center;
                    var radius = box.Dimensions.Length() * 1.5f;
                    if (radius < 32) radius = 32;

                    _camera.Position = center - new Vector3(radius, radius, -radius / 2f);
                    _camera.Direction = Vector3.Normalize(center - _camera.Position);
                }
            }
            catch (Exception ex)
            {
                Log.Error(nameof(ModelBrowserDialog), $"Failed to load preview for model: {file.Name}", ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_renderable != null)
            {
                Sledge.Rendering.Engine.Engine.Interface.Remove((IRenderable)_renderable);
                Sledge.Rendering.Engine.Engine.Interface.Remove((IUpdateable)_renderable);
                Sledge.Rendering.Engine.Engine.Interface.DestroyResource(_renderable);
                _renderable.Dispose();
                _renderable = null;
            }
            if (_model != null)
            {
                Sledge.Rendering.Engine.Engine.Interface.DestroyResource(_model);
                _model.Dispose();
                _model = null;
            }
            if (_viewport != null)
            {
                _viewport.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}