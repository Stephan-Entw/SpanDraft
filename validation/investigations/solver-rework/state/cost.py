"""Round 3, point 5: cost of the target architecture as a function of model size.

Continuous beams with s spans (supports at the span ends; per span one EI change, every second
span an elastic spring, every third span a hinge, two point loads, a point couple and a
distributed load). Per size:
- n_m mechanical stations, n unknowns, restraints n_r, pieces;
- structure: lower/upper bandwidth of the scaled matrix S and of L and U after LU with partial
  pivoting (row exchanges stay inside the band: fill is bounded by the lower bandwidth);
- counts: adjoint solves and refinement steps of the quality contract (one step each);
- Python wall time per phase (dense research code; only the growth exponent is meaningful);
- an operation model of a banded C# implementation with the measured bandwidths:
    LU      n p (p + q)            (p lower, q upper bandwidth of U after fill)
    solve   2 n (p + q + 1)        per right-hand side
    resid   2 nnz(S)               per right-hand side
    quality (n + n_r) * (2 solves + 1 residual)     (one adjoint refinement step)
  and of the dense variant (LU 2/3 n^3, solve 2 n^2, residual 2 n^2).
A second series puts many point loads into one segment: field evaluation from the segment
start makes every evaluation O(loads in the segment) and the piece gate O(loads^2).
Usage: python3 state/cost.py"""
import math, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from coverage import events
from gate_study import piece_gates
from gen import case
from metrics import scales
from state import StateSolver

E, I = 210e9, 8e-6
EI = E * I


def beam(spans, a=4.):
    L = spans * a
    sup = [(0., 'Pinned')] + [(k * a, 'Roller') for k in range(1, spans + 1)]
    springs, hinges, ei, pf, pm, q = [], [], [], [], [], []
    for k in range(spans):
        x = k * a
        ei.append((x + .5 * a, EI * (1 + .3 * (k % 3))))
        if k % 2 == 1:
            springs.append((x + .35 * a, 1e3 * EI / a ** 3, 0, False))
        if k % 3 == 2:
            hinges.append(x + .8 * a)
        pf += [(x + .2 * a, -1e4), (x + .6 * a, -5e3)]
        pm.append((x + .45 * a, 2e3))
        q.append((x + .1 * a, x + .9 * a, -2e3))
    return case('beam%d' % spans, L, sup, springs=springs, hinges=hinges, pf=pf, pm=pm, q=q, E=E, I=I, ei=ei)


def bandwidths(m):
    """Bandwidths of S, and of the factors as a banded LU (LAPACK gbtrf) stores them: the
    multipliers of step k measured in the row order of step k (later row exchanges are applied
    to the right-hand side, not to stored multipliers), and the upper bandwidth of U after fill."""
    n, S = m.n, m.S
    lo = max([i - j for i in range(n) for j in range(n) if S[i][j] != 0 and i > j] + [0])
    up = max([j - i for i in range(n) for j in range(n) if S[i][j] != 0 and j > i] + [0])
    A = [row[:] for row in S]
    mult, upu = 0, 0
    for k in range(n):
        p = max(range(k, n), key=lambda i: abs(A[i][k]))
        mult = max(mult, p - k)
        A[k], A[p] = A[p], A[k]
        for i in range(k + 1, n):
            if A[i][k] != 0:
                mult = max(mult, i - k)
                f = A[i][k] / A[k][k]
                A[i][k] = 0.
                for j in range(k + 1, n):
                    A[i][j] -= f * A[k][j]
        upu = max([upu] + [j - k for j in range(k + 1, n) if A[k][j] != 0])
    nnz = sum(1 for i in range(n) for j in range(n) if S[i][j] != 0)
    return lo, up, mult, upu, nnz


def timed(f):
    t = time.perf_counter()
    r = f()
    return r, time.perf_counter() - t


