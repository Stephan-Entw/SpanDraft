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

await_text() {
    local file=$1 message=$2 limit=$((SECONDS + 45))
    until [[ -f "$file" ]] && grep -Fq -- "$message" "$file"; do
        [[ $SECONDS -lt $limit ]] || fail "timed out waiting for '$message'"
        "$real_sleep" 0.1
    done
}

mkdir -p "$work/scripts" "$work/bin" "$work/tmp"
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
                until [[ -f "$STATE/release" ]]; do "$REAL_SLEEP" 0.1; done
                printf 'line\n' ;;
            cancel)
                "$REAL_SLEEP" 60 &
                printf '%s\n' "$!" >"$STATE/child"
                printf '%s\n' "$$" >"$STATE/worker"
                printf 'worker is ready\n'
                wait "$!" ;;
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
printf '%s\n' "$$" >>"$STATE/timers"
exec "$REAL_SLEEP" "$@"
EOF
chmod +x "$work/bin/git" "$work/bin/dotnet" "$work/bin/sleep"

start_verify() {
    local scenario=$1
    shift
    rm -rf -- "$work/state"
    mkdir "$work/state"
    SCENARIO="$scenario" STATE="$work/state" REAL_SLEEP="$real_sleep" TMPDIR="$work/tmp" \
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
    while kill -0 "$pid" 2>/dev/null; do
        [[ $SECONDS -lt $limit ]] || fail "process $pid survived cancellation"
        "$real_sleep" 0.1
    done
}

assert_timers_stopped() {
    local timer
    while IFS= read -r timer; do assert_stopped "$timer"; done <"$work/state/timers"
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
    File.WriteAllText(Path.Combine(args[1], "native-child"), Environment.ProcessId.ToString());
}
else
{
    File.WriteAllText(Path.Combine(args[0], "native-parent"), Environment.ProcessId.ToString());
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
    await_text "$work/state/native-child" ''
    await_text "$work/state/timers" ''
    kill -TERM "$agent_pid"
    finish_verify 143
    has "$log" 'native worker is ready'
    assert_timers_stopped
    "$native_dotnet" "$native_dll" check "$(cat "$work/state/native-child")" "$(cat "$work/state/native-parent")" || fail 'a native .NET process survived cancellation'
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

start_verify heartbeat build
await_text "$work/terminal" 'live warning'
await_text "$work/terminal" '[RUN] build (elapsed '
heartbeat=$(sed -n 's/^\[RUN\] build (elapsed \([0-9][0-9]*\)s)$/\1/p' "$work/terminal")
[[ $heartbeat -ge 30 && $heartbeat -le 31 ]] || fail 'expected the first heartbeat at 30 seconds'
lacks "$work/terminal" 'routine output'
: >"$work/state/release"
finish_verify 0
has "$log" 'routine output'
has "$log" 'partial line'

start_verify cancel build
await_text "$work/state/child" ''
await_text "$work/state/worker" ''
await_text "$work/state/timers" ''
child=$(cat "$work/state/child")
worker=$(cat "$work/state/worker")
kill -TERM "$agent_pid"
finish_verify 143
has "$log" 'worker is ready'
has "$terminal" '[FAIL] build ('
assert_stopped "$child"
assert_stopped "$worker"
assert_timers_stopped
printf 'Verify shell tests passed.\n'
