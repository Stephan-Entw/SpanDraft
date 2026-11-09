# Federlager: w(x_i) + R_i/k_i = 0. Kraftform (roh / Newton skaliert) und Knotenform (Hermite, Feder auf w-DOF) gegen exakt.
from fractions import Fraction as Fr
import numpy as np
exec(open('proto.py').read().split("gaps=[")[0])
EI=210e9*8.33e-6
INF=None
def flex(k): return Fr(0) if k is INF else 1/Fr(k)/Fr(1)
def exact_s(xs,ks,loads):
    X=[Fr(x) for x in xs];P=[(Fr(p),Fr(v)) for p,v in loads];n=len(X)
    A=[[G(X[i],X[j])+(flex(ks[i]) if i==j else 0) for j in range(n)]+[Fr(1),X[i]] for i in range(n)]+[[Fr(1)]*n+[Fr(0),Fr(0)],X+[Fr(0),Fr(0)]]
    b=[-sum(v*G(X[i],p) for p,v in P) for i in range(n)]+[-sum(v for p,v in P),-sum(v*p for p,v in P)]
    return gauss(A,b)
def fields(e,xs,loads,pr):
    n=len(xs);X=[Fr(x) for x in xs];P=[(Fr(p),Fr(v)) for p,v in loads];p=Fr(pr)
    w=e[n]+e[n+1]*p+sum(e[j]*G(p,X[j]) for j in range(n))+sum(v*G(p,q) for q,v in P)
    M=sum(e[j]*(p-X[j]) for j in range(n) if X[j]<p)+sum(v*(p-q) for q,v in P if q<p)
    return w,M
def kraftform(xs,ks,loads,cl,pr,mode):
    n=len(xs);X=[Fr(x) for x in xs];P=[(Fr(p),Fr(v)) for p,v in loads];p=Fr(pr)
    T=Tmat(xs,[cl],2) if mode!='roh' else [[Fr(int(i==j)) for j in range(n)] for i in range(n)]
    Ge=[[G(X[i],X[j])+(flex(ks[i]) if i==j else 0) for j in range(n)] for i in range(n)]
    be=[-sum(v*G(X[i],q) for q,v in P) for i in range(n)]
    if mode=='naiv':
        Tf=np.array([[float(v) for v in r] for r in T]);Gf=np.array([[float(v) for v in r] for r in Ge])
        H=Tf@Gf@Tf.T;c1=Tf@np.ones(n);cx=Tf@np.array([float(x) for x in X]);rf=Tf@np.array([float(v) for v in be])
    else:
        H=np.array([[float(sum(T[i][a]*Ge[a][b]*T[j][b] for a in range(n) for b in range(n))) for j in range(n)] for i in range(n)])
        c1=np.array([float(sum(T[i])) for i in range(n)]);cx=np.array([float(sum(T[i][a]*X[a] for a in range(n))) for i in range(n)])
        rf=np.array([float(sum(T[i][a]*be[a] for a in range(n))) for i in range(n)])
    K=np.zeros((n+2,n+2));K[:n,:n]=H;K[:n,n]=c1;K[:n,n+1]=cx;K[n,:n]=c1;K[n+1,:n]=cx
    r=np.concatenate([rf,[float(-sum(v for q,v in P)),float(-sum(v*q for q,v in P))]])
    Ks,d=equil(K);y=d*np.linalg.solve(Ks,d*r);cond=np.linalg.cond(Ks)
    Tf=np.array([[float(v) for v in row] for row in T]);R=Tf.T@y[:n]
    cM=[sum(T[i][a]*(p-X[a]) for a in range(n) if X[a]<p) for i in range(n)]
    Mv=sum(float(cM[i])*y[i] for i in range(n))+float(sum(v*(p-q) for q,v in P if q<p))
    cw=[sum(T[i][a]*G(p,X[a]) for a in range(n)) for i in range(n)]
    wv=y[n]+y[n+1]*float(p)+sum(float(cw[i])*y[i] for i in range(n))+float(sum(v*G(p,q) for q,v in P))
    return cond,R,wv,Mv
def knotenform(xs,ks,loads,pr):
    # Hermite-Knoten an allen Stationen, starres Lager w=0 eingesetzt, Feder addiert k auf w-DOF
    st=sorted(set(list(xs)+[p for p,_ in loads]+[pr,0.0,1.0]));m=len(st);nd=2*m;K=np.zeros((nd,nd));f=np.zeros(nd)
    for e in range(m-1):
        h=st[e+1]-st[e];ke=np.array([[12,6*h,-12,6*h],[6*h,4*h*h,-6*h,2*h*h],[-12,-6*h,12,-6*h],[6*h,2*h*h,-6*h,4*h*h]])/h**3
        K[2*e:2*e+4,2*e:2*e+4]+=ke
    for p,v in loads:f[2*st.index(p)]+=v
    fix=[]
    for x,k in zip(xs,ks):
        i=st.index(x)
        if k is INF:fix.append(2*i)
        else:K[2*i,2*i]+=float(k)
    free=[i for i in range(nd) if i not in fix]
    A=K[np.ix_(free,free)];As,d=equil(A);u=np.zeros(nd);u[free]=d*np.linalg.solve(As,d*f[free]);cond=np.linalg.cond(As)
    Kfull=K
    R=[]
    for x,k in zip(xs,ks):
        i=st.index(x)
        R.append((Kfull[2*i]@u-f[2*i]) if k is INF else -float(k)*u[2*i])
    iP=st.index(pr)
    return cond,np.array(R),u[2*iP]
