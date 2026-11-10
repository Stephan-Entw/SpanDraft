"""Round 4, phase 1 follow-up: why does Householder QR (with refinement) fail where GEPP works?
For the models where the QR solution was rejected (results/state-factor-*.log), the scaled matrix
S is characterised by its normwise condition kappa_inf(S) = ||S^-1|| ||S||, Skeel's condition
cond(S) = || |S^-1| |S| || and the componentwise condition at the solution
cond(S, y) = || |S^-1| |S| |y| || / ||y|| (all infinity norm, S^-1 in exact rational arithmetic),
and by the ratio of the smallest to the largest nonzero entry per column (grading).
Usage: python3 state/cond_check.py"""
import glob, json, math, os, random, sys
from fractions import Fraction as Fr
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from cases import all_cases
from factor_study import BuildOnly, stress_models
from gen import gen_s, gen_f
from state import StateSolver

HERE = os.path.dirname(os.path.abspath(__file__))


def exact_inverse(S):
    n = len(S)
    M = [[Fr(v) for v in row] + [Fr(int(i == j)) for j in range(n)] for i, row in enumerate(S)]
    for i in range(n):
        p = next(r for r in range(i, n) if M[r][i] != 0)
        M[i], M[p] = M[p], M[i]
        piv = M[i][i]
        M[i] = [v / piv for v in M[i]]
        for r in range(n):
            if r != i and M[r][i] != 0:
                f = M[r][i]
                M[r] = [a - f * b for a, b in zip(M[r], M[i])]
    return [row[n:] for row in M]


def conds(c):
    m = StateSolver(c)
    S = m.S
    n = m.n
    Si = exact_inverse(S)
    absS = [[abs(Fr(v)) for v in row] for row in S]
    absSi = [[abs(v) for v in row] for row in Si]
    normS = max(sum(r) for r in absS)
    normSi = max(sum(r) for r in absSi)
    P = [[sum(absSi[i][k] * absS[k][j] for k in range(n)) for j in range(n)] for i in range(n)]
    skeel = max(sum(r) for r in P)
    y = [abs(Fr(v)) for v in m.y]
    cy = max(sum(P[i][j] * y[j] for j in range(n)) for i in range(n)) / max(y)
    grad = min(min(abs(S[i][j]) for i in range(n) if S[i][j] != 0) / max(abs(S[i][j]) for i in range(n)) for j in range(n))
    return float(normS * normSi), float(skeel), float(cy), grad


if __name__ == '__main__':
    bad, good = [], []
    for f in glob.glob(os.path.join(HERE, '..', 'results', 'state-factor-*.log')):
        for line in open(f):
            if line.startswith('DATA '):
                r = json.loads(line[5:])
                if 'qr' in r and r['qr'].get('status') == 'rejected' and r['lu'].get('status') == 'delivered':
                    bad.append(r['id'])
                elif 'qr' in r and r['qr'].get('status') == 'delivered' and len(good) < 400:
                    good.append(r['id'])
    want = set(bad) | set(good[::40])
    found = {}
    for _, c in all_cases():
        if c['id'] in want:
            found[c['id']] = c
    for kind, N in (('s', 1500), ('f', 1000)):
        for r0 in (1e-2, 1e-4, 1e-6, 1e-9, 1e-11):
            seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
            rng = random.Random(seed)
            for k in range(N):
                c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
                if c['id'] in want:
                    found[c['id']] = c
    for label, ids in (('QR rejected, GEPP delivered', bad), ('QR delivered (sample)', good[::40])):
        print(label)
        for i in ids:
            if i not in found:
                print('  %-24s (not regenerated)' % i)
                continue
            k, sk, cy, g = conds(found[i])
            print('  %-24s kappa_inf %.1e  Skeel cond %.1e  cond(S,y) %.1e  smallest/largest entry per column %.1e  u*kappa %.1e' % (
                i, k, sk, cy, g, k * 2 ** -53))
