"""Round 5, stage A: every contract formula against its derived bound (actual error <= bound),
exact reference in rational arithmetic, adversarial inputs, with gradual underflow and with
emulated flush-to-zero / denormals-are-zero (MODE 'ftz').

Families (FLOATING_POINT_CONTRACT.md, items):
  A1 distance fl(x - c) 2^-eL                         (item 1, 2)
  A2 transfer coefficients a, a/e, a a/2/e, a a a/6/e (items 3-5; range rule counted)
  A3 point-force and point-couple terms p = 1..4      (item 7)
  A4 load integrals of a segment (point loads, clipped linear loads, accumulation) through
     CS.loadint against its exact twin                 (items 6-8)
  A5 the same load integrals with the bound of state.py (K_DIST = 40, |t1| + |t2|): violations
     of the round-1..4 rounding model
  A6 residual sb - S y with its bound                  (items 14, 17)
  A7 upward/downward operations (L5)
  A8 field evaluation without the propagated part      (item 20)
  A9 SI back-scaling                                   (item 23)
Inputs: structured values (exact small integers and dyadics, all-ones mantissas, values next to
powers of two, ties, 1-ulp pairs, very large and very small exponents, subnormal-producing
combinations, cancellation) and random values.
Usage: python3 state/fpc_stage_a.py [N]          (summary and DATA lines)"""
import json, math, os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import fpc
from fpc import (T, dist, bound_T, point_force, point_couple, gb, U, MINN, LAM, fadd, fsub, fmul, fscale,
                 up, dn, uadd, umul, udiv, dsub, dmul, ddiv, udot, lam_term, RangeError, CS, MODE)
from gen import case, gen_f, gen_s
from state import StateSolver, gamma, ETA

R = random.Random(20261110)


def special():
    """Structured doubles."""
    m = R.choice(['int', 'dyad', 'ones', 'pow2m', 'pow2p', 'rand', 'tie'])
    e = R.randint(-60, 60)
    if m == 'int':
        v = float(R.randint(1, 2 ** 20))
    elif m == 'dyad':
        v = R.randint(1, 2 ** 10) * 2.0 ** -R.randint(0, 40)
    elif m == 'ones':
        v = (1 - 2.0 ** -53) * 2.0 ** e
    elif m == 'pow2m':
        v = math.nextafter(2.0 ** e, 0)
    elif m == 'pow2p':
        v = math.nextafter(2.0 ** e, math.inf)
    elif m == 'tie':
        v = (1 + 2.0 ** -R.randint(20, 30) + 2.0 ** -R.randint(31, 52)) * 2.0 ** e
    else:
        v = R.uniform(1, 2) * 2.0 ** e
    return v * R.choice((1, -1))


def stats():
    return dict(n=0, viol=0, worst=0., range=0)


def rec(st, err, bnd):
    st['n'] += 1
    if err > bnd:
        st['viol'] += 1
    r = float(err / Fr(bnd)) if bnd else (0. if err == 0 else math.inf)
    st['worst'] = max(st['worst'], r)


def pair_close():
    """Two coordinates: equal-exponent, 1 ulp, Sterbenz, wide, tiny."""
    k = R.choice(['ulp', 'sterbenz', 'wide', 'tiny', 'rand', 'subnormal'])
    x = abs(special()) if R.random() < .5 else R.uniform(0, 10)
    if k == 'ulp':
        c = math.nextafter(x, 0)
    elif k == 'sterbenz':
        c = x * R.uniform(.5, 1)
    elif k == 'wide':
        c = x * 2.0 ** -R.randint(2, 60)
    elif k == 'tiny':
        c = x * (1 - 2.0 ** -R.randint(30, 52))
    elif k == 'subnormal':
        x = MINN * (1 + R.randint(1, 2 ** 20) * 2.0 ** -52)
        c = MINN
    else:
        c = R.uniform(0, x)
    return x, c


