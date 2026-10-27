import SwiftUI
import UniformTypeIdentifiers

/// A property template (.stprp.json): groups of controls bound to custom property keys,
/// the STAWN counterpart of a property tab template.
struct PropertyTemplate: Decodable {
    struct Control: Decodable, Identifiable {
        var key: String
        var label: String
        var type: String  // text, list, radio, date
        var options: [String]?
        var `default`: String?
        var linked: Bool?
        var id: String { key }
    }

    struct Group: Decodable, Identifiable {
        var title: String
        var controls: [Control]
        var id: String { title }
    }

    var name: String
    var groups: [Group]

    static func bundled() -> [PropertyTemplate] {
        let urls = Bundle.main.urls(forResourcesWithExtension: "json", subdirectory: nil) ?? []
        return urls.filter { $0.lastPathComponent.hasSuffix(".stprp.json") }.compactMap(load)
    }

    static func load(_ url: URL) -> PropertyTemplate? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(PropertyTemplate.self, from: data)
    }
}

/// Right-hand task pane that fills custom properties through a template.
struct CustomPropertiesPane: View {
    @ObservedObject var model: PartModel
    @State private var templates = PropertyTemplate.bundled()
    @State private var templateIndex = 0
    @State private var configurationSpecific = false
    @State private var collapsed: Set<String> = []

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Custom Properties").font(.headline)
                Spacer()
                Menu {
                    ForEach(templates.indices, id: \.self) { i in
                        Button(templates[i].name) { templateIndex = i }
                    }
                    Divider()
                    Button("Load Template…", action: loadTemplate)
                } label: { Image(systemName: "doc.badge.gearshape") }
                .menuStyle(.borderlessButton)
                .fixedSize()
                .help("Template options")
            }
            .padding(8)
            Toggle("Configuration specific (\(model.activeConfiguration))", isOn: $configurationSpecific)
                .font(.caption)
                .padding(.horizontal, 8)
            Divider().padding(.top, 6)
            if templates.indices.contains(templateIndex) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 10) {
                        ForEach(templates[templateIndex].groups) { g in group(g) }
                    }
                    .padding(8)
                }
            } else {
                rawEditor
            }
        }
        .background(Color(nsColor: .controlBackgroundColor))
    }

    private func group(_ g: PropertyTemplate.Group) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Button {
                if collapsed.contains(g.title) { collapsed.remove(g.title) } else { collapsed.insert(g.title) }
            } label: {
                HStack {
                    Text(g.title.uppercased()).font(.caption.weight(.bold))
                    Spacer()
                    Image(systemName: collapsed.contains(g.title) ? "chevron.down" : "chevron.up")
                }
            }
            .buttonStyle(.plain)
            if !collapsed.contains(g.title) {
                ForEach(g.controls) { c in control(c) }
            }
            Divider()
        }
    }

    @ViewBuilder
    private func control(_ c: PropertyTemplate.Control) -> some View {
        let resolved = model.resolvedProperties[c.key] ?? ""
        VStack(alignment: .leading, spacing: 3) {
            Text(c.label.uppercased()).font(.caption2).foregroundStyle(.secondary)
            switch c.type {
            case "list":
                Picker("", selection: binding(c)) {
                    Text("").tag("")
                    ForEach(c.options ?? [], id: \.self) { Text($0).tag($0) }
                }
                .labelsHidden()
            case "radio":
                Picker("", selection: binding(c)) {
                    ForEach(c.options ?? [], id: \.self) { Text($0).tag($0) }
                }
                .pickerStyle(.radioGroup)
                .labelsHidden()
            default:
                if c.linked == true {
                    HStack {
                        Text(resolved.isEmpty ? "–" : resolved)
                        Spacer()
                        Image(systemName: "link").foregroundStyle(.secondary).help("Linked to \(c.default ?? "")")
                    }
                    .onAppear { ensureDefault(c) }
                } else {
                    CommitField(value: model.properties[c.key] ?? "", display: resolved) { set(c.key, $0) }
                        .onAppear { ensureDefault(c) }
                }
            }
        }
    }

    private var rawEditor: some View {
        List(model.resolvedProperties.sorted(by: { $0.key < $1.key }), id: \.key) { kv in
            LabeledContent(kv.key, value: kv.value)
        }
    }

    private func binding(_ c: PropertyTemplate.Control) -> Binding<String> {
        Binding(get: { model.resolvedProperties[c.key] ?? "" }, set: { set(c.key, $0) })
    }

    private func set(_ key: String, _ value: String) {
        model.setProperty(key, value, configurationSpecific: configurationSpecific)
    }

    private func ensureDefault(_ c: PropertyTemplate.Control) {
        if let d = c.default, model.properties[c.key] == nil { set(c.key, d) }
    }

    private func loadTemplate() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url, let t = PropertyTemplate.load(url) else { return }
        templates.append(t)
        templateIndex = templates.count - 1
    }
}

/// Text field that writes its value when editing ends instead of on every keystroke.
/// While not being edited it shows the evaluated value (e.g. the part name for ${PartName});
/// while focused it shows the stored text so links can be edited.
struct CommitField: View {
    let value: String
    var display: String? = nil
    let commit: (String) -> Void
    @State private var text = ""
    @FocusState private var focused: Bool

    var body: some View {
        TextField("", text: $text)
            .textFieldStyle(.roundedBorder)
            .focused($focused)
            .onAppear { text = display ?? value }
            .onChange(of: value) { _, v in if !focused { text = display ?? v } }
            .onChange(of: display) { _, d in if !focused { text = d ?? value } }
            .onChange(of: focused) { _, f in
                if f {
                    text = value
                } else {
                    if text != value { commit(text) }
                    text = display ?? text
                }
            }
            .onSubmit { if text != value { commit(text) } }
    }
}
