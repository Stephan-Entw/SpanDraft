"""Round 5, stages B-F: the complete proof chain of the contract solver (fpc.py) on hard
reference models, against exact rational arithmetic.

  B  data and residual: |S^ - S*| <= dS, |sb^ - sb*| <= dsb entrywise and |r*| <= d
     componentwise, with r* = sb* - S* y^ the residual of the exact system (S*, sb*: the same
     formulas in exact arithmetic on the stored doubles).
  C  verification premise: |I - R S*| <= M entrywise (R rows: computed adjoints, M = |S_res| +
     |Z|^T dS); enclosure of the exact inverse |S*^-1 - R| <= M|R| + alpha mu^2/(1 - mu) v
     (n <= NC only, O(n^3) in rationals).
  D  functional: |y^ - y*| <= E (verified bound) for every unknown; reactions against the exact
     reference with their bounds. Also counted: values whose error exceeds the unverified beta.
  E  field and gate: at dense points of every piece (ends, interior, 1 ulp from the ends) the
     SI value lies within its SI bound; the bound inside a piece stays below the piece majorant;
     for delivered models the error in the contract measure (exact reference scales) stays below
     the gate; the piece lower bound LB (M, V) lies below the exact Bernstein enclosure of
     min |Q| (n <= NB only).
  F  output: SI values and bounds include the back-scaling; range cases are rejected with
     NumericalRange before a value is delivered.
Also per model: the exact solution of (S*, sb*) equals the exact mechanical reference
transfer_ref.py at the stations (formulation check), and the state.py/refine_study bound (round 4,
with verification) at the same points for comparison.
Sets: targeted (close supports, spring clusters stiff/soft, graded matrices, EI changes, short
loads with sign change), near tau, perturbed LU (1e-4, 1e-1), underflow (U1-U6, tiny beam,
subnormal evaluation point), zero-crossing loads, short heavy loads, EI change 1 ulp from a
spring, the two unverifiable models of round 4. Each in gradual mode; underflow and constructed
sets also with emulated FTZ/DAZ.
Usage: python3 state/fpc_chain.py SET        (SET: targeted | neartau | perturbed | underflow |
                                              constructed | unverified)"""
import json, math, os, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
import fpc
from fpc import solve_case, MINN, uadd, umul, udiv, dn, gb, TAU
from cases import all_cases
from coverage import events
from gen import case
from metrics import Q

HERE = os.path.dirname(os.path.abspath(__file__))
NC = 30          # stage C up to this size
NB = 40          # Bernstein lower-bound check up to this size


def exact_solve(A, b):
    n = len(A)
    M = [row[:] + [bb] for row, bb in zip(A, b)]
    for i in range(n):
        p = next(r for r in range(i, n) if M[r][i] != 0)
        M[i], M[p] = M[p], M[i]
        for r in range(i + 1, n):
            if M[r][i] != 0:
                f = M[r][i] / M[i][i]
                M[r] = [a - f * c for a, c in zip(M[r], M[i])]
    x = [Fr(0)] * n
    for i in reversed(range(n)):
        x[i] = (M[i][n] - sum(M[i][j] * x[j] for j in range(i + 1, n))) / M[i][i]
    return x


def exact_inverse(A):
    n = len(A)
    M = [row[:] + [Fr(int(i == j)) for j in range(n)] for i, row in enumerate(A)]
    for i in range(n):
        p = next(r for r in range(i, n) if M[r][i] != 0)
        M[i], M[p] = M[p], M[i]
        piv = M[i][i]
        M[i] = [a / piv for a in M[i]]
        for r in range(n):
            if r != i and M[r][i] != 0:
                f = M[r][i]
                M[r] = [a - f * c for a, c in zip(M[r], M[i])]
    return [row[n:] for row in M]