def fam_dist(st):
    x, c = pair_close()
    if x <= c:
        return
    eL = R.randint(-5, 5)
    t = dist(x, c, eL, False)
    ex = (Fr(x) - Fr(c)) * Fr(2) ** -eL
    b = uadd(bound_T(t), LAM if (t.sub or abs(t.v) <= MINN) else 0.)
    rec(st, abs(Fr(t.v) - ex), b)


def fam_coef(st):
    x0, x1 = pair_close()
    x0, x1 = min(x0, x1), max(x0, x1)
    if x1 == x0:
        return
    E, I = abs(special()), abs(special())
    EI = fmul(E, I)
    if not (MINN <= EI < math.inf):
        st['range'] += 1
        return
    eL, eE = round(math.log2(abs(x1) or 1)), round(math.log2(EI)) + R.randint(-3, 0)
    a, ax = dist(x1, x0, eL, False), dist(x1, x0, eL, True)
    e, ex = T(fscale(EI, -eE), 1), T(Fr(E) * Fr(I) * Fr(2) ** -eE)
    out, outx = {}, {}
    for o, aa, ee in ((out, a, e), (outx, ax, ex)):
        sq = aa * aa
        o.update(a=aa, a_e=aa / ee, a2=(sq / 2) / ee, a3=((sq * aa) / 6) / ee)
    if any(t.sub or not (MINN < abs(t.v) < math.inf) for t in list(out.values()) + [e]):
        st['range'] += 1
        return
    for k in out:
        rec(st, abs(Fr(out[k].v) - outx[k].v), bound_T(out[k]))


def fam_point(st):
    v = special() * 2.0 ** -R.randint(0, 40)
    if abs(v) > 4:
        v = math.copysign(R.uniform(.1, 2), v)
    x, c = pair_close()
    if x <= c:
        return
    eL = round(math.log2(x)) if x > 0 else 0
    if x < 2 * fpc.SI_MIN:                       # beam-length range rule (L >= x)
        st['range'] += 1
        return
    fpc.LAMD[0] = max(LAM, math.ldexp(LAM, -eL))
    d, dx = dist(x, c, eL, False), dist(x, c, eL, True)
    for p in (1, 2, 3, 4):
        for f in (point_force, point_couple):
            if f is point_couple and p == 1:
                continue
            t = f(T(v), d, p)
            tx = f(T(Fr(v)), dx, p)
            rec(st, abs(Fr(t.v) - tx.v), uadd(bound_T(t), lam_term(v, p)))


def residual_case(st):
    m = R.randint(1, 8)
    S = [special() * 2.0 ** -R.randint(0, 60) for _ in range(m)]
    y = []
    for _ in range(m):
        k = R.random()
        y.append(0. if k < .1 else special() * 2.0 ** (-R.randint(900, 1015) if k < .25 else -R.randint(0, 80)))
    y = fpc.flush(y)
    sb = sum(S[j] * y[j] for j in range(m)) * (1 + R.choice((0, 2.0 ** -40, 1e-3))) if R.random() < .5 else special()
    acc = sb
    for j in range(m):
        acc = fsub(acc, fmul(S[j], y[j]))
    mag = uadd(abs(sb), udot(S, y))
    b = uadd(umul(gb(m + 1), mag), umul(LAM, 2.0 * m))
    ex = Fr(sb) - sum(Fr(S[j]) * Fr(y[j]) for j in range(m))
    rec(st, abs(Fr(acc) - ex), b)


def updown(st):
    a, b = abs(special()) * 2.0 ** -R.choice((0, 900, 1000)), abs(special()) * 2.0 ** -R.choice((0, 30, 100))
    for op, uop, dop in ((lambda p, q: p + q, uadd, None), (lambda p, q: p * q, umul, dmul), (lambda p, q: p / q, udiv, ddiv)):
        ex = op(Fr(a), Fr(b))
        st['n'] += 1
        if Fr(uop(a, b)) < ex:
            st['viol'] += 1
        if dop and Fr(dop(a, b)) > ex:
            st['viol'] += 1
        # under emulated FTZ the round-to-nearest result may be flushed: up() of the flushed value
        r = fpc._ftz(op(a, b)) if MODE[0] == 'ftz' else op(a, b)
        if ex != 0 and Fr(up(r)) < ex:
            st['viol'] += 1


