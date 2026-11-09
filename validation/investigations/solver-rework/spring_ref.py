# Exact rational reference: rigid supports, translational/rotational springs, hinges (rotation jumps, M(h)=0).
from fractions import Fraction as Fr
import fuzz, hinge
from fuzz import mac, H
INF='inf'
class Mechanism(Exception): pass
class ExactS(hinge.ExactH):
    def __init__(s, c):
        F = lambda v: Fr(float(v))
        s.L = F(c['L']); s.EI = F(c['E']) * F(c['I'])
        s.pf = [(F(a), F(v)) for a, v in c['pf']]; s.pm = [(F(a), F(v)) for a, v in c['pm']]
        s.q = [(F(a), F(b), F(v)) for a, b, v in c['q']]; s.h = [F(x) for x in c.get('hinges', [])]
        tr = []; ro = []   # (x, flexibility*EI)
        for a, t in c['sup']:
            tr.append((F(a), Fr(0)))
            if t == 'Fixed': ro.append((F(a), Fr(0)))
        for x, kw, kt, _ in c.get('springs', []):
            tr.append((F(x), Fr(0) if kw == INF else s.EI / F(kw)))
            if kt == INF: ro.append((F(x), Fr(0)))
            elif kt > 0: ro.append((F(x), s.EI / F(kt)))
        s.sup = [(x, 'S') for x, _ in tr]
        nt, nr, nh = len(tr), len(ro), len(s.h); n = nt + nr + 2 + nh
        A = [[Fr(0)] * (n + 1) for _ in range(n)]; r = 0
        for i in range(nt): A[r][i] = Fr(1)
        A[r][n] = -(sum(v for _, v in s.pf) + sum(v * (b - a) for a, b, v in s.q)); r += 1
        for i, (a, _) in enumerate(tr): A[r][i] = a
        for j in range(nr): A[r][nt + j] = Fr(1)
        A[r][n] = -(sum(v * a for a, v in s.pf) + sum(v for _, v in s.pm) + sum(v * (b - a) * (a + b) / 2 for a, b, v in s.q)); r += 1
        for k, (x, cw) in enumerate(tr):
            for i, (a, _) in enumerate(tr): A[r][i] = mac(x, a, 3) / 6
            A[r][k] += cw
            for j, (a, _) in enumerate(ro): A[r][nt + j] = -mac(x, a, 2) / 2
            A[r][nt + nr] = x; A[r][nt + nr + 1] = Fr(1)
            for k2, h in enumerate(s.h): A[r][nt + nr + 2 + k2] = mac(x, h, 1)
            A[r][n] = -s.wload(x); r += 1
        for k, (x, ct) in enumerate(ro):
            for i, (a, _) in enumerate(tr): A[r][i] = mac(x, a, 2) / 2
            for j, (a, _) in enumerate(ro): A[r][nt + j] = -mac(x, a, 1)
            A[r][nt + k] += ct
            A[r][nt + nr] = Fr(1)
            for k2, h in enumerate(s.h): A[r][nt + nr + 2 + k2] = Fr(1) if x > h else Fr(0)
            A[r][n] = -s.tload(x); r += 1
        for h in s.h:
            for i, (a, _) in enumerate(tr): A[r][i] = mac(h, a, 1)
            for j, (a, _) in enumerate(ro): A[r][nt + j] = -(Fr(1) if h > a else Fr(0))
            A[r][n] = -(sum(v * mac(h, a, 1) for a, v in s.pf) - sum(v for a, v in s.pm if h > a)
                        + sum(v * (mac(h, a, 2) - mac(h, b, 2)) / 2 for a, b, v in s.q)); r += 1
        for i in range(n):
            p = next((k for k in range(i, n) if A[k][i] != 0), None)
            if p is None: raise Mechanism()
            A[i], A[p] = A[p], A[i]; d = A[i][i]
            for k in range(n):
                if k != i and A[k][i] != 0:
                    f = A[k][i] / d; A[k] = [u - f * v for u, v in zip(A[k], A[i])]
        sol = [A[i][n] / A[i][i] for i in range(n)]
        s.Ry = sol[:nt]; s.Rm = sol[nt:nt + nr]; s.C1 = sol[nt + nr]; s.C2 = sol[nt + nr + 1]; s.D = sol[nt + nr + 2:]
        s.tr, s.ro = tr, ro
        s.F = s.pf + [(a, v) for (a, _), v in zip(tr, s.Ry)]
        s.C = s.pm + [(a, v) for (a, _), v in zip(ro, s.Rm)]
        s.fixed = [a for a, _ in ro]
def pts(c):
    L = c['L']
    ev = sorted({0.0, L} | {s[0] for s in c['sup']} | {s[0] for s in c.get('springs', [])} | {a for a, _ in c['pf']} | {a for a, _ in c['pm']}
                | {x for a, b, _ in c['q'] for x in (a, b)} | set(c.get('hinges', [])))
    out = []
    for j, x in enumerate(ev):
        if j > 0: out.append([x, 'L'])
        if j < len(ev) - 1:
            out.append([x, 'R'])
            for f in (.3, .7): out.append([x + (ev[j + 1] - x) * f, 'R'])
    return out
def case(id, L, sup, springs=(), hinges=(), pf=(), pm=(), q=(), E=210e9, I=None):
    I = I if I is not None else 8e-6 * L ** 4 / 4 ** 4
    c = dict(id=id, kind='s', L=L, E=E, A=.003 * L * L / 16, I=I, sup=[[a, t] for a, t in sup], springs=[list(s) for s in springs],
             hinges=list(hinges), pf=[list(p) for p in pf], pm=[list(p) for p in pm], q=[list(p) for p in q])
    c['pts'] = pts(c); return c
def errors(c, out):
    ex = ExactS(c); rows = []
    for x, sd in c['pts']:
        X = Fr(float(x)); rows.append([float(ex.w(X)), float(ex.t(X, sd)), float(ex.V(X, sd)), float(ex.M(X, sd))])
    sc = [max(abs(r[i]) for r in rows) or 1. for i in range(4)]
    L, EI = float(c['L']), float(c['E']) * float(c['I'])
    v, m = sc[2], sc[3]; sc[2] = max(v, m / L); sc[3] = max(m, v * L); sc[1] = max(sc[1], sc[3] * L / EI); sc[0] = max(sc[0], sc[3] * L ** 2 / EI)
    f = max(max(abs(r[i] - o[i]) / sc[i] for i in range(4)) for r, o in zip(rows, out['S']))
    xr = ex.extrema(); g = max(abs(out['X'][k][0] - float(xr[k])) / sc[{'w': 0, 'V': 2, 'M': 3}[k[0]]] for k in out['X'])
    R = {q[0]: q for q in out['R']}; r = 0.
    for (a, _), ry in zip(ex.tr, ex.Ry):
        o = R.get(float(a)); r = max(r, abs(o[1] - float(ry)) / sc[2]) if o else max(r, 9.)
    for (a, _), rm in zip(ex.ro, ex.Rm):
        o = R.get(float(a)); r = max(r, abs((o[2] or 0) - float(rm)) / sc[3]) if o else max(r, 9.)
    return f, g, r
