# Lagerbedingungen als Zwangsbedingungen: roh, skaliert (Differenzenquotient), gegen exakte Bruchrechnung.
# Geschlossene Felder (Green'sche Funktion |x-xi|^3/12), kein Netz. EI=1.
from fractions import Fraction as Fr
import numpy as np
def G(x,xi): return abs(x-xi)**3/12
def exact(xs,loads):
    X=[Fr(x) for x in xs]; P=[(Fr(p),Fr(v)) for p,v in loads]; n=len(X)
    A=[[G(X[i],X[j]) for j in range(n)]+[Fr(1),X[i]] for i in range(n)]+[[Fr(1)]*n+[Fr(0),Fr(0)],X+[Fr(0),Fr(0)]]
    b=[-sum(v*G(X[i],p) for p,v in P) for i in range(n)]+[-sum(v for p,v in P),-sum(v*p for p,v in P)]
    return gauss(A,b)
def gauss(A,b):
    n=len(A);M=[r[:]+[b[i]] for i,r in enumerate(A)]
    for c in range(n):
        p=next(r for r in range(c,n) if M[r][c]!=0);M[c],M[p]=M[p],M[c]
        for r in range(n):
            if r!=c and M[r][c]!=0:
                f=M[r][c]/M[c][c];M[r]=[a-f*q for a,q in zip(M[r],M[c])]
    return [M[i][n]/M[i][i] for i in range(n)]
def Tmat(xs,clusters,order):
    # Zeilen: Newton-Dividierte-Differenzen innerhalb jeder Gruppe
    X=[Fr(x) for x in xs];n=len(X);T=[[Fr(int(i==j)) for j in range(n)] for i in range(n)]
    for cl in clusters:
        rows={i:{i:Fr(1)} for i in cl}
        if order==1:
            for a,b in zip(cl,cl[1:]):
                d=X[b]-X[a];rows[b]={b:1/d,a:-1/d}
        else:
            for k,i in enumerate(cl):
                r={}
                for j in cl[:k+1]:
                    w=Fr(1)
                    for m in cl[:k+1]:
                        if m!=j:w*=X[j]-X[m]
                    r[j]=1/w
                rows[i]=r
        for i in cl:
            T[i]=[rows[i].get(j,Fr(0)) for j in range(n)]
    return T
def equil(A):
    A=A.copy();d=np.ones(len(A))
    for _ in range(8):
        s=1/np.sqrt(np.abs(A).max(axis=1));A=s[:,None]*A*s;d*=s
    return A,d
def rel(a,e):
    e=float(e);return abs(float(a)-e)/abs(e) if e else abs(float(a))
