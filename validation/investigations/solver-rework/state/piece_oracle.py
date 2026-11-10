"""Round 4, phases 3 and 4: exact piece polynomials, Bernstein enclosure and exact root isolation.

Two exact polynomials per piece [x0, x1] between event stations and per quantity (w, th, M, V),
in the local coordinate t = (x - x0)/h on [0, 1], with rational coefficients:
  p_hat  the computed field: the state formula of state.py evaluated in exact rational arithmetic
         with the computed data (solution vector, section data, nondimensional load values).
         The floating-point EvaluateAt differs from p_hat only by its own evaluation rounding.
  p      the exact field of the exact reference transfer_ref.py.
Both are obtained by exact interpolation at 6 rational interior points (degree <= 5); inside a
piece every bracket <x - c>^k has a fixed branch, so the field is a polynomial.

Bernstein (phase 3): exact Bernstein coefficients on [0, 1] (Cargo/Shisha range enclosure
min b_i <= p(t) <= max b_i), exact de Casteljau subdivision at 1/2 with branch and bound for a
certified enclosure [lo, hi] of min |p| on the piece. No floating-point step is involved, so the
enclosure is an oracle for p_hat and for p separately.

Roots (phase 4): exact square-free decomposition (Yun) of the derivative, Sturm sequences of
every square-free factor, bisection on dyadic intervals until each interval holds exactly one
root, multiplicity from the factor index; roots exactly at 0 or 1 are detected by exact
evaluation. Cross-check of the number of distinct roots in [0, 1] and their multiplicities with
sympy (Poly.intervals over QQ, an independent implementation).

PolynomialRoots: Python port of src/SpanDraft.Solver/PolynomialRoots.cs (constants, NearZero,
bisection, quadratic, multiple-root cluster rule, distinct filter) for the candidate search.
"""
import math, os, sys
from fractions import Fraction as Fr
from math import comb
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from state import FACT

DEG = 5
NODES = [Fr(k + 1, DEG + 2) for k in range(DEG + 1)]


# ------------------------------------------------------------------ exact fields on a piece
def difference_exact(x, a, b, p):
    """state.Difference in exact arithmetic (state.Difference returns the float 0.0)."""
    if x <= a:
        return Fr(0)
    near = x - a
    if x <= b:
        return near ** p
    far = x - b
    d = b - a
    return {1: d, 2: d * (near + far), 3: d * (near * near + near * far + far * far),
            4: d * (near + far) * (near * near + far * far)}[p]


def ramp_exact(x, a, b, rise, p):
    """state.Ramp in exact arithmetic (state.Ramp contains the float literal .5)."""
    if x <= a:
        return Fr(0)
    d = b - a
    if x <= b:
        n = x - a
        return rise / d * {1: n * n / 2, 2: n ** 3 / 6, 3: n ** 4 / 24, 4: n ** 5 / 120}[p]
    f = x - b
    return rise * d * {1: Fr(1, 2), 2: (d + 3 * f) / 6, 3: (d * d + 4 * d * f + 6 * f * f) / 24,
                       4: (d ** 3 + 5 * d * d * f + 10 * d * f * f + 10 * f ** 3) / 120}[p]


def loadint_exact(m, j, X, side):
    a, e, pts, mom, lins = m.seg[j]
    Lr = Fr(m.Lr)
    out = []
    for p, g in ((4, 6), (3, 2), (2, 1), (1, 1)):
        val = Fr(0)
        for cpos, v in pts:
            c = Fr(cpos)
            if X > c or (p == 1 and X == c and side == 'R'):
                val += Fr(v) * ((X - c) / Lr) ** (p - 1) / g
        for cpos, v in mom:
            if p == 1:
                continue
            c = Fr(cpos)
            if X > c or (p == 2 and X == c and side == 'R'):
                val += -Fr(v) * ((X - c) / Lr) ** (p - 2) / {4: 2, 3: 1, 2: 1}[p]
        for ca, cb, qa, qb in lins:
            val += Fr(qa) * difference_exact(X / Lr, Fr(ca) / Lr, Fr(cb) / Lr, p) / FACT[p] \
                + ramp_exact(X / Lr, Fr(ca) / Lr, Fr(cb) / Lr, Fr(qb) - Fr(qa), p)
        out.append(val)
    return out


