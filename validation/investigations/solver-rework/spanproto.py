# Scratch prototype only: nodes at supports, loads handled analytically, in plain doubles.
# V and M come from equilibrium, w and theta from closed-form integration.
# Unknowns are the rotations at non-fixed supports. Nothing is shifted or merged.
import json, random, subprocess, sys
from fractions import Fraction as Fr
sys.path.insert(0, '.')
import fuzz, limit


def m(x, a, n):
    return (x - a) ** n if x > a else 0.0


def step(x, a, side):
    return 1.0 if (x > a or (x == a and side == 'R')) else 0.0


class Seg:
    """Loads on [a, b], in local coordinate s = x - a."""
    def __init__(s, c, a, b):
        s.a, s.l = a, b - a
        s.pf = [(x - a, v) for x, v in c['pf'] if a < x <= b or (x == a and a == 0.0 and False)]
        s.pm = [(x - a, v) for x, v in c['pm'] if a < x <= b]
        s.q = []
        for p, r, v in c['q']:
            lo, hi = max(p, a), min(r, b)
            if hi > lo:
                s.q.append((lo - a, hi - a, v))

    def P(s, x):
        return (sum(v * m(x, a, 3) / 6 for a, v in s.pf) - sum(v * m(x, a, 2) / 2 for a, v in s.pm)
                + sum(v * (m(x, a, 4) - m(x, b, 4)) / 24 for a, b, v in s.q))

    def T(s, x):
        return (sum(v * m(x, a, 2) / 2 for a, v in s.pf) - sum(v * m(x, a, 1) for a, v in s.pm)
                + sum(v * (m(x, a, 3) - m(x, b, 3)) / 6 for a, b, v in s.q))

    def Vl(s, x, side):
        return (sum(v * step(x, a, side) for a, v in s.pf)
                + sum(v * (m(x, a, 1) - m(x, b, 1)) for a, b, v in s.q))

    def Ml(s, x, side):
        return (sum(v * m(x, a, 1) for a, v in s.pf) - sum(v * step(x, a, side) for a, v in s.pm)
                + sum(v * (m(x, a, 2) - m(x, b, 2)) / 2 for a, b, v in s.q))


def solve_sym(A, b):
    n = len(b)
    if n == 0:
        return []
    d = [1 / A[i][i] ** .5 for i in range(n)]
    M = [[d[i] * A[i][j] * d[j] for j in range(n)] for i in range(n)]
    y = [d[i] * b[i] for i in range(n)]
    for i in range(n):
        for k in range(i + 1, n):
            f = M[k][i] / M[i][i]
            if f:
                for j in range(i, n):
                    M[k][j] -= f * M[i][j]
                y[k] -= f * y[i]
    z = [0.0] * n
    for i in range(n - 1, -1, -1):
        z[i] = (y[i] - sum(M[i][j] * z[j] for j in range(i + 1, n))) / M[i][i]
    return [d[i] * z[i] for i in range(n)]


