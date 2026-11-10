"""Round 3, finding: the axial part (u, N) had no quality indicator in the research code, although
the contract (7.2/7.3) covers u and N. This adds the same componentwise indicator and gate to the
axial formulation and measures it against the exact axial reference.

AxialQ(c): AxialSolver with stored scaled system, rounding counts per entry, d as in state.py
(|r| + residual rounding + data rounding + underflow term), adjoint solves with one refinement
step. Rounding counts (positive terms, nondimensional): a = fl(x1 - x0)/Lr: 1; ea = fl(E*A)/EAr: 1;
a/ea: 3; spring k*Lr/EAr: 1 (sum of springs at one position: +1 each); load terms: point force
P/EAr exact (0), its u term P*d with d = fl(x - p)/Lr: 2; distributed n*near: 2, n*near^2/2: 3,
n*near*(near/2 + far): 4; a sum of m terms adds m; division by ea adds 2.
Field: u(x) = (u0 + (N0 s + du)/ea) Lr, N(x) = (N0 + dN) EAr with the propagated indicator.
Reactions: R = N- - N+ - P at a rigid restraint or spring (jump), one adjoint per restraint.
Gate (same rules as bending): u relative to S_u, N and reactions relative to max(|value|, S_N);
piece gate with the closed-form lower bound for |N| (variant C). Scales from computed values.
Usage: python3 state/axial_quality.py"""
import math, os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from axial import AxialSolver, AxialExact, Mechanism, stations
from state import pow2, gamma, U, ETA
from zero import axial_case, axial_points

TAU = 1e-8


