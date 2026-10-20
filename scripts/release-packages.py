"""Validate and bundle a local package feed. Never publish to a package registry."""
import argparse
import hashlib
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

BASE_PACKAGES = ['NetWiv.Core', 'NetWiv.Hosting', 'NetWiv.BlazorHybrid', 'NetWiv.Generators',
                 'NetWiv.Build', 'NetWiv.Platform.Desktop', 'NetWiv.Cli']
NATIVE_FILES = [Path(name) for name in [
    'win-x64/netwiv_native.dll', 'win-x64/WebView2Loader.dll',
    'linux-x64/libnetwiv_native.so', 'osx-arm64/libnetwiv_native.dylib']]


def version_from_tag(tag):
    number = r'(?:0|[1-9][0-9]*)'
    if not re.fullmatch(rf'v{number}\.{number}\.{number}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?', tag):
        raise ValueError('Use a version tag such as v0.1.0 or v0.1.0-beta.1 (no build metadata).')
    version = tag[1:]
    if '-' in version:
        for part in version.split('-', 1)[1].split('.'):
            if part.isdigit() and len(part) > 1 and part.startswith('0'):
                raise ValueError('Numeric prerelease identifiers must not have leading zeroes.')
    return version


def bundle(repository, feed, destination, version, revision):
    version_from_tag('v' + version)
    if not re.fullmatch('[0-9a-f]{40}', revision):
        raise ValueError('A full source commit SHA is required.')
    projects = BASE_PACKAGES + sorted(p.name for p in (repository / 'src').glob('NetWiv.Plugin.*') if p.is_dir())
    expected = {f'{project}.{version}.nupkg' for project in projects}
    packages = sorted(feed.glob('*.nupkg'))
    if {package.name for package in packages} != expected:
        raise ValueError('Package set does not match the SDK, CLI, Hosting and plugin projects.')
    for file in packages:
        with zipfile.ZipFile(file) as package:
            # NuGet's namespace differs between nuspec schema versions.
            nuspecs = [name for name in package.namelist() if name.endswith('.nuspec')]
            if len(nuspecs) != 1:
                raise ValueError(f'{file.name}: expected one nuspec.')
            metadata = ET.fromstring(package.read(nuspecs[0]))
            for element in metadata.iter():
                element.tag = element.tag.split('}')[-1]
            identity = metadata.findtext('metadata/id')
            if metadata.findtext('metadata/version') != version or file.name != f'{identity}.{version}.nupkg':
                raise ValueError(f'{file.name}: package identity/version mismatch.')
            for notice in ['NOTICE', 'LICENSE-APACHE']:
                if notice not in package.namelist() or not package.read(notice):
                    raise ValueError(f'{file.name}: missing {notice}.')
            if identity == 'NetWiv.Platform.Desktop':
                expected_native = {f'runtimes/{native.parent.as_posix()}/native/{native.name}' for native in NATIVE_FILES}
                actual_native = {name for name in package.namelist() if name.startswith('runtimes/') and '/native/' in name and not name.endswith('/')}
                if actual_native - expected_native:
                    raise ValueError(f'{file.name}: unexpected native assets outside the supported package set.')
                for native in NATIVE_FILES:
                    name = f'runtimes/{native.parent.as_posix()}/native/{native.name}'
                    source = repository / 'artifacts/native' / native
                    if name not in package.namelist() or not source.is_file() or not source.stat().st_size or package.read(name) != source.read_bytes():
                        raise ValueError(f'{file.name}: missing or mismatched native library {name}.')
    files = {f'packages/{package.name}': package.read_bytes() for package in packages}
    for name in ['LICENSE-APACHE', 'NOTICE', 'THIRD-PARTY-NOTICES.md',
                 'native/dependency-lock.json', 'compatibility/upstream-lock.json']:
        files[name] = (repository / name).read_bytes()
    files['README.md'] = (repository / 'docs/release-packages.md').read_bytes()
    files['SOURCE.txt'] = f'NetWiv {version}\nSource commit: {revision}\n'.encode()
    files['SHA256SUMS'] = ''.join(f'{hashlib.sha256(data).hexdigest()}  {name}\n' for name, data in sorted(files.items())).encode()
    destination.mkdir(parents=True, exist_ok=False)
    archive = destination / f'NetWiv-{version}-packages.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        for name, data in sorted(files.items()):
            output.writestr(name, data)
    (destination / 'SHA256SUMS').write_text(f'{hashlib.sha256(archive.read_bytes()).hexdigest()}  {archive.name}\n', encoding='utf-8')
    (destination / 'release-notes.md').write_text(
        f'NetWiv {version} — experimental package bundle. Start with a toy project.\n\n'
        f'Source commit: `{revision}`\n\n'
        'Download and extract the ZIP, then follow its README to use `packages/` as a local NuGet feed. '
        'Contains the SDK, Hosting, CLI, managed plugins and native libraries for Windows x64, Linux x64 and macOS arm64. '
        'The optional Blazor Hybrid package defaults to managed desktop execution on these targets; NativeAOT hosting is experimental and requires explicit opt-in.\n\n'
        'These packages are not published to NuGet.org. CI validates source builds on all three supported platforms '
        'and runs packaged managed GUI/IPC and NativeAOT startup checks on all three desktop RIDs '
        '(.NET 8 everywhere, plus .NET 9/10 on Linux); this is not complete release acceptance, '
        'installer lifecycle validation, Developer ID signing or notarization.\n\n'
        'NetWiv is an independent project. See NOTICE and THIRD-PARTY-NOTICES.md in the bundle for upstream attribution.\n',
        encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    tag = commands.add_parser('version')
    tag.add_argument('tag')
    pack = commands.add_parser('bundle')
    pack.add_argument('--feed', type=Path, required=True)
    pack.add_argument('--output', type=Path, required=True)
    pack.add_argument('--version', required=True)
    pack.add_argument('--revision', required=True)
    args = parser.parse_args()
    if args.command == 'version':
        print(version_from_tag(args.tag))
    else:
        bundle(Path(__file__).resolve().parent.parent, args.feed, args.output, args.version, args.revision)


if __name__ == '__main__':
    main()
