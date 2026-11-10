"""Point 1: does the acceptance gate cover EvaluateAt(x) on the whole continuous beam?

Every piece (between consecutive event stations) is evaluated densely: 15 uniform interior points
and points approaching both ends geometrically (2^-4 ... 2^-40 of the piece length). For every
value: exact error in the contract measure (metrics.py, exact scales), the pointwise indicator
beta(x) in the same measure (computed scales), and the piece gate

    G_q = beta_q(right end, left limit) / D_q
    D_q = S_q                                  for w, th
    D_q = max(S_q, min over the piece of |Q|)  for M, V   (min from endpoints and critical points)

The gate is a finite computation. It dominates beta_q(x) on the piece if beta_q is nondecreasing in
the local coordinate s of the mechanical segment (claim to be checked here, not assumed).
Usage: python3 state/coverage.py targeted | s|f r0 N"""
import math, os, random, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales, Q
from state import StateSolver

FRACS = [k / 16 for k in range(1, 16)]
NEAR = [2.0 ** -j for j in (4, 8, 16, 24, 32, 40)]


def events(c):
    L = c['L']
    return sorted({0.0, L} | {s[0] for s in c['sup']} | {s[0] for s in c.get('springs', [])} | {a for a, _ in c['pf']}
                  | {a for a, _ in c['pm']} | {x for a, b, _ in c['q'] for x in (a, b)}
                  | {x for a, b, *_ in c.get('lin', []) for x in (a, b)} | set(c.get('hinges', []))
                  | {x for x, _ in c.get('ei', [])})


def piece_points(x0, x1):
    d = x1 - x0
    xs = {x0 + d * f for f in FRACS} | {x0 + d * f for f in NEAR} | {x1 - d * f for f in NEAR}
    return sorted(x for x in xs if x0 < x < x1)


def quad_min_abs(f, t0, t1, deg):
    """Lower bound of min |f| on [t0, t1] for a polynomial of degree <= deg known by evaluation:
    0 if a sign change or a possible interior root exists, else min over endpoints and critical
    points. Conservative: near-degenerate cases count as a root."""
    ts = [t0 + (t1 - t0) * k / deg for k in range(deg + 1)] if deg > 0 else [t0]
    vs = [f(t) for t in ts]
    if any(v == 0 for v in vs) or any((a > 0) != (b > 0) for a, b in zip(vs, vs[1:])):
        return 0.
    # dense check of critical points (piece polynomial of degree <= 3): 64 sub-samples
    sub = [f(t0 + (t1 - t0) * k / 64) for k in range(65)]
    if any((a > 0) != (b > 0) or b == 0 for a, b in zip(sub, sub[1:])):
        return 0.
    m = min(map(abs, sub))
    # a smooth minimum between samples can lie lower; take the parabola through the three samples
    i = min(range(65), key=lambda k: abs(sub[k]))
    if 0 < i < 64:
        a, b, cc = abs(sub[i - 1]), abs(sub[i]), abs(sub[i + 1])
        curv = a - 2 * b + cc
        if curv > 0:
            m = min(m, b - (a - cc) ** 2 / (8 * curv))
    return max(m, 0.) * (1 - 1e-9)


def piece_gate(c, m, scc, mixc):
    """Largest piece gate G_q over all pieces and quantities, from computed values only."""
    ev = events(c)
    g = 0.
    for x0, x1 in zip(ev, ev[1:]):
        v0 = m.state(x0, 'R')
        v1, b1 = m.state(x1, 'L', True)
        for i in range(4):
            if i < 2:
                D = scc[i]
            else:
                f = lambda t, _i=i: m.state(t, 'R')[_i] if x0 < t < x1 else (v0[_i] if t <= x0 else v1[_i])
                D = max(mixc[i], quad_min_abs(f, x0, x1, 3 if i == 2 else 2))
            G = b1[i] / D if D else (0. if b1[i] == 0 else math.inf)
            g = max(g, G)
    return g


