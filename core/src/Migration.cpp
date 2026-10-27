#include "stawn/Migration.hpp"

#include <algorithm>
#include <cstring>
#include <fstream>
#include <nlohmann/json.hpp>
#include <sstream>

using nlohmann::json;

namespace stawn {

namespace {

std::string dirOf(const std::string& path) {
    auto slash = path.find_last_of("/\\");
    return slash == std::string::npos ? std::string() : path.substr(0, slash + 1);
}

bool isAbsolute(const std::string& p) {
    return !p.empty() && (p[0] == '/' || p[0] == '\\' || (p.size() > 1 && p[1] == ':'));
}

std::map<std::string, std::string> readProperties(const json& j) {
    std::map<std::string, std::string> out;
    if (!j.is_object()) return out;
    for (auto& [key, v] : j.items()) {
        if (v.is_string()) {
            out[key] = v.get<std::string>();
        } else if (v.is_object()) {
            out[key] = translatePropertyValue(v.value("expression", ""), v.value("resolved", ""));
        }
    }
    return out;
}

}  // namespace

std::string translatePropertyValue(const std::string& expression, const std::string& resolved) {
    static const std::pair<const char*, const char*> links[] = {
        {"SW-Mass", "${Mass}"},          {"SW-Volume", "${Volume}"},
        {"SW-Material", "${Material}"},  {"SW-Density", "${Density}"},
        {"SW-File Name", "${PartName}"}, {"SW-Configuration Name", "${Configuration}"},
    };
    // A plain link such as "SW-Mass@DM400002.SLDPRT" (quotes optional) maps to a STAWN link.
    std::string e = expression;
    e.erase(std::remove(e.begin(), e.end(), '"'), e.end());
    for (auto& [from, to] : links)
        if (e.rfind(from, 0) == 0 && (e.size() == std::strlen(from) || e[std::strlen(from)] == '@')) return to;
    return resolved.empty() ? expression : resolved;
}

std::unique_ptr<PartDocument> partFromManifest(const std::string& manifestPath, std::string* error) {
    auto fail = [&](const std::string& m) -> std::unique_ptr<PartDocument> {
        if (error) *error = m;
        return nullptr;
    };
    std::ifstream in(manifestPath);
    if (!in) return fail("Cannot open " + manifestPath);
    json m;
    try {
        m = json::parse(in);
    } catch (const std::exception& e) {
        return fail(std::string("Invalid manifest: ") + e.what());
    }
    auto configs = m.value("configurations", json::array());
    if (configs.empty()) return fail("The manifest lists no configurations");

    auto doc = std::make_unique<PartDocument>();
    doc->name = m.value("name", "Imported Part");
    doc->properties = readProperties(m.value("properties", json::object()));
    if (m.contains("material")) {
        std::string matName = m["material"].value("name", "");
        double density = m["material"].value("density", 0.0);
        if (auto lib = findMaterial(matName); lib && density <= 0) doc->material = *lib;
        else doc->material = {matName, density > 0 ? density : 7850};
    }
    doc->properties.emplace("SourceFile", m.value("source", ""));

    doc->configurations.clear();
    std::vector<std::string> bodies;
    std::string base = dirOf(manifestPath);
    for (auto& c : configs) {
        std::string step = c.value("step", "");
        if (step.empty()) return fail("Configuration " + c.value("name", "?") + " has no STEP file");
        if (!isAbsolute(step)) step = base + step;
        std::string err;
        Feature* f = doc->importSTEP(step, &err);
        if (!f) return fail(err);
        std::string body = "Body-" + c.value("name", std::to_string(bodies.size() + 1));
        doc->renameFeature(f->name, body);
        bodies.push_back(body);

        Configuration cfg;
        cfg.name = c.value("name", body);
        cfg.description = c.value("description", "");
        cfg.bomPartNumber = c.value("bomPartNumber", "");
        cfg.properties = readProperties(c.value("properties", json::object()));
        doc->configurations.push_back(cfg);
    }
    // Each configuration shows only its own body.
    for (size_t i = 0; i < doc->configurations.size(); ++i)
        for (size_t k = 0; k < bodies.size(); ++k)
            if (k != i) doc->configurations[i].suppressedFeatures.insert(bodies[k]);
    std::string active = m.value("activeConfiguration", "");
    doc->activeConfiguration = doc->findConfiguration(active) ? active : doc->configurations.front().name;
    return doc;
}

}  // namespace stawn
