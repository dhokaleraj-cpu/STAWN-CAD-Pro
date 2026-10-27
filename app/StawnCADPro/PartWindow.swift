import SwiftUI
import UniformTypeIdentifiers

enum CommandTab: String, CaseIterable, Identifiable {
    case features = "Features", sketch = "Sketch", evaluate = "Evaluate", drawing = "Drawing"
    var id: String { rawValue }
}

enum ManagerTab: String, CaseIterable, Identifiable {
    case tree = "Feature Tree", property = "Property", configurations = "Configurations"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .tree: return "list.bullet.indent"
        case .property: return "slider.horizontal.3"
        case .configurations: return "square.stack.3d.up"
        }
    }
}

/// Main part window: CommandManager on top, manager panels on the left, viewport in the
/// middle, Custom Properties task pane on the right and the status bar along the bottom.
struct PartWindow: View {
    @Binding var file: PartFile
    let fileURL: URL?

    @StateObject private var holder = ModelHolder()
    @State private var commandTab = CommandTab.features
    @State private var managerTab = ManagerTab.tree
    @State private var showTaskPane = true
    @State private var showDrawing = false
    @State private var viewCommand: ViewCommand?
    @State private var displayStyle = DisplayStyle.shadedWithEdges
    @State private var alert: String?
    @State private var sketch: SketchSession?
    @State private var featureSheet: FeatureKind?

    var body: some View {
        Group {
            if let model = holder.model {
                content(model)
            } else if let error = holder.error {
                ContentUnavailableView("Cannot open part", systemImage: "exclamationmark.triangle", description: Text(error))
            } else {
                ProgressView()
            }
        }
        .onAppear { holder.load(file.text) { file.text = $0 } }
        .alert(alert ?? "", isPresented: Binding(get: { alert != nil }, set: { if !$0 { alert = nil } })) {}
    }

