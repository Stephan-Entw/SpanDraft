"""Round 3, points 2-4: piece-gate variants, backward error omega, adjoint refinement rules.

Per model and adjoint refinement rule ('none', 'one', 'adaptive' = rule of the primary solution,
'beta' = at least one step, continue while |z|^T d changes by more than 1/16; at most 5 each):
- station gate (normalised beta at the evaluation points, mixed measure) and reaction gate;
- piece gates for M and V (w and th always use D = S):
    A: D = S_q                                   (no minimum of |Q|, no root finding)
    B: D = max(S_q, sampled min |Q|)             (coverage.quad_min_abs; 'one' only)
    C: D = max(S_q, LB_q) with the closed-form lower bound from the piece-end values,
       LB_V = max_e |V_e| - h*qsum,  LB_M = max_e (|M_e| - |V_e| h - qsum h^2/2),
       qsum = sum over distributed loads of max(|q(x0)|, |q(x1)|), minus beta_q(x1-) as margin
       and times (1 - gamma(8)) for its own rounding. Rigorous for the piece polynomial.
- the actual largest normalised error at the evaluation points and reactions (exact reference);
- omega (componentwise backward error after refinement); adjoint solves and refinement steps;
- for 'one': strict monotonicity of beta along every piece (no tolerance, 27 points per piece).
Every 5th model additionally: the LU factors are perturbed by a relative random amount eps
(eps = 1e-8, 1e-4, 1e-1) and the primary solution (refinement against the true system) and
the adjoints (one step against the true system) are recomputed; recorded: actual error, gate,
omega, delivered. A delivered result with error > tau there would be a state that beta does not
catch.
Usage: python3 state/gate_study.py targeted | s|f r0 N     (DATA lines for gate_summary.py)"""
import json, math, os, random, sys, time, zlib
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from coverage import events, piece_points, quad_min_abs
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales, Q, denom
from state import StateSolver, gamma, U

TAU = 1e-8
RULES = ('none', 'one', 'adaptive', 'beta')


def qsum(c, x0, x1):
    tot = 0.
    for a, b, v in c['q']:
        if a <= x0 and b >= x1:
            tot += abs(v)
    for a, b, v1, v2 in c.get('lin', []):
        if a <= x0 and b >= x1:
            f = lambda x: v1 + (v2 - v1) * ((x - a) / (b - a))
            tot += max(abs(f(x0)), abs(f(x1)))
    return tot * (1 + gamma(6))


def piece_gates(c, m, scc, mixc, with_b=False, mono=False):
    """Largest piece gate per variant, with the quantity and the piece of the maximum."""
    ev = events(c)
    out = {k: (0., None) for k in ('A', 'B', 'C')}
    nmono = 0
    for x0, x1 in zip(ev, ev[1:]):
        v0 = m.state(x0, 'R')
        v1, b1 = m.state(x1, 'L', True)
        h = x1 - x0
        qs = qsum(c, x0, x1)
        for i in range(4):
            if i < 2:
                D = {'A': scc[i], 'B': scc[i], 'C': scc[i]}
            else:
                if i == 3:
                    lb = max(abs(v0[3]), abs(v1[3])) - h * qs
                else:
                    lb = max(abs(v0[2]) - abs(v0[3]) * h - qs * h * h / 2, abs(v1[2]) - abs(v1[3]) * h - qs * h * h / 2)
                lb = (lb - b1[i]) * (1 - gamma(8))
                D = {'A': mixc[i], 'C': max(mixc[i], lb)}
                if with_b:
                    f = lambda t, _i=i: m.state(t, 'R')[_i] if x0 < t < x1 else (v0[_i] if t <= x0 else v1[_i])
                    D['B'] = max(mixc[i], quad_min_abs(f, x0, x1, 3 if i == 2 else 2))
            for k, dk in D.items():
                G = b1[i] / dk if dk else (0. if b1[i] == 0 else math.inf)
                if G > out[k][0]:
                    out[k] = (G, dict(q=Q[i], x0=x0, h=h / c['L'], ratio=max(abs(v0[i]), abs(v1[i])) / mixc[i] if i >= 2 and mixc[i] else None))
        if mono:
            xs = [(x0, 'R')] + [(x, 'R') for x in piece_points(x0, x1)] + [(x1, 'L')]
            prev = None
            for x, sd in xs:
                _, b = m.state(x, sd, True)
                if prev is not None and any(b[i] < prev[i] for i in range(4)):
                    nmono += 1
                prev = b
    return out, nmono


