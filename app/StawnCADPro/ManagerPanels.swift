import SwiftUI

/// Feature tree: the part, its folders and reference geometry, then the features in order.
struct FeatureTree: View {
    @ObservedObject var model: PartModel
    @Binding var managerTab: ManagerTab
    var editSketch: (String) -> Void = { _ in }
    @State private var renaming: String?
    @State private var newName = ""

    var body: some View {
        List(selection: $model.selectedFeature) {
            Label(model.name + " (\(model.activeConfiguration))", systemImage: "cube").font(.callout.weight(.semibold))
            Group {
                Label("History", systemImage: "clock.arrow.circlepath")
                Label("Annotations", systemImage: "textformat")
                Label("Solid Bodies (\(model.mesh == nil ? 0 : 1))", systemImage: "cube.fill")
                Label(model.material.name.isEmpty ? "Material <not specified>" : model.material.name,
                      systemImage: "square.grid.3x3.fill")
                Label("Front Plane", systemImage: "square")
                Label("Top Plane", systemImage: "square")
                Label("Right Plane", systemImage: "square")
                Label("Origin", systemImage: "scope")
            }
            .foregroundStyle(.secondary)

            ForEach(model.features.indices, id: \.self) { i in
                let f = model.features[i]
                let name = f["name"] as? String ?? "?"
                row(name: name, type: f["type"] as? String ?? "")
                    .tag(name)
                    .contextMenu {
                        if f["type"] as? String == "Sketch" {
                            Button("Edit Sketch") { editSketch(name) }
                        }
                        Button("Edit Feature") {
                            model.selectedFeature = name
                            managerTab = .property
                        }
                        Button(model.isSuppressed(name) ? "Unsuppress" : "Suppress") {
                            model.setSuppressed(name, !model.isSuppressed(name))
                        }
                        Button("Rename…") {
                            newName = name
                            renaming = name
                        }
                    }
            }
        }
        .listStyle(.sidebar)
        .onTapGesture(count: 2) {
            guard let name = model.selectedFeature else { return }
            if model.features.first(where: { $0["name"] as? String == name })?["type"] as? String == "Sketch" { editSketch(name) }
            else { managerTab = .property }
        }
        .alert("Rename Feature", isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
            TextField("Name", text: $newName)
            Button("OK") {
                if let r = renaming { model.renameFeature(r, to: newName) }
                renaming = nil
            }
            Button("Cancel", role: .cancel) { renaming = nil }
        }
    }

    private func row(name: String, type: String) -> some View {
        let suppressed = model.isSuppressed(name)
        let error = model.errorFor(name)
        return HStack {
            Image(systemName: icon(type))
            Text(name)
            if error != nil { Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.red).help(error ?? "") }
        }
        .foregroundStyle(suppressed ? Color.secondary : Color.primary)
        .opacity(suppressed ? 0.6 : 1)
    }

    private func icon(_ type: String) -> String {
        switch type {
        case "Sketch": return "pencil.and.outline"
        case "Revolve": return "rotate.3d"
        case "Extrude": return "square.stack.3d.up.fill"
        case "Fillet": return "circle.bottomhalf.filled"
        case "Chamfer": return "triangle"
        case "Import": return "shippingbox"
        default: return "questionmark.square"
        }
    }
}

/// Property panel for the selected feature: its driving dimensions, with the choice to apply
/// a change to this configuration only, plus mass properties of the body.
struct PropertyManager: View {
    @ObservedObject var model: PartModel
    @State private var thisConfigurationOnly = false

