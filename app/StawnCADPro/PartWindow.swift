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
                           alert: $alert)
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
                    case .tree: FeatureTree(model: model, managerTab: $managerTab)
                    case .property: PropertyManager(model: model)
                    case .configurations: ConfigurationManager(model: model)
                    }
                }
                .frame(minWidth: 230, idealWidth: 260, maxWidth: 400)

                ZStack(alignment: .top) {
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
        .navigationTitle(model.name + " [" + model.activeConfiguration + "]")
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

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 4) {
                switch tab {
                case .features:
                    tool("Rebuild", "arrow.clockwise") { model.refresh() }
                    Divider().frame(height: 40)
                    tool("Revolve", "rotate.3d") { notYet("Revolve") }
                    tool("Extrude", "square.stack.3d.up.fill") { notYet("Extrude") }
                    tool("Cut", "scissors") { notYet("Cut") }
                    tool("Fillet", "circle.bottomhalf.filled") { notYet("Fillet") }
                    tool("Chamfer", "triangle") { notYet("Chamfer") }
                    Divider().frame(height: 40)
                    tool("Configurations", "square.stack.3d.up") { managerTab = .configurations }
                case .sketch:
                    tool("Sketch", "pencil.and.outline") { notYet("Sketch") }
                    tool("Smart Dimension", "ruler") { managerTab = .property }
                    tool("Line", "line.diagonal") { notYet("Line") }
                    tool("Circle", "circle") { notYet("Circle") }
                    tool("Arc", "rotate.right") { notYet("Arc") }
                    tool("Centerline", "line.3.horizontal.decrease") { notYet("Centerline") }
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
        alert = "\(name) editing in the app arrives with the interactive sketcher. The feature already works in the model core and in parts opened from file."
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
