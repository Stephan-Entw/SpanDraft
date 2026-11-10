"""Local state formulation (multiple shooting) of a straight Euler-Bernoulli beam in IEEE double.

Research implementation for docs/SOLVER_ARCHITEKTUR.md, not production code. Input is the case
dictionary of ../transfer_ref.py (same sign conventions: EI w'' = M, M' = V, V' = q, a force adds +P
to V, a couple adds -C to M).

Target formulation (options at their defaults):
- Mechanical stations: 0, L, supports, springs, hinges, changes of EI. Load positions are no
  stations of the system; loads act through closed-form integrals in local segment coordinates.
- Unknowns: the free components of the right-side state (w, th, M, V) at every mechanical station.
  Prescribed components (w = 0, th = 0, M = 0 right of a hinge, M = V = 0 right of x = L) are known
  values and no unknowns.
- Reactions follow afterwards as jumps.
- Scaling chain: powers of two for length and EI (exact) -> Ruiz equilibration with powers of two
  (exact) -> LU with partial pivoting -> refinement -> error indicator from adjoint solves.

Variants (for the ablation only): prescribed='rows-local' keeps prescribed components as unknowns
with a one-entry row; prescribed='rows-propagated' imposes them on the propagated expression;
reactions='unknown' makes reactions, restraint couples, hinge rotation jumps and spring forces
unknowns (the earlier naive multiple shooting).
"""
import math
from fractions import Fraction as Fr

U = 2.0 ** -53
ETA = 2.0 ** -1074          # absolute error of an operation that underflows (gradual underflow)


def gamma(k):
    return k * U / (1 - k * U)


def pow2(v):
    return 2.0 ** round(math.log2(abs(v)))


# ---------------------------------------------------------------------------------------------
# Rounding counts. Each coefficient is formed by a fixed formula; k is an upper bound of the
# relative rounding steps along any path (positive terms only), so |fl(c) - c| <= gamma(k)|c|.
# a = fl(t1 - t0) / Lref: 1 (division by a power of two is exact). e = fl(E*I) / EIref: 1.
K_A = 1
K_E = 1
K_A_OVER_E = K_A + K_E + 1                 # a / e
K_A2_OVER_2E = 2 * K_A + 1 + K_E + 1       # a*a / 2 / e
K_A3_OVER_6E = 3 * K_A + 2 + 1 + K_E + 1   # a*a*a / 6 / e
# Every distance is formed directly from the stored global coordinates, d = fl(x - c) / Lref
# (1 rounding relative to d itself), never as a difference of rounded local coordinates.
# Point force v*d^(p-1)/g: d 1, power (p-1)+(p-2), product 1, division by 6 1.
K_POINT = {1: 0, 2: 2, 3: 5, 4: 8}
# Point couple -v*d^(p-2)/g: d 1, power, product, division by 2 exact.
K_COUPLE = {2: 0, 3: 2, 4: 4}
# Distributed terms (Difference/Ramp in global distances incl. clipping of the intensities).
K_DIST = 40


def Difference(x, a, b, p):
    """<x-a>^p - <x-b>^p, every term positive beyond b (from SupportSpanSolver.Actions)."""
    if x <= a:
        return 0.0
    near = x - a
    if x <= b:
        return near ** p
    far = x - b
    d = b - a
    return {1: d, 2: d * (near + far), 3: d * (near * near + near * far + far * far),
            4: d * (near + far) * (near * near + far * far)}[p]


def Ramp(x, a, b, rise, p):
    """p-th integral of a ramp 0..rise on [a, b] (from SupportSpanSolver.Actions)."""
    if x <= a:
        return 0.0
    d = b - a
    if x <= b:
        n = x - a
        return rise / d * {1: n * n / 2, 2: n ** 3 / 6, 3: n ** 4 / 24, 4: n ** 5 / 120}[p]
    f = x - b
    return rise * d * {1: .5, 2: (d + 3 * f) / 6, 3: (d * d + 4 * d * f + 6 * f * f) / 24,
                       4: (d ** 3 + 5 * d * d * f + 10 * d * f * f + 10 * f ** 3) / 120}[p]


