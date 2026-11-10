"""Aggregates the DATA lines of results/state-gate-*.log (gate_study.py).
Usage: python3 state/gate_summary.py"""
import glob, json, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 1e-8
models = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-gate-*.log'))):
    if f.endswith('summary.log'):
        continue
    for line in open(f):
        if line.startswith('DATA '):
            try:
                models.append(json.loads(line[5:]))
            except json.JSONDecodeError:      # incomplete last line of a running study
                pass
RULES = ('none', 'one', 'adaptive', 'beta')


def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * len(v)))] if v else float('nan')


def gate(o, variant):
    return max(o['station'], o['react'], o[variant])


print('models %d (targeted and 10 random classes; numerically singular ones excluded)' % len(models))
print('\n== point 2: piece-gate variants (adjoint refinement one step)')
for v in ('A', 'B', 'C'):
    d = [m for m in models if gate(m['one'], v) <= TAU]
    j = [m for m in models if gate(m['one'], v) > TAU]
    print('  %s: delivered %5d rejected %3d | largest gate %.1e | delivered with error > tau %d | rejected with error <= tau %d | max error delivered %.1e' % (
        v, len(d), len(j), max(gate(m['one'], v) for m in models), sum(m['one']['err'] > TAU for m in d), sum(m['one']['err'] <= TAU for m in j),
        max([m['one']['err'] for m in d], default=0.)))
diffAB = [m for m in models if (gate(m['one'], 'A') <= TAU) != (gate(m['one'], 'B') <= TAU)]
diffCB = [m for m in models if (gate(m['one'], 'C') <= TAU) != (gate(m['one'], 'B') <= TAU)]
print('  decisions A != B: %d   C != B: %d' % (len(diffAB), len(diffCB)))
print('  ratio gate C / gate B: max %.3f  (C >= B expected only through the margin)' % max(gate(m['one'], 'C') / gate(m['one'], 'B') for m in models if gate(m['one'], 'B') > 0))
print('  ratio gate A / gate B: median %.2f p99 %.1e max %.1e' % (
    q([gate(m['one'], 'A') / gate(m['one'], 'B') for m in models], .5), q([gate(m['one'], 'A') / gate(m['one'], 'B') for m in models], .99),
    max(gate(m['one'], 'A') / gate(m['one'], 'B') for m in models)))
bycls = {}
for m in diffAB:
    bycls.setdefault(m['cls'], []).append(m)
for cls, ms in sorted(bycls.items()):
    print('    additional rejections by A in %-10s %3d (largest actual error %.1e)' % (cls, len(ms), max(x['one']['err'] for x in ms)))
for m in sorted(diffAB, key=lambda m: -gate(m['one'], 'A'))[:6]:
    w = m['one']['whereA']
    print('    %-22s gate A %.1e B %.1e C %.1e error %.1e | where: %s, piece length %.1e L, max|Q|/S %.1e' % (
        m['id'], gate(m['one'], 'A'), gate(m['one'], 'B'), gate(m['one'], 'C'), m['one']['err'], w['q'], w['h'], w['ratio'] or 0))
print('  gate histogram (decades, variant C): ' + ' '.join('%d:%d' % (e, sum(1 for m in models if math.floor(math.log10(max(gate(m['one'], 'C'), 1e-300))) == e)) for e in range(-17, 1)))
near = [m for m in models if 1e-10 < gate(m['one'], 'C') <= TAU]
print('  gate C in (1e-10, 1e-8]: %d models, largest actual error %.1e, gate/error min %.1f' % (
    len(near), max([m['one']['err'] for m in near], default=0.), min([gate(m['one'], 'C') / m['one']['err'] for m in near if m['one']['err'] > 0], default=float('nan'))))
print('  strict monotonicity (no tolerance) of beta along pieces: decreasing steps %d in %d models' % (
    sum(m['one']['mono'] for m in models), sum(m['one']['mono'] > 0 for m in models)))

print('\n== point 3: omega')
for label, sel in (('delivered', lambda m: gate(m['one'], 'C') <= TAU), ('rejected', lambda m: gate(m['one'], 'C') > TAU)):
    om = [m['one']['omega'] / 2 ** -53 for m in models if sel(m)]
    if om:
        print('  %-9s n %5d: omega/u median %.2f p99 %.2f max %.2f' % (label, len(om), q(om, .5), q(om, .99), max(om)))
