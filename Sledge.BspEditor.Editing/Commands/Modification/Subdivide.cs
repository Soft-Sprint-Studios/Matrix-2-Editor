using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.QuickForms;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "", "Evil", "G")]
    [CommandID("BspEditor:Tools:Subdivide")]
    public class Subdivide : BaseCommand
    {
        public override string Name { get; set; } = "Subdivide...";
        public override string Details { get; set; } = "Subdivide a brush into multiple parts";

        public string PromptTitle { get; set; } = "Subdivide Brush";
        public string SubdivideAmount { get; set; } = "Number of subdivisions";
        public string XYOnly { get; set; } = "Subdivide on XY plane only";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && document.Selection.Any(x => x is Solid);
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var objects = document.Selection.OfType<Solid>().ToList();
            if (!objects.Any())
                return;

            var qf = new QuickForm(PromptTitle) { UseShortcutKeys = true };
            qf.NumericUpDown("Amount", SubdivideAmount, 2, 64, 0, 2);
            qf.CheckBox("XYOnly", XYOnly, true);
            qf.OkCancel(OK, Cancel);

            if (await qf.ShowDialogAsync() != DialogResult.OK)
                return;

            var divisions = (int)qf.Decimal("Amount");
            var xyOnly = qf.Bool("XYOnly");
            var transaction = new Transaction();

            foreach (var solid in objects)
            {
                var box = solid.BoundingBox;
                var currentSolids = new List<Solid> { solid };

                // Subdivide along X, Y, and Z
                currentSolids = SplitAlongAxis(document, currentSolids, box.Start.X, box.End.X, divisions, Vector3.UnitX);
                currentSolids = SplitAlongAxis(document, currentSolids, box.Start.Y, box.End.Y, divisions, Vector3.UnitY);
                if (!xyOnly) currentSolids = SplitAlongAxis(document, currentSolids, box.Start.Z, box.End.Z, divisions, Vector3.UnitZ);

                transaction.Add(new Detatch(solid.Hierarchy.Parent.ID, solid));
                transaction.Add(new Attach(solid.Hierarchy.Parent.ID, currentSolids));
            }

            await MapDocumentOperation.Perform(document, transaction);
        }

        private List<Solid> SplitAlongAxis(MapDocument doc, List<Solid> solids, float start, float end, int count, Vector3 axis)
        {
            if (count <= 1)
                return solids;
            var step = (end - start) / count;
            var result = new List<Solid>(solids);

            for (int i = 1; i < count; i++)
            {
                var planePos = start + (step * i);
                var plane = new Sledge.DataStructures.Geometric.Plane(axis, planePos);
                var nextIter = new List<Solid>();

                foreach (var s in result)
                {
                    if (s.Split(doc.Map.NumberGenerator, plane, out var back, out var front))
                    {
                        if (back != null)
                            nextIter.Add(back);
                        if (front != null)
                            nextIter.Add(front);
                    }
                    else
                    {
                        nextIter.Add(s);
                    }
                }
                result = nextIter;
            }
            return result;
        }
    }
}