using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Grid;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Viewports;
using Veldrid;
using Buffer = Sledge.Rendering.Resources.Buffer;

namespace Sledge.BspEditor.Rendering.Grid
{
    /// <summary>
    /// Renders the grid for a single viewport.
    /// Orthographic views get the classic flat grid.
    /// Perspective views additionally get a world-aligned ground grid at Z = 0
    /// </summary>
    public class GridRenderable : IRenderable
    {
        public float Order => -100;

        private readonly IViewport _viewport;
        private IGrid _grid;

        // Ortho validation cache
        private bool _orthoValidated;
        private OrthographicCamera.OrthographicType _currentType;
        private float _currentZoom;
        private RectangleF _currentBounds;

        // Perspective validation cache
        private bool _perspValidated;
        private bool _lastUpdateWasPerspective;
        private Vector3 _perspLastPosition;
        private float _perspLastStep = -1;
        private float _perspLastRadius = -1;

        private readonly Buffer _buffer;
        private uint _indexCount;

        private const float GroundGridZ = 0f;
        private const int MaxLinesPerAxis = 500;
        private const int MaxTotalIndices = 240000;

        public GridRenderable(IViewport viewport, EngineInterface engine)
        {
            _viewport = viewport;
            _buffer = engine.CreateBuffer();
        }

        public void SetGrid(MapDocument doc)
        {
            _grid = doc?.Map.Data.GetOne<GridData>()?.Grid;
            _orthoValidated = false;
            _perspValidated = false;
        }

        public bool ShouldRender(IPipeline pipeline, IViewport viewport)
        {
            return pipeline.Type == PipelineType.Wireframe
                   && viewport == _viewport
                   && _grid != null
                   && (viewport.Camera.Type == CameraType.Orthographic || viewport.Camera.Type == CameraType.Perspective);
        }

        public void Render(RenderContext context, IPipeline pipeline, IViewport viewport, CommandList cl)
        {
            if (UpdateRequired()) Update();

            if (_indexCount == 0) return;
            _buffer.Bind(cl, 0);
            cl.DrawIndexed(_indexCount, 1, 0, 0, 0);
        }

        public IEnumerable<ILocation> GetLocationObjects(IPipeline pipeline, IViewport viewport) => Enumerable.Empty<ILocation>();

        public void Render(RenderContext context, IPipeline pipeline, IViewport viewport, CommandList cl, ILocation locationObject)
        {
            //
        }

        private bool UpdateRequired()
        {
            if (_grid == null) return false;

            if (_viewport.Camera is OrthographicCamera oc)
            {
                if (_lastUpdateWasPerspective) return true;
                if (!_orthoValidated) return true;
                if (oc.ViewType != _currentType) return true;
                if (Math.Abs(_currentZoom - oc.Zoom) > 0.001f) return true;

                var newBounds = GetValidatedBounds(oc, 0);
                return !_currentBounds.Contains(newBounds);
            }

            if (_viewport.Camera is PerspectiveCamera pc)
            {
                if (!_lastUpdateWasPerspective) return true;
                if (!_perspValidated) return true;

                var step = GetGridStep();
                var radius = GetPerspectiveRadius(step);
                if (Math.Abs(step - _perspLastStep) > 0.001f) return true;
                if (Math.Abs(radius - _perspLastRadius) > 0.001f) return true;

                var d = pc.Position - _perspLastPosition;
                return Math.Abs(d.X) > step || Math.Abs(d.Y) > step || Math.Abs(d.Z) > step;
            }

            return false;
        }

        private void Update()
        {
            if (_viewport.Camera is OrthographicCamera oc && _grid != null)
            {
                UpdateOrthographic(oc);
            }
            else if (_viewport.Camera is PerspectiveCamera pc && _grid != null)
            {
                UpdatePerspective(pc);
            }
        }

        #region Orthographic

        private void UpdateOrthographic(OrthographicCamera oc)
        {
            var newBounds = GetValidatedBounds(oc, 50);
            var min = oc.Expand(new Vector3(newBounds.Left, newBounds.Top, 0));
            var max = oc.Expand(new Vector3(newBounds.Right, newBounds.Bottom, 0));

            var normal = Vector3.One - oc.Expand(new Vector3(1, 1, 0));

            var points = new List<VertexStandard>();
            var indices = new List<uint>();

            uint idx = 0;
            foreach (var line in _grid.GetLines(normal, oc.Zoom, min, max).OrderBy(x => (int)x.Type))
            {
                var c = GetColorForGridLineType(line.Type);
                var col = new Vector4(c.R, c.G, c.B, c.A) / 255f;
                points.Add(new VertexStandard { Position = line.Line.Start, Normal = normal, Colour = col, Texture = Vector2.Zero, Tint = Vector4.One });
                points.Add(new VertexStandard { Position = line.Line.End, Normal = normal, Colour = col, Texture = Vector2.Zero, Tint = Vector4.One });
                indices.Add(idx++);
                indices.Add(idx++);
            }

            _buffer.Update(points, indices);
            _indexCount = idx;

            _orthoValidated = true;
            _lastUpdateWasPerspective = false;
            _currentType = oc.ViewType;
            _currentZoom = oc.Zoom;
            _currentBounds = newBounds;
        }

