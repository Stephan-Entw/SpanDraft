"""Round 4, phase 2: refinement rules, error bounds and trust of the adjoint solutions.

2.1 Refinement rules, separately for the primary system S y = b and the adjoint systems S^T z = c:
    A  SpanDraft: primary: at least one step, continue while ||dy|| <= ||dy_prev|| / 2, stop at
       ||dy|| <= 2u||y||, at most 5. Adjoint: at least one step, continue while |z|^T d changes by
       more than 1/16, at most 5.
    B  xGERFS (LAPACK dgerfs): residual and componentwise backward error BERR first (SAFE1/SAFE2
       treatment), refine while BERR > eps and BERR <= LSTRES/2 and count <= ITMAX = 5.
    C  LAWN 165 / dla_gerfsx_extended: states working/converged/no-progress/unstable for the
       normwise (dx/x, column-scaled) and componentwise (dz/z) change, rthresh = 0.5,
       dz_ub = 0.25, the last correction is not applied when the iteration stops; without
       extra-precise residual and doubled y the "increase precision" transition is a stop.
       ITHRESH = 5 here (LAPACK default 10) for equal cost limits. Also recorded: the LAWN error
       estimate final_dx_x / (1 - dxratmax) (normwise, valid only with extra-precise residual).
    D  exactly 5 steps.
    one: exactly one step (adjoint only, reference of round 3); none: no adjoint refinement.
    Configurations: primary A/B/C/D with adjoint A; primary A with adjoint A/B/C/D/one.
    Every 5th model and every near-tau model additionally with LU factors perturbed by 1e-4 and
    1e-1 (same stream as gate_study), primary A, adjoint A/B/C/D.
2.2 Trust of beta (O15/O16): verification with the computed adjoints as approximate inverse
    (Rump 2010, Theorem 10.2, in the weighted infinity norm with the gate denominators D):
    E <= beta + M E with M = |S_res| + |Z|^T Gamma |S|, where row i of |S_res| bounds the exact
    residual of the i-th adjoint (|s_i| + gamma(nnz+1)(|S|^T|z_i| + e_i) + (nnz+1) eta) and
    Gamma |S| the rounding of the matrix entries (adjoint residual by the compensated dot product
    Dot2 with its rigorous error bound). Two weightings of the norm:
      D:    mu_D = max_i (M D)_i / D_i (one pass); E_i <= beta_i + D_i mu_i g/(1 - mu_D) with g the
            largest normalised beta of the state unknowns;
      beta: v = beta + 2^-200 D, mu_B = max_i (M v)_i / v_i (second pass over the stored
            adjoints); E_i <= beta_i + (M v)_i / (1 - mu_B).
    The smaller valid bound is used. Trust is run with adjoint rules A (T), B (TB) and none (TN). Recorded: mu_D, mu_B, corrected and raw gate, values whose
    exact error exceeds beta. If neither mu < 1, beta of this model is not verified (status
    'unverified'; the uncorrected beta is kept for the measurement).
Usage: python3 state/refine_study.py targeted | s|f r0 N K | neartau     (DATA lines)"""
import json, math, os, random, sys, time, zlib
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from gate_study import piece_gates, reaction_error, models
from kinematics import is_mechanism
from metrics import scales, Q, denom
from state import StateSolver, gamma, U, ETA, dot2

TAU = 1e-8
SAFMIN = 2. ** -1022
DELTA = 2. ** -200
HERE = os.path.dirname(os.path.abspath(__file__))


