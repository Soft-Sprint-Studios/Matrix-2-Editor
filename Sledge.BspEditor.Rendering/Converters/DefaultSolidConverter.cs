using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Numerics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.DataStructures.Geometric;
using Sledge.Providers.Texture;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using System;

namespace Sledge.BspEditor.Rendering.Converters
{
	[Export(typeof(IMapObjectSceneConverter))]
	public class DefaultSolidConverter : IMapObjectSceneConverter
	{
		public MapObjectSceneConverterPriority Priority => MapObjectSceneConverterPriority.DefaultLowest;

		public bool ShouldStopProcessing(MapDocument document, IMapObject obj)
		{
			return false;
		}

		public bool Supports(IMapObject obj)
		{
			return obj.Data.OfType<Face>().Any();
		}

		public Task Convert(BufferBuilder builder, MapDocument document, IMapObject obj, ResourceCollector resourceCollector)
		{
			return ConvertFaces(builder, document, obj, obj.Data.Get<Face>().ToList(), resourceCollector);
		}
		internal static async Task ConvertFaces(BufferBuilder builder, MapDocument document, IMapObject obj, List<Face> faces, ResourceCollector resourceCollector)
		{
			faces = faces.Where(x => x.Vertices.Count > 2).ToList();

			var displayFlags = document.Map.Data.GetOne<DisplayFlags>();
			var displayData = document.Map.Data.GetOne<DisplayData>() ?? new DisplayData();
			var hideNull = displayFlags?.HideNullTextures == true;
			var hideClip = displayFlags?.HideClipTextures == true;
			var wireframe = displayFlags?.Wireframe == true;
			var skybox = displayFlags?.ToggleSkybox == true;

            // Pack the vertices like this [ f1v1 ... f1vn ] ... [ fnv1 ... fnvn ]
            var numVertices = (uint)faces.Sum(x => x.Displacement != null && x.Vertices.Count >= 4 ? ((1 << x.Displacement.Power) + 1) * ((1 << x.Displacement.Power) + 1) : x.Vertices.Count);

            // Pack the indices like this [ solid1 ... solidn ] [ wireframe1 ... wireframe n ]
            var numSolidIndices = (uint)faces.Sum(x => x.Displacement != null && x.Vertices.Count >= 4 ? ((1 << x.Displacement.Power) * (1 << x.Displacement.Power) * 6) : (x.Vertices.Count - 2) * 3);
            var numWireframeIndices = (uint)faces.Sum(x => x.Displacement != null && x.Vertices.Count >= 4 ? (((1 << x.Displacement.Power) + 1) * (1 << x.Displacement.Power) * 4) : x.Vertices.Count * 2);

            var points = new VertexStandard[numVertices];
			var shadowPoints = new VertexStandard[numVertices];
			var indices = new uint[numSolidIndices + numWireframeIndices];
			Color? vColor = null;
			var vId = obj.Data.GetOne<VisgroupID>();
			if (vId != null)
			{
				vColor = document.Map.Data.Get<Visgroup>().FirstOrDefault(x => x.ID == vId.ID)?.Colour;
			}

			var colour = (obj.IsSelected ? Color.Red : vColor.HasValue ? vColor.Value : obj.Data.GetOne<ObjectColor>()?.Color ?? Color.White).ToVector4();

			var tint = Vector4.One;

			var tc = await document.Environment.GetTextureCollection();

			var pipeline = PipelineType.TexturedOpaque;
			var entityHasTransparency = false;
			var flags = obj.IsSelected ? VertexFlags.SelectiveTransformed : VertexFlags.None;

			// try and find the parent entity for render flags
			// TODO: this code is extremely specific to Goldsource and should be abstracted away
			var parentEntity = obj.FindClosestParent(x => x is Entity) as Entity;
			if (parentEntity?.EntityData != null)
			{
				const int renderModeColor = 1;
				const int renderModeTexture = 2;
				const int renderModeGlow = 3; // same as texture for brushes
				const int renderModeSolid = 4;
				const int renderModeAdditive = 5;

				var rendermodevalue = parentEntity.EntityData.Get<int>("rendermode", 0);
				var renderamtvalue = (float)parentEntity.EntityData.Get<int>("renderamt", 255) / 255;
				int rendermode = rendermodevalue;
				float renderamt = renderamtvalue;

				entityHasTransparency = renderamt < 0.99;

				switch (rendermode)
				{
					case renderModeColor:
						// Flat colour, use render colour and force it to run through the alpha tested pipeline
						var rendercolor = parentEntity.EntityData.GetVector3("rendercolor") / 255f ?? Vector3.One;
						tint = new Vector4(rendercolor, renderamt);
						flags |= VertexFlags.FlatColour | VertexFlags.AlphaTested;
						pipeline = PipelineType.TexturedAlpha;
						entityHasTransparency = true;
						break;
					case renderModeTexture:
					case renderModeGlow:
						// Texture is alpha tested and can be transparent
						tint = new Vector4(1, 1, 1, renderamt);
						flags |= VertexFlags.AlphaTested;
						if (entityHasTransparency) pipeline = PipelineType.TexturedAlpha;
						break;
					case renderModeSolid:
						// Texture is alpha tested only
						flags |= VertexFlags.AlphaTested;
						entityHasTransparency = false;
						break;
					case renderModeAdditive:
						// Texture is alpha tested and transparent, force through the additive pipeline
						tint = new Vector4(renderamt, renderamt, renderamt, 1);
						pipeline = PipelineType.TexturedAdditive;
						entityHasTransparency = true;
						break;
					default:
						entityHasTransparency = false;
						break;
				}
			}

			if (obj.IsSelected) tint *= new Vector4(1, 0.5f, 0.5f, 1);

			var vi = 0u;
			var si = 0u;
			var wi = numSolidIndices;
			foreach (var face in faces)
			{
				var opacity = tc.GetOpacity(face.Texture.Name);
				var t = await tc.GetTextureItem(face.Texture.Name);
				var w = t?.Width ?? 0;
				var h = t?.Height ?? 0;

				var tintModifier = new Vector4(1, 1, 1, opacity);
				var extraFlags = t == null ? VertexFlags.FlatColour : VertexFlags.None;

				var offs = vi;
				var numFaceVerts = (uint)face.Vertices.Count;

				var textureCoords = face.GetTextureCoordinates(w, h).ToList();

				var normal = face.Plane.Normal;
                if (face.Displacement != null && face.Vertices.Count >= 4)
                {
                    int power = face.Displacement.Power;
                    int side = (1 << power) + 1;
                    var corners = face.Displacement.Corners.ToList();
                    var d_offs = vi;

                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            float fr_x = (float)x / (side - 1);
                            float fr_y = (float)y / (side - 1);

                            var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                            var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                            var pos = Vector3.Lerp(top, bot, fr_y);

                            var topTex = Vector2.Lerp(new Vector2(textureCoords[0].Item2, textureCoords[0].Item3), new Vector2(textureCoords[1].Item2, textureCoords[1].Item3), fr_x);
                            var botTex = Vector2.Lerp(new Vector2(textureCoords[3].Item2, textureCoords[3].Item3), new Vector2(textureCoords[2].Item2, textureCoords[2].Item3), fr_x);
                            var tex = Vector2.Lerp(topTex, botTex, fr_y);

                            pos += face.Displacement.Vectors[y * side + x] * face.Displacement.Distances[y * side + x];

                            if (face.Uv1 != null && face.Uv1.Length > (y * side + x))
                            {
                                shadowPoints[vi] = new VertexStandard
                                {
                                    Position = pos,
                                    Colour = colour,
                                    Normal = normal,
                                    Texture = face.Uv1[y * side + x],
                                    Tint = tint * tintModifier,
                                    Flags = flags | extraFlags
                                };
                            }
                            else if (face.Uv1 != null)
                            {
                                shadowPoints[vi] = new VertexStandard
                                {
                                    Position = pos,
                                    Colour = colour,
                                    Normal = normal,
                                    Texture = face.Uv1[0],
                                    Tint = tint * tintModifier,
                                    Flags = flags | extraFlags
                                };
                            }
                            points[vi++] = new VertexStandard
                            {
                                Position = pos,
                                Colour = colour,
                                Normal = normal,
                                Texture = tex,
                                Tint = tint * tintModifier,
                                Flags = flags | extraFlags
                            };
                        }
                    }

                    for (uint y = 0; y < side - 1; y++)
                    {
                        for (uint x = 0; x < side - 1; x++)
                        {
                            indices[si++] = d_offs + (y * (uint)side + x);
                            indices[si++] = d_offs + (y * (uint)side + (x + 1));
                            indices[si++] = d_offs + ((y + 1) * (uint)side + x);

                            indices[si++] = d_offs + (y * (uint)side + (x + 1));
                            indices[si++] = d_offs + ((y + 1) * (uint)side + (x + 1));
                            indices[si++] = d_offs + ((y + 1) * (uint)side + x);
                        }
                    }

                    for (uint y = 0; y < side; y++)
                    {
                        for (uint x = 0; x < side - 1; x++)
                        {
                            indices[wi++] = d_offs + (y * (uint)side + x);
                            indices[wi++] = d_offs + (y * (uint)side + (x + 1));
                        }
                    }
                    for (uint y = 0; y < side - 1; y++)
                    {
                        for (uint x = 0; x < side; x++)
                        {
                            indices[wi++] = d_offs + (y * (uint)side + x);
                            indices[wi++] = d_offs + ((y + 1) * (uint)side + x);
                        }
                    }
                    continue;
                }
                for (var i = 0; i < face.Vertices.Count; i++)
				{

					var v = face.Vertices[i];
					if (face.Uv1 != null)
					{
						shadowPoints[vi] = new VertexStandard
						{
							Position = v,
							Colour = colour,
							Normal = normal,
							Texture = face.Uv1?[i] ?? new Vector2(textureCoords[i].Item2, textureCoords[i].Item3),
							Tint = tint * tintModifier,
							Flags = flags | extraFlags
						};
					}
					points[vi++] = new VertexStandard
					{
						Position = v,
						Colour = colour,
						Normal = normal,
						Texture = new Vector2(textureCoords[i].Item2, textureCoords[i].Item3),
						Tint = tint * tintModifier,
						Flags = flags | extraFlags
					};


				}