def backscale(st):
    """Contract: a normalised value below MINN is flushed first (LAM in normalised units), then
    one power-of-two scaling, LAM if the SI result is not normal."""
    v = special() * 2.0 ** -R.choice((0, 500, 990, 1020))
    E = R.randint(-200, 200)
    vin, b = v, 0.
    if abs(vin) < MINN:
        vin, b = 0.0, umul(LAM, 2.0 ** E)
    r = fscale(vin, E)
    if math.isinf(r):
        st['range'] += 1
        return
    b = uadd(b, LAM if abs(r) <= MINN else 0.)        # a result equal to MINN may be rounded
    rec(st, abs(Fr(r) - Fr(v) * Fr(2) ** E), b)


# ---------------------------------------------------------------- model-level families
def models():
    out = []
    rng = random.Random(7)
    for k in range(25):
        out.append(gen_f(rng, 10.0 ** -rng.choice((2, 6, 9)), f'f|a|{k}'))
    rng = random.Random(8)
    for k in range(10):
        out.append(gen_s(rng, 10.0 ** -rng.choice((2, 6, 9)), f's|a|{k}'))
    L, E, I = 4., 210e9, 8e-6
    # linear load through zero, clipped at supports next to its zero crossing (cancellation in
    # the clipped intensity); the station is g away from the crossing
    for g in (1e-3, 1e-7, 1e-11, 0.):
        z = 2. + g
        out.append(case(f'zero-crossing|{g:g}', L, [(0., 'Pinned'), (z, 'Roller'), (math.nextafter(z, 9), 'Roller'), (L, 'Roller')],
                        lin=[(0.3, 3.7, -1e5, 1e5)], pf=[(1.1, -1e3)], E=E, I=I))
        out.append(case(f'zero-crossing-b|{g:g}', L, [(0., 'Pinned'), (z, 'Roller'), (z + 1e-9, 'Roller'), (L, 'Roller')],
                        lin=[(0.1, 3.9, 7e4, -7e4)], E=E, I=I))
    # short heavy loads (resultant 1e4 N) of length down to 1e-12 L and near-uniform loads
    for ln in (1e-3, 1e-8, 1e-12):
        out.append(case(f'short-heavy|{ln:g}', L, [(0., 'Pinned'), (2., 'Roller'), (L, 'Roller')],
                        lin=[(1.3, 1.3 + ln * L, 1e4 / (ln * L), 2e4 / (ln * L))], E=E, I=I))
    out.append(case('near-uniform', L, [(0., 'Pinned'), (1.7, 'Roller'), (L, 'Roller')], lin=[(0.2, 3.9, 5e3, 5e3 * (1 + 2. ** -50))], E=E, I=I))
    return out


def eval_exact_hat(m, x, side):
    """Exact evaluation of the contract formula with the computed state and exact data."""
    Fx = Fr(x)
    if x in m.st:
        i = m.st.index(x)
        if side == 'R' and i < m.N:
            return None
        j, sd = i - 1, 'L'
        s = m.co_x[j]['a'].v
    else:
        j = m.segof(x)
        s = (Fx - Fr(m.st[j])) * Fr(2) ** -m.eL
        sd = side
    e = m.co_x[j]['e'].v
    X = [Fr(v) for v in m.x[4 * j:4 * j + 4]]
    P = m.loadint(j, x, sd, True)
    g = [[1, s, s * s / 2 / e, s ** 3 / 6 / e], [0, 1, s / e, s * s / 2 / e], [0, 0, 1, s], [0, 0, 0, 1]]
    pv = [P[0][0] / e, P[1][0] / e, P[2][0], P[3][0]]
    return [sum(g[q][k] * X[k] for k in range(4)) + pv[q] for q in range(4)]