def state_exact_hat(m, X, side='R'):
    """The computed field (state.py formula) at the rational X, in exact arithmetic (SI)."""
    conv = [Fr(1 / m.f_w), Fr(1), Fr(1 / m.f_M), Fr(1 / m.f_V)]
    st = m.st
    j = max(i for i in range(m.N) if Fr(st[i]) <= X) if side == 'R' else max(i for i in range(m.N) if Fr(st[i]) < X)
    a, e, *_ = m.seg[j]
    e = Fr(e)
    s = (X - Fr(st[j])) / Fr(m.Lr)
    Xs = [Fr(m.x[4 * j + k]) for k in range(4)]
    P = loadint_exact(m, j, X, side)
    g = [[1, s, s * s / 2 / e, s ** 3 / 6 / e], [0, 1, s / e, s * s / 2 / e], [0, 0, 1, s], [0, 0, 0, 1]]
    pv = [P[0] / e, P[1] / e, P[2], P[3]]
    return [(sum(g[q][k] * Xs[k] for k in range(4)) + pv[q]) * conv[q] for q in range(4)]


def interpolate(vals):
    """Power coefficients (low to high) of the degree-DEG polynomial through (NODES, vals)."""
    assert all(isinstance(v, Fr) for v in vals), 'inexact value in an exact polynomial'
    n = len(NODES)
    M = [[t ** k for k in range(n)] + [v] for t, v in zip(NODES, vals)]
    for i in range(n):
        p = next(r for r in range(i, n) if M[r][i] != 0)
        M[i], M[p] = M[p], M[i]
        for r in range(n):
            if r != i and M[r][i] != 0:
                f = M[r][i] / M[i][i]
                M[r] = [a - f * b for a, b in zip(M[r], M[i])]
    co = [M[i][n] / M[i][i] for i in range(n)]
    while len(co) > 1 and co[-1] == 0:
        co.pop()
    return co


def piece_polys(m, ex, x0, x1):
    """{q: (p_hat, p)} for q = 0..3 on the piece (x0, x1) (floats), coefficients in t."""
    X0, X1 = Fr(x0), Fr(x1)
    h = X1 - X0
    xs = [X0 + h * t for t in NODES]
    hat = [state_exact_hat(m, X) for X in xs]
    exa = [ex.state(X, 'R') for X in xs]
    keys = ('w', 't', 'M', 'V')
    return {q: (interpolate([v[q] for v in hat]), interpolate([v[keys[q]] for v in exa])) for q in range(4)}


def peval(co, t):
    v = Fr(0)
    for a in reversed(co):
        v = v * t + a
    return v


def deriv(co):
    d = [k * co[k] for k in range(1, len(co))]
    return d or [Fr(0)]


# ------------------------------------------------------------------ Bernstein enclosure
def bernstein(co):
    n = len(co) - 1
    return [sum(Fr(comb(i, k), comb(n, k)) * co[k] for k in range(i + 1)) for i in range(n + 1)]


def casteljau(b):
    left, right = [b[0]], [b[-1]]
    cur = b[:]
    while len(cur) > 1:
        cur = [(u + v) / 2 for u, v in zip(cur, cur[1:])]
        left.append(cur[0])
        right.append(cur[-1])
    return left, list(reversed(right))


