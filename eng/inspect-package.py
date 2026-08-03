#!/usr/bin/env python3

import argparse
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate Requisite package contents.")
    parser.add_argument("nupkg", type=Path)
    parser.add_argument("snupkg", type=Path)
    parser.add_argument("--version")
    return parser.parse_args()


def inspect_binary_package(path: Path, expected_version: str | None) -> None:
    required = {
        "LICENSE-APACHE",
        "LICENSE-MIT",
        "README.md",
        "analyzers/dotnet/cs/Requisite.Analyzers.dll",
        "lib/net8.0/Requisite.dll",
        "lib/net8.0/Requisite.xml",
        "lib/net10.0/Requisite.dll",
        "lib/net10.0/Requisite.xml",
    }

    with ZipFile(path) as archive:
        names = set(archive.namelist())
        missing = required - names
        if missing:
            raise SystemExit(f"{path}: missing entries: {sorted(missing)}")

        root = ET.fromstring(archive.read("Requisite.nuspec"))
        namespace = {"n": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"}
        metadata = root.find("n:metadata", namespace)
        if metadata is None:
            raise SystemExit(f"{path}: nuspec metadata is missing")

        version = metadata.findtext("n:version", namespaces=namespace)
        if expected_version is not None and version != expected_version:
            raise SystemExit(
                f"{path}: expected version {expected_version}, found {version}"
            )

        groups = metadata.findall("n:dependencies/n:group", namespace)
        frameworks = {group.attrib.get("targetFramework") for group in groups}
        if frameworks != {"net8.0", "net10.0"}:
            raise SystemExit(f"{path}: unexpected dependency groups: {frameworks}")
        if any(len(group) != 0 for group in groups):
            raise SystemExit(f"{path}: runtime dependency groups must be empty")


def inspect_symbol_package(path: Path) -> None:
    required = {
        "lib/net8.0/Requisite.pdb",
        "lib/net10.0/Requisite.pdb",
    }
    with ZipFile(path) as archive:
        missing = required - set(archive.namelist())
        if missing:
            raise SystemExit(f"{path}: missing entries: {sorted(missing)}")


def main() -> None:
    args = parse_args()
    inspect_binary_package(args.nupkg, args.version)
    inspect_symbol_package(args.snupkg)
    print(f"Validated {args.nupkg} and {args.snupkg}.")


if __name__ == "__main__":
    main()
