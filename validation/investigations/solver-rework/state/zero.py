"""Point 5: zero excitation and reference scales; first axial validation.

Excitation is defined on the mechanical solver input: the set of excitation primitives (point
forces and couples, line loads, axial forces and line loads; later prescribed displacements and
imposed curvature). The excitation scale F_exc is the sum of the absolute contributions of every
primitive (never a resultant). "No excitation" means F_exc == 0, i.e. every primitive is zero;
then b == 0 exactly and, for a stable model, the exact solution is 0.
Checks: (1) zero-excitation models give exact zeros, indicator 0, no relative measure;
(2) cancelling loads (resultant 0) have F_exc > 0 and non-zero fields; (3) loads cancelling at one
point have F_exc > 0, b == 0 and an exactly zero solution, measured against F_exc; (4) a nonzero
load whose nondimensional value underflows must not be taken for "no excitation" (range check);
(5) axial random models against the exact axial reference.
Usage: python3 state/zero.py"""
import math, os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from axial import AxialSolver, AxialExact, Mechanism as AxMech
from gen import case, INF
from metrics import compare, load_scale
from state import StateSolver

L, E, I = 4., 210e9, 8e-6
EI = E * I


def excitation_scale(c):
    """F_exc of the bending input: absolute contributions of every primitive (forces, |C|/L, |q|)."""
    return load_scale(c)


def bending_cases():
    k = 1e3 * EI / L ** 3
    return dict(
        bestimmt=dict(sup=[(0, 'Pinned'), (L, 'Roller')]),
        unbestimmt=dict(sup=[(0, 'Fixed'), (L / 3, 'Roller'), (2 * L / 3, 'Roller'), (L, 'Fixed')]),
        federn=dict(sup=[], springs=[(0, k, 0, True), (L / 2, k, 1e2 * EI / L, False), (L, k, 0, False)]),
        ei_wechsel=dict(sup=[(0, 'Fixed'), (L, 'Roller')], ei=[(L / 3, EI / 50), (L / 2, 7 * EI)]),
        gelenke=dict(sup=[(0, 'Fixed'), (L / 2, 'Roller'), (L, 'Fixed')], hinges=[L / 4, 3 * L / 4]),
        nah=dict(sup=[(0, 'Pinned'), (L / 2, 'Roller'), (L / 2 + 1e-11 * L, 'Roller'), (L, 'Roller')]),
    )


