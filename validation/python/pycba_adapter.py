"""PyCBA 1.0.2: native member results, grid refinement, no beam formulas."""
import math
import numpy as np
from scipy.interpolate import CubicSpline
from pycba import BeamAnalysis
from common import events, piece_index, result, extrema_from_candidates, TOLERANCES

FIELDS = {'w':'D','theta':'R','V':'V','M':'M'}


def build(case):
    boundaries=events(case)
    lengths=np.diff(boundaries)
    restraints=[]
    for x in boundaries:
        supports=[s['type'] for s in case['supports'] if s['position']==x]
        restraints.extend([-1 if supports else 0, -1 if 'Fixed' in supports else 0])
    model=BeamAnalysis(lengths, np.full(len(lengths),case['e']*case['i']),restraints,[],
                       eletype=np.ones(len(lengths),dtype=int),GAv=None,kf=None)
    # Native end loads at interior nodes go on the preceding member exactly once.
    for kind in ['pointForces','pointMoments']:
        for p in case[kind]:
            index=max(0,int(np.searchsorted(boundaries,p['position'],side='left'))-1)
            distance=p['position']-boundaries[index]
            if kind=='pointForces':
                model.add_pl(index+1,-p['value'],distance)
            else:
                model.add_ml(index+1,p['value'],distance)
    for q in case['udls']:
        for index,(a,b) in enumerate(zip(boundaries,boundaries[1:])):
            if q['start'] <= a and b <= q['end']:
                model.add_udl(index+1,-q['value'])
    return model,boundaries,restraints


def extract(case,model,boundaries,restraints,npts):
    code=model.analyze(npts=npts,check_stability=True)
    if code != 0: raise RuntimeError(f'PyCBA analysis returned {code}')
    native=model.beam_results
    if len(native.vRes)!=len(boundaries)-1 or len(native.D)!=2*len(boundaries):
        raise RuntimeError('Unexpected native result layout')
    pieces=[]
    endpoint_ratio=0.0
    endpoint_drift={'w':0.0,'theta':0.0}
    for index,res in enumerate(native.vRes):
        a,b=boundaries[index:index+2]
        if len(res.x)!=npts+3:
            raise RuntimeError('Unexpected grid/padding representation')
        t=np.arange(npts+1,dtype=float)/npts
        fields={q:CubicSpline(t,np.asarray(getattr(res,name))[1:-1]) for q,name in FIELDS.items()}
        nodal={'w':(float(native.D[2*index]),float(native.D[2*index+2])),
               'theta':(float(native.D[2*index+1]),float(native.D[2*index+3]))}
        for q in ['w','theta']:
            kind='displacement' if q=='w' else 'rotation'
            absolute,relative=TOLERANCES[kind]
            for end in [0,1]:
                drift=abs(float(fields[q](end))-nodal[q][end])
                endpoint_drift[q]=max(endpoint_drift[q],drift)
                endpoint_ratio=max(endpoint_ratio,drift/(absolute+relative*abs(nodal[q][end])))
        pieces.append((a,b,fields,nodal))
    data=result(case,'pycba',['Ry','Rm','w','theta','V','M'],
                dict(GAv=None,kf=None,elementType=1,checkStability=True,npts=npts,
                     extraction='native vRes[1:-1], cubic interpolation; native R roots for w extrema; exact native nodal D at boundaries'),
                'Input forces/UDLs negated; input CCW moments unchanged; Ry/Rm,w/theta,V,M identity',
                'https://pypi.org/project/PyCBA/1.0.2/')
    reactions=iter(native.R)
    by_position={}
    for index,x in enumerate(boundaries):
        values={}
        for dof,q in [(2*index,'Ry'),(2*index+1,'Rm')]:
            if restraints[dof]<0: values[q]=float(next(reactions))
        if values: by_position[x]=values
    data['reactions']=[dict(position=s['position'],values=by_position[s['position']]) for s in case['supports']]
    for ev in case['evaluations']:
        index=piece_index(pieces,ev['position'],ev['side'])
        a,b,fields,nodal=pieces[index]
        t=(ev['position']-a)/(b-a)
        values={q:float(f(t)) for q,f in fields.items()}
        if t in [0,1]:
            for q in nodal: values[q]=nodal[q][int(t)]
        data['samples'].append(dict(position=ev['position'],side=ev['side'],values=values))
    for q in ['w','V','M']:
        candidates,plateaus=[],[]
        for a,b,fields,nodal in pieces:
            f=fields[q]
            values=np.asarray(f(np.arange(npts+1)/npts))
            for t,side in [(0,'Right'),(1,'Left')]:
                value=nodal[q][t] if q in nodal else float(f(t))
                candidates.append(dict(value=value,position=a+(b-a)*t,side=side))
            scale=float(np.max(np.abs(values)))
            if float(np.ptp(values)) <= 64*np.finfo(float).eps*scale:
                plateaus.append((a,b,float(values[0])))
            else:
                stationary = fields['theta'] if q=='w' else f.derivative()
                for root in stationary.roots(extrapolate=False):
                    if math.isfinite(root) and 0<root<1:
                        candidates.append(dict(value=float(f(root)),position=a+(b-a)*float(root),side=None))
        data['extrema'].update({q+suffix: ext for suffix,ext in extrema_from_candidates(candidates,plateaus).items()})
    return data,endpoint_ratio,endpoint_drift


