"""Round 5 (O2): reference implementation of the floating-point operation contract.

This file is the executable form of ../FLOATING_POINT_CONTRACT.md. Every numerically relevant
formula is written as the operation sequence the C# implementation is bound to (explicit
products, no pow, no FMA, fixed order), and every bound term follows from that sequence with the
lemmas of the contract:

  L1  product chain (Higham Lemma 3.1): a value formed by k roundings of products, quotients and
      sums of same-sign terms from exact inputs satisfies |v^ - v| <= gamma(k) |v|.
  L2  recursive summation of m computed terms: error <= gamma(m - 1) sum |t^|.
  L3  conversion to the computed value: gamma(k) |v| <= gamma(k + 1) |v^|.
  L4  underflow: an operation whose operands are normal or zero has the result
      (x op y)(1 + d) + e with |d| <= u, |e| <= LAM = 2^-1022, d e = 0; this holds for gradual
      underflow (|e| <= 2^-1075) and for flush-to-zero / denormals-are-zero alike.
  L5  upward evaluation: for a single round-to-nearest operation with result r,
      up(r) = succ(r) if |r| >= 2^-1022 else 2^-1022 is >= the exact result (also under FTZ),
      dn(r) correspondingly; every bound is evaluated with these operations.

gb(k) = (k + 1) u >= gamma(k) is an exactly representable bound of gamma(k) (k (k+1) u <= 1).

Differences to state.py (the formulation is the same; the contract fixes the arithmetic):
- right-hand side normalised by a power of two (excitation F_exc in [1, 2) in scaled units);
- load terms with the intensity first and the ramp as rise*(n/d)*n^p (underflow and overflow
  safe orders); distributed loads with the data error of the clipped intensities;
- static rounding counts from the sequences (T class), absolute allowances LAM per operation;
- stored solution and adjoint vectors contain no subnormal numbers (software flush, exact for
  the a-posteriori relations);
- residuals and bounds evaluated with the stated operations; verification of the adjoints with
  a double residual and its rigorous bound (Dot2 as option), weights by Neumann iteration;
- range rules (NumericalRange) for every quantity that must be normal;
- SI back-scaling with its own allowance and range rule; gate with rigorous lower bounds of the
  reference scales.
MODE[0] = 'ftz' emulates flush-to-zero and denormals-are-zero for every modelled operation
(coefficients, load integrals, residuals, evaluation, back-scaling); the LU, the solves and the
refinement need no model (a-posteriori) and run in host arithmetic.
Usage: imported by fpc_stage_a.py, fpc_chain.py, fpc_unverified.py."""
import math, os, random, sys, zlib
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

U = 2.0 ** -53
MINN = 2.0 ** -1022         # smallest normal number
LAM = MINN                  # absolute allowance per operation (gradual and abrupt underflow)
DELTA = 2.0 ** -200         # share of the gate denominators in the verification weights
SI_MIN = 2.0 ** -969        # SI reference scales must be >= SI_MIN (LAM / S <= u)
SI_MAX = 2.0 ** 1000        # and <= SI_MAX (no overflow of values and bounds)
LEN_MIN = 2.0 ** -969       # scaled length of a distributed load (|q| LAM <= u)
TAU = 1e-8
MODE = ['gradual']


class RangeError(Exception):
    """NumericalRange: a quantity that the contract requires to be normal is not."""


class NotVerified(Exception):
    pass


def gb(k):
    return 0.0 if k <= 0 else (k + 1) * U


# ------------------------------------------------------------------ modelled float operations
def _daz(a):
    return math.copysign(0.0, a) if MODE[0] == 'ftz' and a != 0 and abs(a) < MINN else a


def _ftz(r):
    return math.copysign(0.0, r) if MODE[0] == 'ftz' and r != 0 and abs(r) < MINN else r


def fadd(a, b): return _ftz(_daz(a) + _daz(b))
def fsub(a, b): return _ftz(_daz(a) - _daz(b))
def fmul(a, b): return _ftz(_daz(a) * _daz(b))
def fdiv(a, b): return _ftz(_daz(a) / _daz(b))
def fscale(a, e): return _ftz(math.ldexp(_daz(a), e))


def flush(v):
    """Software flush of stored vectors: no subnormal component (exact for the a-posteriori
    relations, which hold for any stored vector)."""
    return [0.0 if abs(a) < MINN else a for a in v]


# ------------------------------------------------------------------ upward / downward bounds
def up(r):
    return math.nextafter(r, math.inf) if abs(r) >= MINN else MINN


def dn(r):
    return math.nextafter(r, -math.inf) if abs(r) >= MINN else -MINN


def uadd(a, b): return 0.0 if a == 0 and b == 0 else up(a + b)
def umul(a, b): return 0.0 if a == 0 or b == 0 else up(a * b)
def udiv(a, b): return 0.0 if a == 0 else up(a / b)
def dadd(a, b): return 0.0 if a == 0 and b == 0 else dn(a + b)
def dsub(a, b): return 0.0 if a == b else dn(a - b)
def dmul(a, b): return 0.0 if a == 0 or b == 0 else dn(a * b)
def ddiv(a, b): return 0.0 if a == 0 else dn(a / b)


def usum(xs):
    s = 0.0
    for x in xs:
        s = uadd(s, x)
    return s


def udot(a, b):
    s = 0.0
    for x, y in zip(a, b):
        s = uadd(s, umul(abs(x), abs(y)))
    return s


# ------------------------------------------------------------------ rounding-count values (L1)
class T:
    """Value of a positive-structured operation chain with its rounding count k (L1).
    Exact mode: v is a Fraction and no rounding happens. sub: some intermediate result was
    below MINN (for the range rule of coefficients)."""
    __slots__ = ('v', 'k', 'sub')

    def __init__(s, v, k=0, sub=False):
        s.v, s.k, s.sub = v, k, sub

    @property
    def exact(s):
        return isinstance(s.v, Fr)

    def _mark(s, r):
        return r != 0 and abs(r) <= MINN

    def __mul__(s, o):
        if isinstance(o, int):
            if s.exact:
                return T(s.v * o)
            r = fmul(s.v, float(o))
            return T(r, s.k + (0 if o & (o - 1) == 0 else 1), s.sub or s._mark(r))
        if s.exact:
            return T(s.v * o.v)
        r = fmul(s.v, o.v)
        return T(r, s.k + o.k + 1, s.sub or o.sub or s._mark(r))

    def __truediv__(s, o):
        if isinstance(o, int):
            if s.exact:
                return T(s.v / o)
            r = fdiv(s.v, float(o))
            return T(r, s.k + (0 if o & (o - 1) == 0 else 1), s.sub or s._mark(r))
        if s.exact:
            return T(s.v / o.v)
        r = fdiv(s.v, o.v)
        return T(r, s.k + o.k + 1, s.sub or o.sub or s._mark(r))

    def __add__(s, o):
        if s.exact:
            return T(s.v + o.v)
        assert s.v == 0 or o.v == 0 or (s.v > 0) == (o.v > 0), 'cancellation in a positive chain'
        r = fadd(s.v, o.v)
        return T(r, max(s.k, o.k) + 1, s.sub or o.sub or s._mark(r))

    def __neg__(s):
        return T(-s.v, s.k, s.sub)


