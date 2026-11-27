#!/bin/bash
set -eo pipefail

repo=$(CDPATH= cd -P -- "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d "${TMPDIR:-/tmp}/spandraft-verify-tests.XXXXXXXX")
agent="$work/scripts/agent"
real_sleep=$(command -v sleep)
agent_pid=
native_dll=

cleanup() {
    if [[ -n "$agent_pid" ]]; then
        kill -TERM "$agent_pid" 2>/dev/null || :
        wait "$agent_pid" 2>/dev/null || :
    fi
    if [[ -n "$native_dll" && -f "$work/state/native-child" ]]; then
        "$native_dotnet" "$native_dll" kill "$(cat "$work/state/native-child")" "$(cat "$work/state/native-parent")" || :
    fi
    rm -rf -- "$work"
}
trap cleanup EXIT

fail() {
    printf 'Verify test failed: %s\n' "$*" >&2
    if [[ -f "$work/terminal" ]]; then cat "$work/terminal" >&2; fi
    exit 1
}

has() { grep -Fq -- "$2" "$1" || fail "missing '$2' in $1"; }
lacks() { if grep -Fq -- "$2" "$1"; then fail "unexpected '$2' in $1"; fi; }

await_pid() {
    local file=$1 pid limit=$((SECONDS + 45))
    until [[ -f "$file" ]] && IFS= read -r pid <"$file" && [[ $pid =~ ^[1-9][0-9]*$ ]]; do
        [[ $SECONDS -lt $limit ]] || fail "timed out waiting for a complete PID in $file"
        "$real_sleep" 0.1
    done
    printf '%s\n' "$pid"
}

mkdir -p "$work/scripts" "$work/bin" "$work/tmp"
# Keep both ends open so readiness and release can be exchanged without blocking on open.
mkfifo "$work/hold" "$work/heartbeat-ready"
exec 4<>"$work/hold"
exec 5<>"$work/heartbeat-ready"
cp "$repo/scripts/agent" "$agent"
for module in SpanDraft.Tests SpanDraft.Desktop.Tests SpanDraft.Rendering.Tests SpanDraft.Tooling.Tests; do
    dir="$work/tests/$module/bin/Release/net10.0"
    mkdir -p "$dir"
    : >"$dir/$module.dll"
done

cat >"$work/bin/git" <<'EOF'
#!/bin/bash
printf 'git %s\n' "$*" >>"$STATE/calls"
case "$*" in
    'merge-base '*) printf 'baseline\n' ;;
    *' diff --check')
        if [[ $SCENARIO == diff-error ]]; then printf 'patch rejected\n'; exit 23; fi ;;
esac
EOF

cat >"$work/bin/dotnet" <<'EOF'
#!/bin/bash
printf 'dotnet %s\n' "$*" >>"$STATE/calls"
case "$1" in
    build)
        case "$SCENARIO" in
            retry|restore-error|retry-error)
                if [[ ! -f "$STATE/restored" ]]; then
                    printf 'fixture : error NETSDK1004: Package assets file missing\n'
                    exit 42
                fi
                if [[ $SCENARIO == retry-error ]]; then exit 34; fi ;;
            build-error)
                for ((i = 1; i <= 15; i++)); do
                    printf 'fixture : error CS1234: diagnostic %s\n' "$i"
                done
                printf 'ALL DIAGNOSTICS ARE LOGGED\n'
                exit 25 ;;
            heartbeat)
                printf 'routine output\nfixture : warning CS2000: live warning\npartial '
                printf '\n' >&5
                if ! IFS= read -r -t 45 _ <&4; then
                    printf '\nTimed out waiting for the heartbeat monitor.\n' >&2
                    exit 1
                fi
                printf 'line\n' ;;
            cancel)
                IFS= read -r _ <&4 &
                child=$!
                printf '%s\n' "$child" >"$STATE/child"
                printf '%s\n' "$$" >"$STATE/worker"
                printf 'worker is ready\n'
                wait "$child" ;;
            native-abort)
                exec "$NATIVE_DOTNET" "$NATIVE_DLL" "$STATE" ;;
        esac
        printf 'full build output\n' ;;
    restore)
        if [[ $SCENARIO == restore-error ]]; then exit 19; fi
        printf 'full restore output\n'
        : >"$STATE/restored" ;;
    tests/*)
        module=${1%.dll}
        module=${module##*/}
        if [[ $SCENARIO == missing-summary ]]; then printf 'unexpected runner failure\n'; exit 17; fi
        failed=0
        status=0
        if [[ $SCENARIO == test-error ]]; then
            case "$module" in
                SpanDraft.Tests) failed=1; status=7 ;;
                SpanDraft.Desktop.Tests) failed=1; status=9 ;;
            esac
        fi
        if [[ $failed == 1 ]]; then printf '    %s.Fixture [FAIL]\n' "$module"; fi
        printf '   %s  Total: 3, Errors: 0, Failed: %s, Skipped: 1, Not Run: 0, Time: 0.1s\n' "$module" "$failed"
        exit "$status" ;;
    *) printf 'full tool output\n' ;;
