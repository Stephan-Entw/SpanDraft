import json,random,s_fuzz,spring_ref as S
from fractions import Fraction as Fr
EI=lambda c:c['E']*c['I']
for r0 in [1e-2,1e-4,1e-6,1e-9]:
    rng=random.Random(int(r0**-1)%9973+11);cs=[s_fuzz.gen(rng,r0) for _ in range(1500)]
    res=__import__('compare').run('harness',cs,'s')
    for c,o in zip(cs,res):
        if o.get('fail')!='IllConditionedSystem':continue
        ex=S.ExactS(c);L=c['L'];w=max(abs(float(ex.w(Fr(float(x))))) for x,_ in c['pts'])
        ks=[(round(s[0]/L,9),'starr' if s[1]=='inf' else '%.0e'%(s[1]*L**3/EI(c)),'0' if s[2]==0 else ('starr' if s[2]=='inf' else '%.0e'%(s[2]*L/EI(c)))) for s in c['springs']]
        print('L/%.0e  max|w|/L=%.1e  Lager %s  Federn(x/L, kL3/EI, ktL/EI) %s  Gelenke %s'%(1/r0,w/L,[(round(a/L,9),t) for a,t in c['sup']],ks,[round(h/L,9) for h in c['hinges']]))
