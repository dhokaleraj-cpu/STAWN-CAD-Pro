import SceneKit
import SwiftUI

enum DisplayStyle: String, CaseIterable, Identifiable {
    case shadedWithEdges = "Shaded With Edges", shaded = "Shaded", wireframe = "Wireframe"
    var id: String { rawValue }
}

enum StandardView: String, CaseIterable, Identifiable {
    case front = "Front", back = "Back", top = "Top", bottom = "Bottom", right = "Right", left = "Left"
    case isometric = "Isometric"
    var id: String { rawValue }

    /// Direction from the model towards the camera, and the camera's up vector.
    var direction: (eye: SIMD3<Float>, up: SIMD3<Float>) {
        switch self {
        case .front: return ([0, 0, 1], [0, 1, 0])
        case .back: return ([0, 0, -1], [0, 1, 0])
        case .top: return ([0, 1, 0], [0, 0, -1])
        case .bottom: return ([0, -1, 0], [0, 0, 1])
        case .right: return ([1, 0, 0], [0, 1, 0])
        case .left: return ([-1, 0, 0], [0, 1, 0])
        case .isometric: return (simd_normalize([1, 1, 1]), [0, 1, 0])
        }
    }
}

/// One-shot request from the heads-up toolbar to the viewport.
struct ViewCommand: Equatable {
    var view: StandardView?
    var zoomToFit = false
    var serial = UUID()
    var label: String { view?.rawValue ?? "Isometric" }
}

struct HeadsUpToolbar: View {
    @Binding var command: ViewCommand?
    @Binding var style: DisplayStyle

    var body: some View {
        HStack(spacing: 10) {
            Button { command = ViewCommand(view: command?.view, zoomToFit: true) } label: {
                Image(systemName: "arrow.up.left.and.arrow.down.right")
            }
            .help("Zoom to Fit (F)")
            .keyboardShortcut("f", modifiers: [])
            Menu {
                ForEach(StandardView.allCases) { v in
                    Button(v.rawValue) { command = ViewCommand(view: v, zoomToFit: true) }
                }
            } label: { Image(systemName: "cube.transparent") }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .help("View Orientation")
            Menu {
                ForEach(DisplayStyle.allCases) { s in Button(s.rawValue) { style = s } }
            } label: { Image(systemName: "circle.lefthalf.filled") }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .help("Display Style")
        }
        .buttonStyle(.borderless)
        .padding(.horizontal, 12)
        .padding(.vertical, 5)
        .background(.regularMaterial, in: Capsule())
    }
}

/// 3D view of the part. Orbit with the left mouse button, pan with the right (or two-finger
/// drag) and zoom with the scroll wheel or pinch.
struct Viewport: NSViewRepresentable {
    let mesh: MeshBuffers?
    @Binding var command: ViewCommand?
    let style: DisplayStyle

    final class Coordinator {
        var lastCommand: UUID?
        var meshNode = SCNNode()
        var hasFramed = false
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeNSView(context: Context) -> SCNView {
        let view = SCNView()
        let scene = SCNScene()
        view.scene = scene
        view.allowsCameraControl = true
        view.defaultCameraController.interactionMode = .orbitArcball
        view.autoenablesDefaultLighting = false
        view.antialiasingMode = .multisampling4X
        view.backgroundColor = .clear
        scene.background.contents = NSGradient(colors: [NSColor(white: 0.97, alpha: 1), NSColor(calibratedRed: 0.78, green: 0.83, blue: 0.9, alpha: 1)])
            .map { gradientImage($0) }

        let camera = SCNNode()
        camera.camera = SCNCamera()
        camera.camera?.usesOrthographicProjection = true
        camera.camera?.zNear = 0.01
        camera.camera?.zFar = 100_000
        camera.name = "camera"
        scene.rootNode.addChildNode(camera)
        view.pointOfView = camera

        let ambient = SCNNode()
        ambient.light = SCNLight()
        ambient.light?.type = .ambient
        ambient.light?.intensity = 350
        scene.rootNode.addChildNode(ambient)
        let key = SCNNode()
        key.light = SCNLight()
        key.light?.type = .directional
        key.light?.intensity = 800
        camera.addChildNode(key)  // headlight follows the camera

        scene.rootNode.addChildNode(context.coordinator.meshNode)
        return view
    }

