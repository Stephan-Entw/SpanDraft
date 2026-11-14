"""Fault injection for the multi-segment logic of Core, the solver kernel, the engineering assessment and the editor document.

Each mutant patches one place in src/, rebuilds the product test project, runs the Product tests and records
the failing tests. A mutant is killed when at least one test fails. The source is always restored.

Usage (repository root, after a successful Release build of tests/SpanDraft.Tests):
    python3 validation/investigations/multi-segment-mutants/mutate.py [--only ID,ID] [--output file.json]
"""
import json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
K = 'src/SpanDraft.Solver/Kernel/'
SYSTEM, FIELDS, PREP, STATION, KERNEL, QUALITY, RESULT, STATE = (K + 'BendingSystem.cs', K + 'BendingFields.cs', K + 'BendingPreparation.cs',
    K + 'StationModel.cs', K + 'SolverKernel.cs', K + 'BendingQuality.cs', K + 'BendingResult.cs', K + 'BendingStateSolver.cs')
ADAPTER = 'src/SpanDraft.Solver/BeamModelAdapter.cs'
ENGINEERING = 'src/SpanDraft.Engineering/BeamEngineeringAnalysis.cs'
VALIDATOR = 'src/SpanDraft.Core/Validation/BeamModelValidator.cs'
LOAD = 'src/SpanDraft.Core/Loads/DistributedLoad.cs'
DOCUMENT = 'src/SpanDraft.Desktop/State/EditorDocument.cs'
COMPARER = 'src/SpanDraft.Desktop/State/BeamModelMechanicalComparer.cs'
PROJECT = 'src/SpanDraft.Desktop/State/ProjectState.cs'
CODEC = 'src/SpanDraft.Desktop/Persistence/ProjectFileCodec.cs'

