"""Aggregates the DATA lines of results/state-spring-rule-*.log (spring_rule.py).
Usage: python3 state/spring_rule_summary.py"""
import glob, json, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
TAU = 1e-8
models = []
for f in sorted(glob.glob(os.path.join(HERE, '..', 'results', 'state-spring-rule-*.log'))):
    if f.endswith('summary.log'):
        continue
    for line in open(f):
        if line.startswith('DATA '):
            try:
                models.append(json.loads(line[5:]))
            except json.JSONDecodeError:      # incomplete last line of a running study
                pass
RULES = ('jump', 'law', 'min', 'star')


def q(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, int(p * len(v)))] if v else float('nan')


recs = []
for mo in models:
    for r in mo['recs']:
        r['delivered'] = mo['gates']['min'] <= TAU
        r['cats'] = r['cats'] + (['translational'] if r['q'] == 3 else [])
        recs.append(r)
print('models %d, elastic restraints %d (translational %d, rotational %d)' % (
    len(models), len(recs), sum(r['q'] == 3 for r in recs), sum(r['q'] == 2 for r in recs)))

print('\n(a)-(d) identity, consistency relation and adjoint linearity (all restraints)')
print('  identity |D - |r_p|/rs_p| <= bound violated: %d; largest ratio to the bound %.2f' % (
    sum(not r['ident'] for r in recs), max(r['ident_ratio'] for r in recs)))
print('  D / (beta_jump + beta_law): max %.3f   with d reduced to |r| + delta r: max %.3f   restraints with D > sum: %d / %d (reduced)' % (
    max(r['Dbeta'] for r in recs), max(r['Dbeta_red'] for r in recs), sum(r['Dbeta'] > 1 for r in recs), sum(r['Dbeta_red'] > 1 for r in recs)))
print('  adjoint linearity: max_{m != p} |z_j - z_l|_m / |z_j - z_l|_p  p99 %.1e max %.1e;  rs_p |z_j - z_l|_p  min %.6f max %.6f' % (
    q([r['lin_off'] for r in recs], .99), max(r['lin_off'] for r in recs), min(r['lin_p'] for r in recs), max(r['lin_p'] for r in recs)))
print('  the same off-p deviation relative to u * max|z|: max %.1f (rounding level of the two adjoint solves)' % max(r['lin_off_u'] for r in recs))
for label, sub in (('delivered', [r for r in recs if r['delivered']]), ('rejected', [r for r in recs if not r['delivered']])):
    if sub:
        print('  %-9s n %5d: D/(bj+bl) max %.3f, lin_off max %.1e, rs_p|dz_p| in [%.4f, %.4f]' % (
            label, len(sub), max(r['Dbeta'] for r in sub), max(r['lin_off'] for r in sub), min(r['lin_p'] for r in sub), max(r['lin_p'] for r in sub)))

print('\n(e) injected implementation faults detected by D > beta_jump + beta_law')
for k, nm in (('k', 'k in the law off by 2^-20'), ('P', 'station load missing in the jump'), ('I', 'segment load integral missing in the jump')):
    v = [r['det_' + k] for r in recs if r['det_' + k] is not None]
    w = [r['id_' + k] for r in recs if r.get('id_' + k) is not None]
    print('  %-42s applicable %5d  detected by D > bj + bl %5d (%.1f %%), by the identity check %5d (%.1f %%)' % (
        nm, len(v), sum(v), 100 * sum(v) / len(v) if v else 0, sum(w), 100 * sum(w) / len(w) if w else 0))

print('\n(f) value per rule: error relative to max(|R_exact|, S) and beta, by category')
cats = ['all', 'translational', 'rotational', 'combined', 'soft', 'stiff', 'close springs', 'between close supports', 'springs only']
for label, sub0 in (('delivered models', [r for r in recs if r['delivered']]), ('rejected models', [r for r in recs if not r['delivered']])):
    print('  == %s' % label)
    print('  %-23s %5s | %-17s %-17s %-17s %-17s | %-22s | %-14s | %s' % ('category', 'n', 'jump med/max', 'law med/max', 'min-beta med/max', 'star med/max',
                                                               'min worse: n, max ratio', 'err > beta', 'alpha: in [0,1] / median'))
    for cat in cats:
        rs = [r for r in sub0 if cat in r['cats']]
        if not rs:
            continue
        cell = lambda nm: '%.0e/%.0e' % (q([r['e_' + nm] for r in rs], .5), max(r['e_' + nm] for r in rs))
        other = [(r['e_min'], r['e_law'] if r['b_jump'] <= r['b_law'] else r['e_jump']) for r in rs]
        worse = [(a, b) for a, b in other if a > b and a > 1e-15]
        big = max([a / b if b > 0 else math.inf for a, b in worse], default=1.)
        viol = '/'.join(str(sum(r['e_' + nm] > r['b_' + nm] for r in rs)) for nm in RULES)
        inside = sum(-1e-12 <= r['alpha'] <= 1 + 1e-12 for r in rs)
        print('  %-23s %5d | %-17s %-17s %-17s %-17s | %5d, %-14.2f | %-14s | %d / %.3g' % (
            cat, len(rs), cell('jump'), cell('law'), cell('min'), cell('star'), len(worse), big, viol, inside, q([r['alpha'] for r in rs], .5)))
    star_better = sum(r['e_star'] < r['e_min'] for r in sub0)
    star_worse = sum(r['e_star'] > r['e_min'] for r in sub0)
    print('  star vs min-beta: error smaller %d, larger %d, equal %d; beta_star/beta_min median %.3f min %.2e max %.3f' % (
        star_better, star_worse, len(sub0) - star_better - star_worse, q([r['b_star'] / r['b_min'] for r in sub0 if r['b_min'] > 0], .5),
        min([r['b_star'] / r['b_min'] for r in sub0 if r['b_min'] > 0], default=float('nan')),
        max([r['b_star'] / r['b_min'] for r in sub0 if r['b_min'] > 0], default=float('nan'))))

print('\nmodel decision per rule (gate = max of field gate incl. piece gate B, rigid reactions, elastic reactions by rule)')
for rule in RULES:
    d = [m for m in models if m['gates'][rule] <= TAU]
    j = [m for m in models if m['gates'][rule] > TAU]
    print('  %-5s delivered %5d rejected %3d | max error delivered %.1e | delivered with error > tau %d | rejected with error <= tau %d' % (
        rule, len(d), len(j), max([m['errs'][rule] for m in d], default=0.), sum(m['errs'][rule] > TAU for m in d),
        sum(m['errs'][rule] <= TAU for m in j)))
extra = [m for m in models if m['gates']['jump'] > TAU >= m['gates']['min']]
for m in extra[:8]:
    print('  rejected only with the jump rule: %-22s gate jump %.1e  gate min %.1e  error %.1e' % (m['id'], m['gates']['jump'], m['gates']['min'], m['errs']['min']))
print('  (%d models rejected only with the jump rule, %d only with the law rule)' % (
    len(extra), sum(m['gates']['law'] > TAU >= m['gates']['min'] for m in models)))
