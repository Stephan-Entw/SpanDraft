"""Diagnosis of indicator violations: decomposes the error of one model.
(1) data: residual of the float system at the exact solution, against the modelled data rounding
    gamma(k)|A||x| + gamma(kb)|b|_abs, row by row;
(2) solve: exact residual of the computed solution against d (the indicator's residual vector);
(3) evaluation: error of the field value against propagated state error + evaluation rounding.
Usage: python3 state/violations.py s|f r0 index"""
import os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from gen import gen_s, gen_f
from state import StateSolver, gamma
from metrics import Q


def model(kind, r0, index):
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    for k in range(index + 1):
        c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
    return c


def diagnose(c):
    ex = T.Transfer(c)
    m = StateSolver(c)
    e = m.indicator()
    conv = [Fr(m.f_w), Fr(1), Fr(m.f_M), Fr(m.f_V)]
    xe = []
    for i, x in enumerate(m.st):
        st = ex.state(Fr(x), 'R' if i < m.N else 'L')
        v = [st[k] for k in Q]
        if i == m.N:
            v[2] = v[3] = Fr(0)
        xe += [v[q] * conv[q] for q in range(4)]
    xf = [xe[k] for k in m.free]
    worst_data = worst_solve = 0.
    rows = []
    for i in range(m.n):
        rdata = Fr(m.b[i]) - sum(Fr(m.A[i][j]) * xf[j] for j in range(m.n) if m.A[i][j])
        bound = sum(gamma(m.K[i][j] + 1) * abs(m.A[i][j] * float(xf[j])) for j in range(m.n)) + gamma(m.kb[i] + 1) * m.bab[i]
        q = float(abs(rdata)) / bound if bound else (0. if rdata == 0 else float('inf'))
        rows.append((q, i, float(rdata), bound))
        worst_data = max(worst_data, q)
    rows.sort(reverse=True)
    print('data rounding: largest |residual at exact solution| / modelled bound = %.2f' % worst_data)
    for q, i, rd, bd in rows[:4]:
        nz = {m.free[j]: (m.A[i][j], m.K[i][j]) for j in range(m.n) if m.A[i][j]}
        print('   row %3d ratio %.2f residual %.2e bound %.2e b %.3e bab %.3e kb %d entries %s' % (
            i, q, rd, bd, m.b[i], m.bab[i], m.kb[i], {k: ('%.3e' % v, kk) for k, (v, kk) in nz.items()}))
    # state errors against indicator
    bad = [(float(abs(Fr(m.x[k]) - xe[k])) / m.e[k] if m.e[k] else float('inf'), k) for k in m.free if Fr(m.x[k]) != xe[k]]
    bad.sort(reverse=True)
    print('state errors / indicator, largest:', ['%s%d %.2f' % (Q[k % 4], k // 4, r) for r, k in bad[:6]])
    return m


if __name__ == '__main__':
    c = model(sys.argv[1], float(sys.argv[2]), int(sys.argv[3]))
    print(c['id'], {k: c[k] for k in ('L', 'E', 'I', 'sup', 'springs', 'hinges', 'ei', 'lin', 'pf', 'pm', 'q')})
    diagnose(c)
