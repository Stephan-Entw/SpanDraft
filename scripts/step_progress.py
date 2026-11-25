"""Console output for a sequence of long-running steps.

Three modes, detected once:
  live   terminal with ANSI support: colours, a block of lines updated in place, Unicode symbols
  plain  terminal without ANSI: one line updated with carriage returns, ASCII only
  log    redirected output: plain lines, no control characters, periodic status lines

Output never affects the caller: the first failing write silently turns the output off.
"""

import os
import shutil
import sys
import threading
import time
from collections import deque
from contextlib import contextmanager

LIVE, PLAIN, LOG = "live", "plain", "log"
TONES = {"success": "32", "busy": "33", "warning": "33", "error": "31", "info": "2"}
UNICODE = {"done": "✓", "failed": "✗", "full": "█", "empty": "░", "sep": " · ", "spin": "◐◓◑◒", "dots": "…"}
ASCII = {"done": "OK", "failed": "!!", "full": "#", "empty": "-", "sep": " | ", "spin": "|/-\\", "dots": "..."}
TITLE_COLUMN = 50  # Width of "[ n/N] title" in finished lines; fits the longest submission step.
LOG_INTERVAL = 30.0  # Seconds between repeated status lines in log mode.
LIVE_INTERVAL = 0.2  # Minimum seconds between upload redraws on terminals.


def _enable_windows_vt(stream):
    """Turn on ANSI processing of a Windows console; False if that is not possible."""
    try:
        import ctypes
        import msvcrt
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        handle = msvcrt.get_osfhandle(stream.fileno())
        mode = ctypes.c_uint32()
        if not kernel32.GetConsoleMode(handle, ctypes.byref(mode)):
            return False
        return bool(kernel32.SetConsoleMode(handle, mode.value | 0x0004))  # ENABLE_VIRTUAL_TERMINAL_PROCESSING
    except Exception:
        return False


def detect(stream, environ=os.environ):
    """Return (mode, colour, unicode) for a stream."""
    try:
        terminal = stream.isatty()
    except Exception:
        terminal = False
    if not terminal:
        return LOG, False, False
    if environ.get("TERM") == "dumb" or (os.name == "nt" and not _enable_windows_vt(stream)):
        return PLAIN, False, False
    try:
        "".join(UNICODE.values()).encode(getattr(stream, "encoding", None) or "ascii")
        unicode = True
    except (LookupError, UnicodeError):
        unicode = False
    return LIVE, "NO_COLOR" not in environ, unicode


def clock_text(seconds):
    """MM:SS, or H:MM:SS from one hour."""
    seconds = max(0, int(seconds))
    minutes, seconds = divmod(seconds, 60)
    hours, minutes = divmod(minutes, 60)
    return f"{hours}:{minutes:02d}:{seconds:02d}" if hours else f"{minutes:02d}:{seconds:02d}"


def _megabytes(value, total):
    return f"{value / 1e6:.0f}" if total >= 10e6 else f"{value / 1e6:.1f}"


