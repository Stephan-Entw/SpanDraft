"""Serialization and numerical processing of reference results; no beam assembly."""
import hashlib
import json
import platform
from datetime import datetime, timezone
from importlib.metadata import version
from pathlib import Path

import numpy as np
import sympy as sp

ROOT = Path(__file__).resolve().parents[2]
ADAPTER_VERSION = '1'
VERSIONS = {'indeterminatebeam': '2.4.0', 'pycba': '1.0.2'}
TOLERANCES = {'force': (1e-5, 1e-6), 'moment': (1e-5, 1e-6),
              'displacement': (1e-10, 1e-6), 'rotation': (1e-10, 1e-6),
              'position': (1e-8, 1e-7)}
QUANTITIES = ['Rx', 'Ry', 'Rm', 'u', 'w', 'theta', 'N', 'V', 'M']


def check_environment():
    if platform.python_implementation() != 'CPython' or platform.python_version() != '3.9.6':
        raise RuntimeError('Reference generation requires CPython 3.9.6')
    for package, expected in VERSIONS.items():
        if version(package) != expected:
            raise RuntimeError(f'{package}: expected {expected}, found {version(package)}')
    for line in (ROOT/'validation/python/requirements.txt').read_text().splitlines():
        if line and not line[0].isspace() and not line.startswith('#'):
            package, expected=line.rstrip(' \\').split('==')
            if version(package)!=expected:
                raise RuntimeError(f'Lock mismatch: {package} expected {expected}, found {version(package)}')


def load_case(path):
    case = json.loads(path.read_text())
    case['_hash'] = hashlib.sha256(path.read_bytes()).hexdigest()
    return case


def events(case):
    return sorted(set([0.0, case['length']] + [s['position'] for s in case['supports']] +
                      [p['position'] for p in case['pointForces'] + case['pointMoments']] +
                      [x for q in case['udls'] for x in [q['start'], q['end']]]))


def result(case, solver, supported, options, mapping, source):
    adapter_hash=hashlib.sha256()
    for path in sorted((ROOT/'validation/python').glob('*.py')):
        adapter_hash.update(path.name.encode())
        adapter_hash.update(path.read_bytes())
    return dict(schemaVersion=1, caseVersion=case['caseVersion'], caseId=case['caseId'],
                inputSha256=case['_hash'], solver=solver, status='PASS',
                provenance=dict(package=solver, version=VERSIONS.get(solver, '1'),
                                python=platform.python_version(),
                                generatedAt=datetime.now(timezone.utc).isoformat(),
                                theory='Euler-Bernoulli; constant E, A, I; linear; small displacements',
                                signMapping=mapping, options=options, adapterVersion=ADAPTER_VERSION,
                                requirementsSha256=hashlib.sha256((ROOT/'validation/python/requirements.txt').read_bytes()).hexdigest(),
                                adapterSourcesSha256=adapter_hash.hexdigest(),
                                source=source),
                availability={q: ('supported' if q in supported else 'notSupported') for q in QUANTITIES},
                reactions=[], samples=[], extrema={})


def write_result(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    # Never overwrite, including candidate directories. Deliberate promotion is separate.
    with path.open('x') as stream:
        json.dump(data, stream, indent=2, allow_nan=False)
        stream.write('\n')


def select_branch(expr, x, midpoint):
    """Select the solver-generated expression's open-interval branch, retain x."""
    expr = sp.sympify(expr)
    if isinstance(expr, sp.Piecewise):
        for value, condition in expr.args:
            if bool(condition.subs(x, midpoint)):
                return select_branch(value, x, midpoint)
        raise ValueError('No expression branch at interval midpoint')
    if not expr.args:
        return expr
    return expr.func(*(select_branch(arg, x, midpoint) for arg in expr.args))


def polynomial_pieces(case, expressions, x):
    """Algebraic coordinate change of already computed reference expressions."""
    t = sp.Symbol('t')
    pieces = []
    boundaries = events(case)
    for a, b in zip(boundaries, boundaries[1:]):
        fields = {}
        for name, expr in expressions.items():
            branch = select_branch(expr, x, (a + b) / 2)
            poly = sp.Poly(sp.expand(branch.subs(x, a + (b - a) * t)), t)
            fields[name] = np.polynomial.Polynomial([float(c) for c in reversed(poly.all_coeffs())])
        pieces.append((a, b, fields))
    return pieces


def piece_index(pieces, x, side):
    boundaries = np.asarray([p[0] for p in pieces] + [pieces[-1][1]])
    index = int(np.searchsorted(boundaries, x, side='left' if side == 'Left' else 'right')) - 1
    return max(0, min(len(pieces) - 1, index))


def polynomial_samples(case, pieces):
    rows = []
    for evaluation in case['evaluations']:
        x, side = evaluation['position'], evaluation['side']
        a, b, fields = pieces[piece_index(pieces, x, side)]
        rows.append(dict(position=x, side=side,
                         values={q: float(poly((x-a)/(b-a))) for q, poly in fields.items()}))
    return rows


def extrema_from_candidates(candidates, plateaus):
    """Retain physical multiplicity, with a floating-point roundoff tie bound."""
    scale = max(abs(c['value']) for c in candidates)
    tie = 64 * np.finfo(float).eps * scale
    out = {}
    for suffix, operation in [('Min', min), ('Max', max)]:
        target = operation(c['value'] for c in candidates)
        locations = [dict(position=c['position'], side=c['side']) for c in candidates
                     if abs(c['value']-target) <= tie]
        flats = [dict(start=a, end=b) for a, b, v in plateaus if abs(v-target) <= tie]
        # Remove locations inside plateaus; plateau boundary sides remain explicit
        # through inward-limit membership in the .NET comparator.
        def inside_plateau(loc, a, b):
            return (a < loc['position'] < b or
                    (loc['position']==a and loc['side']=='Right') or
                    (loc['position']==b and loc['side']=='Left'))
        locations = [loc for loc in locations if not any(inside_plateau(loc,a,b)
                     for a,b,v in plateaus if abs(v-target) <= tie)]
        dedup = {(loc['position'], loc['side']): loc for loc in locations}
        out[suffix] = dict(value=target, locations=list(dedup.values()), plateaus=flats)
    return out


def polynomial_extrema(pieces):
    out = {}
    for q in ['w', 'V', 'M']:
        candidates, plateaus = [], []
        for a, b, fields in pieces:
            poly = fields[q]
            for t, side in [(0.0, 'Right'), (1.0, 'Left')]:
                candidates.append(dict(value=float(poly(t)), position=a+(b-a)*t, side=side))
            derivative = poly.deriv().trim()
            if np.all(derivative.coef == 0):
                plateaus.append((a, b, float(poly(0))))
            else:
                for root in derivative.roots():
                    if abs(complex(root).imag) <= 1e-12 and 0 < float(np.real(root)) < 1:
                        t = float(np.real(root))
                        candidates.append(dict(value=float(poly(t)), position=a+(b-a)*t, side=None))
        out.update({q+suffix: value for suffix,value in extrema_from_candidates(candidates, plateaus).items()})
    return out