class SpanSolver:
    def __init__(s, c):
        s.c = c
        s.L = c['L']
        s.EI = c['E'] * c['I']
        sup = sorted((a, t) for a, t in c['sup'])
        s.nodes = [a for a, _ in sup]
        fixed = [t == 'Fixed' for _, t in sup]
        n = len(sup)
        s.spans = [Seg(c, s.nodes[i], s.nodes[i + 1]) for i in range(n - 1)]
        left_end = s.nodes[0]
        # Left overhang is measured from x = 0, and includes loads at x = 0.
        s.over_l = None
        if left_end > 0:
            cl = dict(c)
            cl['pf'] = [(x + 1.0, v) for x, v in c['pf'] if x < left_end]
            cl['pm'] = [(x + 1.0, v) for x, v in c['pm'] if x < left_end]
            cl['q'] = [(p + 1.0, r + 1.0, v) for p, r, v in c['q'] if p < left_end]
            s.over_l = Seg(dict(pf=[(x, v) for x, v in c['pf'] if x < left_end],
                                pm=[(x, v) for x, v in c['pm'] if x < left_end],
                                q=c['q']), -1e300, left_end)
            s.over_l.a = 0.0
            s.over_l.l = left_end
            s.over_l.pf = [(x, v) for x, v in c['pf'] if x < left_end]
            s.over_l.pm = [(x, v) for x, v in c['pm'] if x < left_end]
            s.over_l.q = [(p, min(r, left_end), v) for p, r, v in c['q'] if p < left_end]
        s.over_r = Seg(c, s.nodes[-1], s.L) if s.nodes[-1] < s.L else None

        Mleft0 = s.over_l.Ml(left_end, 'R') if s.over_l else 0.0
        Mright_end = 0.0
        if s.over_r:
            o = s.over_r
            Va = -o.Vl(o.l, 'R')
            Mright_end = -Va * o.l - o.Ml(o.l, 'R')
        Cnode = [sum(v for x, v in c['pm'] if x == a) for a in s.nodes]

        free = [i for i in range(n) if not fixed[i]]
        idx = {i: k for k, i in enumerate(free)}
        A = [[0.0] * len(free) for _ in free]
        b = [0.0] * len(free)
        for i in free:
            k = idx[i]
            rhs = Cnode[i]
            if i < n - 1:
                sp = s.spans[i]
                l = sp.l
                A[k][k] += 4 * s.EI / l
                if i + 1 in idx:
                    A[k][idx[i + 1]] += 2 * s.EI / l
                rhs += 2 * sp.T(l) / l - 6 * sp.P(l) / l ** 2
            else:
                rhs += Mright_end
            if i > 0:
                sp = s.spans[i - 1]
                l = sp.l
                A[k][k] += 4 * s.EI / l
                if i - 1 in idx:
                    A[k][idx[i - 1]] += 2 * s.EI / l
                rhs -= -4 * sp.T(l) / l + 6 * sp.P(l) / l ** 2 + sp.Ml(l, 'L')
            else:
                rhs -= Mleft0
            b[k] = rhs
        sol = solve_sym(A, b)
        s.th = [0.0] * n
        for i in free:
            s.th[i] = sol[idx[i]]
        s.Va, s.Ma = [0.0] * n, [0.0] * n
        for i in range(n - 1):
            sp = s.spans[i]
            l = sp.l
            ta, tb = s.th[i], s.th[i + 1]
            s.Va[i] = 6 * s.EI * (ta + tb) / l ** 2 + (12 * sp.P(l) - 6 * sp.T(l) * l) / l ** 3
            s.Ma[i] = -s.EI * (4 * ta + 2 * tb) / l + 2 * sp.T(l) / l - 6 * sp.P(l) / l ** 2
        if s.over_r:
            o = s.over_r
            s.Va[-1] = -o.Vl(o.l, 'R')
            s.Ma[-1] = Mright_end

    def at(s, x, side):
        n = s.nodes
        if x < n[0] or (x == n[0] and side == 'L' and n[0] > 0):
            o = s.over_l
            C1 = s.EI * s.th[0] - o.T(o.l)
            C2 = -C1 * o.l - o.P(o.l)
            return [(o.P(x) + C1 * x + C2) / s.EI, (o.T(x) + C1) / s.EI, o.Vl(x, side), o.Ml(x, side)]
        i = 0
        for j, a in enumerate(n):
            if x > a or (x == a and side == 'R'):
                i = j
        if x == n[0] and side == 'L':
            i = 0
        seg = s.spans[i] if i < len(s.spans) else s.over_r
        if seg is None:  # end of the beam exactly on the last support
            seg = s.spans[i - 1]
            i -= 1
        t = x - n[i]
        Va, Ma, th = s.Va[i], s.Ma[i], s.th[i]
        return [(s.EI * th * t + Va * t ** 3 / 6 + Ma * t ** 2 / 2 + seg.P(t)) / s.EI,
                th + (Va * t ** 2 / 2 + Ma * t + seg.T(t)) / s.EI,
                Va + seg.Vl(t, side),
                Ma + Va * t + seg.Ml(t, side)]


def err(c):
    e = fuzz.Exact(c)
    so = SpanSolver(c)
    rows = []
    for x, sd in c['pts']:
        X = Fr(float(x))
        rows.append(([float(e.w(X)), float(e.t(X)), float(e.V(X, sd)), float(e.M(X, sd))], so.at(float(x), sd)))
    sc = [max(abs(r[0][i]) for r in rows) or 1. for i in range(4)]
    v, mm = sc[2], sc[3]
    sc[2] = max(v, mm / c['L'])
    sc[3] = max(mm, v * c['L'])
    EI = c['E'] * c['I']
    sc[1] = max(sc[1], sc[3] * c['L'] / EI)
    sc[0] = max(sc[0], sc[3] * c['L'] ** 2 / EI)
    return max(max(abs(r[0][i] - r[1][i]) for r in rows) / sc[i] for i in range(4))


def today(cases):
    res = limit.evaluate(cases)
    return {r['id']: r for r in res}


def fmt(r):
    return 'ABGELEHNT' if 'ref' in r else '%.0e' % r['all']


