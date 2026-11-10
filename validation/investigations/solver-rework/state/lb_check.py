"""Round 3, point 2 follow-up: is the closed-form lower bound LB_q of variant C (gate_study.py)
below |Q(x)| at every densely evaluated point of its piece, for the computed field and for the
exact field? Same dense points as coverage.py (15 uniform, geometric towards both ends). Points
where the exact |Q| is below LB are listed with the model's station gate.
Usage: python3 state/lb_check.py targeted | s|f r0 N"""
import math, os, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from coverage import events, piece_points
from gate_study import qsum, models
from kinematics import is_mechanism
from state import StateSolver, gamma
from metrics import scales, denom

if __name__ == '__main__':
    t0 = time.time()
    cases, name = models(sys.argv[1:])
    nmod = npieces = viol_c = viol_e = 0
    tight = math.inf
    for c in cases:
        if is_mechanism(c):
            continue
        try:
            m = StateSolver(c)
        except ZeroDivisionError:
            continue
        m.indicator()
        ex = T.Transfer(c)
        nmod += 1
        ev = events(c)
        for x0, x1 in zip(ev, ev[1:]):
            v0 = m.state(x0, 'R')
            v1, b1 = m.state(x1, 'L', True)
            h = x1 - x0
            qs = qsum(c, x0, x1)
            lb = {3: (max(abs(v0[3]), abs(v1[3])) - h * qs - b1[3]) * (1 - gamma(8)),
                  2: (max(abs(v0[2]) - abs(v0[3]) * h - qs * h * h / 2, abs(v1[2]) - abs(v1[3]) * h - qs * h * h / 2) - b1[2]) * (1 - gamma(8))}
            if lb[2] <= 0 and lb[3] <= 0:
                continue
            npieces += 1
            pts = [(x0, 'R')] + [(x, 'R') for x in piece_points(x0, x1)] + [(x1, 'L')]
            for x, sd in pts:
                v = m.state(x, sd)
                st = ex.state(Fr(x), sd)
                for i, key in ((2, 'M'), (3, 'V')):
                    if lb[i] <= 0:
                        continue
                    if abs(v[i]) < lb[i]:
                        viol_c += 1
                    if abs(st[key]) < Fr(lb[i]):
                        viol_e += 1
                        got = [m.state(float(xx), sd_, True) for xx, sd_ in c['pts']]
                        scc, mixc = scales(c, [g[0] for g in got])
                        gate = max(max(e[k] / denom(k, v_[k], scc, mixc) for v_, e in got for k in range(4)),
                                   *(e / max(abs(R), mixc[3]) for _, R, e in m.reactions(True)[0]))
                        print('  exact below LB: %-16s %s x %.17g side %s | |Q exact| %.6e LB %.6e |Q computed| %.6e beta(x1-) %.2e '
                              'actual error here %.2e | model station gate %.1e (%s)' % (
                                  c['id'], key, x, sd, float(abs(st[key])), lb[i], abs(v[i]), b1[i], float(abs(Fr(v[i]) - st[key])),
                                  gate, 'delivered' if gate <= 1e-8 else 'rejected'))
                    tight = min(tight, abs(v[i]) / lb[i])
    print('%s: models %d, pieces with LB > 0: %d | |Q computed| < LB: %d, |Q exact| < LB: %d | smallest |Q|/LB %.6f | %.0f s' % (
        name, nmod, npieces, viol_c, viol_e, tight, time.time() - t0))
