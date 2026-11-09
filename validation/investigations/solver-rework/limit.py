# Measures how SpanDraft behaves as a function of gap/L against the exact reference.
import json,subprocess,sys,random,math,collections
from fractions import Fraction as Fr
sys.path.insert(0,'.')
import fuzz
PY_R=[1e-2,5e-3,2e-3,1e-3,5e-4,2e-4,1e-4,5e-5,2e-5,1e-5,1e-6,1e-7]
def pts_for(c):
    L=c['L'];ev=sorted({0.0,L}|{s[0] for s in c['sup']}|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}|{x for a,b,_ in c['q'] for x in(a,b)})
    pts=[]
    for j,x in enumerate(ev):
        if j>0: pts.append([x,'L'])
        if j<len(ev)-1:
            pts.append([x,'R'])
            for f in(.3,.7): pts.append([x+(ev[j+1]-x)*f,'R'])
    return pts
def case(id,L,sup,pf=(),pm=(),q=(),E=210e9,I=None):
    I=I if I is not None else 8e-6*L**4/4**4  # keeps slenderness similar across scales
    c=dict(id=id,kind='x',L=L,E=E,A=.003*L*L/16,I=I,sup=[[a,t] for a,t in sup],pf=[list(p) for p in pf],pm=[list(p) for p in pm],q=[list(p) for p in q])
    c['pts']=pts_for(c);return c
def evaluate(cases):
    with open('l-in.jsonl','w') as f:
        for c in cases: f.write(json.dumps(c)+'\n')
    subprocess.run(['dotnet','run','--no-build','-c','Release','--project','clean/fuzz','--','l-in.jsonl','l-out.jsonl'],check=True)
    res=[]
    for c,l in zip(cases,open('l-out.jsonl')):
        o=json.loads(l)
        if 'fail' in o or 'crash' in o: res.append(dict(id=c['id'],ref=o.get('fail') or o.get('crash')));continue
        e=fuzz.Exact(c);rows=[]
        for (x,s),v in zip(c['pts'],o['S']):
            X=Fr(float(x));rows.append(([float(e.w(X)),float(e.t(X)),float(e.V(X,s)),float(e.M(X,s))],v))
        sc=[max(abs(r[0][i]) for r in rows) or 1. for i in range(4)]
        _L=float(c['L']);_v,_m=sc[2],sc[3];sc[2]=max(_v,_m/_L);sc[3]=max(_m,_v*_L)
        er=[max(abs(r[0][i]-r[1][i]) for r in rows)/sc[i] for i in range(4)]
        xr=e.extrema();xe=0.;
        for k,(val,pos,side) in o['X'].items():
            xe=max(xe,abs(val-float(xr[k]))/sc[{'w':0,'V':2,'M':3}[k[0]]])
        res.append(dict(id=c['id'],w=er[0],t=er[1],V=er[2],M=er[3],x=xe,all=max(er+[xe])))
    return res
def topologies(L,g):
    P=-5000.;q=-2000.;m=L/2
    T={}
    T['Einfeld, 2 Lasten Mitte']=dict(sup=[(0.,'Pinned'),(L,'Roller')],pf=[(m,P),(m+g,P*.6)])
    T['Einfeld, q+Last am Lager']=dict(sup=[(0.,'Pinned'),(L,'Roller')],pf=[(g,P)],q=[(0.,L,q)])
    T['Kragarm, 2 Lasten Mitte']=dict(sup=[(0.,'Fixed')],pf=[(m,P),(m+g,P*.6)])
    T['Kragarm, Last an Einspannung']=dict(sup=[(0.,'Fixed')],pf=[(g,P),(L,P)])
    T['Kragarm, 2 Lasten am Ende']=dict(sup=[(0.,'Fixed')],pf=[(L-g,P),(L,P*.6)])
    T['Überhang kurz am Lager']=dict(sup=[(g,'Pinned'),(L,'Roller')],pf=[(m,P)])
    T['Überhang lang, 2 Lasten Ende']=dict(sup=[(0.,'Pinned'),(.4*L,'Roller')],pf=[(L-g,P),(L,P*.6)])
    T['Zweifeld, Last am Mittellager']=dict(sup=[(0.,'Pinned'),(m,'Roller'),(L,'Roller')],pf=[(m+g,P)],q=[(0.,L,q)])
    T['Zwei Lager dicht']=dict(sup=[(0.,'Pinned'),(g,'Roller')],pf=[(L,P)])
    T['Einspannung+Rolle, 2 Lasten']=dict(sup=[(0.,'Fixed'),(L,'Roller')],pf=[(m,P),(m+g,P*.6)])
    T['Beidseitig eingespannt, 2 Lasten']=dict(sup=[(0.,'Fixed'),(L,'Fixed')],pf=[(m,P),(m+g,P*.6)])
    T['Streckenlast sehr kurz']=dict(sup=[(0.,'Pinned'),(L,'Roller')],q=[(m,m+g,q*L/g*.01)])
    T['Kragarm, Moment+Last dicht']=dict(sup=[(0.,'Fixed')],pf=[(m,P)],pm=[(m+g,5000.*L)])
    return T
