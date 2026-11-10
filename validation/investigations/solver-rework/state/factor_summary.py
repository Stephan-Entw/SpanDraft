"""Summary of factor_study.py (results/state-factor-*.log).
Usage: python3 state/factor_summary.py"""
import glob, json, math, os, statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 1e-8
rows = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-factor-*.log'))):
    for line in open(f):
        if line.startswith('DATA '):
            try:
                rows.append(json.loads(line[5:]))
            except json.JSONDecodeError:
                pass


def q(xs, p):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(p * len(xs)))]


for name, sel in (('all', rows),) + tuple((c, [r for r in rows if r['cls'] == c]) for c in sorted({r['cls'] for r in rows})):
    s = [r['struct'] for r in sel if 'struct' in r]
    g = [r['gepp'] for r in sel if r.get('gepp')]
    if not s:
        continue
    print('%s: models %d, largest n %d, largest number of stations %d' % (name, len(sel), max(x['n'] for x in s), max(x['stations'] for x in s)))
    print('  structure: lower bandwidth max %d, upper max %d, stations coupled by one row max %d (1 = neighbours only), unknowns per station max %d' % (
        max(x['lower'] for x in s), max(x['upper'] for x in s), max(x['station_span'] for x in s), max(x['max_unknowns_per_station'] for x in s)))
    print('  GEPP: growth rho max %.6f (p99 %.6f), rho_U max %.6f, largest multiplier %.3f, multiplier distance max %d, band of U max %d, '
          'row interchanges per unknown %.2f' % (
              max(x['rho'] for x in g), q([x['rho'] for x in g], .99), max(x['rho_U'] for x in g), max(x['mult_max'] for x in g),
              max(x['mult_dist'] for x in g), max(x['U_band'] for x in g), st.mean(x['swaps'] / r['struct']['n'] for x, r in zip(g, sel) if r.get('gepp'))))
    both = [r for r in sel if 'lu' in r and 'qr' in r and 'err' in r['lu'] and 'err' in r['qr']]
    if not both:
        continue
    same = sum(r['lu']['status'] == r['qr']['status'] for r in both)
    dl = [r for r in both if r['lu']['status'] == 'delivered']
    ratio = [r['lu']['err'] / r['qr']['err'] for r in both if r['qr']['err'] > 0 and r['lu']['err'] > 0]
    worse = [(r['id'], r['lu']['err'], r['qr']['err']) for r in both if r['lu']['err'] > 10 * r['qr']['err'] and r['lu']['err'] > 1e-14]
    better = [(r['id'], r['lu']['err'], r['qr']['err']) for r in both if r['qr']['err'] > 10 * r['lu']['err'] and r['qr']['err'] > 1e-14]
    print('  LU vs QR: compared %d | same status %d | LU delivered %d (QR %d) | largest error delivered LU %.2e QR %.2e | err LU/QR median %.2f max %.2e min %.2e' % (
        len(both), same, len(dl), sum(r['qr']['status'] == 'delivered' for r in both),
        max([r['lu']['err'] for r in dl] + [0.]), max([r['qr']['err'] for r in both if r['qr']['status'] == 'delivered'] + [0.]),
        st.median(ratio) if ratio else math.nan, max(ratio) if ratio else math.nan, min(ratio) if ratio else math.nan))
    print('  LU error > 10x QR error and > 1e-14: %d %s' % (len(worse), ['%s %.1e/%.1e' % w for w in worse[:6]]))
    print('  QR error > 10x LU error and > 1e-14: %d %s' % (len(better), ['%s %.1e/%.1e' % w for w in better[:6]]))
    diff = [(r['id'], r['lu']['status'], r['qr']['status'], r['lu']['gate'], r['qr']['gate'], r['lu']['err'], r['qr']['err']) for r in both
            if r['lu']['status'] != r['qr']['status']]
    for d in diff[:8]:
        print('    status differs: %s LU %s QR %s | gate %.1e / %.1e | err %.1e / %.1e' % d)
    print('  refinement steps LU %.2f QR %.2f | omega LU max %.1e QR max %.1e' % (
        st.mean(r['lu']['steps'] for r in both), st.mean(r['qr']['steps'] for r in both),
        max(r['lu']['omega'] for r in both), max(r['qr']['omega'] for r in both)))
    zp = [r['id'] for r in sel if r.get('status') == 'zero pivot' or r.get('lu', {}).get('status') == 'zero pivot']
    print('  zero pivot (GEPP): %d %s' % (len(zp), zp[:5]))
adv = [l for f in glob.glob(os.path.join(HERE, '..', 'results', 'state-factor-adversarial.log')) for l in open(f) if l.startswith('search')]
if adv:
    print('adversarial search (hill climb on rho):', ' '.join(a.split(':')[1].strip().replace('largest rho ', '') for a in adv))