def flatten(data):
    flat={}
    for group in ['samples','reactions']:
        for row in data[group]:
            for q,v in row['values'].items():
                kind='displacement' if q=='w' else 'rotation' if q=='theta' else 'moment' if q in ['M','Rm'] else 'force'
                flat[(group,row['position'],row.get('side'),q)]=(v,kind)
    for q,ext in data['extrema'].items():
        kind='displacement' if q[0]=='w' else 'moment' if q[0]=='M' else 'force'
        flat[('extrema',q,'value')]=(ext['value'],kind)
        # Multiplicity is physical data; topology changes prevent convergence.
        for i,loc in enumerate(sorted(ext['locations'],key=lambda l:(l['position'],l['side'] or ''))):
            flat[('extrema',q,'location',i,loc['side'])]=(loc['position'],'position')
        for i,p in enumerate(ext['plateaus']):
            flat[('extrema',q,'plateau',i,'start')]=(p['start'],'position')
            flat[('extrema',q,'plateau',i,'end')]=(p['end'],'position')
    return flat


def generate(case):
    model,boundaries,restraints=build(case)
    previous=None
    consecutive=0
    trace=[]
    prior_deltas={}
    npts=1024
    while True:
        data,endpoint_ratio,endpoint_drift=extract(case,model,boundaries,restraints,npts)
        current=flatten(data)
        ratios={}; deltas={}; topology=True
        if previous is not None:
            topology=previous.keys()==current.keys()
            for key,(value,kind) in current.items():
                if key not in previous: continue
                delta=abs(value-previous[key][0])
                absolute,relative=TOLERANCES[kind]
                ratios[kind]=max(ratios.get(kind,0),delta/(absolute+relative*abs(value)))
                deltas[kind]=max(deltas.get(kind,0),delta)
            passed=topology and max(ratios.values(),default=0)<=.1 and endpoint_ratio<=.1
            consecutive=consecutive+1 if passed else 0
        order={kind:(math.log(prior_deltas[kind]/delta,2) if delta>0 and prior_deltas.get(kind,0)>0 else None)
               for kind,delta in deltas.items()}
        trace.append(dict(npts=npts,topologyStable=topology,maxRatioByKind=ratios,maxDeltaByKind=deltas,
                          observedOrderByKind=order,nativeEndpointDrift=endpoint_drift,nativeEndpointMaxRatio=endpoint_ratio))
        if consecutive>=2 or npts==131072: break
        previous=current
        prior_deltas=deltas
        npts*=2
    data['status']='PASS' if consecutive>=2 else 'PARTIAL'
    data['convergence']=dict(converged=consecutive>=2,requiredConsecutiveRefinements=2,
                             fractionOfTolerance=.1,maxNpts=131072,trace=trace)
    return data
