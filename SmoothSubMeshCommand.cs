using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

namespace MeshBrushTool
{
    public class SmoothSubMeshCommand : Command
    {
        public SmoothSubMeshCommand() { Instance = this; }
        public static SmoothSubMeshCommand Instance { get; private set; }
        public override string EnglishName => "SmoothBrush";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Nacho said - Select a mesh or SubD to smooth"); // <-- Traducido
            go.GeometryFilter = ObjectType.Mesh | ObjectType.SubD;
            go.SubObjectSelect = false;
            go.Get();

            if (go.CommandResult() != Result.Success) return go.CommandResult();

            var objRef = go.Object(0);
            var targetObject = objRef.Object();
            if (targetObject == null) return Result.Failure;

            var originalGeometry = targetObject.Geometry.Duplicate();
            var originalAttributes = targetObject.Attributes.Duplicate();
            var originalId = targetObject.Id;

            doc.AddCustomUndoEvent("SmoothSubMesh Action", (sender, e) => { });

            var brush = new SmoothBrush(targetObject);
            brush.DoPaint();

            GeometryBase finalGeometry;
            if (brush.IsSubD())
            {
                finalGeometry = brush.GetFinalSubD();
            }
            else
            {
                var finalMesh = brush.GetFinalMesh();
                finalMesh.Weld(doc.ModelAngleToleranceRadians);
                finalGeometry = finalMesh;
            }

            if (finalGeometry == null)
            {
                doc.Objects.Delete(originalId, true);
                doc.Objects.Add(originalGeometry, originalAttributes);
                return Result.Failure;
            }

            if (brush.KeepOriginalObject)
            {
                doc.Objects.Delete(originalId, true);
                doc.Objects.Add(originalGeometry, originalAttributes);
                doc.Objects.Add(finalGeometry, originalAttributes);
            }
            else
            {
                doc.Objects.Delete(originalId, true);
                doc.Objects.Add(finalGeometry, originalAttributes);
            }

            doc.Views.Redraw();
            return Result.Success;
        }
    }
}