class Steps:
    """Announce each step, show its progress while it runs and report its result and duration."""

    def __init__(self, total, stream=None, clock=time.monotonic, mode=None, colour=None, unicode=None,
                 width=None, tick=1.0, environ=os.environ):
        self.total, self.number, self.failed = total, 0, None
        self.stream = stream or sys.stdout
        self.clock = clock
        detected = detect(self.stream, environ) if mode is None else (mode, mode == LIVE, mode == LIVE)
        self.mode = detected[0]
        self.colour = detected[1] if colour is None else colour and self.mode == LIVE
        unicode = detected[2] if unicode is None else unicode and self.mode == LIVE
        self.symbols = UNICODE if unicode else ASCII
        self.width = width or (lambda: shutil.get_terminal_size((80, 24)).columns)
        self.tick = tick if self.mode == LIVE else None
        self.detail, self.title, self.started = "", "", None
        self.broken = False
        self._lock = threading.RLock()
        self._active = False
        self._extra = []  # Lines below the step title: (text, tone).
        self._drawn, self._drawn_width, self._single, self._single_length = 0, None, False, 0
        self._logged, self._logged_at = None, None
        self._wait = None
        self._spin = 0

    # Steps ---------------------------------------------------------------------------------------
    @contextmanager
    def step(self, title):
        with self._lock:
            self.number += 1
            self.title, self.detail, self.started = title, "", self.clock()
            self._extra, self._wait, self._active = [], None, True
            self._drawn, self._drawn_width, self._single, self._single_length = 0, None, False, 0
            self._logged = self._logged_at = None
            if self.mode == LOG:
                self._write(f"{self._prefix()} {title}\n")
            else:
                self._render()
        stop, ticker = self._start_ticker()
        succeeded = False
        try:
            yield self
            succeeded = True
        finally:
            if ticker:
                stop.set()
                ticker.join(2)
            with self._lock:
                if not succeeded:
                    self.failed = self.number
                self._finish(succeeded)
                self._active = False

    def _start_ticker(self):
        if not self.tick:
            return None, None
        stop = threading.Event()

        def run():
            while not stop.wait(self.tick):
                self.refresh()

        ticker = threading.Thread(target=run, name="step-progress", daemon=True)
        ticker.start()
        return stop, ticker

    def refresh(self):
        """Redraw the running step, e.g. to update its elapsed time."""
        with self._lock:
            if self._active and self.mode != LOG:
                if self._wait:
                    self._spin += 1
                    self._extra = self._wait_lines()
                self._render()

    # Progress of the running step -----------------------------------------------------------------
    def status(self, text, tone="info"):
        """Show an intermediate state below the step title."""
        with self._lock:
            self._extra = [(text, tone)]
            self._update(text, key=text)

    def waiting(self, status):
        """While waiting for a server: its last status and the elapsed time."""
        with self._lock:
            self._wait = {"status": status, "next": None}
            self._spin += 1
            self._extra = self._wait_lines()
            elapsed = clock_text(self.clock() - self.started)
            self._update(f"{status} (elapsed {elapsed})", key=status)

    def checking(self):
        with self._lock:
            if self._wait is not None:
                self._wait["next"] = None
                self._extra = self._wait_lines()
                self._render()

    def countdown(self, seconds, sleep):
        """Sleep the given whole seconds in one-second steps, showing the time to the next check."""
        for remaining in range(int(seconds), 0, -1):
            with self._lock:
                if self._wait is not None:
                    self._wait["next"] = remaining
                    self._spin += 1
                    self._extra = self._wait_lines()
                    self._render()
            sleep(1)

    def _wait_lines(self):
        elapsed = clock_text(self.clock() - self.started)
        spinner = self.symbols["spin"][self._spin % len(self.symbols["spin"])]
        if self._wait["next"] is None:
            follow = ("Checking status " + self.symbols["dots"], "busy")
        else:
            follow = (f"Next check in {self._wait['next']:02d} s", "info")
        return [(f"{spinner} {self._wait['status']}", "busy"),
                (f"Elapsed {elapsed}{self.symbols['sep']}{follow[0]}", follow[1])]

    def reader(self, stream, size):
        """Wrap a binary stream; the bytes the HTTP client reads from it drive the upload display."""
        return _Reader(self, stream, size)

    def _upload(self, sent, size, rate, remaining, done):
        with self._lock:
            # Rounded down: 100 % appears only once every byte was read.
            percent = 100 if done else min(99, int(100 * sent / max(size, 1)))
            amounts = f"{_megabytes(sent, size)} / {_megabytes(size, size)} MB"
            speed = f"{rate / 1e6:.1f} MB/s" if rate else "-- MB/s"
            eta = f"ETA {clock_text(remaining)}" if remaining is not None else "ETA --:--"
            sep = self.symbols["sep"]
            if done:
                second = (f"{amounts} transferred{sep}waiting for Microsoft to confirm {self.symbols['dots']}", "busy")
            else:
                second = (f"{amounts}{sep}{speed}{sep}{eta}", "info")
            self._extra = [(f"{self._bar(percent)} {percent:3d}%", "info"), second]
            self._update(f"{percent:3d}%  {second[0]}", key="done" if done else "sending")

    def _update(self, summary, key):
        """Redraw on terminals; in log mode write a line when the key changes, else every LOG_INTERVAL."""
        if self.mode != LOG:
            self._render()
            return
        now = self.clock()
        if key != self._logged or now - self._logged_at >= LOG_INTERVAL:
            self._logged, self._logged_at = key, now
            self._write(f"    {summary}\n")

    def _bar(self, percent):
        length = max(5, min(20, self.width() - 20))
        filled = int(length * percent / 100)
        return self.symbols["full"] * filled + self.symbols["empty"] * (length - filled)

    # Messages -------------------------------------------------------------------------------------
    def say(self, text="", tone=None):
        """A complete message line; never shortened."""
        with self._lock:
            self._leave_block()
            self._write(self._paint(text, tone) + "\n")

    def success(self, text):
        self.say(f"{self.symbols['done']} {text}", "success")

    # Drawing --------------------------------------------------------------------------------------
    def _prefix(self):
        return f"[{self.number:>{len(str(self.total))}}/{self.total}]"

    def _paint(self, text, tone):
        return f"\x1b[{TONES[tone]}m{text}\x1b[0m" if self.colour and tone and text else text

    def _render(self):
        if self.broken or not self._active or self.mode == LOG:
            return
        width = max(20, self.width())
        elapsed = clock_text(self.clock() - self.started)
        head = f"{self._prefix()} {self.title}"
        if self.mode == PLAIN or self._single or (
                self._drawn > 1 and width != self._drawn_width):
            if self.mode == LIVE and not self._single and self._drawn:
                self._write("\n")  # The width changed: continue below on one line.
            self._single = True
            parts = [head] + [text for text, _ in self._extra] + [elapsed]
            line = self._clip("  ".join(parts), width)
            erase = "\x1b[2K" if self.mode == LIVE else ""
            pad = 0 if erase else min(self._single_length, width - 1)  # Without ANSI, blank out the rest.
            self._write("\r" + erase + line.ljust(pad))
            self._single_length = len(line)
            return
        # Same columns as finished lines: a two-character mark slot, the title, then the elapsed time.
        indent = " " * (len(self._prefix()) + 3)
        lines = [self._clip(f"  {head.ljust(TITLE_COLUMN)} {elapsed.rjust(5)}", width)]
        lines += [indent + self._paint(self._clip(text, width - len(indent)), tone) for text, tone in self._extra]
        up = f"\x1b[{self._drawn - 1}F" if self._drawn > 1 else ""
        self._write(up + "\r\x1b[J" + "\n".join(lines))
        self._drawn, self._drawn_width = len(lines), width

    def _clip(self, text, width):
        limit = max(1, width - 1)
        return text if len(text) <= limit else text[:max(1, limit - len(self.symbols["dots"]))] + self.symbols["dots"]

    def _leave_block(self):
        if self.mode != LOG and self._active and (self._drawn or self._single):
            self._write("\n")
            self._drawn, self._single = 0, False

    def _finish(self, succeeded):
        elapsed = clock_text(self.clock() - self.started)
        head = f"{self._prefix()} {self.title}"
        if self.mode == LOG:
            text = f"ok, {self.detail}" if succeeded and self.detail else ("ok" if succeeded else "FAILED")
            self._write(f"{head}: {text} ({elapsed})\n")
            return
        if self.mode == PLAIN:
            text = (f"ok, {self.detail}" if self.detail else "ok") if succeeded else "FAILED"
            line = f"{head} ".ljust(TITLE_COLUMN + 4, ".") + f" {text} ({elapsed})"
            self._write("\r" + line.ljust(self._single_length) + "\n")
            return
        mark = self._paint(self.symbols["done" if succeeded else "failed"], "success" if succeeded else "error")
        outcome = self.detail if succeeded else self._paint("failed", "error")
        line = f"{mark} {head.ljust(TITLE_COLUMN)} {self._paint(elapsed.rjust(5), 'info')}  {outcome}".rstrip()
        if self._single:
            self._write("\r\x1b[2K" + line + "\n")
        else:
            up = f"\x1b[{self._drawn - 1}F" if self._drawn > 1 else ""
            self._write(up + "\r\x1b[J" + line + "\n")
        self._drawn, self._single = 0, False

    def _write(self, text):
        if self.broken:
            return
        try:
            self.stream.write(text)
            self.stream.flush()
        except Exception:
            self.broken = True  # The terminal is gone or cannot show this; the work goes on.


class _Reader:
    """Counts the bytes the HTTP client reads; 100 % only once everything was read."""

    WINDOW = 5.0

    def __init__(self, steps, stream, size):
        self.steps, self.stream, self.size = steps, stream, size
        self.sent, self.started, self.shown = 0, steps.clock(), None
        self.samples = deque([(self.started, 0)])
        self.interval = LIVE_INTERVAL if steps.mode != LOG else 0.0  # Log mode throttles itself.

    def read(self, amount=-1):
        chunk = self.stream.read(amount)
        self.sent += len(chunk)
        now = self.steps.clock()
        self.samples.append((now, self.sent))
        while len(self.samples) > 2 and now - self.samples[1][0] >= self.WINDOW:
            self.samples.popleft()
        done = self.sent >= self.size
        if done or self.shown is None or now - self.shown >= self.interval:
            self.shown = now
            then, before = self.samples[0]
            rate = (self.sent - before) / (now - then) if now - then >= 0.5 else None
            remaining = (self.size - self.sent) / rate if rate else None
            self.steps._upload(self.sent, self.size, rate, remaining, done)
        return chunk
