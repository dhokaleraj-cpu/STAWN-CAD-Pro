import SwiftUI

/// Start window with New, Open and recent documents, like the Welcome dialog in the recording.
struct WelcomeView: View {
    @Environment(\.dismissWindow) private var dismissWindow
    @State private var tab = 0

    private var recents: [URL] { NSDocumentController.shared.recentDocumentURLs }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("STAWN CAD Pro").font(.title2.weight(.semibold))
                Spacer()
                Picker("", selection: $tab) {
                    Text("Home").tag(0)
                    Text("Recent").tag(1)
                }
                .pickerStyle(.segmented)
                .frame(width: 200)
            }
            .padding()
            Divider()
            if tab == 0 { home } else { recent }
        }
    }

    private var home: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("New").font(.headline)
            HStack(spacing: 16) {
                tile("Part", systemImage: "cube") {
                    NSDocumentController.shared.newDocument(nil)
                    dismissWindow(id: "welcome")
                }
                tile("Sample Shaft", systemImage: "cylinder") {
                    PartFile.openSample()
                    dismissWindow(id: "welcome")
                }
                tile("Open…", systemImage: "folder") {
                    NSDocumentController.shared.openDocument(nil)
                    dismissWindow(id: "welcome")
                }
            }
            Text("Recent").font(.headline).padding(.top, 8)
            recentGrid(Array(recents.prefix(6)))
            Spacer()
        }
        .padding()
    }

    private var recent: some View {
        ScrollView { recentGrid(recents).padding() }
    }

    private func recentGrid(_ urls: [URL]) -> some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 130), spacing: 14)], alignment: .leading, spacing: 14) {
            ForEach(urls, id: \.self) { url in
                Button {
                    NSDocumentController.shared.openDocument(withContentsOf: url, display: true) { _, _, _ in }
                    dismissWindow(id: "welcome")
                } label: {
                    VStack {
                        Image(nsImage: NSWorkspace.shared.icon(forFile: url.path))
                            .resizable()
                            .frame(width: 64, height: 64)
                        Text(url.deletingPathExtension().lastPathComponent)
                            .font(.caption)
                            .lineLimit(2)
                            .multilineTextAlignment(.center)
                    }
                    .frame(width: 130, height: 110)
                    .background(RoundedRectangle(cornerRadius: 8).fill(Color(nsColor: .controlBackgroundColor)))
                }
                .buttonStyle(.plain)
            }
            if urls.isEmpty {
                Text("No recent documents").foregroundStyle(.secondary)
            }
        }
    }

    private func tile(_ title: String, systemImage: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(spacing: 8) {
                Image(systemName: systemImage).font(.system(size: 34))
                Text(title)
            }
            .frame(width: 130, height: 100)
            .background(RoundedRectangle(cornerRadius: 8).fill(Color(nsColor: .controlBackgroundColor)))
        }
        .buttonStyle(.plain)
    }
}
