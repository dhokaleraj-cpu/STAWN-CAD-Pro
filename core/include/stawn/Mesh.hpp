// STAWN CAD Pro - tessellation of a B-rep body for the viewport.
#pragma once

#include <TopoDS_Shape.hxx>

#include <cstdint>
#include <vector>

namespace stawn {

struct MeshData {
    std::vector<float> positions;     // xyz per vertex
    std::vector<float> normals;       // xyz per vertex
    std::vector<uint32_t> indices;    // three per triangle
    std::vector<int32_t> triangleFace;  // B-rep face index per triangle, for picking
    std::vector<float> edgeLines;     // xyz xyz per edge segment (model edges drawn in black)
    std::vector<int32_t> segmentEdge;   // B-rep edge index per segment
    float bounds[6] = {0, 0, 0, 0, 0, 0};  // min xyz, max xyz
};

MeshData tessellate(const TopoDS_Shape& shape, double linearDeflection = 0.02, double angularDeflection = 0.3);

}  // namespace stawn
