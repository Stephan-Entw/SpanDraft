"""Round 4, phase 2.3: underflow paths against the eta model and the LAPACK SAFE1/SAFE2 treatment.

Question: is there a path on which a nonzero rounding or underflow uncertainty leads to beta = 0
or to a gate that is too small?

Cases (each against the exact reference transfer_ref.py; float model as in state.py):
  U1  point load 1e-310 N and 1e-320 N on a simple beam (excitation primitive not normal after
      nondimensionalisation; range rule).
  U2  subnormal load integrals: a distributed load on a mechanical section of length 2^-k L
      (k = 250..330) between two supports at 0 and a: a^3 is normal, q a^4 / 24 underflows.
  U3  evaluation at a subnormal local coordinate: EvaluateAt(x) with x = 1e-320 m on a beam
      whose section starts at 0.
  U4  SI output underflow: tiny beam (L = 1e-60 m) whose nondimensional values are normal but
      whose deflection in SI is subnormal (w_max about 1e-305 ... 1e-321 m).
  U5  equilibration: can Ruiz scaling turn a normal matrix entry into a subnormal one?
      Search over short sections [a, 2a], a = 2^-k L,, EI ratios and stiff springs.
  U6  rows of very different scale (stiffness 1e-300 ... 1e300 in SI with normal nondimensional
      values).
Per case: range rule as specified (every nonzero coefficient and excitation primitive normal
after nondimensionalisation), new candidate checks (nonzero entries of the equilibrated S
normal; SI result scales S_q normal), gate as computed by state.py, gate with an additional
eta per SI conversion (beta_SI + eta), actual error (mixed measure; scales from the exact
solution, no fallback to 1).
Usage: python3 state/underflow_study.py"""
import math, os, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from gen import case
from kinematics import is_mechanism
from metrics import Q, load_scale
from state import StateSolver, ETA

NORMAL = 2. ** -1022
TAU = 1e-8


def normal(v):
    return v == 0 or abs(v) >= NORMAL


def exact_scales(c, rows):
    """Mixed-measure denominators from exact values, without the fallback to 1 of metrics.scales."""
    L = Fr(c['L'])
    EI = min([Fr(c['E']) * Fr(c['I'])] + [Fr(e[1]) for e in c.get('ei', [])])
    sc = [max(abs(r[i]) for r in rows) for i in range(4)]
    sc[3] = max(sc[3], sc[2] / L); sc[2] = max(sc[2], sc[3] * L); sc[1] = max(sc[1], sc[2] * L / EI)
    sc[0] = max(sc[0], sc[2] * L * L / EI, sc[1] * L)
    F = Fr(load_scale(c))
    SM = max(max(abs(r[2]) for r in rows), F * L)
    SV = max(SM / L, F)
    return [sc[0], sc[1], SM, SV]


def analyse(c, extra_x=()):
    out = dict(id=c['id'])
    m = StateSolver(c)
    A = [v for row in m.A for v in row if v != 0]
    S = [v for row in m.S for v in row if v != 0]
    b = [v for v in m.b if v != 0]
    out['A_normal'] = all(normal(v) for v in A)
    out['b_normal'] = all(normal(v) for v in b)
    out['S_normal'] = all(normal(v) for v in S)
    out['min_A'] = min(map(abs, A))
    out['min_S'] = min(map(abs, S))
    # (input value, nondimensional value): an input != 0 must stay a normal number != 0
    prims = [(v, v * m.f_V) for _, v in m.pf] + [(v, v * m.f_M) for _, v in m.pm]
    fq = m.Lr ** 3 / m.EIr
    prims += [(q, q * fq) for _, _, q in c['q']] + [(q, q * fq) for l in c.get('lin', []) for q in l[2:]]
    out['prim_normal'] = all(v == 0 or (s != 0 and normal(s)) for v, s in prims)
    m.indicator()
    ex = T.Transfer(c)
    pts = [(float(x), sd) for x, sd in c['pts']] + list(extra_x)
    exact = [[ex.state(Fr(x), sd)[k] for k in Q] for x, sd in pts]
    got = [m.state(x, sd, True) for x, sd in pts]
    D = exact_scales(c, exact)
    Dc = exact_scales(c, [[Fr(v) for v in g[0]] for g in got])
    out['SI_scales_normal'] = all(normal(float(v)) for v in Dc)
    gate = gate_eta = err = 0.
    for (v, e), r in zip(got, exact):
        for i in range(4):
            den = Dc[i] if i < 2 else max(abs(Fr(v[i])), Dc[i])
            den_x = D[i] if i < 2 else max(abs(r[i]), D[i])
            g = math.inf if den == 0 else float(Fr(e[i]) / den)
            ge = math.inf if den == 0 else float((Fr(e[i]) + Fr(ETA)) / den)
            gate, gate_eta = max(gate, g), max(gate_eta, ge)
            ae = abs(Fr(v[i]) - r[i])
            err = max(err, math.inf if den_x == 0 and ae else (0. if ae == 0 else float(ae / den_x)))
    out.update(gate=gate, gate_eta=gate_eta, err=err, S=[float(v) for v in Dc])
    out['verdict'] = ('range rule' if not (out['A_normal'] and out['prim_normal']) else
                      'delivered' if gate <= TAU else 'rejected')
    out['verdict_new'] = ('range rule' if not (out['A_normal'] and out['prim_normal'] and out['S_normal'] and out['SI_scales_normal'])
                          else 'delivered' if gate_eta <= TAU else 'rejected')
    out['wrong'] = out['verdict'] == 'delivered' and err > TAU
    out['wrong_new'] = out['verdict_new'] == 'delivered' and err > TAU
    return out


