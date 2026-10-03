"""Inspect V14's native reference fields, without reconstructing beam mechanics.

Separate from the reference adapters: this read-only investigation neither
changes their provenance hashes nor writes/promotes any golden reference.
"""
import hashlib
import json
import os
import sys
from pathlib import Path

os.environ.setdefault('MPLCONFIGDIR', '/tmp/spandraft-validation-matplotlib')
os.environ.setdefault('MPLBACKEND', 'Agg')
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'python'))

import sympy as sp
from indeterminatebeam import Beam, Support, PointLoadV
from common import ROOT, check_environment, events, load_case, select_branch
import indeterminatebeam_adapter


def main():
    check_environment()
    case = load_case(ROOT / 'validation/cases/V14.json')
    beam = Beam(case['length'], E=case['e'], A=case['a'], I=case['i'], G=float('inf'))
    flags = {'Pinned': (1, 1, 0), 'Roller': (0, 1, 0)}
    beam.add_supports(*(Support(s['position'], flags[s['type']]) for s in case['supports']))
    beam.add_loads(*(PointLoadV(p['value'], p['position']) for p in case['pointForces']))
    beam.analyse()
    expressions = {q: sp.sympify(getattr(beam, attribute)) for q, attribute in
                   [('N', '_normal_forces'), ('V', '_shear_forces'),
                    ('M', '_bending_moments'), ('w', '_deflection_equation')]}
    x, = set().union(*(expr.free_symbols for expr in expressions.values()))
    fields = {}
    boundaries = events(case)
    for q, expression in expressions.items():
        branches = []
        for a, b in zip(boundaries, boundaries[1:]):
            branch = sp.expand(select_branch(expression, x, (a + b) / 2))
            branches.append(dict(start=a, end=b, expression=str(branch),
                                 derivative=str(sp.diff(branch, x)),
                                 identicallyZero=branch == 0,
                                 identicallyConstant=sp.diff(branch, x) == 0))
        fields[q] = dict(nativeExpression=str(expression), branches=branches)
    reproduced = indeterminatebeam_adapter.generate(case)
    golden = json.loads((ROOT / 'validation/references/V14.indeterminatebeam.json').read_text())
    # Include the entire golden payload except its generation timestamp.
    for data in (reproduced, golden):
        data['provenance'].pop('generatedAt')
    assert reproduced == golden, 'V14 reference did not reproduce exactly'
    source = Path(sys.modules[Beam.__module__].__file__)
    report = dict(caseId='V14', inputSha256=case['_hash'], version='2.4.0',
                  nativeSourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                  nativeReactions=beam._reactions, fields=fields,
                  referenceReproducedExactly=True, rawExtrema=reproduced['extrema'],
                  physicalZeroPlateauEstablished=False,
                  conclusion='N is exactly zero. w/M are nonconstant residual polynomials; '
                  'V has two unequal nonzero constant residual branches. No exact physical '
                  'zero-field extremum set is established for w/V/M. Only their six '
                  'position comparisons are NOT APPLICABLE; values remain mandatory.')
    target = ROOT / 'validation/results/v14-native-fields.json'
    target.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps(fields, indent=2))
    print('V14 reference reproduced exactly; evidence:', target)


if __name__ == '__main__':
    main()