def contract_scales(c, ex):
    """Exact reference scales of the contract (7.2) in SI from the exact solution at the event
    stations (both sides) and the exact data."""
    ev = events(c)
    L = Fr(float(c['L']))
    mx = [Fr(0)] * 4
    for x in ev:
        for sd in ('L', 'R'):
            if (x == 0 and sd == 'L') or (x == c['L'] and sd == 'R'):
                continue
            st = ex.state(Fr(float(x)), sd)
            for q in range(4):
                mx[q] = max(mx[q], abs(st[Q[q]]))
    F = sum((abs(Fr(float(v))) for _, v in c['pf']), Fr(0)) + \
        sum((abs(Fr(float(v))) * (Fr(float(b)) - Fr(float(a))) for a, b, v in c['q']), Fr(0)) + \
        sum(((abs(Fr(float(v1))) + abs(Fr(float(v2)))) / 2 * (Fr(float(b)) - Fr(float(a))) for a, b, v1, v2 in c.get('lin', [])), Fr(0)) + \
        sum((abs(Fr(float(v))) for _, v in c['pm']), Fr(0)) / L
    EImin = min([Fr(float(c['E'])) * Fr(float(c['I']))] + [Fr(float(v)) for _, v in c.get('ei', [])])
    SM = max(mx[2], F * L)
    SV = max(SM / L, F)
    St = max(mx[1], SM * L / EImin)
    Sw = max(mx[0], SM * L * L / EImin, St * L)
    return [Sw, St, SM, SV]


def dense(ev):
    out = []
    for x0, x1 in zip(ev, ev[1:]):
        h = x1 - x0
        pts = [(x0, 'R'), (math.nextafter(x0, x1), 'R')]
        pts += [(x0 + h * f, 'R') for f in (1e-12, .25, .5, .75, 1 - 1e-12)]
        pts += [(math.nextafter(x1, x0), 'R'), (x1, 'L')]
        out.append(((x0, x1), [(x, sd) for x, sd in pts if (x0 <= x < x1 and sd == 'R') or (x == x1 and sd == 'L')]))
    return out


