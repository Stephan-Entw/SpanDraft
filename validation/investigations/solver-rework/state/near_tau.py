"""Round 4 (O13): targeted models whose gate lies near tau.

Every random model of the earlier gate runs with gate >= 1e-11 is regenerated with a continuously
varied distance parameter (same random stream, see family()) on a log grid; between two grid points whose gates bracket a target value
(1e-10, 1e-9, 3e-9, 1e-8, 3e-8) the parameter is bisected in log space. Only the float solver is
used for the search (gate = station, reaction and piece gate C, adjoint rule 'beta'); the
exact comparison happens in refine_study.py. Writes the models as JSON lines.
Usage: python3 state/near_tau.py > results/state-near-tau-models.log"""
import json, math, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from gen import gen_s, gen_f
from gate_study import piece_gates
from kinematics import is_mechanism
from metrics import scales, denom
from state import StateSolver

TARGETS = (1e-10, 1e-9, 3e-9, 1e-8, 3e-8)
# sources: random models of the state-gate runs with gate >= 1e-11 (results/state-gate-*.log)
SOURCES = ['f|1e-06|885', 's|1e-11|24', 'f|0.0001|398', 's|1e-06|141', 'f|1e-06|641', 'f|0.0001|185', 's|1e-09|56',
           's|1e-06|1457', 's|1e-09|58', 'f|1e-06|329', 's|1e-11|1267', 's|1e-06|986', 's|1e-09|867', 's|1e-09|1222',
           's|1e-09|1359', 's|1e-09|1181', 's|1e-09|978', 'f|1e-09|613', 's|1e-09|267', 's|1e-11|460']


def family(src, e):
    """Model `index` of the random class `kind|r0`, regenerated with the distance parameter 10^e
    instead of r0. The random stream is seeded as for r0 and the draws do not depend on the
    distance, so the family moves the close pairs of the same model continuously."""
    kind, r0, index = src.split('|')
    r0, index = float(r0), int(index)
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    for k in range(index + 1):
        c = (gen_s if kind == 's' else gen_f)(rng, 10. ** e, 'neartau|%s|%.6f' % (src, e))
    return c


def gate(c):
    if is_mechanism(c):
        return None
    try:
        m = StateSolver(c, adj_refine='beta')
    except ZeroDivisionError:
        return None
    m.indicator()
    got = [m.state(float(x), sd, True) for x, sd in c['pts']]
    scc, mixc = scales(c, [g[0] for g in got])
    station = max(e[i] / denom(i, v[i], scc, mixc) for v, e in got for i in range(4))
    rt, rr = m.reactions(True)
    react = max([0.] + [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr])
    pg, _ = piece_gates(c, m, scc, mixc)
    G = max(station, react, pg['C'][0])
    return G if math.isfinite(G) else None


if __name__ == '__main__':
    found = []
    for s in SOURCES:
        grid = [-12 + 0.5 * i for i in range(19)]          # log10 of the distance parameter, 1e-12 .. 1e-3
        vals = [(e, gate(family(s, e))) for e in grid]
        print('# %s: %s' % (s, ' '.join('%.0e' % g if g is not None else '-' for _, g in vals)), file=sys.stderr, flush=True)
        for (e0, g0), (e1, g1) in zip(vals, vals[1:]):
            if g0 is None or g1 is None:
                continue
            for tg in TARGETS:
                if (g0 - tg) * (g1 - tg) > 0:
                    continue
                a, b, ga = e0, e1, g0
                for _ in range(12):
                    mid = (a + b) / 2
                    gm = gate(family(s, mid))
                    if gm is None:
                        break
                    if (ga - tg) * (gm - tg) <= 0:
                        b = mid
                    else:
                        a, ga = mid, gm
                c = family(s, a)
                G = gate(c)
                if G is not None:
                    found.append(G)
                    print(json.dumps(dict(case=c, gate=G, target=tg)), flush=True)
    print('# models %d, gates in (1e-10, 1e-8]: %d' % (len(found), sum(1e-10 < g <= 1e-8 for g in found)), file=sys.stderr)
