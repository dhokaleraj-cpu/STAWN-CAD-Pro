import Foundation
import SwiftUI

/// Swift side of a part document. Holds the C core document and a JSON mirror of it that the
/// panels edit. Every edit writes the JSON back to the core and rebuilds, the same way a
/// change in a PropertyManager triggers a rebuild.
final class PartModel: ObservableObject {
    private(set) var handle: OpaquePointer

    @Published private(set) var json: [String: Any] = [:]
    @Published private(set) var mesh: MeshBuffers?
    @Published private(set) var rebuildErrors: [String] = []
    @Published private(set) var mass = StawnMassProperties()
    @Published private(set) var resolvedProperties: [String: String] = [:]
    @Published var selectedFeature: String?

    /// Called after each edit with the new file contents so the document can be marked dirty.
    var onChange: ((String) -> Void)?

    init(handle: OpaquePointer) {
        self.handle = handle
        refresh()
    }

    convenience init(jsonText: String) throws {
        var error: UnsafeMutablePointer<CChar>?
        guard let h = stawn_doc_from_json(jsonText, &error) else {
            let message = error.map { String(cString: $0) } ?? "Unknown error"
            stawn_free_string(error)
            throw CocoaError(.fileReadCorruptFile, userInfo: [NSLocalizedDescriptionKey: message])
        }
        self.init(handle: h)
    }

    static func sampleShaft() -> PartModel { PartModel(handle: stawn_doc_new_sample_shaft()) }
    static func empty() -> PartModel { PartModel(handle: stawn_doc_new()) }

    deinit { stawn_doc_free(handle) }

    // MARK: - Reading

    var name: String { json["name"] as? String ?? "Part" }
    var features: [[String: Any]] { json["features"] as? [[String: Any]] ?? [] }
    var configurations: [[String: Any]] { json["configurations"] as? [[String: Any]] ?? [] }
    var activeConfiguration: String { json["activeConfiguration"] as? String ?? "Default" }
    var properties: [String: String] { json["properties"] as? [String: String] ?? [:] }
    var material: (name: String, density: Double) {
        let m = json["material"] as? [String: Any] ?? [:]
        return (m["name"] as? String ?? "", m["density"] as? Double ?? 7850)
    }

    func isSuppressed(_ feature: String) -> Bool {
        let active = configurations.first { $0["name"] as? String == activeConfiguration }
        return (active?["suppressed"] as? [String] ?? []).contains(feature)
    }

    func errorFor(_ feature: String) -> String? {
        rebuildErrors.first { $0.hasPrefix(feature + ": ") }.map { String($0.dropFirst(feature.count + 2)) }
    }

    /// Driving dimensions of one feature, e.g. ("D1@Sketch1", 46.9).
    func dimensions(of feature: String) -> [(name: String, value: Double)] {
        guard let f = features.first(where: { $0["name"] as? String == feature }) else { return [] }
        var names: [String] = []
        if f["type"] as? String == "Sketch" {
            let sketch = f["sketch"] as? [String: Any] ?? [:]
            for c in sketch["constraints"] as? [[String: Any]] ?? [] {
                if let n = c["name"] as? String { names.append("\(n)@\(feature)") }
            }
        } else {
            names = ["D1@\(feature)"]
        }
        return names.compactMap { n in
            var v = 0.0
            return stawn_doc_get_dimension(handle, n, &v) != 0 ? (n, v) : nil
        }
    }

    func fileText() -> String {
        let p = stawn_doc_json(handle)!
        defer { stawn_free_string(p) }
        return String(cString: p)
    }

    // MARK: - Editing

    func setDimension(_ name: String, _ value: Double, thisConfigurationOnly: Bool = false) {
        guard stawn_doc_set_dimension(handle, name, value, thisConfigurationOnly ? 1 : 0) != 0 else { return }
        rebuildAndNotify()
    }

