"""Read-only extraction from IndeterminateBeam 2.4.0 generated expressions."""
import sympy as sp
from indeterminatebeam import Beam, Support, PointLoadV, PointTorque, UDLV
from common import result, polynomial_pieces, polynomial_samples, polynomial_extrema


def generate(case):
    beam = Beam(case['length'], E=case['e'], A=case['a'], I=case['i'], G=float('inf'))
    # The explicit package defaults are SI; require them rather than silently converting.
    expected={'length':'m','force':'N','moment':'N.m','distributed':'N/m','stiffness':'N/m',
              'A':'m2','G':'Pa','E':'Pa','I':'m4','deflection':'m'}
    if beam._units != expected:
        raise RuntimeError('Unexpected IndeterminateBeam unit representation')
    flags={'Fixed':(1,1,1),'Pinned':(1,1,0),'Roller':(0,1,0)}
    beam.add_supports(*(Support(s['position'],flags[s['type']]) for s in case['supports']))
    beam.add_loads(*(list(PointLoadV(p['value'],p['position']) for p in case['pointForces']) +
                     list(PointTorque(p['value'],p['position']) for p in case['pointMoments']) +
                     list(UDLV(q['value'],(q['start'],q['end'])) for q in case['udls'])))
    beam.analyse()
    expressions=dict(N=sp.sympify(beam._normal_forces),V=sp.sympify(beam._shear_forces),
                     M=sp.sympify(beam._bending_moments),w=sp.sympify(beam._deflection_equation))
    symbols=set().union(*(expr.free_symbols for expr in expressions.values()))
    if not symbols: x=sp.Symbol('x')
    elif len(symbols)==1: x=next(iter(symbols))
    else: raise RuntimeError('Unexpected symbols in solved reference expressions')
    expressions['theta']=sp.diff(expressions['w'],x)
    pieces=polynomial_pieces(case,expressions,x)
    data=result(case,'indeterminatebeam',list(expressions)+['Rx','Ry','Rm'],
                dict(G='infinity', units=expected, extraction='generated piecewise expressions; exact interval branches'),
                'Inputs, Rx/Ry/Rm, w/theta, tension-positive N, V=dM/dx and sagging M: identity',
                'https://pypi.org/project/indeterminatebeam/2.4.0/')
    for support in case['supports']:
        rx,ry,rm=beam.get_reaction(support['position'])
        values={'Ry':float(ry)}
        if support['type'] in ['Fixed','Pinned']: values['Rx']=float(rx)
        if support['type']=='Fixed': values['Rm']=float(rm)
        data['reactions'].append(dict(position=support['position'],values=values))
    data['samples']=polynomial_samples(case,pieces)
    data['extrema']=polynomial_extrema(pieces)
    if case['caseId']=='V14':
        # Observed upstream limitation, not a replacement reference calculation:
        # native linsolve uses floats and analyse substitutes float(ans). All
        # deformation/action fields contain roundoff residue. Their computed
        # stationary locations cannot establish the physical zero-field plateau.
        # Keep every native value/location, and leave this acceptance item open.
        data['status']='PARTIAL'
        for ext in data['extrema'].values():
            ext['locationStatus']='notComparable'
            ext['locationNote']='V14 native residual-only field: physical extremum location set is not established by IndeterminateBeam 2.4.0. Raw locations retained; no zero/plateau substitution.'
    return data
