import fcntl
import os
import pty
from pathlib import Path
import select
import signal
import struct
import subprocess
import termios
import time


TEST_DIRECTORY = Path(__file__).resolve().parent
PROJECT = TEST_DIRECTORY / "Dvtui.TerminalTests" / "Dvtui.TerminalTests.csproj"
ASSEMBLY = PROJECT.parent / "bin" / "Debug" / "net10.0" / "Dvtui.TerminalTests.dll"
subprocess.run(["dotnet", "build", str(PROJECT)], check=True)


class Browser:
    def __init__(self, mode="normal"):
        self.fd, slave = pty.openpty()
        self.resize(30, 100)
        env = dict(os.environ, TERM="xterm-256color", NO_COLOR="1")
        self.process = subprocess.Popen(
            [
                "dotnet",
                str(ASSEMBLY),
                mode,
            ],
            stdin=slave,
            stdout=slave,
            stderr=slave,
            env=env,
        )
        os.close(slave)

    def resize(self, rows, columns):
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, columns, 0, 0))
        if hasattr(self, "process"):
            os.kill(self.process.pid, signal.SIGWINCH)

    def read(self, duration=0.25):
        result = b""
        end = time.monotonic() + duration
        while time.monotonic() < end:
            if select.select([self.fd], [], [], 0.03)[0]:
                try:
                    result += os.read(self.fd, 65536)
                except OSError:
                    break
        return result.decode(errors="replace")

    def key(self, text):
        os.write(self.fd, text.encode())
        return self.read()

    def close(self):
        if self.process.poll() is None:
            self.process.kill()
        self.process.wait(timeout=2)
        os.close(self.fd)


browser = Browser()
try:
    initial = browser.read(0.9)
    assert "Loading tables..." in initial
    assert "75 tables | Customizable only" in initial
    assert "excluded_false" not in initial
    assert "excluded_unknown" not in initial
    assert "Display [name] 000" in initial
    assert "\x1b[?1049h" in initial
    assert not browser.read(), "Idle screen keeps repainting"
    for line in initial.splitlines():
        if line.startswith("╭─Tables"):
            assert line.index("╭─Table details") == 25
    down = browser.key("\x1b[B")
    assert "> table_001" in down
    assert "Display [name] 001" in down
    assert "Custom table     No" in down
    end = browser.key("\x1b[F")
    assert "> table_074" in end
    assert "Display [name] 074" in end
    page_up = browser.key("\x1b[5~")
    assert "> table_048" in page_up
    home = browser.key("\x1b[H")
    assert "> table_000" in home
    detail_end = browser.key("\t\x1b[F")
    assert "DESCRIPTION_END" in detail_end
    assert "> table_000" in detail_end
    detail_home = browser.key("\x1b[H")
    assert "Display [name] 000" in detail_home
    browser.resize(18, 72)
    resized = browser.read(0.4)
    assert "Table details" in resized, repr(resized)
    assert "table_000" in resized
    browser.resize(8, 40)
    small = browser.read(0.4)
    assert "Enlarge the terminal" in small
    browser.resize(30, 100)
    restored = browser.read(0.4)
    assert "Display [name] 000" in restored
    exited = browser.key("\x1b")
    assert "Browser exited; attempts: 1" in exited
    assert "\x1b[?1049l" in exited
    assert browser.process.wait(timeout=2) == 0
    print("PASS: 25/75 layout, filter, selection, scrolling, resize, exit")
finally:
    browser.close()

browser = Browser("retry")
try:
    error = browser.read(0.9)
    assert "Could not load tables" in error
    assert "Simulated [metadata] error" in error
    browser.key("r")
    recovered = browser.read(0.6)
    assert "75 tables | Customizable only" in recovered
    exited = browser.key("\x1b")
    assert "Browser exited; attempts: 2" in exited
    print("PASS: metadata failure and reload")
finally:
    browser.close()

browser = Browser("empty")
try:
    empty = browser.read(0.9)
    assert "No customizable tables found" in empty
    browser.key("\x1b[B\x1b[F\t\x1b[B")
    exited = browser.key("\x1b")
    assert "Browser exited; attempts: 1" in exited
    print("PASS: empty metadata list and navigation")
finally:
    browser.close()

browser = Browser("cancel")
try:
    loading = browser.read(0.3)
    assert "Loading tables..." in loading
    exited = browser.key("\x03")
    assert "Browser exited; attempts: 1" in exited
    assert "\x1b[?1049l" in exited
    assert browser.process.wait(timeout=2) == 0
    print("PASS: cancellation during metadata load")
finally:
    browser.close()

browser = Browser("long")
try:
    browser.resize(24, 80)
    initial = browser.read(0.9)
    assert "> table_000_wit…" in initial
    for index in range(1, 75):
        os.write(browser.fd, b"\x1b[B")
        screen = browser.read(0.15)
        left = [line.split("│")[1] for line in screen.splitlines() if line.startswith("│")]
        assert len(left) == 20, (index, left)
        assert any(f"> table_{index:03}_wit…" in row for row in left)
    for index in range(73, -1, -1):
        os.write(browser.fd, b"\x1b[A")
        screen = browser.read(0.15)
        left = [line.split("│")[1] for line in screen.splitlines() if line.startswith("│")]
        assert any(f"> table_{index:03}_wit…" in row for row in left)
    browser.resize(30, 100)
    wider = browser.read(0.4)
    assert "> table_000_with_a_v…" in wider
    browser.resize(18, 72)
    narrower = browser.read(0.4)
    assert "> table_000_w…" in narrower
    browser.key("\x1b")
    print("PASS: long names, every arrow step stays visible, dynamic clipping")
finally:
    browser.close()