MUTANTS = [
    ('E1', 'transfer coefficients of an interval use the stiffness of the previous interval',
     [(SYSTEM, 'stations[i - 1].Position, stations[i].Position, preparation, i - 1);', 'stations[i - 1].Position, stations[i].Position, preparation, Math.Max(0, i - 2));')]),
    ('E2', 'load integrals are divided by the stiffness of the first interval',
     [(SYSTEM, 'transferred[q] /= new Bounded(preparation.IntervalEI[i - 1].Value, preparation.IntervalEI[i - 1].Bound());', 'transferred[q] /= new Bounded(preparation.IntervalEI[0].Value, preparation.IntervalEI[0].Bound());')]),
    ('E3', 'field evaluation uses the stiffness of the first interval',
     [(FIELDS, 'var e = new Bounded(IntervalEI[j].Value, IntervalEI[j].Bound());', 'var e = new Bounded(IntervalEI[0].Value, IntervalEI[0].Bound());')]),
    ('E4', 'field evaluation uses the stiffness of the next interval',
     [(FIELDS, 'var e = new Bounded(IntervalEI[j].Value, IntervalEI[j].Bound());', 'var e = new Bounded(IntervalEI[Math.Min(j + 1, IntervalEI.Length - 1)].Value, IntervalEI[Math.Min(j + 1, IntervalEI.Length - 1)].Bound());')]),
    ('E5', 'piece polynomials use the stiffness of the first interval',
     [(RESULT, 'double ei = fields.IntervalEI[fields.SegmentOf(a)].Value;', 'double ei = fields.IntervalEI[0].Value;')]),
    ('E6', 'piece quality uses the stiffness of the first interval',
     [(QUALITY, 'RoundCounted own = fields.IntervalEI[fields.SegmentOf(x0)];', 'RoundCounted own = fields.IntervalEI[0];')]),
    ('E7', 'upper stiffness reference scale from the first interval only',
     [(QUALITY, 'double eiHigh = fields.IntervalEI.Max(e => Add(e.Value, e.Bound(), true));', 'double eiHigh = Add(fields.IntervalEI[0].Value, fields.IntervalEI[0].Bound(), true);')]),
    ('E8', 'lower stiffness reference scale from the first interval only',
     [(QUALITY, 'double eiLow = fields.IntervalEI.Min(e => Subtract(e.Value, e.Bound(), false));', 'double eiLow = Subtract(fields.IntervalEI[0].Value, fields.IntervalEI[0].Bound(), false);')]),
    ('E9', 'verification weights from the greatest instead of the least stiffness',
     [(STATE, 'double least = system.LeastEI.Value;', 'double least = system.IntervalEI.Max(e => e.Value);')]),
    ('E10', 'normalization follows the softest instead of the stiffest section',
     [(PREP, 'int eEI = PowerOfTwoExponent(input.Sections.Max(section => section.EI));', 'int eEI = PowerOfTwoExponent(input.Sections.Min(section => section.EI));')]),
    ('S1', 'an interval starting on a boundary is attributed to the section on its left',
     [(STATION, 'while (input.Sections[section].End <= stations[i].Position)', 'while (input.Sections[section].End < stations[i].Position)')]),
    ('S2', 'boundaries are not mechanical stations',
     [(STATION, 'restraints[StationPosition(section.Start)] = RestrainedDirections.None;', '_ = section;')]),
    ('S3', 'the tiling of the sections is not checked',
     [(STATION, '        RequireTiling(input);\n', '')]),
    ('S4', 'a boundary counts as an occupied position, so a spring cannot stand on it',
     [(STATION, 'var occupied = new HashSet<double>();', 'var occupied = new HashSet<double>(input.Sections.Skip(1).Select(s => StationPosition(s.Start)));')]),
    ('S5', 'a section of zero or negative length is accepted',
     [(STATION, '|| !(section.End > section.Start))', '|| false)')]),
    ('S6', 'the straddle check of an interval is dropped (defensive check; expected equivalent)',
     [(STATION, 'if (input.Sections[section].Start > stations[i].Position || input.Sections[section].End < stations[i + 1].Position)', 'if (false)')]),
    ('X1', 'section extrema are found on all pieces from the section start on',
     [(KERNEL, 'pieces.Where(piece => piece.Start >= section.Start && piece.End <= section.End).ToImmutableArray()', 'pieces.Where(piece => piece.Start >= section.Start).ToImmutableArray()')]),
    ('X2', 'every section reports the global extrema',
     [(KERNEL, 'pieces.Where(piece => piece.Start >= section.Start && piece.End <= section.End).ToImmutableArray()', 'pieces')]),
    ('A1', 'adapter: the stiffness of the first segment for every section',
     [(ADAPTER, 'Binary64.StiffnessProduct(segment.Material.YoungsModulus.Pascals,\n                segment.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth)',
       'Binary64.StiffnessProduct(beam.Segments[0].Material.YoungsModulus.Pascals,\n                beam.Segments[0].BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth)')]),
    ('A2', 'adapter: the modulus of the first segment only',
     [(ADAPTER, 'Binary64.StiffnessProduct(segment.Material.YoungsModulus.Pascals,', 'Binary64.StiffnessProduct(beam.Segments[0].Material.YoungsModulus.Pascals,')]),
    ('G1', 'engineering: the segment with the largest safety factor governs',
     [(ENGINEERING, 'return !tied && a < b;', 'return !tied && a > b;')]),
    ('G3', 'engineering: every segment uses the strength of the first one',
     [(ENGINEERING, 'segment.Material.YieldStrength.Pascals / stress.Pascals', 'solution.Beam.Segments[0].Material.YieldStrength.Pascals / stress.Pascals')]),
    ('G4', 'engineering: every segment uses the section modulus of the first one',
     [(ENGINEERING, 'var axis = segment.BendingAxisProperties;', 'var axis = solution.Beam.Segments[0].BendingAxisProperties;')]),
    ('G5', 'engineering: the segment moment is the global one',
     [(ENGINEERING, 'BeamExtrema own = solution.SegmentExtrema[i];', 'BeamExtrema own = solution.Extrema;')]),
    ('V1', 'validator: gaps and overlaps between segments are accepted',
     [(VALIDATOR, 'if (segment.Start.Meters != expectedStart)', 'if (false)')]),
    ('V2', 'validator: a last segment that ends elsewhere than the beam is accepted',
     [(VALIDATOR, 'if (expectedStart != beam.Length.Meters)', 'if (false)')]),
    ('C1', 'self-weight of a segment starts at the beam start',
     [(LOAD, 'return new(start, end, ForcePerLength.FromNewtonsPerMeter(', 'return new(default, end, ForcePerLength.FromNewtonsPerMeter(')]),
    ('D1', 'document: the end of a segment is its own start boundary',
     [(DOCUMENT, 'index == _segments.Length - 1 ? Length : _boundaries[index]);', 'index == _segments.Length - 1 ? Length : _boundaries[Math.Max(0, index - 1)]);')]),
    ('D2', 'document: boundaries need not ascend or lie inside the beam',
     [(DOCUMENT, 'if (!(boundary.Meters > previous) || !(boundary.Meters < length.Meters))', 'if (false)')]),
    ('D3', 'document: self-weight needs the density of the first segment only',
     [(DOCUMENT, 'int missing = Array.FindIndex(_segments, segment => segment.Material.Density is null);', 'int missing = _segments[0].Material.Density is null ? 0 : -1;')]),
    ('D4', 'document: a boundary belongs to the segment that ends there',
     [(DOCUMENT, 'while (index < _boundaries.Length && _boundaries[index].Meters <= position.Meters) index++;', 'while (index < _boundaries.Length && _boundaries[index].Meters < position.Meters) index++;')]),
    ('D5', 'document: splitting does not copy the segment',
     [(DOCUMENT, '_segments.Take(index + 1).Concat(_segments.Skip(index)));', '_segments.Take(index + 1).Concat(_segments.Skip(index + 1)).Append(_segments[^1]));')]),
    ('D6', 'comparer: the segment count is not compared',
     [(COMPARER, '        && left.Segments.Count == right.Segments.Count\n', '')]),
    ('D7', 'comparer: segment ranges are not compared',
     [(COMPARER, '        left.Start == right.Start && left.End == right.End\n        && ', '        ')]),
    ('D8', 'content equality ignores the boundaries',
     [(PROJECT, 'a.SegmentBoundaries.SequenceEqual(b.SegmentBoundaries) && ', '')]),
    ('P1', 'format: a further segment is written with the first boundary',
     [(CODEC, 'Start = d.SegmentBoundaries[i].Meters,', 'Start = d.SegmentBoundaries[0].Meters,')]),
    ('P2', 'format: further segments are not read',
     [(CODEC, 'foreach (var entry in d.FurtherSegments ?? [])', 'foreach (var entry in Array.Empty<SegmentV5>())')]),
    ('P3', 'format: strength confirmation is checked for the first segment only',
     [(CODEC, 'd.Segments.Any(segment => segment.Material.StrengthType is null))', 'd.Segments[0].Material.StrengthType is null)')]),
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
