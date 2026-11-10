"""Aggregates the DATA lines of results/state-acceptance-*.log: totals per acceptance factor and
adjoint variant, and beta/err quantiles per quantity and problem class.
Usage: python3 state/acceptance_summary.py"""
import glob, json, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
recs = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-acceptance-*.log'))):
    if f.endswith('summary.log'):
        continue
    for line in open(f):
        if line.startswith('DATA '):
            recs.append(json.loads(line[5:]))
TAU = 1e-8
print('totals over %d classes (targeted + 10 random classes), tau = %.0e' % (len({r['cls'] for r in recs}), TAU))
for adj in ('none', 'one', 'adaptive'):
    rs = [r for r in recs if r['adj'] == adj]
    viol = [v for r in rs for v in r['viol']]
    print('adjoint refinement %-8s | values with err > beta in delivered models: %d%s' % (
        adj, len(viol), ' (largest err/beta %.2f, largest normalised err %.1e)' % (max(v[2] for v in viol), max(v[3] for v in viol)) if viol else ''))
    for s in ('1', '2', '4', '10'):
        d = sum(r['rows'][s]['delivered'] for r in rs)
        j = sum(r['rows'][s]['rejected'] for r in rs)
        mx = max(r['rows'][s]['max_err'] for r in rs)
        w = sum(r['rows'][s]['wrong'] for r in rs)
        u = sum(r['rows'][s]['unnecessary'] for r in rs)
        print('   s=%-2s delivered %6d  rejected %3d  max actual error delivered %.1e  delivered with error > tau %d  rejected with error <= tau %d' % (s, d, j, mx, w, u))


def quantiles(h):
    items = sorted((int(b), n) for b, n in h.items())
    tot = sum(n for _, n in items)
    out = []
    for p in (0.0, 0.01, 0.5, 0.99):
        acc = 0
        for b, n in items:
            acc += n
            if acc >= max(1, p * tot):
                out.append(b / 4)
                break
    return tot, out


print('beta/err per value (err > 0), adjoint refinement one step: log10 quantiles min / p1 / median / p99 (0.25-decade bins, lower bin edge)')
classes = sorted({r['cls'] for r in recs})
for qn in ('w', 't', 'M', 'V', 'R', 'Rm'):
    line = '%-3s' % qn
    agg = {}
    for cls in classes:
        h = next((r['hist'].get(qn, {}) for r in recs if r['cls'] == cls and r['adj'] == 'one'), {})
        for b, n in h.items():
            agg[b] = agg.get(b, 0) + n
    tot, qs = quantiles(agg)
    print('%s all classes  n %8d  %s' % (line, tot, ' / '.join('%+.2f' % v for v in qs)))
    for cls in classes:
        h = next((r['hist'].get(qn, {}) for r in recs if r['cls'] == cls and r['adj'] == 'one'), {})
        if h:
            tot, qs = quantiles(h)
            print('      %-12s n %8d  %s' % (cls, tot, ' / '.join('%+.2f' % v for v in qs)))
