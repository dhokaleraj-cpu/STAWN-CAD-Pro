#include "stawn/Mesh.hpp"

#include <BRepAdaptor_Curve.hxx>
#include <BRepBndLib.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <GCPnts_TangentialDeflection.hxx>
#include <Poly_Triangulation.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>

namespace stawn {

MeshData tessellate(const TopoDS_Shape& shape, double linearDeflection, double angularDeflection) {
    MeshData m;
    if (shape.IsNull()) return m;
    BRepMesh_IncrementalMesh mesher(shape, linearDeflection, Standard_False, angularDeflection, Standard_True);

    TopTools_IndexedMapOfShape faces, edges;
    TopExp::MapShapes(shape, TopAbs_FACE, faces);
    TopExp::MapShapes(shape, TopAbs_EDGE, edges);

    for (int fi = 1; fi <= faces.Extent(); ++fi) {
        TopoDS_Face face = TopoDS::Face(faces(fi));
        TopLoc_Location loc;
        Handle(Poly_Triangulation) tri = BRep_Tool::Triangulation(face, loc);
        if (tri.IsNull()) continue;
        if (!tri->HasNormals()) tri->ComputeNormals();
        const gp_Trsf& trsf = loc.Transformation();
        bool reversed = face.Orientation() == TopAbs_REVERSED;
        uint32_t base = uint32_t(m.positions.size() / 3);
        for (int i = 1; i <= tri->NbNodes(); ++i) {
            gp_Pnt p = tri->Node(i).Transformed(trsf);
            gp_Dir n = tri->Normal(i).Transformed(trsf);
            if (reversed) n.Reverse();
            m.positions.insert(m.positions.end(), {float(p.X()), float(p.Y()), float(p.Z())});
            m.normals.insert(m.normals.end(), {float(n.X()), float(n.Y()), float(n.Z())});
        }
        for (int i = 1; i <= tri->NbTriangles(); ++i) {
            int a, b, c;
            tri->Triangle(i).Get(a, b, c);
            if (reversed) std::swap(b, c);
            m.indices.insert(m.indices.end(), {base + a - 1, base + b - 1, base + c - 1});
            m.triangleFace.push_back(fi - 1);
        }
    }

    for (int ei = 1; ei <= edges.Extent(); ++ei) {
        TopoDS_Edge e = TopoDS::Edge(edges(ei));
        if (BRep_Tool::Degenerated(e)) continue;
        BRepAdaptor_Curve curve(e);
        GCPnts_TangentialDeflection pts(curve, angularDeflection, linearDeflection);
        for (int i = 1; i < pts.NbPoints(); ++i) {
            gp_Pnt a = pts.Value(i), b = pts.Value(i + 1);
            m.edgeLines.insert(m.edgeLines.end(), {float(a.X()), float(a.Y()), float(a.Z()), float(b.X()),
                                                   float(b.Y()), float(b.Z())});
            m.segmentEdge.push_back(ei - 1);
        }
    }

    Bnd_Box box;
    BRepBndLib::Add(shape, box);
    if (!box.IsVoid()) {
        double x0, y0, z0, x1, y1, z1;
        box.Get(x0, y0, z0, x1, y1, z1);
        float b[6] = {float(x0), float(y0), float(z0), float(x1), float(y1), float(z1)};
        std::copy(b, b + 6, m.bounds);
    }
    return m;
}

}  // namespace stawn
