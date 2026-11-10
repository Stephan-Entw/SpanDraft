"""Deterministic closure regressions. All mathematical expectations use independent rationals."""
import json
import math
from pathlib import Path
import sys
from fractions import Fraction as F
from unittest.mock import patch

import fpc
from fpc_chain import chain, constructed, neartau, underflow
from gen import case

NORMAL = 2.0 ** -1022
TINY = 2.0 ** -1074
MAX = sys.float_info.max
MODES = ('gradual', 'ftz', 'daz', 'ftz_daz')


def check_exponent(x, e):
    assert F(2) ** e <= x < F(2) ** (e + 1), (x, e)


def stored(x):
    return x == 0 or abs(x) >= NORMAL


def encloses(lo, x, hi):
    assert stored(lo) and stored(hi), (lo, hi)
    assert (lo == -math.inf or F(lo) <= x) and (hi == math.inf or x <= F(hi)), (lo, x, hi)


def direction():
    samples = (0., TINY, math.nextafter(NORMAL, 0.), NORMAL,
               math.nextafter(NORMAL, math.inf), MAX, math.inf)
    for sign in (-1, 1):
        for x in samples:
            x *= sign
            lo, hi = fpc.dn(x), fpc.up(x)
            if math.isfinite(x):
                encloses(lo, F(x), hi)
            else:
                assert stored(lo) and stored(hi) and (hi == x if x > 0 else lo == x)
    assert fpc.up(-NORMAL) == fpc.dn(NORMAL) == 0.
    encloses(fpc.dmul(NORMAL, 1.), F(NORMAL), fpc.umul(NORMAL, 1.))
    encloses(fpc.dsub(-NORMAL, 0.), -F(NORMAL), fpc.uadd(-NORMAL, 0.))
    for x, e in ((TINY, 1074), (MAX, -1023), (1., -1075), (-1., -1075),
                 (1., 1024), (-1., 1024), (1., -1022), (math.nextafter(1., 0.), -1022)):
        exact = F(x) * F(2) ** e
        encloses(fpc.directed_scale(x, e, False), exact, fpc.directed_scale(x, e, True))
        result = fpc.scale_by_power_of_two(x, e)
        assert result == (math.copysign(math.inf, x) if abs(exact) > F(MAX) else float(exact))


def exponent():
    for x in (TINY, math.nextafter(NORMAL, 0.), NORMAL, math.nextafter(1., 0.), 1.,
              math.nextafter(2., 0.), 2., math.nextafter(2., math.inf), MAX):
        check_exponent(F(x), fpc.power_of_two_exponent(x))


def gamma():
    for k in (0, 1, 4, 2 ** 27, 2 ** 52, 2 ** 53 - 1):
        g = fpc.gamma_upper(k)
        assert stored(g) and F(g) >= F(k, 2 ** 53 - k)
    try:
        fpc.gamma_upper(2 ** 53)
    except fpc.RangeError:
        return
    raise AssertionError('missing gamma range rejection')


def products_and_gate():
    for a, b in ((NORMAL, math.nextafter(1., 0.)), (NORMAL, 1.),
                 (NORMAL, math.nextafter(1., math.inf)), (NORMAL, NORMAL),
                 (MAX, math.nextafter(1., 0.)), (MAX, 1.),
                 (MAX, math.nextafter(1., math.inf)), (MAX, 2.)):
        expected = F(a) * F(b)
        try:
            value = fpc.stiffness_product(a, b)
        except fpc.RangeError:
            assert expected < F(NORMAL) or expected > F(MAX), (a, b)
        else:
            assert F(NORMAL) <= expected <= F(MAX) and value == float(expected), (a, b)
    for e, i in ((NORMAL, .5), (MAX, 2.)):
        model = case('invalid EI', 4., [(0., 'Pinned'), (4., 'Roller')],
                     pf=[(1., 1.)], E=e, I=i)
        result, solver = fpc.solve_case(model)
        assert result.get('cause') == 'NumericalRange' and solver is None, result
    for d in (NORMAL, 1., 100_000_000., MAX):
        limit = F(d) / 100_000_000
        mid = float(limit)
        threshold = fpc.threshold_down(d)
        assert stored(threshold) and F(threshold) <= limit
        successor = math.nextafter(threshold, math.inf) if threshold else NORMAL
        assert F(successor) > limit
        for bound in (0., NORMAL, math.nextafter(mid, 0.), mid, math.nextafter(mid, math.inf)):
            if stored(bound):
                assert (bound <= threshold) == (F(bound) <= limit)


