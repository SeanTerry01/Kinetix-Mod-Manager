"""Mirror MinecraftLauncher's classpath algorithm and diff it against the one the real
Minecraft launcher built, which is recorded verbatim in launcher_log.txt.

This checks the parts that are easy to get quietly wrong - rule evaluation, ordering, and
turning a maven coordinate into a path - against ground truth rather than against my
expectations of it.
"""

import json
import os
import re

MC = os.path.join(os.environ["APPDATA"], ".minecraft")
ROAMING = os.environ["APPDATA"].rsplit("\\", 1)[0] if False else os.path.dirname(MC)
VERSION_ID = "fabric-loader-0.19.5-26.2"
OS_NAME = "windows"
OS_ARCH = "x86_64"


def read_version(version_id):
    path = os.path.join(MC, "versions", version_id, version_id + ".json")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def arch_matches(wanted, actual):
    return wanted == actual or (wanted == "x86" and actual == "x86_64")


def rules_allow(rules):
    if not rules:
        return True
    allowed = False
    for rule in rules:
        if "features" in rule:
            continue
        matches = True
        os_spec = rule.get("os")
        if os_spec:
            if os_spec.get("name") and os_spec["name"] != OS_NAME:
                matches = False
            if os_spec.get("arch") and not arch_matches(os_spec["arch"], OS_ARCH):
                matches = False
        if matches:
            allowed = rule.get("action") == "allow"
    return allowed


def maven_to_path(coordinate):
    parts = coordinate.split(":")
    if len(parts) < 3:
        return ""
    group, artifact, version = parts[0], parts[1], parts[2]
    classifier = "-" + parts[3] if len(parts) > 3 else ""
    return os.path.join(group.replace(".", os.sep), artifact, version,
                        f"{artifact}-{version}{classifier}.jar")


def resolve(version_id, depth=0):
    """Merge a version onto what it inherits from - child libraries FIRST."""
    version = read_version(version_id)
    parent_id = version.get("inheritsFrom")
    if not parent_id or depth > 8:
        return version, None

    parent, _ = resolve(parent_id, depth + 1)
    merged = dict(parent)
    for k, v in version.items():
        if k in ("libraries", "arguments", "inheritsFrom"):
            continue
        merged[k] = v
    merged["libraries"] = version.get("libraries", []) + parent.get("libraries", [])
    return merged, parent_id


def library_paths(resolved):
    paths, seen = [], set()
    for library in resolved.get("libraries", []):
        if not rules_allow(library.get("rules")):
            continue
        explicit = library.get("downloads", {}).get("artifact", {}).get("path")
        relative = explicit.replace("/", os.sep) if explicit else maven_to_path(library.get("name", ""))
        if relative and relative.lower() not in seen:
            seen.add(relative.lower())
            paths.append(relative)
    return paths


# ---------------------------------------------------------------- ours

resolved, inherits_from = resolve(VERSION_ID)
libraries_root = os.path.join(MC, "libraries")
ours = [os.path.join(libraries_root, p) for p in library_paths(resolved)]

own_jar = os.path.join(MC, "versions", VERSION_ID, VERSION_ID + ".jar")
if os.path.isfile(own_jar):
    ours.append(own_jar)
elif inherits_from:
    ours.append(os.path.join(MC, "versions", inherits_from, inherits_from + ".jar"))

# ---------------------------------------------------------------- theirs

lines = open(os.path.join(MC, "launcher_log.txt"), encoding="utf-8", errors="replace").read().splitlines()
last = max(i for i, l in enumerate(lines) if "Creating process" in l)
args = [re.search(r"Java argument:(.*)$", l).group(1)
        for l in lines[:last] if "JavaLaunchConfiguration.cpp(281)] Java argument:" in l]
args = args[max(i for i, a in enumerate(args) if "HeapDumpPath" in a):]
cp = next(a for a in args if a.count(";") > 10)
theirs = [p.replace("<WORKDIR>", ROAMING) for p in cp.split(";")]

# ---------------------------------------------------------------- diff

print(f"ours:   {len(ours)} entries")
print(f"theirs: {len(theirs)} entries")
print()

if ours == theirs:
    print("IDENTICAL - same entries, same order.")
else:
    only_ours = [p for p in ours if p not in theirs]
    only_theirs = [p for p in theirs if p not in ours]
    print(f"only in ours   ({len(only_ours)}):")
    for p in only_ours:
        print("   +", p)
    print(f"only in theirs ({len(only_theirs)}):")
    for p in only_theirs:
        print("   -", p)

    if not only_ours and not only_theirs:
        print("Same set, DIFFERENT ORDER. First divergence:")
        for i, (a, b) in enumerate(zip(ours, theirs)):
            if a != b:
                print(f"   index {i}\n     ours:   {a}\n     theirs: {b}")
                break

missing = [p for p in ours if not os.path.exists(p)]
print()
print(f"entries of ours that do not exist on disk: {len(missing)}")
for p in missing:
    print("   MISSING", p)
