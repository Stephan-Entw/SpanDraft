# Cross-check: transfer reference equals Macaulay reference exactly on the random spring/hinge models.
import random,s_fuzz,spring_ref as S,transfer_ref as T
from fractions import Fraction as Fr
n=0;bad=0;mech=0
for r0 in [1e-2,1e-6]:
    rng=random.Random(5+int(1/r0)%97)
    for _ in range(150):
        c=s_fuzz.gen(rng,r0)
        try: a=S.ExactS(c)
        except S.Mechanism:
            try: T.Transfer(c); bad+=1; print('nur Macaulay Mechanismus')
            except T.Mechanism: mech+=1
            continue
        b=T.Transfer(c);n+=1
        for x,sd in c['pts'][::3]:
            X=Fr(float(x));st=b.state(X,sd)
            if st['w']!=a.w(X) or st['M']!=a.M(X,sd) or st['V']!=a.V(X,sd) or st['t']!=a.t(X,sd): bad+=1;print('Abweichung',c['id'],x,sd,float(st['w']-a.w(X)),float(st['M']-a.M(X,sd)),float(st['V']-a.V(X,sd)),float(st['t']-a.t(X,sd)));break
print('Modelle',n,'Mechanismen beide',mech,'Unterschiede',bad)