def model_families(stA4, stA5, stA8, mode):
    for c in models():
        MODE[0] = mode
        try:
            m = CS(c, exact_twin=True)
        except RangeError:
            stA4['range'] += 1
            continue
        if m.unloaded:
            continue
        try:
            m.solve()
        except (ZeroDivisionError, RangeError, ArithmeticError):
            m = None
        cs = CS(c, exact_twin=True) if m is None else m
        old = None
        try:
            MODE[0] = 'gradual'
            old = StateSolver(c)
        except Exception:
            pass
        MODE[0] = mode
        ev = sorted(set(cs.st) | {a for a, _ in cs.pf} | {x for l in cs.lin for x in l[:2]})
        xs = []
        for x0, x1 in zip(ev, ev[1:]):
            h = x1 - x0
            for f in (2. ** -50, 1e-9, .3, .5, .77, 1 - 1e-9):
                xs.append((x0 + h * f, 'R'))
            xs += [(x1, 'L'), (x0, 'R'), (math.nextafter(x0, x1), 'R'), (math.nextafter(x1, x0), 'R')]
        for x, sd in xs:
            if not (0 <= x <= cs.L):
                continue
            j = cs.segof(x) if not (x in cs.st and sd == 'L') else cs.st.index(x) - 1
            if j < 0 or j >= cs.N:
                continue
            P = cs.loadint(j, x, sd, False)
            Px = cs.loadint(j, x, sd, True)
            for (v, b, _, _), (vx, *_), p in zip(P, Px, (4, 3, 2, 1)):
                rec(stA4, abs(Fr(v) - vx), b)
            if old is not None and old.N == cs.N:
                # state.py bound of the same integrals, generously gamma(k + 3) ab + (k + 30) eta (its rows use
                # kb + 1 = k + 3 for w, theta; its evaluation adds up to (k + 30) eta)
                Po = old.loadint(j, x, sd)
                for (v, ab, k), (vx, *_) in zip(Po, Px):
                    # state.py has no right-hand side normalisation: compare in its units
                    scale = Fr(2) ** cs.eF
                    rec(stA5, abs(Fr(v) - vx * scale), gamma(k + 3) * ab + (k + 30) * ETA)
            if m is not None and not (x in m.st and sd == 'R' and m.st.index(x) < m.N):
                ex = eval_exact_hat(m, x, sd)
                if ex is None:
                    continue
                m.Ex = [0.] * m.nfull           # propagated part off: evaluation and data only
                v, b = m.evaluate(x, sd)
                for q in range(4):
                    rec(stA8, abs(Fr(v[q]) - ex[q]), b[q])
    MODE[0] = 'gradual'


if __name__ == '__main__':
    N = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
    out = {}
    for mode in ('gradual', 'ftz'):
        MODE[0] = mode
        fams = dict(A1=fam_dist, A2=fam_coef, A3=fam_point, A6=residual_case, A7=updown, A9=backscale)
        for name, f in fams.items():
            st = stats()
            for _ in range(N):
                f(st)
            out[(name, mode)] = st
        MODE[0] = 'gradual'
        a4, a5, a8 = stats(), stats(), stats()
        model_families(a4, a5, a8, mode)
        out[('A4', mode)], out[('A5', mode)], out[('A8', mode)] = a4, a5, a8
    names = dict(A1='distance', A2='coefficients', A3='point-load terms', A4='load integrals (contract)',
                 A5='load integrals (bound of state.py)', A6='residual', A7='up/down operations',
                 A8='evaluation (without propagation)', A9='SI back-scaling')
    for (name, mode), st in sorted(out.items()):
        print('DATA ' + json.dumps(dict(fam=name, mode=mode, **st)))
        print('%-3s %-36s %-8s checks %7d | error > bound %4d | largest error/bound %.3g | range rule %d' % (
            name, names[name], mode, st['n'], st['viol'], st['worst'], st['range']))
