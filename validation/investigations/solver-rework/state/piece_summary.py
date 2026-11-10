"""Summary of piece_study.py (results/state-piece-*.log).
Usage: python3 state/piece_summary.py"""
import glob, json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 1e-8
rows = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-piece-*.log'))):
    for line in open(f):
        if line.startswith('DATA '):
            try:
                rows.append(json.loads(line[5:]))
            except json.JSONDecodeError:
                pass

for name, sel in (('all', rows),) + tuple((c, [r for r in rows if r['cls'] == c]) for c in sorted({r['cls'] for r in rows})):
    p3 = [r['p3'] for r in sel]
    p4 = [r['p4'] for r in sel]
    dl = [r for r in sel if r['delivered']]
    print('%s: models %d (delivered %d)' % (name, len(sel), len(dl)))
    margins = [p['min_margin'] for p in p3 if p['min_margin'] is not None and p['min_margin'] < 1e300]
    print('  phase 3: pieces with LB_C > 0: %d | LB_C > min|p_hat| (certified): %d, undecided %d | LB_C > min|p| (exact field): %d '
          '(in delivered models %d) | smallest margin (min|p_hat| - LB_C)/beta %.3f | decisions with Bernstein denominator different: %d' % (
              sum(p['pieces'] for p in p3), sum(p['viol_hat'] for p in p3), sum(p['undecided_hat'] for p in p3),
              sum(p['viol_exact'] for p in p3), sum(r['p3']['viol_exact'] for r in dl), min(margins) if margins else math.nan,
              sum(r['decision_B_differs'] for r in sel)))
    gB = [(r['p3']['gate_B'], r['gate']) for r in sel if r['gate'] > 0]
    print('  phase 3: gate with Bernstein denominator / gate C: min %.3f max %.3f' % (min(a / b for a, b in gB), max(a / b for a, b in gB)))
    print('  phase 4: exact roots of the derivative in [0,1]: computed %d (multiple %d), exact %d (multiple %d) | sympy mismatches %d | '
          'candidates %d, missed local extrema %d (largest effect %.1e of the scale), spurious %d | position effect max %.1e, > beta %d, '
          'effect / second-order prediction max %.2f | reproducible %s' % (
              sum(p['roots_hat'] for p in p4), sum(p['multiple_hat'] for p in p4), sum(p['roots_exact'] for p in p4),
              sum(p['multiple_exact'] for p in p4), sum(p['sympy_mismatch'] for p in p4), sum(p['cand'] for p in p4),
              sum(p['missed'] for p in p4), max(p['missed_effect'] for p in p4), sum(p['spurious'] for p in p4),
              max(p['max_pos_effect'] for p in p4), sum(p['pos_over_beta'] for p in p4), max(p['pos_over_2nd'] for p in p4),
              all(p['reproducible'] for p in p4)))
    print('  phase 4: global extrema error (mixed measure, exact reference): delivered max %.2e | delivered with error > tau %d' % (
        max([r['ext_err'] for r in dl] + [0.]), sum(r['ext_wrong'] for r in sel)))
    notes = [n for r in sel for n in r['p3']['notes'] + r['p4']['notes']]
    for n in notes[:12]:
        print('     ', n)