class RS(StateSolver):
    """StateSolver with selectable refinement rules and the trust correction."""

    def __init__(s, c, prim='A', adj='A', trust=False, LUeps=None, rng=None):
        s.prim_rule, s.adj_rule, s.trust = prim, adj, trust
        s.LUeps, s.rng = LUeps, rng
        StateSolver.__init__(s, c, refine='none', adj_refine='beta')

    def solve(s, max_iter):
        StateSolver.solve(s, max_iter)          # factorization; refine='none' leaves y unrefined
        if s.LUeps:
            for eps in s.LUeps:                 # same stream as gate_study: draw for every eps
                LUp = [[v * (1 + eps * s.rng.uniform(-1, 1)) for v in row] for row in s.LU]
            s.LU = LUp
            s.y = s.lusolve(s.sb)
        s.y, s.iterations, s.hist, s.lawn = s.refine(s.lusolve, s.residual_fn(), s.sb, s.prim_rule, primary=True)
        y, n = s.y, s.n
        s.res, s.reserr = s.residual(y)
        x = [s.cs[i] * y[i] for i in range(n)]
        s.x = [0.] * s.nfull
        for i, k in enumerate(s.free):
            s.x[k] = x[i]
        den = [sum(abs(s.S[i][j] * y[j]) for j in s.nz[i]) + abs(s.sb[i]) for i in range(n)]
        s.omega = max([abs(s.res[i]) / den[i] for i in range(n) if den[i] > 0] + [0.])
        s.berr = berr(s.res, den, max(len(r) for r in s.nz) + 1)

    def residual_fn(s, transposed=False):
        S, n = s.S, s.n
        if not transposed:
            return lambda v, b: [b[i] - sum(S[i][j] * v[j] for j in s.nz[i]) for i in range(n)]
        cols = [[i for i in range(n) if S[i][j] != 0] for j in range(n)]
        return lambda v, b: [b[j] - sum(S[i][j] * v[i] for i in cols[j]) for j in range(n)]

    def abs_fn(s, transposed=False):
        S, n = s.S, s.n
        if not transposed:
            return lambda v, b: [sum(abs(S[i][j] * v[j]) for j in s.nz[i]) + abs(b[i]) for i in range(n)]
        return lambda v, b: [sum(abs(S[i][j] * v[i]) for i in range(n) if S[i][j] != 0) + abs(b[j]) for j in range(n)]

    def refine(s, solve, resid, b, rule, primary, d=None):
        """Returns (solution, steps, contraction history, LAWN estimate)."""
        n = s.n
        y = solve(b) if not primary else s.y
        cs = s.cs
        hist = []
        steps = 0
        absf = s.abs_fn(not primary)
        if rule == 'B':                                       # dgerfs
            nzmax = max(len(r) for r in s.nz) + 1
            safe1 = nzmax * SAFMIN
            safe2 = safe1 / U
            lstres = 3.
            count = 1
            while True:
                r = resid(y, b)
                den = absf(y, b)
                be = max([abs(r[i]) / den[i] if den[i] > safe2 else (abs(r[i]) + safe1) / (den[i] + safe1) for i in range(n)] + [0.])
                if be > U and 2 * be <= lstres and count <= 5:
                    dy = solve(r)
                    y = [a + c for a, c in zip(y, dy)]
                    steps += 1
                    hist.append(be)
                    lstres = be
                    count += 1
                else:
                    break
            return y, steps, hist, None
        if rule == 'C':                                       # dla_gerfsx_extended
            WORK, CONV, NOPROG, UNST = 1, 2, 3, 0
            xs, zs = WORK, UNST
            prevdx = prevdz = math.inf
            dxratmax = dzratmax = 0.
            final_dx = final_dz = math.inf
            for cnt in range(1, 6):
                r = resid(y, b)
                dy = solve(r)
                normx = max(abs(cs[i] * y[i]) for i in range(n)) if primary else max(map(abs, y))
                normdx = max(abs(cs[i] * dy[i]) for i in range(n)) if primary else max(map(abs, dy))
                dz_z = 0.
                for i in range(n):
                    if y[i] != 0:
                        dz_z = max(dz_z, abs(dy[i]) / abs(y[i]))
                    elif dy[i] != 0:
                        dz_z = math.inf
                dx_x = normdx / normx if normx else (0. if normdx == 0 else math.inf)
                dxrat = normdx / prevdx if prevdx not in (0., math.inf) else (0. if prevdx == math.inf else math.inf)
                dzrat = dz_z / prevdz if prevdz not in (0., math.inf) else (0. if prevdz == math.inf else math.inf)
                hist.append(dxrat if math.isfinite(dxrat) else -1.)
                if xs == NOPROG and dxrat <= .5:
                    xs = WORK
                if xs == WORK:
                    if dx_x <= U:
                        xs = CONV
                    elif dxrat > .5:
                        xs = NOPROG                          # no doubled y available
                    else:
                        dxratmax = max(dxratmax, dxrat)
                    if xs > WORK:
                        final_dx = dx_x
                if zs == UNST and dz_z <= .25:
                    zs = WORK
                if zs == NOPROG and dzrat <= .5:
                    zs = WORK
                if zs == WORK:
                    if dz_z <= U:
                        zs = CONV
                    elif dz_z > .25:
                        zs = UNST
                        dzratmax = 0.
                        final_dz = math.inf
                    elif dzrat > .5:
                        zs = NOPROG
                    else:
                        dzratmax = max(dzratmax, dzrat)
                    if zs > WORK:
                        final_dz = dz_z
                if xs != WORK and (zs in (NOPROG, CONV) or (zs == UNST and cnt > 1)):
                    break
                prevdx, prevdz = normdx, dz_z
                y = [a + c for a, c in zip(y, dy)]
                steps += 1
            if xs == WORK:
                final_dx = dx_x
            lawn = final_dx / (1 - dxratmax) if dxratmax < 1 else math.inf
            return y, steps, hist, lawn
        # A (primary or adjoint) and D
        prev = math.inf
        bprev = sum(abs(y[j]) * d[j] for j in range(n)) if (not primary and rule == 'A') else 0.
        limit = 0 if rule == 'none' else 1 if rule == 'one' else 5
        for _ in range(limit):
            r = resid(y, b)
            dy = solve(r)
            y = [a + c for a, c in zip(y, dy)]
            steps += 1
            nd, ny = max(map(abs, dy)), max(map(abs, y))
            hist.append(nd / prev if prev not in (0., math.inf) else -1.)
            if rule == 'D' or rule == 'one':
                prev = nd
                continue
            if primary:
                if not (nd == nd) or nd <= 2 * U * ny or nd > 0.5 * prev:
                    break
            else:
                bb = sum(abs(y[j]) * d[j] for j in range(n))
                if abs(bb - bprev) <= bb / 16:
                    break
                bprev = bb
            prev = nd
        return y, steps, hist, None

    def adjoint_solve(s, c):
        z, steps, hist, _ = s.refine(s.lusolve_transposed, s.residual_fn(True), c, s.adj_rule, False, s.d)
        s.adj_solves = getattr(s, 'adj_solves', 0) + 1
        s.adj_steps = getattr(s, 'adj_steps', 0) + steps
        if s.trust:
            s.last_sbar = s.res_bound(z, c)
        return z

    # ----------------------------------------------------------------------- trust (2.2)
    def res_bound(s, z, c):
        """Componentwise bound of the exact residual c - S^T z of a computed adjoint. The residual
        is evaluated with the compensated dot product Dot2 (Ogita, Rump, Oishi 2005, Thm. 5.3:
        |res - exact| <= u |exact| + gamma_m^2 sum |terms|), so the rounding allowance is of
        second order; plus an eta allowance per operation for underflow, under which the
        error-free transformations are not exact."""
        n, S = s.n, s.S
        out = []
        for j in range(n):
            col = s.cols[j]
            pairs = [(-S[i][j], z[i]) for i in col]
            r = dot2(pairs, c[j])
            m = len(col) + 1
            absum = sum(abs(a * b) for a, b in pairs) + abs(c[j])
            out.append(((abs(r) + gamma(m) ** 2 * absum) / (1 - U) + 8 * m * ETA) * (1 + gamma(4)))
        return out

    def Mv(s, z, sbar, GSv, v):
        """(M v) for one functional: sbar^T v + |z|^T Gamma|S| v, rounded upwards (factor)."""
        n = s.n
        return (sum(sbar[j] * v[j] for j in range(n)) + sum(abs(z[i]) * GSv[i] for i in range(n))) * (1 + gamma(2 * n + 4))

    def GS(s, v):
        S = s.S
        return [sum(gamma(s.K[i][j] + 1) * abs(S[i][j]) * v[j] for j in s.nz[i]) * (1 + gamma(len(s.nz[i]) + 2))
                for i in range(s.n)]

    def indicator(s):
        if not s.trust:
            return StateSolver.indicator(s)
        conv = [1 / s.f_w, 1.0, 1 / s.f_M, 1 / s.f_V]
        vals = [s.x[k] * conv[k % 4] for k in s.free]
        got = [s.state(float(x), sd) for x, sd in s.c['pts']]
        scc, mixc = scales(s.c, got)
        n, S, y = s.n, s.S, s.y
        Dy = [denom(k % 4, vals[i], scc, mixc) / conv[k % 4] / s.cs[i] for i, k in enumerate(s.free)]
        d = []
        for i in range(n):
            data = sum(gamma(s.K[i][j] + 1) * abs(S[i][j] * y[j]) for j in s.nz[i])
            data += gamma(s.kb[i] + 1) * abs(s.rs[i]) * s.bab[i]
            data += (sum(s.K[i][j] + 2 for j in s.nz[i]) + s.kb[i] + len(s.nz[i]) + 2) * ETA * max(abs(s.rs[i]), 1.)
            d.append(abs(s.res[i]) + s.reserr[i] + data)
        s.d = d
        s.col = {k: i for i, k in enumerate(s.free)}
        s.cols = [[i for i in range(n) if S[i][j] != 0] for j in range(n)]
        Z, SB, by = [], [], []
        for i in range(n):
            z = s.adjoint_solve([1. if k == i else 0. for k in range(n)])
            Z.append(z)
            SB.append(s.last_sbar)
            by.append(sum(abs(z[j]) * d[j] for j in range(n)) * (1 + gamma(n + 1)))
        # weights D (gate denominators): one pass
        GSD = s.GS(Dy)
        mD = [s.Mv(Z[i], SB[i], GSD, Dy) / Dy[i] for i in range(n)]
        muD = max(mD, default=0.)
        gD = max((by[i] / Dy[i] for i in range(n)), default=0.)
        # weights v = beta + 2^-200 D (second pass over the stored adjoints). Pure beta fails for
        # components that exact rows fix exactly (beta at the eta level): there the eta allowance of
        # the adjoint residual bound dominates. The D share adds at most 2^-200 mu_D to the
        # normalised bound.
        v = [by[j] + DELTA * Dy[j] for j in range(n)]
        GSB = s.GS(v)
        mB = [s.Mv(Z[i], SB[i], GSB, v) / v[i] for i in range(n)]
        muB = max(mB, default=0.)
        s.muD, s.muB, s.by, s.GSD, s.GSB, s.gD, s.Dy, s.v = muD, muB, by, GSD, GSB, gD, Dy, v
        s.raw_e = [0.] * s.nfull
        s.e = [0.] * s.nfull
        for i in range(n):
            cands = []
            if muD < 1:
                cands.append(by[i] + Dy[i] * mD[i] * gD / (1 - muD))
            if muB < 1:
                cands.append(by[i] + mB[i] * v[i] / (1 - muB))
            s.raw_e[s.free[i]] = s.cs[i] * by[i]
            s.e[s.free[i]] = s.cs[i] * (min(cands) if cands else by[i])
        s.verified = muD < 1 or muB < 1
        return s.e

    def adjoint(s, c):
        v = [0.] * s.n
        for k, ck in c.items():
            if k in s.col:
                i = s.col[k]
                v[i] += ck * s.cs[i]
        if not any(v):
            return 0.
        z = s.adjoint_solve(v)
        b = sum(abs(z[j]) * s.d[j] for j in range(s.n))
        if s.trust:
            b *= 1 + gamma(s.n + 1)
            cands = []
            if s.muD < 1:
                cands.append(b + s.Mv(z, s.last_sbar, s.GSD, s.Dy) * s.gD / (1 - s.muD))
            if s.muB < 1:
                cands.append(b + s.Mv(z, s.last_sbar, s.GSB, s.v) / (1 - s.muB))
            b = min(cands) if cands else b
        return b


