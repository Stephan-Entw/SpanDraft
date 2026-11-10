"""Axial part (u, N) in the same local state formulation, and an independent exact reference.

Conventions: x and u rightwards, N > 0 tension, EA u' = N, N' = -n (n: distributed axial load,
positive rightwards), an applied axial point force P (positive rightwards) gives N+ = N- - P.
Case keys used: L, E, A, 'ea' (list of [x, EA from x onwards]), 'ax' (axial restraints: list of x),
'axs' (axial springs [x, k]), 'px' ([x, P]), 'qx' ([a, b, n]).

AxialSolver: unknowns are the free right-side states (u, N) at mechanical stations (restraints,
springs, EA changes, ends); u+ = 0 at a rigid restraint and N+ = 0 at x = L are eliminated; the
spring law is N+ - N- - k u+ = -P on the local state. Ruiz scaling with powers of two, LU,
adaptive refinement. AxialExact: single shooting in Fractions with unknowns u(0) and every
restraint force (independent of the float formulation)."""
import math
from fractions import Fraction as Fr
from state import pow2, U


class Mechanism(Exception):
    pass


def stations(c):
    L = float(c['L'])
    return sorted({0.0, L} | {float(x) for x in c.get('ax', [])} | {float(x) for x, _ in c.get('axs', [])}
                  | {float(x) for x, _ in c.get('ea', []) if 0 < x < L})


class AxialSolver:
    def __init__(s, c, max_iter=5):
        L = float(c['L'])
        EA0 = float(c['E']) * float(c['A'])
        eas = [(0.0, EA0)] + [(float(x), float(v)) for x, v in c.get('ea', [])]
        s.Lr = pow2(L)
        s.EAr = pow2(max(e for _, e in eas))
        st = stations(c)
        s.st, s.N = st, len(st) - 1
        s.px = [(float(a), float(v)) for a, v in c.get('px', [])]
        qx = [(float(a), float(b), float(v)) for a, b, v in c.get('qx', [])]
        rigid = {float(x) for x in c.get('ax', [])}
        spring = {}
        for x, k in c.get('axs', []):
            spring[float(x)] = spring.get(float(x), 0.) + float(k)
        # nondimensional: u/Lr, N/EAr, P/EAr, n*Lr/EAr, k*Lr/EAr
        s.seg = []
        for j in range(s.N):
            a, b = st[j], st[j + 1]
            EA = [e for x, e in eas if x <= a][-1]
            s.seg.append(((b - a) / s.Lr, EA / s.EAr, [(x, v / s.EAr) for x, v in s.px if a < x < b],
                          [(max(qa, a), min(qb, b), v * s.Lr / s.EAr) for qa, qb, v in qx if min(qb, b) > max(qa, a)]))
        rows, presc = [], set()
        for i, x in enumerate(st):
            P = sum(v for p, v in s.px if p == x) / s.EAr
            k = spring.get(x, 0.) * s.Lr / s.EAr
            o = 2 * i
            if i > 0:
                lu, lN, cu, cN = s.left(i)
                rows.append(({o: 1.0, **{c_: -v for c_, v in lu.items()}}, cu))          # u continuous
                if x not in rigid:                                                     # N+ - N- - k u+ = -P
                    d = {o + 1: 1.0, **{c_: -v for c_, v in lN.items()}}
                    if k:
                        d[o] = d.get(o, 0.) - k
                    rows.append((d, cN - P))
            else:
                if x not in rigid:                                                     # N+ - 0 - k u+ = -P
                    d = {o + 1: 1.0}
                    if k:
                        d[o] = -k
                    rows.append((d, -P))
            if x in rigid:
                presc.add(o)
            if i == s.N:
                presc.add(o + 1)
        s.free = [k for k in range(2 * (s.N + 1)) if k not in presc]
        col = {k: n for n, k in enumerate(s.free)}
        n = len(s.free)
        if len(rows) != n:
            raise RuntimeError('rows %d unknowns %d' % (len(rows), n))
        A = [[0.] * n for _ in range(n)]
        bb = [0.] * n
        for r, (d, rhs) in enumerate(rows):
            for kx, v in d.items():
                if kx in col:
                    A[r][col[kx]] += v
            bb[r] = rhs
        s.n, s.A, s.b = n, A, bb
        s.solve(max_iter)

    def loadint(s, j, x, side):
        """(integral of N change, integral for u) of loads in segment j up to global x."""
        a, ea, pts, dist = s.seg[j]
        dN = du = 0.
        for p, v in pts:
            if x > p or (x == p and side == 'R'):
                dN -= v
                du -= v * ((x - p) / s.Lr)
        for qa, qb, v in dist:
            if x <= qa:
                continue
            near = (min(x, qb) - qa) / s.Lr
            dN -= v * near
            if x <= qb:
                du -= v * near * near / 2
            else:
                du -= v * near * (near / 2 + (x - qb) / s.Lr)
        return dN, du

    def left(s, i):
        j = i - 1
        a, ea, *_ = s.seg[j]
        dN, du = s.loadint(j, s.st[i], 'L')
        o = 2 * j
        return {o: 1.0, o + 1: a / ea}, {o + 1: 1.0}, du / ea, dN

    def solve(s, max_iter):
        A, b, n = s.A, s.b, s.n
        rs, cs = [1.] * n, [1.] * n
        for _ in range(8):
            for i in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for j in range(n))
                if m > 0:
                    rs[i] /= pow2(math.sqrt(m))
            for j in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for i in range(n))
                if m > 0:
                    cs[j] /= pow2(math.sqrt(m))
        S = [[rs[i] * A[i][j] * cs[j] for j in range(n)] for i in range(n)]
        sb = [rs[i] * b[i] for i in range(n)]
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

        def sol(v):
            z = [v[piv[i]] for i in range(n)]
            for i in range(n):
                z[i] -= sum(LU[i][jj] * z[jj] for jj in range(i))
            for i in reversed(range(n)):
                z[i] = (z[i] - sum(LU[i][jj] * z[jj] for jj in range(i + 1, n))) / LU[i][i]
            return z
        y = sol(sb)
        prev = math.inf
        for _ in range(max_iter):
            r = [sb[i] - sum(S[i][jj] * y[jj] for jj in range(n)) for i in range(n)]
            dy = sol(r)
            y = [y[i] + dy[i] for i in range(n)]
            nd, ny = max(map(abs, dy), default=0.), max(map(abs, y), default=0.)
            if nd <= 2 * U * ny or nd > 0.5 * prev:
                break
            prev = nd
        x = [0.] * (2 * (s.N + 1))
        for i, k in enumerate(s.free):
            x[k] = cs[i] * y[i]
        s.x = x

    def state(s, x, side):
        """(u, N) in SI."""
        if x in s.st:
            i = s.st.index(x)
            if side == 'R' and i < s.N:
                return [s.x[2 * i] * s.Lr, s.x[2 * i + 1] * s.EAr]
            j = i - 1
        else:
            lo = max(k for k in range(s.N) if s.st[k] <= x)
            j = lo
        a, ea, *_ = s.seg[j]
        xl = (x - s.st[j]) / s.Lr
        dN, du = s.loadint(j, x, side)
        u0, N0 = s.x[2 * j], s.x[2 * j + 1]
        return [(u0 + (N0 * xl + du) / ea) * s.Lr, (N0 + dN) * s.EAr]


