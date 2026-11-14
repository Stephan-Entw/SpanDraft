"""Fault injection for the spring support terms of the solver kernel and Core.

Each mutant patches one place in src/, rebuilds the product test project, runs the Product tests and records
the failing tests. A mutant is killed when at least one test fails. The source is always restored.

Usage (repository root, after a successful Release build of tests/SpanDraft.Tests):
    python3 validation/investigations/spring-support-mutants/mutate.py [--only ID,ID] [--output file.json]
"""
import json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
K = 'src/SpanDraft.Solver/Kernel/'
SYSTEM, REACT, PREP, STATION, STAB, KERNEL = (K + 'BendingSystem.cs', K + 'BendingReactions.cs', K + 'BendingPreparation.cs',
                                              K + 'StationModel.cs', K + 'MechanicalStability.cs', K + 'SolverKernel.cs')
ADAPTER = 'src/SpanDraft.Solver/BeamModelAdapter.cs'
VALIDATOR = 'src/SpanDraft.Core/Validation/BeamModelValidator.cs'
SUPPORT = 'src/SpanDraft.Core/Supports/Support.cs'

MUTANTS = [
    ('S1', 'spring term of the shear row: sign reversed',
     [(SYSTEM, 'Put(4 * i, preparation.ScaledTranslationalStiffness(stations[i].Springs.Translational));',
       'Put(4 * i, -preparation.ScaledTranslationalStiffness(stations[i].Springs.Translational));')]),
    ('S2', 'spring term of the shear row omitted',
     [(SYSTEM, 'if (q == 3 && stations[i].ElasticW)', 'if (q == 3 && stations[i].ElasticW && false)')]),
    ('S3', 'spring term placed in the rotation column',
     [(SYSTEM, 'Put(4 * i, preparation.ScaledTranslationalStiffness(', 'Put(4 * i + 1, preparation.ScaledTranslationalStiffness(')]),
    ('S4', 'stiffness exponent 3 e_L + e_EI instead of 3 e_L - e_EI',
     [(PREP, 'exponent = checked(3 * lengthExponent - stiffnessExponent);', 'exponent = checked(3 * lengthExponent + stiffnessExponent);')]),
    ('S5', 'stiffness exponent 2 e_L - e_EI',
     [(PREP, 'exponent = checked(3 * lengthExponent - stiffnessExponent);', 'exponent = checked(2 * lengthExponent - stiffnessExponent);')]),
    ('S6', 'scaled stiffness not required to be normal',
     [(PREP, '        scaled.RequireNormal();\n        return scaled;', '        return scaled;')]),
    ('S7', 'spring input not range-checked',
     [(PREP, '            CheckInput(elastic.TranslationalStiffness);\n', '')]),
    ('R1', 'reaction choice reversed: the larger bound is delivered',
     [(REACT, 'return law.Bound < jump.Bound ? law : jump;', 'return law.Bound > jump.Bound ? law : jump;')]),
    ('R2', 'reaction choice: the law wins a tie',
     [(REACT, 'return law.Bound < jump.Bound ? law : jump;', 'return law.Bound <= jump.Bound ? law : jump;')]),
    ('R3', 'reaction choice: always the jump',
     [(REACT, 'return law.Bound < jump.Bound ? law : jump;', 'return jump;')]),
    ('R4', 'reaction choice: always the spring law',
     [(REACT, 'return law.Bound < jump.Bound ? law : jump;', 'return law;')]),
    ('R5', 'identity check removed',
     [(REACT, 'if (!(difference <= Directed.Add(jump.Bound, law.Bound, upper: true)))', 'if (false)')]),
    ('R6', 'identity check against the jump bound only',
     [(REACT, 'if (!(difference <= Directed.Add(jump.Bound, law.Bound, upper: true)))', 'if (!(difference <= jump.Bound))')]),
    ('R7', 'identity check against the law bound only',
     [(REACT, 'if (!(difference <= Directed.Add(jump.Bound, law.Bound, upper: true)))', 'if (!(difference <= law.Bound))')]),
    ('R8', 'identity check with a rounded sum of the bounds',
     [(REACT, 'Directed.Add(jump.Bound, law.Bound, upper: true)', 'Directed.Add(jump.Bound, law.Bound, upper: false)')]),
    ('R9', 'spring law without the minus sign',
     [(REACT, 'Bounded law = -(new Bounded(stiffness.Value) * new Bounded(state.FullState[w], state.FullStateBound[w]));',
       'Bounded law = new Bounded(stiffness.Value) * new Bounded(state.FullState[w], state.FullStateBound[w]);')]),
    ('R10', 'spring law without the state bound',
     [(REACT, 'new Bounded(state.FullState[w], state.FullStateBound[w])', 'new Bounded(state.FullState[w])')]),
    ('R11', 'spring law reads the rotation instead of the deflection',
     [(REACT, 'int w = 4 * station;', 'int w = 4 * station + 1;')]),
    ('R12', 'spring law with the unscaled stiffness exponent',
     [(REACT, 'system.Stations[station].Springs.Translational, system.EL, system.EEI);', 'system.Stations[station].Springs.Translational, system.EL, 0);')]),
    ('R13', 'spring reactions not selected between jump and law',
     [(REACT, 'if (direction == RestrainedDirections.W && station.ElasticW)', 'if (false)')]),
    ('R14', 'no reaction reported for a spring',
     [(REACT, 'RestrainedDirections.W => station.ConstrainsW,', 'RestrainedDirections.W => station.RigidW,')]),
    ('K1', 'spring station not marked as a reaction jump',
     [(KERNEL, 'if (e.MechanicalStation?.ConstrainsW == true) jumps |= BendingJump.ReactionY;', 'if (e.MechanicalStation?.RigidW == true) jumps |= BendingJump.ReactionY;')]),
    ('T1', 'rank: a translational spring does not count',
     [(STAB, 'if (station.ConstrainsW)', 'if (station.RigidW)')]),
    ('T2', 'rank: a rotational spring does not count',
     [(STAB, 'if (station.ConstrainsTheta)', 'if (station.RigidTheta)')]),
    ('T3', 'spring support is not axially rigid',
     [(STATION, 'restraints[position] = restraints.GetValueOrDefault(position) | RestrainedDirections.U |',
       'restraints[position] = restraints.GetValueOrDefault(position) |')]),
    ('T4', 'spring coincident with a rigid support accepted by the kernel',
     [(STATION, 'if (!occupied.Add(position))', 'if (!occupied.Add(position) && false)')]),
    ('T5', 'spring stiffness lost at the mechanical station',
     [(STATION, 'restraints.Select(pair => new MechanicalStation(pair.Key, pair.Value, springs.GetValueOrDefault(pair.Key)))',
       'restraints.Select(pair => new MechanicalStation(pair.Key, pair.Value))')]),
    ('T6', 'rigid translation accepted together with a translational spring',
     [(STATION, 'if (elastic.RigidTranslation && (w != 0 || theta == 0))', 'if (elastic.RigidTranslation && theta == 0)')]),
    ('A1', 'adapter: translational stiffness dropped',
     [(ADAPTER, 'new(support.Position.Meters, support.TranslationalSpring?.NewtonsPerMeter ?? 0,', 'new(support.Position.Meters, 0,')]),
    ('A2', 'adapter: springs treated as rollers',
     [(ADAPTER, 'beam.Supports.Where(s => s.Type != SupportType.Spring).Select(AdaptRigid)', 'beam.Supports.Select(s => s.Type == SupportType.Spring ? new SpanDraft.Solver.Kernel.RigidRestraint(s.Position.Meters, SpanDraft.Solver.Kernel.RestrainedDirections.W) : AdaptRigid(s))')]),
    ('V1', 'validator: a spring after a rigid support is accepted',
     [(VALIDATOR, 'if (spring || springBefore)', 'if (spring && springBefore)')]),
    ('V2', 'validator: duplicate check also applies to springs',
     [(VALIDATOR, 'if (!spring && !seenSupports.Add(', 'if (!seenSupports.Add(')]),
    ('V3', 'validator: coincidence flag of a position forgotten',
     [(VALIDATOR, 'positions[support.Position.Meters] = springBefore || spring;', 'positions[support.Position.Meters] = spring;')]),
    ('C1', 'Core: rigid translation accepted together with a translational spring',
     [(SUPPORT, 'if (rigidTranslation && translational is not null)', 'if (false)')]),
    ('C2', 'Core: no spring at all accepted',
     [(SUPPORT, 'if (translational is null && rotational is null)', 'if (false)')]),
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
                originals.setdefault(path, open(full, encoding='utf-8').read())
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
