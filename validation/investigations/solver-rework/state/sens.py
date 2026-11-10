"""Input sensitivity (diagnosis, not part of the numerical contract): change of the exact solution
when one class of input doubles moves by one ulp. Measured in the mixed measure of metrics.py."""
import copy, math, os, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from metrics import scales, Q

up = lambda v: math.nextafter(v, math.inf)


def exact_rows(c, pts):
    ex = T.Transfer(c)
    return [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in pts]


def perturbations(c):
    yield 'E', dict(c, E=up(c['E']))
    yield 'I', dict(c, I=up(c['I']))
    if c['springs']:
        yield 'k', dict(c, springs=[[x, up(k) if k != 'inf' else k, kt, a] for x, k, kt, a in c['springs']])
        yield 'kt', dict(c, springs=[[x, k, up(kt) if kt not in ('inf', 0) else kt, a] for x, k, kt, a in c['springs']])
    d = copy.deepcopy(c)
    d['sup'] = [[up(a), t] if a < c['L'] else [a, t] for a, t in d['sup']]
    d['springs'] = [[up(x) if x < c['L'] else x] + rest for x, *rest in [list(v) for v in d['springs']]]
    d['hinges'] = [up(h) for h in d['hinges']]
    yield 'positions', d
    yield 'loads', dict(c, pf=[[a, up(v)] for a, v in c['pf']], q=[[a, b, up(v)] for a, b, v in c['q']],
                        lin=[[a, b, up(v1), up(v2)] for a, b, v1, v2 in c.get('lin', [])])


def sensitivity(c):
    base = exact_rows(c, c['pts'])
    fb = [[float(v) for v in r] for r in base]
    sc, mix = scales(c, fb)
    out = {}
    for name, d in perturbations(c):
        try:
            rows = exact_rows(d, c['pts'])
        except T.Mechanism:
            out[name] = math.inf
            continue
        out[name] = max(float(abs(a - b)) / (max(abs(fb[p][i]), mix[i]) if i >= 2 else sc[i])
                        for p, (ra, rb) in enumerate(zip(rows, base)) for i, (a, b) in enumerate(zip(ra, rb)))
    return out