def min_abs(co, rtol=Fr(1, 10 ** 9), maxleaves=4000):
    """Certified [lo, hi] with lo <= min_{[0,1]} |p| <= hi (exact Bernstein branch and bound)."""
    def lb(bb):
        if all(v > 0 for v in bb):
            return min(bb)
        if all(v < 0 for v in bb):
            return min(-v for v in bb)
        return Fr(0)
    b = bernstein(co)
    if b[0] == 0 or b[-1] == 0 or (b[0] > 0) != (b[-1] > 0):
        return Fr(0), Fr(0)
    hi = min(abs(b[0]), abs(b[-1]))
    leaves = [b]
    for _ in range(maxleaves):
        leaves.sort(key=lb)
        bb = leaves.pop(0)
        l = lb(bb)
        if l >= hi * (1 - rtol):
            return l, hi
        L, R = casteljau(bb)
        mid = L[-1]                                  # exact value at the subdivision point
        if mid == 0 or (mid > 0) != (bb[0] > 0):
            return Fr(0), Fr(0)
        hi = min(hi, abs(mid))
        leaves += [L, R]
    return min(lb(x) for x in leaves), hi


# ------------------------------------------------------------------ exact roots (Yun + Sturm)
def trim(a):
    a = a[:]
    while len(a) > 1 and a[-1] == 0:
        a.pop()
    return a


def pdivmod(a, b):
    a, b = trim(a), trim(b)
    q = [Fr(0)] * max(1, len(a) - len(b) + 1)
    r = a[:]
    while len(r) >= len(b) and any(r):
        k = len(r) - len(b)
        f = r[-1] / b[-1]
        q[k] = f
        r = [ri - (f * b[i - k] if 0 <= i - k < len(b) else 0) for i, ri in enumerate(r)]
        r = trim(r[:-1]) if len(r) > 1 else [Fr(0)]
    return trim(q), trim(r)


def pgcd(a, b):
    a, b = trim(a), trim(b)
    while any(b):
        _, r = pdivmod(a, b)
        a, b = b, r
    return [c / a[-1] for c in a] if any(a) else a


def yun(f):
    """Square-free factors [(a_i, i)] with f = c * prod a_i^i (characteristic 0)."""
    f = trim(f)
    if len(f) == 1:
        return []
    d = deriv(f)
    a = pgcd(f, d)
    b, _ = pdivmod(f, a)
    c, _ = pdivmod(d, a)
    out = []
    i = 1
    while len(trim(b)) > 1:
        dd = [ci - di for ci, di in zip(c + [Fr(0)] * len(b), (deriv(b) + [Fr(0)] * len(c)))]
        dd = trim(dd[:max(len(c), len(deriv(b)))])
        ai = pgcd(b, dd)
        if len(ai) > 1:
            out.append((ai, i))
        b, _ = pdivmod(b, ai)
        c, _ = pdivmod(dd, ai)
        i += 1
    return out


def sturm(a):
    seq = [trim(a), trim(deriv(a))]
    while len(seq[-1]) > 1 or seq[-1][0] != 0:
        _, r = pdivmod(seq[-2], seq[-1])
        if not any(r):
            break
        seq.append([-x for x in r])
    return seq


def sign_changes(seq, t):
    vals = [peval(p, t) for p in seq]
    vals = [v for v in vals if v != 0]
    return sum(1 for u, v in zip(vals, vals[1:]) if (u > 0) != (v > 0))


def roots01(f, width=Fr(1, 2 ** 90)):
    """Distinct real roots of f in [0, 1]: list of (lo, hi, multiplicity, exact) with dyadic lo,
    hi (lo == hi for a root found exactly)."""
    out = []
    if not any(trim(f)):
        return None                                   # identically zero
    for a, mult in yun(f):
        found = []
        for t in (Fr(0), Fr(1)):
            if peval(a, t) == 0:
                found.append((t, t, mult, True))
        seq = sturm(a)
        stack = [(Fr(0), Fr(1))]
        while stack:
            lo, hi = stack.pop()
            # open interval (lo, hi): roots strictly inside
            n = sign_changes(seq, lo) - sign_changes(seq, hi) - (1 if peval(a, hi) == 0 else 0)
            if n <= 0:
                continue
            mid = (lo + hi) / 2
            if peval(a, mid) == 0:
                found.append((mid, mid, mult, True))
                stack += [(lo, mid), (mid, hi)]
                continue
            if n == 1 and hi - lo <= width:
                found.append((lo, hi, mult, False))
                continue
            stack += [(lo, mid), (mid, hi)]
        out += found
    return sorted(out, key=lambda r: r[0])