def cad():
    print('== CAD-Fall: F1 bei 100,54 mm, F2 bei 100,540006 mm (6 nm Abstand)')
    x1, x2 = 0.10054, 0.100540006
    F1, F2 = -5000., -3000.
    cs = []
    for L in [0.2, 0.3, 0.5, 1.0, 2.0, 5.0, 10.0]:
        I = 8e-6 * L ** 4 / 4 ** 4
        cs.append(limit.case(f'Einfeld L={L}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(x1, F1), (x2, F2)], I=I))
        cs.append(limit.case(f'Kragarm L={L}', L, [(0., 'Fixed')], pf=[(x1, F1), (x2, F2)], I=I))
        cs.append(limit.case(f'Beidseitig L={L}', L, [(0., 'Fixed'), (L, 'Fixed')], pf=[(x1, F1), (x2, F2)], I=I))
    t = today(cs)
    print('%-22s %10s %10s' % ('Modell', 'heute', 'Prototyp'))
    for c in cs:
        print('%-22s %10s %10.0e' % (c['id'], fmt(t[c['id']]), err(c)))


def topo():
    print('== 13 Lagerungs- und Lastbilder, L = 10 m: heute | Prototyp')
    rs = [1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8, 1e-9, 1e-10]
    L = 10.0
    cs = []
    for r in rs:
        for name, t in limit.topologies(L, r * L).items():
            cs.append(limit.case(f'{name}|{r}', L, **t))
    tt = today(cs)
    names = list(limit.topologies(L, .1).keys())
    print('%-34s' % 'Abstand/L' + ''.join('%16.0e' % r for r in rs))
    for nm in names:
        row = ''
        for r in rs:
            c = next(q for q in cs if q['id'] == f'{nm}|{r}')
            row += '%9s|%6.0e' % (fmt(tt[c['id']]), err(c))
        print('%-34s' % nm + row, flush=True)


def gen(r0, N):
    rng = random.Random(int(1 / r0) + 7)
    cases = []
    for i in range(N):
        L = 10 ** rng.uniform(-2, 1.5)
        st = rng.choice(['K', 'E', 'U', 'ZW', 'EW', 'FF', 'VF', 'DS'])
        p = rng.uniform(.2, .8) * L
        sup = {'K': [(0., 'Fixed')], 'E': [(0., 'Pinned'), (L, 'Roller')], 'U': [(0., 'Pinned'), (p, 'Roller')],
               'ZW': [(0., 'Pinned'), (.5 * L, 'Roller'), (L, 'Roller')], 'EW': [(0., 'Fixed'), (L, 'Roller')],
               'FF': [(0., 'Fixed'), (L, 'Fixed')], 'VF': [(0., 'Pinned')] + [(L * (k + 1) / 4, 'Roller') for k in range(4)],
               'DS': [(p, 'Pinned'), (p + r0 * L, 'Roller')]}[st]
        stations, g = [], r0 * L
        for _ in range(rng.randint(1, 6)):
            base = rng.choice([0., L, rng.choice(sup)[0], rng.uniform(0, L), rng.uniform(0, L)])
            d = rng.choice([1, -1])
            for j in range(1, rng.randint(1, 4) + 1):
                x = base + d * j * g
                if 0 <= x <= L:
                    stations.append(x)
        acc = sorted({0., L} | {a for a, _ in sup})
        for x in stations:
            if all(abs(x - a) >= g * (1 - 1e-9) for a in acc):
                acc.append(x)
                acc.sort()
        supx = [a for a, _ in sup]
        extra = [x for x in acc if x not in (0., L) and x not in supx] or [L / 2]
        pf, pm, q, mag = [], [], [], 10 ** rng.uniform(1, 5)
        for x in extra:
            k = rng.random()
            if k < .6:
                pf.append((x, rng.choice([-1, -1, 1]) * mag * rng.uniform(.2, 1)))
            elif k < .8:
                pm.append((x, rng.choice([-1, 1]) * mag * L * rng.uniform(.05, .5)))
            else:
                y = rng.choice(acc)
                if y != x:
                    q.append((min(x, y), max(x, y), -mag / L))
                else:
                    pf.append((x, -mag))
        if rng.random() < .3:
            q.append((0., L, -mag / L))
        cases.append(limit.case(f'{r0}|{i}', L, sup, pf=pf, pm=pm, q=q,
                                E=rng.choice([210e9, 70e9, 110e9, 2.5e9]), I=L ** 4 * 10 ** rng.uniform(-9, -5)))
    return cases


def rnd(N):
    print('== Zufallstraeger ohne Mindestabstand, Prototyp gegen exakte Rechnung')
    for r0 in [1e-2, 1e-3, 1e-5, 1e-7, 1e-9, 1e-11]:
        cs = gen(r0, N)
        tt = today(cs)
        refused = sum('ref' in tt[c['id']] for c in cs)
        es = []
        for c in cs:
            try:
                es.append(err(c))
            except ZeroDivisionError:
                es.append(float('inf'))
        es.sort()
        print('Abstand/L=%-7g n=%d | heute abgelehnt %5.1f%% | Prototyp abgelehnt 0, Median %.0e, p99 %.0e, Max %.0e, >1e-6: %d'
              % (r0, N, refused / N * 100, es[N // 2], es[int(.99 * N)], es[-1], sum(e > 1e-6 for e in es)), flush=True)


if __name__ == '__main__':
    mode = sys.argv[1]
    if mode == 'cad':
        cad()
    if mode == 'topo':
        topo()
    if mode == 'rnd':
        rnd(int(sys.argv[2]))
