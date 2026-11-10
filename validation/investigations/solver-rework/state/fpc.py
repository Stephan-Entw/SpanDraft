"""Executable bending contract from ../FLOATING_POINT_CONTRACT.md.

T counts normal coefficient roundings. Bounded propagates local errors and supplies
monotone magnitude envelopes for Variant C. The factored solution is a candidate
until the common primary/adjoint residual and the contraction test have verified it.
MODE selects gradual, ftz, daz or ftz_daz for modeled operations.
"""
import math, os, random, sys, zlib
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

U = float.fromhex('0x1p-53')
MINN = sys.float_info.min         # smallest normal number
LAM = MINN                  # absolute allowance per operation (gradual and abrupt underflow)
DELTA = float.fromhex('0x1p-200')         # share of the gate denominators in the verification weights
SI_MIN = float.fromhex('0x1p-969')        # SI reference scales must be >= SI_MIN (LAM / S <= u)
SI_MAX = float.fromhex('0x1p1000')        # and <= SI_MAX (no overflow of values and bounds)
LEN_MIN = float.fromhex('0x1p-969')       # scaled length of a distributed load (|q| LAM <= u)
TAU_DENOMINATOR = 100_000_000
TAU = Fr(1, TAU_DENOMINATOR)
MAX_DOUBLE = sys.float_info.max
MODE = ['gradual']


class RangeError(Exception):
    """NumericalRange: a quantity that the contract requires to be normal is not."""


class NotVerified(Exception):
    pass


class InternalInconsistency(ArithmeticError):
    pass


def power_of_two_exponent(x):
    if not (0 < x < math.inf):
        raise RangeError('binary exponent')
    return math.frexp(x)[1] - 1


def scale_by_power_of_two(x, e):
    try:
        return math.ldexp(x, e)
    except OverflowError:
        return math.copysign(math.inf, x)


def exact_scale(x, e):
    return x * (1 << e) if e >= 0 else x / (1 << -e)


def exact_exponent(x):
    e = x.numerator.bit_length() - x.denominator.bit_length()
    return e - (exact_scale(x, -e) < 1)


def stiffness_product(e, i):
    if not (MINN <= e <= MAX_DOUBLE and MINN <= i <= MAX_DOUBLE):
        raise RangeError('stiffness inputs')
    # as_integer_ratio exposes the binary significands and their power-of-two denominators.
    en, ed = e.as_integer_ratio()
    inn, ind = i.as_integer_ratio()
    n, d = en * inn, ed * ind
    if n << 1022 < d or n > int(MAX_DOUBLE) * d:
        raise RangeError('stiffness product')
    return e * i


# ------------------------------------------------------------------ modelled float operations
def _daz(a):
    return math.copysign(0.0, a) if MODE[0] in ('daz', 'ftz_daz') and 0 < abs(a) < MINN else a


def _ftz(r):
    return math.copysign(0.0, r) if MODE[0] in ('ftz', 'ftz_daz') and 0 < abs(r) < MINN else r


def fadd(a, b): return _ftz(_daz(a) + _daz(b))
def fsub(a, b): return _ftz(_daz(a) - _daz(b))
def fmul(a, b): return _ftz(_daz(a) * _daz(b))
def fdiv(a, b): return _ftz(_daz(a) / _daz(b))
def fscale(a, e): return _ftz(scale_by_power_of_two(_daz(a), e))


def flush(v):
    """Software flush of stored vectors: no subnormal component (exact for the a-posteriori
    relations, which hold for any stored vector)."""
    return [0.0 if abs(a) < MINN else a for a in v]


# ------------------------------------------------------------------ upward / downward bounds
def outward(r, upper):
    """Enclose a gradual RN result, or an unsigned zero from abrupt underflow."""
    if math.isnan(r):
        raise ArithmeticError('NaN in bound')
    if r == 0:
        return MINN if upper else -MINN
    if 0 < r < MINN:
        return MINN if upper else 0.0
    if -MINN < r < 0:
        return 0.0 if upper else -MINN
    neighbor = math.nextafter(r, math.inf if upper else -math.inf)
    if 0 < abs(neighbor) < MINN:
        return (MINN if neighbor > 0 else 0.0) if upper else (0.0 if neighbor > 0 else -MINN)
    return neighbor


def up(r): return outward(r, True)
def dn(r): return outward(r, False)


def uadd(a, b): return 0.0 if a == -b and math.isfinite(a) else up(a + b)
def umul(a, b): return 0.0 if a == 0 or b == 0 else up(a * b)
def udiv(a, b): return 0.0 if a == 0 and b > 0 else up(a / b)
def dadd(a, b): return 0.0 if a == -b and math.isfinite(a) else dn(a + b)
def dsub(a, b): return 0.0 if a == b and math.isfinite(a) else dn(a - b)
def dmul(a, b): return 0.0 if a == 0 or b == 0 else dn(a * b)
def ddiv(a, b): return 0.0 if a == 0 and b > 0 else dn(a / b)


