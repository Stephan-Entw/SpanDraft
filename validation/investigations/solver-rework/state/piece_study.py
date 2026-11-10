"""Round 4, phases 3 and 4: independent check of the piece gate (variant C) and of the extremum
candidates against exact oracles (piece_oracle.py).

Phase 3, per piece and quantity M, V with LB_C > 0 (variant C of 7.3, as in gate_study.py):
  - certified enclosure [lo, hi] of min |p_hat| (computed field, exact arithmetic) and of
    min |p| (exact field) by exact Bernstein subdivision;
  - LB_C <= min |p_hat| ?  (violation if LB_C > hi; undecided if lo < LB_C <= hi)
  - margin (lo_hat - LB_C) / beta(x1-): share of the beta deduction still unused
    (the floating-point evaluation differs from p_hat by less than its evaluation rounding);
  - LB_C <= min |p| ?  (exact field; holds only as far as beta covers the error)
  - Bernstein as product alternative: D_B = max(S, lo_hat - beta(x1-)); G_B vs G_C, and the
    model decision with either.
Phase 4, per piece and quantity w (roots of th), M (roots of V), V (roots of q), all in t:
  - exact distinct roots in [0, 1] of the derivative of p_hat and of p, with multiplicities
    (Yun + Sturm), cross-checked with sympy;
  - product candidates: piece polynomial from the computed state at x0+ in floating point and
    the port of PolynomialRoots.InUnitInterval; candidate position x = fl(x0 + t h), side from
    the exact comparison with x0, x1; value by EvaluateAt (state.py) at that x and side, so the
    reported value is reproducible by construction (checked);
  - completeness: every odd-multiplicity interior root of p_hat' (a local extremum of p_hat)
    has a candidate within |dt| <= 1e-4 (else: missed, with its effect on the piece's extreme
    value); candidates without a root of p_hat' within 1e-4 (spurious);
  - position effect: |p_hat(t_c) - p_hat(t*)| against beta at the candidate and against the
    second-order prediction |p_hat''(t*)| dt^2 / 2 (only meaningful for simple roots);
  - global extrema (max and min of w, M, V over the beam): product selection rule of
    BeamExtrema (relative tie 64 eps, smallest x, Left before Right) against the exact extrema;
    error in the mixed measure (exact scales); delivered models must have error <= tau.
Usage: python3 state/piece_study.py targeted | random K | constructed     (DATA lines)"""
import json, math, os, random, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from coverage import events
from gate_study import qsum, piece_gates
from gen import case, gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales, denom, Q
from piece_oracle import (piece_polys, peval, deriv, min_abs, roots01, sympy_count, in_unit_interval, MP)
from state import StateSolver, gamma

TAU = 1e-8
DT = 1e-4


def loads_on_piece(c, x0, x1):
    """q at x0+ and the slope dq/dx on the piece (floating point, like the product)."""
    q0 = qs = 0.
    for a, b, v in c['q']:
        if a <= x0 and b >= x1:
            q0 += v
    for a, b, v1, v2 in c.get('lin', []):
        if a <= x0 and b >= x1:
            s = (v2 - v1) / (b - a)
            q0 += v1 + s * (x0 - a)
            qs += s
    return q0, qs


def section_EI(c, x0):
    EI = c['E'] * c['I']
    for x, v in sorted(c.get('ei', [])):
        if x <= x0:
            EI = v
    return EI


def candidates(c, m, x0, x1):
    """Interior extremum candidates of the product per quantity: [(t, x, side, value)]."""
    h = x1 - x0
    w0, th0, M0, V0 = m.state(x0, 'R')
    q0, qs = loads_on_piece(c, x0, x1)
    EI = section_EI(c, x0)
    polys = {0: [th0, M0 * h / EI, V0 * h * h / (2 * EI), q0 * h ** 3 / (6 * EI), qs * h ** 4 / (24 * EI)],
             2: [V0, q0 * h, qs * h * h / 2], 3: [q0, qs * h]}
    out = {}
    for q, co in polys.items():
        lst = []
        for t in in_unit_interval(*co):
            if 0 < t < 1:
                x = x0 + t * h
                side = 'R' if x == x0 else 'L' if x == x1 else 'R'
                v = m.state(x, side)[q]
                lst.append((t, x, side, v))
        out[q] = lst
    return out


