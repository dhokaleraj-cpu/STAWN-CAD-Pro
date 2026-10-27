import AppKit
import SwiftUI

/// An open sketch: the core's sketch editor state (the sketch_input command) plus the sketch
/// itself (get_sketch). Shared by the canvas, the prompt overlay and the CommandManager.
final class SketchSession: ObservableObject {
    let model: PartModel
    @Published private(set) var sketch: [String: Any]
    @Published private(set) var editor: [String: Any] = [:]
    /// Set when Smart Dimension has picked geometry and needs a value (the Modify box).
    @Published var dimensionRequest: [[String: Any]]?
    var scale: CGFloat = 4  // points per millimetre, kept by the canvas for the snap distance

    init(model: PartModel, sketch: [String: Any]) {
        self.model = model
        self.sketch = sketch
    }

    var name: String { sketch["sketch"] as? String ?? "" }
    var status: String { sketch["status"] as? String ?? "" }
    var degreesOfFreedom: Int { sketch["degrees_of_freedom"] as? Int ?? 0 }
    var prompt: String { editor["prompt"] as? String ?? "" }
    var relations: [String] { editor["relations"] as? [String] ?? [] }

    func setTool(_ tool: String) { send(["event": "tool", "tool": tool]) }
    func addRelation(_ index: Int) { send(["event": "relation", "index": index]) }
    func addDimension(option: Int, value: Double) { send(["event": "dimension", "option": option, "value": value]) }
    func escape() { send(["event": "escape"]) }
    func deleteSelection() { send(["event": "delete"]) }

    func send(_ event: [String: Any], at point: CGPoint? = nil) {
        var e = event
        e["op"] = "sketch_input"
        if let p = point {
            e["x"] = Double(p.x)
            e["y"] = Double(p.y)
        }
        e["tol"] = Double(8 / scale)  // snap within 8 points on screen
        let reply = model.command(e)
        guard reply["ok"] as? Bool == true else { return }
        editor = reply
        if reply["changed"] as? Bool == true { reload() }
        if event["event"] as? String == "click", let request = reply["dimension_request"] as? [String: Any],
           let options = request["options"] as? [[String: Any]], !options.isEmpty {
            dimensionRequest = options
        }
    }

    /// Opens an existing sketch, or a new one on a plane, for editing with the Line tool active.
    static func open(_ model: PartModel, sketch name: String? = nil, plane: String? = nil) -> SketchSession? {
        var sketchName = name
        if let plane {
            sketchName = model.command(["op": "create_sketch", "plane": plane])["sketch"] as? String
        }
        guard let sketchName else { return nil }
        let state = model.command(["op": "edit_sketch", "sketch": sketchName])
        guard state["ok"] as? Bool == true else { return nil }
        model.commitCommands()  // shows a new sketch in the feature tree
        model.selectedFeature = sketchName
        let session = SketchSession(model: model, sketch: state)
        session.setTool("line")
        return session
    }

    /// Closes the sketch and rebuilds, so the features that use it update.
    func close() {
        model.command(["op": "close_sketch"])
        model.commitCommands()
    }

    func reload() {
        let s = model.command(["op": "get_sketch"])
        if s["ok"] as? Bool == true { sketch = s }
    }
}

/// The graphics area while a sketch is open, looking normal to the sketch plane. Geometry that
/// can still move is blue, defined geometry black, over-defined geometry red. Scroll to pan,
/// pinch or hold Option and scroll to zoom, Esc ends the tool, Delete removes the selection.
struct SketchCanvas: NSViewRepresentable {
    @ObservedObject var session: SketchSession

    func makeNSView(context: Context) -> SketchNSView {
        let v = SketchNSView()
        v.session = session
        DispatchQueue.main.async {
            v.zoomToFit()
            v.window?.makeFirstResponder(v)
        }
        return v
    }

    func updateNSView(_ view: SketchNSView, context: Context) {
        if view.session !== session {
            view.session = session
            view.zoomToFit()
        }
        view.needsDisplay = true
    }
}

final class SketchNSView: NSView {
    weak var session: SketchSession?
    private var pan = CGPoint.zero  // screen offset of the sketch origin from the view centre
    private var scale: CGFloat = 4 {
        didSet { session?.scale = scale }
    }