def run_bending():
    print('(1) bending without excitation (no loads at all)')
    for nm, t in bending_cases().items():
        c = case(nm, L, t['sup'], springs=t.get('springs', []), hinges=t.get('hinges', []), ei=t.get('ei', []), E=E, I=I)
        m = StateSolver(c)
        m.indicator()
        rt, rr = m.reactions(True)
        exact_zero = all(v == 0.0 for v in m.x)
        reac_zero = all(R == 0.0 for _, R, e in rt + rr)
        fields = [m.state(float(x), sd, True) for x, sd in c['pts']]
        field_zero = all(v == 0.0 for vals, inds in fields for v in vals)
        ind = max([e for vals, inds in fields for e in inds] + [e for _, _, e in rt + rr])
        print('  %-11s F_exc %.1e | b == 0: %s | states exactly 0: %s | fields: %s | reactions: %s | largest indicator %.1e (underflow term only; no relative measure, scale 0) | refinement steps %d' % (
            nm, excitation_scale(c), all(v == 0.0 for v in m.b), exact_zero, field_zero, reac_zero, ind, m.iterations))
    print('(2) loads with zero resultant and zero resultant moment, but local fields')
    P = 1e4
    c = case('kompensiert', L, [(0, 'Pinned'), (L, 'Roller')], pf=[(L / 4, P), (L / 2, -2 * P), (3 * L / 4, P)], E=E, I=I)
    o, m = compare(c)
    ex = T.Transfer(c)
    Mmax = max(abs(float(ex.state(Fr(float(x)), sd)['M'])) for x, sd in c['pts'])
    print('  resultant %.1e, resultant moment %.1e, F_exc %.1e, max|M| exact %.1e | delivered, mixed error %.1e, indicator %.1e' % (
        P - 2 * P + P, P * L / 4 - 2 * P * L / 2 + P * 3 * L / 4, excitation_scale(c), Mmax, o['mixed'], o['ind']))
    print('(3) loads cancelling at one point (P and -P at the same x)')
    c = case('punktgleich', L, [(0, 'Fixed'), (L / 2, 'Roller'), (L, 'Roller')], pf=[(L / 3, P), (L / 3, -P)], E=E, I=I)
    m = StateSolver(c)
    m.indicator()
    o, _ = compare(c)
    print('  F_exc %.1e | b == 0: %s | solution exactly 0: %s | mixed error %.1e (scale from F_exc, no division by zero)' % (
        excitation_scale(c), all(v == 0.0 for v in m.b), all(v == 0.0 for v in m.x), o['mixed']))
    print('(4) non-zero load whose nondimensional value underflows (indicator without / with the underflow term)')
    for Pt in (1e3, 1e-300, 1e-310, 1e-320):
        c = case('unterlauf', L, [(0, 'Pinned'), (L, 'Roller')], pf=[(L / 3, -Pt)], E=E, I=I)
        m = StateSolver(c)
        nondim = Pt * m.f_V
        sub = nondim != 0 and abs(nondim) < 2.2250738585072014e-308
        o0, _ = compare(c, underflow=False)
        o1, _ = compare(c, underflow=True)
        print('  P = %.0e N: nondimensional P %.1e (%s), b == 0: %s | actual error %.1e | indicator without %.1e (%s), with %.1e (%s)' % (
            Pt, nondim, 'zero' if nondim == 0 else 'subnormal' if sub else 'normal', all(v == 0.0 for v in m.b), o1['mixed'],
            o0['ind'], 'deliver' if o0['ind'] <= 1e-8 else 'reject', o1['ind'], 'deliver' if o1['ind'] <= 1e-8 else 'reject'))


def axial_case(rng, r0):
    Lx = 10 ** rng.uniform(-2, 1.5)
    EA0 = rng.choice([210e9, 70e9]) * Lx * Lx * 10 ** rng.uniform(-5, -3)
    xs = sorted(rng.sample(range(0, 21), rng.randint(1, 4)))
    ax, axs = [], []
    for g in xs:
        x = Lx if g == 20 else g * Lx / 20
        if rng.random() < .3 and 0 < x:
            x = max(0., x - r0 * Lx)
        if rng.random() < .6:
            ax.append(x)
        else:
            axs.append([x, 10 ** rng.uniform(-4, 12) * EA0 / Lx])
    ax = sorted(set(ax))
    axs = [s for s in axs if s[0] not in ax]
    items = ax + [s[0] for s in axs]
    near = lambda: min(Lx, max(0., rng.choice(items) + rng.choice([-1, 1]) * r0 * Lx)) if rng.random() < .5 else rng.uniform(0, Lx)
    ea = sorted({near(): EA0 * 10 ** rng.uniform(-2, 2) for _ in range(rng.randint(0, 2))}.items())
    ea = [[x, v] for x, v in ea if 0 < x < Lx]
    mag = 10 ** rng.uniform(1, 5)
    px = [[near(), mag * rng.uniform(-1, 1)] for _ in range(rng.randint(1, 3))]
    qx = []
    for _ in range(rng.randint(0, 2)):
        a = near(); b = a + (r0 * Lx if rng.random() < .4 else rng.uniform(0, Lx - a))
        if a < b <= Lx:
            qx.append([a, b, mag / Lx * rng.uniform(-1, 1)])
    return dict(L=Lx, E=1.0, A=EA0, ax=ax, axs=axs, ea=ea, px=px, qx=qx)


