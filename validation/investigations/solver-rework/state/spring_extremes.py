"""Round 3, point 1 follow-up: is a fixed rule (always the jump, always the spring law) as good as
the smaller indicator, also outside the reference set? Targeted extremes where one of the two
evaluations is expected to be sensitive to the residual of the spring row:
- stiff spring next to / between close rigid supports with a large shear force passing,
- soft spring between close rigid supports (law better),
- springs only, very stiff, close pair,
- rotational spring at a clamped support neighbour.
Stiffness k L^3/EI = 10^-6 ... 10^15 (rotational kt L/EI likewise), distances L/10^3 ... L/10^11.
Per case: gate of the model with each rule (jump / law / min) and the actual reaction errors.
Usage: python3 state/spring_extremes.py"""
import math, os, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from gen import case
from spring_rule import study

L, E, I = 4., 210e9, 8e-6
EI = E * I
TAU = 1e-8


def configs(g, k, kt):
    m = L / 2
    return dict(
        steif_zwischen_lagern=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (m + 2 * g, 'Roller'), (L, 'Roller')], springs=[(m + g, k, 0, False)],
                                   pm=[(m - 0.1, 5e4)]),
        steif_neben_lager=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (L, 'Roller')], springs=[(m + g, k, 0, False)], pm=[(m + 2 * g, 5e4)]),
        weich_zwischen_lagern=dict(sup=[(0, 'Pinned'), (m, 'Roller'), (m + 2 * g, 'Roller'), (L, 'Roller')], springs=[(m + g, k * 1e-12, 0, False)]),
        nur_federn_paar=dict(sup=[], springs=[(0, k, 0, True), (m, k, 0, False), (m + g, k, 0, False), (L, k, 0, False)]),
        dreh_neben_einspannung=dict(sup=[(0, 'Pinned'), (L, 'Fixed')], springs=[(L - g, 'inf', kt, False)]),
        kombi_zwischen_lagern=dict(sup=[(0, 'Fixed'), (m, 'Roller'), (m + 2 * g, 'Roller'), (L, 'Fixed')], springs=[(m + g, k, kt, False)]),
    )


if __name__ == '__main__':
    worst = {r: (0., None) for r in ('jump', 'law', 'min')}
    rej = {r: [] for r in ('jump', 'law', 'min')}
    n = 0
    for r in (1e-3, 1e-7, 1e-9, 1e-11):
        g = r * L
        for u in (-6, 0, 6, 9, 12, 15):
            k, kt = 10. ** u * EI / L ** 3, 10. ** u * EI / L
            for nm, t in configs(g, k, kt).items():
                c = case(f'{nm}|{r}|{u}', L, t['sup'], springs=t.get('springs', []), pf=[(.3 * L, -1e4), (.8 * L, -1e4)],
                         pm=t.get('pm', []), q=[(0., L, -2e3)], E=E, I=I)
                try:
                    o = study(c, T.Transfer(c))
                except ZeroDivisionError:
                    print('%-34s numerically singular' % c['id'])
                    continue
                n += 1
                for rule in worst:
                    if o['gates'][rule] > TAU:
                        rej[rule].append((c['id'], o['gates'][rule], o['errs'][rule]))
                    e = max([rr['e_' + rule] for rr in o['recs']], default=0.)
                    if e > worst[rule][0]:
                        worst[rule] = (e, c['id'])
                print('%-34s gate jump %.1e law %.1e min %.1e | reaction error jump %.1e law %.1e min %.1e' % (
                    c['id'], o['gates']['jump'], o['gates']['law'], o['gates']['min'],
                    *(max([rr['e_' + rule] for rr in o['recs']], default=0.) for rule in ('jump', 'law', 'min'))))
    print('models %d' % n)
    for rule in worst:
        print('rule %-4s rejected %3d (actual error <= tau: %d) | largest reaction error %.1e (%s)' % (
            rule, len(rej[rule]), sum(e <= TAU for _, _, e in rej[rule]), worst[rule][0], worst[rule][1]))
    only = [x for x in rej['law'] if x[0] not in {y[0] for y in rej['min']}]
    for i, g_, e in only[:10]:
        print('  rejected only with the law rule: %-34s gate %.1e error %.1e' % (i, g_, e))
    only = [x for x in rej['jump'] if x[0] not in {y[0] for y in rej['min']}]
    for i, g_, e in only[:10]:
        print('  rejected only with the jump rule: %-34s gate %.1e error %.1e' % (i, g_, e))
