"""Ablation: which parts of the formulation and the solution process are necessary.
Usage: python3 state/ablation.py"""
import os, random, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from cases import all_cases
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import compare

VARIANTS = [
    ('target (eliminate, adaptive, double residual)', {}),
    ('refine none', dict(refine='none')),
    ('refine one step', dict(refine='one')),
    ('adaptive, compensated residual', dict(residual='compensated')),
    ('adaptive, exact residual', dict(residual='exact')),
    ('one step, compensated residual', dict(refine='one', residual='compensated')),
    ('no nondimensionalisation', dict(nondim=False)),
    ('no equilibration', dict(equilibrate=False)),
    ('neither nondim nor equilibration', dict(nondim=False, equilibrate=False)),
    ('prescribed as local rows', dict(prescribed='rows-local')),
    ('prescribed on propagated expression', dict(prescribed='rows-propagated')),
    ('reactions as unknowns (naive)', dict(reactions='unknown')),
]
NAMES = {'paar', 'acht', 'drehacht', 'lagerdrei', 'lagerfuenf', 'gelenkcluster', 'eiimcluster', 'kurzlastvz', 'federaneinsp',
         'gemischt', 'federgelenkfeder'}


def models():
    out = [c for _, c in all_cases(distances=(1e-5, 1e-9, 1e-11), exponents=(0, 6, 12), names=NAMES)]
    for kind, r0, n in (('s', 1e-9, 300), ('f', 1e-6, 300), ('f', 1e-11, 300)):
        seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
        rng = random.Random(seed)
        out += [(gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}') for k in range(n)]
    return [c for c in out if not is_mechanism(c)]


def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * len(v)))] if v else float('nan')


if __name__ == '__main__':
    TAU = 1e-8
    t0 = time.time()
    ms = models()
    accepted = set()
    for i, c in enumerate(ms):          # models the target formulation delivers (indicator <= tau)
        try:
            o, _ = compare(c)
            if o['ind'] <= TAU:
                accepted.add(i)
        except ZeroDivisionError:
            pass
    print('models (no mechanism): %d, delivered by the target formulation: %d' % (len(ms), len(accepted)))
    print('errors in the mixed measure on the delivered models; "rej" = indicator > tau or zero pivot among them')
    print('%-40s %9s %9s %9s %6s %5s %6s' % ('variant', 'median', 'p99', 'max', 'rej', 'exc', 'steps'))
    for name, opt in VARIANTS:
        errs, rej, exc, steps = [], 0, 0, []
        for i in sorted(accepted):
            try:
                o, _ = compare(ms[i], **opt)
            except ZeroDivisionError:
                rej += 1
                continue
            except Exception:
                exc += 1
                continue
            errs.append(o['mixed']); rej += o['ind'] > TAU; steps.append(o['iterations'])
        print('%-40s %9.1e %9.1e %9.1e %6d %5d %6.2f' % (name, q(errs, .5), q(errs, .99), max(errs), rej, exc,
                                                       sum(steps) / len(steps)), flush=True)
    print('%.0f s' % (time.time() - t0))