def show(o):
    print('%-28s A %d prim %d S %d SI %d | gate %.2e gate+eta %.2e err %.2e | today: %-10s %s | new: %-10s %s' % (
        o['id'], o['A_normal'], o['prim_normal'], o['S_normal'], o['SI_scales_normal'], o['gate'], o['gate_eta'], o['err'],
        o['verdict'], 'WRONG' if o['wrong'] else '', o['verdict_new'], 'WRONG' if o['wrong_new'] else ''), flush=True)


if __name__ == '__main__':
    res = []
    L, E, I = 4., 210e9, 8e-6
    for P in (1e-300, 1e-310, 1e-320):
        res.append(analyse(case(f'U1|P={P:g}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(1.3, -P)], E=E, I=I)))
    for k in (250, 280, 300, 320, 330):
        a = L * 2. ** -k
        res.append(analyse(case(f'U2|a=2^-{k}L', L, [(0., 'Pinned'), (a, 'Roller'), (L, 'Roller')],
                                pf=[(2.5, -1e4)], q=[(0., a, -1e6)], E=E, I=I)))
    res.append(analyse(case('U3|x=1e-320', L, [(L, 'Fixed')], pf=[(0., -1e4)], E=E, I=I), extra_x=[(1e-320, 'R'), (5e-324, 'R')]))
    for wt in (1e-300, 1e-305, 1e-310, 1e-315, 1e-318, 1e-321):
        Lt, Et, It = 1e-60, 1e10, 1.
        P = wt * 48 * Et * It / Lt ** 3            # w_max of a simple beam with central load
        res.append(analyse(case(f'U4|w~{wt:g}', Lt, [(0., 'Pinned'), (Lt, 'Roller')], pf=[(Lt / 2, -P)], E=Et, I=It)))
    worst = None
    for k in (200, 250, 300, 320, 335):
        for eir in (1., 1e-8, 1e8):
            for kexp in (-12, 0, 15):
                a = L * 2. ** -k
                EI = E * I
                c = case(f'U5|2^-{k}|{eir:g}|{kexp}', L, [(0., 'Pinned'), (L, 'Roller')],
                         springs=[(a, 10. ** kexp * EI / L ** 3, 0, False), (2 * a, 10. ** kexp * EI / L ** 3, 0, False)],
                         ei=[(a, EI * eir), (2 * a, EI)], pf=[(1.3, -1e4)], E=E, I=I)
                if is_mechanism(c):
                    continue
                try:
                    o = analyse(c)
                except ZeroDivisionError:
                    print('%-28s numerically singular' % c['id'])
                    continue
                res.append(o)
    for ks in (1e-300, 1e-150, 1e150, 1e300):
        c = case(f'U6|k={ks:g}', L, [(0., 'Pinned'), (L, 'Roller')], springs=[(2., ks, 0, False)], pf=[(1.3, -1e4)], E=E, I=I)
        try:
            res.append(analyse(c))
        except (ZeroDivisionError, OverflowError) as e:
            print('%-28s %s' % (c['id'], type(e).__name__))
    for o in res:
        show(o)
    print('summary: cases %d | delivered with error > tau: today %d, with the new checks %d | S has a subnormal entry while A is normal: %d' % (
        len(res), sum(o['wrong'] for o in res), sum(o['wrong_new'] for o in res),
        sum(o['A_normal'] and not o['S_normal'] for o in res)))
