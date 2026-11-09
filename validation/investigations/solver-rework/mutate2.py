# Fault injection for the new solver path: same fault classes as mutate.py, plus faults that only the new code can have.
import json, os, re, subprocess, sys
S = os.path.dirname(os.path.abspath(__file__))
B = S + '/base2'
SV = 'src/SpanDraft.Solver/'
SS = SV + 'SupportSpanSolver.cs'
LO = SV + 'SolverLoads.cs'
FI = SV + 'ElementField.cs'
SM = SV + 'SolverModel.cs'
SO = SV + 'EulerBernoulliBeamSolver.cs'
BS = SV + 'BeamSolution.cs'
EX = SV + 'BeamExtrema.cs'
RT = SV + 'PolynomialRoots.cs'
I = 'beam.BendingAxisProperties.SecondMomentOfArea.MetersToTheFourth'
UL = 'src/SpanDraft.Core/Loads/UniformDistributedLoad.cs'
# FI has an old and a new constructor; both are patched so that the new path is really affected.
M = [
    ('A1', 'Steifigkeit 6EI/L2 in V +1 %', [(SS, '6 * ei * sum / l / l', '6.06 * ei * sum / l / l')]),
    ('A2', 'Steifigkeit 4EI/L +1 %', [(SS, 'matrix[k, k] += NumericalGuard.Positive(4 * (ei / l), "4EI/L");', 'matrix[k, k] += NumericalGuard.Positive(4.04 * (ei / l), "4EI/L");')]),
    ('A3', 'Steifigkeit 2EI/L +1 %', [(SS, 'matrix[k, next] += 2 * (ei / l)', 'matrix[k, next] += 2.02 * (ei / l)')]),
    ('A4', 'EI ueberall +1 %', [(SS, I, '1.01 * ' + I), (FI, I, '1.01 * ' + I)]),
    ('A5', 'EI nur im System +1 %', [(SS, I, '1.01 * ' + I)]),
    ('A6', 'EI nur in Feldauswertung +1 %', [(FI, I, '1.01 * ' + I)]),
    ('B1', 'Streckenlast in Querkraft +1 %', [(SS, 'q.Intensity * Difference(x, q.Start, q.End, 1)', '1.01 * q.Intensity * Difference(x, q.Start, q.End, 1)')]),
    ('B2', 'Streckenlast in Neigung 1/5 statt 1/6', [(SS, 'Difference(x, q.Start, q.End, 3) / 6', 'Difference(x, q.Start, q.End, 3) / 5')]),
    ('B3', 'Punktmoment im Feld Vorzeichen falsch', [(SS, '- couples.Where(c => c.X <= x).Sum(c => c.Value)', '+ couples.Where(c => c.X <= x).Sum(c => c.Value)')]),
    ('B4', 'Punktkraft +1 %', [(LO, 'new(force.Position.Meters, force.Force.Newtons)', 'new(force.Position.Meters, 1.01 * force.Force.Newtons)')]),
    ('B5', 'Streckenlast wirkt bis Traegerende', [(SS, 'Math.Min(q.End, end), q.Intensity', 'end, q.Intensity'), (SS, 'loads.Uniform.Where(l => l.Start <= start && l.End >= end)', 'loads.Uniform.Where(l => l.Start <= start)')]),
    ('B6', 'Ueberlappende Streckenlasten: Feld nimmt nur letzte', [(SS, 'NumericalGuard.Finite(intensity, "Resultant UDL")', 'NumericalGuard.Finite(loads.Uniform.Where(l => l.Start <= start && l.End >= end).Select(l => l.Intensity).LastOrDefault(), "Resultant UDL")')]),
    ('B7', 'Streckenlast nach oben wird nach unten gerechnet', [(LO, 'distributed.Intensity.NewtonsPerMeter', '-Math.Abs(distributed.Intensity.NewtonsPerMeter)')]),
    ('C1', 'Feldanteil Streckenlast (Faktor 24) +1 %', [(FI, 'c4 = q / ei * length * length * length * length / 24;', 'c4 = q / ei * length * length * length * length / 23.76;')]),
    ('C2', 'Biegemoment Vorzeichen falsch', [(FI, 'double moment = m + v * s + q * s * s / 2;', 'double moment = -(m + v * s + q * s * s / 2);')]),
    ('C3', 'Querkraft +1 %', [(FI, 'double shear = v + q * s;', 'double shear = 1.01 * (v + q * s);')]),
    ('C4', 'Links/Rechts am Knoten vertauscht', [(BS, 'side == EvaluationSide.Left ? middle - 1 : middle', 'side == EvaluationSide.Left ? middle : middle - 1')]),
    ('C5', 'Rotation zwischen Knoten +1 %', [(FI, '* t + c1) / length :', '* t + c1) / length * 1.01 :')]),
    ('C6', 'Polynomkoeffizient c2 falsch', [(FI, 'c2 = m / ei * length * length / 2;', 'c2 = m / ei * length * length / 1.99;')]),
    ('C7', 'Durchbiegung zwischen Knoten +1 %', [(FI, 't + c1) * t + c0;', 't + c1) * t * 1.01 + c0;')]),
    ('D1', 'Auflagerreaktion ohne Knotenlasten', [(SS, 'return (shearRight[i] - shearLeft - forces.Where(p => p.X == x).Sum(p => p.Value),', 'return (shearRight[i] - shearLeft,')]),
    ('E1', 'Momentenextrem im Feld wird nicht gesucht', [(EX, 'foreach (double t in field.StationaryMoments())', 'foreach (double t in Array.Empty<double>())')]),
    ('E2', 'Durchbiegungsextrem im Feld wird nicht gesucht', [(EX, 'foreach (double t in field.StationaryDisplacements())', 'foreach (double t in Array.Empty<double>())')]),
    ('E3', 'Ort des Momentenextrems falsch berechnet', [(FI, 'InUnitInterval(v, q * length)', 'InUnitInterval(v, .5 * q * length)')]),
    ('F1', 'Quadratische Gleichung: zweite Wurzel fehlt', [(RT, '        Add(roots, c / numerator);\n', '')]),
    ('F2', 'Nullstellensuche bricht nach 3 Schritten ab', [(RT, 'iteration < 128', 'iteration < 3')]),
    ('H1', 'Biegeachse: immer erste Achse des Querschnitts', [(SS, I, 'beam.Section.Axes[0].SecondMomentOfArea.MetersToTheFourth'), (FI, I, 'beam.Section.Axes[0].SecondMomentOfArea.MetersToTheFourth')]),
    ('H2', 'Eigengewicht: Erdbeschleunigung 10', [(UL, '* 9.80665', '* 10.0')]),
    ('H3', 'Eigengewicht wird nach oben angesetzt', [(UL, '-density.KilogramsPerCubicMeter', 'density.KilogramsPerCubicMeter')]),
    ('N1', 'E fest auf 210 GPa verdrahtet', [(SS, 'beam.Material.YoungsModulus.Pascals', '210e9'), (FI, 'beam.Material.YoungsModulus.Pascals', '210e9')]),
    ('N2', 'I fest auf 8e-6 m4 verdrahtet', [(SS, I, '8e-6'), (FI, I, '8e-6')]),
    ('N3', 'Durchbiegung auf 1 Mikrometer gerundet', [(SO, 'Displacement.FromMeters(result.Displacements[i])', 'Displacement.FromMeters(Math.Round(result.Displacements[i], 6))')]),
    ('N4', 'Lasten ueber 1 kN werden gekappt', [(LO, 'new(force.Position.Meters, force.Force.Newtons)', 'new(force.Position.Meters, Math.Max(force.Force.Newtons, -1000))')]),
    ('X1', 'Punktmoment direkt am Lager wird ignoriert', [(SS, 'double right = moments.Where(c => c.X == nodes[i]).Sum(c => c.Value);', 'double right = 0;')]),
    ('X2', 'Einspannung im Feld wirkt wie gelenkiges Lager', [(SS, 'clamped = nodes.Select(x => supports.Any(s => s.X == x && s.Clamped)).ToArray();', 'clamped = nodes.Select(x => (x == start || x == end) && supports.Any(s => s.X == x && s.Clamped)).ToArray();')]),
    # Faults only the new path can have.
    ('Y1', 'Lastanteil 12P in V falsch', [(SS, '(12 * p / l - 6 * t) / l / l', '(12.12 * p / l - 6 * t) / l / l')]),
    ('Y2', 'Lastanteil 6P in M falsch', [(SS, '2 * t / l - 6 * p / l / l;\n', '2 * t / l - 6.06 * p / l / l;\n')]),
    ('Y3', 'Rechter Ueberhang fehlt im System', [(SS, 'else right += overhangMoment;', 'else right += 0;')]),
    ('Y4', 'Linker Ueberhang fehlt im System', [(SS, 'else if (leftOverhang is not null) right -= leftOverhang.Moment(nodes[0]);', '')]),
    ('Y5', 'Linker Ueberhang: Durchbiegung falsch verankert', [(SS, '(o.Deflection(x) - o.Deflection(nodes[0]) + c1 * (x - nodes[0])) / ei', '(o.Deflection(x) + c1 * (x - nodes[0])) / ei')]),
    ('Y6', 'Kurze Streckenlast: Differenzformel falsch', [(SS, '3 => d * (near * near + near * far + far * far),', '3 => d * (near * near + far * far),')]),
    ('Y7', 'Kraft genau am Lager geht in die Spanne', [(SS, 'spans[i] = new(this.forces.Where(p => p.X > a && p.X < b), this.moments', 'spans[i] = new(this.forces.Where(p => p.X >= a && p.X < b), this.moments')]),
    ('Y8', 'Moment am rechten Spannenende doppelt', [(SS, 'this.moments.Where(p => p.X > a && p.X < b),\n                    Clip', 'this.moments.Where(p => p.X > a && p.X <= b),\n                    Clip')]),
    ('Y9', 'Ueberhang-Moment Vorzeichen falsch', [(SS, '-overhangShear * (end - last) - rightOverhang.Moment(end)', 'overhangShear * (end - last) - rightOverhang.Moment(end)')]),
    ('Z1', 'Axial: Kraft zwischen Festpunkten falsch geteilt', [(SS, 'normalRight[i] = spans[i].Moment(b) / (b - a);', 'normalRight[i] = 1.01 * spans[i].Moment(b) / (b - a);')]),
    ('Z2', 'Axial: Loslager haelt fest', [(SS, 'beam.Supports.Where(s => s.Type is SupportType.Fixed or SupportType.Pinned)', 'beam.Supports.Where(s => s.Type is SupportType.Fixed or SupportType.Pinned or SupportType.Roller)')]),
    ('Z3', 'Axial: Reaktion ohne Knotenlast', [(SS, 'reaction = left - normalRight[i] - loads.AxialForces.Where(p => p.X == x).Sum(p => p.Value);', 'reaction = left - normalRight[i];')]),
    ('Z4', 'Axial: Verschiebung ohne Lastanteil', [(SS, '((normalRight[i] * (x - nodes[i]) - actions.Moment(x)) / ea,', '((normalRight[i] * (x - nodes[i])) / ea,')]),
    ('Z5', 'Axiale Streckenlast im Feld falsch', [(FI, 'n - nq * s', 'n + nq * s')]),
    ('G1', 'Gelenk: Querkraft rechts falsches Vorzeichen', [(SS, 'forces.Add(new(part.Start, -solution[part.ShearLeft]))', 'forces.Add(new(part.Start, solution[part.ShearLeft]))')]),
    ('G2', 'Gelenk: Starrkoerperdrehung um falschen Punkt', [(SS, '1 => solution[p.FirstRigid] * (x - p.Anchor),', '1 => solution[p.FirstRigid] * (x - p.Start),')]),
    ('G3', 'Gelenk: freies Ende nicht erkannt', [('src/SpanDraft.Solver/SolverModel.cs', 'if (freedom == 2) return false;', '')]),
    ('G4', 'Gelenk: Freiheitsgrad falsch weitergegeben', [('src/SpanDraft.Solver/SolverModel.cs', 'else freedom = freedom == 0 ? 1 : 2;', 'else freedom = 1;')]),
    ('G5', 'Gelenk: Rotation links wie rechts', [(SS, 'if (index < 0) return right;', 'return right;')]),
    ('G6', 'Gelenk auf Lager: Reaktion nur von einer Seite', [(SS, 'reactionY += y;', 'reactionY = y;')]),
    ('G7', 'Gelenk: Kraft genau am Gelenk geht verloren', [(SS, 'Forces = loads.Forces.Where(p => p.X > a && p.X <= b ||', 'Forces = loads.Forces.Where(p => p.X > a && p.X < b ||')]),
    ('G8', 'Gelenk: Momentenbedingung Vorzeichen falsch', [(SS, 'if (p.ShearRight >= 0) left[row, p.ShearRight] += unitR.Moment;', 'if (p.ShearRight >= 0) left[row, p.ShearRight] -= unitR.Moment;')]),
    ('G9', 'Gelenk fehlt als Station', [(LO, '.Concat(Hinges)', '')]),
    ('G10', 'Gelenk: Vertraeglichkeit ohne Nachbarteil', [(SS, 'AddDisplacement(row, i + 1, parts[i].End, -1);', '')]),
]


