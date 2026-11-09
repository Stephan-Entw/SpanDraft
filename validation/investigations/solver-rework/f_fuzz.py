import random,sys,compare,s_fuzz,f_ref as R,transfer_ref as T
from fractions import Fraction as Fr
def gen(rng,r0):
    c=s_fuzz.gen(rng,r0);L=c['L'];EI=c['E']*c['I']
    items=[a for a,_ in c['sup']]+[s[0] for s in c['springs']]+c['hinges']+[a for a,_ in c['pf']]
    near=lambda:min(L*(1-1e-12),max(L*1e-12,rng.choice(items)+rng.choice([-1,1])*r0*L)) if items and rng.random()<.5 else rng.uniform(0,L)
    c['ei']=sorted({near():EI*10**rng.uniform(-2,2) for _ in range(rng.randint(0,3))}.items())
    c['ei']=[[x,v] for x,v in c['ei'] if 0<x<L]
    mag=max(abs(p[1]) for p in c['pf'])/L if c['pf'] else 1e3
    lin=[]
    for _ in range(rng.randint(0,2)):
        a=near();b=a+(r0*L if rng.random()<.4 else rng.uniform(0,L-a))
        if b>L or b<=a: continue
        lin.append([a,b,mag*rng.uniform(-2,2),mag*rng.uniform(-2,2)])
    c['lin']=lin
    curv=[]
    for _ in range(rng.randint(0,2) if rng.random()<.5 else 0):
        a=rng.uniform(0,L);b=rng.uniform(a,L)
        if b>a: curv.append([a,b,10**rng.uniform(-5,-2)/L*rng.choice([-1,1])])
    c['curv']=curv
    c['pts']=R.pts(c);return c
def run(name,cases):
    res=compare.run('harness',cases,'g');st=dict(ref={},crash=0,f=0.,g=0.,r=0.,bad=0,agree=0,cs_only=0,ex_only=0);worst=None
    for c,o in zip(cases,res):
        try: T.Transfer(c);ex_ok=True
        except T.Mechanism: ex_ok=False
        if 'crash' in o: st['crash']+=1;print('  CRASH',o['crash'][:200],c['id']);continue
        if 'fail' in o:
            st['ref'][o['fail']]=st['ref'].get(o['fail'],0)+1
            if o['fail']=='UnstableModel':
                if ex_ok: st['cs_only']+=1
                else: st['agree']+=1
            continue
        if not ex_ok: st['ex_only']+=1;continue
        f,g,r=R.errors(c,o);st['f']=max(st['f'],f);st['g']=max(st['g'],g);st['r']=max(st['r'],r);e=max(f,g,r)
        if e>1e-6:
            st['bad']+=1
            if worst is None or e>worst[0]: worst=(e,f,g,r,c)
    print('%-9s n=%4d abgelehnt %s Absturz %d | Felder %.0e Extrem %.0e Reakt. %.0e | >1e-6: %d | instabil gleich %d, nur C# %d, nur exakt %d'%(name,len(cases),st['ref'],st['crash'],st['f'],st['g'],st['r'],st['bad'],st['agree'],st['cs_only'],st['ex_only']),flush=True)
    if worst:
        e,f,g,r,c=worst;print('   schlechtester %.1e (F %.0e X %.0e R %.0e)'%(e,f,g,r),{k:c[k] for k in ('L','sup','springs','hinges','ei','lin','curv','pf','pm','q')})
if __name__=='__main__':
    N=int(sys.argv[1])
    for r0 in [1e-2,1e-4,1e-6,1e-9]:
        rng=random.Random(4242+int(1/r0)%991);run(f'L/{1/r0:.0e}',[gen(rng,r0) for _ in range(N)])