def run_size(spans):
    c = beam(spans)
    m = StateSolver(c)                       # then build and solve again, timed separately
    _, t_build = timed(m.build)
    _, t_solve = timed(lambda: m.solve(m.max_iter))
    m.adj_solves = m.adj_steps = 0
    _, t_ind = timed(m.indicator)
    _, t_react = timed(lambda: m.reactions(True))
    got = [m.state(float(x), sd) for x, sd in c['pts']]
    scc, mixc = scales(c, got)
    _, t_gate = timed(lambda: piece_gates(c, m, scc, mixc))
    lo, up, lol, upu, nnz = bandwidths(m)
    n, nr = m.n, len(m.tr) + len(m.ro)
    p, qq = lol, upu
    band = dict(lu=n * p * (p + qq), solve=2 * n * (p + qq + 1), resid=2 * nnz)
    band['primary'] = band['lu'] + (1 + 2 * (m.iterations)) * band['solve'] + m.iterations * band['resid']
    band['quality'] = (n + nr) * (2 * band['solve'] + band['resid'])
    dense = dict(lu=2 * n ** 3 / 3, solve=2 * n * n, resid=2 * n * n)
    dense['primary'] = dense['lu'] + (1 + 2 * m.iterations) * dense['solve']
    dense['quality'] = (n + nr) * (2 * dense['solve'] + dense['resid'])
    return dict(spans=spans, nm=m.N + 1, n=n, nr=nr, pieces=len(events(c)) - 1, lo=lo, up=up, lol=lol, upu=upu, nnz=nnz,
                adj=m.adj_solves, steps=m.adj_steps, t_build=t_build, t_solve=t_solve, t_ind=t_ind, t_react=t_react,
                t_gate=t_gate, band=band, dense=dense)


def run_loads(k):
    L = 4.
    pf = [(L * (j + .5) / k, -1e3) for j in range(k)]
    c = case('loads%d' % k, L, [(0., 'Pinned'), (L, 'Roller')], pf=pf, E=E, I=I)
    m = StateSolver(c)
    m.indicator()
    got = [m.state(float(x), sd) for x, sd in c['pts']]
    scc, mixc = scales(c, got)
    _, t_gate = timed(lambda: piece_gates(c, m, scc, mixc))
    _, t_eval = timed(lambda: [m.state(L * j / 997, 'R', True) for j in range(1, 997)])
    return k, m.n, t_gate, t_eval / 996


if __name__ == '__main__':
    rows = [run_size(s) for s in (2, 4, 8, 16, 32)]
    print('size series (continuous beams); times in s of the dense Python research code')
    print('%5s %4s %5s %4s %6s | %-27s | %5s %5s | %8s %8s %8s %8s %8s' % (
        'spans', 'n_m', 'n', 'n_r', 'pieces', 'bandwidth S lo/up, mult, U up', 'adj', 'steps', 'build', 'LU+refine', 'quality', 'reactions', 'piece gate'))
    for r in rows:
        print('%5d %4d %5d %4d %6d | %3d/%-3d  m %-3d  U %-3d nnz %5d | %5d %5d | %8.3f %8.3f %8.3f %8.3f %8.3f' % (
            r['spans'], r['nm'], r['n'], r['nr'], r['pieces'], r['lo'], r['up'], r['lol'], r['upu'], r['nnz'], r['adj'], r['steps'],
            r['t_build'], r['t_solve'], r['t_ind'], r['t_react'], r['t_gate']))
    print('growth exponents between the two largest sizes (log time ratio / log n ratio):')
    a, b = rows[-2], rows[-1]
    for k in ('t_build', 't_solve', 't_ind', 't_react', 't_gate'):
        print('  %-8s %.2f' % (k, math.log(b[k] / a[k]) / math.log(b['n'] / a['n'])))
    print('operation model (floating-point operations) with the measured bandwidths')
    print('%5s %6s | %12s %12s %12s | %12s %12s %12s' % ('spans', 'n', 'band primary', 'band quality', 'quality/prim', 'dense primary', 'dense quality', 'quality/prim'))
    for r in rows:
        print('%5d %6d | %12.3g %12.3g %12.1f | %12.3g %12.3g %12.1f' % (
            r['spans'], r['n'], r['band']['primary'], r['band']['quality'], r['band']['quality'] / r['band']['primary'],
            r['dense']['primary'], r['dense']['quality'], r['dense']['quality'] / r['dense']['primary']))
    print('extrapolation with the per-n constants of the largest size (p, q fixed):')
    r = rows[-1]
    cb = r['band']['quality'] / (r['n'] * (r['n'] + r['nr']))
    cd = r['dense']['quality'] / (r['n'] ** 2 * (r['n'] + r['nr']))
    for nm in (10, 30, 100, 300, 1000):
        n = 4 * nm
        print('  n_m %5d (n ~ %5d): band quality %.2g flop, dense quality %.2g flop, dense memory S+LU %.1f MB, band %.2f MB' % (
            nm, n, cb * n * n * 1.1, cd * n ** 3 * 1.1, 2 * 8 * n * n / 1e6, 2 * 8 * n * (r['lol'] + r['upu'] + 1) / 1e6))
    print('load series (point loads in one segment): piece gate and one field evaluation')
    for k in (10, 40, 160, 640):
        kk, n, tg, te = run_loads(k)
        print('  loads %4d  unknowns %d  piece gate %.3f s  per evaluation %.2e s' % (kk, n, tg, te))