    override var isFlipped: Bool { false }  // y up, like the sketch
    override var acceptsFirstResponder: Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }

    // MARK: Coordinates

    private var centre: CGPoint { CGPoint(x: bounds.midX, y: bounds.midY) }
    private func screen(_ x: Double, _ y: Double) -> CGPoint {
        CGPoint(x: centre.x + pan.x + CGFloat(x) * scale, y: centre.y + pan.y + CGFloat(y) * scale)
    }
    private func sketchPoint(_ p: CGPoint) -> CGPoint {
        CGPoint(x: (p.x - centre.x - pan.x) / scale, y: (p.y - centre.y - pan.y) / scale)
    }

    func zoomToFit() {
        let pts = points()
        var minX = -10.0, minY = -10.0, maxX = 10.0, maxY = 10.0
        for p in pts { minX = min(minX, p.x); maxX = max(maxX, p.x); minY = min(minY, p.y); maxY = max(maxY, p.y) }
        for c in array("circles") {
            let ctr = pts[int(c, "center")], r = num(c, "radius")
            minX = min(minX, ctr.x - r); maxX = max(maxX, ctr.x + r)
            minY = min(minY, ctr.y - r); maxY = max(maxY, ctr.y + r)
        }
        let w = max(bounds.width, 200), h = max(bounds.height, 200)
        scale = min(w / CGFloat(maxX - minX), h / CGFloat(maxY - minY)) * 0.7
        pan = CGPoint(x: -CGFloat(minX + maxX) / 2 * scale, y: -CGFloat(minY + maxY) / 2 * scale)
        needsDisplay = true
    }

    // MARK: Sketch JSON

    private var geometry: [String: Any] { session?.sketch["geometry"] as? [String: Any] ?? [:] }
    private func array(_ key: String) -> [[String: Any]] { geometry[key] as? [[String: Any]] ?? [] }
    private func int(_ d: [String: Any], _ key: String) -> Int { d[key] as? Int ?? -1 }
    private func num(_ d: [String: Any], _ key: String) -> Double { d[key] as? Double ?? 0 }
    private func points() -> [(x: Double, y: Double)] { array("points").map { (num($0, "x"), num($0, "y")) } }

    private func defined(_ key: String, _ i: Int) -> Bool {
        guard let d = session?.sketch["defined"] as? [String: Any], let a = d[key] as? [Bool], i >= 0, i < a.count else { return false }
        return a[i]
    }

    private func selected(_ kind: String, _ index: Int) -> Bool {
        (session?.editor["selection"] as? [[String: Any]] ?? []).contains { $0["kind"] as? String == kind && $0["index"] as? Int == index }
    }

    private func colour(_ kind: String, _ index: Int, defined: Bool) -> NSColor {
        if selected(kind, index) { return .systemOrange }
        if session?.status == "Over Defined" { return .systemRed }
        return defined ? .black : NSColor(calibratedRed: 0, green: 0.25, blue: 1, alpha: 1)
    }

    // MARK: Drawing

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.98, alpha: 1).setFill()
        bounds.fill()
        let o = screen(0, 0)
        stroke([o, CGPoint(x: o.x + 40, y: o.y)], .systemRed, 1.2)
        stroke([o, CGPoint(x: o.x, y: o.y + 40)], .systemGreen, 1.2)
        guard session != nil else { return }
        let pts = points()
        func p(_ i: Int) -> CGPoint { screen(pts[i].x, pts[i].y) }

        let lines = array("lines"), circles = array("circles"), arcs = array("arcs")
        for (i, l) in lines.enumerated() {
            let a = int(l, "p1"), b = int(l, "p2")
            stroke([p(a), p(b)], colour("line", i, defined: defined("points", a) && defined("points", b)),
                   selected("line", i) ? 2.5 : 1.6, dashed: l["construction"] as? Bool == true)
        }
        for (i, c) in circles.enumerated() {
            let ctr = int(c, "center"), r = CGFloat(num(c, "radius")) * scale
            let path = NSBezierPath(ovalIn: CGRect(x: p(ctr).x - r, y: p(ctr).y - r, width: 2 * r, height: 2 * r))
            stroke(path, colour("circle", i, defined: defined("points", ctr) && defined("circles", i)),
                   selected("circle", i) ? 2.5 : 1.6, dashed: c["construction"] as? Bool == true)
        }
        for (i, a) in arcs.enumerated() {
            let c = int(a, "center"), s = int(a, "start"), e = int(a, "end")
            let a0 = atan2(pts[s].y - pts[c].y, pts[s].x - pts[c].x), a1 = atan2(pts[e].y - pts[c].y, pts[e].x - pts[c].x)
            let r = hypot(pts[s].x - pts[c].x, pts[s].y - pts[c].y)
            var sweep = a1 - a0
            if sweep <= 0 { sweep += 2 * .pi }
            stroke(arcPath(pts[c].x, pts[c].y, r, a0, sweep),
                   colour("arc", i, defined: defined("points", c) && defined("points", s) && defined("points", e)),
                   selected("arc", i) ? 2.5 : 1.6, dashed: a["construction"] as? Bool == true)
        }

        var used = Set([0])
        for l in lines { used.insert(int(l, "p1")); used.insert(int(l, "p2")) }
        for c in circles { used.insert(int(c, "center")) }
        for a in arcs { used.insert(int(a, "center")); used.insert(int(a, "start")); used.insert(int(a, "end")) }
        for i in used where i >= 0 && i < pts.count {
            colour("point", i, defined: defined("points", i)).setFill()
            CGRect(x: p(i).x - 2.5, y: p(i).y - 2.5, width: 5, height: 5).fill()
        }

        drawDimensions(pts, lines: lines, circles: circles, arcs: arcs)
        drawPreview()

        if let snap = session?.editor["snap"] as? [String: Any] {
            let sp = screen(num(snap, "x"), num(snap, "y"))
            stroke(NSBezierPath(ovalIn: CGRect(x: sp.x - 6, y: sp.y - 6, width: 12, height: 12)), .systemYellow, 1.5)
            if let kind = snap["kind"] as? String, kind == "horizontal" || kind == "vertical" {
                (kind == "horizontal" ? "—" : "|").draw(at: CGPoint(x: sp.x + 10, y: sp.y - 16),
                                                        withAttributes: [.foregroundColor: NSColor.systemOrange])
            }
        }
    }

    private func arcPath(_ cx: Double, _ cy: Double, _ r: Double, _ start: Double, _ sweep: Double) -> NSBezierPath {
        let path = NSBezierPath()
        path.appendArc(withCenter: screen(cx, cy), radius: CGFloat(r) * scale,
                       startAngle: CGFloat(start * 180 / .pi), endAngle: CGFloat((start + sweep) * 180 / .pi), clockwise: false)
        return path
    }

    private func stroke(_ pts: [CGPoint], _ colour: NSColor, _ width: CGFloat, dashed: Bool = false) {
        let path = NSBezierPath()
        path.move(to: pts[0])
        for q in pts.dropFirst() { path.line(to: q) }
        stroke(path, colour, width, dashed: dashed)
    }

    private func stroke(_ path: NSBezierPath, _ colour: NSColor, _ width: CGFloat, dashed: Bool = false) {
        path.lineWidth = width
        if dashed { path.setLineDash([6, 4], count: 2, phase: 0) }
        colour.setStroke()
        path.stroke()
    }

    private func drawPreview() {
        guard let pv = session?.editor["preview"] as? [String: Any] else { return }
        let grey = NSColor(calibratedWhite: 0.45, alpha: 1)
        for l in pv["lines"] as? [[String: Any]] ?? [] {
            stroke([screen(num(l, "x1"), num(l, "y1")), screen(num(l, "x2"), num(l, "y2"))], grey, 1.2,
                   dashed: l["construction"] as? Bool == true)
        }
        for c in pv["circles"] as? [[String: Any]] ?? [] {
            let ctr = screen(num(c, "cx"), num(c, "cy")), r = CGFloat(num(c, "r")) * scale
            stroke(NSBezierPath(ovalIn: CGRect(x: ctr.x - r, y: ctr.y - r, width: 2 * r, height: 2 * r)), grey, 1.2)
        }
        for a in pv["arcs"] as? [[String: Any]] ?? [] {
            let a0 = num(a, "start_angle") * .pi / 180, a1 = num(a, "end_angle") * .pi / 180
            stroke(arcPath(num(a, "cx"), num(a, "cy"), num(a, "r"), a0, a1 - a0), grey, 1.2)
        }
    }

    private func drawDimensions(_ pts: [(x: Double, y: Double)], lines: [[String: Any]], circles: [[String: Any]], arcs: [[String: Any]]) {
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: NSColor.darkGray]
        for c in array("constraints") where c["name"] != nil {
            let type = c["type"] as? String ?? "", a = int(c, "a"), b = int(c, "b"), kindA = c["kindA"] as? String ?? "point"
            var text = String(format: "%g", (num(c, "value") * 100).rounded() / 100)
            var at: (x: Double, y: Double)
            switch type {
            case "length" where a >= 0 && a < lines.count:
                let p1 = pts[int(lines[a], "p1")], p2 = pts[int(lines[a], "p2")]
                at = ((p1.x + p2.x) / 2, (p1.y + p2.y) / 2)
            case "radius", "diameter":
                let isArc = kindA == "arc"
                guard a >= 0, a < (isArc ? arcs.count : circles.count) else { continue }
                let ctr = isArc ? int(arcs[a], "center") : int(circles[a], "center")
                let r = isArc ? hypot(pts[int(arcs[a], "start")].x - pts[ctr].x, pts[int(arcs[a], "start")].y - pts[ctr].y)
                              : num(circles[a], "radius")
                at = (pts[ctr].x + r * 0.7071, pts[ctr].y + r * 0.7071)
                text = (type == "diameter" ? "Ø" : "R") + text
            case "angle" where a >= 0 && a < lines.count:
                at = pts[int(lines[a], "p1")]
                text += "°"
            default:
                guard a >= 0, b >= 0, a < pts.count, b < pts.count else { continue }
                at = ((pts[a].x + pts[b].x) / 2, (pts[a].y + pts[b].y) / 2)
            }
            let s = screen(at.x, at.y)
            let size = (text as NSString).size(withAttributes: attrs)
            (text as NSString).draw(at: CGPoint(x: s.x - size.width / 2, y: s.y + 8), withAttributes: attrs)
        }
    }

    // MARK: Input

    override func mouseMoved(with event: NSEvent) {
        session?.send(["event": "move"], at: sketchPoint(convert(event.locationInWindow, from: nil)))
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let add = event.modifierFlags.contains(.command) || event.modifierFlags.contains(.shift)
        session?.send(["event": "click", "add": add], at: sketchPoint(convert(event.locationInWindow, from: nil)))
        needsDisplay = true
    }

    override func otherMouseDragged(with event: NSEvent) { panBy(event.deltaX, -event.deltaY) }
    override func rightMouseDragged(with event: NSEvent) { panBy(event.deltaX, -event.deltaY) }

    override func scrollWheel(with event: NSEvent) {
        if event.modifierFlags.contains(.option) {
            zoom(by: pow(1.01, event.scrollingDeltaY), at: convert(event.locationInWindow, from: nil))
        } else {
            panBy(event.scrollingDeltaX, -event.scrollingDeltaY)
        }
    }

    override func magnify(with event: NSEvent) {
        zoom(by: 1 + event.magnification, at: convert(event.locationInWindow, from: nil))
    }

    private func panBy(_ dx: CGFloat, _ dy: CGFloat) {
        pan.x += dx
        pan.y += dy
        needsDisplay = true
    }

    private func zoom(by factor: CGFloat, at p: CGPoint) {
        let cursor = CGPoint(x: p.x - centre.x, y: p.y - centre.y)
        pan = CGPoint(x: cursor.x + (pan.x - cursor.x) * factor, y: cursor.y + (pan.y - cursor.y) * factor)
        scale *= factor
        needsDisplay = true
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 53: session?.escape()                           // Esc
        case 51, 117: session?.deleteSelection()             // Delete, Forward Delete
        case 3 where event.modifierFlags.intersection(.deviceIndependentFlagsMask).isEmpty: zoomToFit()  // F
        default: super.keyDown(with: event)
        }
        needsDisplay = true
    }
}