def directed_scale(x, e, upper):
    if not x or math.isinf(x):
        return x
    r = scale_by_power_of_two(x, e)
    if MINN < abs(r) < math.inf:
        return r                        # scaling into this range is exact
    if r == 0:
        return (MINN if upper else 0.0) if x > 0 else (0.0 if upper else -MINN)
    if abs(r) == MINN and power_of_two_exponent(abs(x)) + e == -1022:
        return r
    return outward(r, upper)


def gamma_upper(k):
    if k == 0:
        return 0.0
    if not 0 < k < 1 << 53:
        raise RangeError('rounding count')
    ku = scale_by_power_of_two(float(k), -53)
    return udiv(ku, dsub(1.0, ku))


def threshold_down(d):
    if not (0 < d < math.inf):
        raise RangeError('gate denominator')
    r = d / TAU_DENOMINATOR
    rn, rd = r.as_integer_ratio()
    n, den = d.as_integer_ratio()
    if rn * TAU_DENOMINATOR * den > n * rd:
        r = math.nextafter(r, 0.0)
    return r if r >= MINN else 0.0


def is_unloaded(forces, couples, lines):
    return not (any(v for _, v in forces) or any(v for _, v in couples)
                or any(qa or qb for _, _, qa, qb in lines))


def excitation(length, forces, couples, lines):
    """Positive mantissa/exponent enclosure and floor binary exponent of its exact sum."""
    terms = []

    def point(x):
        m, e = math.frexp(abs(x))
        return m, m, e

    def product(a, b, divide=False):
        al, ah, ae = a
        bl, bh, be = b
        if divide:
            return ddiv(al, bh), udiv(ah, bl), ae - be
        return dmul(al, bl), umul(ah, bh), ae + be

    terms.extend(point(v) for _, v in forces if v)
    terms.extend(product(point(v), point(length), True) for _, v in couples if v)
    for a, b, qa, qb in lines:
        if not (qa or qb):
            continue
        width = Fr(b) - Fr(a)
        if width <= 0:
            raise RangeError('distributed load length')
        e = exact_exponent(width) + 1
        m = float(exact_scale(width, -e))
        for q in (qa, qb):
            if q:
                terms.append(product(point(q), (dn(m), up(m), e - 1)))
    if not terms:
        return False, 0, 0.0, 0.0
    e = max(t[2] for t in terms)
    lo, hi = 0.0, 0.0
    for a, b, k in terms:
        lo = max(lo, dadd(lo, directed_scale(a, k - e, False)))
        hi = uadd(hi, directed_scale(b, k - e, True))
    el, eh = power_of_two_exponent(lo), power_of_two_exponent(hi)
    if el != eh:
        exact = (sum(abs(Fr(v)) for _, v in forces)
                 + sum(abs(Fr(v)) / Fr(length) for _, v in couples)
                 + sum((abs(Fr(qa)) + abs(Fr(qb))) * (Fr(b) - Fr(a)) / 2
                       for a, b, qa, qb in lines))
        el = exact_exponent(exact) - e
    return True, e + el, directed_scale(lo, -el, False), directed_scale(hi, -el, True)


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


def local_rounding(result):
    if not math.isfinite(result):
        raise ArithmeticError('nonfinite operation')
    return LAM if abs(result) <= MINN else umul(U, abs(result))


def residual(rhs, products):
    """Enclose the error against the stored RHS and exact products of stored operands."""
    acc = 0.0 if abs(rhs) < MINN else rhs
    error = LAM if 0 < abs(rhs) < MINN else 0.0
    for a, b in products:
        if not (a and b):
            continue
        # Callers supply normal or zero matrix and solution components.
        p = fmul(a, b)
        error = uadd(error, local_rounding(p))
        if abs(p) < MINN:
            p = 0.0
        if not p:
            continue
        acc = fsub(acc, p)
        error = uadd(error, local_rounding(acc))
        if abs(acc) < MINN:
            acc = 0.0
    return acc, error


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
            return T(r, s.k + (0 if o & (o - 1) == 0 else 1), s.sub or s._mark(r) or (s.v != 0 and r == 0))
        if s.exact:
            return T(s.v * o.v)
        r = fmul(s.v, o.v)
        return T(r, s.k + o.k + 1, s.sub or o.sub or s._mark(r) or (s.v != 0 and o.v != 0 and r == 0))

    def __truediv__(s, o):
        if isinstance(o, int):
            if s.exact:
                return T(s.v / o)
            r = fdiv(s.v, float(o))
            return T(r, s.k + (0 if o & (o - 1) == 0 else 1), s.sub or s._mark(r) or (s.v != 0 and r == 0))
        if s.exact:
            return T(s.v / o.v)
        r = fdiv(s.v, o.v)
        return T(r, s.k + o.k + 1, s.sub or o.sub or s._mark(r) or (s.v != 0 and o.v != 0 and r == 0))

    def __add__(s, o):
        if s.exact:
            return T(s.v + o.v)
        assert s.v == 0 or o.v == 0 or (s.v > 0) == (o.v > 0), 'cancellation in a positive chain'
        r = fadd(s.v, o.v)
        return T(r, max(s.k, o.k) + 1, s.sub or o.sub or s._mark(r) or (s.v != 0 and o.v != 0 and r == 0))

    def __neg__(s):
        return T(-s.v, s.k, s.sub)