def evaluate(c, m, exact, fexact, with_b=False, mono=False):
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    vals = [g[0] for g in got]
    scc, mixc = scales(c, vals)
    sc, mix = scales(c, fexact)
    station = max(e[i] / denom(i, v[i], scc, mixc) for v, e in got for i in range(4))
    err = max(float(abs(Fr(v[i]) - r[i])) / denom(i, fe[i], sc, mix) for (v, _), r, fe in zip(got, exact, fexact) for i in range(4))
    rt, rr = m.reactions(True)
    react = max([0.] + [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr])
    pg, nmono = piece_gates(c, m, scc, mixc, with_b, mono)
    return dict(station=station, react=react, A=pg['A'][0], B=pg['B'][0] if with_b else None, C=pg['C'][0],
                whereA=pg['A'][1], whereC=pg['C'][1], err=err, omega=m.omega, mono=nmono)


def reaction_error(c, m, ex, mix):
    rt, rr = ex.reactions()
    mt, mr = m.reactions(True)
    e = 0.
    for exr, comp, i in ((rt, mt, 3), (rr, mr, 2)):
        for (a, R), (_, Rm, _) in zip(exr, comp):
            e = max(e, float(abs(Fr(Rm) - R)) / max(abs(float(R)), mix[i]))
    return e


def resolve(m, LUp):
    """Primary solution and refinement with perturbed factors LUp against the true scaled system."""
    m.LU = LUp
    y = m.lusolve(m.sb)
    prev = math.inf
    for _ in range(m.max_iter):
        r, _ = m.residual(y)
        dy = m.lusolve(r)
        y = [a + b for a, b in zip(y, dy)]
        nd, ny = max(map(abs, dy)), max(map(abs, y))
        if not (nd == nd) or nd <= 2 * U * ny or nd > 0.5 * prev:
            break
        prev = nd
    m.y = y
    m.res, m.reserr = m.residual(y)
    x = [m.cs[i] * y[i] for i in range(m.n)]
    m.x = [0.] * m.nfull
    for i, k in enumerate(m.free):
        m.x[k] = x[i]
    den = [sum(abs(m.S[i][j] * y[j]) for j in m.nz[i]) + abs(m.sb[i]) for i in range(m.n)]
    m.omega = max([abs(m.res[i]) / den[i] for i in range(m.n) if den[i] > 0] + [0.])


def study(c, ex, perturb):
    exact = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
    fexact = [[float(v) for v in r] for r in exact]
    sc, mix = scales(c, fexact)
    out = dict(id=c['id'])
    for rule in RULES:
        m = StateSolver(c, adj_refine=rule)
        n = m.n
        t = time.process_time()
        m.indicator()
        o = evaluate(c, m, exact, fexact, with_b=(rule == 'one'), mono=(rule == 'one'))
        o['t_ind'] = time.process_time() - t
        o['err'] = max(o['err'], reaction_error(c, m, ex, mix))
        o['adj_solves'] = getattr(m, 'adj_solves', 0)
        o['adj_steps'] = getattr(m, 'adj_steps', 0)
        o['prim_steps'] = m.iterations
        out[rule] = o
    out['n'] = n
    if perturb:
        out['perturb'] = {}
        rng = random.Random(zlib.crc32(c["id"].encode()))
        for eps in (1e-8, 1e-4, 1e-1):
            m = StateSolver(c, adj_refine='one')
            LUp = [[v * (1 + eps * rng.uniform(-1, 1)) for v in row] for row in m.LU]
            try:
                resolve(m, LUp)
                m.indicator()
                o = evaluate(c, m, exact, fexact)
                o['err'] = max(o['err'], reaction_error(c, m, ex, mix))
                gate = max(o['station'], o['react'], o['C'])
                fin = all(math.isfinite(v) for v in m.x) and math.isfinite(gate)
                out['perturb'][str(eps)] = dict(err=o['err'], gate=gate, omega=m.omega, finite=fin)
            except (ZeroDivisionError, OverflowError, ValueError):
                out['perturb'][str(eps)] = dict(err=None, gate=None, omega=None, finite=False)
    return out


def models(arg):
    if arg[0] == 'targeted':
        return [c for _, c in all_cases()], 'targeted'
    kind, r0, N = arg[0], float(arg[1]), int(arg[2])
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    return [(gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}') for k in range(N)], '%s L/%.0e' % (kind, 1 / r0)


if __name__ == '__main__':
    t0 = time.time()
    cases, name = models(sys.argv[1:])
    n = sing = 0
    for k, c in enumerate(cases):
        if is_mechanism(c):
            continue
        ex = T.Transfer(c)
        try:
            o = study(c, ex, perturb=(k % 5 == 0))
        except ZeroDivisionError:
            sing += 1
            continue
        n += 1
        o['cls'] = name
        print('DATA ' + json.dumps(o))
    print('%s: models %d, numerically singular %d | %.0f s' % (name, n, sing, time.time() - t0))