esac
EOF

cat >"$work/bin/sleep" <<'EOF'
#!/bin/bash
case "$SCENARIO" in
    heartbeat) exit 0 ;;
    cancel|native-abort)
        if [[ ! -e "$STATE/timers" ]]; then
            printf '%s\n' "$$" >"$STATE/timers"
            IFS= read -r _ <&4
            exit
        fi ;;
esac
printf '%s\n' "$$" >>"$STATE/timers"
exec "$REAL_SLEEP" "$@"
EOF
chmod +x "$work/bin/git" "$work/bin/dotnet" "$work/bin/sleep"

cat >"$work/heartbeat-clock" <<'EOF'
unset BASH_ENV SECONDS
SECONDS=0
heartbeat_poll=0
# The worker liveness check runs in the same shell as the heartbeat clock.
kill() {
    if [[ $1 == -0 ]]; then
        heartbeat_poll=$((heartbeat_poll + 1))
        case "$heartbeat_poll" in
            1)
                if ! IFS= read -r -t 45 _ <&5; then
                    printf 'Timed out waiting for the heartbeat worker.\n' >&2
                    exit 1
                fi
                SECONDS=29 ;;
            2) SECONDS=30 ;;
            3) SECONDS=59 ;;
            4) SECONDS=60 ;;
            5) printf '\n' >&4 ;;
        esac
    fi
    builtin kill "$@"
}
EOF

start_verify() {
    local scenario=$1 bash_env=
    shift
    rm -rf -- "$work/state"
    mkdir "$work/state"
    if [[ $scenario == heartbeat ]]; then bash_env="$work/heartbeat-clock"; fi
    BASH_ENV="$bash_env" SCENARIO="$scenario" STATE="$work/state" REAL_SLEEP="$real_sleep" TMPDIR="$work/tmp" \
        PATH="$work/bin:$PATH" "$BASH" "$agent" verify "$@" >"$work/terminal" 2>&1 &
    agent_pid=$!
}

finish_verify() {
    local expected=$1 actual
    if wait "$agent_pid"; then actual=0; else actual=$?; fi
    agent_pid=
    [[ $actual == "$expected" ]] || fail "expected exit $expected, got $actual"
    terminal="$work/terminal"
    log=$(sed -n 's/^VERIFY .* | log: //p' "$terminal")
    [[ -f "$log" ]] || fail 'log is unavailable'
    has "$terminal" "| exit: $expected |"
    if [[ $expected == 0 ]]; then has "$terminal" 'VERIFY PASSED'; else has "$terminal" 'VERIFY FAILED'; fi
    if compgen -G "$work/tmp/spandraft-agent.*" >/dev/null; then fail 'temporary build directory remained'; fi
}

run_verify() {
    local expected=$1
    shift
    start_verify "$@"
    finish_verify "$expected"
}

assert_stopped() {
    local limit=$((SECONDS + 5)) pid=$1
    while kill -0 -- "$pid" 2>/dev/null; do
        [[ $SECONDS -lt $limit ]] || fail "process $pid survived cancellation"
        "$real_sleep" 0.1
    done
}

assert_timers_stopped() {
    local timer
    while IFS= read -r timer; do assert_stopped "$timer"; done <"$work/state/timers"
}

abort_verify() {
    local ready=$1 limit=$((SECONDS + 45))
    await_pid "$work/state/timers" >/dev/null
    # PID files can be visible before tee flushes the worker's output to the log.
    until grep -Fq -- "$ready" "$work"/artifacts/verify/run.*/verify.log; do
        [[ $SECONDS -lt $limit ]] || fail "timed out waiting for '$ready' in the log"
        "$real_sleep" 0.1
    done
    kill -TERM "$agent_pid"
    finish_verify 143
    has "$log" "$ready"
    assert_timers_stopped
}

if [[ ${1:-} == --native-abort ]]; then
    native_dotnet=$(command -v dotnet)
    cat >"$work/AbortProbe.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
    cat >"$work/Program.cs" <<'EOF'
using System.Diagnostics;
using System.Reflection;

