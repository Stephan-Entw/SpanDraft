"""Round 3, point 3/4 follow-up: with LU factors perturbed by 10 % (gate_study.py, eps = 0.1) some
delivered models had an actual error above the gate (beta underestimated; errors still <= tau).
The primary solution is refined against the true system, so its residual is measured; what is
inaccurate is the adjoint z computed with the perturbed factors. This repeats exactly those
perturbations (same random stream) for every model that gate_study delivered at eps = 0.1 and
compares the adjoint refinement rules 'one', 'adaptive' and 'beta'.
Usage: python3 state/perturb_check.py        (reads results/state-gate-*.log)"""
import glob, json, math, os, random, sys, zlib
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from gate_study import resolve, evaluate, reaction_error, models
from metrics import scales, Q
from state import StateSolver

TAU = 1e-8
HERE = os.path.dirname(os.path.abspath(__file__))
sel = {}
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-gate-*.log'))):
    if f.endswith('summary.log'):
        continue
    for line in open(f):
        if line.startswith('DATA '):
            m = json.loads(line[5:])
            p = m.get('perturb', {}).get('0.1')
            if p and p['finite'] and p['gate'] is not None and p['gate'] <= TAU:
                sel.setdefault(m['cls'], set()).add(m['id'])
args = {'targeted': ['targeted']}
for kind, N in (('s', 1500), ('f', 1000)):
    for r0 in ('1e-2', '1e-4', '1e-6', '1e-9', '1e-11'):
        args['%s L/%.0e' % (kind, 1 / float(r0))] = [kind, r0, str(N)]
tot = {r: dict(n=0, under=0, wrong=0, rejected=0) for r in ('one', 'adaptive', 'beta')}
for cls, ids in sorted(sel.items()):
    cases, _ = models(args[cls])
    for c in cases:
        if c['id'] not in ids:
            continue
        ex = T.Transfer(c)
        exact = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
        fexact = [[float(v) for v in r] for r in exact]
        sc, mix = scales(c, fexact)
        line = []
        for rule in ('one', 'adaptive', 'beta'):
            rng = random.Random(zlib.crc32(c['id'].encode()))
            for eps in (1e-8, 1e-4, 1e-1):          # same stream as gate_study
                m = StateSolver(c, adj_refine=rule)
                LUp = [[v * (1 + eps * rng.uniform(-1, 1)) for v in row] for row in m.LU]
            resolve(m, LUp)
            m.adj_steps = 0
            m.adj_solves = 0
            m.indicator()
            o = evaluate(c, m, exact, fexact)
            err = max(o['err'], reaction_error(c, m, ex, mix))
            gate = max(o['station'], o['react'], o['C'])
            t = tot[rule]
            t['n'] += 1
            t['under'] += err > gate
            t['wrong'] += gate <= TAU < err
            t['rejected'] += gate > TAU
            line.append('%s gate %.2e steps/adjoint %.2f' % (rule, gate, m.adj_steps / m.adj_solves))
        print('%-22s error %.2e | %s' % (c['id'], err, ' | '.join(line)))
for rule, t in tot.items():
    print('%-8s models %d | error > gate %d | delivered with error > tau %d | rejected %d' % (rule, t['n'], t['under'], t['wrong'], t['rejected']))
