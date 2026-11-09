# Outside check of the exact transfer reference with PyCBA (direct stiffness, closed-form prismatic members).
import logging,random,sys,itertools,numpy as np
logging.disable(logging.CRITICAL)
from fractions import Fraction as Fr
from pycba import BeamAnalysis
import transfer_ref as T, f_fuzz
INF='inf'
def build(c,sC,sK):
    L=c['L'];EI0=c['E']*c['I']
    ev={0.,L}|{a for a,_ in c['sup']}|{s[0] for s in c['springs']}|set(c['hinges'])|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}|{x for x,_ in c['ei']}
    for a,b,*_ in c['q']+c['lin']+c['curv']: ev|={a,b}
    ev=sorted(ev);n=len(ev)
    EIat=lambda x:([EI0]+[v for p,v in sorted(c['ei']) if p<=x])[-1]
    spans=[b-a for a,b in zip(ev,ev[1:])];EI=[EIat((a+b)/2) for a,b in zip(ev,ev[1:])]
    R=[0.]*(2*n)
    for a,t in c['sup']:
        i=ev.index(a);R[2*i]=-1
        if t=='Fixed': R[2*i+1]=-1
    for x,kw,kt,_ in c['springs']:
        i=ev.index(x);R[2*i]=-1 if kw==INF else kw
        if kt==INF: R[2*i+1]=-1
        elif kt>0: R[2*i+1]=kt
    et=[1]*(n-1)
    for h in c['hinges']: et[ev.index(h)-1]=2
    LM=[]
    def span_of(x): i=ev.index(x);return (i,0.) if i<n-1 else (i-1,spans[-1])
    for x,v in c['pf']: i,a=span_of(x);LM.append([i+1,2,-v,a])
    for x,v in c['pm']: i,a=span_of(x);LM.append([i+1,4,sC*v,a])
    for j,(a,b) in enumerate(zip(ev,ev[1:])):
        for qa,qb,v in c['q']:
            if qa<=a and qb>=b: LM.append([j+1,1,-v])
        for la,lb,v1,v2 in c['lin']:
            if la<=a and lb>=b:
                f=lambda x:v1+(v2-v1)*(x-la)/(lb-la);LM.append([j+1,5,-f(a),-f(b)])
        for ka,kb,k in c['curv']:
            if ka<=a and kb>=b: LM.append([j+1,6,sK*k])
    return ev,BeamAnalysis(spans,EI,R,LM,eletype=et),R
def compare(c,sC=1,sK=1,sM=1):
    ev,ba,Rv=build(c,sC,sK);ba.analyze(check_stability=False);D=ba.beam_results.D;Rp=list(ba.beam_results.R)
    ex=T.Transfer(c);w=[];t=[];wd=[];td=[]
    for i,x in enumerate(ev):
        st=ex.state(Fr(x),'R');w.append(float(st['w']));wd.append(D[2*i])
        if x not in c['hinges']: t.append(float(st['t']));td.append(D[2*i+1])
    rt,rr=ex.reactions();rt={float(a):float(v) for a,v in rt};rr={float(a):float(v) for a,v in rr}
    exp=[]
    for i,x in enumerate(ev):
        if Rv[2*i]==-1: exp.append(rt[x])
        if Rv[2*i+1]==-1: exp.append(sM*rr[x])
    e=lambda a,b:max(abs(p-q) for p,q in zip(a,b))/max(max(abs(p) for p in a),1e-300)
    load=sum(abs(v) for _,v in c['pf'])+sum(abs(v)*(b-a) for a,b,v in c['q'])+sum((abs(v1)+abs(v2))/2*(b-a) for a,b,v1,v2 in c['lin'])
    rs=max([abs(v) for v in exp]+[abs(v) for v in Rp]+[load,1e-300])
    return e(w,wd),e(t,td) if t else 0.,(max(abs(p-q) for p,q in zip(exp,Rp))/rs if exp else 0.)
if __name__=='__main__':
    N=int(sys.argv[1]);rng=random.Random(99);done=0;skip=0;worst=[0,0,0];signs=(1,1,1);res=[]
    while done<N:
        c=f_fuzz.gen(rng,1e-2);L=c['L']
        ev=sorted({0.,L}|{a for a,_ in c['sup']}|{s[0] for s in c['springs']}|set(c['hinges'])|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}|{x for x,_ in c['ei']}|{x for a,b,*_ in c['q']+c['lin']+c['curv'] for x in (a,b)})
        if min(np.diff(ev))<1e-3*L or any(x==h for h in c['hinges'] for x in [a for a,_ in c['sup']]): skip+=1;continue
        try: T.Transfer(c)
        except T.Mechanism: skip+=1;continue
        if signs is None and c['pm'] and c['curv'] and any(t=='Fixed' for _,t in c['sup']):
            best=min(itertools.product([1,-1],[1,-1],[1,-1]),key=lambda s:max(compare(c,*s)));signs=best;print('Vorzeichen Moment, Kruemmung, Einspannmoment:',signs)
        if signs is None: continue
        try: r=compare(c,*signs)
        except Exception as ex: print('PyCBA Fehler',ex);skip+=1;continue
        worst=[max(a,b) for a,b in zip(worst,r)];done+=1;res.append(max(r))
    print('Modelle',done,'uebersprungen',skip,'groesste Abweichung w %.1e theta %.1e Reaktionen %.1e'%tuple(worst),' >1e-8:',sum(e>1e-8 for e in res))