def inp(v, exact):
    return T(Fr(v)) if exact else T(float(v))


def dist(x, c, eL, exact):
    """Scaled distance (x - c) 2^-eL from two stored coordinates: one rounding (invariant 4)."""
    if exact:
        return T((Fr(x) - Fr(c)) * Fr(2) ** -eL)
    r = fscale(fsub(x, c), -eL)
    return T(r, 1, r != 0 and abs(r) <= MINN)


def bound_T(t):
    """L3: |t^ - t| <= gb(k + 1) |t^| (relative part only)."""
    return umul(gb(t.k + 1), abs(t.v)) if t.k > 0 else 0.0


# ------------------------------------------------------------------ load terms (items 6-8)
def point_force(v, d, p):
    """v d^(p-1)/(p-1)! with the intensity first: v, v d, (v d) d/2, ((v d) d) d/6."""
    if p == 1:
        return v
    t = v * d
    if p == 2:
        return t
    t = t * d
    if p == 3:
        return t / 2
    return (t * d) / 6


def point_couple(v, d, p):
    """-v d^(p-2)/(p-2)!: -v, -v d, (-v d) d/2 (p = 2, 3, 4)."""
    t = -v
    if p == 2:
        return t
    t = t * d
    if p == 3:
        return t
    return (t * d) / 2


def diff_term(q, n, far, dd, p, inside):
    """q * Difference_p / p! with the intensity first. inside: x <= b (n = x - a);
    beyond: dd = b - a, n = x - a, far = x - b (all terms positive in n, far, dd)."""
    if inside:
        t = q * n
        for _ in range(p - 1):
            t = t * n
        return t / {1: 1, 2: 2, 3: 6, 4: 24}[p]
    t = q * dd
    if p == 1:
        return t
    if p == 2:
        return (t * (n + far)) / 2
    if p == 3:
        return (t * ((n * n + n * far) + far * far)) / 6
    return ((t * (n + far)) * (n * n + far * far)) / 24


def ramp_term(r, n, far, dd, p, inside):
    """Ramp_p (rise r over [a, b]) with r (n/d) first (no overflow for short loads)."""
    if inside:
        t = r * (n / dd)
        for _ in range(p):
            t = t * n
        return t / {1: 2, 2: 6, 3: 24, 4: 120}[p]
    t = r * dd
    if p == 1:
        return t / 2
    if p == 2:
        return (t * (dd + far * 3)) / 6
    if p == 3:
        return (t * ((dd * dd + (dd * far) * 4) + (far * far) * 6)) / 24
    return (t * (((dd * dd * dd + ((dd * dd) * far) * 5) + ((dd * far) * far) * 10) + ((far * far) * far) * 10)) / 120


def clip(la, lb, v1, v2, ca, cb, exact):
    """Clipped intensities at ca, cb of the linear load v1..v2 on [la, lb] (scaled values) and
    the bounds of their data errors: qa = v1 + (v2 - v1) ((ca - la)/(lb - la))."""
    out = []
    for c, vend, isstart in ((ca, v1, True), (cb, v2, False)):
        if (isstart and c == la) or (not isstart and c == lb):
            out.append((Fr(vend) if exact else vend, 0.0))
            continue
        if exact:
            out.append((Fr(v1) + (Fr(v2) - Fr(v1)) * ((Fr(c) - Fr(la)) / (Fr(lb) - Fr(la))), 0.0))
            continue
        r = fsub(v2, v1)
        if r == 0:
            out.append((v1, 0.0))
            continue
        f = fdiv(fsub(c, la), fsub(lb, la))
        pr = fmul(r, f)
        q = fadd(v1, pr)
        # |q^ - q| <= gamma(5)|r f| + u|q^| (+ LAM per operation, amplified by |r| <= 2 max|v|)
        bnd = uadd(uadd(umul(gb(6), abs(pr)), umul(U, abs(q))), umul(LAM, uadd(abs(r), 8.0)))
        out.append((q, bnd))
    return out


LAMD = [LAM]                # absolute allowance of a scaled distance: LAM 2^-eL (set per model)


def lam_term(w, p):
    """Absolute underflow allowance of one load term with intensity w (L4): every operation of
    the chain may lose LAM, a distance fl(x - c) 2^-eL may lose LAM 2^-eL (flushed SI
    difference); later factors are distances <= 2^0.5 or constants; factor 2 for L3."""
    return umul(LAMD[0], umul(uadd(abs(w), 8.0), 64.0))