def chain(c, mode='gradual', LUeps=None, old=True):
    t0 = time.time()
    o, m = solve_case(c, exact_twin=True, LUeps=LUeps, mode=mode)
    out = dict(id=c['id'], mode=mode, LUeps=LUeps, status=o['status'], cause=o.get('cause'), detail=o.get('detail'),
               gate=o.get('gate'), mu=o.get('mu'))
    ex = T.Transfer(c)
    if m is None or not hasattr(m, 'y'):
        # range rejections etc.: report the error a delivery would have had (state.py) if possible
        return out
    fpc.MODE[0] = mode
    try:
        n = m.n
        out['n'] = n
        Rr, Cc = [Fr(v) for v in m.rs], [Fr(v) for v in m.cs]
        Sx = [[Rr[i] * m.Ax[i][j] * Cc[j] for j in range(n)] for i in range(n)]
        sbx = [Rr[i] * m.bx[i] for i in range(n)]
        # stage B
        vS = sum(abs(Fr(m.S[i][j]) - Sx[i][j]) > Fr(m.dS[i][j]) for i in range(n) for j in range(n))
        vb = sum(abs(Fr(m.sb[i]) - sbx[i]) > Fr(m.dsb[i]) for i in range(n))
        y = [Fr(v) for v in m.y]
        rx = [sbx[i] - sum(Sx[i][j] * y[j] for j in range(n) if Sx[i][j]) for i in range(n)]
        vr = sum(abs(rx[i]) > Fr(m.d[i]) for i in range(n))
        wr = max((float(abs(rx[i]) / Fr(m.d[i])) for i in range(n) if m.d[i]), default=0.)
        out.update(B_entries=vS, B_rhs=vb, B_res=vr, B_res_worst=wr)
        ys = exact_solve(Sx, sbx)
        # formulation check against transfer_ref at the stations (right side)
        mism = 0
        for i, x in enumerate(m.st[:-1]):
            stx = ex.state(Fr(x), 'R')
            for q in range(4):
                k = 4 * i + q
                val = Cc[m.col[k]] * ys[m.col[k]] if k in m.col else Fr(0)
                if val * Fr(2) ** m.Eq[q] != stx[Q[q]]:
                    mism += 1
        out['formulation_mismatch'] = mism
        # stage D
        e = [abs(y[i] - ys[i]) for i in range(n)]
        out['D_viol'] = sum(e[i] > Fr(m.E[i]) for i in range(n)) if m.verified else None
        out['D_raw_viol'] = sum(e[i] > Fr(m.beta[i]) for i in range(n))
        out['D_worst'] = max((float(e[i] / Fr(m.E[i])) for i in range(n) if m.E[i]), default=0.)
        out['verified'] = m.verified
        # stage C
        if m.verified and n <= NC:
            Z = m.Z
            Mm = [[uadd(m.SB[i][j], sum(abs(Z[i][k]) * m.dS[k][j] for k in range(n)) * (1 + gb(2 * n))) for j in range(n)] for i in range(n)]
            vc = 0
            for i in range(n):
                zi = [Fr(v) for v in Z[i]]
                for j in range(n):
                    cij = (1 if i == j else 0) - sum(zi[k] * Sx[k][j] for k in range(n) if Sx[k][j])
                    if abs(cij) > Fr(Mm[i][j]):
                        vc += 1
            inv = exact_inverse(Sx)
            mu, v = m.mu, m.v
            vz = 0
            for k in range(n):
                col = [abs(Z[l][k]) for l in range(n)]
                alpha = max(udiv(col[l], v[l]) for l in range(n))
                for i in range(n):
                    b = uadd(sum(Mm[i][l] * col[l] for l in range(n)) * (1 + gb(2 * n)),
                             udiv(umul(umul(alpha, umul(mu, mu)), v[i]), dn(1 - mu)))
                    if abs(inv[i][k] - Fr(Z[i][k])) > Fr(b):
                        vz += 1
            out.update(C_premise=vc, C_inverse=vz)
        # reactions (stage D, functionals)
        ert, err_r = ex.reactions()
        exr = {('R', float(a)): R for a, R in ert}
        exr.update({('C', float(a)): R for a, R in err_r})
        rv = rw = 0
        rworst = 0.
        for kind, a, val, bnd, q, how in m.reactions():
            Rx = exr[(kind, a)]
            vs = math.ldexp(val, m.Eq[q])
            bs = umul(bnd, 2.0 ** m.Eq[q]) + (fpc.LAM if abs(vs) <= MINN else 0.)
            err = abs(Fr(vs) - Rx)
            if err > Fr(bs):
                rv += 1
            rworst = max(rworst, float(err / Fr(bs)) if bs else (0. if err == 0 else math.inf))
        out.update(R_viol=rv, R_worst=rworst)
        # stage E / F
        ev = [float(x) for x in events(c)]
        Sc = contract_scales(c, ex)
        ve = vm = vg = 0
        worst = worstn = 0.
        maj = {(x0, x1, q): B for x0, x1, q, lb, B, D in getattr(m, 'pieceinfo', [])}
        for (x0, x1), pts in dense(ev):
            for x, sd in pts:
                vn, bn = m.evaluate(x, sd)
                vs, bs = m.to_si(vn, bn)
                stx = ex.state(Fr(x), sd)
                for q in range(4):
                    err = abs(Fr(vs[q]) - stx[Q[q]])
                    if err > Fr(bs[q]):
                        ve += 1
                    worst = max(worst, float(err / Fr(bs[q])) if bs[q] else (0. if err == 0 else math.inf))
                    B = maj.get((x0, x1, q))
                    if B is not None and x0 < x <= x1 and bn[q] > B:
                        vm += 1
                    D = Sc[q] if q < 2 else max(Sc[q], abs(stx[Q[q]]))
                    if D:
                        worstn = max(worstn, float(err / D))
        out.update(E_viol=ve, E_worst=worst, E_majorant=vm, E_err_norm=worstn)
        if o['status'] == 'delivered':
            out['E_err_over_gate'] = worstn > o['gate']
        # Bernstein: LB <= min |p| exact
        if n <= NB and getattr(m, 'pieceinfo', None):
            from piece_oracle import NODES, interpolate, min_abs
            vlb = und = 0
            for x0, x1, q, lb, B, D in m.pieceinfo:
                if lb <= 0:
                    continue
                X0, X1 = Fr(x0), Fr(x1)
                xs = [X0 + (X1 - X0) * t for t in NODES]
                co = interpolate([ex.state(X, 'R')[Q[q]] for X in xs])
                lo, hi = min_abs(co)
                lbs = Fr(lb) * Fr(2) ** m.Eq[q]
                if lbs > hi:
                    vlb += 1
                elif lbs > lo:
                    und += 1
            out.update(E_LB_viol=vlb, E_LB_undecided=und)
    finally:
        fpc.MODE[0] = 'gradual'
    if old and mode == 'gradual' and not LUeps:
        out.update(old_bound(c, ex))
    out['t'] = round(time.time() - t0, 2)
    return out


