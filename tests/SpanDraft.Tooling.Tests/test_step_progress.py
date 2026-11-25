import io
import re
import threading
import unittest

from scripts import step_progress
from scripts.step_progress import Steps, clock_text, detect

ANSI = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")


class Recorder(io.StringIO):
    """Keeps every write separately, like frames on a terminal."""

    def __init__(self, terminal=True, encoding="utf-8"):
        super().__init__()
        self.frames, self.terminal, self._encoding = [], terminal, encoding

    @property
    def encoding(self):
        return self._encoding

    def write(self, text):
        self.frames.append(text)
        return super().write(text)

    def isatty(self):
        return self.terminal


class Clock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class Chunks:
    """A stream of `size` bytes read in fixed chunks; the clock advances per read."""

    def __init__(self, size, chunk, clock, seconds):
        self.left, self.chunk, self.clock, self.seconds = size, chunk, clock, seconds

    def read(self, amount=-1):
        self.clock.now += self.seconds
        count = min(self.chunk, self.left)
        self.left -= count
        return b"x" * count


def steps(mode="live", width=80, colour=True, unicode=True, stream=None, clock=None):
    clock = clock or Clock()
    stream = stream or Recorder(terminal=mode != "log")
    return Steps(10, stream=stream, clock=clock, mode=mode, colour=colour, unicode=unicode,
                 width=lambda: width if not callable(width) else width(), tick=None), stream, clock


def visible_lines(frames):
    """Lines of the in-place frames as a terminal shows them (finished lines excluded)."""
    lines = []
    for frame in frames:
        if "✓" in frame or "✗" in frame:
            continue
        for part in re.split(r"[\r\n]", ANSI.sub("", frame)):
            if part:
                lines.append(part)
    return lines


class UploadDisplayTests(unittest.TestCase):
    def upload(self, mode="live", width=80, rate=0.6e6, size=85_000_000, stop_at=None):
        progress, stream, clock = steps(mode, width)
        with progress.step("Uploading files"):
            reader = progress.reader(Chunks(size, int(rate), clock, 1.0), size)
            sent = 0
            while sent < (stop_at or size):
                sent += len(reader.read(1 << 20))
            self.before_end = stream.getvalue()
            progress.detail = "85 MB received by Microsoft"
        return stream

    def test_bar_percent_amounts_rate_and_eta(self):
        stream = self.upload(stop_at=53_000_000)
        output = ANSI.sub("", stream.getvalue())
        # 89 reads of 0.6 MB: 53.4 MB sent, 31.6 MB left at 0.6 MB/s.
        self.assertIn("62%", output)
        self.assertIn("53 / 85 MB · 0.6 MB/s · ETA 00:52", output)
        self.assertRegex(output, "█{12}░{8}  62%")
        self.assertIn("\x1b[J", stream.getvalue())

    def test_100_percent_only_after_everything_was_read_and_received_only_after_the_answer(self):
        progress, stream, clock = steps()
        with progress.step("Uploading files"):
            reader = progress.reader(Chunks(1000, 999, clock, 1.0), 1000)
            reader.read(999)
            self.assertIn(" 99%", ANSI.sub("", stream.frames[-1]))
            self.assertNotIn("100%", stream.getvalue())
            reader.read(999)
            last = ANSI.sub("", stream.frames[-1])
            self.assertIn("100%", last)
            self.assertIn("transferred · waiting for Microsoft to confirm", last)
            self.assertNotIn("received", stream.getvalue())
            progress.detail = "received by Microsoft"
        self.assertIn("✓", stream.frames[-1])
        self.assertIn("received by Microsoft", stream.frames[-1])

    def test_unknown_rate_shows_no_estimate(self):
        progress, stream, clock = steps()
        with progress.step("Uploading files"):
            progress.reader(Chunks(10_000_000, 1000, clock, 0.0), 10_000_000).read(1000)
        self.assertIn("-- MB/s · ETA --:--", ANSI.sub("", stream.getvalue()))

    def test_redraws_are_throttled(self):
        progress, stream, clock = steps()
        with progress.step("Uploading files"):
            reader = progress.reader(Chunks(1000, 1, clock, 0.01), 1000)
            for _ in range(1000):
                reader.read(1)
        redraws = sum("ETA" in frame or "transferred" in frame for frame in stream.frames)
        self.assertLessEqual(redraws, 1000 * 0.01 / step_progress.LIVE_INTERVAL + 2)
        self.assertGreater(redraws, 10)

    def test_log_mode_writes_periodic_lines_without_control_characters(self):
        stream = self.upload(mode="log", rate=1e6, size=120_000_000)
        output = stream.getvalue()
        self.assertNotIn("\x1b", output)
        self.assertNotIn("\r", output)
        progress_lines = [line for line in output.splitlines() if "%" in line]
        self.assertLessEqual(len(progress_lines), 120 / step_progress.LOG_INTERVAL + 2)
        self.assertIn("100%  120 / 120 MB transferred", progress_lines[-1])
        self.assertTrue(output.splitlines()[-1].startswith("[ 1/10] Uploading files: ok, 85 MB received"))