class AxialQ(AxialSolver):
    def __init__(s, c, max_iter=5):
        s.c = c
        super().__init__(c, max_iter)
        s.counts()
        s.indicator()

    def loadabs(s, j, x, side):
        """(|dN| terms, k_N, |du| terms, k_u) of segment j up to x."""
        a, ea, pts, dist = s.seg[j]
        aN = au = 0.
        m = 0
        for p, v in pts:
            if x > p or (x == p and side == 'R'):
                aN += abs(v); au += abs(v * ((x - p) / s.Lr)); m += 1
        for qa, qb, v in dist:
            if x <= qa:
                continue
            near = (min(x, qb) - qa) / s.Lr
            aN += abs(v * near)
            au += abs(v * near * near / 2) if x <= qb else abs(v * near * (near / 2 + (x - qb) / s.Lr))
            m += 1
        return aN, 2 + m, au, 4 + m

    def counts(s):
        n = s.n
        s.K = [[0] * n for _ in range(n)]
        s.bab, s.kb = [0.] * n, [0] * n
        col = {k: i for i, k in enumerate(s.free)}
        rigid = {float(x) for x in s.c.get('ax', [])}
        nspr = {}
        for x, k in s.c.get('axs', []):
            nspr[float(x)] = nspr.get(float(x), 0) + 1
        r = 0
        for i, x in enumerate(s.st):
            o = 2 * i
            Pab = sum(abs(v) for p, v in s.px if p == x) / s.EAr
            nP = sum(1 for p, v in s.px if p == x)
            if i > 0:
                j = i - 1
                a, ea, *_ = s.seg[j]
                aN, kN, au, ku = s.loadabs(j, x, 'L')
                if o - 1 in col:
                    s.K[r][col[o - 1]] = 3
                s.bab[r], s.kb[r] = au / ea, ku + 2
                r += 1
                if x not in rigid:
                    if o in col and nspr.get(x):
                        s.K[r][col[o]] = nspr[x]
                    s.bab[r], s.kb[r] = aN + Pab, kN + nP + 1
                    r += 1
            elif x not in rigid:
                if o in col and nspr.get(x):
                    s.K[r][col[o]] = nspr[x]
                s.bab[r], s.kb[r] = Pab, nP
                r += 1

    def solve(s, max_iter):
        A, b, n = s.A, s.b, s.n
        rs, cs = [1.] * n, [1.] * n
        for _ in range(8):
            for i in range(n):
                mm = max(abs(rs[i] * A[i][j] * cs[j]) for j in range(n))
                if mm > 0:
                    rs[i] /= pow2(math.sqrt(mm))
            for j in range(n):
                mm = max(abs(rs[i] * A[i][j] * cs[j]) for i in range(n))
                if mm > 0:
                    cs[j] /= pow2(math.sqrt(mm))
        s.rs, s.cs = rs, cs
        S = s.S = [[rs[i] * A[i][j] * cs[j] for j in range(n)] for i in range(n)]
        s.sb = [rs[i] * b[i] for i in range(n)]
        LU = [r[:] for r in S]
        piv = list(range(n))
        for k in range(n):
            p = max(range(k, n), key=lambda i: abs(LU[i][k]))
            if LU[p][k] == 0:
                raise ZeroDivisionError('zero pivot')
            LU[k], LU[p] = LU[p], LU[k]
            piv[k], piv[p] = piv[p], piv[k]
            for i in range(k + 1, n):
                f = LU[i][k] / LU[k][k]
                LU[i][k] = f
                for jj in range(k + 1, n):
                    LU[i][jj] -= f * LU[k][jj]
        s.LU, s.piv = LU, piv
        y = s.lu(s.sb)
        prev = math.inf
        for _ in range(max_iter):
            r = s.resid(y)[0]
            dy = s.lu(r)
            y = [y[i] + dy[i] for i in range(n)]
            nd, ny = max(map(abs, dy), default=0.), max(map(abs, y), default=0.)
            if nd <= 2 * U * ny or nd > 0.5 * prev:
                break
            prev = nd
        s.y = y
        s.res, s.reserr = s.resid(y)
        x = [0.] * (2 * (s.N + 1))
        for i, k in enumerate(s.free):
            x[k] = cs[i] * y[i]
        s.x = x

    def resid(s, y):
        n = s.n
        r, e = [0.] * n, [0.] * n
        for i in range(n):
            acc = s.sb[i]
            nz = 0
            ab = abs(s.sb[i])
            for j in range(n):
                if s.S[i][j] != 0:
                    acc -= s.S[i][j] * y[j]
                    ab += abs(s.S[i][j] * y[j])
                    nz += 1
            r[i], e[i] = acc, gamma(nz + 1) * ab
        return r, e

    def lu(s, v):
        n, LU = s.n, s.LU
        z = [v[s.piv[i]] for i in range(n)]
        for i in range(n):
            z[i] -= sum(LU[i][j] * z[j] for j in range(i))
        for i in reversed(range(n)):
            z[i] = (z[i] - sum(LU[i][j] * z[j] for j in range(i + 1, n))) / LU[i][i]
        return z

    def lut(s, c):
        n, LU = s.n, s.LU
        w = c[:]
        for i in range(n):
            w[i] = (w[i] - sum(LU[j][i] * w[j] for j in range(i))) / LU[i][i]
        for i in reversed(range(n)):
            w[i] -= sum(LU[j][i] * w[j] for j in range(i + 1, n))
        z = [0.] * n
        for i in range(n):
            z[s.piv[i]] = w[i]
        return z

    def adjoint_vec(s, v):
        z = s.lut(v)
        r = [v[j] - sum(s.S[i][j] * z[i] for i in range(s.n)) for j in range(s.n)]
        dz = s.lut(r)
        return [a + b for a, b in zip(z, dz)]

    def indicator(s):
        n, S, y = s.n, s.S, s.y
        d = []
        for i in range(n):
            nz = [j for j in range(n) if S[i][j] != 0]
            data = sum(gamma(s.K[i][j] + 1) * abs(S[i][j] * y[j]) for j in nz) + gamma(s.kb[i] + 1) * abs(s.rs[i]) * s.bab[i]
            data += (sum(s.K[i][j] + 2 for j in nz) + s.kb[i] + len(nz) + 2) * ETA * max(abs(s.rs[i]), 1.)
            d.append(abs(s.res[i]) + s.reserr[i] + data)
        s.d = d
        s.col = {k: i for i, k in enumerate(s.free)}
        s.e = [0.] * (2 * (s.N + 1))
        for i in range(n):
            z = s.adjoint_vec([s.cs[i] if k == i else 0. for k in range(n)])
            s.e[s.free[i]] = sum(abs(zz) * dd for zz, dd in zip(z, d))

    def functional_beta(s, c):
        v = [0.] * s.n
        for k, ck in c.items():
            if k in s.col:
                v[s.col[k]] += ck * s.cs[s.col[k]]
        if not any(v):
            return 0.
        z = s.adjoint_vec(v)
        return sum(abs(zz) * dd for zz, dd in zip(z, s.d))

    def qstate(s, x, side):
        """(u, N) and their indicators in SI."""
        if x in s.st:
            i = s.st.index(x)
            if side == 'R' and i < s.N:
                return [s.x[2 * i] * s.Lr, s.x[2 * i + 1] * s.EAr], [s.e[2 * i] * s.Lr, s.e[2 * i + 1] * s.EAr]
            j = i - 1
        else:
            j = max(k for k in range(s.N) if s.st[k] <= x)
        a, ea, *_ = s.seg[j]
        xl = (x - s.st[j]) / s.Lr
        dN, du = s.loadint(j, x, side)
        aN, kN, au, ku = s.loadabs(j, x, side)
        u0, N0 = s.x[2 * j], s.x[2 * j + 1]
        e0, eN = s.e[2 * j], s.e[2 * j + 1]
        u = (u0 + (N0 * xl + du) / ea) * s.Lr
        N = (N0 + dN) * s.EAr
        bu = ((e0 + xl / ea * eN) * (1 + gamma(4)) + gamma(6) * (abs(u0) + abs(N0 * xl / ea)) + gamma(ku + 4) * au / ea + 20 * ETA) * s.Lr
        bN = (eN * (1 + gamma(2)) + gamma(2) * abs(N0) + gamma(kN + 2) * aN + 10 * ETA) * s.EAr
        return [u, N], [bu, bN]

    def reactions(s):
        """[(x, R, beta)] for rigid restraints and springs: R = N- - N+ - P (rightwards positive)."""
        out = []
        for x in sorted({float(v) for v in s.c.get('ax', [])} | {float(v) for v, _ in s.c.get('axs', [])}):
            i = s.st.index(x)
            c = {}
            if i < s.N:
                c[2 * i + 1] = -1.
            const = 0.
            cab = 0.
            if i > 0:
                lu_, lN, cu, cN = s.left(i)
                for k, v in lN.items():
                    c[k] = c.get(k, 0.) + v
                const = cN
                cab = s.loadabs(i - 1, x, 'L')[0]
            P = sum(v for p, v in s.px if p == x)
            R = (sum(ck * s.x[k] for k, ck in c.items()) + const) * s.EAr - P
            absum = (sum(abs(ck * s.x[k]) for k, ck in c.items()) + cab) * s.EAr + abs(P)
            b = s.functional_beta(c) * s.EAr * (1 + gamma(4)) + gamma(len(c) + 6) * absum
            out.append((x, R, b))
        return out