def old_bound(c, ex):
    """Round-4 bound (refine_study, rules A, verification) at the evaluation points."""
    from refine_study import RS
    try:
        r = RS(c, 'A', 'A', trust=True)
        r.c = c
        r.adj_solves = r.adj_steps = 0
        r.indicator()
    except Exception as e:
        return dict(old='failed')
    v = 0
    w = 0.
    for x, sd in c['pts']:
        vals, ind = r.state(float(x), sd, True)
        stx = ex.state(Fr(float(x)), sd)
        for q in range(4):
            err = abs(Fr(vals[q]) - stx[Q[q]])
            if err > Fr(ind[q]):
                v += 1
            w = max(w, float(err / Fr(ind[q])) if ind[q] else (0. if err == 0 else math.inf))
    return dict(old_viol=v, old_worst=w, old_verified=r.verified)


# ---------------------------------------------------------------- reference sets
def targeted():
    names = ['lagerpaar', 'lagerdrei', 'lagerfuenf', 'acht', 'drehacht', 'gemischt', 'eiimcluster', 'eiamlager',
             'kurzlastvz', 'gelenkcluster', 'federaneinsp', 'nurfedern', 'paar', 'gelenkneblager', 'federgelenkfeder']
    return [c for _, c in all_cases(distances=(1e-3, 1e-7, 1e-9, 1e-11), exponents=(-3, 0, 12), names=names)]


def neartau():
    return [json.loads(l)['case'] for l in open(os.path.join(HERE, '..', 'results', 'state-near-tau-models.log'))]