				// Triangles - [0 1 2]  ... [0 n-1 n]
				for (uint i = 2; i < numFaceVerts; i++)
				{
					indices[si++] = offs;
					indices[si++] = offs + i - 1;
					indices[si++] = offs + i;
				}

				// Lines - [0 1] ... [n-1 n] [n 0]
				for (uint i = 0; i < numFaceVerts; i++)
				{
					indices[wi++] = offs + i;
					indices[wi++] = offs + (i == numFaceVerts - 1 ? 0 : i + 1);
				}
			}

			var groups = new List<BufferGroup>();
			var shadowGroups = new List<BufferGroup>();

			uint texOffset = 0;
			foreach (var f in faces)
			{
                var texInd = (uint)(f.Displacement != null && f.Vertices.Count >= 4 ? ((1 << f.Displacement.Power) * (1 << f.Displacement.Power) * 6) : (f.Vertices.Count - 2) * 3);

                if ((hideNull && tc.IsNullTexture(f.Texture.Name)) || (hideClip && tc.IsClipTexture(f.Texture.Name) || (skybox && f.Texture.Name.ToLower() == "sky")))
				{
					texOffset += texInd;
					continue;
				}

                string primaryTexName = f.Texture.Name;
                if (f.Texture.Name.Contains("_blend_", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = f.Texture.Name.Split(new[] { "_blend_" }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1) primaryTexName = parts[0];
                }

                var opacity = tc.GetOpacity(primaryTexName);
                var t = await tc.GetTextureItem(primaryTexName);
                var transparent = entityHasTransparency || opacity < 0.95f || t?.Flags.HasFlag(TextureFlags.Transparent) == true;

				var texture = t == null ? string.Empty : $"{document.Environment.ID}::{f.Texture.Name}";
				BufferGroup group;

				group = new BufferGroup(
				   pipeline == PipelineType.TexturedOpaque && transparent ? PipelineType.TexturedAlpha : pipeline,
				   CameraType.Perspective, transparent, f.Origin, texture, texOffset, texInd
			   );

				groups.Add(group);

				if (f.Uv1 != null)
				{
					group = new BufferGroup(
						PipelineType.ShadowOverlay,
						CameraType.Perspective, false, f.Origin, f.LightMap.GetHashCode().ToString(), texOffset, texInd
						);
					shadowGroups.Add(group);
				}

				texOffset += texInd;

				if (t != null) resourceCollector.RequireTexture(t.Name);
			}

			groups.Add(new BufferGroup(PipelineType.Wireframe, obj.IsSelected ? CameraType.Both : CameraType.Orthographic, numSolidIndices, numWireframeIndices));

			builder.Append(points, indices, groups);
			builder.Append(shadowPoints, indices, shadowGroups);

            foreach (var face in faces)
            {
                if (face.Displacement != null && face.Vertices.Count >= 4 && !string.IsNullOrWhiteSpace(face.Displacement.Texture2Name))
                {
                    var t2 = await tc.GetTextureItem(face.Displacement.Texture2Name);
                    if (t2 != null)
                    {
                        resourceCollector.RequireTexture(t2.Name);

                        int power = face.Displacement.Power;
                        int side = (1 << power) + 1;
                        var corners = face.Displacement.Corners.ToList();
                        var normal = face.Plane.Normal;

                        var pass2Verts = new List<VertexStandard>();
                        var pass2Indices = new List<uint>();

                        var t2W = t2.Width;
                        var t2H = t2.Height;
                        var t2Coords = face.GetTextureCoordinates(t2W, t2H).ToList();

                        for (int y = 0; y < side; y++)
                        {
                            for (int x = 0; x < side; x++)
                            {
                                float fr_x = (float)x / (side - 1);
                                float fr_y = (float)y / (side - 1);
                                var top = Vector3.Lerp(corners[0], corners[1], fr_x);
                                var bot = Vector3.Lerp(corners[3], corners[2], fr_x);
                                var pos = Vector3.Lerp(top, bot, fr_y) + face.Displacement.Vectors[y * side + x] * face.Displacement.Distances[y * side + x];

                                var topTex = Vector2.Lerp(new Vector2(t2Coords[0].Item2, t2Coords[0].Item3), new Vector2(t2Coords[1].Item2, t2Coords[1].Item3), fr_x);
                                var botTex = Vector2.Lerp(new Vector2(t2Coords[3].Item2, t2Coords[3].Item3), new Vector2(t2Coords[2].Item2, t2Coords[2].Item3), fr_x);
                                var tex2 = Vector2.Lerp(topTex, botTex, fr_y);

                                float alphaRatio = (face.Displacement.Alphas != null && face.Displacement.Alphas.Length > (y * side + x))
                                    ? face.Displacement.Alphas[y * side + x] / 255.0f
                                    : 0f;

                                pass2Verts.Add(new VertexStandard
                                {
                                    Position = pos + (normal * 0.1f),
                                    Colour = colour,
                                    Normal = normal,
                                    Texture = tex2,
                                    Tint = new Vector4(tint.X, tint.Y, tint.Z, alphaRatio * tint.W),
                                    Flags = flags
                                });
                            }
                        }

                        for (uint y = 0; y < (uint)side - 1; y++)
                        {
                            for (uint x = 0; x < (uint)side - 1; x++)
                            {
                                pass2Indices.Add(y * (uint)side + x);
                                pass2Indices.Add(y * (uint)side + (x + 1));
                                pass2Indices.Add((y + 1) * (uint)side + x);

                                pass2Indices.Add(y * (uint)side + (x + 1));
                                pass2Indices.Add((y + 1) * (uint)side + (x + 1));
                                pass2Indices.Add((y + 1) * (uint)side + x);
                            }
                        }

                        string tex2Binding = $"{document.Environment.ID}::{face.Displacement.Texture2Name}";
                        var pass2Group = new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, true, face.Origin, tex2Binding, 0, (uint)pass2Indices.Count);
                        builder.Append(pass2Verts, pass2Indices, new[] { pass2Group });
                    }
                }
            }

            if (wireframe)
			{
				var wirePoints = points.ToList().Select(x => { x.Flags |= VertexFlags.Wireframed; return x; });
				builder.Append(wirePoints, indices, new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, numSolidIndices, numWireframeIndices) });
			}
			// Also push the untransformed wireframe when selected
			if (obj.IsSelected)
			{
				for (var i = 0; i < points.Length; i++) points[i].Flags = VertexFlags.None;
				var untransformedIndices = indices.Skip((int)numSolidIndices);
				builder.Append(points, untransformedIndices, new[]
				{
					new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, numWireframeIndices)
				});
			}
		}
	}
}