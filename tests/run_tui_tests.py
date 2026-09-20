#!/usr/bin/env python3
"""PTY integration tests for the dvtui terminal screens."""

import atexit
import fcntl
import os
import pty
import select
import signal
import struct
import subprocess
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
TEST_PROJECT = ROOT / "tests" / "dvtui.TerminalTests" / "dvtui.TerminalTests.csproj"
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
DELETE_MUTATION = "Deleting column new_custom"
PUBLISH_MUTATION = "Publishing table account"
ACTIVE_PROCESSES = set()


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
    ACTIVE_PROCESSES.add(process)
    set_size(master_fd, columns, rows)
    return master_fd, process


def build_test_host():
    result = subprocess.run(
        [
            "dotnet",
            "build",
            str(TEST_PROJECT),
            "--disable-build-servers",
            "--nologo",
        ],
        cwd=ROOT,
    )
    if result.returncode != 0:
        raise SystemExit("Could not build the terminal test host.")


def set_size(fd, columns, rows):
    # TIOCSWINSZ ioctl
    fcntl.ioctl(
        fd,
        0x5413,
        struct.pack("HHHH", rows, columns, 0, 0),
        True,
    )


def read_available(fd, seconds=0.5, max_iter=60):
    """Read for a bounded window, even when the application redraws forever."""
    data = b""
    deadline = time.monotonic() + max(0.0, seconds)
    for _ in range(max_iter):
        remaining = deadline - time.monotonic()
        timeout = 0.0 if seconds <= 0 else max(0.0, remaining)
        ready, _, _ = select.select([fd], [], [], timeout)
        if not ready:
            break
        try:
            chunk = os.read(fd, 4096)
        except OSError:
            break
        if not chunk:
            break
        data += chunk
        if seconds <= 0 or time.monotonic() >= deadline:
            break
    return data.decode(errors="ignore")


def read_bounded(fd, max_iter=60, seconds=0.1):
    """Read with a hard iteration cap. Safe for Live-loop screens that
    repaint continuously."""
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
    data = ""
    deadline = time.time() + timeout
    while time.time() < deadline:
        if process.poll() is not None:
            data += read_available(fd, 0.3)
            break
        ready, _, _ = select.select([fd], [], [], 0.3)
        if ready:
            try:
                chunk = os.read(fd, 4096)
            except OSError:
                break
            if not chunk:
                break
            data += chunk.decode(errors="ignore")
    if process.poll() is None:
        try:
            process.wait(timeout=0.5)
        except subprocess.TimeoutExpired:
            pass
    return data


def read_until(fd, expected, timeout=10):
    data = ""
    deadline = time.time() + timeout
    while time.time() < deadline:
        ready, _, _ = select.select([fd], [], [], 0.2)
        if ready:
            try:
                chunk = os.read(fd, 4096)
            except OSError:
                break
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
    ACTIVE_PROCESSES.discard(process)
    if process.poll() is None:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=5)
    finally:
        ACTIVE_PROCESSES.discard(process)


def cleanup_processes():
    for process in list(ACTIVE_PROCESSES):
        stop_process(process)


def handle_signal(signum, _frame):
    cleanup_processes()
    raise SystemExit(128 + signum)


atexit.register(cleanup_processes)
signal.signal(signal.SIGINT, handle_signal)
signal.signal(signal.SIGTERM, handle_signal)


def test_solution_selection_navigates_and_selects():
    master_fd, process = start_process("solution-selection")
    try:
        data = read_until(master_fd, "Solutions")
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "Managed")
        press_key(master_fd, "\t")
        press_key(master_fd, "\x1b")
        assert process.poll() is None
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
        stop_process(process)
        os.close(master_fd)
    print("solution-selection navigation passed")


def test_solution_browser_navigates_and_esc():
    master_fd, process = start_process("solution-browser")
    try:
        data = read_until(master_fd, "Components")
        data += read_until(master_fd, "3 component rows")
        press_key(master_fd, "\t")
        press_key(master_fd, "\x1b[F")
        data += read_until(master_fd, "AccountId (primary key)")
        data += read_available(master_fd, 0.5)
        press_key(master_fd, "\x1b[B")
        data += read_until(master_fd, "contact")
        press_key(master_fd, "\x1b")
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
        stop_process(process)
        assert "3 component rows" in data
        assert "Table: account" in data
        assert "Table: contact" in data
        assert "Table: product" in data
        assert "AccountId (primary key)" in data
        assert "Name (primary name)" in data
        assert "Esc: list/back" in data
        assert "Q: quit" in data
    finally:
        stop_process(process)
        os.close(master_fd)
    print("solution-browser navigation passed")


