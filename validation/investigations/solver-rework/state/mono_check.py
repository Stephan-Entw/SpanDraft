"""Round 3, point 2 follow-up: where do the strict (tolerance-free) decreases of beta along a piece
occur? Re-evaluates every model that gate_study.py reported with a decrease and classifies each
decreasing step: first step from the closed left end x0 with a distributed load ending at x0, or
elsewhere; and its relative size.
Usage: python3 state/mono_check.py        (reads results/state-gate-*.log)"""
import glob, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from coverage import events, piece_points
from gate_study import models
from state import StateSolver

HERE = os.path.dirname(os.path.abspath(__file__))
want = {}
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-gate-*.log'))):
    if f.endswith('summary.log'):
        continue
    for line in open(f):
        if line.startswith('DATA '):
            m = json.loads(line[5:])
            if m['one']['mono'] > 0:
                want.setdefault(m['cls'], set()).add(m['id'])
args = {'targeted': ['targeted']}
for kind, N in (('s', 1500), ('f', 1000)):
    for r0 in ('1e-2', '1e-4', '1e-6', '1e-9', '1e-11'):
        args['%s L/%.0e' % (kind, 1 / float(r0))] = [kind, r0, str(N)]
n_left = n_other = 0
worst = 0.
for cls, ids in sorted(want.items()):
    cases, _ = models(args[cls])
    for c in cases:
        if c['id'] not in ids:
            continue
        m = StateSolver(c, adj_refine='one')
        m.indicator()
        ends = {b for a, b, _ in c['q']} | {b for a, b, *_ in c.get('lin', [])}
        ev = events(c)
        for x0, x1 in zip(ev, ev[1:]):
            xs = [(x0, 'R')] + [(x, 'R') for x in piece_points(x0, x1)] + [(x1, 'L')]
            prev = None
            for k, (x, sd) in enumerate(xs):
                _, b = m.state(x, sd, True)
                if prev is not None:
                    for i in range(4):
                        if b[i] < prev[i]:
                            rel = (prev[i] - b[i]) / prev[i]
                            worst = max(worst, rel)
                            if k == 1 and x0 in ends:
                                n_left += 1
                            else:
                                n_other += 1
                                print('  elsewhere: %-20s q %d x %.17g step %d rel %.2e' % (c['id'], i, x, k, rel))
                prev = b
print('decreasing steps: at the closed left end where a distributed load ends %d, elsewhere %d; largest relative decrease %.2e (u = %.2e)' % (
    n_left, n_other, worst, 2. ** -53))