class Rng:
    """Global extremum selection of BeamExtrema (relative tie 64 eps, smallest x, L before R)."""
    def __init__(s):
        s.mn = s.mx = None

    @staticmethod
    def precedes(a, b):
        return a[1] < b[1] or (a[1] == b[1] and (a[2] or '') < (b[2] or ''))

    def better(s, cand, cur, minimize):
        scale = max(abs(cand[0]), abs(cur[0]))
        tied = scale == 0 or abs(cand[0] / scale - cur[0] / scale) <= 64 * MP
        return s.precedes(cand, cur) if tied else (cand[0] < cur[0] if minimize else cand[0] > cur[0])

    def add(s, cand):
        if s.mn is None or s.better(cand, s.mn, True):
            s.mn = cand
        if s.mx is None or s.better(cand, s.mx, False):
            s.mx = cand


def study(c):
    m = StateSolver(c, adj_refine='beta')
    m.indicator()
    ex = T.Transfer(c)
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    scc, mixc = scales(c, [g[0] for g in got])
    exact_pts = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
    sc, mix = scales(c, [[float(v) for v in r] for r in exact_pts])
    station = max(e[i] / denom(i, v[i], scc, mixc) for v, e in got for i in range(4))
    rt, rr = m.reactions(True)
    react = max([0.] + [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr])
    pg, _ = piece_gates(c, m, scc, mixc)
    gate_C = max(station, react, pg['C'][0])
    o = dict(id=c['id'], gate=gate_C, delivered=gate_C <= TAU)
    p3 = dict(pieces=0, viol_hat=0, undecided_hat=0, viol_exact=0, min_margin=math.inf, min_ratio=math.inf,
              gate_B=max(station, react), notes=[])
    p4 = dict(roots_hat=0, roots_exact=0, multiple_hat=0, multiple_exact=0, sympy_mismatch=0, missed=0,
              missed_effect=0., spurious=0, max_pos_effect=0., pos_over_beta=0, pos_over_2nd=0., reproducible=True,
              cand=0, notes=[])
    prng = {q: Rng() for q in (0, 2, 3)}
    exext = {q: [None, None] for q in (0, 2, 3)}
    ev = events(c)
    for x0, x1 in zip(ev, ev[1:]):
        h = x1 - x0
        polys = piece_polys(m, ex, x0, x1)
        v0 = m.state(x0, 'R')
        v1, b1 = m.state(x1, 'L', True)
        qs_ = qsum(c, x0, x1)
        # ---------------- phase 3
        for i in (2, 3):
            if i == 3:
                lb = max(abs(v0[3]), abs(v1[3])) - h * qs_
            else:
                lb = max(abs(v0[2]) - abs(v0[3]) * h - qs_ * h * h / 2, abs(v1[2]) - abs(v1[3]) * h - qs_ * h * h / 2)
            lb = (lb - b1[i]) * (1 - gamma(8))
            phat, pex = polys[i]
            lo_h, hi_h = min_abs(phat)
            lo_e, hi_e = min_abs(pex)
            DB = max(mixc[i], float(lo_h) - b1[i])
            GB = b1[i] / DB if DB else math.inf
            p3['gate_B'] = max(p3['gate_B'], GB)
            if lb <= 0:
                continue
            p3['pieces'] += 1
            L = Fr(lb)
            if L > hi_h:
                p3['viol_hat'] += 1
                p3['notes'].append('LB > min|p_hat| at %s piece %.17g q %s' % (c['id'], x0, Q[i]))
            elif L > lo_h:
                p3['undecided_hat'] += 1
            if L > hi_e:
                p3['viol_exact'] += 1
                p3['notes'].append('LB > min|p| (exact) at %s piece %.17g q %s, gate %.1e' % (c['id'], x0, Q[i], gate_C))
            if b1[i] > 0:
                p3['min_margin'] = min(p3['min_margin'], float((lo_h - L) / Fr(b1[i])))
            p3['min_ratio'] = min(p3['min_ratio'], float(lo_h / L))
        # ---------------- phase 4
        cands = candidates(c, m, x0, x1)
        for q in (0, 2, 3):
            phat, pex = polys[q]
            dh, de = deriv(phat), deriv(pex)
            rh, re_ = roots01(dh), roots01(de)
            # endpoints (both sides) as candidates of the product and of the exact extrema
            for x, sd in ((x0, 'R'), (x1, 'L')):
                prng[q].add((m.state(x, sd)[q], x, sd))
                ve = ex.state(Fr(x), sd)[Q[q]]
                lo, hi = exext[q]
                exext[q] = [ve if lo is None else min(lo, ve), ve if hi is None else max(hi, ve)]
            if re_:
                for a, b, mult, exact in re_:
                    t = (a + b) / 2
                    if 0 < t < 1:
                        ve = peval(pex, t)
                        lo, hi = exext[q]
                        exext[q] = [min(lo, ve), max(hi, ve)]
            for t, x, sd, v in cands[q]:
                p4['cand'] += 1
                prng[q].add((v, x, sd))
                if m.state(x, sd)[q] != v:
                    p4['reproducible'] = False
            if rh is None or re_ is None:
                continue
            p4['roots_hat'] += len(rh)
            p4['roots_exact'] += len(re_)
            p4['multiple_hat'] += sum(r[2] > 1 for r in rh)
            p4['multiple_exact'] += sum(r[2] > 1 for r in re_)
            for poly, mine in ((dh, rh), (de, re_)):
                sy = sympy_count(poly)
                if sy is not None and (len(sy) != len(mine) or sorted(r[2] for r in sy) != sorted(r[2] for r in mine)):
                    p4['sympy_mismatch'] += 1
                    p4['notes'].append('sympy mismatch %s piece %.17g q %s: mine %s sympy %s' % (
                        c['id'], x0, Q[q], [(float(a), mm) for a, _, mm, _ in mine], [(float(a), mm) for a, _, mm in sy]))
            ts = [t for t, *_ in cands[q]]
            ts_end = ts + [0., 1.]                     # piece ends are always candidates (both sides)
            # completeness: local extrema of p_hat (odd multiplicity, interior)
            d2 = deriv(dh)
            for a, b, mult, exact in rh:
                tstar = (a + b) / 2
                if not (0 < tstar < 1) or mult % 2 == 0:
                    continue
                near = [t for t in ts_end if abs(t - float(tstar)) <= DT]
                if not near:
                    p4['missed'] += 1
                    # effect: how far the local extreme value lies beyond every candidate and end of the piece
                    vals = [peval(phat, Fr(t)) for t in ts] + [peval(phat, Fr(0)), peval(phat, Fr(1))]
                    vs = peval(phat, tstar)
                    eff = min(abs(vs - v) for v in vals)
                    effn = float(eff) / denom(q, float(vs), scc, mixc)
                    p4['missed_effect'] = max(p4['missed_effect'], effn)
                    p4['notes'].append('missed extremum %s piece %.17g q %s t* %.6g mult %d effect %.2e' % (
                        c['id'], x0, Q[q], float(tstar), mult, effn))
                    continue
                tc = min(near, key=lambda t: abs(t - float(tstar)))
                eff = abs(peval(phat, Fr(tc)) - peval(phat, tstar))
                effn = float(eff) / denom(q, float(peval(phat, tstar)), scc, mixc)
                p4['max_pos_effect'] = max(p4['max_pos_effect'], effn)
                xc = x0 + tc * h
                _, bc = m.state(xc, 'R', True)
                if eff > Fr(bc[q]):
                    p4['pos_over_beta'] += 1
                    p4['notes'].append('position effect > beta %s piece %.17g q %s mult %d effect %.2e beta %.2e' % (
                        c['id'], x0, Q[q], mult, float(eff), bc[q]))
                curv = abs(peval(d2, tstar))
                if mult == 1 and curv > 0:
                    pred = curv * (Fr(tc) - tstar) ** 2 / 2
                    if pred > 0:
                        p4['pos_over_2nd'] = max(p4['pos_over_2nd'], float(eff / pred))
            for t in ts:
                if not any(abs(t - float((a + b) / 2)) <= DT for a, b, *_ in rh):
                    p4['spurious'] += 1
    # global extrema
    errs = {}
    for q in (0, 2, 3):
        lo, hi = exext[q]
        for name, cand, exv in (('min', prng[q].mn, lo), ('max', prng[q].mx, hi)):
            e = float(abs(Fr(cand[0]) - exv)) / denom(q, float(exv), sc, mix)
            errs['%s_%s' % (Q[q], name)] = e
    o['ext_err'] = max(errs.values())
    o['ext_err_detail'] = errs
    o['ext_wrong'] = o['delivered'] and o['ext_err'] > TAU
    p3['notes'] = p3['notes'][:10]
    p4['notes'] = p4['notes'][:10]
    o['p3'], o['p4'] = p3, p4
    o['decision_B_differs'] = (max(station, react, p3['gate_B']) <= TAU) != o['delivered']
    return o


