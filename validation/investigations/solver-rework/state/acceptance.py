"""Point 2: adjoint refinement and the acceptance factor s.

tau  = required accuracy of the actual result (contract, 1e-8),
beta = run-time a-posteriori estimate (indicator), no proven bound,
s    = acceptance factor: a model is delivered if s * gate <= tau, where gate is the largest
       normalised indicator over stations, reactions and the piece gate of coverage.py.
Per model and adjoint variant (none / one step / adaptive): gate and the actual largest error
(exact reference). Per value: beta/err histograms by quantity (0.25-decade bins).
Usage: python3 state/acceptance.py targeted | s|f r0 N"""
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
from state import StateSolver

TAU = 1e-8
FACTORS = (1, 2, 4, 10)
VARIANTS = ('none', 'one', 'adaptive')
QN = ('w', 't', 'M', 'V', 'R', 'Rm')


def evaluate(c, ex, exact, adj):
    m = StateSolver(c, adj_refine=adj)
    m.indicator()
    got, ind = [], []
    for x, sd in c['pts']:
        v, e = m.state(float(x), sd, True)
        got.append(v); ind.append(e)
    fexact = [[float(v) for v in r] for r in exact]
    sc, mix = scales(c, fexact)
    scc, mixc = scales(c, got)
    values = []                                   # (quantity, err_norm, beta_norm, err_abs, beta_abs)
    for r, fr, g, e in zip(exact, fexact, got, ind):
        for i in range(4):
            err = float(abs(Fr(g[i]) - r[i]))
            values.append((QN[i], err / denom(i, fr[i], sc, mix), e[i] / denom(i, g[i], scc, mixc), err, e[i]))
    rt, rr = ex.reactions()
    mt, mr = m.reactions(True)
    for ex_r, comp, i, qn in ((rt, mt, 3, 'R'), (rr, mr, 2, 'Rm')):
        for (a, R), (_, Rm, e) in zip(ex_r, comp):
            err = float(abs(Fr(Rm) - R))
            values.append((qn, err / max(abs(float(R)), mix[i]), e / max(abs(Rm), mixc[i]), err, e))
    gate = max([v[2] for v in values] + [piece_gate(c, m, scc, mixc)])
    return dict(gate=gate, err=max(v[1] for v in values), values=values)


def models(arg):
    if arg[0] == 'targeted':
        return [c for _, c in all_cases()], 'targeted'
    kind, r0, N = arg[0], float(arg[1]), int(arg[2])
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    return [(gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}') for k in range(N)], '%s L/%.0e' % (kind, 1 / r0)


if __name__ == '__main__':
    t0 = time.time()
    cases, name = models(sys.argv[1:])
    data = {v: dict(models=0, singular=0, gates=[], errs=[], viol=[], hist={}) for v in VARIANTS}
    for c in cases:
        if is_mechanism(c):
            continue
        ex = T.Transfer(c)
        exact = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
        for adj in VARIANTS:
            d = data[adj]
            d['models'] += 1
            try:
                o = evaluate(c, ex, exact, adj)
            except ZeroDivisionError:
                d['singular'] += 1
                continue
            d['gates'].append(o['gate']); d['errs'].append(o['err'])
            for qn, en, bn, ea, ba in o['values']:
                if ea > ba:
                    d['viol'].append((c['id'], qn, ea / ba if ba else math.inf, en, o['gate']))
                if ea > 0:
                    b = math.floor(4 * math.log10(ba / ea)) if ba > 0 else -999
                    h = d['hist'].setdefault(qn, {})
                    h[b] = h.get(b, 0) + 1
    print('%s | %.0f s' % (name, time.time() - t0))
    for adj in VARIANTS:
        d = data[adj]
        acc_viol = [v for v in d['viol'] if v[4] <= TAU]
        print('  adjoint refinement %-8s models %d singular %d | values err>beta: %d (in models with gate <= tau: %d, largest err/beta %.2f, largest normalised err %.1e)' % (
            adj, d['models'], d['singular'], len(d['viol']), len(acc_viol),
            max([v[2] for v in acc_viol], default=0.), max([v[3] for v in acc_viol], default=0.)))
        for v in acc_viol:
            print('     %-18s %-2s err/beta %.2f normalised err %.1e' % v[:4])
        rows = {}
        for s_ in FACTORS:
            deliv = [(g, e) for g, e in zip(d['gates'], d['errs']) if s_ * g <= TAU]
            rej = [(g, e) for g, e in zip(d['gates'], d['errs']) if s_ * g > TAU]
            rows[s_] = dict(delivered=len(deliv), rejected=len(rej) + d['singular'],
                            max_err=max([e for _, e in deliv], default=0.),
                            wrong=sum(e > TAU for _, e in deliv), unnecessary=sum(e <= TAU for _, e in rej))
            print('     s=%-2d delivered %5d rejected %3d | max actual error delivered %.1e | delivered with error > tau %d | rejected with error <= tau %d' % (
                s_, rows[s_]['delivered'], rows[s_]['rejected'], rows[s_]['max_err'], rows[s_]['wrong'], rows[s_]['unnecessary']))
        print('DATA ' + json.dumps(dict(cls=name, adj=adj, rows=rows, hist=d['hist'],
                                        viol=[v for v in d['viol'] if v[4] <= TAU])))
