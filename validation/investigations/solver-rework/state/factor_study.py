"""Round 4, phase 1: factorization of the concrete multiple-shooting system.

1.1 Structure (no solve needed): for every model the scaled matrix S (nondimensionalised and
    Ruiz-equilibrated exactly as in state.py) is checked for
    - coupling: every row references unknowns of at most two adjacent mechanical stations,
    - rows and unknowns per station (the offset between row block and column block),
    - structural lower/upper bandwidth in station order,
    - no column outside the band (the "bordered" corner columns of coupled boundary conditions,
      the structure of Wright 1993, cannot occur when every condition is local).
1.2 Factorization comparison on the identical scaled system:
    LU: GEPP as in state.py (Python, first maximum as pivot), growth over all elimination stages
        rho = max_k max|a_ij^(k)| / max|a_ij|, rho_U = max|U| / max|S|, row interchanges, largest
        multiplier (<= 1 by construction), multiplier distance below the diagonal, band of U;
    QR: Householder QR (numpy.linalg.qr, LAPACK dgeqrf) as robust reference.
    Both with the same primary refinement (state.py rule), the same adjoint rule ('beta') and the
    same indicator; recorded per model: actual error (mixed measure, stations and reactions,
    exact reference), gate (station, reaction, piece variant C), residual (omega), refinement
    steps, status.
Model sets:
    targeted      the 840 targeted cases (cases.py)
    random K      every K-th model of the ten random classes (gen.py, same seeds as state-rand)
    stress NMAX   literature-guided families up to NMAX spans (full comparison with exact reference);
    stressgrowth NMAX  the same families, structure and GEPP growth only (no exact reference,
                  for the large models whose exact reference is too slow in Python);
                  families: Gerber chains (many hinges), alternating very short
                  sections, EI jumps up to 1e8, beams on very soft springs only (near mechanism),
                  very stiff spring clusters, hinge chains on springs, combinations; 2..40 spans
    adversarial   random models with up to 40 mechanical stations whose parameters are moved by
                  a hill climb that maximises the GEPP growth rho (no exact reference needed for
                  the search); the 3 largest-rho models per seed are then solved and compared.
Usage: python3 state/factor_study.py targeted | random K | stress NMAX | stressgrowth NMAX | adversarial SEEDS STEPS"""
import json, math, os, random, sys, time
from fractions import Fraction as Fr
import numpy as np
from scipy.linalg import solve_triangular
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import transfer_ref as T
from cases import all_cases
from gen import case, gen_s, gen_f, INF
from gate_study import evaluate, reaction_error
from kinematics import is_mechanism
from metrics import scales, Q
from state import StateSolver, U, pow2

TAU = 1e-8


class BuildOnly(StateSolver):
    """Builds and equilibrates exactly like StateSolver, without factorization."""
    def solve(s, max_iter):
        A, b, n = s.A, s.b, s.n
        rs, cs = [1.] * n, [1.] * n
        for _ in range(8):
            for i in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for j in range(n))
                if m > 0:
                    rs[i] = rs[i] / pow2(math.sqrt(m))
            for j in range(n):
                m = max(abs(rs[i] * A[i][j] * cs[j]) for i in range(n))
                if m > 0:
                    cs[j] = cs[j] / pow2(math.sqrt(m))
        s.rs, s.cs = rs, cs
        s.S = [[rs[i] * A[i][j] * cs[j] for j in range(n)] for i in range(n)]
        s.sb = [rs[i] * b[i] for i in range(n)]