    var body: some View {
        Form {
            if let f = model.selectedFeature {
                Section(f) {
                    if model.configurations.count > 1 {
                        Picker("Apply changes to", selection: $thisConfigurationOnly) {
                            Text("All configurations").tag(false)
                            Text("This configuration (\(model.activeConfiguration))").tag(true)
                        }
                    }
                    let dims = model.dimensions(of: f)
                    if dims.isEmpty { Text("No dimensions").foregroundStyle(.secondary) }
                    ForEach(dims, id: \.name) { d in
                        DimensionField(name: d.name, value: d.value) { v in
                            model.setDimension(d.name, v, thisConfigurationOnly: thisConfigurationOnly)
                        }
                    }
                    Toggle("Suppressed", isOn: Binding(get: { model.isSuppressed(f) },
                                                      set: { model.setSuppressed(f, $0) }))
                    if let e = model.errorFor(f) { Text(e).foregroundStyle(.red) }
                }
            } else {
                Text("Select a feature in the feature tree").foregroundStyle(.secondary)
            }
            Section("Mass Properties") {
                LabeledContent("Material", value: model.material.name.isEmpty ? "–" : model.material.name)
                LabeledContent("Density", value: String(format: "%.0f kg/m³", model.material.density))
                LabeledContent("Volume", value: String(format: "%.2f mm³", model.mass.volume))
                LabeledContent("Surface area", value: String(format: "%.2f mm²", model.mass.area))
                LabeledContent("Mass", value: String(format: "%.3f g", model.mass.mass))
                LabeledContent("Centre of mass",
                               value: String(format: "%.2f, %.2f, %.2f", model.mass.cx, model.mass.cy, model.mass.cz))
            }
        }
        .formStyle(.grouped)
    }
}

private struct DimensionField: View {
    let name: String
    let value: Double
    let commit: (Double) -> Void
    @State private var text = ""

    var body: some View {
        LabeledContent(name) {
            TextField("", text: $text)
                .multilineTextAlignment(.trailing)
                .frame(width: 90)
                .onSubmit { if let v = Double(text.replacingOccurrences(of: ",", with: ".")) { commit(v) } }
        }
        .onAppear { text = format(value) }
        .onChange(of: value) { _, v in text = format(v) }
    }

    private func format(_ v: Double) -> String {
        var s = String(format: "%.3f", v)
        while s.hasSuffix("0") { s.removeLast() }
        if s.hasSuffix(".") { s.removeLast() }
        return s
    }
}

/// Configurations list with the Configuration Properties panel for the selected one.
struct ConfigurationManager: View {
    @ObservedObject var model: PartModel
    @State private var editing: String?
    @State private var name = ""
    @State private var description = ""
    @State private var bomNumber = ""
    @State private var adding = false
    @State private var newConfig = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            List {
                Section(model.name + " Configuration(s)") {
                    ForEach(model.configurations.indices, id: \.self) { i in
                        let c = model.configurations[i]
                        let n = c["name"] as? String ?? "?"
                        HStack {
                            Image(systemName: n == model.activeConfiguration ? "checkmark.circle.fill" : "circle")
                                .foregroundStyle(n == model.activeConfiguration ? .green : .secondary)
                            Text(n)
                            if let d = c["description"] as? String, !d.isEmpty, d != n {
                                Text("[\(d)]").foregroundStyle(.secondary)
                            }
                        }
                        .contentShape(Rectangle())
                        .onTapGesture(count: 2) { model.activate(configuration: n) }
                        .contextMenu {
                            Button("Show Configuration") { model.activate(configuration: n) }
                            Button("Properties…") { beginEditing(c) }
                            Button("Delete", role: .destructive) { model.deleteConfiguration(n) }
                                .disabled(n == model.activeConfiguration)
                        }
                    }
                }
            }
            .listStyle(.sidebar)
            HStack {
                Button { adding = true } label: { Label("Add Configuration", systemImage: "plus") }
                Spacer()
            }
            .padding(8)
            if editing != nil {
                Divider()
                Form {
                    Section("Configuration Properties") {
                        TextField("Configuration name", text: $name)
                        TextField("Description", text: $description)
                        TextField("Part number in bill of materials", text: $bomNumber, prompt: Text(model.name))
                    }
                    HStack {
                        Button("Cancel") { editing = nil }
                        Spacer()
                        Button("OK") {
                            if let e = editing {
                                model.updateConfiguration(e, newName: name, description: description, bomPartNumber: bomNumber)
                            }
                            editing = nil
                        }
                        .keyboardShortcut(.defaultAction)
                    }
                }
                .formStyle(.grouped)
                .frame(height: 230)
            }
        }
        .alert("Add Configuration", isPresented: $adding) {
            TextField("Configuration name", text: $newConfig)
            Button("Add") {
                model.addConfiguration(named: newConfig)
                newConfig = ""
            }
            Button("Cancel", role: .cancel) {}
        }
    }

    private func beginEditing(_ c: [String: Any]) {
        editing = c["name"] as? String
        name = editing ?? ""
        description = c["description"] as? String ?? ""
        bomNumber = c["bomPartNumber"] as? String ?? ""
    }
}