class AxialExact:
    """Single shooting in Fractions: unknowns u(0) and every restraint force (rigid or spring)."""

    def __init__(s, c):
        F = lambda v: Fr(float(v))
        s.L = F(c['L'])
        s.eas = [(Fr(0), F(c['E']) * F(c['A']))] + [(F(x), F(v)) for x, v in c.get('ea', [])]
        s.px = [(F(a), F(v)) for a, v in c.get('px', [])]
        s.qx = [(F(a), F(b), F(v)) for a, b, v in c.get('qx', [])]
        s.res = [(F(x), None) for x in c.get('ax', [])] + [(F(x), F(k)) for x, k in c.get('axs', [])]
        s.ev = sorted({Fr(0), s.L} | {x for x, _ in s.res} | {x for x, _ in s.px} | {x for x, _ in s.eas}
                      | {x for a, b, _ in s.qx for x in (a, b)})
        n = 1 + len(s.res)
        rows = []

        def lin(c0=Fr(0), **kw):
            return dict(c=dict(kw.get('c', {})), k=c0)
        st = dict(u={0: Fr(1)}, uk=Fr(0), N={}, Nk=Fr(0))
        rows = []
        s.rows = rows
        st = s.sweep(None, rows)
        # N right of L must vanish
        rows.append((st['N'], -st['Nk']))
        if len(rows) != n:
            raise RuntimeError('rows %d unknowns %d' % (len(rows), n))
        A = [[r[0].get(i, Fr(0)) for i in range(n)] + [r[1]] for r in rows]
        for i in range(n):
            p = next((k for k in range(i, n) if A[k][i] != 0), None)
            if p is None:
                raise Mechanism()
            A[i], A[p] = A[p], A[i]
            for k in range(n):
                if k != i and A[k][i] != 0:
                    f = A[k][i] / A[i][i]
                    A[k] = [a - f * b for a, b in zip(A[k], A[i])]
        s.sol = [A[i][n] / A[i][i] for i in range(n)]

    def EA(s, x):
        return [e for a, e in s.eas if a <= x][-1]

    def sweep(s, stop, rows, side='R'):
        u, uk, N, Nk = {0: Fr(1)}, Fr(0), {}, Fr(0)
        for j, x in enumerate(s.ev):
            if stop is not None and x == stop and side == 'L':
                return dict(u=u, uk=uk, N=N, Nk=Nk)
            for a, v in s.px:
                if a == x:
                    Nk -= v
            for r, (a, k) in enumerate(s.res):
                if a == x:
                    if rows is not None:   # u + R/k = 0 (rigid: u = 0); restraint force R acts like P
                        d = dict(u)
                        if k is not None:
                            d[1 + r] = d.get(1 + r, Fr(0)) + 1 / k
                        rows.append((d, -uk))
                    N[1 + r] = N.get(1 + r, Fr(0)) - 1
            if stop is not None and x == stop:
                return dict(u=u, uk=uk, N=N, Nk=Nk)
            if j == len(s.ev) - 1:
                break
            b = s.ev[j + 1]
            inside = stop is not None and x < stop < b
            if inside:
                b = stop
            ea, t = s.EA(x + (b - x) / 2), b - x
            n0 = sum(v for qa, qb, v in s.qx if qa <= x and qb >= b)
            u = {i: u.get(i, Fr(0)) + N.get(i, Fr(0)) * t / ea for i in set(u) | set(N)}
            uk = uk + (Nk * t - n0 * t * t / 2) / ea
            Nk = Nk - n0 * t
            if inside:            # stop strictly inside the interval: done after the advance
                return dict(u=u, uk=uk, N=N, Nk=Nk)
        return dict(u=u, uk=uk, N=N, Nk=Nk)

    def state(s, x, side):
        st = s.sweep(Fr(x), None, side)
        val = lambda d, k: k + sum(v * s.sol[i] for i, v in d.items())
        return [val(st['u'], st['uk']), val(st['N'], st['Nk'])]
