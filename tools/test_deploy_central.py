"""Exercise the deployment transaction locally; no SSH, Docker or production data."""
import os
import shutil
import tempfile
import unittest
from pathlib import Path

from deploy_central import _install


class Fixture:
    names = ('wwwroot/index.html', 'ClassNex.Central.dll', 'web.config', 'wwwroot/control.css')
    previous = {'wwwroot/index.html', 'ClassNex.Central.dll'}

    def __init__(self, root):
        self.root = Path(root)
        self.restarts = 0
        self.fault = None
        for name in self.names:
            for prefix in ('app', 'stage/new', 'stage/previous', 'stage/restore'):
                (self.root / prefix / name).parent.mkdir(parents=True, exist_ok=True)
            (self.root / 'stage/new' / name).write_text('new:' + name)
            if name in self.previous:
                for prefix in ('app', 'stage/previous'):
                    (self.root / prefix / name).write_text('old:' + name)
        (self.root / 'app/data').mkdir()
        (self.root / 'app/data/sentinel').write_text('untouched')

    def path(self, value):
        if not value.startswith(('/stage/', '/app/')) or value.startswith('/app/data/') or '..' in value.split('/'):
            raise AssertionError('Out of transaction: ' + value)
        return self.root / value[1:]

    def posix_rename(self, source, target):
        if self.fault == (source, target, 'before'):
            raise OSError('injected rename failure')
        os.replace(self.path(source), self.path(target))
        if self.fault == (source, target, 'after'):
            raise OSError('injected rename failure')

    def lstat(self, name):
        return self.path(name).lstat()

    def run(self, command):
        if command == 'docker restart central':
            self.restarts += 1
            return
        action, flag, source, dest = command.split()
        if (action, flag) != ('cp', '-p'):
            raise AssertionError(command)
        shutil.copy2(self.path(source), self.path(dest))

    def install(self, check):
        _install(self, self.run, self.names, self.previous, '/app', '/stage', 'central', check)


class RollbackTests(unittest.TestCase):
    def fixture(self):
        temp = tempfile.TemporaryDirectory(prefix='classnex-rollback-')
        self.addCleanup(temp.cleanup)
        return Fixture(temp.name)

    def assert_restored(self, f):
        for name in f.names:
            if name in f.previous:
                self.assertEqual((f.root / 'app' / name).read_text(), 'old:' + name)
                self.assertEqual((f.root / 'stage/previous' / name).read_text(), 'old:' + name)
            else:
                self.assertFalse((f.root / 'app' / name).exists())
                self.assertEqual((f.root / 'stage/new' / name).read_text(), 'new:' + name)
        self.assertEqual((f.root / 'app/data/sentinel').read_text(), 'untouched')

    @staticmethod
    def unhealthy():
        raise RuntimeError('injected health failure')

    def test_failed_health_check_restores_exact_previous_payload(self):
        f = self.fixture()
        with self.assertRaisesRegex(RuntimeError, 'injected health failure'):
            f.install(self.unhealthy)
        self.assert_restored(f)
        self.assertEqual(f.restarts, 2)

    def test_operator_interrupt_rolls_back_without_swallowing_interrupt(self):
        f = self.fixture()
        def interrupt():
            raise KeyboardInterrupt
        with self.assertRaises(KeyboardInterrupt):
            f.install(interrupt)
        self.assert_restored(f)

    def test_partial_promotion_before_or_after_acknowledgement(self):
        for point in ('before', 'after'):
            with self.subTest(point=point):
                f = self.fixture()
                f.fault = ('/stage/new/wwwroot/control.css', '/app/wwwroot/control.css', point)
                with self.assertRaisesRegex(OSError, 'injected rename failure'):
                    f.install(lambda: self.fail('partially installed files must not be started'))
                self.assert_restored(f)
                self.assertEqual(f.restarts, 1)

    def test_rollback_error_keeps_restoring_other_files_without_restart(self):
        f = self.fixture()
        f.fault = ('/app/wwwroot/control.css', '/stage/new/wwwroot/control.css', 'before')
        with self.assertRaisesRegex(RuntimeError, 'Rollback incomplete for wwwroot/control.css'):
            f.install(self.unhealthy)
        self.assertEqual((f.root / 'app/ClassNex.Central.dll').read_text(), 'old:ClassNex.Central.dll')
        self.assertFalse((f.root / 'app/web.config').exists())
        self.assertEqual(f.restarts, 1)


if __name__ == '__main__':
    unittest.main()