/// Sketch mode's overlay: the prompt and the Add Relation choices for the selection.
struct SketchPromptPanel: View {
    @ObservedObject var session: SketchSession

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(session.prompt).font(.callout.weight(.semibold))
            if !session.relations.isEmpty {
                HStack {
                    Text("Add Relation:").font(.callout)
                    ForEach(Array(session.relations.enumerated()), id: \.offset) { i, label in
                        Button(label) { session.addRelation(i) }
                    }
                }
            }
            Text(session.status == "Under Defined" ? "Under Defined (\(session.degreesOfFreedom) free)" : session.status)
                .font(.caption)
                .foregroundStyle(session.status == "Fully Defined" ? Color.primary : session.status == "Under Defined" ? .blue : .red)
        }
        .padding(10)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
        .padding(12)
        .sheet(isPresented: Binding(get: { session.dimensionRequest != nil }, set: { if !$0 { session.dimensionRequest = nil } })) {
            if let options = session.dimensionRequest {
                ModifyDimensionSheet(options: options) { result in
                    session.dimensionRequest = nil
                    if let result { session.addDimension(option: result.option, value: result.value) } else { session.escape() }
                }
            }
        }
    }
}

/// Smart Dimension's Modify box.
struct ModifyDimensionSheet: View {
    let options: [[String: Any]]
    let done: ((option: Int, value: Double)?) -> Void
    @State private var option = 0
    @State private var value: Double

    init(options: [[String: Any]], done: @escaping ((option: Int, value: Double)?) -> Void) {
        self.options = options
        self.done = done
        _value = State(initialValue: options.first?["value"] as? Double ?? 0)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Modify").font(.headline)
            if options.count > 1 {
                Picker("Dimension", selection: $option) {
                    ForEach(options.indices, id: \.self) { i in Text(options[i]["label"] as? String ?? "").tag(i) }
                }
                .pickerStyle(.radioGroup)
                .onChange(of: option) { _, i in value = options[i]["value"] as? Double ?? value }
            }
            HStack {
                TextField("Value", value: $value, format: .number.precision(.fractionLength(0...4)))
                    .frame(width: 140)
                    .onSubmit { done((option, value)) }
                Text(options[option]["unit"] as? String == "deg" ? "°" : "mm")
            }
            HStack {
                Spacer()
                Button("Cancel") { done(nil) }.keyboardShortcut(.cancelAction)
                Button("OK") { done((option, value)) }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 300)
    }
}
