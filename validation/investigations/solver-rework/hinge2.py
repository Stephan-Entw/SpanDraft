# Scratch: cut at every hinge. Unknowns are hinge shears and rigid motions of under-supported parts.
import random, sys
from fractions import Fraction as Fr
import numpy as np
sys.path.insert(0, '.')
import hinge, spanproto
from hinge import Mechanism, stable, ExactH, case


def equilibrate(A):
    r = np.ones(A.shape[0]); c = np.ones(A.shape[1])
    for _ in range(8):
        B = (r[:, None] * A) * c
        rr = np.sqrt(np.abs(B).max(axis=1)); rr[rr == 0] = 1
        cc = np.sqrt(np.abs(B).max(axis=0)); cc[cc == 0] = 1
        r /= rr; c /= cc
    return r, c


class Part:
    def __init__(s, c, x0, x1, first, last):
        s.c, s.x0, s.x1, s.len = c, x0, x1, x1 - x0
        s.pf = [(x - x0, v) for x, v in c['pf'] if (x0 < x <= x1) or (first and x == x0)]
        s.pm = [(x - x0, v) for x, v in c['pm'] if (x0 < x <= x1) or (first and x == x0)]
        s.q = [(max(a, x0) - x0, min(b, x1) - x0, v) for a, b, v in c['q'] if min(b, x1) > max(a, x0)]
        s.sup = [(a - x0, t) for a, t in c['sup'] if x0 <= a <= x1]
        fixed = [a for a, t in s.sup if t == 'Fixed']
        s.rigid = 0
        if not fixed and len(s.sup) == 1:
            s.rigid = 1
        elif not s.sup:
            s.rigid = 2
        s.anchor = s.sup[0][0] if s.rigid == 1 else 0.0
        s.calc_sup = [(s.anchor, 'Fixed')] if s.rigid else s.sup

    def solve(s, extra):
        d = dict(L=s.len, E=s.c['E'], I=s.c['I'], sup=s.calc_sup, pf=s.pf + extra, pm=s.pm, q=s.q)
        return spanproto.SpanSolver(d), extra

    def reactions(s, sol, extra):
        """Fictive clamp reaction at the anchor, positive like a support reaction."""
        a = s.anchor
        R = sol.at(a, 'R') if a < s.len else [0, 0, 0, 0]
        Lf = sol.at(a, 'L') if a > 0 else [0, 0, 0, 0]
        F = sum(v for x, v in s.pf + extra if x == a)
        C = sum(v for x, v in s.pm if x == a)
        return R[2] - Lf[2] - F, Lf[3] - R[3] - C


