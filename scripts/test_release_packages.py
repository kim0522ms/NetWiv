import hashlib
import importlib.util
import contextlib
import io
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from pathlib import Path

spec = importlib.util.spec_from_file_location('release_packages', Path(__file__).with_name('release-packages.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleasePackagesTests(unittest.TestCase):
    def test_tag_versions(self):
        for tag in ['v0.1.0', 'v0.1.0-beta.1', 'v1.2.3-rc.2']:
            self.assertEqual(tag[1:], release.version_from_tag(tag))
        for tag in ['main', '0.1.0', 'v01.2.3', 'v1.2', 'v1.2.3-',
                    'v1.2.3-beta..1', 'v1.2.3-01', 'v1.2.3+metadata', 'v1.2.3\n']:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.version_from_tag(tag)

    def fixture(self, root, *, missing=None, version='0.1.0-beta.1', corrupt=False):
        feed = root / 'feed'
        feed.mkdir()
        for name in ['LICENSE-APACHE', 'NOTICE', 'THIRD-PARTY-NOTICES.md']:
            (root / name).write_text(name)
        (root / 'docs').mkdir()
        (root / 'docs/release-packages.md').write_text('Local feed instructions')
        (root / 'native').mkdir()
        (root / 'native/dependency-lock.json').write_text('{}')
        (root / 'compatibility').mkdir()
        (root / 'compatibility/upstream-lock.json').write_text('{}')
        for project in [*release.BASE_PACKAGES, 'NetWiv.Plugin.Example']:
            (root / 'src' / project).mkdir(parents=True)
            if missing == project:
                continue
            with zipfile.ZipFile(feed / f'{project}.0.1.0-beta.1.nupkg', 'w') as package:
                package.writestr(f'{project}.nuspec', f'<package><metadata><id>{project}</id><version>{version}</version></metadata></package>')
                for notice in ['NOTICE', 'LICENSE-APACHE']:
                    if missing != notice:
                        package.writestr(notice, notice)
                if project == 'NetWiv.Platform.Desktop':
                    for native in release.NATIVE_FILES:
                        source = root / 'artifacts/native' / native
                        source.parent.mkdir(parents=True, exist_ok=True)
                        source.write_bytes(b'native fixture')
                        if missing != native:
                            package.writestr(f'runtimes/{native.parent}/native/{native.name}', b'corrupt' if corrupt else source.read_bytes())
        return feed

    def test_bundle_contents_and_checksums(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            feed = self.fixture(root)
            self.assertEqual({'win-x64', 'linux-x64', 'osx-arm64'}, {native.parent.as_posix() for native in release.NATIVE_FILES})
            self.assertFalse((root / 'artifacts/native/osx-x64').exists())
            output = root / 'output'
            release.bundle(root, feed, output, '0.1.0-beta.1', 'a' * 40)
            archive = output / 'NetWiv-0.1.0-beta.1-packages.zip'
            with zipfile.ZipFile(archive) as package:
                names = package.namelist()
                self.assertEqual(8, len([name for name in names if name.endswith('.nupkg')]))
                self.assertIn('packages/NetWiv.Blazor.0.1.0-beta.1.nupkg', names)
                self.assertIn('README.md', names)
                self.assertIn('LICENSE-APACHE', names)
                self.assertIn('native/dependency-lock.json', names)
                self.assertIn('a' * 40, package.read('SOURCE.txt').decode())
                for line in package.read('SHA256SUMS').decode().splitlines():
                    digest, name = line.split('  ')
                    self.assertEqual(digest, hashlib.sha256(package.read(name)).hexdigest())
            self.assertIn(hashlib.sha256(archive.read_bytes()).hexdigest(), (output / 'SHA256SUMS').read_text())
            with self.assertRaises(FileExistsError):
                release.bundle(root, feed, output, '0.1.0-beta.1', 'a' * 40)

    def test_rejects_retired_macos_x64_payload(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            feed = self.fixture(root)
            with zipfile.ZipFile(feed / 'NetWiv.Platform.Desktop.0.1.0-beta.1.nupkg', 'a') as package:
                package.writestr('runtimes/osx-x64/native/libnetwiv_native.dylib', b'stale native fixture')
            with self.assertRaisesRegex(ValueError, 'unexpected native assets'):
                release.bundle(root, feed, root / 'output', '0.1.0-beta.1', 'a' * 40)
            self.assertFalse((root / 'output').exists())

    def test_rejects_incomplete_or_wrong_packages(self):
        cases = [dict(missing='NetWiv.Hosting'), dict(missing='NetWiv.Blazor'), dict(missing='NOTICE'),
                 dict(missing='LICENSE-APACHE'), dict(version='0.1.0-dev'),
                 dict(corrupt=True)] + [dict(missing=native) for native in release.NATIVE_FILES]
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                feed = self.fixture(root, **case)
                with self.assertRaises(ValueError):
                    release.bundle(root, feed, root / 'output', '0.1.0-beta.1', 'a' * 40)
                self.assertFalse((root / 'output').exists())

    def test_rejects_bad_revision_and_nuspec(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            feed = self.fixture(root)
            with self.assertRaisesRegex(ValueError, 'commit SHA'):
                release.bundle(root, feed, root / 'output', '0.1.0-beta.1', 'main')
            package = feed / 'NetWiv.Core.0.1.0-beta.1.nupkg'
            with zipfile.ZipFile(package, 'w') as archive:
                archive.writestr('NOTICE', 'notice')
            with self.assertRaisesRegex(ValueError, 'one nuspec'):
                release.bundle(root, feed, root / 'output', '0.1.0-beta.1', 'a' * 40)

    def test_cli_version_and_bundle(self):
        with patch('sys.argv', ['release-packages.py', 'version', 'v1.2.3']), contextlib.redirect_stdout(io.StringIO()) as stdout:
            release.main()
            self.assertEqual('1.2.3\n', stdout.getvalue())
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            feed = self.fixture(root)
            (root / 'scripts').mkdir()
            with patch.object(release, '__file__', str(root / 'scripts/release-packages.py')), patch('sys.argv', [
                    'release-packages.py', 'bundle', '--feed', str(feed), '--output', str(root / 'output'),
                    '--version', '0.1.0-beta.1', '--revision', 'a' * 40]):
                release.main()
            self.assertTrue((root / 'output/NetWiv-0.1.0-beta.1-packages.zip').exists())


if __name__ == '__main__':
    unittest.main()