def sympy_count(f):
    """Distinct real roots intersecting [0, 1] with multiplicities, from sympy (independent
    implementation: Poly.intervals over QQ, refined to width 1e-30)."""
    import sympy
    t = sympy.Symbol('t')
    P = sympy.Poly(sum(sympy.Rational(c.numerator, c.denominator) * t ** k for k, c in enumerate(f)), t, domain='QQ')
    if P.is_zero:
        return None
    res = []
    for (lo, hi), mult in P.intervals(eps=sympy.Rational(1, 10 ** 30)):
        lo = Fr(int(sympy.numer(lo)), int(sympy.denom(lo)))
        hi = Fr(int(sympy.numer(hi)), int(sympy.denom(hi)))
        if hi >= 0 and lo <= 1:
            res.append((lo, hi, mult))
    return sorted(res)


# ------------------------------------------------------------------ PolynomialRoots (port)
MP = 2.2204460492503131e-16
BT = 32 * MP
RF = 64 * MP


def _sign(v):
    return (v > 0) - (v < 0)


def _evaluate(c, t):
    v = 0.
    for a in reversed(c):
        v = v * t + a
    return v


def _near_zero(c, t):
    bound = 0.
    for a in reversed(c):
        bound = bound * abs(t) + abs(a)
    return abs(_evaluate(c, t)) <= RF * bound


def _add(roots, r):
    if not math.isfinite(r) or r < -BT or r > 1 + BT:
        return
    if r <= BT:
        r = 0.
    elif r >= 1 - BT:
        r = 1.
    roots.append(r)


def _quadratic(c, b, a, roots):
    bb, ac4 = b * b, 4 * a * c
    disc = bb - ac4
    unc = RF * (abs(bb) + abs(ac4))
    if disc < -unc:
        return
    if abs(disc) <= unc:
        _add(roots, -b / (2 * a))
        return
    num = -0.5 * (b + math.copysign(math.sqrt(disc), b))
    _add(roots, num / a)
    _add(roots, c / num)


def in_unit_interval(*coefficients):
    if not 1 <= len(coefficients) <= 5:
        raise ValueError
    scale = max(abs(v) for v in coefficients)
    if scale == 0:
        return []
    c = [v / scale for v in coefficients]
    degree = len(c) - 1
    while degree > 0 and c[degree] == 0:
        degree -= 1
    roots = []
    if degree == 1:
        _add(roots, -c[0] / c[1])
    elif degree == 2:
        _quadratic(c[0], c[1], c[2], roots)
    elif degree >= 3:
        bounds = [0.]
        der = [k * c[k] for k in range(1, degree + 1)]
        for t in in_unit_interval(*der):
            if 0 < t < 1:
                bounds.append(t)
        bounds.append(1.)
        if degree == 3 and len(bounds) == 4 and _near_zero(c, bounds[1]) and _near_zero(c, bounds[2]):
            _add(roots, -c[2] / (3 * c[3]))
            return roots
        for t in bounds:
            if _near_zero(c, t):
                _add(roots, t)
        for i in range(1, len(bounds)):
            lo, hi = bounds[i - 1], bounds[i]
            flo, fhi = _evaluate(c, lo), _evaluate(c, hi)
            if _near_zero(c, lo) or _near_zero(c, hi) or _sign(flo) == _sign(fhi):
                continue
            it = 0
            while it < 128 and hi - lo > BT:
                mid = lo + (hi - lo) / 2
                fm = _evaluate(c, mid)
                if fm == 0:
                    lo = hi = mid
                    break
                if _sign(fm) == _sign(flo):
                    lo, flo = mid, fm
                else:
                    hi = mid
                it += 1
            _add(roots, lo + (hi - lo) / 2)
    roots.sort()
    distinct = []
    for r in roots:
        if not distinct or r - distinct[-1] > BT:
            distinct.append(r)
    return distinct