    func activate(configuration: String) {
        guard stawn_doc_set_active_configuration(handle, configuration) != 0 else { return }
        rebuildAndNotify()
    }

    func setSuppressed(_ feature: String, _ suppressed: Bool) {
        edit { j in
            var configs = j["configurations"] as? [[String: Any]] ?? []
            for i in configs.indices where configs[i]["name"] as? String == j["activeConfiguration"] as? String {
                var s = Set(configs[i]["suppressed"] as? [String] ?? [])
                if suppressed { s.insert(feature) } else { s.remove(feature) }
                configs[i]["suppressed"] = Array(s).sorted()
            }
            j["configurations"] = configs
        }
    }

    func addConfiguration(named name: String, copyFromActive: Bool = true) {
        guard !name.isEmpty, !configurations.contains(where: { $0["name"] as? String == name }) else { return }
        edit { j in
            var configs = j["configurations"] as? [[String: Any]] ?? []
            var c: [String: Any] = ["name": name, "description": name, "suppressed": [String](),
                                    "dimensions": [String: Double](), "properties": [String: String]()]
            if copyFromActive, let active = configs.first(where: { $0["name"] as? String == j["activeConfiguration"] as? String }) {
                c = active
                c["name"] = name
                c["description"] = name
            }
            configs.append(c)
            j["configurations"] = configs
            j["activeConfiguration"] = name
        }
    }

    func updateConfiguration(_ name: String, newName: String, description: String, bomPartNumber: String) {
        edit { j in
            var configs = j["configurations"] as? [[String: Any]] ?? []
            guard let i = configs.firstIndex(where: { $0["name"] as? String == name }) else { return }
            if !newName.isEmpty, newName != name, !configs.contains(where: { $0["name"] as? String == newName }) {
                configs[i]["name"] = newName
                if j["activeConfiguration"] as? String == name { j["activeConfiguration"] = newName }
            }
            configs[i]["description"] = description
            configs[i]["bomPartNumber"] = bomPartNumber
            j["configurations"] = configs
        }
    }

    func deleteConfiguration(_ name: String) {
        guard configurations.count > 1, name != activeConfiguration else { return }
        edit { j in
            j["configurations"] = (j["configurations"] as? [[String: Any]] ?? []).filter { $0["name"] as? String != name }
        }
    }

    func setProperty(_ key: String, _ value: String, configurationSpecific: Bool = false) {
        edit { j in
            if configurationSpecific {
                var configs = j["configurations"] as? [[String: Any]] ?? []
                for i in configs.indices where configs[i]["name"] as? String == j["activeConfiguration"] as? String {
                    var p = configs[i]["properties"] as? [String: String] ?? [:]
                    p[key] = value
                    configs[i]["properties"] = p
                }
                j["configurations"] = configs
            } else {
                var p = j["properties"] as? [String: String] ?? [:]
                p[key] = value
                j["properties"] = p
            }
        }
    }

    func setMaterial(name: String, density: Double) {
        edit { j in j["material"] = ["name": name, "density": density] }
    }

    func renameFeature(_ from: String, to: String) {
        guard !to.isEmpty, !features.contains(where: { $0["name"] as? String == to }) else { return }
        edit { j in
            var fs = j["features"] as? [[String: Any]] ?? []
            for i in fs.indices {
                if fs[i]["name"] as? String == from { fs[i]["name"] = to }
                if fs[i]["sketchName"] as? String == from { fs[i]["sketchName"] = to }
            }
            j["features"] = fs
            var configs = j["configurations"] as? [[String: Any]] ?? []
            for i in configs.indices {
                var dims: [String: Any] = [:]
                for (k, v) in configs[i]["dimensions"] as? [String: Any] ?? [:] {
                    dims[k.hasSuffix("@" + from) ? String(k.dropLast(from.count)) + to : k] = v
                }
                configs[i]["dimensions"] = dims
                configs[i]["suppressed"] = (configs[i]["suppressed"] as? [String] ?? []).map { $0 == from ? to : $0 }
            }
            j["configurations"] = configs
        }
        if selectedFeature == from { selectedFeature = to }
    }