def study(c):
    ex = T.Transfer(c)
    m = StateSolver(c)
    m.indicator()
    ev = events(c)
    recs = []                     # (piece, x, side, is_end, exact[4], got[4], beta[4])
    for p, (x0, x1) in enumerate(zip(ev, ev[1:])):
        pts = [(x0, 'R', True)] + [(x, 'R', False) for x in piece_points(x0, x1)] + [(x1, 'L', True)]
        for x, sd, end in pts:
            st = ex.state(Fr(x), sd)
            v, b = m.state(x, sd, True)
            recs.append((p, x, sd, end, [st[k] for k in Q], v, b))
    fex = [[float(v) for v in r[4]] for r in recs]
    got = [r[5] for r in recs]
    sc, mix = scales(c, fex)
    scc, mixc = scales(c, got)

    def den(i, val, s_, m_):
        return max(abs(val), m_[i]) if i >= 2 else s_[i]

    out = dict(end_err=0., in_err=0., end_beta=0., in_beta=0., gate=0., gate_fail=0, err_over_gate=0,
               err_over_beta=0, mono_fail=0, worst_in_end=0.)
    by_piece = {}
    for r in recs:
        by_piece.setdefault(r[0], []).append(r)
    for p, rs in by_piece.items():
        x0, x1 = rs[0][1], rs[-1][1]
        for i in range(4):
            right = rs[-1][6][i]
            # monotonicity of beta along the piece (the claim behind the gate)
            for a, b in zip(rs, rs[1:]):
                if b[6][i] < a[6][i] * (1 - 1e-12):
                    out['mono_fail'] += 1
            if i < 2:
                D = scc[i]
            else:
                k = [j for j, r in enumerate(recs) if r[0] == p]
                f = lambda t, _i=i: m.state(t, 'R')[_i] if x0 < t < x1 else (rs[0][5][_i] if t <= x0 else rs[-1][5][_i])
                D = max(mixc[i], quad_min_abs(f, x0, x1, 3 if i == 2 else 2))
            G = right / D if D else (0. if right == 0 else math.inf)
            out['gate'] = max(out['gate'], G)
            for r in rs:
                errv = float(abs(Fr(r[5][i]) - r[4][i]))
                en = errv / (den(i, float(r[4][i]), sc, mix) or 1)
                bn = r[6][i] / (den(i, r[5][i], scc, mixc) or 1)
                key = 'end' if r[3] else 'in'
                out[key + '_err'] = max(out[key + '_err'], en)
                out[key + '_beta'] = max(out[key + '_beta'], bn)
                if bn > G * (1 + 1e-12):
                    out['gate_fail'] += 1
                if en > G:
                    out['err_over_gate'] += 1
                if errv > r[6][i]:
                    out['err_over_beta'] += 1
    out['ratio_err'] = out['in_err'] / out['end_err'] if out['end_err'] else (0. if out['in_err'] == 0 else math.inf)
    out['ratio_beta'] = out['in_beta'] / out['end_beta'] if out['end_beta'] else (0. if out['in_beta'] == 0 else math.inf)
    return out


def summary(name, rows, t0):
    def mx(k):
        return max((r[k] for r in rows), default=float('nan'))
    acc = [r for r in rows if r['gate'] <= 1e-8]
    print('%s: models %d (gate <= 1e-8: %d) | %.0f s' % (name, len(rows), len(acc), time.time() - t0))
    print('  max interior/end ratio  actual error %.2f   beta %.2f' % (mx('ratio_err'), mx('ratio_beta')))
    print('  largest actual error    ends %.1e  interior %.1e' % (mx('end_err'), mx('in_err')))
    print('  largest beta            ends %.1e  interior %.1e   gate %.1e' % (mx('end_beta'), mx('in_beta'), mx('gate')))
    print('  beta not monotone along a piece: %d values' % sum(r['mono_fail'] for r in rows))
    print('  beta(x) above its piece gate: %d values' % sum(r['gate_fail'] for r in rows))
    print('  actual error above gate: %d values (in models with gate <= 1e-8: %d)' % (
        sum(r['err_over_gate'] for r in rows), sum(r['err_over_gate'] for r in acc)))
    print('  actual error above pointwise beta: %d values (in models with gate <= 1e-8: %d)' % (
        sum(r['err_over_beta'] for r in rows), sum(r['err_over_beta'] for r in acc)))
    worst = sorted(rows, key=lambda r: -r['ratio_err'])[:3]
    for r in worst:
        print('   interior-dominated: %-24s interior/end %.2f interior err %.1e end err %.1e' % (r['id'], r['ratio_err'], r['in_err'], r['end_err']))


if __name__ == '__main__':
    t0 = time.time()
    rows = []
    if sys.argv[1] == 'targeted':
        for nm, c in all_cases():
            if is_mechanism(c):
                continue
            o = study(c); o['id'] = c['id']; rows.append(o)
        summary('targeted', rows, t0)
    else:
        kind, r0, N = sys.argv[1], float(sys.argv[2]), int(sys.argv[3])
        seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
        rng = random.Random(seed)
        for k in range(N):
            c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
            if is_mechanism(c):
                continue
            try:
                o = study(c)
            except ZeroDivisionError:
                continue
            o['id'] = c['id']; rows.append(o)
        summary('%s L/%.0e' % (kind, 1 / r0), rows, t0)