K_real=[1e3,1e6,1e9,1e12,1e15,1e18]
gaps=[1e-3,1e-5,1e-7,6e-9,1e-11,1e-13]
cases={
 'S1 zwei Federn Mitte, starr aussen':lambda a,k:([0.0,.5,.5+a,1.0],[INF,k,k,INF],[(.25,1000.)],[1,2],.25),
 'S2 nur Federn, Paar am Rand':lambda a,k:([0.0,a,1.0],[k,k,k],[(.5,1000.)],[0,1],.5),
 'S3 starres Lager + Feder daneben':lambda a,k:([0.0,.5,.5+a,1.0],[INF,INF,k,INF],[(.25,1000.)],[1,2],.25),
}
for nm,fc in cases.items():
    worst={m:[0,0,0,0.] for m in ['roh','skaliert','naiv','knoten']}
    print('===',nm)
    rows=[]
    for kr in K_real:
        kb=Fr(kr)/Fr(EI)
        for a in gaps:
            xs,ks,l,cl,pr=fc(a,kb)
            e=exact_s(xs,ks,l);n=len(xs);ew,eM=fields(e,xs,l,pr)
            eR=e[:n]
            res={}
            for m in ['roh','skaliert','naiv']:
                c,R,wv,Mv=kraftform(xs,ks,l,cl,pr,m)
                res[m]=(c,max(rel(R[i],eR[i]) for i in cl),rel(wv,ew),rel(Mv,eM))
            try:
                c,R,wv=knotenform(xs,[None if q is None else float(q) for q in ks],l,pr)
                res['knoten']=(c,max(rel(R[i],eR[i]) for i in cl),rel(wv,ew),float('nan'))
            except np.linalg.LinAlgError:
                res['knoten']=(float('inf'),float('inf'),float('inf'),float('nan'))
            rows.append((kr,a,res,[float(eR[i]) for i in cl]))
            for m,v in res.items():
                worst[m][0]=max(worst[m][0],v[0]);worst[m][1]=max(worst[m][1],v[1]);worst[m][2]=max(worst[m][2],v[2])
                if v[3]==v[3]:worst[m][3]=max(worst[m][3],v[3])
    for m,v in worst.items():print(f'  schlechtest {m:9s} cond {v[0]:.1e} R {v[1]:.1e} w {v[2]:.1e} M {v[3]:.1e}')
    for kr,a,res,er in rows:
        if a in (1e-3,6e-9,1e-13) and kr in (1e6,1e9,1e12,1e18):
            print(f'  k={kr:.0e} a={a:<7g} R={er[0]:.3e} | '+' | '.join(f'{m[:3]} c{res[m][0]:.0e} R{res[m][1]:.0e} w{res[m][2]:.0e}' for m in res))
print('=== Physik: Grenzwerte, Feder k an 0,5 und 0,5+a, starr bei 0 und 1, F=1 kN bei 0,25')
for a in [1e-1,1e-2,1e-3,1e-5,6e-9]:
  for kr in [1e6,1e9,1e12,1e15]:
    kb=Fr(kr)/Fr(EI);xs,ks,l,cl,pr=cases['S1 zwei Federn Mitte, starr aussen'](a,kb)
    e=exact_s(xs,ks,l);ew,eM=fields(e,xs,l,pr)
    print(f'  a={a:<7g} k={kr:.0e} N/m  R1={float(e[1]):>12.5g} R2={float(e[2]):>12.5g} N  M(F)={float(eM):8.4f} Nm  w(F)={float(ew)/EI*1e3:.6g} mm')
kb=None
for lab,sup,kk in [('eine Feder 2k=2e6',[0.0,.5,1.0],[INF,Fr(2_000_000)/Fr(EI),INF]),('eine Feder 2k=2e9',[0.0,.5,1.0],[INF,Fr(2_000_000_000)/Fr(EI),INF]),('ein starres Lager',[0.0,.5,1.0],[INF,INF,INF]),('Einspannung-Grenzwert ueber a=6e-9 starr',[0.0,.5,.5+6e-9,1.0],[INF,INF,INF,INF])]:
    e=exact_s(sup,kk,[(.25,1000.)]);ew,eM=fields(e,sup,[(.25,1000.)],.25)
    print(f'  {lab:42s} M(F)={float(eM):8.4f} Nm  w(F)={float(ew)/EI*1e3:.6g} mm')
