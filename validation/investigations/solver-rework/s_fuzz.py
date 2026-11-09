import json,random,sys,math,compare,spring_ref as S
from spring_ref import INF,Mechanism
def targeted():
    L=4.;P=-10000.;E=210e9;I=8e-6;EI=E*I;out=[]
    for r in [1e-1,1e-3,1e-5,1e-7,1e-9]:
        g=r*L
        for u in [-3,0,3,6,9,12]:
            k=10**u*EI/L**3;kt=10**u*EI/L
            T=dict(
             paar=dict(sup=[(0,'Pinned'),(L,'Roller')],springs=[(L/2,k,0,False),(L/2+g,k,0,False)]),
             nebenlager=dict(sup=[(0,'Pinned'),(L/2,'Roller'),(L,'Roller')],springs=[(L/2+g,k,0,False)]),
             nebengelenk=dict(sup=[(0,'Fixed'),(L,'Roller')],hinges=[L/2],springs=[(L/2+g,k,0,False)]),
             nurfedern=dict(sup=[],springs=[(0,k,0,True),(g,k,0,False),(L,k,0,False)]),
             teileinsp=dict(sup=[(g,'Roller'),(L,'Roller')],springs=[(0,INF,kt,True)]),
             elasteinsp=dict(sup=[(0,'Pinned')],hinges=[L-g],springs=[(L,k,kt,False)]),
             amgelenk=dict(sup=[(0,'Fixed')],hinges=[L/2],springs=[(L/2,k,0,False),(L/2+g,k,0,False),(L,k,0,False)]),
             drei=dict(sup=[(0,'Pinned'),(L,'Roller')],springs=[(L/2,k,0,False),(L/2+g,k,0,False),(L/2+2*g,k,0,False)]),
             drehpaar=dict(sup=[(0,'Pinned'),(L,'Roller')],springs=[(L/2,k,kt,False),(L/2+g,k,kt,False)]))
            for nm,t in T.items():
                pf=[(.3*L,P),(.8*L,P)]+[(min(t['springs'][0][0]+g/2,L-g/2),P)]
                out.append(S.case(f'{nm}|{r}|{u}',L,t['sup'],springs=t['springs'],hinges=t.get('hinges',[]),pf=pf,q=[(0.,L,-2000.)],E=E,I=I))
    return out
def gen(rng,r0):
    L=10**rng.uniform(-2,1.5);E=rng.choice([210e9,70e9,2.5e9]);I=L**4*10**rng.uniform(-9,-5);EI=E*I
    while True:
        k=rng.randint(1,6);grid=sorted(rng.sample(range(0,21),k));sup=[];hinges=[];spr=[]
        for gq in grid:
            x=L if gq==20 else gq*L/20;u=rng.random()
            if u<.25: sup.append((x,'Roller'))
            elif u<.35: sup.append((x,'Pinned'))
            elif u<.45: sup.append((x,'Fixed'))
            elif u<.6 and 0<x<L: hinges.append(x)
            else:
                kw=INF if rng.random()<.15 else 10**rng.uniform(-4,12)*EI/L**3
                v=rng.random();kt=0 if v<.5 else (INF if v<.65 else 10**rng.uniform(-4,12)*EI/L)
                if kw==INF and kt==0: kt=10**rng.uniform(-4,12)*EI/L
                spr.append((x,kw,kt,rng.random()<.5))
        if not spr: continue
        # move some items r0*L beside another item
        anchors=[a for a,_ in sup]+[s[0] for s in spr]+hinges
        spr=[((rng.choice(anchors)+rng.choice([-1,1])*r0*L) if rng.random()<.5 else s[0],)+tuple(s[1:]) for s in spr]
        spr=[s for s in spr if 0<=s[0]<=L]
        hinges=sorted({(rng.choice(anchors)+rng.choice([-1,1])*r0*L) if rng.random()<.4 else h for h in hinges})
        hinges=[h for h in hinges if 0<h<L]
        xs=[a for a,_ in sup]+[s[0] for s in spr]
        if len(set(s[0] for s in spr))<len(spr) or any(s[0] in [a for a,_ in sup] for s in spr): continue
        if any(h==a and t=='Fixed' for h in hinges for a,t in sup) or any(h==s[0] and s[2]!=0 for h in hinges for s in spr): continue
        if not spr: continue
        if not any(t!='Roller' for _,t in sup) and not any(s[3] for s in spr): spr[0]=spr[0][:3]+(True,)
        if spr: break
    mag=10**rng.uniform(1,5)
    allx=[a for a,_ in sup]+[s[0] for s in spr]+hinges
    pos=lambda:rng.choice([rng.uniform(0,L),rng.choice(allx)+rng.choice([-1,1])*r0*L])
    pf=[(x,-mag*rng.uniform(.2,1)) for x in (pos() for _ in range(rng.randint(1,4))) if 0<=x<=L]
    pm=[(x,mag*L*rng.uniform(-.3,.3)) for x in (pos() for _ in range(rng.randint(0,2))) if 0<=x<=L and x not in hinges]
    q=[(0.,L,-mag/L)] if rng.random()<.3 else []
    if not pf: pf=[(L*.51,-mag)]
    return S.case(f'{r0}',L,sup,springs=spr,hinges=hinges,pf=pf,pm=pm,q=q,E=E,I=I)