# ------------------------------------------------------------------ the solver
class CS:
    """Contract solver. opts: v_iter (Neumann steps for the verification weights), adj_res
    ('double' | 'dot2'), LUeps (perturbation of the LU factors), verify_weights ('B' | 'N')."""

    def __init__(s, c, v_iter=3, adj_res='double', LUeps=None, rng=None, exact_twin=False):
        s.c = c
        s.v_iter, s.adj_res, s.LUeps, s.rng = v_iter, adj_res, LUeps, rng
        s.status, s.cause = None, None
        s.prepare()
        s.build(exact=False)
        if exact_twin:
            s.build(exact=True)

    # -------------------------------------------------------------------------- inputs, ranges
    def prepare(s):
        c = s.c
        L = float(c['L'])
        s.L = L
        E, I = float(c['E']), float(c['I'])
        vals = [L, E, I] + [float(v) for _, v in c['pf']] + [float(v) for _, v in c['pm']] + \
               [float(v) for *_, v in c['q']] + [float(v) for l in c.get('lin', []) for v in l[2:]] + \
               [float(v) for _, v in c.get('ei', [])] + \
               [float(x) for s_ in c.get('springs', []) for x in s_[1:3] if x != 'inf'] + \
               [float(x) for x, _ in c['sup']] + [float(s_[0]) for s_ in c.get('springs', [])] + \
               [float(h) for h in c.get('hinges', [])] + [float(a) for a, _ in c['pf']] + [float(a) for a, _ in c['pm']] + \
               [float(x) for l in c['q'] for x in l[:2]] + [float(x) for l in c.get('lin', []) for x in l[:2]] + \
               [float(x) for x, _ in c.get('ei', [])]
        if any(v != 0 and (abs(v) < MINN or not math.isfinite(v)) for v in vals):
            raise RangeError('input not normal')
        EI0 = fmul(E, I)                        # adapter product (counted as data, k = 1)
        if not (MINN <= EI0 < math.inf):
            raise RangeError('EI')
        s.eis = [(0.0, EI0, 1)] + [(float(x), float(v), 0) for x, v in c.get('ei', [])]
        s.eis_exact = [(Fr(0), Fr(E) * Fr(I))] + [(Fr(float(x)), Fr(float(v))) for x, v in c.get('ei', [])]
        s.eL = round(math.log2(L))
        s.eE = round(math.log2(max(e for _, e, _ in s.eis)))
        s.Lr, s.EIr = 2.0 ** s.eL, 2.0 ** s.eE
        if L < SI_MIN * 2:                       # LAM 2^-eL <= u: a flushed coordinate difference
            raise RangeError('beam length')      # stays at rounding level in scaled units
        LAMD[0] = max(LAM, math.ldexp(LAM, -s.eL))
        # excitation (7.2): F = sum|P| + sum (|q_a| + |q_b|)/2 (b - a) + sum |C|/L; only the
        # exponent of its computed value is used here (any power of two is exact)
        F = sum(abs(float(v)) for _, v in c['pf']) + sum(abs(float(v)) * (float(b) - float(a)) for a, b, v in c['q']) + \
            sum((abs(float(v1)) + abs(float(v2))) / 2 * (float(b) - float(a)) for a, b, v1, v2 in c.get('lin', [])) + \
            sum(abs(float(v)) for _, v in c['pm']) / L
        s.F_si = F
        if F == 0:
            s.eF = 0
            s.unloaded = True
        else:
            s.unloaded = False
            s.eF = math.frexp(F)[1] - 1 + 2 * s.eL - s.eE        # F_n = F 2^(2eL - eE - eF) in [1, 2)
        eL, eE, eF = s.eL, s.eE, s.eF
        # exponents of the normalised units: x_n = x_SI 2^(-E_q)
        s.Eq = [eL + eF, eF, eE - eL + eF, eE - 2 * eL + eF]         # w, theta, M, V
        s.eV, s.eM, s.eq = 2 * eL - eE - eF, eL - eE - eF, 3 * eL - eE - eF
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
        st = sorted({0.0, L} | {x for x, _ in tr} | {x for x, _ in ro} | set(s.h) | {x for x, _, _ in s.eis[1:] if 0 < x < L})
        s.st = st
        s.N = len(st) - 1
        s.pf = [(float(a), float(v)) for a, v in c['pf']]
        s.pm = [(float(a), float(v)) for a, v in c['pm']]
        s.lin = [(float(a), float(b), float(v), float(v)) for a, b, v in c['q']] + [tuple(map(float, l)) for l in c.get('lin', [])]
        for la, lb, v1, v2 in s.lin:
            d = fscale(fsub(lb, la), -eL)
            if d < LEN_MIN:
                raise RangeError('distributed load length')
        # distances between distinct mechanical stations: normal, and the coefficient chains
        for x0, x1 in zip(st, st[1:]):
            if fsub(x1, x0) < MINN:
                raise RangeError('station distance')

    def sec(s, x):
        return [t for t in s.eis if t[0] <= x][-1]

    def sec_exact(s, x):
        return [t for t in s.eis_exact if t[0] <= Fr(x)][-1][1]

    # -------------------------------------------------------------------------- load integrals
    def seg_loads(s, j, exact):
        """Loads inside the open segment j in normalised units: point forces, couples, distributed
        (clipped, with data bounds)."""
        a, b = s.st[j], s.st[j + 1]
        if exact:
            pts = [(x, Fr(v) * Fr(2) ** s.eV) for x, v in s.pf if a < x < b]
            mom = [(x, Fr(v) * Fr(2) ** s.eM) for x, v in s.pm if a < x < b]
        else:
            pts = [(x, fscale(v, s.eV)) for x, v in s.pf if a < x < b]
            mom = [(x, fscale(v, s.eM)) for x, v in s.pm if a < x < b]
        lins = []
        for la, lb, v1, v2 in s.lin:
            ca, cb = max(la, a), min(lb, b)
            if cb <= ca:
                continue
            if exact:
                w1, w2 = Fr(v1) * Fr(2) ** s.eq, Fr(v2) * Fr(2) ** s.eq
            else:
                w1, w2 = fscale(v1, s.eq), fscale(v2, s.eq)
            (qa, da), (qb, db) = clip(la, lb, w1, w2, ca, cb, exact)
            lins.append((ca, cb, qa, qb, da, db))
        return pts, mom, lins

    def loadint(s, j, x, side, exact):
        """Per p in (4, 3, 2, 1): value, error bound (contract data + evaluation rounding),
        magnitude sum |terms| (monotone in x), number of terms. Order: point forces, couples,
        distributed loads (term qa*Diff, then ramp), each in input order."""
        pts, mom, lins = s.loads[j] if not exact else s.loads_x[j]
        eL = s.eL
        out = []
        for p in (4, 3, 2, 1):
            terms = []           # (T value, data bound, lam bound)
            for cpos, v in pts:
                if x > cpos or (p == 1 and x == cpos and side == 'R'):
                    d = dist(x, cpos, eL, exact)
                    t = point_force(T(v) if exact else T(v), d, p)
                    terms.append((t, 0.0, 0.0 if exact else lam_term(v, p)))
            for cpos, v in mom:
                if p == 1:
                    continue
                if x > cpos or (p == 2 and x == cpos and side == 'R'):
                    d = dist(x, cpos, eL, exact)
                    t = point_couple(T(v), d, p)
                    terms.append((t, 0.0, 0.0 if exact else lam_term(v, p)))
            for ca, cb, qa, qb, da, db in lins:
                if not x > ca:
                    continue
                inside = x <= cb
                n = dist(x, ca, eL, exact)
                far = None if inside else dist(x, cb, eL, exact)
                dd = dist(cb, ca, eL, exact)
                if exact:
                    rise = T(qb - qa)
                    terms.append((diff_term(T(qa), n, far, dd, p, inside), 0., 0.))
                    terms.append((ramp_term(rise, n, far, dd, p, inside), 0., 0.))
                    continue
                rv = fsub(qb, qa)
                rise = T(rv, 1)
                t1 = diff_term(T(qa), n, far, dd, p, inside)
                t2 = ramp_term(rise, n, far, dd, p, inside)
                # data: |qa^ - qa| |Diff|/p! + (|qa^ - qa| + |qb^ - qb|) Ramp (unit polynomials)
                u1 = diff_term(T(1.0), n, far, dd, p, inside)
                u2 = ramp_term(T(1.0), n, far, dd, p, inside)
                data1 = umul(da, umul(abs(u1.v), 1 + gb(u1.k + 1))) if da else 0.0
                data2 = umul(uadd(da, db), umul(abs(u2.v), 1 + gb(u2.k + 1))) if (da or db) else 0.0
                terms.append((t1, data1, lam_term(qa, p)))
                terms.append((t2, data2, lam_term(rv, p)))
            if exact:
                out.append((sum((t.v for t, _, _ in terms), Fr(0)), 0., 0., len(terms)))
                continue
            val = 0.0
            for t, _, _ in terms:
                val = fadd(val, t.v)
            m = len(terms)
            mag = usum(abs(t.v) for t, _, _ in terms)
            bnd = usum(uadd(umul(uadd(gb(t.k + 1) if t.k else 0.0, gb(m - 1)), abs(t.v)), uadd(dt, lt)) for t, dt, lt in terms)
            bnd = uadd(bnd, umul(LAM, 2.0 * m))
            out.append((val, bnd, mag, m))
        return out

    # -------------------------------------------------------------------------- equations
    def coefs(s, j, exact):
        """Transfer coefficients of segment j (items 3-5): a, a/e, a a/2/e, a a a/6/e."""
        x0, x1 = s.st[j], s.st[j + 1]
        a = dist(x1, x0, s.eL, exact)
        if exact:
            e = T(s.sec_exact(x0) * Fr(2) ** -s.eE)
        else:
            _, EI, ke = s.sec(x0)
            e = T(fscale(EI, -s.eE), ke)
        aa = a * a
        c = dict(a=a, a_e=a / e, a2=(aa / 2) / e, a3=((aa * a) / 6) / e, e=e)
        if not exact:
            for k, t in c.items():
                if t.sub or not (MINN < abs(t.v) < math.inf):
                    raise RangeError('coefficient ' + k)
        return c

    def left(s, i, exact):
        """Left state of station i: per q (coef dict col -> T, const value, const bound)."""
        if i == 0:
            one = T(Fr(1)) if exact else T(1.0)
            z = Fr(0) if exact else 0.0
            return [({0: one}, z, 0.), ({1: one}, z, 0.), ({}, z, 0.), ({}, z, 0.)]
        j = i - 1
        c = s.co_x[j] if exact else s.co[j]
        o = 4 * j
        P = s.loadint(j, s.st[i], 'L', exact)
        one = T(Fr(1)) if exact else T(1.0)
        e = c['e']
        out = []
        for q, coefs in ((0, {o: one, o + 1: c['a'], o + 2: c['a2'], o + 3: c['a3']}),
                         (1, {o + 1: one, o + 2: c['a_e'], o + 3: c['a2']}),
                         (2, {o + 2: one, o + 3: c['a']}),
                         (3, {o + 3: one})):
            val, bnd, mag, m = P[q]
            if q < 2:
                if exact:
                    out.append((coefs, val / e.v, 0.))
                    continue
                cv = fdiv(val, e.v)
                # |val^/e^ (1+d) - val/e| <= (bnd + gamma(2)|val^|)(1 + gb(2))/e^ (+ LAM)
                cb = uadd(udiv(umul(uadd(bnd, umul(gb(2), abs(val))), 1 + gb(2)), abs(e.v)), umul(U, abs(cv)))
                cb = uadd(cb, LAM if abs(cv) <= MINN else 0.0)
                out.append((coefs, cv, cb))
            else:
                out.append((coefs, val, bnd))
        return out

    def build(s, exact):
        N, st = s.N, s.st
        s.co_x = None
        if exact:
            s.loads_x = [s.seg_loads(j, True) for j in range(N)]
            s.co_x = [s.coefs(j, True) for j in range(N)]
        else:
            s.loads = [s.seg_loads(j, False) for j in range(N)]
            s.co = [s.coefs(j, False) for j in range(N)]
        rows = []                     # (dict col -> T, rhs, rhs bound)
        prescribed = set()
        s.rowidx = {}
        for i, x in enumerate(st):
            sm = s.left(i, exact)
            pfs = [v for a, v in s.pf if a == x]
            pms = [v for a, v in s.pm if a == x]
            if exact:
                Pst = sum((Fr(v) * Fr(2) ** s.eV for v in pfs), Fr(0))
                Cst = sum((Fr(v) * Fr(2) ** s.eM for v in pms), Fr(0))
                Pb = Cb = 0.
            else:
                pv = [fscale(v, s.eV) for v in pfs]
                cv = [fscale(v, s.eM) for v in pms]
                Pst = Cst = 0.0
                for v in pv:
                    Pst = fadd(Pst, v)
                for v in cv:
                    Cst = fadd(Cst, v)
                Pb = uadd(umul(gb(len(pv) - 1), usum(abs(v) for v in pv)), umul(LAM, 2.0 * len(pv)))
                Cb = uadd(umul(gb(len(cv) - 1), usum(abs(v) for v in cv)), umul(LAM, 2.0 * len(cv)))
            ktr = [k for a, k in s.tr if a == x]
            kro = [k for a, k in s.ro if a == x]
            hinge = x in s.h
            rigidw = any(k == math.inf for k in ktr)
            rigidt = any(k == math.inf for k in kro)
            kws = [k for k in ktr if k != math.inf]
            kts = [k for k in kro if k != math.inf]
            if exact:
                kw = sum((Fr(k) for k in kws), Fr(0)) * Fr(2) ** (3 * s.eL - s.eE)
                kt = sum((Fr(k) for k in kts), Fr(0)) * Fr(2) ** (s.eL - s.eE)
                kwT, ktT = T(kw), T(kt)
            else:
                kw = kt = 0.0
                for k in kws:
                    kw = fadd(kw, k)
                for k in kts:
                    kt = fadd(kt, k)
                kw, kt = fscale(kw, 3 * s.eL - s.eE), fscale(kt, s.eL - s.eE)
                for v in (kw, kt):
                    if v != 0 and not (MINN < v < math.inf):
                        raise RangeError('spring')
                kwT, ktT = T(kw, max(len(kws) - 1, 0)), T(kt, max(len(kts) - 1, 0))
            o = 4 * i
            interior = i > 0

            def jump(q, const, cbnd, extra=()):
                s.rowidx[(i, q)] = len(rows)
                d = {o + q: T(Fr(1)) if exact else T(1.0)}
                for col, t in sm[q][0].items():
                    d[col] = -t
                for col, t in extra:
                    d[col] = t
                if exact:
                    rows.append((d, sm[q][1] + const, 0.))
                else:
                    r = fadd(sm[q][1], const)
                    rb = uadd(uadd(sm[q][2], cbnd), umul(U, abs(r)))
                    rb = uadd(rb, LAM if abs(r) <= MINN else 0.0)
                    rows.append((d, r, rb))

            z = Fr(0) if exact else 0.0
            if interior:
                jump(0, z, 0.)
            if rigidw:
                prescribed.add(o)
            if hinge:
                prescribed.add(o + 2)
            elif interior:
                jump(1, z, 0.)
            if rigidt:
                prescribed.add(o + 1)
            else:
                jump(2, -Cst, Cb, [(o + 1, -ktT)] if kt else ())
            if not rigidw:
                jump(3, Pst, Pb, [(o, kwT)] if kw else ())
            if i == N:
                prescribed.add(o + 2)
                prescribed.add(o + 3)
        nfull = 4 * (N + 1)
        free = [k for k in range(nfull) if k not in prescribed]
        col = {k: n for n, k in enumerate(free)}
        n = len(free)
        if len(rows) != n:
            raise RuntimeError('InternalInconsistency: rows %d unknowns %d' % (len(rows), n))
        if exact:
            Ax = [[Fr(0)] * n for _ in range(n)]
            bx = [Fr(0)] * n
            for r, (d, rhs, _) in enumerate(rows):
                for kx, t in d.items():
                    if kx in col:
                        Ax[r][col[kx]] = t.v
                bx[r] = rhs
            s.Ax, s.bx = Ax, bx
            return
        A = [[0.] * n for _ in range(n)]
        K = [[0] * n for _ in range(n)]
        b, bb = [0.] * n, [0.] * n
        for r, (d, rhs, rb) in enumerate(rows):
            for kx, t in d.items():
                if kx in col:
                    A[r][col[kx]] = t.v
                    K[r][col[kx]] = t.k
            b[r], bb[r] = rhs, rb
        s.nfull, s.free, s.col, s.n = nfull, free, col, n
        s.A, s.K, s.b, s.bb = A, K, b, bb

    # -------------------------------------------------------------------------- solution
    def solve(s):
        A, n = s.A, s.n
        rs, cs = [1.] * n, [1.] * n
        p2 = lambda v: 2.0 ** round(math.log2(abs(v)))
        for _ in range(8):
            for i in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for j in range(n))
                if m > 0:
                    rs[i] = rs[i] / p2(math.sqrt(m))
            for j in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for i in range(n))
                if m > 0:
                    cs[j] = cs[j] / p2(math.sqrt(m))
        s.rs, s.cs = rs, cs
        S = [[fmul(fmul(rs[i], A[i][j]), cs[j]) for j in range(n)] for i in range(n)]
        for i in range(n):
            for j in range(n):
                if A[i][j] != 0 and not (MINN < abs(S[i][j]) < math.inf):
                    raise RangeError('equilibrated entry')
        s.S = S
        s.sb = [fmul(rs[i], s.b[i]) for i in range(n)]
        # bounds of the data errors of the equilibrated system (L3, exact power-of-two scaling)
        s.dS = [[umul(gb(s.K[i][j] + 1), abs(S[i][j])) if s.K[i][j] else 0.0 for j in range(n)] for i in range(n)]
        s.dsb = [uadd(umul(rs[i], s.bb[i]), LAM if s.b[i] != 0 and abs(s.sb[i]) <= MINN else 0.0) for i in range(n)]
        s.nz = [[j for j in range(n) if S[i][j] != 0] for i in range(n)]
        s.cols = [[i for i in range(n) if S[i][j] != 0] for j in range(n)]
        LU = [row[:] for row in S]
        piv = list(range(n))
        for k in range(n):
            p = max(range(k, n), key=lambda i: abs(LU[i][k]))
            if LU[p][k] == 0:
                raise ZeroDivisionError('zero pivot')
            if abs(LU[p][k]) < MINN:
                raise RangeError('pivot')
            LU[k], LU[p] = LU[p], LU[k]
            piv[k], piv[p] = piv[p], piv[k]
            for i in range(k + 1, n):
                f = LU[i][k] / LU[k][k]
                LU[i][k] = f
                if f != 0:
                    rk, ri = LU[k], LU[i]
                    for j in range(k + 1, n):
                        ri[j] -= f * rk[j]
        if s.LUeps:
            for eps in s.LUeps:
                LUp = [[v * (1 + eps * s.rng.uniform(-1, 1)) for v in row] for row in LU]
            LU = LUp
        s.LU, s.piv = LU, piv
        # primary solution, rule A (at least one step, continue while the correction halves)
        y = flush(s.lusolve(s.sb))
        s.iterations = 0
        prev = math.inf
        for _ in range(5):
            r = [s.sb[i] - sum(S[i][j] * y[j] for j in s.nz[i]) for i in range(n)]
            dy = s.lusolve(r)
            y = flush([y[i] + dy[i] for i in range(n)])
            s.iterations += 1
            nd = max(map(abs, dy)) if dy else 0.
            ny = max(map(abs, y)) if y else 0.
            if not nd == nd or nd <= 2 * U * ny or nd > 0.5 * prev:
                break
            prev = nd
        s.y = y
        if not all(math.isfinite(v) for v in y):
            raise ArithmeticError('NonFinite')
        s.x = [0.] * s.nfull
        for i, k in enumerate(s.free):
            s.x[k] = fmul(cs[i], y[i])
        s.residual_and_d()

    def lusolve(s, v):
        n, LU = s.n, s.LU
        z = [v[s.piv[i]] for i in range(n)]
        for i in range(n):
            z[i] -= sum(LU[i][j] * z[j] for j in range(i))
        for i in reversed(range(n)):
            z[i] = (z[i] - sum(LU[i][j] * z[j] for j in range(i + 1, n))) / LU[i][i]
        return z

    def lusolve_t(s, c):
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

    def residual_and_d(s):
        """Item 14: r^_i = fl(sb_i - S_i1 y_1 - ... ) (product, then subtraction, column order) and
        |r^_i - r_i| <= gb(m+1)(|sb_i| + sum|S_ij||y_j|) + 2 m LAM; d_i = |r^| + dr + dS|y| + dsb."""
        S, n, y = s.S, s.n, s.y
        s.res, s.dr, s.d = [0.] * n, [0.] * n, [0.] * n
        for i in range(n):
            acc = s.sb[i]
            for j in s.nz[i]:
                acc = fsub(acc, fmul(S[i][j], y[j]))
            m = len(s.nz[i])
            mag = uadd(abs(s.sb[i]), udot([S[i][j] for j in s.nz[i]], [y[j] for j in s.nz[i]]))
            dr = uadd(umul(gb(m + 1), mag), umul(LAM, 2.0 * m))
            data = uadd(udot([s.dS[i][j] for j in s.nz[i]], [y[j] for j in s.nz[i]]), s.dsb[i])
            s.res[i], s.dr[i] = acc, dr
            s.d[i] = uadd(uadd(abs(acc), dr), data)

    # -------------------------------------------------------------------------- adjoints
    def adjoint(s, cvec):
        """z with S^T z = c, rule A (at least one step, continue while |z|^T d changes by more
        than 1/16, at most 5); flushed; verification residual and its bound (item 16, 17)."""
        S, n = s.S, s.n
        z = flush(s.lusolve_t(cvec))
        bprev = udot(z, s.d)
        steps = 0
        for _ in range(5):
            r = [cvec[j] - sum(S[i][j] * z[i] for i in s.cols[j]) for j in range(n)]
            dz = s.lusolve_t(r)
            z = flush([z[i] + dz[i] for i in range(n)])
            steps += 1
            bb = udot(z, s.d)
            if abs(bb - bprev) <= bb / 16:
                break
            bprev = bb
        s.adj_steps += steps
        s.adj_solves += 1
        sbar = []
        for j in range(n):
            col = s.cols[j]
            m = len(col)
            mag = uadd(abs(cvec[j]), udot([S[i][j] for i in col], [z[i] for i in col]))
            if s.adj_res == 'dot2':
                from state import dot2
                rj = dot2([(-S[i][j], z[i]) for i in col], cvec[j])
                g2 = gb(m + 1)
                b = uadd(udiv(uadd(abs(rj), umul(umul(g2, g2), mag)), 1 - U), umul(LAM, 16.0 * (m + 1)))
            else:
                acc = cvec[j]
                for i in col:
                    acc = fsub(acc, fmul(S[i][j], z[i]))
                rj = acc
                b = uadd(abs(rj), uadd(umul(gb(m + 1), mag), umul(LAM, 2.0 * m)))
            sbar.append(b)
        return z, sbar

    def Mv(s, z, sbar, GSv, v):
        return uadd(udot(sbar, v), udot(z, GSv))

    def GS(s, v):
        return [udot([s.dS[k][j] for j in s.nz[k]], [v[j] for j in s.nz[k]]) for k in range(s.n)]

    def verify(s, weights_D):
        """Items 16, 17: adjoints of all unknowns, beta_i = |z_i|^T d, verification of
        |e| <= beta + M|e| with M = |S_res| + |Z|^T dS (exact relation C = I - R S*), weights
        v = beta + DELTA D and Neumann steps v <- beta + M v + DELTA D."""
        n = s.n
        s.adj_steps = s.adj_solves = 0
        Z, SB, beta = [], [], []
        for i in range(n):
            z, sbar = s.adjoint([1. if k == i else 0. for k in range(n)])
            Z.append(z)
            SB.append(sbar)
            beta.append(udot(z, s.d))
        s.Z, s.SB, s.beta = Z, SB, beta
        D = weights_D
        cands = []
        v = [uadd(beta[j], umul(DELTA, D[j])) for j in range(n)]
        for it in range(s.v_iter + 1):
            GSv = s.GS(v)
            Mv = [s.Mv(Z[i], SB[i], GSv, v) for i in range(n)]
            mu = max([udiv(Mv[i], v[i]) for i in range(n)], default=0.)
            cands.append((mu, v, Mv))
            v = [uadd(uadd(beta[j], Mv[j]), umul(DELTA, D[j])) for j in range(n)]
        s.mus = [c[0] for c in cands]
        ok = [c for c in cands if c[0] < 1]
        s.verified = bool(ok)
        s.E = beta[:]
        if ok:
            # the last verified candidate (Neumann steps tighten v); any positive v is valid
            mu, v, Mv = ok[-1]
            q = dn(1 - mu)
            s.E = [uadd(beta[i], udiv(Mv[i], q)) for i in range(n)]
            s.mu, s.v = mu, v
        else:
            s.mu, s.v = s.mus[0], cands[0][1]
        s.Ex = [0.] * s.nfull
        for i, k in enumerate(s.free):
            s.Ex[k] = umul(s.cs[i], s.E[i])

    def functional(s, c):
        """Bound of c^T (x^ - x*) for a functional on the full state (c: dict full index -> float):
        beta_c = |z_c|^T d plus m_c^T E (verified state bounds), with m_c = sbar_c + dS^T |z_c|."""
        v = [0.] * s.n
        for k, ck in c.items():
            if k in s.col:
                i = s.col[k]
                v[i] = fmul(ck, s.cs[i])
        if not any(v):
            return 0.
        z, sbar = s.adjoint(v)
        b = udot(z, s.d)
        if s.verified:
            b = uadd(b, uadd(udot(sbar, s.E), udot(z, s.GS(s.E))))
        return b

    # -------------------------------------------------------------------------- evaluation
    def segof(s, x):
        lo, hi = 0, s.N
        while hi - lo > 1:
            m = (lo + hi) // 2
            if s.st[m] <= x:
                lo = m
            else:
                hi = m
        return lo

    def evaluate(s, x, side):
        """Item 20: normalised values and bounds (w, th, M, V) at x, side; evaluation directly
        from the start of the mechanical segment. Returns (values_n, bounds_n)."""
        if x in s.st:
            i = s.st.index(x)
            if side == 'R' and i < s.N:
                return [s.x[4 * i + q] for q in range(4)], [s.Ex[4 * i + q] for q in range(4)]
            j = i - 1
            sT = s.co[j]['a']
            sd = 'L'
        else:
            j = s.segof(x)
            sT = dist(x, s.st[j], s.eL, False)
            sd = side
        e = s.co[j]['e']
        o = 4 * j
        X = s.x[o:o + 4]
        E = s.Ex[o:o + 4]
        P = s.loadint(j, x, sd, False)
        g = [[T(1.0), sT, ((sT * sT) / 2) / e, (((sT * sT) * sT) / 6) / e],
             [None, T(1.0), sT / e, ((sT * sT) / 2) / e],
             [None, None, T(1.0), sT],
             [None, None, None, T(1.0)]]
        vals, bnds = [], []
        for q in range(4):
            acc = 0.0
            terms = []
            for k in range(4):
                if g[q][k] is None:
                    continue
                t = g[q][k] * T(X[k])
                terms.append(t)
                acc = fadd(acc, t.v)
            pv, pb, pm, pmn = P[q]
            # pm' = pm (1 + gb(pmn + 1)) >= |pv| (L2); only monotone magnitudes enter the bound
            pm1 = umul(pm, 1 + gb(pmn + 1))
            if q < 2:
                pvv = fdiv(pv, e.v)
                pmag = udiv(umul(pm1, 1 + gb(3)), abs(e.v))           # >= |pvv|
                pbb = uadd(udiv(umul(uadd(pb, umul(gb(2), pm1)), 1 + gb(2)), abs(e.v)), umul(U, pmag))
            else:
                pvv, pbb, pmag = pv, pb, pm1
            acc = fadd(acc, pvv)
            m = len(terms) + 1
            # propagated: sum |g_k| E_k with |g| <= |g^|(1 + gb(kg + 1))
            prop = usum(umul(umul(abs(g[q][k].v), 1 + gb(g[q][k].k + 1)), E[k]) for k in range(4) if g[q][k] is not None)
            ev = usum(umul(uadd(gb(t.k + 1) if t.k else 0.0, gb(m - 1)), abs(t.v)) for t in terms)
            ev = uadd(ev, umul(gb(m - 1), pmag))
            # underflow allowance: per operation LAM, amplified by |X_k|/e and the constants
            lam = umul(LAMD[0], umul(64.0, uadd(usum(abs(v) for v in X), 8.0)))
            lam = uadd(lam, udiv(lam, abs(e.v)))
            vals.append(acc)
            bnds.append(usum([prop, ev, pbb, lam]))
        return vals, bnds

    def to_si(s, vals, bnds):
        """Item 23: one exact power-of-two scaling per value; LAM if the SI value is not normal."""
        out_v, out_b = [], []
        for q in range(4):
            vq, bq = vals[q], bnds[q]
            if abs(vq) < MINN:                   # software flush before scaling (DAZ-safe)
                vq, bq = 0.0, uadd(bq, LAM)
            v = fscale(vq, s.Eq[q])
            b = umul(bq, 2.0 ** s.Eq[q]) if bq else 0.0
            if abs(v) <= MINN:                   # a result equal to MINN may be rounded up
                b = uadd(b, LAM)
            out_v.append(v)
            out_b.append(b)
        return out_v, out_b

    # -------------------------------------------------------------------------- reactions
    def reactions(s):
        """Items 18, 19: jump with its own adjoint bound; spring law with the state bound; the
        smaller bound is delivered. Normalised units; list of (kind, x, value_n, bound_n, q)."""
        out = []
        for kind, items, q, loads in (('R', s.tr, 3, s.pf), ('C', s.ro, 2, s.pm)):
            for a, kspring in items:
                i = s.st.index(a)
                sign = 1. if q == 3 else -1.
                c = {}
                cT = {}
                if i < s.N:
                    c[4 * i + q] = sign
                    cT[4 * i + q] = T(sign)
                const, cb = 0.0, 0.0
                if i > 0:
                    sm = s.left(i, False)[q]
                    for colk, t in sm[0].items():
                        c[colk] = c.get(colk, 0.) - sign * t.v
                        cT[colk] = T(-sign * t.v, t.k)
                    const, cb = -sign * sm[1], sm[2]
                ev = s.eV if q == 3 else s.eM
                Pl = [fscale(v, ev) for b_, v in loads if b_ == a]
                acc = 0.0
                for k, ck in c.items():
                    acc = fadd(acc, fmul(ck, s.x[k]))
                acc = fadd(acc, const)
                Ps = 0.0
                for v in Pl:
                    Ps = fadd(Ps, v)
                val = fsub(acc, Ps)
                m = len(c) + 2 + len(Pl)
                mag = usum([udot(list(c.values()), [s.x[k] for k in c]), abs(const), usum(abs(v) for v in Pl)])
                bj = s.functional(c)
                coef = usum(umul(bound_T(cT[k]), uadd(abs(s.x[k]), s.Ex[k])) for k in c)
                bj = usum([bj, coef, umul(gb(m), mag), cb, umul(LAM, 4.0 * m)])
                choice = ('jump', val, bj)
                if kspring != math.inf:
                    kk = 0 if q == 3 else 1
                    vp, ip = s.evaluate(a, 'R' if i < s.N else 'L')
                    kn = fscale(kspring, 3 * s.eL - s.eE if q == 3 else s.eL - s.eE)
                    law = fmul(-kn, vp[kk])
                    bl = usum([umul(kn, ip[kk]), umul(U, abs(law)), LAM])
                    if bl < bj:
                        choice = ('law', law, bl)
                out.append((kind, a, choice[1], choice[2], q, choice[0]))
        return out

    # -------------------------------------------------------------------------- scales and gate
    def F_low(s):
        """Lower bound of the normalised excitation (7.2, trapezoid of |q| per distributed load)."""
        t = 0.0
        for _, v in s.pf:
            t = dadd(t, abs(fscale(v, s.eV)))
        for la, lb, v1, v2 in s.lin:
            w = dmul(dmul(dadd(abs(fscale(v1, s.eq)), abs(fscale(v2, s.eq))), 0.5), dn(fscale(fsub(lb, la), -s.eL)))
            t = dadd(t, max(w, 0.))
        Ln = fscale(s.L, -s.eL)
        for _, v in s.pm:
            t = dadd(t, ddiv(abs(fscale(v, s.eM)), up(Ln)))
        return max(t, 0.)

    def scales_low(s, stvals):
        """Rigorous lower bounds (normalised units) of S_w, S_th, S_M, S_V (7.2) from the station
        values (both sides): max(|Q^| - beta) and the excitation terms."""
        mx = [0.] * 4
        for v, b in stvals:
            for q in range(4):
                mx[q] = max(mx[q], dsub(abs(v[q]), b[q]))
        F = s.F_low()
        Ln = fscale(s.L, -s.eL)
        eimin_up = min(up(umul(fscale(EI, -s.eE), 1 + gb(2))) for _, EI, _ in s.eis)
        SM = max(mx[2], dmul(F, dn(Ln)))
        SV = max(ddiv(SM, up(Ln)), F)
        St = max(mx[1], ddiv(dmul(SM, dn(Ln)), eimin_up))
        Sw = max(mx[0], ddiv(dmul(dmul(SM, dn(Ln)), dn(Ln)), eimin_up), dmul(St, dn(Ln)))
        return [max(Sw, 0.), max(St, 0.), max(SM, 0.), max(SV, 0.)]

    def qsum_up(s, x0, x1):
        """Upper bound of |q^| (computed data) on the piece: |qa| + |rise| of each clipped load."""
        j = s.segof(x0) if x0 < s.L else s.N - 1
        t = 0.0
        for ca, cb, qa, qb, da, db in s.loads[j][2]:
            if ca <= x0 and cb >= x1:
                t = uadd(t, uadd(abs(qa), up(abs(qb - qa))))
        return t

    def gate(s, events):
        """Station, reaction and piece gate (variant C) in normalised units with rigorous lower
        bounds of the denominators; and the SI range rule (S_q, upper bounds)."""
        stv = []
        for x in events:
            for sd in ('L', 'R'):
                if (x == 0. and sd == 'L') or (x == s.L and sd == 'R'):
                    continue
                stv.append(s.evaluate(x, sd))
        S = s.scales_low(stv)
        s.S_low = S
        # SI range rule
        for q in range(4):
            Ssi = dmul(S[q], 2.0 ** s.Eq[q]) if S[q] > 0 else 0.
            if not (SI_MIN <= Ssi):
                raise RangeError('SI scale %d' % q)
        G = 0.
        where = None
        for v, b in stv:
            for q in range(4):
                Dq = S[q] if q < 2 else max(S[q], dsub(abs(v[q]), b[q]))
                g = udiv(b[q], Dq)
                if g > G:
                    G, where = g, ('station', q)
        for kind, a, val, bnd, q, how in s.reactions():
            Dq = max(S[q], dsub(abs(val), bnd))
            g = udiv(bnd, Dq)
            if g > G:
                G, where = g, ('reaction', q)
        ubmax = [0.] * 4
        s.pieceinfo = []
        for x0, x1 in zip(events, events[1:]):
            v0, b0 = s.evaluate(x0, 'R')
            v1, b1 = s.evaluate(x1, 'L')
            h = up(fscale(up(x1 - x0), -s.eL))
            qs = s.qsum_up(x0, x1)
            for q in range(4):
                B = max(b0[q], b1[q])
                if q < 2:
                    Dq = S[q]
                elif q == 3:
                    lb = dsub(dsub(max(abs(v0[3]), abs(v1[3])), umul(h, qs)), umul(2.0, B))
                    Dq = max(S[q], lb)
                else:
                    BV = max(b0[3], b1[3])
                    lbs = []
                    for vv in (v0, v1):
                        lbs.append(dsub(dsub(abs(vv[2]), umul(uadd(abs(vv[3]), BV), h)), umul(umul(qs, umul(h, h)), 0.5)))
                    lb = dsub(max(lbs), umul(2.0, B))
                    Dq = max(S[q], lb)
                g = udiv(B, Dq)
                s.pieceinfo.append((x0, x1, q, Dq if q >= 2 and Dq > S[q] else 0., B, Dq))
                if g > G:
                    G, where = g, ('piece', q)
            # upper bounds of |Q| on the piece (overflow rule)
            ubV = uadd(max(abs(v0[3]), abs(v1[3])), umul(h, qs))
            ubmax[3] = max(ubmax[3], ubV)
            ubmax[2] = max(ubmax[2], uadd(uadd(abs(v0[2]), umul(ubV, h)), umul(qs, umul(h, h))))
            ubmax[1] = max(ubmax[1], abs(v0[1]), abs(v1[1]))
            ubmax[0] = max(ubmax[0], abs(v0[0]), abs(v1[0]))
        for q in range(4):
            if umul(uadd(ubmax[q], S[q]), 2.0 ** s.Eq[q]) > SI_MAX:
                raise RangeError('SI upper %d' % q)
        # SI allowance of the back-scaling: LAM / D_SI <= LAM / (S_q 2^E_q)
        Gsi = max(uadd(G, udiv(LAM, dmul(S[q], 2.0 ** s.Eq[q]))) for q in range(4))
        s.G, s.G_si, s.gate_where = G, Gsi, where
        return Gsi