def axial_points(c):
    ev = sorted({0.0, c['L']} | set(c['ax']) | {x for x, _ in c['axs']} | {x for x, _ in c['px']}
                | {x for x, _ in c['ea']} | {x for a, b, _ in c['qx'] for x in (a, b)})
    out = []
    for j, x in enumerate(ev):
        if j > 0:
            out.append((x, 'L'))
        if j < len(ev) - 1:
            out.append((x, 'R'))
            for f in (.3, .7):
                out.append((x + (ev[j + 1] - x) * f, 'R'))
    return out


def run_axial():
    print('(5) axial part')
    c = dict(L=L, E=E, A=3e-3, ax=[0.0, L / 2], axs=[[L, 1e9]], ea=[[L / 3, E * 1e-3]], px=[], qx=[])
    m = AxialSolver(c)
    print('  without excitation: states exactly 0: %s' % all(v == 0.0 for v in m.x))
    try:
        AxialSolver(dict(c, ax=[], axs=[]))
        print('  without axial restraint: solved (unexpected)')
    except ZeroDivisionError:
        print('  without axial restraint: zero pivot; exact reference:', end=' ')
        try:
            AxialExact(dict(c, ax=[], axs=[]))
            print('solvable (unexpected)')
        except AxMech:
            print('mechanism')
    for r0 in (1e-2, 1e-6, 1e-11):
        rng = random.Random(int(1 / r0) % 977 + 5)
        worst, n, mech = 0., 0, 0
        for _ in range(300):
            c = axial_case(rng, r0)
            if not c['ax'] and not c['axs']:
                continue
            try:
                ex = AxialExact(c)
            except AxMech:
                mech += 1
                continue
            m = AxialSolver(c)
            pts = axial_points(c)
            exact = [ex.state(x, sd) for x, sd in pts]
            got = [m.state(x, sd) for x, sd in pts]
            Fx = sum(abs(v) for _, v in c['px']) + sum(abs(v) * (b - a) for a, b, v in c['qx'])
            SN = max(max(abs(float(e[1])) for e in exact), Fx) or 1.
            EAmin = min([c['E'] * c['A']] + [v for _, v in c['ea']])
            Su = max(max(abs(float(e[0])) for e in exact), SN * c['L'] / EAmin) or 1.
            err = max(max(float(abs(Fr(g[0]) - e[0])) / Su, float(abs(Fr(g[1]) - e[1])) / max(abs(float(e[1])), SN)) for g, e in zip(got, exact))
            worst = max(worst, err)
            n += 1
        print('  random L/%.0e: models %d (mechanisms %d) | largest error, u relative to S_u, N relative to max(|N|, S_N): %.1e' % (1 / r0, n, mech, worst))


def run_singular():
    print('(6) exactly stable but numerically singular model without excitation')
    from decoupling import regenerate
    loaded = regenerate('f', 1e-11, 64)        # zero pivot in state-rand-f-1e-11 (two springs 5e-12 m apart)
    c = case('singulaer', loaded['L'], [tuple(v) for v in loaded['sup']], springs=[tuple(v) for v in loaded['springs']],
             hinges=loaded['hinges'], ei=loaded['ei'], E=loaded['E'], I=loaded['I'])
    try:
        StateSolver(c)
        solved = 'solved'
    except ZeroDivisionError:
        solved = 'zero pivot'
    ex = T.Transfer(c)
    zero = all(ex.state(Fr(float(x)), sd)[k] == 0 for x, sd in c['pts'] for k in ('w', 't', 'M', 'V'))
    print('  F_exc %.1e | factorisation: %s | exact solution identically 0: %s -> rule 7.2: deliver exact zeros without solving' % (
        excitation_scale(c), solved, zero))
    try:
        StateSolver(loaded)
        s2 = 'solved'
    except ZeroDivisionError:
        s2 = 'zero pivot'
    print('  same model with a load: factorisation %s -> numerically not reliably solvable' % s2)


if __name__ == '__main__':
    run_singular()
    run_bending()
    run_axial()
