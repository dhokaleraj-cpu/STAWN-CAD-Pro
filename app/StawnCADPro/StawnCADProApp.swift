import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    static let stawnPart = UTType(exportedAs: "com.stawnprecision.part")
}

@main
struct StawnCADProApp: App {
    var body: some Scene {
        Window("Welcome to STAWN CAD Pro", id: "welcome") {
            WelcomeView()
                .frame(minWidth: 760, minHeight: 520)
        }
        .windowResizability(.contentSize)

        DocumentGroup(newDocument: PartFile()) { file in
            PartWindow(file: file.$document, fileURL: file.fileURL)
                .frame(minWidth: 1100, minHeight: 700)
        }
        .commands {
            CommandGroup(after: .newItem) {
                Button("New from Sample Shaft") { PartFile.openSample() }
                    .keyboardShortcut("n", modifiers: [.command, .shift])
                WelcomeCommand()
            }
        }
    }
}

private struct WelcomeCommand: View {
    @Environment(\.openWindow) private var openWindow
    var body: some View { Button("Welcome") { openWindow(id: "welcome") } }
}

/// The .stprt file on disk: JSON written by the core.
struct PartFile: FileDocument {
    static var readableContentTypes: [UTType] { [.stawnPart, .json] }

    var text: String

    init(text: String? = nil) {
        if let text {
            self.text = text
        } else {
            let model = PartModel.empty()
            self.text = model.fileText()
        }
    }

    init(configuration: ReadConfiguration) throws {
        guard let data = configuration.file.regularFileContents, let text = String(data: data, encoding: .utf8) else {
            throw CocoaError(.fileReadCorruptFile)
        }
        self.text = text
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: Data(text.utf8))
    }

    /// Writes the sample shaft to a temporary file and opens it as a new document.
    static func openSample() {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("DM400002_8mm_Shaft.stprt")
        let model = PartModel.sampleShaft()
        try? model.fileText().write(to: url, atomically: true, encoding: .utf8)
        NSDocumentController.shared.openDocument(withContentsOf: url, display: true) { _, _, _ in }
    }
}
