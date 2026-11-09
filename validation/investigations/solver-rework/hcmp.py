# Real C# hinges against exact fractions; PyCBA as an outside check of the exact reference.
import json,random,subprocess,sys
from fractions import Fraction as Fr
import numpy as np
sys.path.insert(0,'.')
import hinge,hinge2,compare,logging
logging.disable(logging.CRITICAL)
from pycba import BeamAnalysis
def errors(c,out):
    ex=hinge.ExactH(c);rows=[]
    for x,s in c['pts']:
        X=Fr(float(x));rows.append([float(ex.w(X)),float(ex.t(X,s)),float(ex.V(X,s)),float(ex.M(X,s))])
    sc=[max(abs(r[i]) for r in rows) or 1. for i in range(4)]
    L,EI=float(c['L']),float(c['E'])*float(c['I']);v,m=sc[2],sc[3];sc[2]=max(v,m/L);sc[3]=max(m,v*L);sc[1]=max(sc[1],sc[3]*L/EI);sc[0]=max(sc[0],sc[3]*L**2/EI)
    f=max(max(abs(r[i]-o[i])/sc[i] for i in range(4)) for r,o in zip(rows,out['S']))
    xr=ex.extrema();g=max(abs(out['X'][k][0]-float(xr[k]))/sc[{'w':0,'V':2,'M':3}[k[0]]] for k in out['X'])
    r=0.
    for (a,_),ry in zip(ex.sup,ex.Ry):
        o=[q for q in out['R'] if q[0]==float(a)]
        if o: r=max(r,abs(o[0][1]-float(ry))/sc[2])
    return f,g,r,max(abs(v) for v in [o[0] for o in out['S']])/L
def run(name,cases):
    res=compare.run('harness',cases,'h');st=dict(n=len(cases),ref={},crash=0,f=0.,g=0.,r=0.,bad=0)
    worst=None
    for c,o in zip(cases,res):
        if 'crash' in o: st['crash']+=1;print('  CRASH',o['crash'],c['sup'],c['hinges']);continue
        if 'fail' in o: st['ref'][o['fail']]=st['ref'].get(o['fail'],0)+1;continue
        f,g,r,wL=errors(c,o);st['f']=max(st['f'],f);st['g']=max(st['g'],g);st['r']=max(st['r'],r)
        e=max(f,g,r)
        if e>1e-6:
            st['bad']+=1
            if worst is None or e>worst[0]: worst=(e,wL,[(a/c['L'],t) for a,t in c['sup']],[h/c['L'] for h in c['hinges']])
    print('%-24s n=%5d abgelehnt %s Absturz %d | Felder %.0e Extremwerte %.0e Reaktionen %.0e | >1e-6: %d'%(name,st['n'],st['ref'],st['crash'],st['f'],st['g'],st['r'],st['bad']),flush=True)
    if worst: print('    schlechtester %.1e w/L=%.1e'%(worst[0],worst[1]),worst[2],worst[3])
def gens():
    L=10.;P=-5000.;out=[]
    for r in [1e-1,1e-2,1e-3,1e-4,1e-5,1e-6,1e-7,1e-8,1e-9]:
        g=r*L
        T=[dict(sup=[(0.,'Pinned'),(.5*L,'Roller'),(L,'Roller')],hinges=[.5*L+g],pf=[(.3*L,P),(.8*L,P)]),
           dict(sup=[(0.,'Fixed'),(L,'Roller')],hinges=[g],pf=[(.5*L,P)]),
           dict(sup=[(0.,'Fixed'),(L,'Roller')],hinges=[L-g],pf=[(.5*L,P)]),
           dict(sup=[(0.,'Fixed'),(L,'Fixed')],hinges=[.5*L],pf=[(.5*L+g,P)]),
           dict(sup=[(0.,'Fixed'),(L,'Fixed')],hinges=[.5*L,.5*L+g],pf=[(.25*L,P)]),
           dict(sup=[(0.,'Fixed'),(L,'Fixed')],hinges=[.3*L,.7*L],pf=[(.3*L+g,P)]),
           dict(sup=[(0.,'Pinned'),(.4*L,'Roller'),(L,'Roller')],hinges=[.4*L+g],pf=[(.7*L,P)],q=[(0.,L,-2000.)]),
           dict(sup=[(0.,'Pinned'),(.3*L,'Roller'),(.3*L+g,'Roller'),(L,'Roller')],hinges=[.6*L],pf=[(.5*L,P)]),
           dict(sup=[(0.,'Pinned'),(.5*L,'Roller'),(L,'Roller')],hinges=[.5*L],pf=[(.25*L,P),(.5*L+g,P)])]
        out+=[hinge.case(f't{r}',L,**t) for t in T]
    return out
def pycba_check(N):
    rng=random.Random(77);worst=0;n=0;skip=0
    for i in range(N):
        c=hinge2.gen(rng,1e-2);L=c['L']
        if any(a in c['hinges'] for a,_ in c['sup']): skip+=1;continue
        c['pf']=[p for p in c['pf'] if 0<p[0]<L]; c['pm']=[p for p in c['pm'] if 0<p[0]<L]
        if not c['pf'] and not c['pm'] and not c['q']: c['pf']=[(L*.51,-100.)]
        ev=sorted({0.,L}|{a for a,_ in c['sup']}|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}|set(c['hinges']))
        if min(np.diff(ev))<1e-4*L: skip+=1;continue
        lengths=np.diff(ev);R=[];types=[]
        for x in ev:
            st=[t for a,t in c['sup'] if a==x];R+=[-1 if st else 0,-1 if 'Fixed' in st else 0]
        for a,b in zip(ev,ev[1:]): types.append(2 if b in c['hinges'] else 1)
        m=BeamAnalysis(lengths,np.full(len(lengths),c['E']*c['I']),R,[],eletype=np.array(types),GAv=None,kf=None)
        for x,v in c['pf']:
            j=max(0,int(np.searchsorted(ev,x,side='left'))-1);m.add_pl(j+1,-v,x-ev[j])
        for x,v in c['pm']:
            j=max(0,int(np.searchsorted(ev,x,side='left'))-1);m.add_ml(j+1,v,x-ev[j])
        for a,b,v in c['q']:
            for j,(p,q) in enumerate(zip(ev,ev[1:])):
                if a<=p and q<=b: m.add_udl(j+1,-v)
        if m.analyze(npts=11,check_stability=False)!=0: skip+=1;continue
        ex=hinge.ExactH(c);D=m.beam_results.D;Rs=m.beam_results.R
        scw=max(abs(float(ex.w(Fr(x)))) for x in ev) or 1;EI=c['E']*c['I']
        got=[abs(D[2*k]) for k in range(len(ev))];want=[abs(float(ex.w(Fr(x)))) for x in ev]
        ee=max(abs(a-b) for a,b in zip(got,want))/scw
        worst=max(worst,ee);n+=1
    print('PyCBA gegen exakte Referenz: %d Modelle, %d ausgelassen, groesste Abweichung der Durchbiegung %.1e'%(n,skip,worst))
if __name__=='__main__':
    run('Gezielte Faelle',gens())
    for r0 in [1e-2,1e-3,1e-5,1e-7,1e-9]:
        rng=random.Random(int(1/r0)+3)
        run(f'Zufall Abstand/L={r0:g}',[hinge2.gen(rng,r0) for _ in range(1000)])
    pycba_check(400)
