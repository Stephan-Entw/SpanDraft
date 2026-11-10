"""Round 5: regression of the contract solver (fpc.py) against round 4 on the random model sets.

Per model of the round-4 sets (gate_study.models, every K-th): status and cause of the contract
solver, gate, the actual error in the contract measure at the evaluation points and reactions,
values whose error exceeds their contract bound (must be 0), and the round-4 decision (status
from results/state-refine-*.log, configuration T) for comparison. No exact twin, no stage C.
Usage: python3 state/fpc_regress.py s|f r0 N K"""
import glob, json, math, os, sys, time
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
import fpc
from fpc import solve_case, umul, MINN
from fpc_chain import contract_scales
from gate_study import models
from metrics import Q

HERE = os.path.dirname(os.path.abspath(__file__))


def round4():
    out = {}
    for f in glob.glob(os.path.join(HERE, '..', 'results', 'state-refine-*.log')):
        for l in open(f):
            if l.startswith('DATA '):
                r = json.loads(l[5:])
                if 'T' in r:
                    out[r['id']] = r['T'].get('status')
    return out


def measure(c, m, gate):
    ex = T.Transfer(c)
    Sc = contract_scales(c, ex)
    viol = 0
    err = 0.
    for x, sd in c['pts']:
        vn, bn = m.evaluate(float(x), sd)
        vs, bs = m.to_si(vn, bn)
        st = ex.state(Fr(float(x)), sd)
        for q in range(4):
            e = abs(Fr(vs[q]) - st[Q[q]])
            if e > Fr(bs[q]):
                viol += 1
            D = Sc[q] if q < 2 else max(Sc[q], abs(st[Q[q]]))
            if D:
                err = max(err, float(e / D))
    ert, err_r = ex.reactions()
    exr = {('R', float(a)): R for a, R in ert}
    exr.update({('C', float(a)): R for a, R in err_r})
    for kind, a, val, bnd, q, how in m.reactions():
        Rx = exr[(kind, a)]
        vs = math.ldexp(val, m.Eq[q])
        bs = umul(bnd, 2.0 ** m.Eq[q]) + (fpc.LAM if abs(vs) <= MINN else 0.)
        e = abs(Fr(vs) - Rx)
        if e > Fr(bs):
            viol += 1
        D = max(Sc[q], abs(Rx))
        if D:
            err = max(err, float(e / D))
    return viol, err


if __name__ == '__main__':
    t0 = time.time()
    arg = sys.argv[1:]
    cases, name = models(arg[:3])
    cases = cases[::int(arg[3])]
    r4 = round4()
    for c in cases:
        o, m = solve_case(c)
        rec = dict(id=c['id'], cls=name, status=o['status'], cause=o.get('cause'), gate=o.get('gate'),
                   mu=(o.get('mu') or [None])[-1], r4=r4.get(c['id']))
        if m is not None and hasattr(m, 'E') and o['status'] not in ('UnstableModel',):
            try:
                rec['viol'], rec['err'] = measure(c, m, o.get('gate'))
            except Exception as e:
                rec['measure'] = repr(e)
        print('DATA ' + json.dumps(rec), flush=True)
    print('%s: models %d | %.0f s' % (name, len(cases), time.time() - t0))
