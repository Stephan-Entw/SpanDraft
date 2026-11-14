"""Fault injection for the moment-release logic of Core, the solver kernel, the adapter, the editor document and the format.

Each mutant patches one place in src/, rebuilds the product test project, runs the Product tests and records
the failing tests. A mutant is killed when at least one test fails. A first run executes only the test classes of the
feature and its direct neighbours; a mutant that survives it is run again against the complete Product suite.
The source is always restored.

Usage (repository root, after a successful Release build of tests/SpanDraft.Tests):
    python3 validation/investigations/moment-release-mutants/mutate.py [--only ID,ID] [--output file.json]
"""
import json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
K = 'src/SpanDraft.Solver/Kernel/'
SYSTEM, FIELDS, STATION, STABILITY, KERNEL = (K + 'BendingSystem.cs', K + 'BendingFields.cs', K + 'StationModel.cs',
                                              K + 'MechanicalStability.cs', K + 'SolverKernel.cs')
ADAPTER, SOLVER, SOLUTION = ('src/SpanDraft.Solver/BeamModelAdapter.cs', 'src/SpanDraft.Solver/EulerBernoulliBeamSolver.cs',
                             'src/SpanDraft.Solver/BeamSolution.cs')
VALIDATOR = 'src/SpanDraft.Core/Validation/BeamModelValidator.cs'
MODEL = 'src/SpanDraft.Core/Beams/BeamModel.cs'
DOCUMENT = 'src/SpanDraft.Desktop/State/EditorDocument.cs'
COMPARER = 'src/SpanDraft.Desktop/State/BeamModelMechanicalComparer.cs'
PROJECT = 'src/SpanDraft.Desktop/State/ProjectState.cs'
CONSTRAINTS = 'src/SpanDraft.Desktop/State/BeamLengthConstraints.cs'
NAMING = 'src/SpanDraft.Desktop/State/EntityNaming.cs'
RULES = 'src/SpanDraft.Desktop/State/MomentReleaseRules.cs'
CODEC = 'src/SpanDraft.Desktop/Persistence/ProjectFileCodec.cs'
OVERVIEW = 'src/SpanDraft.Desktop/State/ProjectOverviewState.cs'
EDITOR = 'src/SpanDraft.Desktop/ViewModels/EditorViewModel.cs'
SUPPORT_DRAFT = 'src/SpanDraft.Desktop/ViewModels/SupportDraftViewModel.cs'
LOAD_DRAFT = 'src/SpanDraft.Desktop/ViewModels/PointLoadDraftViewModel.cs'