class ForceHinge:
    def __init__(s, c):
        if not stable(c):
            raise Mechanism()
        s.c = c
        L = c['L']
        s.EI = c['E'] * c['I']
        hs = sorted(c.get('hinges', []))
        cuts = [0.0] + hs + [L]
        s.cuts = cuts
        s.parts = [Part(c, cuts[i], cuts[i + 1], i == 0, i == len(cuts) - 2) for i in range(len(cuts) - 1)]
        supx = {a for a, _ in c['sup']}
        s.link = [i for i, h in enumerate(hs) if h not in supx]  # hinges with an unknown shear
        ns = len(s.link)
        rows = {}
        nr = sum(p.rigid for p in s.parts)
        n = ns + nr
        s.rigid_index = {}
        k = ns
        for i, p in enumerate(s.parts):
            if p.rigid:
                s.rigid_index[i] = k
                k += p.rigid

        def extras(z):
            out = [[] for _ in s.parts]
            for j, hi in enumerate(s.link):
                out[hi].append((s.parts[hi].len, float(z[j])))
                out[hi + 1].append((0.0, -float(z[j])))
            return out

        def evaluate(z, with_loads=True):
            res = np.zeros(n)
            ex = extras(z)
            sols = []
            for i, p in enumerate(s.parts):
                if not with_loads:
                    q = Part.__new__(Part); q.__dict__.update(p.__dict__); q.pf = []; q.pm = []; q.q = []
                else:
                    q = p
                sol, extra = q.solve(ex[i])
                sols.append((sol, extra, q))
            def W(i, x):
                sol, _, q = sols[i]
                w = sol.at(x, 'L' if x > 0 else 'R')[0]
                if q.rigid and i in s.rigid_index:
                    idx = s.rigid_index[i]
                    if q.rigid == 1:
                        w += float(z[idx]) * (x - q.anchor)
                    else:
                        w += float(z[idx]) + float(z[idx + 1]) * x
                return w
            for j, hi in enumerate(s.link):
                res[j] = W(hi, s.parts[hi].len) - W(hi + 1, 0.0)
            for i, (sol, extra, q) in enumerate(sols):
                if q.rigid:
                    ry, rm = q.reactions(sol, extra)
                    idx = s.rigid_index[i]
                    if q.rigid == 1:
                        res[idx] = rm
                    else:
                        res[idx], res[idx + 1] = ry, rm
            return res

        z = np.zeros(n)
        if n:
            b = evaluate(np.zeros(n))
            K = np.zeros((n, n))
            for j in range(n):
                e = np.zeros(n); e[j] = 1.0
                K[:, j] = evaluate(e, False)
            r, cs = equilibrate(K)
            A = (r[:, None] * K) * cs
            s.cond = np.linalg.cond(A)
            z = np.linalg.solve(A, -b * r) * cs
        else:
            s.cond = 1.0
        s.z = z
        ex = extras(z)
        s.sols = [p.solve(ex[i])[0] for i, p in enumerate(s.parts)]

    def at(s, x, side):
        i = 0
        for j in range(len(s.parts)):
            if x > s.cuts[j] or (x == s.cuts[j] and side == 'R'):
                i = j
        if x == s.c['L']:
            i = len(s.parts) - 1
        p, sol = s.parts[i], s.sols[i]
        t = x - s.cuts[i]
        v = sol.at(t, side if t > 0 else 'R')
        v = list(v)
        if p.rigid:
            idx = s.rigid_index[i]
            if p.rigid == 1:
                v[0] += s.z[idx] * (t - p.anchor); v[1] += s.z[idx]
            else:
                v[0] += s.z[idx] + s.z[idx + 1] * t; v[1] += s.z[idx + 1]
        return v


def err(c):
    e = ExactH(c)
    so = ForceHinge(c)
    rows = []
    for x, sd in c['pts']:
        X = Fr(float(x))
        rows.append(([float(e.w(X)), float(e.t(X, sd)), float(e.V(X, sd)), float(e.M(X, sd))], so.at(float(x), sd)))
    sc = [max(abs(r[0][i]) for r in rows) or 1. for i in range(4)]
    v, mm = sc[2], sc[3]
    sc[2] = max(v, mm / c['L']); sc[3] = max(mm, v * c['L'])
    EI = c['E'] * c['I']
    sc[1] = max(sc[1], sc[3] * c['L'] / EI); sc[0] = max(sc[0], sc[3] * c['L'] ** 2 / EI)
    return max(max(abs(r[0][i] - r[1][i]) / sc[i] for r in rows) for i in range(4)), so.cond


def sweep():
    L = 10.0; P = -5000.
    rs = [1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9]
    T = {
        'Gerber: Gelenk nahe Mittellager': lambda g: dict(sup=[(0., 'Pinned'), (.5 * L, 'Roller'), (L, 'Roller')], hinges=[.5 * L + g], pf=[(.3 * L, P), (.8 * L, P)]),
        'Einspannung, Gelenk nahe daran': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Roller')], hinges=[g], pf=[(.5 * L, P)]),
        'Einspannung+Rolle, Gelenk nahe Rolle': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Roller')], hinges=[L - g], pf=[(.5 * L, P)]),
        'Beidseitig fest, Gelenk Mitte, Last nah': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.5 * L], pf=[(.5 * L + g, P)]),
        'Einhaengetraeger, Gelenke dicht': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.5 * L, .5 * L + g], pf=[(.25 * L, P)]),
        'Einhaengetraeger normal, Last nah Gelenk': lambda g: dict(sup=[(0., 'Fixed'), (L, 'Fixed')], hinges=[.3 * L, .7 * L], pf=[(.3 * L + g, P)]),
        'Ueberhang-Gerber: Gelenk nahe Lager': lambda g: dict(sup=[(0., 'Pinned'), (.4 * L, 'Roller'), (L, 'Roller')], hinges=[.4 * L + g], pf=[(.7 * L, P)], q=[(0., L, -2000.)]),
        'Zwei Lager dicht plus Gelenk': lambda g: dict(sup=[(0., 'Pinned'), (.3 * L, 'Roller'), (.3 * L + g, 'Roller'), (L, 'Roller')], hinges=[.6 * L], pf=[(.5 * L, P)]),
        'Gelenk genau auf Lager': lambda g: dict(sup=[(0., 'Pinned'), (.5 * L, 'Roller'), (L, 'Roller')], hinges=[.5 * L], pf=[(.25 * L, P), (.5 * L + g, P)]),
    }
    print('%-42s' % 'Abstand/L' + ''.join('%9.0e' % r for r in rs))
    for name, f in T.items():
        row = ''; wc = 0
        for r in rs:
            c = case(f'{name}|{r}', L, **f(r * L))
            try:
                e, cond = err(c)
                row += '%9.0e' % e; wc = max(wc, cond)
            except Mechanism:
                row += '     MECH'
            except np.linalg.LinAlgError:
                row += '     SING'
        print('%-42s' % name + row, 'cond %.0e' % wc, flush=True)


