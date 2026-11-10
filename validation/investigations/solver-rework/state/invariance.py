"""Scaling invariance. (1) Exact powers of two: lengths x2^5, EI x2^-7, forces x2^3 must give
bit-identical scaled results. (2) General unit changes (m -> mm, N -> kN): each run stays within the
numerical contract against the exact solution of its own input doubles.
Usage: python3 state/invariance.py"""
import os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from gen import gen_f, pts
from kinematics import is_mechanism
from metrics import compare
from state import StateSolver


def scaled(c, a, b, f):
    """Lengths x a, EI x b (through E), forces x f."""
    d = dict(c)
    d['L'] = c['L'] * a; d['E'] = c['E'] * b
    d['sup'] = [[x * a, t] for x, t in c['sup']]
    d['springs'] = [[x * a, k if k == 'inf' else k * b / a ** 3, kt if kt in ('inf', 0) else kt * b / a, ax] for x, k, kt, ax in c['springs']]
    d['hinges'] = [h * a for h in c['hinges']]
    d['ei'] = [[x * a, v * b] for x, v in c['ei']]
    d['pf'] = [[x * a, v * f] for x, v in c['pf']]
    d['pm'] = [[x * a, v * f * a] for x, v in c['pm']]
    d['q'] = [[x * a, y * a, v * f / a] for x, y, v in c['q']]
    d['lin'] = [[x * a, y * a, v1 * f / a, v2 * f / a] for x, y, v1, v2 in c['lin']]
    d['pts'] = [[x * a, s] for x, s in c['pts']]
    return d


if __name__ == '__main__':
    rng = random.Random(77)
    a, b, f = 2.0 ** 5, 2.0 ** -7, 2.0 ** 3
    fac = [f * a ** 3 / b, f * a ** 2 / b, f * a, f]
    n = same = 0
    worst = {}
    for k in range(400):
        c = gen_f(rng, [1e-2, 1e-6, 1e-11][k % 3], f'inv|{k}')
        if is_mechanism(c):
            continue
        m0 = StateSolver(c)
        m1 = StateSolver(scaled(c, a, b, f))
        n += 1
        ok = all(v1 == v0 * fq for (x, sd) in c['pts'] for v0, v1, fq in
                 zip(m0.state(float(x), sd), m1.state(float(x) * a, sd), fac))
        same += ok
        if k < 150:
            for name, (ua, ub, uf) in (('m, N', (1, 1, 1)), ('mm, N', (1e3, 1e12, 1)), ('m, kN', (1, 1e-3, 1e-3)), ('mm, kN', (1e3, 1e9, 1e-3))):
                try:
                    o, _ = compare(scaled(c, ua, ub, uf))
                except ZeroDivisionError:
                    worst.setdefault(name, [0., 0, 0])[2] += 1
                    continue
                w = worst.setdefault(name, [0., 0, 0])
                if o['ind'] <= 1e-8:
                    w[0] = max(w[0], o['mixed']); w[1] += 1
                else:
                    w[2] += 1
    print('power-of-two scaling: %d of %d models bit-identical after exact rescaling' % (same, n))
    # (3) invariant 1: an extra load (new event station, no mechanical effect) leaves matrix,
    # scaling and unknowns bit-identical
    rng = random.Random(78)
    same_m = n_m = 0
    for k in range(300):
        c = gen_f(rng, [1e-2, 1e-6, 1e-11][k % 3], f'inv1|{k}')
        if is_mechanism(c):
            continue
        d = dict(c)
        x = rng.uniform(0, c['L'])
        d['pf'] = c['pf'] + [[x, -123.0], [c['pf'][0][0], 77.0]]
        d['q'] = c['q'] + [[x / 2, x, 55.0]]
        d['pts'] = pts(d)
        try:
            m0, m1 = StateSolver(c), StateSolver(d)
        except ZeroDivisionError:
            continue
        n_m += 1
        same_m += (m0.A == m1.A and m0.S == m1.S and m0.free == m1.free and m0.K == m1.K and m0.rs == m1.rs and m0.cs == m1.cs)
    print('invariant 1: extra point loads and a line load leave matrix, rounding counts, scaling and unknowns bit-identical: %d of %d' % (same_m, n_m))
    print('unit changes (first 150 models): largest mixed error of delivered results against the exact solution of the own input')
    for name, (e, n_ok, n_rej) in worst.items():
        print('  %-7s delivered %3d  rejected %2d  max error %.1e' % (name, n_ok, n_rej, e))