def excitation():
    a = 2.0 ** -550
    for length, forces, couples, lines in (
            (4., [], [], [(0., a, a, a)]),
            (4., [(2., TINY)], [], []),
            (4., [], [(2., TINY)], []),
            (4., [(2., MAX), (2., -MAX), (3., TINY)], [], []),
            (4., [(2., NORMAL)], [(3., math.nextafter(NORMAL, 0.))], []),
            (4., [(2., 4.), (2., -4.)], [], [(0., 1., 1., -1.)]),
            (4., [(2., -0.)], [(2., 0.)], [(0., a, -0., 0.)])):
        target = sum(abs(F(v)) for _, v in forces)
        target += sum(abs(F(v)) / F(length) for _, v in couples)
        target += sum((abs(F(v)) + abs(F(w))) * (F(y) - F(x)) / 2 for x, y, v, w in lines)
        present, e, lo, hi = fpc.excitation(length, forces, couples, lines)
        assert present == bool(target)
        if target:
            check_exponent(target, e)
            encloses(lo, target / F(2) ** e, hi)


def loads():
    a = 2.0 ** -550
    supports = [(0., 'Pinned'), (4., 'Roller')]
    tiny_line = case('tiny line', 4., supports, q=[(0., a, a)], E=1., I=1.)
    tiny_moment = case('tiny moment', 4., supports, pm=[(2., NORMAL)], E=1., I=1.)
    zero = case('zero', 4., supports, pf=[(1., -0.)], E=1., I=1.)
    cancelled = case('cancelled', 4., supports, pf=[(1., 4.), (1., -4.)], E=1., I=1.)
    for mode in MODES:
        for c in (tiny_line, tiny_moment, zero, cancelled):
            result, solver = fpc.solve_case(c, mode=mode)
            assert (result['status'] == 'zero') == (c is zero), (mode, c['id'], result)
            if c is zero:
                assert solver.to_si(*solver.evaluate(1., 'R')) == ((0.,) * 4, (0.,) * 4)
                for _, _, value, bound, q, _ in solver.reactions():
                    assert solver.to_si_value(value, bound, q) == (0., 0.)
            if c is tiny_line or c is tiny_moment:
                assert result.get('cause') == 'NumericalRange', result


def residual():
    near = math.nextafter(NORMAL, 0.)
    for mode in MODES:
        fpc.MODE[0] = mode
        for rhs, terms in ((near, []), (NORMAL, []), (0., [(NORMAL, 0.75)]),
                           (NORMAL, [(NORMAL, 0.75)]), (NORMAL, [(NORMAL, 0.75)] * 4),
                           (1., [(1., 1.)]), (1., [(1., 1.), (2.0 ** -54, 1.)]),
                           (2., [(math.nextafter(1., 2.), math.nextafter(1., 2.))] * 2)):
            exact = F(rhs) - sum(F(a) * F(b) for a, b in terms)
            val, bound = fpc.residual(rhs, terms)
            assert stored(bound) and abs(F(val) - exact) <= F(bound), (mode, rhs, terms, val, bound)
    fpc.MODE[0] = 'gradual'


def functional_scaling():
    for mode in MODES:
        fpc.MODE[0] = mode
        solver = object.__new__(fpc.CS)
        solver.n = solver.nfull = 1
        solver.d = [0.]
        solver.nz, solver.dS = [[]], [[0.]]
        solver.free, solver.col = [0], {0: 0}
        solver.cs, solver.y = [-1024], [1.]
        solver.x = [fpc.fscale(1., -1024)]
        solver.v_iter = 0
        solver.adjoint = lambda rhs: (rhs, [0.])
        solver.verify([1.])
        assert solver.verified
        exact = F(2) ** -1024
        assert abs(F(solver.x[0]) - exact) <= F(solver.functional({0: 1.}))
    fpc.MODE[0] = 'gradual'


def full_cases():
    cases = [next(c for c in constructed() if c['id'].startswith(name))
             for name in ('zero-crossing', 'short-heavy', '6nm-pair')]
    cases += [next(c for c in underflow() if c['id'].startswith('U2|a=2^-330')), neartau()[0]]
    for c in cases:
        outcomes = [fpc.solve_case(c, mode=mode)[0] for mode in MODES]
        assert all(o['status'] == 'delivered' or o.get('cause') in ('Gate', 'NumericalRange')
                   for o in outcomes), (c['id'], outcomes)
    result, _ = fpc.solve_case(cases[-1], LUeps=(0.1,))
    assert result.get('cause') in ('Gate', 'NotVerified', 'NumericalRange'), result


