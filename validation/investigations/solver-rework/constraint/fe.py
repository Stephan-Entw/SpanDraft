# Lager NICHT auf FE-Knoten: Hermite-Netz + Zwangsbedingungen (roh/skaliert) gegen exakt.
from fractions import Fraction as Fr
import numpy as np, sys
sys.argv=['x'];exec(open('proto.py').read().split("gaps=[")[0])
def shape(x,h,xl):
    s=(x-xl)/h
    return [1-3*s*s+2*s**3,h*(s-2*s*s+s**3),3*s*s-2*s**3,h*(s**3-s*s)]
def row(x,N,h):
    e=min(int(x/h),N-1);r=np.zeros(2*N+2);r[2*e:2*e+4]=shape(x,h,e*h);return r
def fe(N,sup,F,pF,scale):
    h=1.0/N;nd=2*N+2;K=np.zeros((nd,nd))
    ke=np.array([[12,6*h,-12,6*h],[6*h,4*h*h,-6*h,2*h*h],[-12,-6*h,12,-6*h],[6*h,2*h*h,-6*h,4*h*h]])/h**3
    for e in range(N):K[2*e:2*e+4,2*e:2*e+4]+=ke
    f=np.zeros(nd);nF=round(pF/h);f[2*nF]=F
    C=np.array([row(x,N,h) for x in sup])
    if scale:
        # Differenzenquotient der Formfunktionen exakt (Bruchrechnung), nicht aus Rundungswerten
        x1,x2=Fr(sup[0]),Fr(sup[1]);hf=Fr(1,N);a=x2-x1
        def rf(x):
            e=min(int(x/hf),N-1);return e,shape(x,hf,e*hf)
        e1,s1=rf(x1);e2,s2=rf(x2);d={}
        for i,v in enumerate(s1):d[2*e1+i]=d.get(2*e1+i,0)-v
        for i,v in enumerate(s2):d[2*e2+i]=d.get(2*e2+i,0)+v
        r=np.zeros(nd)
        for k,v in d.items():r[k]=float(v/a)
        C[1]=r
    n=len(sup);A=np.zeros((nd+n,nd+n));A[:nd,:nd]=K;A[:nd,nd:]=C.T;A[nd:,:nd]=C
    b=np.concatenate([f,np.zeros(n)])
    As,d=equil(A);y=d*np.linalg.solve(As,d*b);lam=y[nd:]
    if scale:Nn,Cc=lam[0],lam[1]
    else:Nn,Cc=lam[0]+lam[1],lam[1]*(sup[1]-sup[0])
    e=nF-1;w=y[2*nF];s=1.0
    d2=(-6*y[2*e]-4*h*y[2*e+1]+6*y[2*e+2]-2*h*y[2*e+3])/h**2
    Mp=(6*y[2*e]+2*h*y[2*e+1]-6*y[2*e+2]+4*h*y[2*e+3])/h**2
    return dict(N=Nn,C=Cc,w=w,M=Mp,cond=np.linalg.cond(As) if N<=100 else float('nan'))
for a in [1e-3,6e-9]:
    x1=0.3337;x2=x1+a;sup=[x1,x2,1.0];pF=0.7
    e=exact(sup,[(pF,1000.)]);X=[Fr(x) for x in sup];pr=Fr(pF)
    eN=abs(float(e[0]+e[1]));eC=abs(float(e[1]*(X[1]-X[0])))
    ew=abs(float(e[3]+e[4]*pr+sum(e[j]*G(pr,X[j]) for j in range(3))))
    eM=abs(float(sum(e[j]*(pr-X[j]) for j in range(3) if X[j]<pr)))
    print(f'a={a:g}: exakt Summe {eN:.6g} N, Moment {eC:.6g} Nm, w {ew:.6g}, M(F) {eM:.6g}')
    for N in [10,100,1000]:
        for sc in [0,1]:
            r=fe(N,sup,1000.,pF,sc)
            print(f'  N={N:<5d} {"skaliert" if sc else "roh     "} cond {r["cond"]:.1e} Summe {rel(abs(r["N"]),eN):.1e} Moment {rel(abs(r["C"]),eC):.1e} w {rel(abs(r["w"]),ew):.1e} M(F) {rel(abs(r["M"]),eM):.1e}')