def test_table_columns_escape_returns_to_list():
    master_fd, process = start_process("table-columns")
    try:
        read_until(master_fd, "2 columns")
        press_key(master_fd, "E")
        read_until(master_fd, "Editing new_custom")
        press_key(master_fd, "\x1b")
        press_key(master_fd, "D")
        final = drain_until_exit(master_fd, process)
        assert "deletecolumn:new_custom" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns escape focus passed")


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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
        os.close(master_fd)
    print("solution-browser small terminal passed")


def test_solution_browser_readonly_disables_writes():
    master_fd, process = start_process("solution-browser-readonly")
    try:
        data = read_until(master_fd, "3 component rows")
        press_key(master_fd, "n")
        data += read_until(master_fd, "Managed solution: read-only")
        assert "Writes disabled" in data
        assert "N: new (disabled)" in data
        press_key(master_fd, "\x1b")
        wait_idle(master_fd)
    finally:
        stop_process(process)
        os.close(master_fd)
    print("solution-browser read-only passed")


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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
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
        assert "Esc: list/back" in data
    finally:
        stop_process(process)
        os.close(master_fd)
    print("solution-browser wide tall small terminal passed")


def test_table_columns_navigates_and_deletes():
    master_fd, process = start_process("table-columns")
    try:
        data = read_until(master_fd, "new_custom")
        data += read_available(master_fd, 0.5)
        assert "2 columns" in data
        assert "new_standard" in data
        assert data.index("new_custom") < data.index("new_standard")
        press_key(master_fd, "R")
        time.sleep(0.3)
        reload_data = read_available(master_fd, 1.0)
        assert "2 columns." in reload_data
        # Select the deletable column and delete it.
        press_key(master_fd, "D")
        final = drain_until_exit(master_fd, process)
        assert "deletecolumn:new_custom" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns navigation passed")


def test_table_columns_edits_in_detail_pane():
    master_fd, process = start_process("table-columns")
    try:
        data = read_until(master_fd, "2 columns")
        data += read_available(master_fd, 0.3)
        assert "new_custom" in data
        press_key(master_fd, "E")
        data += read_until(master_fd, "Editing new_custom")
        data += read_available(master_fd, 0.5)
        assert "Field" in data
        assert "Display name" in data
        assert "Ctrl+S: save" in data
        press_key(master_fd, "r")
        data += read_until(master_fd, "Customr")
        press_key(master_fd, "\x13")
        final = drain_until_exit(master_fd, process)
        assert "savecolumn:new_custom" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns inline edit passed")


def test_table_columns_save_returns_focus_to_list():
    master_fd, process = start_process("table-columns-mutation")
    try:
        data = read_until(master_fd, "2 columns")
        data += read_available(master_fd, 0.5)
        assert "N: New" in data
        press_key(master_fd, "\t")
        frame = read_until(master_fd, "Tab: list", timeout=5)
        frame += read_available(master_fd, 0.5)
        segment = frame[frame.index("Tab: list") - 20:]
        assert "N: New" not in segment
        press_key(master_fd, "\t")
        frame = read_until(master_fd, "Green: editable", timeout=5)
        frame += read_available(master_fd, 0.5)
        assert "N: New" in frame
        press_key(master_fd, "E")
        data = read_until(master_fd, "Ctrl+S: save")
        data += read_available(master_fd, 0.3)
        assert "Editing new_custom" in data
        press_key(master_fd, "x")
        press_key(master_fd, "\x13")
        data = read_until(master_fd, "Green: editable", timeout=10)
        data += read_available(master_fd, 1.0)
        assert "N: New" in data
        assert "2 columns" in data
        press_key(master_fd, "\x1b")
        final = drain_until_exit(master_fd, process)
        assert "close" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns save refocus passed")


def test_table_columns_delete_progress_stays_in_screen():
    master_fd, process = start_process("table-columns-mutation")
    try:
        data = read_until(master_fd, "new_custom")
        data += read_available(master_fd, 0.5)
        assert "2 columns" in data
        assert "new_standard" in data
        press_key(master_fd, "D")
        progress = read_until(master_fd, DELETE_MUTATION, timeout=5)
        assert "Dataverse operation" not in progress
        assert "new_standard" in progress
        assert "━" in progress or "─" in progress
        completed = read_until(master_fd, "Pending changes", timeout=10)
        completed += read_available(master_fd, 1.0)
        assert "1 column" in completed
        assert "Dataverse operation" not in completed
        press_key(master_fd, "\x1b")
        final = drain_until_exit(master_fd, process)
        assert "close" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns delete progress passed")


