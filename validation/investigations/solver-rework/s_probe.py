import os,random,s_fuzz,compare,spring_ref as S,json
from fractions import Fraction as Fr
ill=[]
for r0 in [1e-2,1e-4,1e-6,1e-9]:
    rng=random.Random(int(r0**-1)%9973+11);cs=[s_fuzz.gen(rng,r0) for _ in range(1500)]
    for c,o in zip(cs,compare.run('harness',cs,'s')):
        if o.get('fail')=='IllConditionedSystem': ill.append(c)
os.environ['SPANDRAFT_PROBE_CUT_LIMIT']='0'
res=compare.run('harness',ill,'p')
for c,o in zip(ill,res):
    ex=S.ExactS(c);L=c['L'];w=max(abs(float(ex.w(Fr(float(x))))) for x,_ in c['pts'])
    e='%s'%o.get('fail') if 'fail' in o else '%.0e'%max(S.errors(c,o))
    print('w/L %.0e  ohne Grenze: Abweichung %s'%(w/L,e))