def inp(v, exact):
    return T(Fr(v)) if exact else T(float(v))


def dist(x, c, eL, exact):
    """Scaled distance (x - c) 2^-eL from two stored coordinates: one rounding (invariant 4)."""
    if exact:
        return T((Fr(x) - Fr(c)) * Fr(2) ** -eL)
    raw = fsub(x, c)
    r = fscale(raw, -eL)
    return T(r, 1, x != c and (abs(raw) <= MINN or abs(r) <= MINN))


def computed_bound(k, value):
    if k == 0:
        return 0.0
    gamma = gamma_upper(k)
    if gamma >= 1:
        raise RangeError('relative data bound')
    return umul(udiv(gamma, dsub(1.0, gamma)), abs(value))


def bound_T(t):
    return computed_bound(t.k, t.v)


class Bounded:
    """Computed value, absolute error, and a magnitude majorant for the computed value.

    Only the value uses signed arithmetic. Error and magnitude are built from monotone
    nonnegative expressions, also for cancellation in a sum or difference.
    """
    __slots__ = ('v', 'error', 'mag')

    def __init__(self, value, error=0.0, magnitude=None):
        if not math.isfinite(value):
            raise RangeError('local formula')
        self.v = value
        self.error = error
        self.mag = abs(value) if magnitude is None else magnitude

    @staticmethod
    def operand(o):
        return Bounded(float(o)) if isinstance(o, int) else o

    @property
    def with_daz(self):
        return uadd(self.error, min(self.mag, LAM))

    @staticmethod
    def result(value, error, magnitude):
        rounding = uadd(umul(U, magnitude), LAM)
        return Bounded(value, uadd(error, rounding), uadd(magnitude, rounding))

    def __mul__(self, other):
        other = self.operand(other)
        if (self.mag == self.error == 0) or (other.mag == other.error == 0):
            return Bounded(0.0)
        a, b = self.with_daz, other.with_daz
        error = uadd(umul(a, uadd(other.mag, b)), umul(self.mag, b))
        return self.result(fmul(self.v, other.v), error, umul(self.mag, other.mag))

    def __truediv__(self, other):
        other = self.operand(other)
        d = abs(other.v)
        dl = dsub(d, other.error)
        if not (d >= MINN and dl > 0):
            raise RangeError('local formula divisor')
        if self.mag == self.error == 0:
            return Bounded(0.0)
        magnitude = udiv(self.mag, d)
        error = udiv(uadd(self.with_daz, umul(magnitude, other.error)), dl)
        return self.result(fdiv(self.v, other.v), error, magnitude)

    def __add__(self, other):
        if self.mag == self.error == 0:
            return other
        if other.mag == other.error == 0:
            return self
        error = uadd(self.with_daz, other.with_daz)
        return self.result(fadd(self.v, other.v), error, uadd(self.mag, other.mag))

    def __neg__(self):
        return Bounded(-self.v, self.error, self.mag)

    def __sub__(self, other):
        return self + -other


def bounded_dist(x, c, exponent):
    if x == c:
        return Bounded(0.0)
    # Stored coordinates are exact even if an evaluation coordinate is subnormal.
    # The protected subtraction uses gradual RN before one modeled scaling.
    raw = x - c
    value = _ftz(scale_by_power_of_two(raw, -exponent))
    error = uadd(umul(U, abs(value)),
                 uadd(directed_scale(LAM, -exponent, True), LAM))
    return Bounded(value, error)


def bounded_sum(terms):
    total = Bounded(0.0)
    for term in terms:
        total = total + term
    return total


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


def clip(la, lb, v1, v2, ca, cb, exact, exponent):
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
        if v1 == v2:
            out.append((v1, 0.0))
            continue
        n = bounded_dist(c, la, exponent)
        d = bounded_dist(lb, la, exponent)
        q = Bounded(v1) + (Bounded(v2) - Bounded(v1)) * (n / d)
        out.append((q.v, q.error))
    return out