def raw_gate(c, m):
    """Station gate with the uncorrected beta (for the size of the correction)."""
    e, m.e = m.e, m.raw_e
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    m.e = e
    scc, mixc = scales(c, [g[0] for g in got])
    return max(ee[i] / denom(i, v[i], scc, mixc) for v, ee in got for i in range(4))


def berr(res, den, nz=1):
    safe1 = nz * SAFMIN
    safe2 = safe1 / U
    return max([abs(res[i]) / den[i] if den[i] > safe2 else (abs(res[i]) + safe1) / (den[i] + safe1) for i in range(len(res))] + [0.])


def measure(c, m, exact, fexact, ex):
    """Gate (station, reaction, piece C), actual error, values whose error exceeds beta."""
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    scc, mixc = scales(c, [g[0] for g in got])
    sc, mix = scales(c, fexact)
    station = max(e[i] / denom(i, v[i], scc, mixc) for v, e in got for i in range(4))
    under = 0
    err = 0.
    worst = 0.
    for (v, e), r, fe in zip(got, exact, fexact):
        for i in range(4):
            ae = abs(Fr(v[i]) - r[i])
            err = max(err, float(ae) / denom(i, fe[i], sc, mix))
            if ae > Fr(e[i]):
                under += 1
                worst = max(worst, float(ae) / e[i] if e[i] else math.inf)
    rt, rr = m.reactions(True)
    react = max([0.] + [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr])
    ert, err_r = ex.reactions()
    for exr, comp, i in ((ert, rt, 3), (err_r, rr, 2)):
        for (a, R), (_, Rm, e) in zip(exr, comp):
            ae = abs(Fr(Rm) - R)
            err = max(err, float(ae) / max(abs(float(R)), mix[i]))
            if ae > Fr(e):
                under += 1
                worst = max(worst, float(ae) / e if e else math.inf)
    pg, _ = piece_gates(c, m, scc, mixc)
    gate = max(station, react, pg['C'][0])
    return dict(gate=gate, err=err, under=under, worst=worst)


