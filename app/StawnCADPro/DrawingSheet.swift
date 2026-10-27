import PDFKit
import SwiftUI
import UniformTypeIdentifiers

/// Make Drawing from Part: standard three views plus isometric on a sheet with the title
/// block filled from custom properties, previewed as PDF and saved as PDF, DXF or SVG.
struct DrawingSheet: View {
    @ObservedObject var model: PartModel
    @Environment(\.dismiss) private var dismiss
    @State private var paper = "A3"
    @State private var document: PDFDocument?
    @State private var error: String?

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Drawing – \(model.name) [\(model.activeConfiguration)]").font(.headline)
                Spacer()
                Picker("Sheet", selection: $paper) {
                    ForEach(["A4", "A3", "A2", "A1", "A0"], id: \.self) { Text($0) }
                }
                .frame(width: 140)
                Menu("Save As") {
                    Button("PDF…") { save("pdf") }
                    Button("DXF…") { save("dxf") }
                    Button("SVG…") { save("svg") }
                }
                .fixedSize()
                Button("Close") { dismiss() }.keyboardShortcut(.cancelAction)
            }
            .padding()
            Divider()
            PDFPreview(document: document)
                .frame(minWidth: 900, minHeight: 640)
            if let error { Text(error).foregroundStyle(.red).padding(6) }
        }
        .onAppear(perform: render)
        .onChange(of: paper) { _, _ in render() }
    }

    private func render() {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("stawn-preview-\(UUID().uuidString).pdf")
        if model.exportDrawing(to: url, paper: paper, format: "pdf") {
            document = PDFDocument(url: url)
            error = nil
        } else {
            error = "The drawing could not be created. Rebuild the part first."
        }
    }

    private func save(_ format: String) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "\(model.name).\(format)"
        panel.allowedContentTypes = [UTType(filenameExtension: format) ?? .data]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        if !model.exportDrawing(to: url, paper: paper, format: format) { error = "Saving \(format.uppercased()) failed" }
    }
}

private struct PDFPreview: NSViewRepresentable {
    let document: PDFDocument?

    func makeNSView(context: Context) -> PDFView {
        let v = PDFView()
        v.autoScales = true
        v.displayMode = .singlePage
        v.backgroundColor = .underPageBackgroundColor
        return v
    }

    func updateNSView(_ v: PDFView, context: Context) {
        if v.document !== document { v.document = document }
    }
}