# ------------------------------------------------------------------ the solver
class CS:
    """Contract solver with optional perturbations of the candidate factorisation."""

    def __init__(s, c, v_iter=3, LUeps=None, rng=None, exact_twin=False):
        s.c = c
        s.v_iter, s.LUeps, s.rng = v_iter, LUeps, rng
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
        EI0 = stiffness_product(E, I)
        s.eis = [(0.0, EI0, 1)] + [(float(x), float(v), 0) for x, v in c.get('ei', [])]
        if not (MINN <= L <= MAX_DOUBLE and all(MINN <= e <= MAX_DOUBLE for _, e, _ in s.eis)):
            raise RangeError('reference scale')
        s.eis_exact = [(Fr(0), Fr(E) * Fr(I))] + [(Fr(float(x)), Fr(float(v))) for x, v in c.get('ei', [])]
        s.eL = power_of_two_exponent(L)
        s.eE = power_of_two_exponent(max(e for _, e, _ in s.eis))
        if any(fscale(e, -s.eE) <= MINN for _, e, _ in s.eis):
            raise RangeError('normalised stiffness')
        if L < SI_MIN * 2:
            raise RangeError('beam length')
        s.pf = [(float(a), float(v)) for a, v in c['pf']]
        s.pm = [(float(a), float(v)) for a, v in c['pm']]
        s.lin = [(float(a), float(b), float(v), float(v)) for a, b, v in c['q']] + [tuple(map(float, l)) for l in c.get('lin', [])]
        s.unloaded = is_unloaded(s.pf, s.pm, s.lin)
        s.eF, s.excitation_lower, s.excitation_upper = 0, 0.0, 0.0
        if not s.unloaded:
            _, force_exp, s.excitation_lower, s.excitation_upper = excitation(L, s.pf, s.pm, s.lin)
            s.eF = force_exp + 2 * s.eL - s.eE
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
        for values, exponent in (([v for _, v in s.pf], s.eV),
                                 ([v for _, v in s.pm], s.eM),
                                 ([v for line in s.lin for v in line[2:]], s.eq)):
            if any(v != 0 and not MINN < abs(fscale(v, exponent)) < math.inf for v in values):
                raise RangeError('normalised excitation')
        for la, lb, v1, v2 in s.lin:
            d = fscale(fsub(lb, la), -eL)
            if d < LEN_MIN:
                raise RangeError('distributed load length')
        # distances between distinct mechanical stations: normal, and the coefficient chains
        for x0, x1 in zip(st, st[1:]):
            if fsub(x1, x0) <= MINN:
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
            (qa, da), (qb, db) = clip(la, lb, w1, w2, ca, cb, exact, s.eL)
            lins.append((ca, cb, qa, qb, da, db))
        return pts, mom, lins

    def loadint(s, j, x, side, exact):
        """Per p in (4, 3, 2, 1): value, error bound (contract data + evaluation rounding),
        magnitude sum |terms| (monotone in x), number of terms. Order: point forces, couples,
        distributed loads (term qa*Diff, then ramp), each in input order."""
        pts, mom, lins = s.loads[j] if not exact else s.loads_x[j]
        eL = s.eL
        out = []
        distance = (lambda a, b: dist(a, b, eL, True)) if exact else (lambda a, b: bounded_dist(a, b, eL))
        number = T if exact else Bounded
        for p in (4, 3, 2, 1):
            terms = []
            for cpos, v in pts:
                if x > cpos or (p == 1 and x == cpos and side == 'R'):
                    terms.append(point_force(number(v), distance(x, cpos), p))
            for cpos, v in mom:
                if p >= 2 and (x > cpos or (p == 2 and x == cpos and side == 'R')):
                    terms.append(point_couple(number(v), distance(x, cpos), p))
            for ca, cb, qa, qb, da, db in lins:
                if not x > ca:
                    continue
                inside = x <= cb
                n = distance(x, ca)
                far = None if inside else distance(x, cb)
                dd = distance(cb, ca)
                a, b = (T(qa), T(qb)) if exact else (Bounded(qa, da), Bounded(qb, db))
                rise = T(qb - qa) if exact else b - a
                terms.append(diff_term(a, n, far, dd, p, inside))
                terms.append(ramp_term(rise, n, far, dd, p, inside))
            if exact:
                out.append((sum((t.v for t in terms), Fr(0)), 0., 0., len(terms)))
            else:
                total = bounded_sum(terms)
                out.append((total.v, total.error, total.mag, len(terms)))
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
                cv = Bounded(val, bnd, mag) / Bounded(e.v, bound_T(e))
                out.append((coefs, cv.v, cv.error))
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
                P = bounded_sum(Bounded(v) for v in pv)
                C = bounded_sum(Bounded(v) for v in cv)
                Pst, Cst, Pb, Cb = P.v, C.v, P.error, C.error
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
                scaled = []
                for springs, exponent in ((kws, 3 * s.eL - s.eE), (kts, s.eL - s.eE)):
                    terms = []
                    for k in springs:
                        v = fscale(k, exponent)
                        if k and not MINN < v < math.inf:
                            raise RangeError('spring')
                        terms.append(v)
                    total = 0.0
                    for value in terms:
                        total = fadd(total, value)
                    if total and not MINN < total < math.inf:
                        raise RangeError('spring sum')
                    scaled.append(total)
                kw, kt = scaled
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
                    r = Bounded(sm[q][1], sm[q][2]) + Bounded(const, cbnd)
                    rows.append((d, r.v, r.error))

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
            raise InternalInconsistency('rows %d unknowns %d' % (len(rows), n))
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
        rs, cs = [0] * n, [0] * n

        def correction(entries):
            maxima = [((m := math.frexp(abs(a)))[1] - 1 + e, m[0]) for a, e in entries if a]
            if not maxima:
                return 0
            e, mantissa = max(maxima)
            half = e // 2
            return half + (e % 2 != 0 and (mantissa != 0.5 or half % 2 != 0))

        for _ in range(8):
            for i in range(n):
                rs[i] -= correction((A[i][j], rs[i] + cs[j]) for j in range(n))
            for j in range(n):
                cs[j] -= correction((A[i][j], rs[i] + cs[j]) for i in range(n))
        s.rs, s.cs = rs, cs
        S = [[fscale(A[i][j], rs[i] + cs[j]) for j in range(n)] for i in range(n)]
        for i in range(n):
            for j in range(n):
                if A[i][j] != 0 and not (MINN < abs(S[i][j]) < math.inf):
                    raise RangeError('equilibrated entry')
        s.S = S
        s.sb = [_ftz(scale_by_power_of_two(s.b[i], rs[i])) for i in range(n)]
        if not all(map(math.isfinite, s.sb)):
            raise RangeError('equilibrated RHS')
        # bounds of the data errors of the equilibrated system (L3, exact power-of-two scaling)
        s.dS = [[computed_bound(s.K[i][j], S[i][j]) for j in range(n)] for i in range(n)]
        s.dsb = [uadd(directed_scale(s.bb[i], rs[i], True), LAM if s.b[i] != 0 and abs(s.sb[i]) <= MINN else 0.0) for i in range(n)]
        s.nz = [[j for j in range(n) if S[i][j] != 0] for i in range(n)]
        s.cols = [[i for i in range(n) if S[i][j] != 0] for j in range(n)]
        LU = [row[:] for row in S]
        piv = list(range(n))
        for k in range(n):
            p = max(range(k, n), key=lambda i: abs(LU[i][k]))
            if LU[p][k] == 0:
                raise ZeroDivisionError('zero pivot')
            if abs(LU[p][k]) <= MINN:
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
            s.x[k] = fscale(y[i], cs[i])
            if not math.isfinite(s.x[k]):
                raise RangeError('unscaled state')
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
        """Add equilibrated data errors to the residual of the stored system."""
        S, n, y = s.S, s.n, s.y
        s.res, s.dr, s.d = [0.] * n, [0.] * n, [0.] * n
        for i in range(n):
            acc, dr = residual(s.sb[i], ((S[i][j], y[j]) for j in s.nz[i]))
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
        if not all(map(math.isfinite, z)):
            raise ArithmeticError('nonfinite adjoint')
        s.adj_steps += steps
        s.adj_solves += 1
        sbar = []
        for j in range(n):
            col = s.cols[j]
            m = len(col)
            rj, dr = residual(cvec[j], ((S[i][j], z[i]) for i in col))
            b = uadd(abs(rj), dr)
            sbar.append(b)
        return z, sbar

    def Mv(s, z, sbar, GSv, v):
        return uadd(udot(sbar, v), udot(z, GSv))

    def GS(s, v):
        return [udot([s.dS[k][j] for j in s.nz[k]], [v[j] for j in s.nz[k]]) for k in range(s.n)]

    def verify(s, weights_D):
        """Items 16, 17: adjoints of all unknowns, RawBound_i = |z_i|^T d, verification of
        |e| <= raw + M|e| with M = |S_res| + |Z|^T dS (exact relation C = I - R S*), weights
        v = raw + DELTA D and Neumann steps v <- raw + M v + DELTA D."""
        n = s.n
        s.adj_steps = s.adj_solves = 0
        Z, SB, raw = [], [], []
        for i in range(n):
            z, sbar = s.adjoint([1. if k == i else 0. for k in range(n)])
            Z.append(z)
            SB.append(sbar)
            raw.append(udot(z, s.d))
        s.Z, s.SB, s.raw_bound = Z, SB, raw
        D = weights_D
        cands = []
        v = [uadd(raw[j], umul(DELTA, D[j])) for j in range(n)]
        for it in range(s.v_iter + 1):
            GSv = s.GS(v)
            Mv = [s.Mv(Z[i], SB[i], GSv, v) for i in range(n)]
            if not all(MINN <= w < math.inf for w in v) or not all(map(math.isfinite, Mv)):
                break
            mu = max((udiv(Mv[i], v[i]) for i in range(n)), default=0.)
            cands.append((mu, v, Mv))
            v = [uadd(uadd(raw[j], Mv[j]), umul(DELTA, D[j])) for j in range(n)]
        s.mus = [c[0] for c in cands]
        ok = [c for c in cands if c[0] < 1]
        s.verified = False
        s.verified_state_bound = None
        if not ok:
            s.mu, s.v = (s.mus[0], cands[0][1]) if cands else (math.inf, None)
            return
        # Any positive weight vector with a verified contraction is valid.
        s.mu, s.v, Mv = ok[-1]
        denominator = dsub(1., s.mu)
        state_bound = [uadd(raw[i], udiv(Mv[i], denominator)) for i in range(n)]
        if not all(map(math.isfinite, state_bound)):
            raise RangeError('verified state bound')
        s.Ex = [0.] * s.nfull
        s.state_scale_error = [0.] * s.nfull
        for i, k in enumerate(s.free):
            b = directed_scale(state_bound[i], s.cs[i], True)
            loss = LAM if s.y[i] and abs(s.x[k]) <= MINN else 0.
            s.state_scale_error[k] = loss
            s.Ex[k] = uadd(b, loss) if loss else b
        if not all(map(math.isfinite, s.Ex)):
            raise RangeError('unscaled state bound')
        s.x = flush(s.x)
        s.verified_state_bound = state_bound
        s.verified = True

    def functional(s, c):
        """Bound of c^T (x^ - x*) for a functional on the full state (c: dict full index -> float):
        Add m_c^T VerifiedStateBound to RawBound_c = |z_c|^T d, where
        m_c = sbar_c + dS^T |z_c|. Include both state and coefficient scale losses."""
        v = [0.] * s.n
        for k, ck in c.items():
            if k in s.col:
                i = s.col[k]
                v[i] = fscale(ck, s.cs[i])
        if not s.verified:
            raise NotVerified()
        scaling_error = usum(umul(abs(ck), s.state_scale_error[k]) for k, ck in c.items())
        for k, ck in c.items():
            if k in s.col:
                i = s.col[k]
                if ck and abs(v[i]) <= MINN:
                    scaling_error = uadd(scaling_error, umul(LAM, s.verified_state_bound[i]))
        if not all(map(math.isfinite, v)):
            raise RangeError('functional scaling')
        if not any(v):
            return scaling_error
        z, sbar = s.adjoint(v)
        b = udot(z, s.d)
        return usum((b, scaling_error, udot(sbar, s.verified_state_bound),
                     udot(z, s.GS(s.verified_state_bound))))

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
        if s.unloaded:
            return [0.] * 4, [0.] * 4
        if x in s.st:
            i = s.st.index(x)
            if side == 'R' and i < s.N:
                return [s.x[4 * i + q] for q in range(4)], [s.Ex[4 * i + q] for q in range(4)]
            j = i - 1
            sT = bounded_dist(x, s.st[j], s.eL)
            sd = 'L'
        else:
            j = s.segof(x)
            sT = bounded_dist(x, s.st[j], s.eL)
            sd = side
        e = s.co[j]['e']
        e = Bounded(e.v, bound_T(e))
        o = 4 * j
        X = [Bounded(s.x[k], s.Ex[k]) for k in range(o, o + 4)]
        P = s.loadint(j, x, sd, False)
        one = Bounded(1.0)
        a2 = ((sT * sT) / 2) / e
        g = [[one, sT, a2, (((sT * sT) * sT) / 6) / e],
             [None, one, sT / e, a2],
             [None, None, one, sT],
             [None, None, None, one]]
        vals, bnds = [], []
        for q in range(4):
            field = bounded_sum(g[q][k] * X[k] for k in range(4) if g[q][k] is not None)
            pv, pb, pm, _ = P[q]
            load = Bounded(pv, pb, pm)
            field = field + (load / e if q < 2 else load)
            vals.append(field.v)
            bnds.append(field.error)
        return vals, bnds

    def to_si_value(s, v, b, q):
        if s.unloaded:
            return 0., 0.
        if 0 < abs(v) < MINN:
            v, b = 0.0, uadd(b, LAM)
        v = fscale(v, s.Eq[q])
        b = directed_scale(b, s.Eq[q], True)
        if abs(v) <= MINN:
            b = uadd(b, LAM)
        return v, b

    def to_si(s, vals, bnds):
        """Item 23: one exact power-of-two scaling per value; LAM if the SI value is not normal."""
        return tuple(zip(*(s.to_si_value(v, b, q) for q, (v, b) in enumerate(zip(vals, bnds)))))

    # -------------------------------------------------------------------------- reactions
    def reactions(s):
        """Items 18, 19: jump with its own adjoint bound; spring law with the state bound; the
        smaller bound is delivered. Normalised units; list of (kind, x, value_n, bound_n, q)."""
        out = []
        for kind, items, q, loads in (('R', s.tr, 3, s.pf), ('C', s.ro, 2, s.pm)):
            for a, kspring in items:
                if s.unloaded:
                    out.append((kind, a, 0., 0., q, 'zero'))
                    continue
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
                estimate = (bounded_sum(Bounded(ck) * Bounded(s.x[k]) for k, ck in c.items())
                            + Bounded(const, cb) - bounded_sum(Bounded(v) for v in Pl))
                coef = usum(umul(bound_T(cT[k]), uadd(abs(s.x[k]), s.Ex[k])) for k in c)
                bj = usum((estimate.error, s.functional(c), coef))
                choice = ('jump', estimate.v, bj)
                if kspring != math.inf:
                    kk = 0 if q == 3 else 1
                    vp, ip = s.evaluate(a, 'R' if i < s.N else 'L')
                    kn = fscale(kspring, 3 * s.eL - s.eE if q == 3 else s.eL - s.eE)
                    if kspring and not MINN < kn < math.inf:
                        raise RangeError('spring reaction')
                    law = Bounded(-kn) * Bounded(vp[kk], ip[kk])
                    if sum(x == a for x, _ in items) == 1:
                        gap = max(dsub(estimate.v, law.v), dsub(law.v, estimate.v))
                        if gap > uadd(bj, law.error):
                            raise InternalInconsistency('spring reaction identity')
                    if law.error < bj:
                        choice = ('law', law.v, law.error)
                out.append((kind, a, choice[1], choice[2], q, choice[0]))
        return out

    # -------------------------------------------------------------------------- scales and gate
    def F_low(s):
        """Lower bound of the normalised excitation (7.2, trapezoid of |q| per distributed load)."""
        return s.excitation_lower

    def scales_low(s, stvals):
        """Rigorous lower bounds (normalised units) of S_w, S_th, S_M, S_V (7.2) from the station
        values (both sides): max(|Q^| - beta) and the excitation terms."""
        mx = [0.] * 4
        for v, b in stvals:
            for q in range(4):
                mx[q] = max(mx[q], dsub(abs(v[q]), b[q]))
        F = s.F_low()
        Ln = fscale(s.L, -s.eL)
        eimin_up = min(uadd(v, computed_bound(k, v))
                        for v, k in ((fscale(EI, -s.eE), k) for _, EI, k in s.eis))
        SM = max(mx[2], dmul(F, dn(Ln)))
        SV = max(ddiv(SM, up(Ln)), F)
        St = max(mx[1], ddiv(dmul(SM, dn(Ln)), eimin_up))
        Sw = max(mx[0], ddiv(dmul(dmul(SM, dn(Ln)), dn(Ln)), eimin_up), dmul(St, dn(Ln)))
        return [max(Sw, 0.), max(St, 0.), max(SM, 0.), max(SV, 0.)]

    def scales_high(s, stvals):
        mx = [max((uadd(abs(v[q]), b[q]) for v, b in stvals), default=0.)
              for q in range(4)]
        Ln = fscale(s.L, -s.eL)
        eimin = min(dsub(v, computed_bound(k, v))
                    for v, k in ((fscale(EI, -s.eE), k) for _, EI, k in s.eis))
        F = s.excitation_upper
        SM = max(mx[2], umul(F, Ln))
        SV = max(udiv(SM, Ln), F)
        St = max(mx[1], udiv(umul(SM, Ln), eimin))
        Sw = max(mx[0], udiv(umul(umul(SM, Ln), Ln), eimin), umul(St, Ln))
        return [Sw, St, SM, SV]

    def qsum_up(s, x0, x1):
        """Upper bound of the sum of exact absolute intensities on the piece."""
        j = s.segof(x0) if x0 < s.L else s.N - 1
        t = 0.0
        for ca, cb, qa, qb, da, db in s.loads[j][2]:
            if ca <= x0 and cb >= x1:
                t = uadd(t, max(uadd(abs(qa), da), uadd(abs(qb), db)))
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
        S_high = s.scales_high(stv)
        # SI range rule
        for q in range(4):
            Ssi = directed_scale(S[q], s.Eq[q], False)
            if not (SI_MIN <= Ssi and directed_scale(S_high[q], s.Eq[q], True) <= SI_MAX):
                raise RangeError('SI scale %d' % q)
        G = 0.
        where = None
        s.gate_passed = True

        def check(bound, denominator, q):
            D = directed_scale(denominator, s.Eq[q], False)
            B = uadd(directed_scale(uadd(bound, LAM), s.Eq[q], True), LAM)
            if not math.isfinite(B):
                raise RangeError('SI bound')
            s.gate_passed &= B <= threshold_down(D)
            return udiv(B, D)
        for v, b in stv:
            for q in range(4):
                Dq = S[q] if q < 2 else max(S[q], dsub(abs(v[q]), b[q]))
                g = check(b[q], Dq, q)
                if g > G:
                    G, where = g, ('station', q)
        for kind, a, val, bnd, q, how in s.reactions():
            if directed_scale(abs(val), s.Eq[q], True) > SI_MAX:
                raise RangeError('SI reaction')
            Dq = max(S[q], dsub(abs(val), bnd))
            g = check(bnd, Dq, q)
            if g > G:
                G, where = g, ('reaction', q)
        ubmax = [0.] * 4
        s.pieceinfo = []
        for x0, x1 in zip(events, events[1:]):
            v0, b0 = s.evaluate(x0, 'R')
            v1, b1 = s.evaluate(x1, 'L')
            h = directed_scale(up(x1 - x0), -s.eL, True)
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
                g = check(B, Dq, q)
                s.pieceinfo.append((x0, x1, q, Dq if q >= 2 and Dq > S[q] else 0., B, Dq))
                if g > G:
                    G, where = g, ('piece', q)
            B = [max(a, b) for a, b in zip(b0, b1)]
            ubV = usum((abs(v0[3]), B[3], umul(h, qs)))
            ubM = usum((abs(v0[2]), B[2], umul(ubV, h)))
            e = s.co[s.segof(x0)]['e']
            el = dsub(e.v, bound_T(e))
            ubT = usum((abs(v0[1]), B[1], udiv(umul(ubM, h), el)))
            ubW = usum((abs(v0[0]), B[0], umul(ubT, h)))
            for q, v in enumerate((ubW, ubT, ubM, ubV)):
                ubmax[q] = max(ubmax[q], uadd(v, B[q]))
        for q in range(4):
            if directed_scale(ubmax[q], s.Eq[q], True) > SI_MAX:
                raise RangeError('SI upper %d' % q)
        s.G, s.G_si, s.gate_where = G, G, where
        return G


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
    return [fscale(Sq[k % 4], -m.cs[i]) for i, k in enumerate(m.free)]