class WaitingDisplayTests(unittest.TestCase):
    def test_spinner_status_elapsed_and_countdown_without_percentages(self):
        progress, stream, clock = steps()
        slept = []

        def sleep(seconds):
            slept.append(seconds)
            clock.sleep(seconds)

        with progress.step("Waiting for Microsoft to process"):
            progress.checking()
            progress.waiting("CommitStarted")
            progress.countdown(20, sleep)
            progress.checking()
            checking = ANSI.sub("", stream.frames[-1])
            clock.now = 134
            progress.waiting("PreProcessing")
        output = ANSI.sub("", stream.getvalue())
        self.assertEqual(slept, [1] * 20)
        for text in ("CommitStarted", "Next check in 20 s", "Next check in 01 s", "Elapsed 00:19", "PreProcessing",
                     "Elapsed 02:14"):
            self.assertIn(text, output)
        self.assertIn("Checking status …", checking)
        self.assertNotIn("Next check in 00 s", output)
        self.assertNotIn("%", output)
        self.assertGreater(len({c for c in output if c in "◐◓◑◒"}), 1)

    def test_log_mode_reports_status_changes_at_once_and_repeats_slowly(self):
        progress, stream, clock = steps("log")
        with progress.step("Waiting for Microsoft to process"):
            for second in range(120):
                clock.now = second
                progress.waiting("CommitStarted" if second < 100 else "PreProcessing")
        lines = [line for line in stream.getvalue().splitlines() if "elapsed" in line]
        self.assertEqual(lines[0].strip(), "CommitStarted (elapsed 00:00)")
        self.assertIn("PreProcessing (elapsed 01:40)", lines[-1])
        self.assertLessEqual(len(lines), 120 / step_progress.LOG_INTERVAL + 2)


class LayoutTests(unittest.TestCase):
    def test_lines_fit_every_terminal_width(self):
        for width in (120, 60, 30):
            with self.subTest(width=width):
                progress, stream, clock = steps(width=width)
                with progress.step("Committing: submit for certification"):
                    progress.reader(Chunks(85_000_000, 600_000, clock, 1.0), 85_000_000).read(1)
                    progress.waiting("CommitStarted with a rather long status text")
                    progress.countdown(2, clock.sleep)
                for line in visible_lines(stream.frames):
                    self.assertLessEqual(len(line), width - 1, line)

    def test_a_width_change_continues_on_one_line(self):
        width = [80]
        progress, stream, clock = steps(width=lambda: width[0])
        with progress.step("Uploading files"):
            reader = progress.reader(Chunks(10_000_000, 1_000_000, clock, 1.0), 10_000_000)
            reader.read(1)
            width[0] = 50
            reader.read(1)
            self.assertTrue(stream.frames[-2].startswith("\n"))
            self.assertTrue(stream.frames[-1].startswith("\r\x1b[2K"))
            self.assertNotIn("\n", stream.frames[-1])
            reader.read(1)
            self.assertNotIn("\x1b[1F", stream.frames[-1])
        self.assertTrue(stream.frames[-1].startswith("\r\x1b[2K✓") or "✓" in stream.frames[-1])

    def test_messages_and_results_are_never_shortened(self):
        progress, stream, clock = steps(width=30)
        message = "error: " + "x" * 200
        progress.say(message, "error")
        with progress.step("A step with a long title for a narrow terminal"):
            progress.detail = "a long result" * 5
        self.assertIn(message, stream.getvalue())
        self.assertIn("a long result" * 5, stream.getvalue())

    def test_finished_steps_are_aligned(self):
        progress, stream, clock = steps()
        for title in ("Reading credentials", "Packing and checking MSIX and screenshots"):
            with progress.step(title):
                clock.now += 65
                progress.detail = "ok"
        finished = [ANSI.sub("", frame).split("\r")[-1].strip("\n") for frame in stream.frames if "✓" in frame]
        column = len("✓ ") + step_progress.TITLE_COLUMN + 1
        self.assertEqual({line.index("01:05") for line in finished}, {column})
        self.assertTrue(all(line.endswith("01:05  ok") for line in finished))
        running = [ANSI.sub("", frame).split("\r")[-1] for frame in stream.frames if "✓" not in frame]
        self.assertEqual({line.index("00:00") for line in running}, {column})

    def test_failures_are_marked_and_counted(self):
        for error in (ValueError("bad"), KeyboardInterrupt()):
            with self.subTest(type(error).__name__):
                progress, stream, clock = steps()
                with self.assertRaises(type(error)):
                    with progress.step("Signing in to Microsoft"):
                        raise error
                self.assertEqual(progress.failed, 1)
                self.assertIn("✗", stream.frames[-1])
                self.assertIn("\x1b[31mfailed", stream.frames[-1])