def exact_reactions(ex):
    return {float(a): float(ex.sol[1 + r]) for r, (a, k) in enumerate(ex.res)}


def gate_and_error(c, m, ex, pts):
    got = [m.qstate(x, sd) for x, sd in pts]
    vals = [g[0] for g in got]
    Fx = sum(abs(v) for _, v in c['px']) + sum(abs(v) * (b - a) for a, b, v in c['qx'])
    EAmin = min([c['E'] * c['A']] + [v for _, v in c['ea']])
    SN = max(max(abs(v[1]) for v in vals), Fx)
    Su = max(max(abs(v[0]) for v in vals), SN * c['L'] / EAmin)
    if SN == 0:
        return None
    exact = [ex.state(x, sd) for x, sd in pts]
    SNe = max(max(abs(float(e[1])) for e in exact), Fx)
    Sue = max(max(abs(float(e[0])) for e in exact), SNe * c['L'] / EAmin)
    gate = err = 0.
    under = 0
    for (v, b), e in zip(got, exact):
        gate = max(gate, b[0] / Su, b[1] / max(abs(v[1]), SN))
        eu, eN = float(abs(Fr(v[0]) - e[0])), float(abs(Fr(v[1]) - e[1]))
        err = max(err, eu / Sue, eN / max(abs(float(e[1])), SNe))
        under += (eu > b[0]) + (eN > b[1])
    exr = exact_reactions(ex)
    for x, R, b in m.reactions():
        gate = max(gate, b / max(abs(R), SN))
        er = float(abs(Fr(R) - Fr(exr[x])))
        err = max(err, er / max(abs(exr[x]), SNe))
        under += er > b
    # piece gate, variant C (N linear in a piece: |N(x) - N_end| <= h * sum |n|)
    ev = sorted({x for x, _ in pts})
    for x0, x1 in zip(ev, ev[1:]):
        (v0, _), (v1, b1) = m.qstate(x0, 'R'), m.qstate(x1, 'L')
        nsum = sum(abs(v) for a, b, v in c['qx'] if a <= x0 and b >= x1)
        lb = (max(abs(v0[1]), abs(v1[1])) - (x1 - x0) * nsum - b1[1]) * (1 - gamma(8))
        gate = max(gate, b1[0] / Su, b1[1] / max(SN, lb))
    return gate, err, under


