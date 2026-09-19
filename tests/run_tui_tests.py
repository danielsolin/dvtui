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
BINARY = (
    ROOT
    / "tests"
    / "dvtui.TerminalTests"
    / "bin"
    / "Debug"
    / "net10.0"
    / "dvtui.TerminalTests.dll"
)
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


def read_bounded(fd, max_iter=60, seconds=0.1):
    """Read with a hard iteration cap. Safe for Live-loop screens that
    repaint continuously, where read_available could loop forever."""
    data = b""
    for _ in range(max_iter):
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
    return data.decode(errors="ignore")


def drain_until_exit(fd, process, timeout=5):
    """Read until the process exits, returning the final output."""
    data = b""
    deadline = time.time() + timeout
    while time.time() < deadline:
        if process.poll() is not None:
            while True:
                ready, _, _ = select.select([fd], [], [], 0.3)
                if not ready:
                    break
                try:
                    chunk = os.read(fd, 4096)
                except OSError:
                    break
                if not chunk:
                    break
                data += chunk
            break
        ready, _, _ = select.select([fd], [], [], 0.3)
        if ready:
            try:
                chunk = os.read(fd, 4096)
            except OSError:
                break
            if not chunk:
                break
            data += chunk
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


def test_table_columns_navigates_and_deletes():
    master_fd, process = start_process("table-columns")
    try:
        data = read_until(master_fd, "new_custom")
        data += read_until(master_fd, "new_standard")
        assert "2 columns" in data
        # Select the deletable column and delete it.
        press_key(master_fd, "D")
        final = drain_until_exit(master_fd, process)
        assert "deletecolumn:new_custom" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns navigation passed")


def test_create_table_submits():
    master_fd, process = start_process("create-table")
    try:
        data = read_until(master_fd, "Create table")
        data += read_until(master_fd, "Publisher prefix")
        for char in b"Test Table":
            os.write(master_fd, bytes([char]))
            time.sleep(0.05)
        read_bounded(master_fd)
        press_key(master_fd, "\r")
        final = drain_until_exit(master_fd, process)
        assert "Table created" in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("create-table submit passed")


def test_column_editor_creates():
    master_fd, process = start_process("column-editor")
    try:
        data = read_until(master_fd, "Create column")
        data += read_until(master_fd, "Display name")
        for char in b"New Field":
            os.write(master_fd, bytes([char]))
            time.sleep(0.05)
        read_bounded(master_fd)
        press_key(master_fd, "\r")
        final = drain_until_exit(master_fd, process)
        assert "Column created" in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("column-editor create passed")


def test_column_editor_edits():
    master_fd, process = start_process("column-editor-edit")
    try:
        data = read_until(master_fd, "Edit column")
        data += read_until(master_fd, "new_custom")
        # Clear the display name and type a new one.
        for _ in range(20):
            os.write(master_fd, b"\x7f")
            time.sleep(0.03)
        read_bounded(master_fd)
        for char in b"Custom Renamed":
            os.write(master_fd, bytes([char]))
            time.sleep(0.05)
        read_bounded(master_fd)
        press_key(master_fd, "\r")
        final = drain_until_exit(master_fd, process)
        assert "Column saved" in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("column-editor edit passed")


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
        test_table_columns_navigates_and_deletes,
        test_create_table_submits,
        test_column_editor_creates,
        test_column_editor_edits,
    ]
    for test in tests:
        test()
    print(f"All {len(tests)} terminal integration tests passed.")


if __name__ == "__main__":
    main()