def test_table_columns_publish_progress_stays_in_screen():
    master_fd, process = start_process("table-columns-mutation")
    try:
        data = read_until(master_fd, "new_custom")
        data += read_available(master_fd, 0.5)
        assert "2 columns" in data
        press_key(master_fd, "P")
        progress = read_until(master_fd, PUBLISH_MUTATION, timeout=5)
        assert "Dataverse operation" not in progress
        assert "new_custom" in progress
        assert "━" in progress or "─" in progress
        completed = read_until(
            master_fd, "Publishing table account completed.", timeout=10
        )
        completed += read_available(master_fd, 1.0)
        assert "Pending changes" not in completed
        assert "Dataverse operation" not in completed
        press_key(master_fd, "\x1b")
        final = drain_until_exit(master_fd, process)
        assert "close" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("table-columns publish progress passed")


def test_confirmation_stays_in_tui():
    master_fd, process = start_process("confirmation")
    try:
        data = read_until(master_fd, "Delete column")
        data += read_available(master_fd, 0.3)
        assert "Environment" in data
        assert "new_custom" in data
        for char in b"new_custom":
            os.write(master_fd, bytes([char]))
            time.sleep(0.03)
        press_key(master_fd, "\r")
        final = drain_until_exit(master_fd, process)
        assert "confirmed" in final
        assert "\x1b[?1049h" in data
        assert "\x1b[?1049l" not in data
    finally:
        stop_process(process)
        os.close(master_fd)
    print("confirmation TUI lifecycle passed")


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
        intermediate = read_available(master_fd, 0.3)
        assert process.poll() is None
        assert "Table created" not in intermediate
        press_key(master_fd, "\x13")
        final = drain_until_exit(master_fd, process)
        assert "Table created" in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("create-table submit passed")


def test_create_table_locks_input_during_progress():
    master_fd, process = start_process("create-table-slow")
    try:
        data = read_until(master_fd, "Create table")
        data += read_until(master_fd, "Publisher prefix")
        for char in b"Slow Table":
            os.write(master_fd, bytes([char]))
            time.sleep(0.05)
        read_bounded(master_fd)
        press_key(master_fd, "\x13")
        progress = read_until(master_fd, "Creating table...", timeout=3)
        assert "━" in progress or "─" in progress
        os.write(master_fd, b"\x1b\rQ")
        time.sleep(0.5)
        assert process.poll() is None
        final = drain_until_exit(master_fd, process, timeout=15)
        assert "Table created" in final
        assert "Operation cancelled" not in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("create-table progress input lock passed")


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
        intermediate = read_available(master_fd, 0.3)
        assert process.poll() is None
        assert "Column created" not in intermediate
        press_key(master_fd, "\x13")
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
        press_key(master_fd, "\x13")
        final = drain_until_exit(master_fd, process)
        assert "Column saved" in final
        assert "submit" in final
    finally:
        stop_process(process)
        os.close(master_fd)
    print("column-editor edit passed")


def main():
    build_test_host()
    tests = [
        test_solution_selection_navigates_and_selects,
        test_solution_browser_navigates_and_esc,
        test_solution_browser_small_terminal,
        test_solution_browser_readonly_disables_writes,
        test_solution_browser_tall_terminal,
        test_solution_browser_wide_terminal,
        test_solution_browser_wide_small_terminal,
        test_solution_browser_wide_tall_terminal,
        test_solution_browser_wide_tall_small_terminal,
        test_table_columns_escape_returns_to_list,
        test_table_columns_navigates_and_deletes,
        test_table_columns_edits_in_detail_pane,
        test_table_columns_save_returns_focus_to_list,
        test_table_columns_delete_progress_stays_in_screen,
        test_table_columns_publish_progress_stays_in_screen,
        test_confirmation_stays_in_tui,
        test_create_table_submits,
        test_create_table_locks_input_during_progress,
        test_column_editor_creates,
        test_column_editor_edits,
    ]
    for test in tests:
        test()
    print(f"All {len(tests)} terminal integration tests passed.")


if __name__ == "__main__":
    main()