    // MARK: - Output

    func exportSTEP(to url: URL) -> Bool { stawn_doc_export_step(handle, url.path) != 0 }
    func exportSTL(to url: URL) -> Bool { stawn_doc_export_stl(handle, url.path) != 0 }
    func exportDrawing(to url: URL, paper: String, format: String) -> Bool {
        stawn_doc_export_drawing(handle, paper, format, url.path) != 0
    }
    func drawingSVG(paper: String) -> String {
        let p = stawn_doc_drawing_svg(handle, paper)!
        defer { stawn_free_string(p) }
        return String(cString: p)
    }

    static func materialLibrary() -> [(name: String, density: Double)] {
        let p = stawn_material_library()!
        defer { stawn_free_string(p) }
        let list = (try? JSONSerialization.jsonObject(with: Data(String(cString: p).utf8))) as? [[String: Any]] ?? []
        return list.map { ($0["name"] as? String ?? "", $0["density"] as? Double ?? 0) }
    }

    // MARK: - Internals

    private func edit(_ change: (inout [String: Any]) -> Void) {
        var j = json
        change(&j)
        guard let data = try? JSONSerialization.data(withJSONObject: j, options: [.sortedKeys]),
              let text = String(data: data, encoding: .utf8) else { return }
        var error: UnsafeMutablePointer<CChar>?
        if stawn_doc_set_json(handle, text, &error) == 0 {
            stawn_free_string(error)
            return
        }
        rebuildAndNotify()
    }

    private func rebuildAndNotify() {
        refresh()
        onChange?(fileText())
    }

    func refresh() {
        var errors: UnsafeMutablePointer<CChar>?
        stawn_doc_rebuild(handle, &errors)
        if let errors {
            rebuildErrors = (try? JSONSerialization.jsonObject(with: Data(String(cString: errors).utf8))) as? [String] ?? []
            stawn_free_string(errors)
        }
        json = (try? JSONSerialization.jsonObject(with: Data(fileText().utf8))) as? [String: Any] ?? [:]
        mass = stawn_doc_mass_properties(handle)
        if let p = stawn_doc_resolved_properties(handle) {
            resolvedProperties = (try? JSONSerialization.jsonObject(with: Data(String(cString: p).utf8))) as? [String: String] ?? [:]
            stawn_free_string(p)
        }
        mesh = MeshBuffers(handle: handle)
    }
}

/// Triangles and edge lines copied out of the core for the viewport.
struct MeshBuffers {
    var positions: [Float]
    var normals: [Float]
    var indices: [UInt32]
    var triangleFace: [Int32]
    var edgeLines: [Float]
    var bounds: (min: SIMD3<Float>, max: SIMD3<Float>)

    init?(handle: OpaquePointer) {
        guard let m = stawn_doc_mesh(handle, 0.01) else { return nil }
        defer { stawn_mesh_free(m) }
        let mesh = m.pointee
        guard mesh.triangleCount > 0 else { return nil }
        let v = Int(mesh.vertexCount), t = Int(mesh.triangleCount), s = Int(mesh.segmentCount)
        positions = Array(UnsafeBufferPointer(start: mesh.positions, count: v * 3))
        normals = Array(UnsafeBufferPointer(start: mesh.normals, count: v * 3))
        indices = Array(UnsafeBufferPointer(start: mesh.indices, count: t * 3))
        triangleFace = Array(UnsafeBufferPointer(start: mesh.triangleFace, count: t))
        edgeLines = s > 0 ? Array(UnsafeBufferPointer(start: mesh.edgeLines, count: s * 6)) : []
        let b = mesh.bounds
        bounds = (SIMD3(b.0, b.1, b.2), SIMD3(b.3, b.4, b.5))
    }
}
