"""Summary of refine_study.py (results/state-refine-*.log).
Usage: python3 state/refine_summary.py"""
import glob, json, math, os, statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 1e-8
rows = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-refine-*.log'))):
    for line in open(f):
        if line.startswith('DATA '):
            try:
                rows.append(json.loads(line[5:]))
            except json.JSONDecodeError:
                pass

CONF = [('PA', 'primary A (SpanDraft), adjoint A'), ('PB', 'primary B (xGERFS), adjoint A'), ('PC', 'primary C (LAWN 165), adjoint A'),
        ('PD', 'primary D (5 steps), adjoint A'), ('ZB', 'primary A, adjoint B (xGERFS)'), ('ZC', 'primary A, adjoint C (LAWN 165)'),
        ('ZD', 'primary A, adjoint D (5 steps)'), ('Zone', 'primary A, adjoint exactly one step'),
        ('T', 'primary A, adjoint A, with trust correction'), ('TB', 'primary A, adjoint B, with trust correction'),
        ('TN', 'primary A, no adjoint refinement, with trust')]
for eps in ('0.0001', '0.1'):
    for a in 'ABCD':
        CONF.append(('E%s%s' % (eps, a), 'LU perturbed %s, primary A, adjoint %s' % (eps, a)))
    CONF.append(('E%sT' % eps, 'LU perturbed %s, primary A, adjoint A, trust' % eps))
    CONF.append(('E%sTB' % eps, 'LU perturbed %s, primary A, adjoint B, trust' % eps))
    CONF.append(('E%sTN' % eps, 'LU perturbed %s, primary A, no adjoint refinement, trust' % eps))


def line(key, label, sel):
    xs = [r[key] for r in sel if key in r and r[key].get('status') != 'failed']
    if not xs:
        return
    dl = [x for x in xs if x['status'] == 'delivered']
    un = [x for x in xs if x['status'] == 'unverified']
    wrong = sum(x['err'] > TAU for x in dl)
    under_models = sum(x['under'] > 0 for x in dl)
    under_vals = sum(x['under'] for x in dl)
    worst = max([x['worst'] for x in dl] + [0.])
    ratio = [x['gate'] / x['err'] for x in dl if x['err'] > 0]
    print('  %-48s n %5d | delivered %5d rejected %4d unverified %3d | delivered: err > tau %d, models with error > beta %3d (%4d values, worst %.3f) | '
          'gate/err min %.1f | steps %.2f, per adjoint %.3f' % (
              label, len(xs), len(dl), len(xs) - len(dl) - len(un), len(un), wrong, under_models, under_vals, worst,
              min(ratio) if ratio else math.nan, st.mean(x['steps'] for x in xs), st.mean(x['adj'] for x in xs)))


for cls_name, sel in (('all', rows), ('targeted', [r for r in rows if r['cls'] == 'targeted']),
                      ('random', [r for r in rows if r['cls'] not in ('targeted', 'neartau')]),
                      ('neartau', [r for r in rows if r['cls'] == 'neartau'])):
    print('%s: models %d' % (cls_name, len(sel)))
    for key, label in CONF:
        line(key, label, sel)

# rejected-with-error-below-tau and status differences between rules
print('decisions that differ from PA (all models):')
for key, _ in CONF[1:9]:
    diff = [r['id'] for r in rows if key in r and 'PA' in r and r[key].get('status') != r['PA'].get('status')]
    print('  %-6s %d %s' % (key, len(diff), diff[:5]))

# trust
for key in ('T', 'TB', 'TN'):
  T = [r[key] for r in rows if key in r and 'muB' in r[key]]
  if T:
    print('trust %-2s (unperturbed): models %d | mu_B max %.2e median %.2e | mu_D max %.2e, mu_D >= 1 in %d | unverified %d | corrected/raw gate max %.6f' % (
    key, len(T), max(t['muB'] for t in T), st.median(t['muB'] for t in T), max(t['muD'] for t in T), sum(t['muD'] >= 1 for t in T),
    sum(not t['verified'] for t in T), max(t['gate'] / t['raw_gate'] for t in T if t['raw_gate'] > 0)))
for eps, key in [(e, 'E%s%s' % (e, k)) for e in ('0.0001', '0.1') for k in ('T', 'TB', 'TN')]:
    TT = [r[key] for r in rows if key in r and 'muB' in r[key]]
    if not TT:
        continue
    un = [t for t in TT if not t['verified']]
    print('trust %s: models %d | mu_B max %.2e (verified ones) | unverified %d, of them with error > beta %d | '
          'verified with error > beta %d | verified delivered with err > tau %d' % (
              key, len(TT), max([t['muB'] for t in TT if t['verified']] + [0.]), len(un), sum(t['under'] > 0 for t in un),
              sum(t['under'] > 0 for t in TT if t['verified']), sum(t['status'] == 'delivered' and t['err'] > TAU for t in TT)))
# BERR and LAWN estimate (primary C)
PC = [r['PC'] for r in rows if 'PC' in r and r['PC'].get('lawn') is not None]
print('primary C: LAWN normwise estimate below the actual (mixed) error in %d of %d models' % (
    sum(x['lawn'] < x['err'] for x in PC), len(PC)))
B = [r['PA']['berr'] for r in rows if 'PA' in r and 'berr' in r['PA']]
print('BERR (xGERFS definition with SAFE1/SAFE2) after rule A: = 1 in %d of %d models (rows with |S||y| + |b| = 0)' % (
    sum(b >= 1 for b in B), len(B)))
# near tau: boundary statistics
nt = [r for r in rows if r['cls'] == 'neartau']
if nt:
    g = [(r['PA']['gate'], r['PA']['err'], r['PA']['status']) for r in nt]
    print('near tau: gate in (1e-10, 1e-8]: %d, delivered %d, gate/err min %.1f, largest delivered error %.2e' % (
        sum(1e-10 < a <= 1e-8 for a, _, _ in g), sum(s == 'delivered' for _, _, s in g),
        min(a / e for a, e, s in g if s == 'delivered' and e > 0), max(e for _, e, s in g if s == 'delivered')))
    rej_ok = [(r['id'], r['PA']['gate'], r['PA']['err']) for r in nt if r['PA']['status'] == 'rejected' and r['PA']['err'] <= TAU]
    print('near tau: rejected with actual error <= tau: %d of %d rejected' % (len(rej_ok), sum(s == 'rejected' for *_, s in g)))