MUTANTS = [
    # Core validator
    ('V1', 'validator: a release at the beam start is accepted', [(VALIDATOR, 'if (!(x > 0 && x < beam.Length.Meters))', 'if (!(x >= 0 && x < beam.Length.Meters))')]),
    ('V2', 'validator: a release at the beam end is accepted', [(VALIDATOR, 'if (!(x > 0 && x < beam.Length.Meters))', 'if (!(x > 0 && x <= beam.Length.Meters))')]),
    ('V3', 'validator: duplicate releases are accepted', [(VALIDATOR, 'if (!seen.Add(x))', 'if (false)')]),
    ('V4', 'validator: a release on a fixed support is accepted', [(VALIDATOR, 's.Type == SupportType.Fixed && s.Position.Meters == x', 'false')]),
    ('V5', 'validator: a release on a rotational spring is accepted', [(VALIDATOR, 's.RotationalSpring is not null && s.Position.Meters == x', 'false')]),
    ('V6', 'validator: a release on a point moment is accepted', [(VALIDATOR, 'l is PointMoment moment && moment.Position.Meters == x', 'false')]),
    ('V7', 'validator: a release on a point force is rejected as a point moment', [(VALIDATOR, 'l is PointMoment moment && moment.Position.Meters == x', 'l is PointForce moment && moment.Position.Meters == x')]),
    ('V8', 'validator: only the first spring counts for the rotational spring rule', [(VALIDATOR, 'beam.Supports.Any(s => s.RotationalSpring is not null && s.Position.Meters == x)', 'beam.Supports.Take(1).Any(s => s.RotationalSpring is not null && s.Position.Meters == x)')]),
    ('M1', 'model: releases are dropped by the segment constructor', [(MODEL, 'MomentReleases = Array.AsReadOnly(releaseArray);', 'MomentReleases = Array.AsReadOnly(Array.Empty<MomentRelease>());')]),
    # Station model
    ('S1', 'station: a release outside the interior is accepted', [(STATION, 'if (!(position > 0 && position < input.Length))', 'if (false)')]),
    ('S2', 'station: duplicate releases are accepted', [(STATION, 'if (!releases.Add(position))', 'if (false)')]),
    ('S3', 'station: a release on an applied moment is accepted', [(STATION, 'if (moments.Contains(position))', 'if (false)')]),
    ('S4', 'station: a release on a rigid rotation is accepted', [(STATION, 'restraints[position].HasFlag(RestrainedDirections.Theta) ||', 'false ||')]),
    ('S5', 'station: a release on a rotational spring is accepted', [(STATION, 'springs.GetValueOrDefault(position).Rotational != 0)', 'false)')]),
    ('S6', 'station: a release is no mechanical station of its own', [(STATION, 'restraints.TryAdd(position, RestrainedDirections.None);', '')]),
    ('S7', 'station: mechanical stations never carry the release flag', [(STATION, 'releases.Contains(pair.Key)))', 'false))')]),
    ('S8', 'station: event stations never carry the release flag', [(STATION, 'releases.Contains(position))\n                    : null', 'false)\n                    : null')]),
    # Mechanism rank
    ('R1', 'rank: the continuity row of a release is missing', [(STABILITY, '            if (station.Release)\n            {', '            if (false)\n            {')]),
    ('R2', 'rank: a support at a release constrains only the left body', [(STABILITY, 'if (station.Position < cuts[body] || station.Position > cuts[body + 1])', 'if (station.Position < cuts[body] || station.Position >= cuts[body + 1])')]),
    ('R3', 'rank: a support at a release constrains only the right body', [(STABILITY, 'if (station.Position < cuts[body] || station.Position > cuts[body + 1])', 'if (station.Position <= cuts[body] || station.Position > cuts[body + 1])')]),
    ('R4', 'rank: the bending modes stay two whatever the number of bodies', [(STABILITY, 'return new(ExactRank(rows, columns), axial, columns);', 'return new(ExactRank(rows, columns), axial, 2);')]),
    ('R5', 'rank: the continuity row compares the wrong bodies', [(STABILITY, 'int body = Array.IndexOf(cuts, station.Position, 1) - 1;', 'int body = Math.Max(0, Array.IndexOf(cuts, station.Position, 1) - 2);')]),
    ('R6', 'rank: a rotational restraint is ignored for the rank', [(STABILITY, 'if (station.ConstrainsTheta)\n                    rows.Add', 'if (false)\n                    rows.Add')]),
    ('R7', 'rank: a translational restraint is ignored for the rank', [(STABILITY, 'if (station.ConstrainsW)\n                    rows.Add', 'if (false)\n                    rows.Add')]),
    ('R8', 'rank: the release changes the axial mode', [(STABILITY, 'axial |= station.Directions.HasFlag(RestrainedDirections.U);', 'axial |= station.Directions.HasFlag(RestrainedDirections.U) && !station.Release;')]),
    ('R9', 'rank: the continuity row has the wrong sign (equivalent: a column sign flip per body absorbs it, the bodies form a chain)', [(STABILITY, '(2 * body + 2, -Binary64.ExactUnits(1)), (2 * body + 3, -Binary64.ExactUnits(station.Position))', '(2 * body + 2, Binary64.ExactUnits(1)), (2 * body + 3, Binary64.ExactUnits(station.Position))')]),
    # System and fields
    ('B1', 'system: the moment right of a release stays an unknown', [(SYSTEM, '                    q == 2 && stations[i].Release ||\n', '')]),
    ('B2', 'system: the rotation continuity row stays at a release', [(SYSTEM, 'if (!stations[i].Release)\n                    Jump(1, default);', 'Jump(1, default);')]),
    ('F1', 'fields: the left moment of a release is the computed value, not zero', [(FIELDS, 'values.Add(0);\n                bounds.Add(field.Error);\n                continue;', 'values.Add(field.Value);\n                bounds.Add(field.Error);\n                continue;')]),
    ('F2', 'fields: the rotation is shared across a release', [(FIELDS, 'bool stationKinematics = q < 2 && station >= 0 && !(release && q == 1);', 'bool stationKinematics = q < 2 && station >= 0;')]),
    ('F3', 'fields: the self-test of the exact zero is dropped', [(FIELDS, 'if (!(Math.Abs(field.Value) <= field.Error))', 'if (false)')]),
    ('F4', 'fields: the self-test of the exact zero accepts any value', [(FIELDS, 'if (!(Math.Abs(field.Value) <= field.Error))', 'if (Math.Abs(field.Value) <= field.Error)')]),
    ('F5', 'fields: the exact zero carries no error bound', [(FIELDS, 'values.Add(0);\n                bounds.Add(field.Error);', 'values.Add(0);\n                bounds.Add(0);')]),
    ('K1', 'kernel: a release station reports no release jump flag', [(KERNEL, 'if (e.MechanicalStation?.Release == true) jumps |= BendingJump.Release;', '')]),
    # Adapter and facade
    ('A1', 'adapter: releases are not mapped', [(ADAPTER, 'lineLoads, elastic, AdaptReleases(beam));', 'lineLoads, elastic);')]),
    ('A2', 'adapter: the mechanism check of an unrepresentable stiffness ignores the releases', [(SOLVER, 'SolverKernel.RequireStable(rigid, elastic, BeamModelAdapter.AdaptReleases(beam), beam.Length.Meters);', 'SolverKernel.RequireStable(rigid, elastic, null, beam.Length.Meters);')]),
    ('A3', 'facade: no node carries the left rotation', [(SOLUTION, 'double? rotationLeft = station.Jumps.HasFlag(BendingJump.Release) ? station.Left?.Bending[1].Value : null;', 'double? rotationLeft = null;')]),
    ('A4', 'facade: the left rotation is the right one', [(SOLUTION, 'station.Left?.Bending[1].Value : null;', 'station.Right?.Bending[1].Value : null;')]),
    ('A5', 'facade: every node with a left limit carries a left rotation', [(SOLUTION, 'double? rotationLeft = station.Jumps.HasFlag(BendingJump.Release) ? station.Left?.Bending[1].Value : null;', 'double? rotationLeft = station.Left?.Bending[1].Value;')]),
    ('A6', 'facade: the mechanism message ignores the releases', [(SOLVER, 'beam.MomentReleases.Count == 0\n                ?', 'true\n                ?')]),
    # Editor document, comparer, constraints, naming, rules, codec
    ('D1', 'document: releases are no named entities', [(DOCUMENT, '.Concat(MomentReleases.Select(r => (r.Id, r.Name)));', ';')]),
    ('D2', 'document: releases do not reach the beam model', [(DOCUMENT, '.Concat(SelfWeightLoads), MomentReleases.Select(r => r.ToCore()));', '.Concat(SelfWeightLoads));')]),
    ('D3', 'comparer: releases are not compared', [(COMPARER, '        && left.MomentReleases.Count == right.MomentReleases.Count\n        && left.MomentReleases.Zip(right.MomentReleases).All(pair => pair.First.Position == pair.Second.Position);', ';')]),
    ('D4', 'content equality ignores the releases', [(PROJECT, 'a.MomentReleases.SequenceEqual(b.MomentReleases) && ', '')]),
    ('D5', 'constraints: a release at the new end is allowed', [(CONSTRAINTS, 'r.Position.Meters >= length.Meters).Select(r => r.Id)', 'r.Position.Meters > length.Meters).Select(r => r.Id)')]),
    ('D6', 'constraints: the length error ignores the releases', [(CONSTRAINTS, '            || document.MomentReleases.Any(r => r.Position.Meters >= length.Meters)) return Strings.LengthExcludesEntities;', '            ) return Strings.LengthExcludesEntities;')]),
    ('D7', 'document: a release name may repeat a support name (names are not shared)', [(DOCUMENT, '.Concat(DistributedLoads.Select(l => (l.Id, l.Name))).Concat(MomentReleases', '.Concat(DistributedLoads.Select(l => (l.Id, l.Name))).Concat(Array.Empty<EditorMomentRelease>()')]),
    ('N1', 'naming: the release counter is not consumed', [(NAMING, 'AutoNameKind.MomentRelease => With(release: next),', 'AutoNameKind.MomentRelease => this,')]),
    ('N2', 'naming: the release name is formatted like a force', [(NAMING, 'AutoNameKind.MomentRelease => "G" + ordinal.ToString(CultureInfo.InvariantCulture),', 'AutoNameKind.MomentRelease => "F" + ordinal.ToString(CultureInfo.InvariantCulture),')]),
    ('N3', 'naming: the release counter may move backwards', [(NAMING, '        && NextMomentReleaseNumber >= other.NextMomentReleaseNumber;', ';')]),
    ('E1', 'rules: a duplicate release is no conflict', [(RULES, 'if (document.MomentReleases.Any(r => r.Id != exclude && r.Position.Meters == x)) return MomentReleaseConflict.Duplicate;', '')]),
    ('E2', 'rules: a fixed support is no conflict', [(RULES, 'if (document.Supports.Any(s => s.Position.Meters == x && s.Type == SupportType.Fixed)) return MomentReleaseConflict.FixedSupport;', '')]),
    ('E3', 'rules: a rotational spring is no conflict', [(RULES, 'if (document.Supports.Any(s => s.Position.Meters == x && s.Spring?.Rotational is not null)) return MomentReleaseConflict.RotationalSpring;', '')]),
    ('E4', 'rules: a point moment is no conflict', [(RULES, 'if (document.Loads.Any(l => l.Kind == PointLoadKind.Moment && l.Position.Meters == x)) return MomentReleaseConflict.PointMoment;', '')]),
    ('E5', 'rules: the edited release conflicts with itself', [(RULES, 'r.Id != exclude && r.Position.Meters == x', 'r.Position.Meters == x')]),
    ('E6', 'rules: a point force counts as a point moment', [(RULES, 'l.Kind == PointLoadKind.Moment && l.Position.Meters == x', 'l.Position.Meters == x')]),
    ('E7', 'rules: a support at a release is never refused', [(RULES, '(type == SupportType.Fixed || spring?.Rotational is not null)\n        &&', 'false\n        &&')]),
    ('E8', 'rules: a pinned support at a release is refused too', [(RULES, '(type == SupportType.Fixed || spring?.Rotational is not null)', '(type != SupportType.Spring || spring?.Rotational is not null)')]),
    ('E9', 'editor: the support draft ignores the release rule', [(SUPPORT_DRAFT, 'else if (MomentReleaseRules.SupportConflicts(', 'else if (false && MomentReleaseRules.SupportConflicts(')]),
    ('E10', 'editor: the moment draft ignores the release rule', [(LOAD_DRAFT, 'Kind == PointLoadKind.Moment && MomentReleaseRules.MomentConflicts(document, position)', 'false')]),
    ('E11', 'editor: a support placement ignores the release rule', [(EDITOR, 'CanPosition(position, exclude) && !MomentReleaseRules.SupportConflicts(Document, position, type, spring);', 'CanPosition(position, exclude);')]),
    ('P1', 'format: releases are not written', [(CODEC, 'MomentReleases = d.MomentReleases.Count == 0 ? null :', 'MomentReleases = true ? null :')]),
    ('P2', 'format: releases are not read', [(CODEC, '(d.MomentReleases ?? []).Select(entry =>', 'Array.Empty<MomentReleaseV5>().Select(entry =>')]),
    ('P3', 'format: the release counter is not written', [(CODEC, 'NextMomentReleaseNumber = n.NextMomentReleaseNumber > 1 ? n.NextMomentReleaseNumber : null', 'NextMomentReleaseNumber = null')]),
    ('P4', 'format: the release counter is not read', [(CODEC, 'n.NextMomentReleaseNumber ?? 1)', '1)')]),
    ('P5', 'format: the release counter is written even at its start value', [(CODEC, 'n.NextMomentReleaseNumber > 1 ? n.NextMomentReleaseNumber : null', 'n.NextMomentReleaseNumber')]),
    ('P6', 'format: a release id is not registered as an entity', [(CODEC, 'return new EditorMomentRelease(EntityId(x.Id), L(x.Position, "position"), Name(x.Name));', 'return new EditorMomentRelease(x.Id, L(x.Position, "position"), Name(x.Name));')]),
    ('O1', 'overview: the left rotation shows the right one', [(OVERVIEW, 'Reaction(node.RotationLeftRadians ?? left.RotationRadians, QuantityKind.Rotation),', 'Reaction(node.RotationRadians, QuantityKind.Rotation),')]),
    ('O2', 'overview: the shear cell shows the left value only', [(OVERVIEW, 'shearLeft == shearRight ? shearLeft : shearLeft + " / " + shearRight);', 'shearLeft);')]),
]