FACT = {1: 1, 2: 2, 3: 6, 4: 24}

# ---------------------------------------------------------------------------------------------
# Residual variants


def two_sum(a, b):
    s = a + b
    bb = s - a
    return s, (a - (s - bb)) + (b - bb)


def split(a):
    c = 134217729.0 * a
    hi = c - (c - a)
    return hi, a - hi


def two_prod(a, b):
    p = a * b
    ah, al = split(a)
    bh, bl = split(b)
    return p, ((ah * bh - p) + ah * bl + al * bh) + al * bl


def dot2(pairs, start):
    """Compensated start + sum(a*b) (Ogita-Rump-Oishi Dot2)."""
    s, c = start, 0.0
    for a, b in pairs:
        p, e = two_prod(a, b)
        s, f = two_sum(s, p)
        c += e + f
    return s + c


class StateSolver:
    def __init__(s, c, prescribed='eliminate', reactions='derived', equilibrate=True, nondim=True,
                 refine='adaptive', residual='double', max_iter=5, adj_refine='one', underflow=True):
        s.opt = dict(prescribed=prescribed, reactions=reactions, equilibrate=equilibrate, nondim=nondim,
                     refine=refine, residual=residual, adj_refine=adj_refine, underflow=underflow)
        s.max_iter = max_iter
        L = float(c['L'])
        s.L = L
        EI0 = float(c['E']) * float(c['I'])
        eis = [(0.0, EI0)] + [(float(x), float(v)) for x, v in c.get('ei', [])]
        s.Lr = pow2(L) if nondim else 1.0
        s.EIr = pow2(max(e for _, e in eis)) if nondim else 1.0
        Lr, EIr = s.Lr, s.EIr
        # nondimensional scale factors (exact powers of two): w/Lr, M*Lr/EIr, V*Lr^2/EIr, P*Lr^2/EIr
        s.f_w, s.f_M, s.f_V = 1 / Lr, Lr / EIr, Lr * Lr / EIr
        s.h = sorted(set(float(x) for x in c.get('hinges', [])))
        tr, ro = [], []
        for a, t in c['sup']:
            tr.append((float(a), math.inf))
            if t == 'Fixed':
                ro.append((float(a), math.inf))
        for x, kw, kt, _ in c.get('springs', []):
            tr.append((float(x), math.inf if kw == 'inf' else float(kw)))
            if kt == 'inf':
                ro.append((float(x), math.inf))
            elif kt > 0:
                ro.append((float(x), float(kt)))
        s.tr, s.ro = tr, ro
        st = sorted({0.0, L} | {x for x, _ in tr} | {x for x, _ in ro} | set(s.h)
                    | {x for x, _ in eis[1:] if 0 < x < L})
        s.st = st
        N = len(st) - 1
        s.N = N
        s.pf = [(float(a), float(v)) for a, v in c['pf']]
        s.pm = [(float(a), float(v)) for a, v in c['pm']]
        lin = [(float(a), float(b), float(v), float(v)) for a, b, v in c['q']] + \
              [tuple(map(float, l)) for l in c.get('lin', [])]
        s.seg = []
        for j in range(N):
            a, b = st[j], st[j + 1]
            # the section of [a, b] is the last change at or before a: exact comparison of stored
            # coordinates, never a computed interior point (a midpoint of a 1-ulp segment rounds)
            EI = [e for x, e in eis if x <= a][-1]
            pts = [(x, v * s.f_V) for x, v in s.pf if a < x < b]          # global positions
            mom = [(x, v * s.f_M) for x, v in s.pm if a < x < b]
            lins = []
            for la, lb, v1, v2 in lin:
                ca, cb = max(la, a), min(lb, b)
                if cb <= ca:
                    continue
                rise = v2 - v1
                qa = v1 if ca == la else v1 + rise * ((ca - la) / (lb - la))
                qb = v2 if cb == lb else v1 + rise * ((cb - la) / (lb - la))
                fq = Lr ** 3 / EIr
                lins.append((ca, cb, qa * fq, qb * fq))
            s.seg.append(((b - a) / Lr, EI / EIr, pts, mom, lins))
        s.build()
        s.solve(max_iter)

    # -----------------------------------------------------------------------------------------
    def loadint(s, j, x, side):
        """Nondimensional load integrals inside segment j at the global position x: per (EIw, EIth,
        M, V) the value, the sum of absolute terms and the rounding count bound."""
        a, e, pts, mom, lins = s.seg[j]
        Lr = s.Lr
        out = []
        for p, g in ((4, 6), (3, 2), (2, 1), (1, 1)):
            val = ab = 0.0
            m = 0
            kmax = 0
            for cpos, v in pts:
                if x > cpos or (p == 1 and x == cpos and side == 'R'):
                    t = v * ((x - cpos) / Lr) ** (p - 1) / g
                    val += t
                    ab += abs(t)
                    m += 1
                    kmax = max(kmax, K_POINT[p])
            for cpos, v in mom:
                if p == 1:
                    continue
                if x > cpos or (p == 2 and x == cpos and side == 'R'):
                    t = -v * ((x - cpos) / Lr) ** (p - 2) / {4: 2, 3: 1, 2: 1}[p]
                    val += t
                    ab += abs(t)
                    m += 1
                    kmax = max(kmax, K_COUPLE[p])
            for ca, cb, qa, qb in lins:
                t1 = qa * Difference(x / Lr, ca / Lr, cb / Lr, p) / FACT[p]
                t2 = Ramp(x / Lr, ca / Lr, cb / Lr, qb - qa, p)
                val += t1 + t2
                ab += abs(t1) + abs(t2)
                m += 2
                kmax = max(kmax, K_DIST)
            out.append((val, ab, kmax + m))
        return out

    def left(s, i):
        """Propagated state left of station i: per component (coef dict col->(value, k), const,
        const_abs, const_k)."""
        if i == 0:
            return [({0: (1.0, 0)}, 0., 0., 0), ({1: (1.0, 0)}, 0., 0., 0), ({}, 0., 0., 0), ({}, 0., 0., 0)]
        j = i - 1
        a, e, *_ = s.seg[j]
        o = 4 * j
        P = s.loadint(j, s.st[i], 'L')
        return [({o: (1.0, 0), o + 1: (a, K_A), o + 2: (a * a / 2 / e, K_A2_OVER_2E), o + 3: (a * a * a / 6 / e, K_A3_OVER_6E)},
                 P[0][0] / e, P[0][1] / e, P[0][2] + K_E + 1),
                ({o + 1: (1.0, 0), o + 2: (a / e, K_A_OVER_E), o + 3: (a * a / 2 / e, K_A2_OVER_2E)},
                 P[1][0] / e, P[1][1] / e, P[1][2] + K_E + 1),
                ({o + 2: (1.0, 0), o + 3: (a, K_A)}, P[2][0], P[2][1], P[2][2]),
                ({o + 3: (1.0, 0)}, P[3][0], P[3][1], P[3][2])]

    def build(s):
        N, st, opt = s.N, s.st, s.opt
        nst = 4 * (N + 1)
        cols = list(range(nst))
        extra = []                      # reaction-type unknowns of the 'unknown' variant
        rows = []                       # (dict col->(value,k), rhs, rhs_abs, rhs_k)
        prescribed = set()

        def add(d, col, v, k):
            if col in d:
                d[col] = (d[col][0] + v, max(d[col][1], k) + 1)
            else:
                d[col] = (v, k)

        def newcol():
            cols.append(len(cols))
            extra.append(len(cols) - 1)
            return len(cols) - 1

        s.reaction_cols = {}
        s.rowidx = {}
        for i, x in enumerate(st):
            sm = s.left(i)
            pfs = [v * s.f_V for a, v in s.pf if a == x]
            pms = [v * s.f_M for a, v in s.pm if a == x]
            Pst, Pab = sum(pfs), sum(abs(v) for v in pfs)
            Cst, Cab = sum(pms), sum(abs(v) for v in pms)
            kP, kC = len(pfs), len(pms)
            ktr = [k for a, k in s.tr if a == x]
            kro = [k for a, k in s.ro if a == x]
            hinge = x in s.h
            rigidw = any(k == math.inf for k in ktr)
            kw = sum(k for k in ktr if k != math.inf) * (s.Lr ** 3 / s.EIr)
            rigidt = any(k == math.inf for k in kro)
            kt = sum(k for k in kro if k != math.inf) * (s.Lr / s.EIr)
            o = 4 * i
            interior = i > 0

            def jump(q, const, cab, kc, extra_terms=()):
                s.rowidx[(i, q)] = len(rows)        # station row of component q (spring rows: spring law)
                d = {}
                add(d, o + q, 1.0, 0)
                for col, (v, k) in sm[q][0].items():
                    add(d, col, -v, k)
                for col, v, k in extra_terms:
                    add(d, col, v, k)
                rows.append((d, sm[q][1] + const, sm[q][2] + cab, max(sm[q][3], kc) + 1))

            def prescribe(q, end=False):
                if opt['prescribed'] == 'eliminate':
                    prescribed.add(o + q)
                elif opt['prescribed'] == 'rows-local' or i == 0 or end:
                    rows.append(({o + q: (1.0, 0)}, 0., 0., 0))
                else:   # rows-propagated: the condition acts on the expression left of the station
                    rows.append((dict(sm[q][0]), -sm[q][1], sm[q][2], sm[q][3]))

            if opt['reactions'] == 'unknown':
                s.build_unknown_station(i, x, sm, rows, add, newcol, o, Pst, Pab, kP, Cst, Cab, kC,
                                        ktr, kro, hinge, rigidw, rigidt)
                continue
            # w
            if interior:
                jump(0, 0., 0., 0)
            if rigidw:
                prescribe(0)
            # theta
            if hinge:
                prescribe(2)
            elif interior:
                jump(1, 0., 0., 0)
            # M
            if rigidt:
                prescribe(1)
            else:
                jump(2, -Cst, Cab, kC, [(o + 1, -kt, 1)] if kt else ())
            # V
            if not rigidw:
                jump(3, Pst, Pab, kP, [(o, kw, 1)] if kw else ())
            if i == N:          # nothing acts right of the beam end
                prescribe(2, end=True)
                prescribe(3, end=True)
        s.nfull = len(cols)
        free = [k for k in cols if k not in prescribed]
        col = {k: n for n, k in enumerate(free)}
        n = len(free)
        if len(rows) != n:
            raise RuntimeError('rows %d unknowns %d' % (len(rows), n))
        A = [[0.] * n for _ in range(n)]
        K = [[0] * n for _ in range(n)]
        b, bab, kb = [0.] * n, [0.] * n, [0] * n
        for r, (d, rhs, ra, rk) in enumerate(rows):
            for kx, (v, k) in d.items():
                if kx in col:
                    A[r][col[kx]] += v
                    K[r][col[kx]] = k
            b[r], bab[r], kb[r] = rhs, ra, rk
        s.free, s.n, s.A, s.K, s.b, s.bab, s.kb = free, n, A, K, b, bab, kb

    def build_unknown_station(s, i, x, sm, rows, add, newcol, o, Pst, Pab, kP, Cst, Cab, kC,
                              ktr, kro, hinge, rigidw, rigidt):
        """Naive variant: every reaction is an unknown, conditions act on the propagated state."""
        N = s.N
        Rcols = []
        for k in ktr:
            cR = newcol()
            Rcols.append(cR)
            d = dict(sm[0][0])
            if k != math.inf:
                d[cR] = (1.0 / (k * s.Lr ** 3 / s.EIr), 1)
            rows.append((d, -sm[0][1], sm[0][2], sm[0][3]))
        Ccols = []
        for k in kro:
            cC = newcol()
            Ccols.append(cC)
            d = dict(sm[1][0])
            if k != math.inf:
                d[cC] = (1.0 / (k * s.Lr / s.EIr), 1)
            rows.append((d, -sm[1][1], sm[1][2], sm[1][3]))
        Dcol = None
        if hinge:
            Dcol = newcol()
            rows.append((dict(sm[2][0]), -sm[2][1], sm[2][2], sm[2][3]))
        s.reaction_cols[x] = (Rcols, Ccols)
        jumps = {0: [], 1: [(Dcol, -1.0)] if Dcol is not None else [], 2: [(c, 1.0) for c in Ccols],
                 3: [(c, -1.0) for c in Rcols]}
        const = {0: 0., 1: 0., 2: -Cst, 3: Pst}
        cab = {0: 0., 1: 0., 2: Cab, 3: Pab}
        for q in (range(4) if i > 0 else (2, 3)):
            d = {}
            add(d, o + q, 1.0, 0)
            for col, (v, k) in sm[q][0].items():
                add(d, col, -v, k)
            for col, v in jumps[q]:
                add(d, col, v, 0)
            rows.append((d, sm[q][1] + const[q], sm[q][2] + cab[q], sm[q][3] + 1))
        if i == N:
            rows.append(({o + 2: (1.0, 0)}, 0., 0., 0))
            rows.append(({o + 3: (1.0, 0)}, 0., 0., 0))

    # -----------------------------------------------------------------------------------------
    def residual(s, y):
        """Residual sb - S y of the scaled system and an upper bound of its rounding error."""
        S, sb, n = s.S, s.sb, s.n
        r, err = [0.] * n, [0.] * n
        for i in range(n):
            pairs = [(S[i][j], y[j]) for j in s.nz[i]]
            absum = sum(abs(a * v) for a, v in pairs) + abs(sb[i])
            if s.opt['residual'] == 'double':
                acc = sb[i]
                for a, v in pairs:
                    acc -= a * v
                r[i] = acc
                err[i] = gamma(len(pairs) + 1) * absum
            elif s.opt['residual'] == 'compensated':
                r[i] = dot2([(-a, v) for a, v in pairs], sb[i])
                err[i] = U * abs(r[i]) + gamma(len(pairs) + 1) ** 2 * absum
            else:   # exact
                ex = Fr(sb[i]) - sum(Fr(a) * Fr(v) for a, v in pairs)
                r[i] = float(ex)
                err[i] = U * abs(r[i])
        return r, err

    def solve(s, max_iter):
        A, b, n = s.A, s.b, s.n
        rs, cs = [1.] * n, [1.] * n
        if s.opt['equilibrate']:
            for _ in range(8):
                for i in range(n):
                    m = max(abs(rs[i] * A[i][j] * cs[j]) for j in range(n))
                    if m > 0:
                        rs[i] = rs[i] / pow2(math.sqrt(m))
                for j in range(n):
                    m = max(abs(rs[i] * A[i][j] * cs[j]) for i in range(n))
                    if m > 0:
                        cs[j] = cs[j] / pow2(math.sqrt(m))
        s.rs, s.cs = rs, cs
        s.S = [[rs[i] * A[i][j] * cs[j] for j in range(n)] for i in range(n)]
        s.sb = [rs[i] * b[i] for i in range(n)]
        s.nz = [[j for j in range(n) if s.S[i][j] != 0] for i in range(n)]
        LU = [row[:] for row in s.S]
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
                if f != 0:
                    rk, ri = LU[k], LU[i]
                    for j in range(k + 1, n):
                        ri[j] -= f * rk[j]
        s.LU, s.piv = LU, piv
        y = s.lusolve(s.sb)
        s.iterations = 0
        s.history = []
        if s.opt['refine'] != 'none':
            limit = 1 if s.opt['refine'] == 'one' else max_iter
            prev = math.inf
            for it in range(limit):
                r, _ = s.residual(y)
                dy = s.lusolve(r)
                y = [y[i] + dy[i] for i in range(n)]
                s.iterations += 1
                nd = max(map(abs, dy)) if dy else 0.
                ny = max(map(abs, y)) if y else 0.
                s.history.append(nd / ny if ny else 0.)
                if nd <= 2 * U * ny or nd > 0.5 * prev:   # converged or stagnating
                    break
                prev = nd
        s.y = y
        s.res, s.reserr = s.residual(y)
        x = [cs[i] * y[i] for i in range(n)]
        s.x = [0.] * s.nfull
        for i, k in enumerate(s.free):
            s.x[k] = x[i]
        den = [sum(abs(s.S[i][j] * y[j]) for j in s.nz[i]) + abs(s.sb[i]) for i in range(n)]
        s.omega = max([abs(s.res[i]) / den[i] for i in range(n) if den[i] > 0] + [0.])

    def lusolve(s, v):
        n, LU = s.n, s.LU
        z = [v[s.piv[i]] for i in range(n)]
        for i in range(n):
            z[i] -= sum(LU[i][j] * z[j] for j in range(i))
        for i in reversed(range(n)):
            z[i] = (z[i] - sum(LU[i][j] * z[j] for j in range(i + 1, n))) / LU[i][i]
        return z

    def lusolve_transposed(s, c):
        """z with S^T z = c, using P S = L U: U^T w = c, L^T v = w, z = P^T v."""
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

    def adjoint_solve(s, c):
        """z with S^T z = c; optionally refined (residual in double).
        Modes: 'none'; 'one' (exactly one step); 'adaptive' (same rule as the primary solution:
        at least one step, continue while the correction halves, stop at |dz| <= 2u|z|, at most
        max_iter); 'beta' (at least one step, continue while the indicator |z|^T d still changes
        by more than 1/16, at most max_iter; needs s.d). s.adj_steps counts the refinement steps."""
        z = s.lusolve_transposed(c)
        s.adj_solves = getattr(s, 'adj_solves', 0) + 1
        mode = s.opt['adj_refine']
        if mode == 'none':
            return z
        n, S = s.n, s.S
        limit = 1 if mode == 'one' else s.max_iter
        prev = math.inf
        bprev = sum(abs(z[j]) * s.d[j] for j in range(n)) if mode == 'beta' else 0.
        for _ in range(limit):
            r = [c[j] - sum(S[i][j] * z[i] for i in range(n) if S[i][j] != 0) for j in range(n)]
            dz = s.lusolve_transposed(r)
            z = [z[i] + dz[i] for i in range(n)]
            s.adj_steps = getattr(s, 'adj_steps', 0) + 1
            if mode == 'beta':
                b = sum(abs(z[j]) * s.d[j] for j in range(n))
                if abs(b - bprev) <= b / 16:
                    break
                bprev = b
                continue
            nd = max(map(abs, dz)) if dz else 0.
            nz = max(map(abs, z)) if z else 0.
            if nd <= 2 * U * nz or nd > 0.5 * prev:
                break
            prev = nd
        return z

    def indicator(s):
        """Componentwise a-posteriori error indicator of every unknown: e_i = |z_i|^T d with the
        adjoint solution S^T z_i = unit_i and d = |r| + residual rounding + data rounding."""
        n, S, y = s.n, s.S, s.y
        d = []
        for i in range(n):
            data = sum(gamma(s.K[i][j] + 1) * abs(S[i][j] * y[j]) for j in s.nz[i])
            data += gamma(s.kb[i] + 1) * abs(s.rs[i]) * s.bab[i]
            if s.opt['underflow']:      # every rounded operation may also lose up to ETA absolutely
                data += (sum(s.K[i][j] + 2 for j in s.nz[i]) + s.kb[i] + len(s.nz[i]) + 2) * ETA * max(abs(s.rs[i]), 1.)
            d.append(abs(s.res[i]) + s.reserr[i] + data)
        s.d = d
        s.col = {k: i for i, k in enumerate(s.free)}
        e = [0.] * s.nfull
        for i in range(n):
            z = s.adjoint_solve([1. if k == i else 0. for k in range(n)])
            e[s.free[i]] = s.cs[i] * sum(abs(z[j]) * d[j] for j in range(n))
        s.e = e
        return e

    # -----------------------------------------------------------------------------------------
    def segof(s, x):
        lo, hi = 0, s.N
        while hi - lo > 1:
            m = (lo + hi) // 2
            if s.st[m] <= x:
                lo = m
            else:
                hi = m
        return lo

    def state(s, x, side, with_indicator=False):
        """(w, th, M, V) in SI at x and side; optionally the error indicator of each value."""
        conv = [1 / s.f_w, 1.0, 1 / s.f_M, 1 / s.f_V]     # exact powers of two
        if x in s.st:
            i = s.st.index(x)
            if side == 'R' and i < s.N:
                v = [s.x[4 * i + q] * conv[q] for q in range(4)]
                if not with_indicator:
                    return v
                return v, [s.e[4 * i + q] * conv[q] for q in range(4)]
            j, xl, sd = i - 1, s.seg[i - 1][0], 'L'
        else:
            j = s.segof(x)
            xl, sd = (x - s.st[j]) / s.Lr, side
        a, e, *_ = s.seg[j]
        o = 4 * j
        X = s.x[o:o + 4]
        P = s.loadint(j, x, sd)
        g = [[1, xl, xl * xl / 2 / e, xl ** 3 / 6 / e], [0, 1, xl / e, xl * xl / 2 / e], [0, 0, 1, xl], [0, 0, 0, 1]]
        kg = [[0, 1, 5, 8], [0, 0, 3, 5], [0, 0, 0, 1], [0, 0, 0, 0]]
        pv = [P[0][0] / e, P[1][0] / e, P[2][0], P[3][0]]
        vals = [(sum(g[q][k] * X[k] for k in range(4)) + pv[q]) * conv[q] for q in range(4)]
        if not with_indicator:
            return vals
        E = s.e[o:o + 4]
        pab = [P[0][1] / e, P[1][1] / e, P[2][1], P[3][1]]
        pk = [P[0][2] + 2, P[1][2] + 2, P[2][2], P[3][2]]
        ind = []
        for q in range(4):
            prop = sum(abs(g[q][k]) * E[k] for k in range(4)) * (1 + gamma(10))
            evaluation = sum(gamma(kg[q][k] + 5) * abs(g[q][k] * X[k]) for k in range(4)) + gamma(pk[q] + 5) * pab[q]
            if s.opt['underflow']:
                evaluation += (sum(kg[q]) + pk[q] + 30) * ETA
            ind.append((prop + evaluation) * conv[q])
        return vals, ind

    def adjoint(s, c):
        """Indicator of the functional y = sum_k c[k] x[k] (k: full state index): |z|^T d with
        S^T z = C c in the scaled system. Captures the correlation of the errors of all terms."""
        v = [0.] * s.n
        for k, ck in c.items():
            if k in s.col:
                i = s.col[k]
                v[i] += ck * s.cs[i]
        if not any(v):
            return 0.
        z = s.adjoint_solve(v)
        return sum(abs(z[j]) * s.d[j] for j in range(s.n))

    def reaction_variants(s, a, q, kspring):
        """Both evaluations of the restraint at a (q = 3: force, q = 2: couple):
        jump: V+ - V- - P (resp. M- - M+ - C) with its adjoint indicator,
        law:  -k w+ (resp. -kt th+) with its indicator (only for 0 < k < inf),
        and the absolute row scale |V+| + |V-| + |P| + |k w+| of the station row both come from."""
        i = s.st.index(a)
        loads = s.pf if q == 3 else s.pm
        sign = 1. if q == 3 else -1.
        c = {}
        if i < s.N:
            c[4 * i + q] = sign
        const = const_abs = 0.
        kc = 0
        if i > 0:
            sm = s.left(i)[q]
            for col, (v, _) in sm[0].items():
                c[col] = c.get(col, 0.) - sign * v
            const, const_abs, kc = -sign * sm[1], sm[2], sm[3]
        conv = 1 / s.f_V if q == 3 else 1 / s.f_M
        Pl = [v for b, v in loads if b == a]
        jump = (sum(ck * s.x[k] for k, ck in c.items()) + const) * conv - sum(Pl)
        absum = sum(abs(ck * s.x[k]) for k, ck in c.items()) * conv + const_abs * conv + sum(map(abs, Pl))
        bj = s.adjoint(c) * conv * (1 + gamma(4)) + gamma(kc + len(c) + len(Pl) + 3) * absum
        out = dict(jump=jump, beta_jump=bj, absum=absum)
        if kspring != math.inf:
            kk = 0 if q == 3 else 1
            vp, ip = s.state(a, 'R', True) if i < s.N else s.state(a, 'L', True)
            out.update(law=-kspring * vp[kk], beta_law=kspring * ip[kk] + gamma(1) * abs(kspring * vp[kk]),
                       rowscale=absum + abs(kspring * vp[kk]))
        return out

    def reactions(s, with_indicator=False):
        """Reaction = jump of V (M) minus the applied load. Its indicator comes from one adjoint
        solve of the jump functional (V+ - V-), not from the sum of the indicators of V+ and V-.
        An elastic restraint has a second, mathematically equivalent evaluation, the spring law
        R = -k w+ (C = -kt th+); of the two the one with the smaller indicator is delivered."""
        rt, rr = [], []
        for kind, items, q, loads in (('R', s.tr, 3, s.pf), ('C', s.ro, 2, s.pm)):
            for a, kspring in items:
                i = s.st.index(a)
                sign = 1. if q == 3 else -1.        # R = V+ - V- - P ; C = M- - M+ - C_applied
                c = {}
                if i < s.N:
                    c[4 * i + q] = sign
                const = const_abs = 0.
                kc = 0
                if i > 0:
                    sm = s.left(i)[q]
                    for col, (v, _) in sm[0].items():
                        c[col] = c.get(col, 0.) - sign * v
                    const, const_abs, kc = -sign * sm[1], sm[2], sm[3]
                conv = 1 / s.f_V if q == 3 else 1 / s.f_M
                Pl = [v for b, v in loads if b == a]
                val = (sum(ck * s.x[k] for k, ck in c.items()) + const) * conv - sum(Pl)
                ind = None
                if with_indicator:
                    absum = sum(abs(ck * s.x[k]) for k, ck in c.items()) * conv + const_abs * conv + sum(map(abs, Pl))
                    ind = s.adjoint(c) * conv * (1 + gamma(4)) + gamma(kc + len(c) + len(Pl) + 3) * absum
                if with_indicator and kspring != math.inf:
                    kk = 0 if q == 3 else 1
                    vp, ip = s.state(a, 'R', True) if i < s.N else s.state(a, 'L', True)
                    law, law_ind = -kspring * vp[kk], kspring * ip[kk] + gamma(1) * abs(kspring * vp[kk])
                    if law_ind < ind:
                        val, ind = law, law_ind
                (rt if q == 3 else rr).append((a, val, ind))
        if not with_indicator:
            return [(a, R) for a, R, _ in rt], [(a, R) for a, R, _ in rr]
        return rt, rr
