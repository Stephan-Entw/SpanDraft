"""Round 3, point 1: the two evaluations of an elastic restraint reaction and what their
difference can and cannot show.

For an elastic restraint at station i (translational: q = 3, spring row is the V row; rotational:
q = 2, the M row) the spring row p of the system reads, in nondimensional unscaled form,
    a_p^T x = b_p,   a_p = c_jump - c_law,
where c_jump is the jump functional (V+ - V- resp. M- - M+) and c_law the law functional
(-k w+ resp. -kt th+). Hence, for the computed solution and exact arithmetic,
    R_jump - R_law = a_p^T x - b_p = -r_p          (residual of the stored spring row)
and for the adjoints S^T z = C c of the two functionals
    z_jump - z_law = +-e_p / rs_p                  (they differ only in component p).
Consequences checked here, per elastic restraint:
 (a) identity: | D - |r_p|/rs_p | <= delta r_p/rs_p + rounding of the two evaluations
     (D = |R_jump - R_law|; no assumption on the rounding model);
 (b) D <= beta_jump + beta_law with the full d;
 (c) the same with d reduced to |r| + delta r (no data or underflow terms): if it still holds,
     the relation does not test the rounding model;
 (d) adjoint linearity: max_{m != p} |z_jump,m - z_law,m| relative to |z_jump - z_law|_p and
     rs_p |z_jump,p - z_law,p| (expected 1);
 (e) injected implementation faults: k in the law off by 2^-20 relative; the station load or the
     segment load integral missing in the jump; detected by D > beta_jump + beta_law, and by the
     identity check (a);
 (f) rules for the delivered value: jump, law, smaller beta, and the combination
     R* = (1 - alpha) R_jump + alpha R_law with alpha = z_jump,p / (z_jump,p - z_law,p), whose
     adjoint has no component p (beta* = beta of the residual of every other row);
     error against R_exact, beta, and the model decision per rule.
Usage: python3 state/spring_rule.py targeted | s|f r0 N      (DATA lines for spring_rule_summary.py)"""
import json, math, os, random, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from coverage import piece_gate
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales, Q, denom
from springs import categories
from state import StateSolver, gamma, U

TAU = 1e-8


def functional(m, i, q):
    """Jump functional of station i, component q: dict col -> coefficient, constant, |constant|, k."""
    sign = 1. if q == 3 else -1.
    c = {}
    if i < m.N:
        c[4 * i + q] = sign
    const = const_abs = 0.
    kc = 0
    if i > 0:
        sm = m.left(i)[q]
        for col, (v, _) in sm[0].items():
            c[col] = c.get(col, 0.) - sign * v
        const, const_abs, kc = -sign * sm[1], sm[2], sm[3]
    return c, const, const_abs, kc


def zvec(m, c):
    v = [0.] * m.n
    for k, ck in c.items():
        if k in m.col:
            v[m.col[k]] += ck * m.cs[m.col[k]]
    return m.adjoint_solve(v) if any(v) else [0.] * m.n


def restraint(m, a, q, k, dred):
    """Everything about one elastic restraint at a (SI values)."""
    i = m.st.index(a)
    c, const, const_abs, kc = functional(m, i, q)
    conv = 1 / m.f_V if q == 3 else 1 / m.f_M
    loads = m.pf if q == 3 else m.pm
    Pl = [v for b, v in loads if b == a]
    xs = m.x
    Rj = (sum(ck * xs[kk] for kk, ck in c.items()) + const) * conv - sum(Pl)
    absum = sum(abs(ck * xs[kk]) for kk, ck in c.items()) * conv + const_abs * conv + sum(map(abs, Pl))
    evj = gamma(kc + len(c) + len(Pl) + 3) * absum
    zj = zvec(m, c)
    bj_adj = sum(abs(z) * dd for z, dd in zip(zj, m.d)) * conv * (1 + gamma(4))
    bj = bj_adj + evj
    kk = 4 * i + (0 if q == 3 else 1)
    knd = k * (m.Lr ** 3 / m.EIr if q == 3 else m.Lr / m.EIr)
    vstate = xs[kk] * (1 / m.f_w if q == 3 else 1.)
    Rl = -k * vstate
    evl = gamma(1) * abs(Rl)
    zl = zvec(m, {kk: -knd})
    bl_adj = sum(abs(z) * dd for z, dd in zip(zl, m.d)) * conv * (1 + gamma(4))
    bl = bl_adj + evl
    # reduced d: residual part only
    bj_red = sum(abs(z) * dd for z, dd in zip(zj, dred)) * conv * (1 + gamma(4)) + evj
    bl_red = sum(abs(z) * dd for z, dd in zip(zl, dred)) * conv * (1 + gamma(4)) + evl
    p = m.rowidx[(i, q)]
    dz = [x - y for x, y in zip(zj, zl)]
    off = max([abs(v) for j, v in enumerate(dz) if j != p] + [0.])
    lin_off = off / abs(dz[p]) if dz[p] else math.inf
    lin_p = abs(dz[p]) * m.rs[p]
    D = abs(Rj - Rl)
    rrow = abs(m.res[p]) / m.rs[p] * conv
    ident_bound = m.reserr[p] / m.rs[p] * conv + gamma(len(c) + len(Pl) + 6) * (absum + abs(Rl))
    alpha = zj[p] / (zj[p] - zl[p]) if zj[p] != zl[p] else 0.
    zs = [(1 - alpha) * x + alpha * y for x, y in zip(zj, zl)]
    Rs = (1 - alpha) * Rj + alpha * Rl
    bs = sum(abs(z) * dd for z, dd in zip(zs, m.d)) * conv * (1 + gamma(4)) + abs(1 - alpha) * evj + abs(alpha) * evl \
        + gamma(3) * (abs(1 - alpha) * abs(Rj) + abs(alpha) * abs(Rl))
    # injected faults
    f_k = abs(Rj - Rl * (1 + 2. ** -20))
    f_P = abs(Rj + sum(Pl) - Rl) if any(Pl) else None
    f_I = abs(Rj - const * conv - Rl) if const else None
    big = bj + bl
    idf = lambda Dbug: abs(Dbug - rrow) > ident_bound          # fault seen by the identity check
    zmax = max(max(map(abs, zj)), max(map(abs, zl)))
    return dict(id_k=idf(f_k), id_P=None if f_P is None else idf(f_P), id_I=None if f_I is None else idf(f_I),
                lin_off_u=off / (U * zmax) if zmax else 0.,Rj=Rj, Rl=Rl, Rs=Rs, bj=bj, bl=bl, bs=bs, D=D, rrow=rrow, ident=abs(D - rrow) <= ident_bound,
                ident_ratio=abs(D - rrow) / ident_bound if ident_bound else (0. if D == rrow else math.inf),
                Dbeta=D / big if big else (0. if D == 0 else math.inf),
                Dbeta_red=D / (bj_red + bl_red) if bj_red + bl_red else (0. if D == 0 else math.inf),
                lin_off=lin_off, lin_p=lin_p, alpha=alpha,
                det_k=f_k > big, det_P=None if f_P is None else f_P > big, det_I=None if f_I is None else f_I > big)


