using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sledge.FileSystem;
using Sledge.Providers.Texture.ImageDecoders;

namespace Sledge.Providers.Texture.Pmf
{
    [Export("Pmf", typeof(ITexturePackageProvider))]
    public class PmfTexturePackageProvider : ITexturePackageProvider
    {
        public IEnumerable<TexturePackageReference> GetPackagesInFile(IFile file)
        {
            var texturesFolder = file.GetChild("textures");
            if (texturesFolder != null && texturesFolder.Exists)
            {
                yield return new TexturePackageReference("textures", texturesFolder);
            }
        }

        public Task<TexturePackage> GetTexturePackage(TexturePackageReference reference)
        {
            return Task.FromResult<TexturePackage>(new PmfTexturePackage(reference));
        }

        public Task<IEnumerable<TexturePackage>> GetTexturePackages(IEnumerable<TexturePackageReference> references)
        {
            return Task.FromResult<IEnumerable<TexturePackage>>(references.Select(r => new PmfTexturePackage(r)).ToList());
        }
    }

    public class PmfTexturePackage : TexturePackage
    {
        private readonly IFile _root;

        public PmfTexturePackage(TexturePackageReference reference) : base(reference.Name, "Pmf")
        {
            _root = reference.File;
            ScanTextures();
        }

        private void ScanTextures()
        {
           var specialTextures = new[]
           {
                "aaatrigger", "trigger", "null", "sky", "clip",
                "hint", "skip", "origin", "solidhint", "boundingbox", "bevel", "water",
                "monitor", "parallax", "video", "glass"
            };
            foreach (var st in specialTextures)
            {
                Textures.Add(st);
            }
            if (_root == null || !_root.Exists) return;

            var pmfFiles = _root.GetFiles(true).Where(x => string.Equals(x.Extension, "pmf", StringComparison.OrdinalIgnoreCase));
            foreach (var f in pmfFiles)
            {
                var relPath = f.GetRelativePath(_root).Replace('\\', '/');
                if (relPath.StartsWith("models/", StringComparison.OrdinalIgnoreCase) || relPath.StartsWith("textures/models/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (relPath.EndsWith(".pmf", StringComparison.OrdinalIgnoreCase))
                {
                    relPath = relPath.Substring(0, relPath.Length - 4);
                }
                if (relPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
                {
                    relPath = relPath.Substring(9);
                }
                Textures.Add(relPath);
            }
        }

        public override Task<TextureItem> GetTexture(string name)
        {
            return Task.FromResult(CreateTextureItem(name));
        }

        public override Task<IEnumerable<TextureItem>> GetTextures(IEnumerable<string> names)
        {
            var items = names.Select(CreateTextureItem).Where(x => x != null).ToList();
            return Task.FromResult<IEnumerable<TextureItem>>(items);
        }

        private TextureItem CreateTextureItem(string name)
        {
            if (PmfHelper.IsSpecialToolTexture(name))
            {
                return new TextureItem(name, TextureFlags.None, 64, 64, "tool");
            }
            var imageFile = PmfHelper.ResolveImageFile(_root, name);
            if (imageFile == null || !imageFile.Exists || imageFile.IsContainer) return new TextureItem(name, TextureFlags.None, 64, 64);

            int width = 64, height = 64;
            try
            {
                using var stream = imageFile.Open();
                Bitmap bmp = null;
                if (imageFile.Extension.Equals("dds", StringComparison.OrdinalIgnoreCase))
                    bmp = DdsDecoder.Decode(stream);
                else if (imageFile.Extension.Equals("tga", StringComparison.OrdinalIgnoreCase))
                    bmp = TgaDecoder.Decode(stream);

                if (bmp != null)
                {
                    width = bmp.Width;
                    height = bmp.Height;
                    bmp.Dispose();
                }
            }
            catch { }

            return new TextureItem(name, TextureFlags.None, width, height, "textures");
        }

        public override ITextureStreamSource GetStreamSource()
        {
            return new PmfStreamSource(_root);
        }
    }

    public class PmfStreamSource : ITextureStreamSource
    {
        private readonly IFile _root;

        public PmfStreamSource(IFile root)
        {
            _root = root;
        }

        public bool HasImage(string item)
        {
            return PmfHelper.IsSpecialToolTexture(item) || PmfHelper.ResolveImageFile(_root, item) != null;
        }

        public Task<ICollection<Bitmap>> GetImage(string item, int maxWidth, int maxHeight)
        {
            return Task.Run<ICollection<Bitmap>>(() =>
            {
                if (PmfHelper.IsSpecialToolTexture(item))
                {
                    return new[] { PmfHelper.GenerateToolBitmap(item, 64, 64) };
                }
                var imageFile = PmfHelper.ResolveImageFile(_root, item);
                if (imageFile == null || !imageFile.Exists || imageFile.IsContainer) return null;

                try
                {
                    using var stream = imageFile.Open();
                    Bitmap bmp = null;
                    if (imageFile.Extension.Equals("dds", StringComparison.OrdinalIgnoreCase))
                        bmp = DdsDecoder.Decode(stream);
                    else if (imageFile.Extension.Equals("tga", StringComparison.OrdinalIgnoreCase))
                        bmp = TgaDecoder.Decode(stream);

                    return bmp != null ? new[] { bmp } : null;
                }
                catch
                {
                    return null;
                }
            });
        }

        public Task<ICollection<Bitmap>> GetRawImage(string item, int maxWidth, int maxHeight) => GetImage(item, maxWidth, maxHeight);

        public void Dispose() { }
    }

    internal static class PmfHelper
    {
        public static bool IsSpecialToolTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            return lower == "null" || lower.StartsWith("sky") || lower == "clip" ||
                   lower == "hint" || lower == "skip" || lower == "origin" ||
                   lower == "aaatrigger" || lower == "trigger" || lower == "solidhint" ||
                   lower == "boundingbox" || lower == "bevel" || lower.StartsWith("water") ||
                   lower.StartsWith("monitor") || lower.StartsWith("parallax") ||
                   lower.StartsWith("video") || lower.StartsWith("glass");
        }

        public static Bitmap GenerateToolBitmap(string name, int width, int height)
        {
            var lower = name.ToLowerInvariant();
            Color bgColor;
            Color textColor = Color.White;
            Color borderColor = Color.Black;

            if (lower == "aaatrigger" || lower == "trigger")
            {
                bgColor = Color.FromArgb(170, 80, 0); // Orange/Brown trigger
            }
            else if (lower == "null")
            {
                bgColor = Color.FromArgb(70, 150, 240); // Blue null
            }
            else if (lower.StartsWith("sky"))
            {
                bgColor = Color.FromArgb(40, 160, 220); // Sky blue
            }
            else if (lower == "clip")
            {
                bgColor = Color.FromArgb(180, 60, 60); // Red clip
            }
            else if (lower == "hint")
            {
                bgColor = Color.FromArgb(40, 140, 230); // Bright blue hint
            }
            else if (lower == "skip")
            {
                bgColor = Color.FromArgb(60, 180, 60); // Green skip
            }
            else if (lower == "origin")
            {
                bgColor = Color.FromArgb(180, 180, 180); // Grey origin
                textColor = Color.Black;
            }
            else if (lower == "solidhint")
            {
                bgColor = Color.FromArgb(0, 120, 200);
            }
            else if (lower == "bevel")
            {
                bgColor = Color.FromArgb(140, 70, 180); // Purple bevel
            }
            else if (lower.StartsWith("water"))
            {
                bgColor = Color.FromArgb(30, 100, 180); // Translucent blue water
            }
            else if (lower.StartsWith("monitor"))
            {
                bgColor = Color.FromArgb(20, 180, 20); // Monitor green
            }
            else if (lower.StartsWith("parallax"))
            {
                bgColor = Color.FromArgb(150, 50, 150); // Parallax magenta
            }
            else if (lower.StartsWith("video"))
            {
                bgColor = Color.FromArgb(200, 100, 50); // Video orange-brown
            }
            else if (lower.StartsWith("glass"))
            {
                bgColor = Color.FromArgb(160, 200, 220); // Glass light cyan
                textColor = Color.DarkBlue;
            }
            else // boundingbox and others
            {
                bgColor = Color.FromArgb(210, 160, 40); // Yellow/gold
                textColor = Color.Black;
            }

            var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bgColor);
                using (var pen = new Pen(borderColor, 2))
                {
                    g.DrawRectangle(pen, 1, 1, width - 2, height - 2);
                }
                using (var font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold))
                using (var brush = new SolidBrush(textColor))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(name.ToUpperInvariant(), font, brush, new RectangleF(2, 2, width - 4, height - 4), sf);
                }
            }
            return bmp;
        }
        public static IFile ResolveImageFile(IFile root, string pmfRelativePath, int depth = 0)
        {
            if (depth > 6 || root == null) return null;

            var path = pmfRelativePath.Replace('\\', '/');
            if (path.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(9);
            if (!path.EndsWith(".pmf", StringComparison.OrdinalIgnoreCase)) path += ".pmf";

            var pmfFile = root.TraversePath(path);
            if (pmfFile == null || !pmfFile.Exists)
            {
                pmfFile = root.TraversePath("textures/" + path);
            }
            if (pmfFile == null || !pmfFile.Exists || pmfFile.IsContainer) return null;

            string diffuseRelPath = null;
            string aliasScript = null;
            bool isAlias = false;

            using (var reader = new StreamReader(pmfFile.Open()))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int cIdx = line.IndexOf("//", StringComparison.Ordinal);
                    if (cIdx >= 0) line = line.Substring(0, cIdx);
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 1 && tokens[0].Equals("$alias", StringComparison.OrdinalIgnoreCase))
                    {
                        isAlias = true;
                    }
                    else if (isAlias && tokens.Length >= 2 && tokens[0].Equals("$scriptfile", StringComparison.OrdinalIgnoreCase))
                    {
                        aliasScript = tokens[1].Trim('\"');
                        break;
                    }
                    else if (tokens.Length >= 3 && tokens[0].Equals("$texture", StringComparison.OrdinalIgnoreCase) && tokens[1].Equals("diffuse", StringComparison.OrdinalIgnoreCase))
                    {
                        diffuseRelPath = tokens[2].Trim('\"');
                        break;
                    }
                }
            }

            if (isAlias && !string.IsNullOrEmpty(aliasScript))
            {
                return ResolveImageFile(root, aliasScript, depth + 1);
            }

            if (string.IsNullOrEmpty(diffuseRelPath)) return null;

            var cleanDiffPath = diffuseRelPath.Replace('\\', '/');
            if (cleanDiffPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) cleanDiffPath = cleanDiffPath.Substring(9);

            var imgFile = root.TraversePath(cleanDiffPath);
            if (imgFile == null || !imgFile.Exists)
            {
                imgFile = root.TraversePath("textures/" + cleanDiffPath);
            }
            if (imgFile == null || !imgFile.Exists)
            {
                var altExt = cleanDiffPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(cleanDiffPath, ".tga") : Path.ChangeExtension(cleanDiffPath, ".dds");
                imgFile = root.TraversePath(altExt) ?? root.TraversePath("textures/" + altExt);
            }
            return imgFile;
        }
    }
}