def run(xs,loads,clusters,probe):
    n=len(xs);e=exact(xs,loads);Xf=[Fr(x) for x in xs];P=[(Fr(p),Fr(v)) for p,v in loads];pr=Fr(probe)
    eR=e[:n];cl=clusters[0]
    eN=sum(eR[i] for i in cl);eC=sum(eR[i]*(Xf[i]-Xf[cl[0]]) for i in cl)
    def Mex(R):return sum(R[j]*(pr-Xf[j]) for j in range(n) if Xf[j]<pr)+sum(v*(pr-p) for p,v in P if p<pr)
    eM=Mex(eR);ew=e[n]+e[n+1]*pr+sum(eR[j]*G(pr,Xf[j]) for j in range(n))+sum(v*G(pr,p) for p,v in P)
    out={}
    Ge=[[G(Xf[i],Xf[j]) for j in range(n)] for i in range(n)]
    be=[-sum(v*G(Xf[i],p) for p,v in P) for i in range(n)]
    for name,order,asm in [('roh',0,'exact'),('1.Diff',1,'exact'),('Newton',2,'exact'),('Newton-naiv',2,'float')]:
        T=Tmat(xs,clusters,order) if order else [[Fr(int(i==j)) for j in range(n)] for i in range(n)]
        def mul(Tf,Gf):
            return [[sum(Tf[i][k]*Gf[k][j] for k in range(n)) for j in range(n)] for i in range(n)]
        if asm=='exact':
            H=[[sum(T[i][k]*Ge[k][l]*T[j][l] for k in range(n) for l in range(n)) for j in range(n)] for i in range(n)]
            c1=[sum(T[i]) for i in range(n)];cx=[sum(T[i][k]*Xf[k] for k in range(n)) for i in range(n)]
            rhs=[sum(T[i][k]*be[k] for k in range(n)) for i in range(n)]
            Hf=np.array([[float(v) for v in r] for r in H]);c1f=np.array([float(v) for v in c1]);cxf=np.array([float(v) for v in cx]);rf=np.array([float(v) for v in rhs])
        else:
            Tf=np.array([[float(v) for v in r] for r in T]);Gf=np.array([[float(v) for v in r] for r in Ge])
            Hf=Tf@Gf@Tf.T;c1f=Tf@np.ones(n);cxf=Tf@np.array([float(x) for x in Xf]);rf=Tf@np.array([float(v) for v in be])
        K=np.zeros((n+2,n+2));K[:n,:n]=Hf;K[:n,n]=c1f;K[:n,n+1]=cxf;K[n,:n]=c1f;K[n+1,:n]=cxf
        r=np.concatenate([rf,[float(-sum(v for p,v in P)),float(-sum(v*p for p,v in P))]])
        Ks,d=equil(K);y=d*np.linalg.solve(Ks,d*r);cond=np.linalg.cond(Ks)
        # Ruecktransformation
        Tf=np.array([[float(v) for v in row] for row in T]);R=Tf.T@y[:n]
        # Gruppenwerte direkt aus y (exakte Koeffizienten)
        cN=[sum(T[i][k] for k in cl) for i in range(n)];cC=[sum(T[i][k]*(Xf[k]-Xf[cl[0]]) for k in cl) for i in range(n)]
        N=sum(float(cN[i])*y[i] for i in range(n));C=sum(float(cC[i])*y[i] for i in range(n))
        cM=[sum(T[i][k]*(pr-Xf[k]) for k in range(n) if Xf[k]<pr) for i in range(n)]
        Mv=sum(float(cM[i])*y[i] for i in range(n))+float(sum(v*(pr-p) for p,v in P if p<pr))
        cw=[sum(T[i][k]*G(pr,Xf[k]) for k in range(n)) for i in range(n)]
        wv=y[n]+y[n+1]*float(pr)+sum(float(cw[i])*y[i] for i in range(n))+float(sum(v*G(pr,p) for p,v in P))
        out[name]=dict(cond=cond,R=max(rel(R[i],eR[i]) for i in cl),N=rel(N,eN),C=rel(C,eC),M=rel(Mv,eM),w=rel(wv,ew))
    return out,dict(R=[float(v) for v in eR],N=float(eN),C=float(eC))
gaps=[1e-3,1e-5,1e-7,6e-9,1e-11,1e-13,1e-15]
cases={
 'A Paar am Rand':lambda a:([0.0,a,1.0],[(.5,1000.)],[[0,1]],.5),
 'B Paar Mitte':lambda a:([0.0,.5,.5+a,1.0],[(.25,1000.)],[[1,2]],.25),
 'K Kragarm':lambda a:([0.0,a],[(1.,1000.)],[[0,1]],.5),
 'D Drei dicht':lambda a:([0.0,.5,.5+a,.5+2*a,1.0],[(.25,1000.)],[[1,2,3]],.25),
}
for nm,f in cases.items():
    print('===',nm)
    for a in gaps:
        xs,l,cl,pr=f(a)
        try:o,e=run(xs,l,cl,pr)
        except Exception as ex:print(a,'FEHLER',ex);continue
        print(f'a={a:<7g} R1={e["R"][cl[0][0]]:.3e} N={e["N"]:.6g} C={e["C"]:.6g}')
        for k,v in o.items():
            print(f'   {k:12s} cond {v["cond"]:.1e}  R {v["R"]:.1e}  Summe {v["N"]:.1e}  Moment {v["C"]:.1e}  M(x) {v["M"]:.1e}  w(x) {v["w"]:.1e}')
