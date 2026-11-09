# Fault injection for the new solver path: same fault classes as mutate.py, plus faults that only the new code can have.
import json, os, re, subprocess, sys
S = os.path.dirname(os.path.abspath(__file__))
B = S + '/base3'
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
    ('S1', 'Federsteifigkeit: Nachgiebigkeit 1/k mit Faktor 1.01', [(SS, 'couple ? 1 / s.Rotational : 1 / s.Translational', 'couple ? 1 / s.Rotational : 1.01 / s.Translational')]),
    ('S2', 'Gruppen: Nachgiebigkeitsblock ohne Kreuzterme', [(SS, 'left[g.Indices[k], g.Indices[l]] += poles[k].Weight(j) * poles[l].Weight(j) * g.Flexibility[j];', 'if (k == l) left[g.Indices[k], g.Indices[l]] += poles[k].Weight(j) * poles[l].Weight(j) * g.Flexibility[j];')]),
    ('S3', 'Dritte Newton-Ordnung: Gewicht falsch', [(SS, 'return j switch { 0 => 1 / (a * c), 1 => -1 / (a * b), _ => 1 / (b * c) };', 'return j switch { 0 => 1 / (a * c), 1 => -1 / (a * b), _ => 1 / (b * a) };')]),
    ('S4', 'Dipol: Durchbiegung jenseits falsch', [(SS, 'return -(d1 * d1 + d1 * d2 + d2 * d2) / 6;', 'return -(d1 * d1 + d2 * d2) / 6;')]),
    ('S5', 'Drehfeder-Dipol: Moment Vorzeichen falsch', [(SS, 'return x >= t1 && x < Knots[1] ? 1 / a : 0;', 'return x >= t1 && x < Knots[1] ? -1 / a : 0;')]),
    ('S6', 'Lokale Auswertung: Richtung links vom Lager falsch', [(SS, 'double oriented = x > node ? sum : -sum;', 'double oriented = sum;')]),
    ('S7', 'Starrkoerperdrehung fehlt in Gruppenzeile', [(SS, ': f.Order switch { 1 => (1, f.First - p.Anchor), 2 => (0, 1), _ => (0, 0) };', ': f.Order switch { 1 => (1, f.First - p.Anchor), _ => (0, 0) };')]),
    ('S8', 'Gruppenzeile 3. Ordnung: Kern mit Faktor 1.01', [(SS, 'integrand = field.Curvature(x) * kernel;', 'integrand = field.Curvature(x) * kernel * 1.01;')]),
    ('S9', 'Federreaktion aus falscher Ordnung', [(SS, 'else reactionY = GroupAction(g, j);', 'else reactionY = solution[g.Indices[j]];')]),
    ('S10', 'Drehfeder am starren Lager wirkt nicht', [(SS, 'matrix[k, k] += rotational[i];', 'matrix[k, k] += 0;')]),
    ('S11', 'Stabilitaet: Drehfedern zaehlen nicht', [(SO, '.Concat(loads.Springs.Where(s => s.Rotational > 0).Select(s => (s.X, true)))', '')]),
    ('S12', 'Stabilitaet: Drehbehinderung als Wegbehinderung', [(SM, 'if (rotation) row[2 * i + 1] = Rational.One;', 'if (false) row[2 * i + 1] = Rational.One;')]),
    ('S13', 'Feder mit Axialhalt haelt nicht axial', [(SS, '.Select(s => s.Position.Meters).Concat(loads.Springs.Where(s => s.AxialRestraint).Select(s => s.X))', '.Select(s => s.Position.Meters)')]),
    ('J1', 'Querschnittswechsel: Moment wird nicht uebertragen', [(SS, 'if (joints.Contains(x) && !supports.Any(s => s.X == x && s.Clamped)) passed.Add(true);', '')]),
    ('J2', 'Querschnittswechsel: EI des ersten statt letzten Wechsels', [(SS, '.Select(r => r.Value).DefaultIfEmpty(ei).Last();', '.Select(r => r.Value).DefaultIfEmpty(ei).First();')]),
    ('J3', 'Einspannmoment am Wechsel ueberschrieben', [(SS, 'reactionMoment = (reactionMoment ?? 0) + moment;', 'reactionMoment = moment;')]),
    ('J4', 'Drehfeder am Wechsel doppelt gezaehlt', [(SS, '.Select(s => s.X == a && joints.Contains(a) ? s with { Rotational = 0 } : s).ToArray()', '.ToArray()')]),
    ('J5', 'Gelenkwinkel links vom falschen Teil', [(SS, 'Part p = parts[Array.IndexOf(cuts, x) - 1];', 'Part p = parts[Array.IndexOf(cuts, x)];')]),
    ('L1', 'Dreieckslast: Neigung jenseits falsch', [(SS, '3 => (d * d + 4 * d * f + 6 * f * f) / 24,', '3 => (d * d + 4 * d * f + 6 * f * f) / 25,')]),
    ('L2', 'Linearlast: Grundanteil Teiler falsch', [(SS, '/ power switch { 1 => 1, 2 => 2, 3 => 6, _ => 24 } +', '/ power switch { 1 => 1, 2 => 2, 3 => 6, _ => 20 } +')]),
    ('L3', 'Linearlast beim Zuschneiden: Endwert falsch', [(SS, 'double qb = b == l.End ? l.EndIntensity : l.StartIntensity + rise * ((b - l.Start) / (l.End - l.Start));', 'double qb = l.EndIntensity;')]),
    ('L4', 'Feldpolynom 5. Grad: Faktor 120 falsch', [(FI, 'c5 = qs / ei * length * length * length * length * length / 120;', 'c5 = qs / ei * length * length * length * length * length / 100;')]),
    ('L5', 'Querkraftextrem im Feld wird nicht gesucht', [(EX, 'foreach (double t in field.StationaryShears())', 'foreach (double t in Array.Empty<double>())')]),
    ('L6', 'Momentenextrem: quadratischer Term fehlt', [(FI, 'PolynomialRoots.InUnitInterval(v, q * length, qs * length * length / 2);', 'PolynomialRoots.InUnitInterval(v, q * length);')]),
    ('K1', 'Kruemmung fehlt im Feldpolynom', [(FI, 'c2 = (m / ei + state.ImposedCurvature) * length * length / 2;', 'c2 = m / ei * length * length / 2;')]),
    ('K2', 'Kruemmung in Neigung mit falschem Vorzeichen', [(SS, '+ LinearPart(x, 3) + curvature.Sum(k => k.Intensity * Difference(x, k.Start, k.End, 1));', '+ LinearPart(x, 3) - curvature.Sum(k => k.Intensity * Difference(x, k.Start, k.End, 1));')]),
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
    rp = S + '/r3_' + mid + '.json'
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
    json.dump(results, open(S + '/mutation3.json', 'w'), indent=1, ensure_ascii=False)
