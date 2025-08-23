using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace MeshBrushTool
{
    public class SmoothBrush : BaseBrush
    {
        private readonly RhinoObject _targetObject;
        private readonly bool _isSubD;
        private SubD _workingSubD;
        private Mesh _workingMesh;
        private RTree _vertexTree;
        private Mesh _referenceMesh;
        private Mesh _paintSurface;

        public SmoothBrush(RhinoObject targetObject)
        {
            _targetObject = targetObject;
            _isSubD = targetObject.Geometry is SubD;

            if (_isSubD)
            {
                _workingSubD = targetObject.Geometry.Duplicate() as SubD;
                _paintSurface = Mesh.CreateFromSubD(_workingSubD, 1);
            }
            else
            {
                _workingMesh = targetObject.Geometry.Duplicate() as Mesh;
                _paintSurface = _workingMesh;
            }

            _paintSurface.FaceNormals.ComputeFaceNormals();
            _paintSurface.Normals.ComputeNormals();
        }

        public bool IsSubD() => _isSubD;
        public Mesh GetFinalMesh() => _workingMesh;
        public SubD GetFinalSubD() => _workingSubD;

        public void BeginStroke()
        {
            if (_isSubD) { _referenceMesh = Mesh.CreateFromSubDControlNet(_workingSubD); }
            else { _referenceMesh = _workingMesh.DuplicateMesh(); }

            _vertexTree = new RTree();
            for (int i = 0; i < _referenceMesh.Vertices.Count; i++) { _vertexTree.Insert(_referenceMesh.Vertices[i], i); }
        }

        public void EndStroke()
        {
            if (!_isSubD) { _workingMesh.Weld(RhinoDoc.ActiveDoc.ModelAngleToleranceRadians); }
            _referenceMesh = null;
            _vertexTree = null;
        }

        protected override Mesh GetPaintSurface() => _paintSurface;

        protected override void ApplyBrush(RhinoDoc doc, Point3d center, bool reverse, Vector3d cameraDirection, BrushMode mode)
        {
            if (_referenceMesh == null || _vertexTree == null) return;

            var verticesToProcess = new List<int>();
            _vertexTree.Search(new Sphere(center, OuterRadius), (s, e) => verticesToProcess.Add(e.Id));
            if (verticesToProcess.Count == 0) return;

            var newPositions = new Dictionary<int, Point3d>();

            foreach (int index in verticesToProcess)
            {
                Point3f currentPosF = _referenceMesh.Vertices[index];
                Vector3d vertexNormal = _referenceMesh.Normals[index];
                if (Vector3d.Multiply(vertexNormal, cameraDirection) > 0.1) continue;

                double distance = center.DistanceTo(currentPosF);
                if (distance > OuterRadius) continue;

                double strengthFactor;
                if (distance <= InnerRadius) strengthFactor = 1.0;
                else
                {
                    double range = OuterRadius - InnerRadius;
                    strengthFactor = (range <= 0) ? 1.0 : 1.0 - ((distance - InnerRadius) / range);
                }

                Point3d currentPosD = (Point3d)currentPosF;
                Vector3d moveVector;

                switch (mode)
                {
                    case BrushMode.Smooth:
                        int[] connectedFaceIndices = _referenceMesh.Vertices.GetVertexFaces(index);
                        if (connectedFaceIndices == null || connectedFaceIndices.Length == 0) continue;
                        var neighborIndices = new HashSet<int>();
                        foreach (int faceIndex in connectedFaceIndices)
                        {
                            MeshFace face = _referenceMesh.Faces[faceIndex];
                            if (face.A != index) neighborIndices.Add(face.A); if (face.B != index) neighborIndices.Add(face.B);
                            if (face.C != index) neighborIndices.Add(face.C); if (face.IsQuad && face.D != index) neighborIndices.Add(face.D);
                        }
                        if (neighborIndices.Count == 0) continue;
                        var avgPos = new Point3d(0, 0, 0);
                        foreach (int neighborIndex in neighborIndices) { avgPos += _referenceMesh.Vertices[neighborIndex]; }
                        avgPos /= neighborIndices.Count;
                        moveVector = avgPos - currentPosD;
                        break;

                    case BrushMode.Inflate:
                        moveVector = vertexNormal;
                        break;

                    default:
                        continue;
                }

                double finalStrength = reverse ? -Strength : Strength;
                Point3d newPos = currentPosD + (moveVector * finalStrength * strengthFactor);
                newPositions[index] = newPos;
            }

            if (newPositions.Count == 0) return;

            foreach (var pair in newPositions)
            {
                Point3d pointToSet = pair.Value;
                _referenceMesh.Vertices.SetVertex(pair.Key, (float)pointToSet.X, (float)pointToSet.Y, (float)pointToSet.Z);
            }

            if (_isSubD)
            {
                _workingSubD = SubD.CreateFromMesh(_referenceMesh);
                if (_workingSubD != null)
                {
                    _paintSurface = Mesh.CreateFromSubD(_workingSubD, 1);
                    _paintSurface.FaceNormals.ComputeFaceNormals();
                    doc.Objects.Replace(_targetObject.Id, _workingSubD);
                }
            }
            else
            {
                _workingMesh = _referenceMesh.DuplicateMesh();
                _paintSurface = _workingMesh;
                doc.Objects.Replace(_targetObject.Id, _workingMesh);
            }

            doc.Views.Redraw();
        }
    }
}