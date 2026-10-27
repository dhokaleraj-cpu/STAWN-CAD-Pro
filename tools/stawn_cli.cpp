// stawn-cli: command line access to the STAWN CAD Pro core.
//
//   stawn-cli sample <out.stprt>                  write the sample 8 mm shaft
//   stawn-cli info <part.stprt> [configuration]   rebuild and print features, mass and properties
//   stawn-cli set <part.stprt> D1@Sketch1=4.5 ... change dimensions and save
//   stawn-cli export <part.stprt> <out.step|out.stl> [configuration]
//   stawn-cli drawing <part.stprt> <out.pdf|out.dxf|out.svg> [paper] [configuration]
#include <cstdio>
#include <iostream>
#include <string>

#include "stawn/Document.hpp"
#include "stawn/Drawing.hpp"
#include "stawn/Samples.hpp"

using namespace stawn;

namespace {

int usage() {
    std::cerr << "usage:\n"
                 "  stawn-cli sample <out.stprt>\n"
                 "  stawn-cli info <part.stprt> [configuration]\n"
                 "  stawn-cli set <part.stprt> NAME=VALUE...\n"
                 "  stawn-cli export <part.stprt> <out.step|out.stl> [configuration]\n"
                 "  stawn-cli drawing <part.stprt> <out.pdf|out.dxf|out.svg> [paper] [configuration]\n";
    return 2;
}

bool endsWith(const std::string& s, const std::string& suffix) {
    return s.size() >= suffix.size() && s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0;
}

std::unique_ptr<PartDocument> open(const std::string& path, const char* configuration) {
    std::string err;
    auto doc = PartDocument::load(path, &err);
    if (!doc) {
        std::cerr << err << "\n";
        return nullptr;
    }
    if (configuration) {
        if (!doc->findConfiguration(configuration)) {
            std::cerr << "No configuration named " << configuration << "\n";
            return nullptr;
        }
        doc->activeConfiguration = configuration;
    }
    RebuildResult r = doc->rebuild();
    for (auto& e : r.errors) std::cerr << "rebuild error: " << e << "\n";
    return doc;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 3) return usage();
    std::string cmd = argv[1];

    if (cmd == "sample") {
        auto doc = makeSampleShaft();
        if (!doc->save(argv[2])) return 1;
        std::cout << "Wrote " << argv[2] << "\n";
        return 0;
    }

    if (cmd == "info") {
        auto doc = open(argv[2], argc > 3 ? argv[3] : nullptr);
        if (!doc) return 1;
        std::cout << doc->name << " [" << doc->activeConfiguration << "]\n";
        for (auto& f : doc->features) {
            std::cout << "  " << (doc->isSuppressed(f) ? "(suppressed) " : "") << f.name << " (" << toString(f.type) << ")";
            if (f.type == FeatureType::Sketch) {
                Sketch s = f.sketch;
                std::cout << " " << toString(s.solve().status);
            }
            if (!f.error.empty()) std::cout << " ERROR: " << f.error;
            std::cout << "\n";
        }
        MassProperties m = doc->massProperties();
        std::printf("Material: %s (%.0f kg/m^3)\nVolume: %.3f mm^3\nArea: %.3f mm^2\nMass: %.3f g\n",
                    doc->material.name.c_str(), doc->material.density, m.volume, m.area, m.mass);
        std::cout << "Dimensions:\n";
        for (auto& [k, v] : doc->listDimensions()) std::cout << "  " << k << " = " << v << "\n";
        std::cout << "Custom properties:\n";
        for (auto& [k, v] : doc->resolvedProperties()) std::cout << "  " << k << " = " << v << "\n";
        return 0;
    }

    if (cmd == "set") {
        auto doc = open(argv[2], nullptr);
        if (!doc) return 1;
        for (int i = 3; i < argc; ++i) {
            std::string a = argv[i];
            auto eq = a.find('=');
            if (eq == std::string::npos || !doc->setDimension(a.substr(0, eq), std::stod(a.substr(eq + 1)))) {
                std::cerr << "Cannot set " << a << "\n";
                return 1;
            }
        }
        RebuildResult r = doc->rebuild();
        if (!r.ok) {
            for (auto& e : r.errors) std::cerr << e << "\n";
            return 1;
        }
        return doc->save(argv[2]) ? 0 : 1;
    }

    if (cmd == "export" && argc >= 4) {
        auto doc = open(argv[2], argc > 4 ? argv[4] : nullptr);
        if (!doc) return 1;
        std::string out = argv[3];
        bool ok = endsWith(out, ".stl") ? doc->exportSTL(out) : doc->exportSTEP(out);
        std::cout << (ok ? "Wrote " : "Failed to write ") << out << "\n";
        return ok ? 0 : 1;
    }

    if (cmd == "drawing" && argc >= 4) {
        auto doc = open(argv[2], argc > 5 ? argv[5] : nullptr);
        if (!doc) return 1;
        DrawingSpec spec = DrawingSpec::standardThreeView(*doc, argc > 4 ? argv[4] : "A3");
        spec.sheetName = doc->activeConfiguration;
        Sheet2D sheet = renderDrawing(*doc, spec);
        std::string out = argv[3];
        bool ok = endsWith(out, ".dxf") ? exportDXF(sheet, out) : endsWith(out, ".svg") ? exportSVG(sheet, out) : exportPDF(sheet, out);
        std::cout << (ok ? "Wrote " : "Failed to write ") << out << "\n";
        return ok ? 0 : 1;
    }

    return usage();
}
