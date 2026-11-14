"""Fault injection for the linearly varying load terms of the solver kernel.

Each mutant patches one place in src/, rebuilds the product test project, runs the Product tests and records
the failing tests. A mutant is killed when at least one test fails. The source is always restored.

Usage (repository root, after a successful Release build of tests/SpanDraft.Tests):
    python3 validation/investigations/linear-load-mutants/mutate.py [--only ID,ID] [--output file.json]
"""
import json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
K = 'src/SpanDraft.Solver/Kernel/'
LOADS, QUALITY, RESULT, EXC, INPUT = (K + 'BendingLoads.cs', K + 'BendingQuality.cs', K + 'BendingResult.cs',
                                      K + 'Numerics/BendingExcitation.cs', K + 'MechanicalInput.cs')

MUTANTS = [
    ('R1', 'ramp divisor inside the load, p = 4: 121 instead of 120',
     [(LOADS, 'p switch { 1 => 2, 2 => 6, 3 => 24, _ => 120 }', 'p switch { 1 => 2, 2 => 6, 3 => 24, _ => 121 }')]),
    ('R2', 'ramp divisor beyond the load, p = 2: 7 instead of 6',
     [(LOADS, '(t * (width + far * new Bounded(3))) / new Bounded(6)', '(t * (width + far * new Bounded(3))) / new Bounded(7)')]),
    ('R3', 'ramp divisor beyond the load, p = 3: 25 instead of 24',
     [(LOADS, '/ new Bounded(24),\n            _ => (t * (((width', '/ new Bounded(25),\n            _ => (t * (((width')]),
    ('R4', 'ramp divisor beyond the load, p = 4: 121 instead of 120',
     [(LOADS, '                / new Bounded(120)\n        };', '                / new Bounded(121)\n        };')]),
    ('R5', 'sign of the rise reversed',
     [(LOADS, 'internal Bounded Rise => EndIntensity - StartIntensity;', 'internal Bounded Rise => StartIntensity - EndIntensity;')]),
    ('R6', 'near and far swapped in the outer ramp polynomial, p = 2',
     [(LOADS, '(t * (width + far * new Bounded(3)))', '(t * (width + near * new Bounded(3)))')]),
    ('R7', 'clip: interpolated intensity replaced by the start intensity',
     [(LOADS, 'Bounded value = new Bounded(first) + (new Bounded(last) - new Bounded(first)) * share;', 'Bounded value = new Bounded(first);')]),
    ('R8', 'clip: data error of the interpolated intensity dropped',
     [(LOADS, 'return new(value.Value, value.Error);', 'return new(value.Value, 0);')]),
    ('R9', 'clip: load end intensity interpolated instead of taken exactly',
     [(LOADS, 'if (position == load.End)\n                return new(last);', '')]),
    ('R10', 'ramp term omitted from the shear integral (reactions and V)',
     [(LOADS, 'if (line.HasRamp)\n                total += BendingLoadTerms.Ramp', 'if (line.HasRamp && p != 1)\n                total += BendingLoadTerms.Ramp')]),
    ('R11', 'ramp term omitted from the moment integral',
     [(LOADS, 'if (line.HasRamp)\n                total += BendingLoadTerms.Ramp', 'if (line.HasRamp && p != 2)\n                total += BendingLoadTerms.Ramp')]),
    ('N1', 'excitation norm: maximum of the end intensities instead of their mean',
     [(EXC, 'AddLineTerm(load.StartNewtonsPerMeter, -1);\n                AddLineTerm(load.EndNewtonsPerMeter, -1);',
       'AddLineTerm(Abs(load.StartNewtonsPerMeter) >= Abs(load.EndNewtonsPerMeter) ? load.StartNewtonsPerMeter : load.EndNewtonsPerMeter, 0);')]),
    ('N2', 'excitation: unloaded when only the start intensity is zero',
     [(EXC, 'IsZero(load.StartNewtonsPerMeter) && IsZero(load.EndNewtonsPerMeter);', 'IsZero(load.StartNewtonsPerMeter);'),
      (EXC, 'input.LineLoads.All(load => IsZero(load.StartNewtonsPerMeter) && IsZero(load.EndNewtonsPerMeter));', 'input.LineLoads.All(load => IsZero(load.StartNewtonsPerMeter));')]),
    ('N3', 'exact excitation exponent: doubled norm not halved',
     [(EXC, 'return (int)sum.GetBitLength() - 1 + 2 * UnitExponent - 1;', 'return (int)sum.GetBitLength() - 1 + 2 * UnitExponent;')]),
    ('N4', 'exact excitation exponent: end intensity omitted',
     [(EXC, 'BigInteger.Abs(ExactUnits(load.StartNewtonsPerMeter)) +\n                    BigInteger.Abs(ExactUnits(load.EndNewtonsPerMeter))', 'BigInteger.Abs(ExactUnits(load.StartNewtonsPerMeter)) * 2')]),
    ('Q1', 'piece majorant uses only the intensity at the piece start',
     [(QUALITY, 'Math.Max(Majorant(line.IntensityAt(x0, fields.EL)), Majorant(line.IntensityAt(x1, fields.EL)))', 'Majorant(line.IntensityAt(x0, fields.EL))')]),
    ('Q2', 'piece majorant without the data error of the intensity',
     [(QUALITY, 'static double Majorant(Bounded intensity) => Add(Abs(intensity.Value), intensity.Error, true);', 'static double Majorant(Bounded intensity) => Add(Abs(intensity.Value), 0, true);')]),
    ('E1', 'shear polynomial: factor 1/2 of the intensity change missing',
     [(RESULT, 'shear = shear.Add(change * h / 2);', 'shear = shear.Add(change * h);')]),
    ('E2', 'rotation polynomial: 12 instead of 24 for the intensity change',
     [(RESULT, '(((change * h) * h) * h / 24) / ei', '(((change * h) * h) * h / 12) / ei')]),
    ('E3', 'shear candidates ignore the intensity change',
     [(RESULT, 'PolynomialRoots.InUnitInterval(Intensity, IntensityChange)', 'PolynomialRoots.InUnitInterval(Intensity)')]),
    ('E4', 'piece intensity change from a difference of end values with the wrong piece length',
     [(RESULT, 'change += line.Rise.Value * (h / width);', 'change += line.Rise.Value;')]),
    ('I1', 'input order ignores the end intensity',
     [(INPUT, ',\n                DoubleToInt64Bits(q.StartNewtonsPerMeter), DoubleToInt64Bits(q.EndNewtonsPerMeter)))', ',\n                DoubleToInt64Bits(q.StartNewtonsPerMeter)))')]),
]