    func updateNSView(_ view: SCNView, context: Context) {
        let c = context.coordinator
        c.meshNode.childNodes.forEach { $0.removeFromParentNode() }
        if let mesh {
            if style != .wireframe { c.meshNode.addChildNode(faceNode(mesh)) }
            if style != .shaded { c.meshNode.addChildNode(edgeNode(mesh)) }
        }
        if !c.hasFramed, mesh != nil {
            c.hasFramed = true
            frame(view, mesh: mesh, standard: .isometric)
        }
        if let cmd = command, cmd.serial != c.lastCommand {
            c.lastCommand = cmd.serial
            frame(view, mesh: mesh, standard: cmd.view ?? .isometric)
        }
    }

    private func frame(_ view: SCNView, mesh: MeshBuffers?, standard: StandardView) {
        guard let mesh, let camera = view.pointOfView else { return }
        let lo = mesh.bounds.min, hi = mesh.bounds.max
        let centre = (lo + hi) / 2
        let radius = max(simd_length(hi - lo) / 2, 1)
        let d = standard.direction
        let eye = centre + d.eye * radius * 4
        camera.simdPosition = eye
        camera.simdLook(at: centre, up: d.up, localFront: [0, 0, -1])
        camera.camera?.orthographicScale = Double(radius) * 1.15
        view.defaultCameraController.target = SCNVector3(centre)
    }

    private func faceNode(_ m: MeshBuffers) -> SCNNode {
        let vertexData = m.positions.withUnsafeBufferPointer { Data(buffer: $0) }
        let normalData = m.normals.withUnsafeBufferPointer { Data(buffer: $0) }
        let count = m.positions.count / 3
        let vertices = SCNGeometrySource(data: vertexData, semantic: .vertex, vectorCount: count, usesFloatComponents: true,
                                         componentsPerVector: 3, bytesPerComponent: 4, dataOffset: 0, dataStride: 12)
        let normals = SCNGeometrySource(data: normalData, semantic: .normal, vectorCount: count, usesFloatComponents: true,
                                        componentsPerVector: 3, bytesPerComponent: 4, dataOffset: 0, dataStride: 12)
        let element = SCNGeometryElement(data: m.indices.withUnsafeBufferPointer { Data(buffer: $0) }, primitiveType: .triangles,
                                         primitiveCount: m.indices.count / 3, bytesPerIndex: 4)
        let geometry = SCNGeometry(sources: [vertices, normals], elements: [element])
        let material = SCNMaterial()
        material.lightingModel = .blinn
        material.diffuse.contents = NSColor(calibratedWhite: 0.78, alpha: 1)
        material.specular.contents = NSColor(calibratedWhite: 0.6, alpha: 1)
        material.shininess = 0.4
        material.isDoubleSided = true
        geometry.materials = [material]
        return SCNNode(geometry: geometry)
    }

    private func edgeNode(_ m: MeshBuffers) -> SCNNode {
        let count = m.edgeLines.count / 3
        let source = SCNGeometrySource(data: m.edgeLines.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: .vertex,
                                       vectorCount: count, usesFloatComponents: true, componentsPerVector: 3,
                                       bytesPerComponent: 4, dataOffset: 0, dataStride: 12)
        let indices = (0..<UInt32(count)).map { $0 }
        let element = SCNGeometryElement(data: indices.withUnsafeBufferPointer { Data(buffer: $0) }, primitiveType: .line,
                                         primitiveCount: count / 2, bytesPerIndex: 4)
        let geometry = SCNGeometry(sources: [source], elements: [element])
        let material = SCNMaterial()
        material.lightingModel = .constant
        material.diffuse.contents = NSColor.black
        geometry.materials = [material]
        let node = SCNNode(geometry: geometry)
        node.renderingOrder = 1
        return node
    }

    private func gradientImage(_ g: NSGradient) -> NSImage {
        let image = NSImage(size: NSSize(width: 4, height: 256))
        image.lockFocus()
        g.draw(in: NSRect(x: 0, y: 0, width: 4, height: 256), angle: 90)
        image.unlockFocus()
        return image
    }
}
