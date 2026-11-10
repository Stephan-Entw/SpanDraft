"""Random models against the exact reference.
Usage: python3 state/rand.py s|f N r0 [tau]   (one distance class per run)"""
import math, os, random, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from gen import gen_s, gen_f
from kinematics import is_mechanism
from metrics import compare
from sens import sensitivity

kind, N, r0 = sys.argv[1], int(sys.argv[2]), float(sys.argv[3])
tau = float(sys.argv[4]) if len(sys.argv) > 4 else 1e-8
seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991   # as s_fuzz / f_fuzz
rng = random.Random(seed)
t0 = time.time()
st = dict(models=0, solved=0, mech=0, kin_only=0, exact_only=0, exc=0, singular=0)
singular = []
glob, mixed, ind, ratio, its = [], [], [], [], {}
rejected, violating, worst = [], [], None
for k in range(N):
    c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
    st['models'] += 1
    km = is_mechanism(c)
    try:
        T.Transfer(c); em = False
    except T.Mechanism:
        em = True
    if km or em:
        st['mech'] += 1
        st['kin_only'] += km and not em
        st['exact_only'] += em and not km
        continue
    try:
        o, m = compare(c)
    except ZeroDivisionError:          # exactly stable, numerically singular: rejected
        st['singular'] += 1
        singular.append(c)
        continue
    except Exception as e:
        st['exc'] += 1
        print('  EXC', c['id'], repr(e)[:150], flush=True)
        continue
    st['solved'] += 1
    glob.append(o['glob']); mixed.append(o['mixed']); ind.append(o['ind'])
    if o['mixed'] > 0:
        ratio.append(o['ind'] / o['mixed'])
    its[o['iterations']] = its.get(o['iterations'], 0) + 1
    if worst is None or o['mixed'] > worst[0]:
        worst = (o['mixed'], o['ind'], c)
    if o['ind'] > tau:
        rejected.append((o, c))
    if o['viol']:
        violating.append((o, c))


def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * len(v)))] if v else float('nan')


print('%s L/%.0e models %d solved %d mech %d (kin only %d, exact only %d) zero pivot %d exc %d | %.0f s' % (
    kind, 1 / r0, st['models'], st['solved'], st['mech'], st['kin_only'], st['exact_only'], st['singular'], st['exc'], time.time() - t0))
for name, v in (('glob', glob), ('mixed', mixed), ('indicator', ind), ('indicator/mixed', ratio)):
    print('  %-16s median %.0e  p99 %.0e  max %.0e%s' % (name, q(v, .5), q(v, .99), max(v) if v else float('nan'),
                                                        '  min %.0e' % min(v) if name == 'indicator/mixed' and v else ''))
print('  refinement steps', dict(sorted(its.items())))
print('  rejected (indicator > %.0e): %d' % (tau, len(rejected)), {t: sum(x > t for x in ind) for t in (1e-12, 1e-10, 1e-8, 1e-6)})
if worst:
    print('  worst mixed %.1e (indicator %.1e): %s' % (worst[0], worst[1], {k: worst[2][k] for k in ('L', 'E', 'I', 'sup', 'springs', 'hinges', 'ei', 'lin', 'pf', 'pm', 'q')}))
for c in singular:
    s = sensitivity(c)
    print('  SING %-16s zero pivot | 1-ulp sensitivity %s' % (c['id'], ' '.join('%s %.0e' % kv for kv in s.items())), flush=True)
accepted_viol = [(o, c) for o, c in violating if o['ind'] <= tau]
print('  violations in accepted models (indicator <= tau): %d models' % len(accepted_viol))
for o, c in rejected:
    s = sensitivity(c)
    print('  REJ %-16s mixed %.1e indicator %.1e | 1-ulp sensitivity %s' % (c['id'], o['mixed'], o['ind'],
          ' '.join('%s %.0e' % kv for kv in s.items())), flush=True)
print('  models with violations: %d, values %d' % (len(violating), sum(len(o['viol']) for o, _ in violating)))
for o, c in violating:
    for v in o['viol']:
        print('  VIOL %-16s %s %s x=%.17g err %.2e ind %.2e value %.2e rel %.1e' % (c['id'], v['q'], v['side'], v['x'], v['err'], v['ind'], v['value'], v['rel']))