def gen(rng, r0):
    L = 10 ** rng.uniform(-2, 1.5)
    while True:
        k = rng.randint(1, 6)
        grid = sorted(rng.sample(range(0, 21), k))
        sup, hinges = [], []
        for g in grid:
            x = L if g == 20 else g * L / 20
            u = rng.random()
            if u < .4: sup.append((x, 'Roller'))
            elif u < .55: sup.append((x, 'Pinned'))
            elif u < .7: sup.append((x, 'Fixed'))
            elif 0 < x < L: hinges.append(x)
        if not sup or not hinges: continue
        # Shift hinges so that some sit r0*L beside a support.
        hinges = sorted({(rng.choice(sup)[0] + rng.choice([-1, 1]) * r0 * L) if rng.random() < .5 else h for h in hinges})
        hinges = [h for h in hinges if 0 < h < L and all(h != a or t != 'Fixed' for a, t in sup)]
        if not hinges: continue
        c0 = dict(L=L, sup=sup, hinges=hinges)
        if stable(c0): break
    mag = 10 ** rng.uniform(1, 5)
    pos = lambda: rng.choice([rng.uniform(0, L), rng.choice(hinges) + rng.choice([-1, 1]) * r0 * L, rng.choice(sup)[0] + rng.choice([-1, 1]) * r0 * L])
    pf = [(x, -mag * rng.uniform(.2, 1)) for x in (pos() for _ in range(rng.randint(1, 4))) if 0 <= x <= L and x not in hinges]
    pm = [(x, mag * L * rng.uniform(-.3, .3)) for x in (pos() for _ in range(rng.randint(0, 2))) if 0 <= x <= L and x not in hinges]
    q = [(0., L, -mag / L)] if rng.random() < .3 else []
    if not pf and not pm and not q: pf = [(L / 2, -mag)] if L / 2 not in hinges else [(L / 3, -mag)]
    return case(f'{r0}', L, sup, hinges=hinges, pf=pf, pm=pm, q=q, E=rng.choice([210e9, 70e9, 2.5e9]), I=L ** 4 * 10 ** rng.uniform(-9, -5))


def rand(N):
    for r0 in [1e-2, 1e-3, 1e-5, 1e-7, 1e-9]:
        rng = random.Random(int(1 / r0) + 3)
        es = []; bad = 0; ex = None
        for i in range(N):
            c = gen(rng, r0)
            try:
                e, _ = err(c)
                es.append(e)
                if ex is None or e > ex[0]:
                    ex = (e, c['sup'], c['hinges'], c['pf'], c['pm'], c['L'])
            except (Mechanism, np.linalg.LinAlgError, ZeroDivisionError) as err_:
                bad += 1
                print('   FEHL', type(err_).__name__, c['sup'], c['hinges'])
        es.sort()
        print('Abstand/L=%-7g n=%d Fehlschlag %d | Median %.0e p99 %.0e Max %.0e >1e-6: %d' % (r0, len(es), bad, es[len(es) // 2], es[int(.99 * len(es))], es[-1], sum(x > 1e-6 for x in es)), flush=True)
        if ex[0] > 1e-8:
            print('    schlechtester', '%.1e' % ex[0], 'L=%.3g' % ex[5], [(round(a / ex[5], 12), t) for a, t in ex[1]], [round(h / ex[5], 12) for h in ex[2]])


if __name__ == '__main__':
    if 'sweep' in sys.argv: sweep()
    if 'rand' in sys.argv: rand(int(sys.argv[sys.argv.index('rand') + 1]))
