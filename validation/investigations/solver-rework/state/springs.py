"""Point 4: two evaluations of a spring reaction.

R_jump = V+ - V- - P (couple: M- - M+ - C) with its adjoint indicator, R_law = -k w+ (-kt th+)
with its indicator, R_exact from transfer_ref. Both are equal in exact arithmetic for the exact
solution; for the computed solution their difference is exactly the residual of the station's
spring row (V+ - V- + k w+ - P), up to the rounding of the two evaluations. A consistency
diagnosis therefore compares D = |R_jump - R_law| with that row's residual scale u * rowscale,
where rowscale = |V+| + |V-| + |P| + |k w+|.
Categories (a restraint can be in several): soft (k L^3/EI or kt L/EI < 1e-2), stiff (> 1e6),
close springs (another spring within 1e-4 L), between close rigid supports (rigid translational
restraints on both sides within 1e-4 L), springs only (no rigid support), rotational.
Usage: python3 state/springs.py"""
import math, os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales, Q
from state import StateSolver, U

CATS = ('soft', 'stiff', 'close springs', 'between close supports', 'springs only', 'rotational', 'all')


def categories(c, x, k, rot):
    L = c['L']
    EI = min([c['E'] * c['I']] + [v for _, v in c.get('ei', [])])
    dimless = k * L / EI if rot else k * L ** 3 / EI
    out = {'all'}
    if dimless < 1e-2:
        out.add('soft')
    if dimless > 1e6:
        out.add('stiff')
    if any(s[0] != x and abs(s[0] - x) <= 1e-4 * L for s in c['springs']):
        out.add('close springs')
    rig = [a for a, _ in c['sup']] + [s[0] for s in c['springs'] if s[1] == 'inf']
    if any(a < x and x - a <= 1e-4 * L for a in rig) and any(a > x and a - x <= 1e-4 * L for a in rig):
        out.add('between close supports')
    if not c['sup']:
        out.add('springs only')
    if rot:
        out.add('rotational')
    return out


def delivered(c, m):
    """Station/reaction gate from computed values only (the piece gate changes no decision, see
    state-acceptance-summary)."""
    got, ind = [], []
    for x, sd in c['pts']:
        v, e = m.state(float(x), sd, True)
        got.append(v); ind.append(e)
    scc, mixc = scales(c, got)
    g = max(e[i] / (max(abs(v[i]), mixc[i]) if i >= 2 else scc[i]) for v, e in zip(got, ind) for i in range(4))
    rt, rr = m.reactions(True)
    g = max([g] + [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr])
    return g <= 1e-8


def records(c):
    ex = T.Transfer(c)
    m = StateSolver(c)
    m.indicator()
    deliv = delivered(c, m)
    got = [m.state(float(x), sd) for x, sd in c['pts']]
    fexact = [[float(ex.state(Fr(float(x)), sd)[k]) for k in Q] for x, sd in c['pts']]
    sc, mix = scales(c, fexact)
    rt, rr = ex.reactions()
    out = []
    for exact_list, items, q, rot in ((rt, m.tr, 3, False), (rr, m.ro, 2, True)):
        for (a, Rex), (x, k) in zip(exact_list, items):
            if k == math.inf:
                continue
            v = m.reaction_variants(x, q, k)
            floor = mix[3] if q == 3 else mix[2]
            den = max(abs(float(Rex)), floor)
            ej = float(abs(Fr(v['jump']) - Rex)) / den
            el = float(abs(Fr(v['law']) - Rex)) / den
            D = abs(v['jump'] - v['law'])
            out.append(dict(cats=categories(c, x, k, rot), ej=ej, el=el, bj=v['beta_jump'] / den, bl=v['beta_law'] / den,
                            chosen='jump' if v['beta_jump'] <= v['beta_law'] else 'law',
                            D=D / den, Dbeta=D / (v['beta_jump'] + v['beta_law']) if v['beta_jump'] + v['beta_law'] else 0.,
                            Drow=D / (U * v['rowscale']) if v['rowscale'] else (0. if D == 0 else math.inf),
                            delivered=deliv, id=c['id']))
    return out


def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * len(v)))] if v else float('nan')


def report(recs):
    print('errors relative to max(|R|, S); "chosen" = evaluation with the smaller indicator')
    print('%-24s %6s | %-27s | %-27s | %-27s | %s' % ('category', 'n', 'jump error  median / max', 'law error  median / max',
                                                    'chosen error  median / max', 'chosen worse than other: n, largest ratio'))
    for cat in CATS:
        rs = [r for r in recs if cat in r['cats']]
        if not rs:
            continue
        ch = [r['ej'] if r['chosen'] == 'jump' else r['el'] for r in rs]
        ot = [r['el'] if r['chosen'] == 'jump' else r['ej'] for r in rs]
        worse = [(a, b) for a, b in zip(ch, ot) if a > b]
        big = max([a / b if b > 0 else math.inf for a, b in worse if a > 1e-15], default=1.)
        print('%-24s %6d | %.0e / %.0e %13s | %.0e / %.0e %13s | %.0e / %.0e %13s | %d, %.2f (only errors > 1e-15)' % (
            cat, len(rs), q([r['ej'] for r in rs], .5), max(r['ej'] for r in rs), '', q([r['el'] for r in rs], .5),
            max(r['el'] for r in rs), '', q(ch, .5), max(ch), '', len(worse), big))
    print('consistency diagnosis D = |R_jump - R_law| (relative to max(|R|, S)):')
    print('%-24s | %-30s | %-30s | %-26s' % ('category', 'D  median / max', 'D/(beta_jump+beta_law) max', 'D/(u*rowscale) p99 / max'))
    for cat in CATS:
        rs = [r for r in recs if cat in r['cats']]
        if not rs:
            continue
        print('%-24s | %.0e / %.0e %17s | %.2e %20s | %.1f / %.1f' % (cat, q([r['D'] for r in rs], .5), max(r['D'] for r in rs), '',
                                                                   max(r['Dbeta'] for r in rs), '', q([r['Drow'] for r in rs], .99),
                                                                   max(r['Drow'] for r in rs)))
    big = sorted(recs, key=lambda r: -r['Drow'])[:5]
    for r in big:
        print('  largest D/(u*rowscale): %-20s %.1f  D %.1e  jump error %.1e  law error %.1e' % (r['id'], r['Drow'], r['D'], r['ej'], r['el']))
    allb = [r for r in recs if r['D'] > r['bj'] + r['bl']]
    print('restraints with D > beta_jump + beta_law (normalised): %d' % len(allb))


if __name__ == '__main__':
    recs = []
    cases = [c for _, c in all_cases() if c['springs']]
    for kind, r0s, N in (('s', (1e-2, 1e-4, 1e-6, 1e-9, 1e-11), 1500), ('f', (1e-2, 1e-6, 1e-11), 1000)):
        for r0 in r0s:
            seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
            rng = random.Random(seed)
            cases += [(gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}') for k in range(N)]
    nmod = 0
    for c in cases:
        if not c['springs'] or is_mechanism(c):
            continue
        try:
            recs += records(c)
            nmod += 1
        except ZeroDivisionError:
            pass
    print('models %d, elastic restraints %d (in delivered models %d)' % (nmod, len(recs), sum(r['delivered'] for r in recs)))
    allrecs = recs
    for label, recs in (('delivered models', [r for r in allrecs if r['delivered']]), ('rejected models', [r for r in allrecs if not r['delivered']])):
        print('=== %s: %d elastic restraints' % (label, len(recs)))
        report(recs)
