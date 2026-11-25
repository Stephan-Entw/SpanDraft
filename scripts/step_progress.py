"""Console output for a sequence of long-running steps, with a transfer display."""

import sys
import time
from contextlib import contextmanager


class Steps:
    """Announce each step before it starts, then report its result and duration."""

    def __init__(self, total, stream=None, clock=time.monotonic):
        self.total, self.number, self.failed = total, 0, None
        self.stream = stream or sys.stdout
        self.clock = clock
        self.detail, self._prefix, self._width = "", "", 0

    @contextmanager
    def step(self, title):
        self.number += 1
        self.detail, self._width = "", 0
        self._prefix = f"[{self.number}/{self.total}] {title} ".ljust(44, ".") + " "
        started = self.clock()
        self._write(self._prefix)
        try:
            yield self
        except BaseException:
            self.failed = self.number
            self._finish("failed")
            raise
        detail = f", {self.detail}" if self.detail else ""
        self._finish(f"ok{detail} ({self.clock() - started:.0f} s)")

    def status(self, text):
        """Show an intermediate state of the running step (terminals only)."""
        if self.stream.isatty():
            self._show(text)

    def reader(self, stream, size, interval=0.5):
        """Wrap a binary stream so that reading it updates the current line (terminals only)."""
        return _Reader(self, stream, size, interval)

    def _show(self, text):
        self._write("\r" + self._prefix + text.ljust(self._width))
        self._width = len(text)

    def _finish(self, text):
        if self._width:
            self._show(text)
            self._write("\n")
        else:
            self._write(text + "\n")

    def _write(self, text):
        self.stream.write(text)
        self.stream.flush()


class _Reader:
    def __init__(self, steps, stream, size, interval):
        self.steps, self.stream, self.size, self.interval = steps, stream, size, interval
        self.sent, self.started, self.shown = 0, steps.clock(), None
        self.live = steps.stream.isatty()

    def read(self, amount=-1):
        chunk = self.stream.read(amount)
        self.sent += len(chunk)
        now = self.steps.clock()
        done = self.sent >= self.size
        if self.live and (done or self.shown is None or now - self.shown >= self.interval):
            self.shown = now
            rate = self.sent / max(now - self.started, 0.001)
            text = (f"{100 * self.sent / max(self.size, 1):3.0f} %  {self.sent / 1e6:.0f} / "
                    f"{self.size / 1e6:.0f} MB  {rate / 1e6:.1f} MB/s")
            self.steps._show(text + ("  waiting for confirmation" if done else ""))
        return chunk