def run(c, ex, exact, fexact, prim, adj, trust=False, LUeps=None):
    rng = random.Random(zlib.crc32(c['id'].encode())) if LUeps else None
    try:
        m = RS(c, prim, adj, trust=trust, LUeps=LUeps, rng=rng)
        m.c = c
        m.adj_solves = m.adj_steps = 0
        m.indicator()
        o = measure(c, m, exact, fexact, ex)
    except (ZeroDivisionError, OverflowError, ValueError):
        return dict(status='failed')
    fin = all(math.isfinite(v) for v in m.x) and math.isfinite(o['gate'])
    o.update(steps=m.iterations, adj=m.adj_steps / max(m.adj_solves, 1), berr=m.berr, omega=m.omega,
             hist=[round(h, 4) for h in m.hist], lawn=m.lawn,
             status='delivered' if fin and o['gate'] <= TAU else 'rejected')
    if trust:
        o.update(muD=m.muD, muB=m.muB, verified=m.verified)
        if not m.verified and o['status'] == 'delivered':
            o['status'] = 'unverified'
        o['raw_gate'] = raw_gate(c, m)
    return o


def study(c, perturb):
    ex = T.Transfer(c)
    exact = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
    fexact = [[float(v) for v in r] for r in exact]
    out = dict(id=c['id'])
    for p in ('A', 'B', 'C', 'D'):
        out['P' + p] = run(c, ex, exact, fexact, p, 'A')
    for a in ('B', 'C', 'D', 'one'):
        out['Z' + a] = run(c, ex, exact, fexact, 'A', a)
    out['T'] = run(c, ex, exact, fexact, 'A', 'A', trust=True)
    out['TB'] = run(c, ex, exact, fexact, 'A', 'B', trust=True)
    out['TN'] = run(c, ex, exact, fexact, 'A', 'none', trust=True)
    if perturb:
        for eps in (1e-4, 1e-1):
            # gate_study drew 1e-8, 1e-4, 1e-1 in this order from the stream
            seq = (1e-8, 1e-4) if eps == 1e-4 else (1e-8, 1e-4, 1e-1)
            for a in ('A', 'B', 'C', 'D'):
                out['E%g%s' % (eps, a)] = run(c, ex, exact, fexact, 'A', a, LUeps=seq)
            out['E%gT' % eps] = run(c, ex, exact, fexact, 'A', 'A', trust=True, LUeps=seq)
            out['E%gTB' % eps] = run(c, ex, exact, fexact, 'A', 'B', trust=True, LUeps=seq)
            out['E%gTN' % eps] = run(c, ex, exact, fexact, 'A', 'none', trust=True, LUeps=seq)
    return out


if __name__ == '__main__':
    t0 = time.time()
    arg = sys.argv[1:]
    if arg[0] == 'neartau':
        cases = [json.loads(l)['case'] for l in open(os.path.join(HERE, '..', 'results', 'state-near-tau-models.log'))]
        name, every = 'neartau', 1
    else:
        every = int(arg[-1]) if arg[0] != 'targeted' else 1
        cases, name = models(arg[:-1] if arg[0] != 'targeted' else arg)
        if arg[0] != 'targeted':
            cases = cases[::every]
    n = 0
    for k, c in enumerate(cases):
        if is_mechanism(c):
            continue
        try:
            StateSolver(c)
        except ZeroDivisionError:
            continue
        o = study(c, perturb=(name == 'neartau' or k % 5 == 0))
        o['cls'] = name
        n += 1
        print('DATA ' + json.dumps(o), flush=True)
    print('%s: models %d | %.0f s' % (name, n, time.time() - t0))
