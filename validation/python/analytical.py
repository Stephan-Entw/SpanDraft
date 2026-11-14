"""Independent statics and integrated closed forms, ONLY for analytical references.
Never imported by an external-solver adapter to fill missing solver results.
"""
import sympy as sp
import numpy as np
from common import result, events, polynomial_pieces, polynomial_samples, polynomial_extrema, stiffness_at


def generate(case):
    x = sp.Symbol('x', real=True)
    L = sp.Rational(str(case['length']))
    EI = sp.Rational(str(case['e'])) * sp.Rational(str(case['i'])) if 'e' in case else None
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
    elif kind in ['V16', 'V17', 'V18', 'V19', 'V20', 'V21', 'V22', 'V23', 'V24', 'V25', 'V26', 'V27', 'V28', 'V29']:
        return generate_linear(case, x, L, EI)
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


def generate_linear(case, x, L, EI):
    """Exact rational statics and compatibility for piecewise linear distributed loads.

    The state (w, theta, M, V) is carried through the intervals between all event positions by
    integrating the exact linear intensity; point actions and unknown support reactions are jumps.
    The unknown initial deflection and rotation and all reactions follow from the support and free-end
    conditions as one rational linear system, solved symbolically. No SpanDraft code and no external beam
    solver is involved.

    A spring support adds the reaction unknown of each acting spring and its law as one more exact condition:
    w + Ry / k = 0 for a translational spring (Ry = -k w), theta + Rm / k_theta = 0 for a rotational one
    (Rm = -k_theta theta); a rigid translation (partial restraint) prescribes w = 0 and keeps its reaction.
    A spring support is axially rigid.

    A case with segments has a piecewise constant EI: the curvature M / EI is integrated with the EI of the segment
    that contains each interval. Segment boundaries are event positions without any jump, so the state is simply
    carried through them with the new stiffness.
    """
    R = sp.Rational
    bounds = [R(str(b)) for b in events(case)]
    supports = [(R(str(s['position'])), s['type']) for s in case['supports']]
    springs = {i: (R(str(s['translationalStiffness'])) if s.get('translationalStiffness') is not None else None,
                   R(str(s['rotationalStiffness'])) if s.get('rotationalStiffness') is not None else None,
                   bool(s.get('rigidTranslation')))
               for i, s in enumerate(case['supports']) if s['type'] == 'Spring'}
    reactions = {i: sp.Symbol(f'Ry{i}') for i, (_, kind) in enumerate(supports)
                 if kind != 'Spring' or springs[i][0] is not None or springs[i][2]}
    couples = {i: sp.Symbol(f'Rm{i}') for i, (_, kind) in enumerate(supports)
               if kind == 'Fixed' or kind == 'Spring' and springs[i][1] is not None}
    w0, theta0 = sp.symbols('w0 theta0')
    lines = [(R(str(q['start'])), R(str(q['end'])), R(str(q['startValue'])), R(str(q['endValue'])))
             for q in case.get('lineLoads', [])]
    lines += [(R(str(q['start'])), R(str(q['end'])), R(str(q['value'])), R(str(q['value']))) for q in case['udls']]
    forces = [(R(str(p['position'])), R(str(p['value']))) for p in case['pointForces']]
    moments = [(R(str(p['position'])), R(str(p['value']))) for p in case['pointMoments']]
    s, u = sp.symbols('s u', real=True)

    def integrate(expression):
        return sp.integrate(expression.subs(s, u), (u, 0, s))

    def jump(position, w, theta, m, v):
        v += sum((f for a, f in forces if a == position), sp.Integer(0))
        m -= sum((c for a, c in moments if a == position), sp.Integer(0))
        for i, (a, kind) in enumerate(supports):
            if a == position:
                if i in reactions:
                    v += reactions[i]
                if i in couples:
                    m -= couples[i]
        return w, theta, m, v

    equations = []

    def support_conditions(position, w, theta):
        for i, (a, kind) in enumerate(supports):
            if a != position:
                continue
            if kind != 'Spring':
                equations.append(w)
                if kind == 'Fixed':
                    equations.append(theta)
                continue
            translational, rotational, rigid = springs[i]
            if translational is not None:
                equations.append(w + reactions[i] / translational)
            elif rigid:
                equations.append(w)
            if rotational is not None:
                equations.append(theta + couples[i] / rotational)

    state = (w0, theta0, sp.Integer(0), sp.Integer(0))
    support_conditions(bounds[0], w0, theta0)
    state = jump(bounds[0], *state)
    pieces = []
    for a, b in zip(bounds, bounds[1:]):
        w, theta, m, v = state
        # Intensity inside (a, b): the sum of the loads covering the whole interval, linear in x.
        q = sp.expand(sum((q1 + (q2 - q1) * (x - qa) / (qb - qa) for qa, qb, q1, q2 in lines if qa <= a and b <= qb),
                          sp.Integer(0)).subs(x, a + s))
        shear = v + integrate(q)
        moment = m + integrate(shear)
        e_mod, inertia = stiffness_at(case, float(a), float(b))
        rotation = theta + integrate(moment) / (R(str(e_mod)) * R(str(inertia)))
        deflection = w + integrate(rotation)
        pieces.append((a, b, dict(w=deflection, theta=rotation, M=moment, V=shear)))
        end = tuple(e.subs(s, b - a) for e in (deflection, rotation, moment, shear))
        support_conditions(b, end[0], end[1])
        state = jump(b, *end)
    equations += [state[2], state[3]]
    unknowns = [w0, theta0] + list(reactions.values()) + list(couples.values())
    solution = sp.solve(equations, unknowns, dict=True)
    if len(solution) != 1 or set(solution[0]) != set(unknowns):
        raise RuntimeError('The statics of ' + case['caseId'] + ' are not uniquely solvable')
    solution = solution[0]
    t = sp.Symbol('t')
    zero = np.polynomial.Polynomial([0.0])
    full = []
    for a, b, fields in pieces:
        polys = dict(u=zero, N=zero)
        for name, expression in fields.items():
            expression = sp.expand(expression.subs(solution).subs(s, t * (b - a)))
            poly = sp.Poly(expression, t)
            polys[name] = np.polynomial.Polynomial([float(c) for c in reversed(poly.all_coeffs())])
        full.append((float(a), float(b), polys))
    data = result(case, 'analytical', ['u', 'w', 'theta', 'N', 'V', 'M', 'Rx', 'Ry', 'Rm'],
                  dict(method='Exact rational statics and compatibility, integrated linear intensity'),
                  'SI and SpanDraft conventions directly; no SpanDraft solver input',
                  'validation/python/analytical.py; derivation in docs/VALIDATION.md')
    for i, (position, kind) in enumerate(supports):
        values = {}
        if i in reactions:
            values['Ry'] = float(solution[reactions[i]])
        if kind in ['Pinned', 'Fixed', 'Spring']:
            values['Rx'] = 0.0
        if i in couples:
            values['Rm'] = float(solution[couples[i]])
        data['reactions'].append(dict(position=float(position), values=values))
    data['samples'] = polynomial_samples(case, full)
    data['extrema'] = polynomial_extrema(full)
    return data
