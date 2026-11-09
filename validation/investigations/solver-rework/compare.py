# Real C# old solver (clean/fuzz) against real C# new solver (newfuzz) against exact fractions.
import json, random, subprocess, sys, inspect
from fractions import Fraction as Fr
sys.path.insert(0, '.')
import fuzz, limit, spanproto


def run(project, cases, tag):
    i, o = f'cmp-{tag}-in.jsonl', f'cmp-{tag}-out.jsonl'
    with open(i, 'w') as f:
        for c in cases:
            f.write(json.dumps(c) + '\n')
    subprocess.run(['dotnet', 'run', '--no-build', '-c', 'Release', '--project', project, '--', i, o], check=True)
    return [json.loads(l) for l in open(o)]


def scales(c, rows):
    sc = [max(abs(r[i]) for r in rows) or 1. for i in range(4)]
    L, EI = float(c['L']), float(c['E']) * float(c['I'])
    v, m = sc[2], sc[3]
    sc[2] = max(v, m / L)
    sc[3] = max(m, v * L)
    sc[1] = max(sc[1], sc[3] * L / EI)
    sc[0] = max(sc[0], sc[3] * L ** 2 / EI)
    return sc


def errors(c, out, ex, rows, sc):
    """Largest scaled deviation of fields, extrema and reactions; None if refused."""
    if 'fail' in out or 'crash' in out:
        return None
    field = max(max(abs(r[i] - v[i]) / sc[i] for i in range(4)) for r, v in zip(rows, out['S']))
    ext = max(abs(out['X'][k][0] - float(ex['x'][k])) / sc[{'w': 0, 'V': 2, 'M': 3}[k[0]]] for k in out['X'])
    return dict(field=field, ext=ext, all=max(field, ext))


def exact_of(c):
    e = fuzz.Exact(c)
    rows = []
    for x, s in c['pts']:
        X = Fr(float(x))
        rows.append([float(e.w(X)), float(e.t(X)), float(e.V(X, s)), float(e.M(X, s))])
    return dict(rows=rows, x=e.extrema())


def compare(name, cases):
    old = run('clean/fuzz', cases, 'old')
    new = run('harness', cases, 'new')
    stat = dict(n=len(cases), oldRef=0, newRef=0, crash=0, oldMax=0., newMax=0., old6=0, new6=0, oldExt=0., newExt=0., diff=0., worst=None)
    for c, o, n in zip(cases, old, new):
        ex = exact_of(c)
        sc = scales(c, ex['rows'])
        eo, en = errors(c, o, ex, ex['rows'], sc), errors(c, n, ex, ex['rows'], sc)
        if 'crash' in n or 'crash' in o:
            stat['crash'] += 1
            print('  CRASH', c['id'], n.get('crash'), o.get('crash'))
        if eo is None:
            stat['oldRef'] += 1
        else:
            stat['oldMax'] = max(stat['oldMax'], eo['all'])
            stat['oldExt'] = max(stat['oldExt'], eo['ext'])
            stat['old6'] += eo['all'] > 1e-6
        if en is None:
            stat['newRef'] += 1
        else:
            if en['all'] > stat['newMax']:
                stat['worst'] = (c['id'], en['field'], en['ext'])
            stat['newMax'] = max(stat['newMax'], en['all'])
            stat['newExt'] = max(stat['newExt'], en['ext'])
            stat['new6'] += en['all'] > 1e-6
        if eo is not None and en is not None:
            d = max(max(abs(a[i] - b[i]) / sc[i] for i in range(4)) for a, b in zip(o['S'], n['S']))
            stat['diff'] = max(stat['diff'], d)
    print('%-26s n=%5d | abgelehnt alt %5d neu %4d crash %d | MaxAbw alt %.1e neu %.1e | Extremwerte alt %.1e neu %.1e | >1e-6 alt %4d neu %3d | alt-neu %.1e'
          % (name, stat['n'], stat['oldRef'], stat['newRef'], stat['crash'], stat['oldMax'], stat['newMax'], stat['oldExt'], stat['newExt'], stat['old6'], stat['new6'], stat['diff']), flush=True)
    if stat['newMax'] > 1e-9:
        print('    schlechtester neu:', stat['worst'])
    return stat


def cad():
    out = []
    x1, x2 = 0.10054, 0.100540006
    for L in [0.2, 0.3, 0.5, 1.0, 2.0, 5.0, 10.0]:
        I = 8e-6 * L ** 4 / 4 ** 4
        for nm, sup in [('Einfeld', [(0., 'Pinned'), (L, 'Roller')]), ('Kragarm', [(0., 'Fixed')]), ('Beidseitig', [(0., 'Fixed'), (L, 'Fixed')])]:
            out.append(limit.case(f'{nm} L={L}', L, sup, pf=[(x1, -5000.), (x2, -3000.)], I=I))
        out.append(limit.case(f'Lager+6nm L={L}', L, [(x1, 'Pinned'), (L, 'Roller')], pf=[(x2, -5000.), ((x1 + L) / 2, -3000.)], I=I))
        out.append(limit.case(f'Lager+6nm+q L={L}', L, [(x1, 'Pinned'), (L, 'Roller')], pf=[(x2, -5000.)], q=[(x1, L, -2000.)], I=I))
    return out


def topo():
    out = []
    L = 10.0
    for r in [1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9, 1e-10]:
        for name, t in limit.topologies(L, r * L).items():
            out.append(limit.case(f'{name}|{r}', L, **t))
    return out


def fuzzset(kind, N, seed):
    r = random.Random(seed)
    out = []
    for i in range(N):
        out.append(fuzz.gen(r, kind, i))
    return out


if __name__ == '__main__':
    what = sys.argv[1:]
    if 'cad' in what:
        compare('CAD 6 nm', cad())
    if 'topo' in what:
        compare('13 Bilder, 9 Abstaende', topo())
    if 'rand' in what:
        for r0 in [1e-2, 5e-3, 1e-3, 1e-4, 1e-5, 1e-7, 1e-9, 1e-11]:
            compare(f'Zufall Abstand/L={r0:g}', spanproto.gen(r0, 1000))
    if 'fuzz' in what:
        for kind, N in [('normal', 1500), ('wide', 1000), ('cluster', 1000), ('many', 100)]:
            compare(f'Fuzz {kind}', fuzzset(kind, N, 4242))
