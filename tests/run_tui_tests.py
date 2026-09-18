#!/usr/bin/env python3
"""PTY integration tests for the dvtui terminal screens."""

import fcntl
import os
import pty
import select
import shutil
import signal
import struct
import subprocess
import sys
import tempfile
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BINARY = ROOT / "tests" / "dvtui.TerminalTests" / "bin" / "Debug" / "net10.0" / "dvtui.TerminalTests.dll"
TEST_SIZE = (80, 24)
SMALL_SIZE = (58, 12)
PROMPT = "DVTUI | 3 entities | Esc: back | Q: quit"
PROMPT_SMALL = "DVTUI | 3 entities | Esc: back | Q: quit"
SOLUTION_PROMPT = "DVTUI | 2 visible solutions | Enter: open"
SOLUTION_BROWSER_PROMPT = "DVTUI | 3 component rows"
SOLUTION_BROWSER_PROMPT_SMALL = "DVTUI | 3 component rows"
SOLUTION_BROWSER_PROMPT_TALL = "DVTUI | 3 component rows"
DETAILS_PROMPT = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_SMALL = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_TALL = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_WIDE = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_WIDE_SMALL = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_WIDE_TALL = "DVTUI | 3 entities | Esc: back | Q: quit"
DETAILS_PROMPT_WIDE_TALL_SMALL = "DVTUI | 3 entities | Esc: back | Q: quit"
RESULTS = []


def wait_for_size(fd, size):
    deadline = time.time() + 10
    while time.time() < deadline:
        ready, _, _ = select.select([fd], [], [], 0.2)
        if ready:
            data = os.read(fd, 4096)
            if not data:
                break
            if b"Enlarge the terminal" in data:
                continue
            try:
                columns, rows = data.decode(errors="ignore").count("\x1b[8;") * 2, 0
            except Exception:
                pass
        time.sleep(0.05)


def start_process(mode, size=TEST_SIZE):
    columns, rows = size
    env = os.environ.copy()
    env["TERM"] = "xterm-256color"
    with tempfile.TemporaryDirectory(prefix="dvtui-pty-") as directory:
        master_fd, slave_fd = pty.openpty()
        process = subprocess.Popen(
            ["dotnet", "exec", str(BINARY), mode],
            stdin=slave_fd,
            stdout=slave_fd,
            stderr=subprocess.PIPE,
            cwd=ROOT,
            env=env,
            start_new_session=True,
        )
        os.close(slave_fd)
        set_size(master_fd, columns, rows)
        return master_fd, process


def set_size(fd, columns, rows):
    # TIOCSWINSZ ioctl
    fcntl.ioctl(
        fd,
        0x5413,
        struct.pack("HHHH", rows, columns, 0, 0),
        True,
    )


def read_available(fd, seconds=0.5):
    data = b""
    while True:
        ready, _, _ = select.select([fd], [], [], seconds)
        if not ready:
            break
        try:
            chunk = os.read(fd, 4096)
        except OSError:
            break
        if not chunk:
            break
        data += chunk
        seconds = 0.1
    return data.decode(errors="ignore")


def read_until(fd, expected, timeout=10):
    data = ""
    deadline = time.time() + timeout
    while time.time() < deadline:
        ready, _, _ = select.select([fd], [], [], 0.2)
        if ready:
            chunk = os.read(fd, 4096)
            if not chunk:
                break
            data += chunk.decode(errors="ignore")
            if expected in data:
                return data
        else:
            data += read_available(fd, 0)
    raise AssertionError(f"Timed out waiting for {expected!r}")


def press_key(fd, key):
    os.write(fd, key.encode())
    time.sleep(0.3)


def wait_idle(fd, seconds=0.6):
    time.sleep(seconds)
    read_available(fd, seconds)


def stop_process(process):
    if process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=5)


def test_solution_selection_navigates_and_selects():
    master_fd, process = start_process("solution-selection")
    try:
        data = read_until(master_fd, "Solutions")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "Managed")
        press_key(master_fd, "\r")
        wait_idle(master_fd)
        stop_process(process)
        assert "2 visible solutions" in data
        assert "Contoso" in data
        assert "contoso" in data
        assert "Managed" in data
        assert "1.0.0.0" in data
        assert "↑↓: move" in data
        assert "Tab: pane" in data
        assert "R: reload" in data
        assert "Q: quit" in data
    finally:
        os.close(master_fd)
    print("solution-selection navigation passed")


def test_solution_browser_navigates_and_esc():
    master_fd, process = start_process("solution-browser")
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Table: product" in data
        assert "Esc: solutions" in data
        assert "Q: quit" in data
    finally:
        os.close(master_fd)
    print("solution-browser navigation passed")


def test_solution_browser_small_terminal():
    master_fd, process = start_process("solution-browser", SMALL_SIZE)
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser small terminal passed")


def test_solution_browser_tall_terminal():
    master_fd, process = start_process("solution-browser", (80, 40))
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser tall terminal passed")


def test_solution_browser_wide_terminal():
    master_fd, process = start_process("solution-browser", (120, 24))
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser wide terminal passed")


def test_solution_browser_wide_small_terminal():
    master_fd, process = start_process("solution-browser", (100, 12))
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser wide small terminal passed")


def test_solution_browser_wide_tall_terminal():
    master_fd, process = start_process("solution-browser", (120, 40))
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser wide tall terminal passed")


def test_solution_browser_wide_tall_small_terminal():
    master_fd, process = start_process("solution-browser", (100, 12))
    try:
        data = read_until(master_fd, "Components")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Esc: solutions" in data
    finally:
        os.close(master_fd)
    print("solution-browser wide tall small terminal passed")


def main():
    if not BINARY.exists():
        raise SystemExit(f"Test host not found: {BINARY}")
    tests = [
        test_solution_selection_navigates_and_selects,
        test_solution_browser_navigates_and_esc,
        test_solution_browser_small_terminal,
        test_solution_browser_tall_terminal,
        test_solution_browser_wide_terminal,
        test_solution_browser_wide_small_terminal,
        test_solution_browser_wide_tall_terminal,
        test_solution_browser_wide_tall_small_terminal,
    ]
    for test in tests:
        test()
    print(f"All {len(tests)} terminal integration tests passed.")


if __name__ == "__main__":
    main()
