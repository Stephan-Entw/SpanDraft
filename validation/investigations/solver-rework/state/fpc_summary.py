"""Summary of fpc_stage_a.py and fpc_chain.py (results/state-fpc-*.log).
Usage: python3 state/fpc_summary.py"""
import glob, json, math, os
from collections import Counter
from fractions import Fraction

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.environ.get('FPC_RESULTS', os.path.join(HERE, '..', 'results'))
TAU = Fraction(1, 100_000_000)

print('deterministic boundary checks and mutation detection:')
with open(os.path.join(RES, 'state-fpc-boundary.log')) as boundary:
    for line in boundary:
        print('  ' + line.rstrip())
print()

print('stage A (single formulas against their bounds, exact reference):')
for l in open(os.path.join(RES, 'state-fpc-stage-a.log')):
    if not l.startswith('DATA'):
        print('  ' + l.rstrip())

rows = []
for f in sorted(glob.glob(os.path.join(RES, 'state-fpc-chain-*.log'))):
    for l in open(f):
        if l.startswith('DATA '):
            rows.append(json.loads(l[5:]))

STAGES = [('B_entries', 'B |S^ - S*| > dS'), ('B_rhs', 'B |sb^ - sb*| > dsb'), ('B_res', 'B |r*| > d'),
          ('C_premise', 'C |I - R S*| > M'), ('C_contraction', 'C Mv > mu v'), ('C_inverse', 'C |S*^-1 - R| > enclosure'),
          ('D_viol', 'D |y^ - y*| > E (verified)'), ('R_viol', 'D reactions error > bound'),
          ('E_viol', 'E/F SI value error > SI bound'), ('E_extrema_viol', 'E extremum candidate error > SI bound'),
          ('R_gate_viol', 'F reaction bound > tau times scale'), ('F_gate_viol', 'F field bound > tau times scale'),
          ('F_actual_over_tau', 'F delivered actual error > tau'), ('E_majorant', 'E bound inside piece > majorant'),
          ('E_LB_viol', 'E LB > exact Bernstein upper enclosure of min|Q|'), ('E_LB_undecided', 'E LB inside the enclosure (undecided)'),
          ('formulation_mismatch', 'exact system != transfer_ref at stations')]


def label(r):
    s = r['set']
    if r.get('LUeps'):
        s += ' LU %g' % r['LUeps'][-1]
    return '%s (%s)' % (s, r['mode'])


groups = {}
for r in rows:
    groups.setdefault(label(r), []).append(r)
for g, rs in sorted(groups.items()):
    st = Counter((r['status'], r.get('cause')) for r in rs)
    print('%s: runs %d | %s' % (g, len(rs), ', '.join('%s%s %d' % (a, '/' + b if b else '', n) for (a, b), n in sorted(st.items(), key=str))))
    for key, text in STAGES:
        vals = [r[key] for r in rs if r.get(key) is not None]
        if vals:
            bad = sum(1 for v in vals if v)
            if bad or key in ('B_res', 'D_viol', 'E_viol', 'E_extrema_viol', 'C_premise', 'C_contraction', 'F_gate_viol', 'formulation_mismatch'):
                print('    %-48s checked models %3d, with violation %d' % (text, len(vals), bad))
    def mx(k):
        v = [r[k] for r in rs if r.get(k) is not None]
        return max(v) if v else math.nan
    print('    worst ratio error/bound: residual %.3g, state %.3g, reactions %.3g, field %.3g' % (
        mx('B_res_worst'), mx('D_worst'), mx('R_worst'), mx('E_worst')))
    dl = [r for r in rs if r['status'] == 'delivered']
    if dl:
        print('    delivered %d: largest error in the contract measure %.2e, error over gate %d, largest gate %.2e' % (
            len(dl), max(r.get('E_err_norm', 0.) for r in dl), sum(bool(r.get('E_err_over_gate')) for r in dl),
            max(r['gate'] for r in dl)))
    wrong = [r for r in rs if r['status'] != 'delivered' and r.get('E_err_norm', 0) > TAU]
    if wrong:
        print('    rejected with error > tau if it had been delivered: %d %s' % (len(wrong), sorted({(r['id'], r.get('cause')) for r in wrong})[:6]))
    ver = [r for r in rs if r.get('mu')]
    if ver:
        first = sum(1 for r in ver if r['mu'][0] < 1)
        anyv = sum(1 for r in ver if min(r['mu']) < 1)
        print('    verification: mu < 1 with v = RawBound + delta D %d of %d, after Neumann steps %d of %d; largest final mu %.2e' % (
            first, len(ver), anyv, len(ver), max(min([u for u in r['mu'] if u < 1] or [math.inf]) for r in ver)))
        nv = [r['id'] for r in ver if min(r['mu']) >= 1]
        if nv:
            print('    not verified:', nv[:8])
    old = [r for r in rs if 'old_viol' in r]
    if old:
        print('    round-4 bound (state.py + verification) at the evaluation points: models with error > bound %d of %d, worst %.3g' % (
            sum(1 for r in old if r['old_viol']), len(old), max(r['old_worst'] for r in old)))
ex = [r for r in rows if r['status'] == 'exception']
print('exceptions:', [(r['id'], r['detail']) for r in ex][:5])

# ---------------------------------------------------------------- regression against round 4
reg = []
for f in sorted(glob.glob(os.path.join(RES, 'state-fpc-regress-*.log'))):
    for l in open(f):
        if l.startswith('DATA '):
            reg.append(json.loads(l[5:]))
if reg:
    st = [r for r in reg if r['status'] != 'UnstableModel']
    print('regression on the round-4 random sets (every 5th model): exactly stable %d (of %d)' % (len(st), len(reg)))
    print('   status:', dict(Counter('%s%s' % (r['status'], '/' + r['cause'] if r.get('cause') else '') for r in st)))
    print('   values or reactions with error > contract bound: %d' % sum(r.get('viol', 0) for r in st))
    print('   delivered values with bound > tau times scale: %d' % sum(r.get('gate_viol', 0) for r in st))
    print('   measurement failures: %d' % sum('measure' in r or (r['status'] == 'delivered' and 'err' not in r) for r in st))
    dl = [r for r in st if r['status'] == 'delivered']
    print('   delivered %d: largest error in the contract measure %.2e, error > tau %d' % (
        len(dl), max(r.get('err', 0.) for r in dl), sum(r.get('over_tau', False) for r in dl)))
    rj = [r for r in st if r['status'] != 'delivered' and 'err' in r]
    print('   rejected with error <= tau: %d of %d %s' % (sum(r['err'] <= TAU for r in rj), len(rj),
                                                       [(r['id'], r.get('cause')) for r in rj][:8]))
    cmp4 = [r for r in st if r.get('r4')]
    m4 = {'delivered': 'delivered', 'rejected': 'rejected', 'unverified': 'rejected'}
    same = sum((r['status'] == 'delivered') == (r['r4'] == 'delivered') for r in cmp4)
    diff = [(r['id'], r['r4'], r['status'], r.get('cause')) for r in cmp4 if (r['status'] == 'delivered') != (r['r4'] == 'delivered')]
    print('   compared with round 4 (verification T): %d models, same decision %d, different %d %s' % (len(cmp4), same, len(diff), diff[:10]))