hi = sorted(models, key=lambda m: -m['one']['omega'])[:5]
for m in hi:
    print('    largest omega: %-22s omega/u %.1f gate %.1e error %.1e' % (m['id'], m['one']['omega'] / 2 ** -53, gate(m['one'], 'C'), m['one']['err']))
pert = [m for m in models if 'perturb' in m]
print('  perturbed LU factors (every 5th model, %d models):' % len(pert))
for eps in ('1e-08', '0.0001', '0.1'):
    rs = [m['perturb'][eps] for m in pert]
    fin = [r for r in rs if r['finite'] and r['gate'] is not None]
    deliv = [r for r in fin if r['gate'] <= TAU]
    bad = [r for r in deliv if r['err'] > TAU]
    under = [r for r in fin if r['err'] > r['gate'] and r['gate'] > 0]
    print('    eps %-7s finite %4d not finite %3d | delivered %4d (error > tau: %d, error > gate: %d) rejected %4d | error > gate %d | omega/u among delivered max %.1e, rejected median %.1e' % (
        eps, len(fin), len(rs) - len(fin), len(deliv), len(bad), sum(r['err'] > r['gate'] for r in deliv), len(fin) - len(deliv), len(under),
        max([r['omega'] / 2 ** -53 for r in deliv], default=0.), q([r['omega'] / 2 ** -53 for r in fin if r['gate'] > TAU], .5)))
    for r, m in [(m['perturb'][eps], m) for m in pert if m['perturb'][eps]['finite'] and m['perturb'][eps]['gate'] is not None
                 and m['perturb'][eps]['err'] > m['perturb'][eps]['gate']][:3]:
        print('      error above gate: %-22s error %.2e gate %.2e omega/u %.1e' % (m['id'], r['err'], r['gate'], r['omega'] / 2 ** -53))

print('\n== point 4: adjoint refinement rules (gate with piece variant C)')
for rule in RULES:
    d = [m for m in models if gate(m[rule], 'C') <= TAU]
    j = [m for m in models if gate(m[rule], 'C') > TAU]
    steps = sum(m[rule]['adj_steps'] for m in models)
    solves = sum(m[rule]['adj_solves'] for m in models)
    print('  %-8s delivered %5d rejected %3d | delivered with error > tau %d | rejected with error <= tau %d | refinement steps per adjoint %.3f (max per model %.2f)' % (
        rule, len(d), len(j), sum(m[rule]['err'] > TAU for m in d), sum(m[rule]['err'] <= TAU for m in j),
        steps / solves, max(m[rule]['adj_steps'] / m[rule]['adj_solves'] for m in models)))
for rule in ('none', 'adaptive', 'beta'):
    r = [gate(m[rule], 'C') / gate(m['one'], 'C') for m in models if gate(m['one'], 'C') > 0]
    print('  gate %-8s / gate one: min %.4f median %.4f max %.4f' % (rule, min(r), q(r, .5), max(r)))
hard = sorted(models, key=lambda m: -gate(m['one'], 'C'))[:12]
print('  hardest models (largest gate): gate none / one / adaptive / beta | error one | steps per adjoint adaptive / beta')
for m in hard:
    print('    %-22s %.2e / %.2e / %.2e / %.2e | %.1e | %.2f / %.2f' % (
        m['id'], *(gate(m[r], 'C') for r in RULES), m['one']['err'], m['adaptive']['adj_steps'] / m['adaptive']['adj_solves'],
        m['beta']['adj_steps'] / m['beta']['adj_solves']))
for rule in ('none', 'adaptive', 'beta'):
    ch = [m for m in models if gate(m['one'], 'C') > 0 and abs(gate(m[rule], 'C') / gate(m['one'], 'C') - 1) > 0.01]
    print('  gate of %-8s differs from one step by more than 1 %%: %d models, of these with gate <= tau: %d' % (
        rule, len(ch), sum(gate(m['one'], 'C') <= TAU or gate(m[rule], 'C') <= TAU for m in ch)))
print('  (CPU times of the parallel runs are not comparable; cost is given by solves and steps per adjoint)')
