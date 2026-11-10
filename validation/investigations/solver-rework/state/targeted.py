"""Targeted limit cases: configurations x distance g = r*L x stiffness k*L^3/EI = 10^u.
Usage: python3 state/targeted.py [tau]"""
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from cases import all_cases
from kinematics import is_mechanism
from metrics import compare

tau = float(sys.argv[1]) if len(sys.argv) > 1 else 1e-8
rows = {}
t0 = time.time()
for nm, c in all_cases():
    a = rows.setdefault(nm, dict(n=0, mech=0, glob=0., mixed=0., ind=0., viol=0, rej=0, it=0, exc=0))
    if is_mechanism(c):
        a['mech'] += 1
        continue
    try:
        o, _ = compare(c)
    except Exception as e:
        a['exc'] += 1
        print('  EXC', c['id'], repr(e)[:120])
        continue
    a['n'] += 1
    a['glob'] = max(a['glob'], o['glob']); a['mixed'] = max(a['mixed'], o['mixed']); a['ind'] = max(a['ind'], o['ind'])
    a['viol'] += len(o['viol']); a['rej'] += o['ind'] > tau; a['it'] = max(a['it'], o['iterations'])
    for v in o['viol']:
        print('  VIOL', c['id'], v)
print('%-17s %4s %4s %9s %9s %9s %5s %4s %3s' % ('config', 'n', 'mech', 'glob', 'mixed', 'ind', 'viol', 'rej', 'it'))
for nm, a in rows.items():
    print('%-17s %4d %4d %9.1e %9.1e %9.1e %5d %4d %3d%s' % (nm, a['n'], a['mech'], a['glob'], a['mixed'], a['ind'], a['viol'],
                                                         a['rej'], a['it'], '  EXC %d' % a['exc'] if a['exc'] else ''))
print('tau %.0e, %.0f s' % (tau, time.time() - t0))
