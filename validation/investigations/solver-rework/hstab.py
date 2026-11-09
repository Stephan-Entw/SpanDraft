# C# stability rule against exact rank, and whether rejected near-mechanisms really are ones.
import random,sys,logging
sys.path.insert(0,'.')
import hinge,hinge2,compare
from fractions import Fraction as Fr
rng=random.Random(11);cases=[]
L=10.
while len(cases)<3000:
    slots=sorted(rng.sample([round(k*.5,2) for k in range(0,21)],rng.randint(1,7)));sup=[];h=[]
    for x in slots:
        k=rng.random()
        if k<.35: sup.append((x,'Roller'))
        elif k<.55: sup.append((x,'Pinned'))
        elif k<.7: sup.append((x,'Fixed'))
        elif 0<x<L: h.append(x)
    if rng.random()<.15 and sup:
        c=[a for a,t in sup if t!='Fixed' and 0<a<L and a not in h]
        if c: h.append(rng.choice(c))
    if not sup or not any(t!='Roller' for _,t in sup): continue
    cases.append(hinge.case(f's{len(cases)}',L,sup,hinges=h,pf=[(4.3,-1000.)]))
res=compare.run('harness',cases,'hs')
tab={}
for c,o in zip(cases,res):
    try: hinge.ExactH(c);ex=True
    except hinge.Mechanism: ex=False
    key=(ex,o.get('fail') or o.get('crash') or 'ok');tab[key]=tab.get(key,0)+1
print('exakt stabil?, C#:',tab)
for r0 in [1e-2,1e-7,1e-9]:
    rng=random.Random(int(1/r0)+3);cs=[hinge2.gen(rng,r0) for _ in range(1000)]
    rs=compare.run('harness',cs,'hs2');nur=mech=0;ill=[]
    for c,o in zip(cs,rs):
        if o.get('fail')=='UnstableModel':
            if all(t=='Roller' for _,t in c['sup']): nur+=1
            else: mech+=1;print('  instabil trotz Festpunkt',c['sup'],c['hinges'])
        if o.get('fail')=='IllConditionedSystem':
            e=hinge.ExactH(c);ws=max(abs(float(e.w(Fr(x)))) for x,_ in c['pts']);ill.append('%.0e'%(ws/c['L']))
    print('Abstand/L',r0,'nur Loslager',nur,'andere',mech,'| abgelehnte Fast-Mechanismen, exakte Durchbiegung/L:',ill)
