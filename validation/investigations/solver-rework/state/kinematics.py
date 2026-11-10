"""Exact mechanism check on the kinematic model of the rigid bodies between moment releases.

Each body between hinges has the rigid motion w = a + b*x (exact rationals of the stored doubles).
Constraint rows: a translational restraint at x (rigid support or spring with 0 < k < inf) acts in
its direction, a + b*x = 0; a rotational restraint (clamp or rotational spring) b = 0; a hinge
keeps w continuous between its two bodies. A spring is no rigid constraint in the mechanics: here it
only removes a rigid-body mode through positive elastic energy, independent of the value k > 0.
The structure is a mechanism if the rank is below 2 * bodies. Only this small matrix is reduced
(fraction-free Bareiss on integers); EI and stiffness values never enter.
"""
from fractions import Fraction as Fr
from math import lcm


def rows_of(c):
    L = Fr(float(c['L']))
    hs = sorted(set(Fr(float(h)) for h in c.get('hinges', [])))
    cuts = [Fr(0)] + hs + [L]
    bodies = len(cuts) - 1
    res = [(Fr(float(a)), False) for a, t in c['sup']] + [(Fr(float(a)), True) for a, t in c['sup'] if t == 'Fixed']
    for x, kw, kt, _ in c.get('springs', []):
        if kw == 'inf' or kw > 0:
            res.append((Fr(float(x)), False))
        if kt == 'inf' or kt > 0:
            res.append((Fr(float(x)), True))
    rows = []
    for x, rot in res:
        for i in range(bodies):
            if cuts[i] <= x <= cuts[i + 1]:       # a restraint exactly at a hinge holds both bodies
                r = [Fr(0)] * (2 * bodies)
                if rot:
                    r[2 * i + 1] = Fr(1)
                else:
                    r[2 * i], r[2 * i + 1] = Fr(1), x
                rows.append(r)
    for i in range(bodies - 1):
        h = cuts[i + 1]
        r = [Fr(0)] * (2 * bodies)
        r[2 * i], r[2 * i + 1], r[2 * i + 2], r[2 * i + 3] = Fr(1), h, Fr(-1), -h
        rows.append(r)
    return rows, 2 * bodies


def rank(rows, n):
    """Bareiss elimination on integer rows (each row scaled by the lcm of its denominators)."""
    M = []
    for r in rows:
        m = lcm(*[v.denominator for v in r]) if r else 1
        M.append([int(v * m) for v in r])
    rk, prev = 0, 1
    for col in range(n):
        p = next((i for i in range(rk, len(M)) if M[i][col] != 0), None)
        if p is None:
            continue
        M[rk], M[p] = M[p], M[rk]
        for i in range(rk + 1, len(M)):
            M[i] = [(M[rk][col] * M[i][j] - M[i][col] * M[rk][j]) // prev for j in range(n)]
        prev = M[rk][col]
        rk += 1
    return rk


def is_mechanism(c):
    rows, n = rows_of(c)
    return rank(rows, n) < n