def structure(m):
    """Coupling and band structure of the scaled matrix in station order."""
    S = np.array(m.S)
    n = m.n
    stat = [k // 4 for k in m.free]                  # station of every unknown
    lo = up = 0
    maxspan = 0
    for i in range(n):
        nz = np.nonzero(S[i])[0]
        sts = sorted({stat[j] for j in nz})
        maxspan = max(maxspan, sts[-1] - sts[0])
        lo = max(lo, i - nz.min())
        up = max(up, nz.max() - i)
    unk = {}
    for st in stat:
        unk[st] = unk.get(st, 0) + 1
    return dict(n=n, stations=m.N + 1, lower=int(lo), upper=int(up), station_span=int(maxspan),
                max_unknowns_per_station=max(unk.values()))


def gepp(S):
    """GEPP like state.py with growth over all stages."""
    A = np.array(S, dtype=float)
    n = len(A)
    amax = np.abs(A).max()
    grow = amax
    swaps = 0
    mdist = 0
    mmax = 0.
    for k in range(n):
        p = k + int(np.argmax(np.abs(A[k:, k])))
        if A[p, k] == 0:
            return None
        if p != k:
            A[[k, p]] = A[[p, k]]
            swaps += 1
        l = A[k + 1:, k] / A[k, k]
        nzl = np.nonzero(l)[0]
        if nzl.size:
            mdist = max(mdist, int(nzl.max()) + 1)
            mmax = max(mmax, float(np.abs(l).max()))
        A[k + 1:, k] = l
        A[k + 1:, k + 1:] -= np.outer(l, A[k, k + 1:])
        if k + 1 < n:
            grow = max(grow, np.abs(A[k + 1:, k + 1:]).max())
    Um = np.triu(A)
    ub = max((int(np.nonzero(Um[i])[0].max()) - i for i in range(n)), default=0)
    return dict(rho=float(grow / amax), rho_U=float(np.abs(Um).max() / amax), swaps=swaps, mult_max=mmax,
                mult_dist=mdist, U_band=ub)


def primary(m, solve):
    """Primary solution and refinement with the given solver (rule of state.py)."""
    n = m.n
    y = solve(m.sb)
    prev = math.inf
    m.iterations = 0
    for _ in range(m.max_iter):
        r, _ = m.residual(y)
        dy = solve(r)
        y = [a + b for a, b in zip(y, dy)]
        m.iterations += 1
        nd, ny = max(map(abs, dy)), max(map(abs, y))
        if not (nd == nd) or nd <= 2 * U * ny or nd > 0.5 * prev:
            break
        prev = nd
    m.y = y
    m.res, m.reserr = m.residual(y)
    x = [m.cs[i] * y[i] for i in range(n)]
    m.x = [0.] * m.nfull
    for i, k in enumerate(m.free):
        m.x[k] = x[i]
    den = [sum(abs(m.S[i][j] * y[j]) for j in m.nz[i]) + abs(m.sb[i]) for i in range(n)]
    m.omega = max([abs(m.res[i]) / den[i] for i in range(n) if den[i] > 0] + [0.])


def use_qr(m):
    S = np.array(m.S)
    Qm, R = np.linalg.qr(S)
    m.lusolve = lambda v: list(solve_triangular(R, Qm.T @ np.array(v, dtype=float)))
    m.lusolve_transposed = lambda c: list(Qm @ solve_triangular(R, np.array(c, dtype=float), trans='T'))
    m.rdiag = float(np.abs(np.diag(R)).min() / np.abs(np.diag(R)).max())


def run(c, ex, method):
    m = StateSolver(c, adj_refine='beta')
    if method == 'qr':
        use_qr(m)
        primary(m, m.lusolve)
    m.adj_solves = m.adj_steps = 0
    m.indicator()
    exact = [[ex.state(Fr(float(x)), sd)[k] for k in Q] for x, sd in c['pts']]
    fexact = [[float(v) for v in r] for r in exact]
    sc, mix = scales(c, fexact)
    o = evaluate(c, m, exact, fexact)
    err = max(o['err'], reaction_error(c, m, ex, mix))
    gate = max(o['station'], o['react'], o['C'])
    fin = all(math.isfinite(v) for v in m.x) and math.isfinite(gate)
    return dict(err=err, gate=gate, omega=m.omega, steps=m.iterations, adj_steps=m.adj_steps / max(m.adj_solves, 1),
                status='delivered' if fin and gate <= TAU else 'rejected')


def study(c, compare=True):
    b = BuildOnly(c)
    st = structure(b)
    g = gepp(b.S)
    out = dict(id=c['id'], struct=st, gepp=g)
    if g is None:
        out['status'] = 'zero pivot'
        return out
    if compare:
        ex = T.Transfer(c)
        for meth in ('lu', 'qr'):
            try:
                out[meth] = run(c, ex, meth)
            except ZeroDivisionError:
                out[meth] = dict(status='zero pivot')
    return out


# ---------------------------------------------------------------------------------------------
# stress families
E0, I0 = 210e9, 8e-6
EI0 = E0 * I0


def stress_models(nmax=40):
    out = []
    for N in (n for n in (2, 5, 10, 20, 40) if n <= nmax):
        for eir in (1, 1e-4, 1e-8):
            for short in (1, 1e-6, 1e-11):
                L = 4. * N
                # Gerber chain: supports at span ends, hinge next to every second inner support,
                # every second span split by a very short section of alternating EI
                sup, hinges, ei = [(0., 'Fixed')], [], []
                for k in range(1, N + 1):
                    sup.append((4. * k, 'Roller'))
                    if k < N and k % 2 == 1:
                        hinges.append(4. * k + 0.8)
                    if short != 1:
                        ei.append((4. * k - 2., EI0 * eir))
                        ei.append((4. * k - 2. + 4. * short, EI0))
                    elif eir != 1:
                        ei.append((4. * k - 2., EI0 * eir))
                ei = sorted({x: v for x, v in ei if 0 < x < L}.items())
                out.append(case(f'gerber|{N}|{eir}|{short}', L, sup, hinges=hinges, ei=ei,
                                pf=[(4. * k - 1.3, -1e4) for k in range(1, N + 1)], q=[(0., L, -2e3)], E=E0, I=I0))
        for kexp in (-12, -6, 3, 15):
            L = 4. * N
            k = 10. ** kexp * EI0 / 4. ** 3
            spr = [(4. * j, k, 0, False) for j in range(N + 1)]
            out.append(case(f'nurfedern|{N}|{kexp}', L, [], springs=spr, pf=[(4. * j + 1.7, -1e4) for j in range(N)],
                            q=[(0., L, -2e3)], E=E0, I=I0))
            # hinge chain: every inner station is a hinge with a spring next to it
            spr = [(0., k, 0, False), (L, k, 0, False)] + [(4. * j + 4e-9, k, 0, False) for j in range(1, N)]
            out.append(case(f'gelenkkette|{N}|{kexp}', L, [(0., 'Pinned')], springs=spr[1:], hinges=[4. * j for j in range(1, N)],
                            pf=[(4. * j + 1.7, -1e4) for j in range(N)], E=E0, I=I0))
            # stiff/soft clusters with rotational springs at distances L/1e9 around every support
            sup = [(0., 'Pinned'), (L, 'Roller')]
            spr = []
            for j in range(1, N):
                for t in range(3):
                    spr.append((4. * j + t * 4e-9, k, k * 16., False))
            out.append(case(f'cluster|{N}|{kexp}', L, sup, springs=spr, pf=[(4. * j + 1.7, -1e4) for j in range(N)],
                            q=[(0., L, -2e3)], E=E0, I=I0))
            # combination: hinges, EI jumps 1e8, soft springs, short sections
            sup = [(0., 'Fixed'), (L, 'Fixed')]
            hinges = [4. * j for j in range(1, N, 2)]
            spr = [(4. * j + 4e-11, k, 0, False) for j in range(1, N)]
            ei = sorted({4. * j + 2.: EI0 * (1e-8 if j % 2 else 1.) for j in range(N)}.items())
            out.append(case(f'kombi|{N}|{kexp}', L, sup, springs=spr, hinges=hinges, ei=ei,
                            pf=[(4. * j + 1.7, -1e4) for j in range(N)], lin=[(1., 3., 5e3, -5e3)], E=E0, I=I0))
    return [c for c in out if not is_mechanism(c)]


# ---------------------------------------------------------------------------------------------
# adversarial search for GEPP growth
def adv_model(p, id):
    """Model from a parameter vector p (dict of lists); positions are kept sorted and distinct."""
    L = 40.
    xs = sorted(set(min(L, max(0., x)) for x in p['x']))
    sup, spr, hinges, ei = [], [], [], []
    for x, kind, ke, kte, eie in zip(xs, p['kind'], p['ke'], p['kte'], p['eie']):
        k = 10. ** ke * EI0 / 64.
        kt = 10. ** kte * EI0 / 4.
        if kind == 0:
            sup.append((x, 'Roller'))
        elif kind == 1:
            sup.append((x, 'Fixed'))
        elif kind == 2 and 0 < x < L:
            hinges.append(x)
        elif kind == 3:
            spr.append((x, k, kt, False))
        else:
            spr.append((x, k, 0, False))
        if 0 < x < L:
            ei.append((x, EI0 * 10. ** eie))
    if not sup and not spr:
        spr.append((0., EI0, 0, False))
    hinges = [h for h in hinges if not any(a == h and t == 'Fixed' for a, t in sup)]
    ei = [(x, v) for x, v in ei if x not in hinges]
    return case(id, L, sup, springs=spr, hinges=hinges, ei=ei, pf=[(L * .37, -1e4), (L * .81, -1e4)], q=[(0., L, -2e3)],
                E=E0, I=I0)


def adversarial(seed, steps):
    rng = random.Random(seed)
    nst = rng.randint(8, 40)
    p = dict(x=sorted(rng.uniform(0, 40) for _ in range(nst)), kind=[rng.randint(0, 4) for _ in range(nst)],
             ke=[rng.uniform(-12, 15) for _ in range(nst)], kte=[rng.uniform(-12, 15) for _ in range(nst)],
             eie=[rng.uniform(-8, 0) for _ in range(nst)])

    def score(p):
        c = adv_model(p, 'x')
        if is_mechanism(c):
            return -1., None
        b = BuildOnly(c)
        g = gepp(b.S)
        return (math.inf if g is None else g['rho']), c

    best, best_c = score(p)
    for it in range(steps):
        q = {k: v[:] for k, v in p.items()}
        for _ in range(rng.randint(1, 3)):
            key = rng.choice(['x', 'kind', 'ke', 'kte', 'eie', 'xclose'])
            i = rng.randrange(nst)
            if key == 'x':
                q['x'][i] = rng.uniform(0, 40)
            elif key == 'xclose':      # move a station extremely close to its neighbour
                j = min(nst - 1, i + 1)
                q['x'][i] = q['x'][j] - 40. * 10. ** rng.uniform(-12, -2)
            elif key == 'kind':
                q['kind'][i] = rng.randint(0, 4)
            else:
                q[key][i] = rng.uniform(-12, 15) if key != 'eie' else rng.uniform(-8, 0)
        s, c = score(q)
        if s == math.inf:
            continue
        if c is not None and s >= best:
            best, p, best_c = s, q, c
    if best_c is None:
        return None, best
    best_c['id'] = f'adv|{seed}|{best:.3g}'
    return best_c, best


if __name__ == '__main__':
    t0 = time.time()
    arg = sys.argv[1:]
    if arg[0] == 'targeted':
        cases, name = [c for _, c in all_cases()], 'targeted'
    elif arg[0] == 'random':
        K = int(arg[1])
        cases = []
        for kind, N in (('s', 1500), ('f', 1000)):
            for r0 in (1e-2, 1e-4, 1e-6, 1e-9, 1e-11):
                seed = int(r0 ** -1) % 9973 + 11 if kind == 's' else 4242 + int(1 / r0) % 991
                rng = random.Random(seed)
                for k in range(N):
                    c = (gen_s if kind == 's' else gen_f)(rng, r0, f'{kind}|{r0}|{k}')
                    if k % K == 0:
                        cases.append(c)
        name = f'random 1/{K}'
    elif arg[0] == 'stress':
        cases, name = stress_models(int(arg[1])), 'stress'
    elif arg[0] == 'stressgrowth':
        cases, name = stress_models(int(arg[1])), 'stress (structure and growth only)'
    else:
        seeds, steps = int(arg[1]), int(arg[2])
        cases = []
        for sd in range(seeds):
            c, s = adversarial(1000 + sd, steps)
            print('search seed %d: largest rho %.3g' % (1000 + sd, s), flush=True)
            if c is not None:
                cases.append(c)
        name = 'adversarial'
    n = 0
    for c in cases:
        if is_mechanism(c):
            continue
        try:
            o = study(c, compare=(name != 'stress (structure and growth only)'))
        except ZeroDivisionError:
            o = dict(id=c['id'], status='zero pivot in build')
        o['cls'] = name
        n += 1
        print('DATA ' + json.dumps(o), flush=True)
    print('%s: models %d | %.0f s' % (name, n, time.time() - t0))