class FallbackTests(unittest.TestCase):
    def run_all(self, progress, clock):
        with progress.step("Uploading files"):
            reader = progress.reader(Chunks(10_000_000, 2_000_000, clock, 1.0), 10_000_000)
            while reader.read(1 << 20):
                pass
            progress.detail = "10 MB received by Microsoft"
        with progress.step("Waiting for Microsoft to process"):
            progress.waiting("CommitStarted")
            progress.countdown(2, clock.sleep)
        progress.success("Submitted for certification")

    def test_plain_terminals_get_ascii_on_one_line_without_ansi(self):
        progress, stream, clock = steps("plain")
        self.run_all(progress, clock)
        output = stream.getvalue()
        self.assertNotIn("\x1b", output)
        self.assertTrue(output.isascii())
        self.assertIn("\r", output)
        self.assertIn("[ 1/10] Uploading files ", output)
        self.assertIn("ok, 10 MB received by Microsoft", output)
        self.assertIn("OK Submitted for certification", output)

    def test_no_color_keeps_updates_in_place_without_colours(self):
        progress, stream, clock = steps(colour=False)
        self.run_all(progress, clock)
        output = stream.getvalue()
        self.assertNotRegex(output, r"\x1b\[(3\d|2|0)m")
        self.assertIn("\x1b[J", output)

    def test_redirected_output_has_no_control_characters(self):
        progress, stream, clock = steps("log")
        self.run_all(progress, clock)
        output = stream.getvalue()
        self.assertNotIn("\x1b", output)
        self.assertNotIn("\r", output)
        self.assertTrue(output.isascii())

    def test_detection(self):
        self.assertEqual(detect(Recorder(terminal=False), {}), ("log", False, False))
        self.assertEqual(detect(Recorder(), {"TERM": "dumb"})[0], "plain")
        if step_progress.os.name != "nt":
            self.assertEqual(detect(Recorder(), {}), ("live", True, True))
            self.assertEqual(detect(Recorder(), {"NO_COLOR": "1"}), ("live", False, True))
            self.assertEqual(detect(Recorder(encoding="cp437"), {}), ("live", True, False))

        class Gone:
            def isatty(self):
                raise ValueError("closed")

        self.assertEqual(detect(Gone(), {})[0], "log")

    def test_clock_text(self):
        self.assertEqual([clock_text(s) for s in (0, 53.9, 134, 3600 + 61)], ["00:00", "00:53", "02:14", "1:01:01"])


class RobustnessTests(unittest.TestCase):
    def test_output_errors_turn_the_display_off_without_raising(self):
        class Broken(Recorder):
            def write(self, text):
                raise OSError("console closed")

        progress, stream, clock = steps(stream=Broken())
        with progress.step("Uploading files"):
            progress.reader(Chunks(100, 100, clock, 1.0), 100).read(100)
            progress.waiting("CommitStarted")
            progress.countdown(1, clock.sleep)
        progress.say("done")
        progress.success("ok")
        self.assertTrue(progress.broken)

    def test_the_ticker_stops_with_every_outcome_and_writes_nothing_afterwards(self):
        class Redraws(Recorder):
            def __init__(self):
                super().__init__()
                self.redrawn = threading.Event()

            def write(self, text):
                super().write(text)
                if len(self.frames) >= 3:
                    self.redrawn.set()

        for outcome in (None, ValueError("bad"), KeyboardInterrupt()):
            with self.subTest(outcome=type(outcome).__name__):
                stream = Redraws()
                progress = Steps(10, stream=stream, mode="live", width=lambda: 80, tick=0.01)
                try:
                    with progress.step("Signing in to Microsoft"):
                        self.assertTrue(stream.redrawn.wait(10))  # The ticker redraws the running step.
                        if outcome:
                            raise outcome
                except (ValueError, KeyboardInterrupt):
                    pass
                self.assertFalse([t for t in threading.enumerate() if t.name == "step-progress"])
                self.assertRegex(stream.frames[-1], "✓|✗")  # The result is the last output; nothing follows.


if __name__ == "__main__":
    unittest.main()