def sh(args):
    return subprocess.run(args, cwd=B, capture_output=True, text=True)


def build():
    r = sh(['dotnet', 'build', 'validation/dotnet/SpanDraft.Validation.Runner', '-c', 'Release', '-v', 'q', '--nologo'])
    if r.returncode:
        return r.stdout[-400:]
    r = sh(['dotnet', 'build', 'tests/SpanDraft.Tests', '-c', 'Release', '-v', 'q', '--nologo'])
    return r.stdout[-400:] if r.returncode else None


def validation(mid):
    rp = S + '/r2_' + mid + '.json'
    if os.path.exists(rp):
        os.remove(rp)
    sh(['dotnet', 'run', '--no-build', '--project', 'validation/dotnet/SpanDraft.Validation.Runner', '-c', 'Release', '--', 'compare', '--report', rp])
    if not os.path.exists(rp):
        return 'absturz'
    v = json.load(open(rp))['validated']
    os.remove(rp)
    return bool(v)


def product():
    r = sh(['dotnet', 'run', '--no-build', '-c', 'Release', '--project', 'tests/SpanDraft.Tests', '--', '-noColor'])
    m = re.findall(r'Total:\s*(\d+),\s*Errors:\s*(\d+),\s*Failed:\s*(\d+)', r.stdout + r.stderr)
    return m[-1] if m else None


if __name__ == '__main__':
    only = set(sys.argv[1:])
    print('Basis: build', build(), 'Abnahme', validation('base'), 'Tests', product(), flush=True)
    results = []
    for mid, name, patches in M:
        if only and mid not in only:
            continue
        saved, ok = {}, True
        for f, a, b in patches:
            p = B + '/' + f
            t = open(p, encoding='utf-8').read()
            saved.setdefault(p, t)
            if a not in t:
                print('NOPATCH', mid, f, a[:60], flush=True)
                ok = False
                break
            open(p, 'w', encoding='utf-8').write(t.replace(a, b))
        res = dict(id=mid, name=name)
        if ok:
            err = build()
            if err:
                res['build'] = err
            else:
                res['abnahmeBestanden'] = validation(mid)
                res['tests'] = product()
        for p, t in saved.items():
            open(p, 'w', encoding='utf-8').write(t)
        results.append(res)
        print(mid, name, '->', res.get('abnahmeBestanden'), res.get('tests'), res.get('build', ''), flush=True)
    json.dump(results, open(S + '/mutation2.json', 'w'), indent=1, ensure_ascii=False)