def solve_case(c, v_iter=3, LUeps=None, exact_twin=False, mode='gradual'):
    """Status per 6.1 (contract order) and the solver object."""
    from kinematics import is_mechanism
    from coverage import events
    MODE[0] = mode
    try:
        if is_mechanism(c):
            return dict(status='UnstableModel'), None
        rng = random.Random(zlib.crc32(c['id'].encode())) if LUeps else None
        try:
            m = CS(c, v_iter=v_iter, LUeps=LUeps, rng=rng, exact_twin=exact_twin)
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), None
        except InternalInconsistency as e:
            return dict(status='NumericalFailure', cause='InternalInconsistency', detail=str(e)), None
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
        try:
            m.verify(weights(m))
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), m
        except ArithmeticError:
            return dict(status='NumericalFailure', cause='NonFinite'), m
        if not m.verified:
            return dict(status='NotReliablySolvable', cause='NotVerified', mu=m.mus), m
        ev = [float(x) for x in events(c)]
        try:
            G = m.gate(ev)
        except RangeError as e:
            return dict(status='NumericalFailure', cause='NumericalRange', detail=str(e)), m
        except InternalInconsistency as e:
            return dict(status='NumericalFailure', cause='InternalInconsistency', detail=str(e)), m
        except ArithmeticError:
            return dict(status='NumericalFailure', cause='NonFinite'), m
        if not math.isfinite(G):
            return dict(status='NumericalFailure', cause='NonFinite'), m
        if not m.gate_passed:
            return dict(status='NotReliablySolvable', cause='Gate', gate=G, mu=m.mus), m
        return dict(status='delivered', gate=G, mu=m.mus), m
    finally:
        MODE[0] = 'gradual'
