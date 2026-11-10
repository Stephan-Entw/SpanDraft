"""Round 5, point 8: why the verification of round 4 failed on s|1e-11|105 and s|1e-11|800.

Per model:
- round 4 (refine_study, Dot2 residual, weights D and beta + 2^-200 D): mu_D, mu_B;
- contract (fpc.py, double residual with rigorous bound, contract scales as weights):
  mu for v = beta + delta D and after 1, 2, 3 Neumann steps v <- beta + M v + delta D;
  also with the Dot2 residual;
- the spectral radius of M (power iteration on the nonnegative matrix, informative only): if it
  is < 1 the inequality |e| <= beta + M|e| carries enough information for a rigorous bound, and
  a failure of mu < 1 is a weakness of the chosen weight vector, not a lack of information;
- the component that attains mu for v = beta + delta D: its weight, beta, (M v) and which
  rows of M contribute.
Usage: python3 state/fpc_unverified.py"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import fpc
from fpc import solve_case, udiv, uadd, udot
from fpc_chain import unverified


def matrix_M(m):
    n = m.n
    M = []
    for i in range(n):
        row = []
        for j in range(n):
            row.append(m.SB[i][j] + sum(abs(m.Z[i][k]) * m.dS[k][j] for k in range(n)))
        M.append(row)
    return M


def spectral_radius(M, it=400):
    n = len(M)
    v = [1.] * n
    lam = 0.
    for _ in range(it):
        w = [sum(M[i][j] * v[j] for j in range(n)) for i in range(n)]
        lam = max(w)
        if lam == 0:
            return 0.
        v = [x / lam for x in w]
    return lam


def round4(c):
    from refine_study import RS
    r = RS(c, 'A', 'A', trust=True)
    r.c = c
    r.adj_solves = r.adj_steps = 0
    r.indicator()
    return r.muD, r.muB


if __name__ == '__main__':
    for c in unverified():
        print('model', c['id'])
        muD, muB = round4(c)
        print('  round 4 (Dot2, weights D / beta + 2^-200 D): mu_D %.3e  mu_B %.3e -> %s' % (
            muD, muB, 'verified' if min(muD, muB) < 1 else 'not verified'))
        o, m = solve_case(c)
        print('  contract: status %s, mu for RawBound + delta D and Neumann steps: %s' %
              (o['status'], ' '.join('%.6g' % u for u in o['mu'])))
        o, m = solve_case(c, v_iter=0)
        M = matrix_M(m)
        rho = spectral_radius(M)
        print('  spectral radius of M (power iteration): %.3e' % rho)
        n = m.n
        # the component attaining mu for the first weight vector
        Dw = fpc.weights(m)
        v = [uadd(m.raw_bound[j], fpc.umul(fpc.DELTA, Dw[j])) for j in range(n)]
        Mv = [sum(M[i][j] * v[j] for j in range(n)) for i in range(n)]
        i = max(range(n), key=lambda k: Mv[k] / v[k])
        k = m.free[i]
        contrib = sorted(((M[i][j] * v[j], j) for j in range(n)), reverse=True)[:3]
        print('  attained at unknown %s%d (station x = %.17g): weight v %.3e (beta %.3e, D %.3e), (Mv) %.3e, ratio %.3e' % (
            'wtMV'[k % 4], k // 4, m.st[k // 4], v[i], m.raw_bound[i], Dw[i], Mv[i], Mv[i] / v[i]))
        for val, j in contrib:
            kj = m.free[j]
            print('     contribution M[i][j] v_j = %.3e from %s%d (v_j %.3e, M_ij %.3e: residual part %.3e, data part %.3e)' % (
                val, 'wtMV'[kj % 4], kj // 4, v[j], M[i][j], m.SB[i][j], M[i][j] - m.SB[i][j]))
        o, m = solve_case(c, v_iter=3)
        Dw = fpc.weights(m)
        g = max(m.verified_state_bound[j] / Dw[j] for j in range(n))
        print('  verified bound after Neumann steps: largest E_j / D_j %.3e (gate %.3e)' % (g, o['gate']))
