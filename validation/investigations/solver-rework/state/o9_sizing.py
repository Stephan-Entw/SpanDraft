"""Round 5, point 10: size of the quality contract (O9) from the target architecture.

Operation and memory model, no optimisation, no measurement of C# code. Constants from
results/state-cost.log (continuous beams, largest size): n = 3.5 n_m unknowns, n_r = 0.54 n_m
restraints, nnz(S) = 3.35 n, band of the multipliers p = 2, of U q = 4; adjoint refinement
1.002 steps per adjoint solve (state-refine-summary, rule A); verification with 1 + 3 passes
(weights beta + delta D and three Neumann steps, fpc.py).
Per adjoint solve (band): solve 2n(p+q+1) per right-hand side, residual 2 nnz; one refinement
step = residual + solve; verification residual with bound 4 nnz; beta 2n.
Per verification pass: (M v)_i = sbar_i^T v + |z_i|^T (dS v) for all n functionals: 4 n^2 (+ 2 nnz).
Dense: LU 2/3 n^3, solve 2 n^2, residual 2 n^2.
Strategies:
  store     all adjoints z_i and residual bounds sbar_i of the n state unknowns: 2 n^2 doubles;
            the n_r reaction adjoints are used once (no storage).
  resolve   nothing stored: every pass solves the n adjoints again (with refinement and residual
            bound); memory O(n (p + q)).
  block     blocks of b adjoints stored per pass; operations as resolve, memory 2 n b.
Usage: python3 state/o9_sizing.py"""

N_PER = 3.5
NR_PER = 0.54
P, Q = 2, 4
NNZ = 3.35
STEPS = 1.002
PASSES = 4
FLOPS = 1e9          # assumed scalar double operations per second (order of magnitude only)


def counts(nm):
    n = N_PER * nm
    nr = NR_PER * nm
    nnz = NNZ * n
    solve_b = 2 * n * (P + Q + 1)
    res = 2 * nnz
    adj_b = solve_b + STEPS * (res + solve_b) + 2 * res + 2 * n      # incl. verification residual bound
    passes = PASSES * (4 * n * n + 2 * nnz)
    prim_b = n * P * (P + Q) + 6 * (solve_b + res)
    store_b = (n + nr) * adj_b + passes
    resolve_b = (n + nr) * adj_b + PASSES * n * adj_b + passes
    solve_d = 2 * n * n
    res_d = 2 * n * n
    adj_d = solve_d + STEPS * (res_d + solve_d) + 2 * res_d + 2 * n
    store_d = 2 / 3 * n ** 3 + (n + nr) * adj_d + passes
    mem_store = 2 * n * n * 8
    mem_dense = 2 * n * n * 8
    mem_band = n * (P + 2 * Q + 2) * 8
    return dict(nm=nm, n=n, nr=nr, adj=n + nr, prim_b=prim_b, store_b=store_b, resolve_b=resolve_b, store_d=store_d,
                mem_store=mem_store, mem_dense=mem_dense, mem_band=mem_band)


def mb(v):
    return '%.3g MB' % (v / 2 ** 20) if v < 2 ** 30 else '%.3g GB' % (v / 2 ** 30)


def sec(v):
    t = v / FLOPS
    return '%.2g ms' % (t * 1e3) if t < 1 else '%.3g s' % t


if __name__ == '__main__':
    print(__doc__.split('Usage')[0])
    print('%6s %7s %8s | %11s %11s | %13s %13s %13s | %11s %11s %11s' % (
        'n_m', 'n', 'adjoints', 'primary(b)', 'time', 'store (band)', 'resolve(band)', 'dense', 'mem store', 'mem dense', 'mem band'))
    for nm in (10, 50, 100, 250, 500, 1000, 5000):
        c = counts(nm)
        print('%6d %7d %8d | %11.2e %11s | %6.2e %6s %6.2e %6s %6.2e %6s | %11s %11s %11s' % (
            nm, c['n'], c['adj'], c['prim_b'], sec(c['prim_b']), c['store_b'], sec(c['store_b']), c['resolve_b'], sec(c['resolve_b']),
            c['store_d'], sec(c['store_d']), mb(c['mem_store']), mb(c['mem_dense']), mb(c['mem_band'])))
