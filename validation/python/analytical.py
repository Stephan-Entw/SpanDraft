"""Independent statics and integrated closed forms, ONLY for analytical references.
Never imported by an external-solver adapter to fill missing solver results.
"""
import sympy as sp
from common import result, polynomial_pieces, polynomial_samples, polynomial_extrema


def generate(case):
    x = sp.Symbol('x', real=True)
    L = sp.Rational(str(case['length']))
    EI = sp.Rational(str(case['e'])) * sp.Rational(str(case['i']))
    kind = case['caseId'].split('-')[0]
    C1 = 0
    if kind in ['V01', 'V06']:
        force = case['pointForces'][0]
        P, a = sp.Rational(str(force['value'])), sp.Rational(str(force['position']))
        Ra, Rb = -P*(L-a)/L, -P*a/L
        z = sp.Piecewise((0, x < a), (x-a, True))
        moment = Ra*x + P*z
        shear = sp.Piecewise((Ra, x < a), (Ra+P, True))
        primitive = Ra*x**3/6 + P*z**3/6
        C1 = -primitive.subs(x,L)/L
        w = (primitive+C1*x)/EI
        reactions = [(0, Ra, None), (float(L), Rb, None)]
    elif kind == 'V05':
        load = case['pointMoments'][0]
        C, a = sp.Rational(str(load['value'])), sp.Rational(str(load['position']))
        Ra = C/L
        z = sp.Piecewise((0,x<a),(x-a,True))
        moment = Ra*x - sp.Piecewise((0,x<a),(C,True))
        shear = Ra
        primitive = Ra*x**3/6-C*z**2/2
        C1 = -primitive.subs(x,L)/L
        w = (primitive+C1*x)/EI
        reactions = [(0,Ra,None),(float(L),-Ra,None)]
    elif kind == 'V02':
        P = sp.Rational(str(case['pointForces'][0]['value']))
        moment, shear = P*(L-x), -P
        w = P*x*x*(3*L-x)/(6*EI)
        reactions = [(0,-P,-P*L)]
    elif kind == 'V03':
        q = sp.Rational(str(case['udls'][0]['value']))
        moment, shear = q*(L-x)**2/2, q*(x-L)
        w = q*x*x*(6*L*L-4*L*x+x*x)/(24*EI)
        reactions = [(0,-q*L,-q*L*L/2)]
    elif kind == 'V04':
        q = sp.Rational(str(case['udls'][0]['value']))
        Ra = -q*L/2
        moment, shear = Ra*x+q*x*x/2, Ra+q*x
        primitive = Ra*x**3/6+q*x**4/24
        C1 = -primitive.subs(x,L)/L
        w = (primitive+C1*x)/EI
        reactions = [(0,Ra,None),(float(L),Ra,None)]
    else:
        raise ValueError('No analytical provider for '+case['caseId'])
    fields = dict(u=sp.Integer(0), w=w, theta=sp.diff(w,x), N=sp.Integer(0), V=shear, M=moment)
    pieces = polynomial_pieces(case,fields,x)
    data = result(case,'analytical',list(fields)+['Rx','Ry','Rm'],
                  dict(method='Exact rational statics, integrated moment-curvature closed forms', integrationConstant=float(C1)),
                  'SI and SpanDraft conventions directly; no SpanDraft solver input',
                  'validation/python/analytical.py; derivation in docs/VALIDATION.md')
    for pos,ry,rm in reactions:
        values={'Ry':float(ry)}
        support = next(s for s in case['supports'] if s['position']==pos)
        if support['type'] in ['Pinned','Fixed']: values['Rx']=0.0
        if rm is not None: values['Rm']=float(rm)
        data['reactions'].append(dict(position=pos,values=values))
    data['samples']=polynomial_samples(case,pieces)
    data['extrema']=polynomial_extrema(pieces)
    return data