        private RectangleF GetValidatedBounds(OrthographicCamera camera, int padding)
        {
            var vmin = camera.Flatten(camera.ScreenToWorld(new Vector3(-padding, camera.Height + padding, 0)));
            var vmax = camera.Flatten(camera.ScreenToWorld(new Vector3(camera.Width + padding, -padding, 0)));
            return new RectangleF(vmin.X, vmin.Y, vmax.X - vmin.X, vmax.Y - vmin.Y);
        }

        #endregion

        #region Perspective (ground grid at Z = 0)

        private float GetGridStep()
        {
            if (_grid is SquareGrid sg && sg.Step > 0) return sg.Step;
            var v = _grid?.AddStep(Vector3.Zero, Vector3.One) ?? Vector3.One;
            var s = Math.Abs(v.X);
            return s < 1 ? 16f : s;
        }

        private float GetPerspectiveRadius(float step)
        {
            var pc = (PerspectiveCamera)_viewport.Camera;
            var r = pc.ClipDistance * 0.75f;
            return Math.Min(Math.Max(r, step * 64f), 32768f);
        }

        private void UpdatePerspective(PerspectiveCamera pc)
        {
            var step = GetGridStep();
            var radius = GetPerspectiveRadius(step);

            var centre = pc.Position;

            var span = radius * 2f;
            var coarse = step;
            while (span / coarse > MaxLinesPerAxis) coarse *= 2;

            var minX = (float)Math.Floor((centre.X - radius) / coarse) * coarse;
            var maxX = minX + (float)Math.Ceiling(span / coarse) * coarse;
            var minY = (float)Math.Floor((centre.Y - radius) / coarse) * coarse;
            var maxY = minY + (float)Math.Ceiling(span / coarse) * coarse;

            int primary = SquareGridFactory.GridPrimaryHighlight;
            int secondary = SquareGridFactory.GridSecondaryHighlight;

            var points = new List<VertexStandard>(4096);
            var indices = new List<uint>(8192);
            uint index = 0;

            void AddLine(float x1, float y1, float x2, float y2, Color colour)
            {
                if (indices.Count >= MaxTotalIndices) return;
                var col = new Vector4(colour.R, colour.G, colour.B, 255) / 255f;
                points.Add(new VertexStandard { Position = new Vector3(x1, y1, GroundGridZ), Normal = Vector3.UnitZ, Colour = col, Texture = Vector2.Zero, Tint = Vector4.One });
                points.Add(new VertexStandard { Position = new Vector3(x2, y2, GroundGridZ), Normal = Vector3.UnitZ, Colour = col, Texture = Vector2.Zero, Tint = Vector4.One });
                indices.Add(index++);
                indices.Add(index++);
            }

            Color Classify(double value)
            {
                var rv = Math.Round(value);
                if (Math.Abs(value) < 0.0001) return Renderer.AxisGridLineColour;
                if (secondary > 0 && Math.Abs(rv % secondary) < 0.0001) return Renderer.SecondaryGridLineColour;
                if (primary > 0 && Math.Abs(rv % (coarse * primary)) < 0.0001) return Renderer.PrimaryGridLineColour;
                return Math.Abs(coarse - step) > 0.001f ? Renderer.FractionalGridLineColour : Renderer.StandardGridLineColour;
            }

            for (var x = minX; x <= maxX + 0.001f; x += coarse)
            {
                AddLine(x, minY, x, maxY, Classify(x));
                if (indices.Count >= MaxTotalIndices) break;
            }

            for (var y = minY; y <= maxY + 0.001f; y += coarse)
            {
                AddLine(minX, y, maxX, y, Classify(y));
                if (indices.Count >= MaxTotalIndices) break;
            }

            _buffer.Update(points, indices);
            _indexCount = index;

            _perspValidated = true;
            _lastUpdateWasPerspective = true;
            _perspLastPosition = centre;
            _perspLastStep = step;
            _perspLastRadius = radius;
        }

        #endregion

        public void Dispose()
        {
            _buffer?.Dispose();
        }

        private Color GetColorForGridLineType(GridLineType type)
        {
            switch (type)
            {
                case GridLineType.Fractional: return Renderer.FractionalGridLineColour;
                case GridLineType.Standard: return Renderer.StandardGridLineColour;
                case GridLineType.Axis: return Renderer.AxisGridLineColour;
                case GridLineType.Primary: return Renderer.PrimaryGridLineColour;
                case GridLineType.Secondary: return Renderer.SecondaryGridLineColour;
                case GridLineType.Boundary: return Renderer.BoundaryGridLineColour;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}