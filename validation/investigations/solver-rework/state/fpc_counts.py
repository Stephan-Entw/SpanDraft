"""Round 5: rounding counts k of every contract formula, derived mechanically from the operation
sequences in fpc.py with the rules of FLOATING_POINT_CONTRACT.md (L1: product/quotient k_a + k_b + 1,
sum of same-sign terms max(k_a, k_b) + 1, multiplication by a power of two exact, distance
fl(x - c) 2^-eL one rounding; inputs exact, EI = fl(E I) one rounding). The table of the
contract document is this output; stage A checks the normal coefficient bounds against exact values. The counts
for other families describe the normal-only model. Their operative bounds use
local absolute propagation.
Usage: python3 state/fpc_counts.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpc import T, point_force, point_couple, diff_term, ramp_term

d = T(0.37, 1)                 # a distance (one rounding)
e = T(0.9, 1)                  # EI / EI_ref (adapter product E I)
one = T(1.0)
v = T(0.8)                     # an input intensity (exact)
print('transfer coefficients (a = distance, e = EI/EI_ref):')
a = d
for name, t in (('a', a), ('a/e', a / e), ('a a/2/e', ((a * a) / 2) / e), ('a a a/6/e', (((a * a) * a) / 6) / e)):
    print('  %-12s k = %d' % (name, t.k))
print('point force v d^(p-1)/(p-1)!  and point couple -v d^(p-2)/(p-2)! (v exact):')
for p in (1, 2, 3, 4):
    print('  p = %d  force k = %d%s' % (p, point_force(v, d, p).k, '' if p == 1 else '   couple k = %d' % point_couple(v, d, p).k))
print('distributed load, per p: q Difference_p/p! (q a clipped intensity, data error separate) and Ramp_p (rise with k = 1):')
n, far, dd = T(0.3, 1), T(0.2, 1), T(0.1, 1)
rise = T(0.5, 1)
for p in (1, 2, 3, 4):
    print('  p = %d  inside: diff k = %2d ramp k = %2d | beyond: diff k = %2d ramp k = %2d' % (
        p, diff_term(v, n, None, dd, p, True).k, ramp_term(rise, n, None, dd, p, True).k,
        diff_term(v, n, far, dd, p, False).k, ramp_term(rise, n, far, dd, p, False).k))
print('clipped intensity q = v1 + (v2 - v1)((c - la)/(lb - la)): local propagation (contract §3)')
print('field evaluation at s (g coefficients times state):')
s = d
for name, g in (('w: 1, s, s s/2/e, s s s/6/e', [one, s, ((s * s) / 2) / e, (((s * s) * s) / 6) / e]),
                ('th: 1, s/e, s s/2/e', [one, s / e, ((s * s) / 2) / e]), ('M: 1, s', [one, s]), ('V: 1', [one])):
    print('  %-30s k(g) = %s, product with the state k(g) + 1' % (name, [t.k for t in g]))
