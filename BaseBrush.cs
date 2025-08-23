using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using System.Drawing;

namespace MeshBrushTool
{
    public enum BrushMode
    {
        Smooth,
        Inflate
    }

    public abstract class BaseBrush : GetPoint
    {
        protected double InnerRadius;
        protected double OuterRadius;
        protected double Strength;
        protected BrushMode CurrentMode;
        protected bool KeepOriginal;

        private OptionDouble _optInner, _optOuter, _optStrength;
        private int _modeListIndex;
        private OptionToggle _optKeepOriginal;
        private readonly SmoothBrush _smoothBrushInstance;
        private bool _isDragging = false;

        public BaseBrush(double innerRadius = 0.5, double outerRadius = 1.0, double strength = 0.5)
        {
            _smoothBrushInstance = this as SmoothBrush;
            this.InnerRadius = innerRadius; this.OuterRadius = outerRadius; this.Strength = strength;
            this.CurrentMode = BrushMode.Smooth;
            this.KeepOriginal = false;

            SetCommandPrompt("Drag mouse to sculpt. Press Enter or Esc to finish."); // <-- Traducido

            _optInner = new OptionDouble(InnerRadius, 0.01, 1000);
            _optOuter = new OptionDouble(OuterRadius, 0.01, 1000);
            _optStrength = new OptionDouble(Strength, 0.01, 1.0);
            _optKeepOriginal = new OptionToggle(KeepOriginal, "No", "Yes"); // <-- Traducido

            // --- Opciones de la línea de comandos traducidas ---
            AddOptionDouble("InnerRadius", ref _optInner, "Radius of maximum influence");
            AddOptionDouble("OuterRadius", ref _optOuter, "Outer radius of the falloff");
            AddOptionDouble("Strength", ref _optStrength, "Strength of the brush (0 to 1)");
            _modeListIndex = AddOptionList("Mode", new[] { "Smooth", "Inflate" }, (int)CurrentMode);
            AddOptionToggle("KeepOriginal", ref _optKeepOriginal);
        }

        public bool KeepOriginalObject => KeepOriginal;

        public void DoPaint()
        {
            while (true)
            {
                var res = Get();
                if (res == GetResult.Option)
                {
                    var lastOption = Option();
                    if (lastOption.Index == _modeListIndex)
                    {
                        CurrentMode = (BrushMode)lastOption.CurrentListOptionIndex;
                    }
                    InnerRadius = _optInner.CurrentValue;
                    OuterRadius = _optOuter.CurrentValue;
                    Strength = _optStrength.CurrentValue;
                    KeepOriginal = _optKeepOriginal.CurrentValue;
                    continue;
                }
                if (res != GetResult.Point) break;
            }
        }

        protected override void OnMouseMove(GetPointMouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButtonDown && !_isDragging) { _isDragging = true; _smoothBrushInstance?.BeginStroke(); }
            if (!e.LeftButtonDown && _isDragging) { _isDragging = false; _smoothBrushInstance?.EndStroke(); }
            if (e.LeftButtonDown) { ApplyBrush(RhinoDoc.ActiveDoc, e.Point, e.ShiftKeyDown, e.Viewport.CameraDirection, CurrentMode); }
        }

        protected override void OnDynamicDraw(GetPointDrawEventArgs e)
        {
            base.OnDynamicDraw(e);
            Point2d screenPoint = e.Viewport.WorldToClient(e.CurrentPoint);
            if (!e.Viewport.GetFrustumLine(screenPoint.X, screenPoint.Y, out Line line)) return;
            var ray = new Ray3d(line.From, line.Direction);
            Mesh paintSurface = GetPaintSurface();
            if (paintSurface == null) return;
            var intersection = Rhino.Geometry.Intersect.Intersection.MeshRay(paintSurface, ray);
            if (intersection < 0) return;
            Point3d hitPoint = ray.PointAt(intersection);
            var meshPoint = paintSurface.ClosestMeshPoint(hitPoint, 0.0);
            if (meshPoint == null) return;
            int faceIndex = meshPoint.FaceIndex;
            if (faceIndex < 0 || faceIndex >= paintSurface.Faces.Count || faceIndex >= paintSurface.FaceNormals.Count) return;
            Vector3d normal = paintSurface.FaceNormals[faceIndex];
            var plane = new Plane(hitPoint, normal);
            e.Display.DrawCircle(new Circle(plane, InnerRadius), System.Drawing.Color.DeepSkyBlue, 3);
            if (OuterRadius > InnerRadius)
            {
                e.Display.DrawCircle(new Circle(plane, OuterRadius), System.Drawing.Color.FromArgb(100, System.Drawing.Color.LightGray), 2);
            }
        }

        protected abstract Mesh GetPaintSurface();
        protected abstract void ApplyBrush(RhinoDoc doc, Point3d center, bool reverse, Vector3d cameraDirection, BrushMode mode);
    }
}