# ---------------------------------------------------------------------------------------------
def constructed():
    """Targeted cases for phases 3 and 4 (exact data are the doubles below)."""
    L, E, I = 4., 210e9, 8e-6
    out = []
    # simple inner root (M extremum), symmetric and unsymmetric
    out.append(case('K1|udl', L, [(0., 'Pinned'), (L, 'Roller')], q=[(0., L, -2e3)], E=E, I=I))
    out.append(case('K1|udl+P', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(1.1, -3e3)], q=[(0., L, -2e3)], E=E, I=I))
    # root exactly at a piece boundary: symmetric load with an event station at L/2
    out.append(case('K2|rand', L, [(0., 'Pinned'), (L, 'Roller')], q=[(0., L / 2, -2e3), (L / 2, L, -2e3)], E=E, I=I))
    # double root of V (flat M extremum): antisymmetric linear load and an end couple with V(L/2) = q(L/2) = 0
    for d, nm in ((0, 'exakt'), (1, '1ulp'), (1e-9, '1e-9'), (-1e-9, '-1e-9')):
        C = 4e3
        C = C + (math.ulp(C) if d == 1 else C * d)
        out.append(case(f'K3|doppelt|{nm}', L, [(0., 'Pinned'), (L, 'Roller')], pm=[(0., C)], lin=[(0., L, 3e3, -3e3)], E=E, I=I))
        out.append(case(f'K3|doppelt-|{nm}', L, [(0., 'Pinned'), (L, 'Roller')], pm=[(0., -C)], lin=[(0., L, 3e3, -3e3)], E=E, I=I))
    # extremum immediately next to an event station
    for r in (1e-6, 1e-9, 1e-12):
        out.append(case(f'K4|neben|{r:g}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(L / 2 + r * L, -1.)], q=[(0., L, -2e3)], E=E, I=I))
        out.append(case(f'K4|nebenM|{r:g}', L, [(0., 'Pinned'), (L, 'Roller')], pm=[(L / 2 + r * L, 1e-3)], q=[(0., L, -2e3)], E=E, I=I))
    # very short piece around the root
    for r in (1e-7, 1e-11):
        out.append(case(f'K5|kurz|{r:g}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(L / 2 - r * L, -5e2), (L / 2 + r * L, -5e2)],
                        q=[(0., L, -2e3)], E=E, I=I))
    # strong coefficient scales
    for Ls, EIs in ((1e-3, 1e-3), (1e3, 1e12), (4., 1e-6), (4., 1e14)):
        out.append(case(f'K6|skala|{Ls:g}|{EIs:g}', Ls, [(0., 'Fixed'), (Ls, 'Roller')], pf=[(0.3 * Ls, -1e4)],
                        lin=[(0., Ls, 1e3, -2e3)], E=EIs, I=1.))
    # small local field next to large neighbours (pair of supports, extremum between them)
    for r in (1e-5, 1e-9, 1e-11):
        g = r * L
        out.append(case(f'K7|klein|{r:g}', L, [(0., 'Pinned'), (2., 'Roller'), (2. + g, 'Roller'), (L, 'Roller')],
                        pf=[(1.3, -1e4), (2. + g / 2, -1e-3)], q=[(0., L, -2e3)], E=E, I=I))
    # w extremum: cantilever and propped beam with linear load (theta quartic) near a double root of theta
    for k, C in enumerate((0., 1e2, 1e3, 3.3e3, 6.7e3)):
        out.append(case(f'K8|w|{k}', L, [(0., 'Fixed'), (L, 'Roller')], pm=[(L, C)], lin=[(0., L, 2e3, -4e3)], E=E, I=I))
    # near-double root of theta by construction (Wronskian of the parameter family, see near_double())
    out += near_double()
    # sign changes of q inside a piece (V extremum) and V root at a load end
    out.append(case('K9|vz', L, [(0., 'Pinned'), (L, 'Roller')], lin=[(0.5, 3.5, 2e3, -2e3)], E=E, I=I))
    return [c for c in out if not is_mechanism(c)]


def near_double():
    """Couple C at x = L on a propped beam with a linear load: theta(t; C) = A(t) + C B(t) on the
    piece. A double root needs A + C B = 0 and A' + C B' = 0, i.e. a root of W = A B' - A' B; C is
    rounded to the nearest double, which leaves two roots at a distance of about sqrt(ulp)."""
    L, E, I = 4., 210e9, 8e-6
    out = []
    from piece_oracle import NODES, interpolate

    def poly_theta(C):
        c = case('tmp', L, [(0., 'Fixed'), (L, 'Roller')], pm=[(L, C)], lin=[(0., L, 2e3, -4e3)], E=E, I=I)
        ex = T.Transfer(c)
        return interpolate([ex.state(Fr(L) * t, 'R')['t'] for t in NODES])

    def mul(a, b):
        r = [Fr(0)] * (len(a) + len(b) - 1)
        for i, x in enumerate(a):
            for j, y in enumerate(b):
                r[i + j] += x * y
        return r

    A = poly_theta(0.)
    A1 = poly_theta(1.)
    n = max(len(A), len(A1))
    A, A1 = A + [Fr(0)] * (n - len(A)), A1 + [Fr(0)] * (n - len(A1))
    B = [u - v for u, v in zip(A1, A)]
    p1, p2 = mul(A, deriv(B)), mul(deriv(A), B)
    k = max(len(p1), len(p2))
    W = [u - v for u, v in zip(p1 + [Fr(0)] * (k - len(p1)), p2 + [Fr(0)] * (k - len(p2)))]
    for lo, hi, mult, exact in roots01(W) or []:
        r = (lo + hi) / 2
        if not 0 < r < 1 or peval(B, r) == 0:
            continue
        Cv = float(-peval(A, r) / peval(B, r))
        for k, d in enumerate((0, 1, -1, 1e-6, -1e-6)):
            C = Cv + (d * math.ulp(Cv) if abs(d) == 1 else Cv * d)
            out.append(case(f'K10|wdoppelt|{float(r):.4f}|{k}', L, [(0., 'Fixed'), (L, 'Roller')], pm=[(L, C)],
                            lin=[(0., L, 2e3, -4e3)], E=E, I=I))
    return out


if __name__ == '__main__':
    t0 = time.time()
    arg = sys.argv[1:]
    if arg[0] == 'targeted':
        cases, name = [c for _, c in all_cases()], 'targeted'
    elif arg[0] == 'random':
        K = int(arg[1])
        cases = []
        for kind, N in (('s', 1500), ('f', 1000)):
            for r0 in (1e-2, 1e-4, 1e-6, 1e-9, 1e-11):
                seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
                rng = random.Random(seed)
                for k in range(N):
                    c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
                    if k % K == 0:
                        cases.append(c)
        name = f'random 1/{K}'
    else:
        cases, name = constructed(), 'constructed'
    n = 0
    for c in cases:
        if is_mechanism(c):
            continue
        try:
            o = study(c)
        except ZeroDivisionError:
            continue
        o['cls'] = name
        n += 1
        print('DATA ' + json.dumps(o), flush=True)
    print('%s: models %d | %.0f s' % (name, n, time.time() - t0))