if __name__ == '__main__':
    tot = dict(models=0, delivered=0, rejected=0, singular=0, under=0, wrong=0, unnecessary=0)
    worst_gate = worst_err = 0.
    for r0 in (1e-2, 1e-6, 1e-11):
        rng = random.Random(int(1 / r0) % 977 + 5)
        gates, errs = [], []
        for _ in range(300):
            c = axial_case(rng, r0)
            if not c['ax'] and not c['axs']:
                continue
            try:
                ex = AxialExact(c)
            except Mechanism:
                continue
            tot['models'] += 1
            try:
                m = AxialQ(c)
            except ZeroDivisionError:
                tot['singular'] += 1
                continue
            o = gate_and_error(c, m, ex, axial_points(c))
            if o is None:
                continue
            g, e, u = o
            gates.append(g); errs.append(e)
            tot['under'] += u
            if g <= TAU:
                tot['delivered'] += 1
                tot['wrong'] += e > TAU
                worst_err = max(worst_err, e)
            else:
                tot['rejected'] += 1
                tot['unnecessary'] += e <= TAU
            worst_gate = max(worst_gate, g)
        ratio = sorted(g / e for g, e in zip(gates, errs) if e > 0)
        print('axial L/%.0e: models %d | gate median %.1e max %.1e | error max %.1e | gate/error p1 %.1f median %.1f' % (
            1 / r0, len(gates), sorted(gates)[len(gates) // 2], max(gates), max(errs), ratio[len(ratio) // 100], ratio[len(ratio) // 2]))
    print('totals: %s | largest gate %.1e | largest delivered error %.1e' % (tot, worst_gate, worst_err))
    # extreme cases: very near restraints with a large internal force, a stiff and a soft spring, underflow
    L, E, A = 4., 210e9, 3e-3
    for nm, c in (
            ('restraints 1e-11 L apart', dict(L=L, E=E, A=A, ax=[2.0, 2.0 + 4e-11], axs=[], ea=[], px=[[1.0, 1e4], [3.0, -2e4]], qx=[])),
            ('stiff + soft spring', dict(L=L, E=E, A=A, ax=[], axs=[[0.0, 1e20], [L, 1e-6]], ea=[[L / 2, E * A * 1e-4]], px=[[L / 3, 5e3]], qx=[[0.5, 3.5, -1e3]])),
            ('short load 1e-11 L', dict(L=L, E=E, A=A, ax=[0.0], axs=[], ea=[], px=[], qx=[[2.0, 2.0 + 4e-11, 1e14]]))):
        ex = AxialExact(c)
        m = AxialQ(c)
        pts = axial_points(c)
        g, e, u = gate_and_error(c, m, ex, pts)
        print('  %-26s gate %.1e error %.1e values with error > beta %d' % (nm, g, e, u))