def run(command):
    return subprocess.run(command, cwd=ROOT, capture_output=True, text=True)


def main():
    only = None
    output = None
    args = sys.argv[1:]
    if '--only' in args:
        only = set(args[args.index('--only') + 1].split(','))
    if '--output' in args:
        output = args[args.index('--output') + 1]
    results = []
    for ident, description, patches in MUTANTS:
        if only and ident not in only:
            continue
        originals = {}
        try:
            for path, old, new in patches:
                full = os.path.join(ROOT, path)
                text = originals.setdefault(path, open(full, encoding='utf-8').read())
                current = open(full, encoding='utf-8').read()
                if current.count(old) != 1:
                    raise SystemExit(f'{ident}: pattern not unique ({current.count(old)}) in {path}: {old!r}')
                open(full, 'w', encoding='utf-8').write(current.replace(old, new))
            build = run(['dotnet', 'build', 'tests/SpanDraft.Tests', '-c', 'Release', '--no-restore'])
            if build.returncode != 0:
                results.append({'id': ident, 'description': description, 'status': 'build-error'})
                print(ident, 'build error')
                continue
            test = run(['dotnet', 'test', '--project', 'tests/SpanDraft.Tests', '-c', 'Release', '--no-build'])
            failed = re.findall(r'^(?:fehlerhaft|failed) (.+?)(?: \([\d,.]+ ?m?s\))?$', test.stdout, re.M)
            names = sorted({re.sub(r'\(.*', '', f).split('.')[-2] + '.' + re.sub(r'\(.*', '', f).split('.')[-1] for f in failed})
            status = 'killed' if test.returncode != 0 else 'SURVIVED'
            results.append({'id': ident, 'description': description, 'status': status,
                            'failedTests': len(failed), 'tests': names[:6]})
            print(ident, status, len(failed), names[:3])
        finally:
            for path, text in originals.items():
                open(os.path.join(ROOT, path), 'w', encoding='utf-8').write(text)
    if output:
        json.dump(results, open(output, 'w'), indent=1)
    run(['dotnet', 'build', 'tests/SpanDraft.Tests', '-c', 'Release', '--no-restore'])
    return 0 if all(r['status'] == 'killed' for r in results) else 1


if __name__ == '__main__':
    sys.exit(main())