def weights(m):
    """Verification weights D in scaled y units: rough reference scales per component type
    (any positive vector is valid; only the sharpness of mu depends on it)."""
    stv = []
    for i, x in enumerate(m.st):
        for sd in ('L', 'R'):
            if (i == 0 and sd == 'L') or (i == m.N and sd == 'R'):
                continue
            if x in m.st and sd == 'R':
                stv.append([abs(m.x[4 * i + q]) for q in range(4)])
    mx = [max([r[q] for r in stv] + [0.]) for q in range(4)]
    F = max(m.F_low(), MINN)
    Ln = 1.0
    SM = max(mx[2], F * Ln)
    SV = max(SM, F)
    eimin = min(fscale(EI, -m.eE) for _, EI, _ in m.eis)
    St = max(mx[1], SM / eimin)
    Sw = max(mx[0], SM / eimin, St)
    Sq = [Sw, St, SM, SV]
    return [Sq[k % 4] / m.cs[i] for i, k in enumerate(m.free)]


def solve_case(c, v_iter=3, adj_res='double', LUeps=None, exact_twin=False, mode='gradual'):
    """Status per 6.1 (contract order) and the solver object."""
    from kinematics import is_mechanism
    from coverage import events
    MODE[0] = mode
    try:
        if is_mechanism(c):
            return dict(status='UnstableModel'), None
        rng = random.Random(zlib.crc32(c['id'].encode())) if LUeps else None
        try:
            m = CS(c, v_iter=v_iter, adj_res=adj_res, LUeps=LUeps, rng=rng, exact_twin=exact_twin)
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), None
        if m.unloaded:
            return dict(status='zero'), m
        try:
            m.solve()
        except ZeroDivisionError:
            return dict(status='NotReliablySolvable', cause='ZeroPivot'), m
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), m
        except ArithmeticError:
            return dict(status='NumericalFailure', cause='NonFinite'), m
        m.verify(weights(m))
        ev = [float(x) for x in events(c)]
        try:
            G = m.gate(ev)
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), m
        if not math.isfinite(G):
            return dict(status='NumericalFailure', cause='NonFinite'), m
        if not m.verified:
            return dict(status='NotReliablySolvable', cause='NotVerified', gate=G, mu=m.mus), m
        if G > TAU:
            return dict(status='NotReliablySolvable', cause='Gate', gate=G, mu=m.mus), m
        return dict(status='delivered', gate=G, mu=m.mus), m
    finally:
        MODE[0] = 'gradual'