def mutants():
    def skip_first_force(length, forces, couples, lines):
        return reference_excitation(length, forces[1:], couples, lines)

    def zero_loaded(forces, couples, lines):
        # The historical zero test formed the SI products before the sum.
        return not (sum(abs(v) for _, v in forces + couples)
                    + sum((abs(a) + abs(b)) / 2 * (hi - lo) for lo, hi, a, b in lines))

    reference_excitation, original_rounding = fpc.excitation, fpc.local_rounding
    for name, helper, mutant, check in (
            ('outward', 'outward', lambda v, upper: math.nextafter(v, math.inf if upper else -math.inf), direction),
            ('lambda', 'local_rounding', lambda r: 0. if abs(r) <= NORMAL else original_rounding(r), residual),
            ('exponent', 'power_of_two_exponent', lambda x: math.frexp(x)[1], exponent),
            ('contribution', 'excitation', skip_first_force, excitation),
            ('gamma', 'gamma_upper', lambda k: (k + 1) * 2.0 ** -53 if k else 0., gamma),
            ('unloaded', 'is_unloaded', zero_loaded, loads)):
        try:
            with patch.object(fpc, helper, mutant):
                check()
        except AssertionError:
            print('MUTANT detected', name)
        else:
            raise AssertionError('surviving mutant: ' + name)
        finally:
            fpc.MODE[0] = 'gradual'


def mechanical_mutants():
    """Existing B3, L1 and L2 fault classes on the new analytic formulas."""
    def test(name, helper, alteration, model):
        original = getattr(fpc, helper)
        clean = chain(model, old=False)
        assert clean['status'] == 'delivered' and not clean['formulation_mismatch']
        with patch.object(fpc, helper, lambda *args: alteration(original(*args), *args)):
            broken = chain(model, old=False)
        assert broken['formulation_mismatch'] or broken.get('E_viol'), (name, broken)
        print('MECHANICAL MUTANT detected', name)

    models = constructed()
    moment = case('couple', 4., [(0., 'Pinned'), (4., 'Roller')],
                  pm=[(1., 3.)], E=1., I=1.)
    ramp = next(c for c in models if c['id'].startswith('zero-crossing|'))
    test('B3', 'point_couple', lambda result, *_: -result, moment)
    test('L1', 'ramp_term', lambda result, r, n, far, d, p, inside:
         (result * 24) / 25 if p == 3 and not inside else result, ramp)
    test('L2', 'diff_term', lambda result, q, n, far, d, p, inside:
         (result * 24) / 20 if p == 4 else result, ramp)


def axial_baseline(export):
    """Check exact zeros and reaction presence on the current public validation cases."""
    root = Path(__file__).resolve().parents[3]
    fixtures = {p.stem: json.loads(p.read_text()) for p in (root / 'cases').glob('*.json')}
    results = json.loads(Path(export).read_text())
    assert fixtures and {r['caseId'] for r in results} == set(fixtures)
    n_samples = n_reactions = 0
    for result in results:
        ident = result['caseId']
        fixture = fixtures[ident]
        assert result['status'] == 'PASS' and result['samples'], ident
        expected = {s['position'] for s in fixture['supports'] if s['type'] in ('Fixed', 'Pinned')}
        actual = {r['position']: r['values']['Rx'] for r in result['reactions'] if 'Rx' in r['values']}
        assert set(actual) == expected and all(F(v) == 0 for v in actual.values()), (ident, actual)
        for sample in result['samples']:
            assert F(sample['values']['u']) == F(sample['values']['N']) == 0, (ident, sample)
        n_samples += len(result['samples'])
        n_reactions += len(actual)
    print(f'PASS axial baseline: {len(results)} cases, {n_samples} samples, {n_reactions} axial reactions')


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--axial-export':
        axial_baseline(sys.argv[2])
        raise SystemExit(0)
    for family in (direction, exponent, gamma, products_and_gate, excitation, loads, residual, functional_scaling, full_cases):
        family()
        print('PASS', family.__name__)
    mutants()
    mechanical_mutants()