def run(name,cases,verbose=False):
    res=compare.run('harness',cases,'s');st=dict(n=len(cases),ref={},crash=0,f=0.,g=0.,r=0.,bad=0,agree=0,mech_cs_only=0,mech_ex_only=0)
    worst=None
    for c,o in zip(cases,res):
        try: ex_ok=True; S.ExactS(c)
        except Mechanism: ex_ok=False
        if 'crash' in o: st['crash']+=1;print('  CRASH',o['crash'],c['id']);continue
        if 'fail' in o:
            st['ref'][o['fail']]=st['ref'].get(o['fail'],0)+1
            if o['fail']=='UnstableModel':
                if ex_ok: st['mech_cs_only']+=1; print('  nur C# instabil',c['sup'],c['springs'],c['hinges'])
                else: st['agree']+=1
            elif verbose or o['fail']!='IllConditionedSystem': print('  ',o['fail'],c['id'],c['sup'],c['springs'],c['hinges'])
            continue
        if not ex_ok: st['mech_ex_only']+=1; print('  nur exakt Mechanismus',c['sup'],c['springs'],c['hinges']); continue
        f,g,r=S.errors(c,o);st['f']=max(st['f'],f);st['g']=max(st['g'],g);st['r']=max(st['r'],r);e=max(f,g,r)
        if e>1e-6:
            st['bad']+=1
            if worst is None or e>worst[0]: worst=(e,c['id'],c['sup'],c['springs'],c['hinges'])
        if verbose: print('  %-26s %.0e %.0e %.0e'%(c['id'],f,g,r))
    print('%-22s n=%5d abgelehnt %s Absturz %d | Felder %.0e Extrem %.0e Reakt. %.0e | >1e-6: %d | instabil gleich %d, nur C# %d, nur exakt %d'%(name,st['n'],st['ref'],st['crash'],st['f'],st['g'],st['r'],st['bad'],st['agree'],st['mech_cs_only'],st['mech_ex_only']),flush=True)
    if worst: print('   schlechtester',worst)
    return st
if __name__=='__main__':
    mode=sys.argv[1]
    if mode=='t':
        cs=targeted();res=compare.run('harness',cs,'s')
        agg={}
        for c,o in zip(cs,res):
            nm,r,u=c['id'].split('|')
            if 'fail' in o or 'crash' in o: e=o.get('fail',o.get('crash'))
            else: e=max(S.errors(c,o))
            agg.setdefault(nm,[]).append((float(r),int(u),e))
        for nm,v in agg.items():
            num=[e for _,_,e in v if not isinstance(e,str)];fl=[(r,u,e) for r,u,e in v if isinstance(e,str)]
            print('%-12s n=%d groesste Abweichung %.0e  Ablehnungen %s'%(nm,len(v),max(num) if num else float('nan'),fl))
    else:
        N=int(sys.argv[2])
        for r0 in [1e-2,1e-4,1e-6,1e-9]:
            rng=random.Random(int(r0**-1)%9973+11);run(f'L/{1/r0:.0e}',[gen(rng,r0) for _ in range(N)])
