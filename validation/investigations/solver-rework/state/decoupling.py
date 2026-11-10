"""Point 3: the input sensitivity is diagnosis only. For the same models, solving with and without
a preceding 1-ulp sensitivity evaluation must give bit-identical solution, indicator, reactions and
status; the sensitivity evaluation must not modify the model.
Models: every model rejected or numerically singular in the state-rand runs, plus random models.
Usage: python3 state/decoupling.py"""
import json, os, random, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from coverage import piece_gate
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import scales
from sens import sensitivity
from state import StateSolver

TAU = 1e-8
HERE = os.path.dirname(os.path.abspath(__file__))


def regenerate(kind, r0, index):
    seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
    rng = random.Random(seed)
    for k in range(index + 1):
        c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
    return c


def run(c):
    """Everything the solver decides, from computed values only."""
    try:
        m = StateSolver(c)
    except ZeroDivisionError:
        return ('singular',)
    m.indicator()
    got, ind = [], []
    for x, sd in c['pts']:
        v, e = m.state(float(x), sd, True)
        got.append(v); ind.append(e)
    scc, mixc = scales(c, got)
    norm = [e[i] / (max(abs(g[i]), mixc[i]) if i >= 2 else scc[i]) for g, e in zip(got, ind) for i in range(4)]
    rt, rr = m.reactions(True)
    norm += [e / max(abs(R), mixc[3]) for _, R, e in rt] + [e / max(abs(R), mixc[2]) for _, R, e in rr]
    gate = max(norm + [piece_gate(c, m, scc, mixc)])
    status = 'delivered' if gate <= TAU else 'rejected'
    return (status, repr(gate), repr(m.x), repr(m.e), repr(rt), repr(rr), repr(got), repr(ind))


if __name__ == '__main__':
    ids = []
    for f in sorted(os.listdir(os.path.join(HERE, '..', 'results'))):
        if f.startswith('state-rand-'):
            for line in open(os.path.join(HERE, '..', 'results', f)):
                mm = re.match(r'\s+(REJ|SING) ([sf])\|([0-9e.+-]+)\|(\d+)', line)
                if mm:
                    ids.append((mm.group(2), float(mm.group(3)), int(mm.group(4))))
    cases = [regenerate(*i) for i in ids]
    rng = random.Random(31)
    for k in range(200):
        r0 = [1e-2, 1e-6, 1e-9, 1e-11][k % 4]
        cases.append((gen_s if k % 2 else gen_f)(rng, r0, f'dec|{k}'))
    n = same = unchanged = 0
    statuses = {}
    for c in cases:
        if is_mechanism(c):
            continue
        before = json.dumps(c, sort_keys=True)
        a = run(c)
        sensitivity(c)
        mid = json.dumps(c, sort_keys=True)
        b = run(c)
        n += 1
        same += a == b
        unchanged += before == mid
        statuses[a[0]] = statuses.get(a[0], 0) + 1
    print('models %d (of them from rejected/singular lists: %d) | status %s' % (n, len(ids), statuses))
    print('bit-identical solution, indicator, reactions and status with and without sensitivity: %d of %d' % (same, n))
    print('model unchanged by the sensitivity evaluation: %d of %d' % (unchanged, n))