if (args[0] is "check" or "kill")
{
    foreach (string pid in args.Skip(1))
    {
        Process process;
        try { process = Process.GetProcessById(int.Parse(pid)); }
        catch (ArgumentException) { continue; }
        using (process)
        {
            if (process.HasExited) continue;
            if (args[0] == "check")
            {
                Console.Error.WriteLine($"Native process {pid} is still running.");
                return 1;
            }
            process.Kill();
            process.WaitForExit();
        }
    }
    return 0;
}

if (args[0] == "child")
{
    File.WriteAllText(Path.Combine(args[1], "native-child"), $"{Environment.ProcessId}\n");
}
else
{
    File.WriteAllText(Path.Combine(args[0], "native-parent"), $"{Environment.ProcessId}\n");
    Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        ArgumentList = { Assembly.GetExecutingAssembly().Location, "child", args[0] }
    });
    Console.WriteLine("native worker is ready");
}
await Task.Delay(Timeout.Infinite);
return 0;
EOF
    "$native_dotnet" build "$work/AbortProbe.csproj" --disable-build-servers -p:NuGetAudit=false --ignore-failed-sources --tl:off >"$work/probe-build.log" 2>&1 || { cat "$work/probe-build.log"; fail 'native probe failed to build'; }
    native_dll="$work/bin/Debug/net10.0/AbortProbe.dll"
    export NATIVE_DLL="$native_dll" NATIVE_DOTNET="$native_dotnet"
    start_verify native-abort build
    native_parent=$(await_pid "$work/state/native-parent")
    native_child=$(await_pid "$work/state/native-child")
    if [[ $OSTYPE == darwin* ]]; then
        native_group=$(ps -o pgid= -p "$native_parent" | tr -d '[:space:]')
        [[ $native_group =~ ^[1-9][0-9]*$ ]] || fail 'could not identify the native worker process group'
    fi
    abort_verify 'native worker is ready'
    "$native_dotnet" "$native_dll" check "$native_child" "$native_parent" || fail 'a native .NET process survived cancellation'
    if [[ $OSTYPE == darwin* ]]; then assert_stopped "-$native_group"; fi
    native_dll=
    printf 'Native .NET cancellation test passed.\n'
    exit 0
fi

[[ $# -eq 0 ]] || fail 'use test-agent-verify.sh [--native-abort]'
run_verify 0 success
has "$terminal" 'VERIFY PASSED — scope: all | tests: 12 total, 8 passed, 0 failed, 4 skipped'
has "$terminal" '[OK] help-assets ('
has "$log" 'full build output'
has "$log" 'full tool output'
lacks "$terminal" 'full build output'
lacks "$terminal" 'full tool output'
run_verify 0 success test-tooling
has "$terminal" 'No rebuild or freshness check is performed.'
has "$terminal" 'scope: test-tooling | tests: 3 total, 2 passed'
lacks "$work/state/calls" 'dotnet build'
lacks "$work/state/calls" 'git '

run_verify 23 diff-error diff
has "$terminal" '[FAIL] diff ('
has "$terminal" 'patch rejected'
lacks "$work/state/calls" 'diff --cached'
run_verify 25 build-error build
has "$terminal" 'diagnostic 12'
lacks "$terminal" 'diagnostic 13'
has "$terminal" 'Additional diagnostics in '
has "$log" 'diagnostic 15'
has "$log" 'ALL DIAGNOSTICS ARE LOGGED'
lacks "$work/state/calls" 'dotnet restore'
run_verify 0 retry build
has "$log" 'full restore output'
has "$log" 'NETSDK1004'
[[ $(grep -c '^dotnet build ' "$work/state/calls") == 2 ]] || fail 'expected one build retry'
run_verify 19 restore-error build
[[ $(grep -c '^dotnet build ' "$work/state/calls") == 1 ]] || fail 'retried after a failed restore'
run_verify 34 retry-error build
run_verify 7 test-error
has "$terminal" '[OK] test-tooling ('
has "$terminal" 'tests: 12 total, 6 passed, 2 failed, 4 skipped'
lacks "$terminal" '[RUN] icons'
run_verify 17 missing-summary test-tooling
has "$terminal" 'tests: unavailable'

run_verify 0 heartbeat build
heartbeat=$(sed -n 's/^\[RUN\] build (elapsed \([0-9][0-9]*\)s)$/\1/p' "$terminal")
[[ $heartbeat == $'30\n60' ]] || fail 'expected exactly one heartbeat each at 30 and 60 seconds'
has "$terminal" 'live warning'
lacks "$terminal" 'routine output'
has "$log" 'routine output'
has "$log" 'partial line'

start_verify cancel build
child=$(await_pid "$work/state/child")
worker=$(await_pid "$work/state/worker")
abort_verify 'worker is ready'
has "$terminal" '[FAIL] build ('
assert_stopped "$child"
assert_stopped "$worker"
printf 'Verify shell tests passed.\n'