def study(c, ex):
    m = StateSolver(c)
    m.indicator()
    dred = [abs(m.res[i]) + m.reserr[i] for i in range(m.n)]
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    vals = [g[0] for g in got]
    scc, mixc = scales(c, vals)
    fexact = [[float(ex.state(Fr(float(x)), sd)[k]) for k in Q] for x, sd in c['pts']]
    sc, mix = scales(c, fexact)
    field = max(e[i] / denom(i, v[i], scc, mixc) for v, e in got for i in range(4))
    field = max(field, piece_gate(c, m, scc, mixc))
    field_err = max(float(abs(Fr(v[i]) - Fr(fe[i]))) / denom(i, fe[i], sc, mix) for (v, _), fe in zip(got, fexact) for i in range(4))
    rt, rr = ex.reactions()
    rigid_gate, rigid_err, recs = 0., 0., []
    for exact_list, items, q in ((rt, m.tr, 3), (rr, m.ro, 2)):
        for (a, Rex), (x, k) in zip(exact_list, items):
            fl, flc = (mix[3], mixc[3]) if q == 3 else (mix[2], mixc[2])
            if k == math.inf:
                v = m.reaction_variants(x, q, k)
                rigid_gate = max(rigid_gate, v['beta_jump'] / max(abs(v['jump']), flc))
                rigid_err = max(rigid_err, float(abs(Fr(v['jump']) - Rex)) / max(abs(float(Rex)), fl))
                continue
            r = restraint(m, x, q, k, dred)
            den = max(abs(float(Rex)), fl)
            out = dict(cats=sorted(categories(c, x, k, q == 2)), q=q, D=r['D'] / den, Dbeta=r['Dbeta'], Dbeta_red=r['Dbeta_red'],
                       id_k=r['id_k'], id_P=r['id_P'], id_I=r['id_I'], lin_off_u=r['lin_off_u'],
                       ident=r['ident'], ident_ratio=r['ident_ratio'], lin_off=r['lin_off'], lin_p=r['lin_p'], alpha=r['alpha'],
                       det_k=r['det_k'], det_P=r['det_P'], det_I=r['det_I'])
            if q == 3 and any(s[0] == x and s[1] != 'inf' and s[2] not in (0, 'inf') for s in c['springs']):
                out['cats'].append('combined')
            for nm, R, b in (('jump', r['Rj'], r['bj']), ('law', r['Rl'], r['bl']), ('star', r['Rs'], r['bs'])):
                out['e_' + nm] = float(abs(Fr(R) - Rex)) / den
                out['b_' + nm] = b / max(abs(R), flc)
            ch = 'jump' if r['bj'] <= r['bl'] else 'law'
            out['e_min'], out['b_min'] = out['e_' + ch], out['b_' + ch]
            recs.append(out)
    gates = {}
    for rule in ('jump', 'law', 'min', 'star'):
        gates[rule] = max([field, rigid_gate] + [r['b_' + rule] for r in recs])
    errs = {rule: max([field_err, rigid_err] + [r['e_' + rule] for r in recs]) for rule in gates}
    return dict(id=c['id'], gates=gates, errs=errs, recs=recs)


def models(arg):
    if arg[0] == 'targeted':
        return [c for _, c in all_cases() if c['springs']], 'targeted'
    kind, r0, N = arg[0], float(arg[1]), int(arg[2])
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    return [(gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}') for k in range(N)], '%s L/%.0e' % (kind, 1 / r0)


if __name__ == '__main__':
    t0 = time.time()
    cases, name = models(sys.argv[1:])
    n = sing = 0
    for c in cases:
        if not c['springs'] or is_mechanism(c):
            continue
        ex = T.Transfer(c)
        try:
            o = study(c, ex)
        except ZeroDivisionError:
            sing += 1
            continue
        n += 1
        o['cls'] = name
        print('DATA ' + json.dumps(o))
    print('%s: models %d, numerically singular %d | %.0f s' % (name, n, sing, time.time() - t0))