def part1(tag):
    print('== Part1',tag)
    L=10.0;table={}
    cases=[]
    for r in PY_R:
        for name,t in topologies(L,r*L).items(): cases.append(case(f'{name}|{r}',L,**t))
    res=evaluate(cases)
    names=list(topologies(L,.1).keys())
    print('%-34s'%'gap/L'+''.join('%9.0e'%r for r in PY_R))
    for n in names:
        row=[]
        for r in PY_R:
            x=next(q for q in res if q['id']==f'{n}|{r}')
            row.append('   REFUSE' if 'ref' in x else '%9.0e'%x['all'])
        print('%-34s'%n+''.join(row))
    return res
def part_scale():
    print('== scale invariance: refused at gap/L per L (Kragarm & Einfeld, independent of size?)')
    for L in [0.02,0.1,0.5,2.,10.,50.]:
        cs=[]
        for r in PY_R:
            g=r*L
            cs.append(case(f'K|{r}',L,[(0.,'Fixed')],pf=[(L/2,-5000.),(L/2+g,-3000.)]))
            cs.append(case(f'E|{r}',L,[(0.,'Pinned'),(L,'Roller')],pf=[(L/2,-5000.),(L/2+g,-3000.)]))
        res=evaluate(cs)
        s=''
        for pre in 'KE':
            row=[next(q for q in res if q['id']==f'{pre}|{r}') for r in PY_R]
            first=next((r for r,q in zip(PY_R,row) if 'ref' in q),None)
            ok=[q['all'] for q,r in zip(row,PY_R) if 'ref' not in q and r>=5e-4]
            s+=f' {pre}: first refused r={first} maxerr(r>=5e-4)={max(ok):.1e} |'
        print('L=%5g'%L,s)
def part_random(rs,N):
    print('== adversarial random with all gaps >= r*L, many at the limit')
    for r0 in rs:
        rng=random.Random(int(1/r0));cases=[]
        for i in range(N):
            L=10**rng.uniform(-2,1.5)
            supt=rng.choice(['K','E','U','ZW','EW','FF','VF'])
            sup={'K':[(0.,'Fixed')],'E':[(0.,'Pinned'),(L,'Roller')],'U':[(0.,'Pinned'),(rng.uniform(.2,.8)*L,'Roller')],'ZW':[(0.,'Pinned'),(.5*L,'Roller'),(L,'Roller')],'EW':[(0.,'Fixed'),(L,'Roller')],'FF':[(0.,'Fixed'),(L,'Fixed')],'VF':[(0.,'Pinned')]+[(L*(k+1)/4,'Roller') for k in range(4)]}[supt]
            # build stations from clusters spaced exactly r0*L apart
            stations=[];nc=rng.randint(1,6)
            g=r0*L
            for _ in range(nc):
                base=rng.choice([0.,L,rng.choice(sup)[0],rng.uniform(0,L),rng.uniform(0,L)])
                k=rng.randint(1,4);d=rng.choice([1,-1])
                for j in range(1,k+1):
                    x=base+d*j*g
                    if 0<=x<=L: stations.append(x)
            # enforce the rule: drop any station closer than r*L to one already accepted (except equal)
            acc=sorted({0.,L}|{a for a,_ in sup})
            for x in stations:
                if all(abs(x-a)>=g*(1-1e-9) for a in acc): acc.append(x);acc.sort()
            extra=[x for x in acc if x not in (0.,L) and x not in [a for a,_ in sup]]
            if not extra: extra=[L/2]
            pf=[];pm=[];q=[];mag=10**rng.uniform(1,5)
            for x in extra:
                k=rng.random()
                if k<.6: pf.append((x,rng.choice([-1,-1,1])*mag*rng.uniform(.2,1)))
                elif k<.8: pm.append((x,rng.choice([-1,1])*mag*L*rng.uniform(.05,.5)))
                else:
                    y=rng.choice(acc)
                    if y!=x: q.append((min(x,y),max(x,y),-mag/L))
                    else: pf.append((x,-mag))
            if rng.random()<.3: q.append((0.,L,-mag/L))
            cases.append(case(f'{r0}|{i}',L,sup,pf=pf,pm=pm,q=q,E=rng.choice([210e9,70e9,110e9,2.5e9]),I=L**4*10**rng.uniform(-9,-5)))
        res=evaluate(cases)
        ok=[x for x in res if 'ref' not in x]
        vals=sorted(x['all'] for x in ok)
        nres=len(res)-len(ok)
        print('r=%g n=%d refused=%d (%.1f%%) err median=%.1e p99=%.1e max=%.1e  >1e-6:%d >1e-4:%d'%(r0,len(res),nres,nres/len(res)*100,vals[len(vals)//2],vals[int(.99*len(vals))],vals[-1],sum(v>1e-6 for v in vals),sum(v>1e-4 for v in vals)),flush=True)
if __name__=='__main__':
    m=sys.argv[1]
    if m=='p1': part1(sys.argv[2])
    if m=='scale': part_scale()
    if m=='rand': part_random([float(x) for x in sys.argv[3:]],int(sys.argv[2]))