SUBSET = ['*MomentRelease*', '*SolverMechanicalStabilityTests*', '*SolverMechanicalInputTests*', '*SolverKernelTests*',
          '*SolverBendingSystemTests*', '*ProjectFile*', '*DesktopMechanicalComparerTests*', '*DesktopNamingAndPresentationTests*',
          '*BeamModelTests*', '*DesktopSegmentDocumentTests*']


def run(command):
    return subprocess.run(command, cwd=ROOT, capture_output=True, text=True)


def failures(output):
    failed = re.findall(r'^(?:fehlerhaft|failed) (.+?)(?: \([\d,.]+ ?m?s\))?$', output, re.M)
    return sorted({re.sub(r'\(.*', '', f).split('.')[-2] + '.' + re.sub(r'\(.*', '', f).split('.')[-1] for f in failed}), len(failed)


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
            command = ['dotnet', 'test', '--project', 'tests/SpanDraft.Tests', '-c', 'Release', '--no-build']
            test = run(command + [a for c in SUBSET for a in ('--filter-class', c)])
            scope = 'subset'
            if test.returncode == 0:
                test = run(command)
                scope = 'full'
            names, count = failures(test.stdout)
            status = 'killed' if test.returncode != 0 else 'SURVIVED'
            results.append({'id': ident, 'description': description, 'status': status, 'scope': scope,
                            'failedTests': count, 'tests': names[:6]})
            print(ident, status, scope, count, names[:3], flush=True)
        finally:
            for path, text in originals.items():
                open(os.path.join(ROOT, path), 'w', encoding='utf-8').write(text)
    if output:
        json.dump(results, open(output, 'w'), indent=1)
    run(['dotnet', 'build', 'tests/SpanDraft.Tests', '-c', 'Release', '--no-restore'])
    return 0 if all(r['status'] == 'killed' for r in results) else 1


if __name__ == '__main__':
    sys.exit(main())