    @ViewBuilder
    private func content(_ model: PartModel) -> some View {
        VStack(spacing: 0) {
            CommandManager(tab: $commandTab, model: model, showDrawing: $showDrawing, managerTab: $managerTab,
                           alert: $alert, sketch: $sketch, featureSheet: $featureSheet)
            Divider()
            HSplitView {
                VStack(spacing: 0) {
                    Picker("", selection: $managerTab) {
                        ForEach(ManagerTab.allCases) { Image(systemName: $0.icon).help($0.rawValue).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .padding(6)
                    Divider()
                    switch managerTab {
                    case .tree:
                        FeatureTree(model: model, managerTab: $managerTab) { name in
                            sketch?.close()
                            sketch = SketchSession.open(model, sketch: name)
                            if sketch != nil { commandTab = .sketch }
                        }
                    case .property: PropertyManager(model: model)
                    case .configurations: ConfigurationManager(model: model)
                    }
                }
                .frame(minWidth: 230, idealWidth: 260, maxWidth: 400)

                ZStack(alignment: .top) {
                    if let sketch {
                        SketchCanvas(session: sketch)
                        VStack {
                            Spacer()
                            HStack {
                                SketchPromptPanel(session: sketch)
                                Spacer()
                            }
                        }
                    } else {
                        Viewport(mesh: model.mesh, command: $viewCommand, style: displayStyle)
                        HeadsUpToolbar(command: $viewCommand, style: $displayStyle)
                            .padding(.top, 8)
                        VStack {
                            Spacer()
                            HStack {
                                Text("*\(viewCommand?.label ?? "Isometric")").font(.caption).foregroundStyle(.secondary)
                                Spacer()
                            }
                            .padding(10)
                        }
                    }
                }
                .frame(minWidth: 500)

                if showTaskPane {
                    CustomPropertiesPane(model: model)
                        .frame(minWidth: 260, idealWidth: 300, maxWidth: 420)
                }
            }
            Divider()
            StatusBar(model: model, showTaskPane: $showTaskPane)
        }
        .sheet(isPresented: $showDrawing) { DrawingSheet(model: model) }
        .sheet(item: $featureSheet) { kind in
            FeatureSheet(kind: kind, sketchName: featureSketch(model) ?? "") { params in
                featureSheet = nil
                guard var params, let name = featureSketch(model) else { return }
                sketch?.close()
                sketch = nil
                params["sketch"] = name
                let reply = model.command(params)
                model.commitCommands()
                if reply["ok"] as? Bool != true { alert = reply["error"] as? String ?? "The feature failed" }
                else if reply["rebuild_ok"] as? Bool == false {
                    alert = (reply["rebuild_errors"] as? [String] ?? []).joined(separator: "\n")
                }
            }
        }
        .navigationTitle(model.name + " [" + model.activeConfiguration + "]")
    }
}

/// The sketch a feature uses: the open one, else the selected one, else the last one.
private func featureSketch(_ model: PartModel) -> String? {
    let sketches = model.features.filter { $0["type"] as? String == "Sketch" }.compactMap { $0["name"] as? String }
    if let s = model.selectedFeature, sketches.contains(s) { return s }
    return sketches.last
}

enum FeatureKind: String, Identifiable {
    case extrude = "Boss-Extrude", cutExtrude = "Cut-Extrude", revolve = "Revolve", cutRevolve = "Cut-Revolve"
    var id: String { rawValue }
    var op: String {
        switch self {
        case .extrude: return "extrude"
        case .cutExtrude: return "cut_extrude"
        case .revolve: return "revolve"
        case .cutRevolve: return "cut_revolve"
        }
    }
    var isRevolve: Bool { self == .revolve || self == .cutRevolve }
}

/// Extrude and Revolve PropertyManagers: Blind or Mid Plane depth, or the revolve angle.
struct FeatureSheet: View {
    let kind: FeatureKind
    let sketchName: String
    let done: ([String: Any]?) -> Void
    @State private var depth = 10.0
    @State private var midPlane = false
    @State private var reverse = false
    @State private var angle = 360.0

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(kind.rawValue).font(.headline)
            Text("Sketch: \(sketchName.isEmpty ? "none: draw a sketch first" : sketchName)").font(.callout)
            if kind.isRevolve {
                Text("Axis of revolution: the sketch's first centerline").font(.caption).foregroundStyle(.secondary)
                HStack {
                    Text("Angle")
                    TextField("", value: $angle, format: .number).frame(width: 90)
                    Text("°")
                }
            } else {
                Picker("End Condition", selection: $midPlane) {
                    Text("Blind").tag(false)
                    Text("Mid Plane").tag(true)
                }
                .pickerStyle(.radioGroup)
                HStack {
                    Text("Depth")
                    TextField("", value: $depth, format: .number).frame(width: 90)
                    Text("mm")
                }
                Toggle("Reverse Direction", isOn: $reverse)
            }
            HStack {
                Spacer()
                Button("Cancel") { done(nil) }.keyboardShortcut(.cancelAction)
                Button("OK") {
                    done(kind.isRevolve ? ["op": kind.op, "angle": angle]
                                        : ["op": kind.op, "depth": depth, "mid_plane": midPlane, "reverse": reverse])
                }
                .keyboardShortcut(.defaultAction)
                .disabled(sketchName.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 320)
    }
}

/// Keeps the core model alive for the window and wires its edits back into the file.
final class ModelHolder: ObservableObject {
    @Published var model: PartModel?
    @Published var error: String?

    func load(_ text: String, onChange: @escaping (String) -> Void) {
        guard model == nil else { return }
        do {
            let m = try PartModel(jsonText: text)
            m.onChange = onChange
            model = m
        } catch {
            self.error = error.localizedDescription
        }
    }
}

struct CommandManager: View {
    @Binding var tab: CommandTab
    @ObservedObject var model: PartModel
    @Binding var showDrawing: Bool
    @Binding var managerTab: ManagerTab
    @Binding var alert: String?
    @Binding var sketch: SketchSession?
    @Binding var featureSheet: FeatureKind?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 4) {
                switch tab {
                case .features:
                    tool("Rebuild", "arrow.clockwise") { model.refresh() }
                    Divider().frame(height: 40)
                    tool("Extrude", "square.stack.3d.up.fill") { featureSheet = .extrude }
                    tool("Revolve", "rotate.3d") { featureSheet = .revolve }
                    tool("Extruded Cut", "scissors") { featureSheet = .cutExtrude }
                    tool("Revolved Cut", "scissors.circle") { featureSheet = .cutRevolve }
                    tool("Fillet", "circle.bottomhalf.filled") { notYet("Fillet") }
                    tool("Chamfer", "triangle") { notYet("Chamfer") }
                    Divider().frame(height: 40)
                    tool("Configurations", "square.stack.3d.up") { managerTab = .configurations }
                case .sketch:
                    Menu {
                        ForEach(["Front", "Top", "Right"], id: \.self) { plane in
                            Button("\(plane) Plane") {
                                sketch?.close()
                                sketch = SketchSession.open(model, plane: plane)
                            }
                        }
                    } label: {
                        VStack(spacing: 3) {
                            Image(systemName: "pencil.and.outline").font(.system(size: 18))
                            Text("Sketch").font(.caption2)
                        }
                        .frame(minWidth: 62, minHeight: 42)
                    }
                    .menuStyle(.borderlessButton)
                    .menuIndicator(.hidden)
                    .fixedSize()
                    tool("Exit Sketch", "arrow.uturn.backward.square") {
                        sketch?.close()
                        sketch = nil
                    }
                    .disabled(sketch == nil)
                    Divider().frame(height: 40)
                    sketchTool("Select", "cursorarrow", "select")
                    sketchTool("Line", "line.diagonal", "line")
                    sketchTool("Centerline", "line.3.horizontal.decrease", "centerline")
                    sketchTool("Rectangle", "rectangle", "rectangle")
                    sketchTool("Circle", "circle", "circle")
                    sketchTool("Arc", "rotate.right", "arc")
                    sketchTool("Smart Dimension", "ruler", "dimension")
                case .evaluate:
                    tool("Mass Properties", "scalemass") { managerTab = .property }
                    tool("Import STEP", "square.and.arrow.down") { importSTEP() }
                    tool("Export STEP", "square.and.arrow.up") { export(.step) }
                    tool("Export STL", "square.and.arrow.up.on.square") { export(.stl) }
                case .drawing:
                    tool("Make Drawing", "doc.richtext") { showDrawing = true }
                }
                Spacer()
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            HStack(spacing: 0) {
                ForEach(CommandTab.allCases) { t in
                    Button(t.rawValue) { tab = t }
                        .buttonStyle(.plain)
                        .font(.callout.weight(tab == t ? .semibold : .regular))
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(tab == t ? Color.accentColor.opacity(0.15) : .clear)
                }
                Spacer()
            }
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private func tool(_ title: String, _ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(spacing: 3) {
                Image(systemName: icon).font(.system(size: 18))
                Text(title).font(.caption2).lineLimit(1)
            }
            .frame(minWidth: 62, minHeight: 42)
        }
        .buttonStyle(.borderless)
    }

    private func notYet(_ name: String) {
        alert = "\(name) in the app arrives with its PropertyManager. It already works in the Windows app and through the command API (stawn-cli)."
    }

    private func sketchTool(_ title: String, _ icon: String, _ tool: String) -> some View {
        self.tool(title, icon) {
            if let sketch { sketch.setTool(tool) }
            else { alert = "Start a sketch first: Sketch > Front, Top or Right Plane, or double-click a sketch in the feature tree." }
        }
    }

    private func importSTEP() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "step"), UTType(filenameExtension: "stp")].compactMap { $0 }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        if let error = model.importSTEP(from: url) { alert = error }
    }

    private enum ExportKind { case step, stl }

    private func export(_ kind: ExportKind) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = model.name + (kind == .step ? ".step" : ".stl")
        panel.allowedContentTypes = [UTType(filenameExtension: kind == .step ? "step" : "stl") ?? .data]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let ok = kind == .step ? model.exportSTEP(to: url) : model.exportSTL(to: url)
        if !ok { alert = "Export failed" }
    }
}

struct StatusBar: View {
    @ObservedObject var model: PartModel
    @Binding var showTaskPane: Bool

    var body: some View {
        HStack(spacing: 16) {
            Text("STAWN CAD Pro").foregroundStyle(.secondary)
            if model.rebuildErrors.isEmpty {
                Label("Rebuilt", systemImage: "checkmark.circle").foregroundStyle(.green)
            } else {
                Label("\(model.rebuildErrors.count) rebuild error(s)", systemImage: "exclamationmark.triangle")
                    .foregroundStyle(.orange)
                    .help(model.rebuildErrors.joined(separator: "\n"))
            }
            Spacer()
            Text(String(format: "Mass %.2f g", model.mass.mass))
            Text("Editing Part")
            Text(model.activeConfiguration)
            Text("MMGS")
            Button { showTaskPane.toggle() } label: { Image(systemName: "sidebar.right") }
                .buttonStyle(.borderless)
                .help("Show or hide the task pane")
        }
        .font(.caption)
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
    }
}
