"""Comparison of the float state solver with the exact transfer reference (../transfer_ref.py).

Error measures (all against the exact solution of the doubles passed to the solver):
- glob: |error| / field scale, the scale of ../f_ref.py (max over the evaluated points, coupled
  through L and the smallest EI); used by all earlier investigation logs.
- mixed: V and reactions relative to max(|value|, S_V); M and reaction moments relative to
  max(|value|, S_M); w and th as glob. S_M = max(max|M|, F*L), S_V = max(S_M/L, F) and F is the
  sum of applied load magnitudes (forces, resultants of |q|, |C|/L).
- ind: largest indicator in the mixed measure, with all scales taken from the computed solution
  (production has no exact reference). A model is rejected if ind > tau.
A violation is a value whose exact error exceeds its indicator.
"""
import os, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from state import StateSolver

Q = ('w', 't', 'M', 'V')


def load_scale(c):
    L = float(c['L'])
    return (sum(abs(v) for a, v in c['pf']) + sum(abs(v) * (b - a) for a, b, v in c['q'])
            + sum((abs(v1) + abs(v2)) / 2 * (b - a) for a, b, v1, v2 in c.get('lin', []))
            + sum(abs(v) for a, v in c['pm']) / L)


def scales(c, rows):
    """rows: per point [w, th, M, V]. Returns (global scales, mixed floors)."""
    sc = [max(abs(r[i]) for r in rows) or 1. for i in range(4)]
    L = float(c['L'])
    EI = min([c['E'] * c['I']] + [e[1] for e in c.get('ei', [])])
    v, m = sc[3], sc[2]
    sc[3] = max(v, m / L); sc[2] = max(m, v * L); sc[1] = max(sc[1], sc[2] * L / EI); sc[0] = max(sc[0], sc[2] * L ** 2 / EI, sc[1] * L)
    F = load_scale(c)
    SM = max(max(abs(r[2]) for r in rows), F * L) or 1.
    SV = max(SM / L, F) or 1.
    return sc, [sc[0], sc[1], SM, SV]


def denom(i, value, sc, mix):
    return max(abs(value), mix[i]) if i >= 2 else sc[i]


def compare(c, **opt):
    ex = T.Transfer(c)
    m = StateSolver(c, **opt)
    m.indicator()
    exact, got, ind = [], [], []
    for x, sd in c['pts']:
        st = ex.state(Fr(float(x)), sd)
        exact.append([st[k] for k in Q])
        v, e = m.state(float(x), sd, True)
        got.append(v)
        ind.append(e)
    fexact = [[float(v) for v in r] for r in exact]
    sc, mix = scales(c, fexact)
    scc, mixc = scales(c, got)
    out = dict(id=c.get('id'), n=m.n, iterations=m.iterations, omega=m.omega, viol=[])
    glob = mixed = indm = 0.
    for (x, sd), r, fr, g, e in zip(c['pts'], exact, fexact, got, ind):
        for i in range(4):
            err = abs(Fr(g[i]) - r[i])
            ferr = float(err)
            glob = max(glob, ferr / sc[i])
            mixed = max(mixed, ferr / denom(i, fr[i], sc, mix))
            indm = max(indm, e[i] / denom(i, g[i], scc, mixc))
            if err > Fr(e[i]):
                out['viol'].append(dict(x=x, side=sd, q=Q[i], err=ferr, ind=e[i], value=fr[i],
                                        rel=ferr / denom(i, fr[i], sc, mix)))
    rt, rr = ex.reactions()
    mt, mr = m.reactions(True)
    rerr = 0.
    for exact_r, computed, i in ((rt, mt, 3), (rr, mr, 2)):
        for (a, R), (_, Rm, e) in zip(exact_r, computed):
            err = abs(Fr(Rm) - R)
            rerr = max(rerr, float(err) / max(abs(float(R)), mix[i]))
            indm = max(indm, e / max(abs(Rm), mixc[i]))
            if err > Fr(e):
                out['viol'].append(dict(x=a, side='-', q='R' if i == 3 else 'Rm', err=float(err), ind=e,
                                        value=float(R), rel=float(err) / max(abs(float(R)), mix[i])))
    out.update(glob=glob, mixed=max(mixed, rerr), react=rerr, ind=indm, err=max(glob, rerr))
    return out, m