def underflow():
    L, E, I = 4., 210e9, 8e-6
    out = []
    for P in (1e-300, 1e-310, 1e-320):
        out.append(case(f'U1|P={P:g}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(1.3, -P)], E=E, I=I))
    for k in (250, 280, 300, 320, 330):
        a = L * 2. ** -k
        out.append(case(f'U2|a=2^-{k}L', L, [(0., 'Pinned'), (a, 'Roller'), (L, 'Roller')], pf=[(2.5, -1e4)], q=[(0., a, -1e6)], E=E, I=I))
    c = case('U3|x=1e-320', L, [(L, 'Fixed')], pf=[(0., -1e4)], E=E, I=I)
    c['pts'] = c['pts'] + [[1e-320, 'R'], [5e-324, 'R']]
    out.append(c)
    for wt in (1e-300, 1e-305, 1e-310, 1e-315, 1e-318, 1e-321):
        Lt, Et, It = 1e-60, 1e10, 1.
        P = wt * 48 * Et * It / Lt ** 3
        out.append(case(f'U4|w~{wt:g}', Lt, [(0., 'Pinned'), (Lt, 'Roller')], pf=[(Lt / 2, -P)], E=Et, I=It))
    for k in (200, 300, 335):
        for eir in (1e-8, 1e8):
            a = L * 2. ** -k
            EI = E * I
            out.append(case(f'U5|2^-{k}|{eir:g}', L, [(0., 'Pinned'), (L, 'Roller')],
                            springs=[(a, EI / L ** 3, 0, False), (2 * a, 1e12 * EI / L ** 3, 0, False)],
                            ei=[(a, EI * eir), (2 * a, EI)], pf=[(1.3, -1e4)], E=E, I=I))
    for ks in (1e-300, 1e-150, 1e150, 1e300):
        out.append(case(f'U6|k={ks:g}', L, [(0., 'Pinned'), (L, 'Roller')], springs=[(2., ks, 0, False)], pf=[(1.3, -1e4)], E=E, I=I))
    # loads of very different size in one model (relative 1e-300 and 1e-310 next to 1e4 N)
    for P in (1e-296, 1e-306):
        out.append(case(f'U7|mixed P={P:g}', L, [(0., 'Pinned'), (L, 'Roller')], pf=[(1.3, -1e4), (2.9, P)], E=E, I=I))
    return out


def constructed():
    from fpc_stage_a import models
    L, E, I = 4., 210e9, 8e-6
    EI = E * I
    out = [c for c in models() if not c['id'].startswith(('f|a', 's|a'))]
    x = 1.1385679834798157
    out.append(case('ei-1ulp-spring', L, [(0., 'Pinned'), (L, 'Roller')], springs=[(x, 1e3 * EI / L ** 3, 0, False)],
                    ei=[(math.nextafter(x, 9), 3 * EI)], pf=[(.4, -1e4)], q=[(0., L, -2e3)], E=E, I=I))
    out.append(case('load-in-short-section', .02, [(0., 'Pinned'), (.00384, 'Roller'), (.01345, 'Roller'), (.02, 'Roller')],
                    pf=[(.0132539, -1e3)], q=[(0., .02, -1e2)], E=E, I=I * 1e-8))
    # 6 nm support pair with large shear between the supports and a small one outside
    out.append(case('6nm-pair', L, [(0., 'Pinned'), (2., 'Roller'), (2. + 6e-9, 'Roller'), (L, 'Roller')],
                    pf=[(2. + 3e-9, -1e4), (3.5, -1.)], q=[(0., L, -1.)], E=E, I=I))
    return out


def unverified():
    from gate_study import models
    cs, _ = models(['s', '1e-11', '801'])
    return [cs[105], cs[800]]


if __name__ == '__main__':
    name = sys.argv[1]
    t0 = time.time()
    if name == 'targeted':
        runs = [(c, 'gradual', None) for c in targeted()]
    elif name == 'neartau':
        runs = [(c, 'gradual', None) for c in neartau()[:40]]
    elif name == 'perturbed':
        cs = [c for c in targeted() if '|1e-11|' in c['id'] or '|1e-09|' in c['id']] + neartau()[:15]
        runs = [(c, 'gradual', (1e-8, 1e-4)) for c in cs] + [(c, 'gradual', (1e-8, 1e-4, 1e-1)) for c in cs]
    elif name == 'underflow':
        runs = [(c, md, None) for c in underflow() for md in ('gradual', 'ftz')]
    elif name == 'constructed':
        runs = [(c, md, None) for c in constructed() for md in ('gradual', 'ftz')]
    elif name == 'unverified':
        runs = [(c, md, None) for c in unverified() for md in ('gradual',)]
    else:
        raise SystemExit(__doc__)
    for c, md, eps in runs:
        try:
            o = chain(c, md, eps)
        except Exception as e:          # report, do not hide
            o = dict(id=c['id'], mode=md, LUeps=eps, status='exception', detail=repr(e))
        o['set'] = name
        print('DATA ' + json.dumps(o), flush=True)
    print('%s: runs %d | %.0f s' % (name, len(runs), time.time() - t0))
