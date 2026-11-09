# Independent exact reference (transfer method, Fractions): piecewise EI, linear distributed loads,
# imposed curvature, point forces/couples, rigid supports, translational/rotational springs, hinges.
# Unknowns: w0, th0, every reaction, every restraint couple, every hinge rotation jump.
# Conventions as the Macaulay reference: EI w'' = M + EI*kT, M' = V, V' = q, a force P adds +P to V,
# a couple C adds -C to M, a hinge adds D to theta.
from fractions import Fraction as Fr
INF='inf'
class Mechanism(Exception): pass
def F(v): return Fr(float(v))
class Lin:
    """Affine combination of unknowns: dict index->coef plus constant."""
    __slots__=('c','k')
    def __init__(s,c=None,k=Fr(0)): s.c=c or {}; s.k=k
    def __add__(s,o):
        if not isinstance(o,Lin): return Lin(dict(s.c),s.k+o)
        d=dict(s.c)
        for i,v in o.c.items(): d[i]=d.get(i,0)+v
        return Lin(d,s.k+o.k)
    __radd__=__add__
    def __mul__(s,f): return Lin({i:v*f for i,v in s.c.items()},s.k*f)
    __rmul__=__mul__
    def __truediv__(s,f): return s*(Fr(1)/f)
    def __neg__(s): return s*Fr(-1)
    def __sub__(s,o): return s+(-o)
    def val(s,x): return s.k+sum(v*x[i] for i,v in s.c.items())
def unknown(i): return Lin({i:Fr(1)})
class Transfer:
    def __init__(s,c):
        s.L=F(c['L']);EI0=F(c['E'])*F(c['I'])
        s.ei=[(Fr(0),EI0)]+[(F(x),F(v)) for x,v in c.get('ei',[])]   # EI from x onwards
        s.pf=[(F(a),F(v)) for a,v in c['pf']];s.pm=[(F(a),F(v)) for a,v in c['pm']]
        s.q=[(F(a),F(b),F(v),F(v)) for a,b,v in c['q']]+[(F(a),F(b),F(v1),F(v2)) for a,b,v1,v2 in c.get('lin',[])]
        s.kt=[(F(a),F(b),F(v)) for a,b,v in c.get('curv',[])]
        s.h=sorted(F(x) for x in c.get('hinges',[]))
        tr=[];ro=[]
        for a,t in c['sup']:
            tr.append([F(a),'rigid'])
            if t=='Fixed': ro.append([F(a),'rigid'])
        for x,kw,kt,_ in c.get('springs',[]):
            tr.append([F(x),'rigid' if kw==INF else 1/F(kw)])
            if kt==INF: ro.append([F(x),'rigid'])
            elif kt>0: ro.append([F(x),1/F(kt)])
        s.tr,s.ro=tr,ro
        n=2+len(tr)+len(ro)+len(s.h);s.n=n
        s.iR=[2+i for i in range(len(tr))];s.iC=[2+len(tr)+i for i in range(len(ro))];s.iD=[2+len(tr)+len(ro)+i for i in range(len(s.h))]
        ev={Fr(0),s.L}|{a for a,_ in tr}|{a for a,_ in ro}|{a for a,_ in s.pf}|{a for a,_ in s.pm}|set(s.h)|{x for x,_ in s.ei}
        for a,b,*_ in s.q: ev|={a,b}
        for a,b,_ in s.kt: ev|={a,b}
        s.ev=sorted(e for e in ev if 0<=e<=s.L)
        rows=[]
        st=s.run(None,rows)
        rows.append(st['V']);rows.append(st['M'])
        A=[[r.c.get(i,Fr(0)) for i in range(n)]+[-r.k] for r in rows]
        if len(A)!=n: raise RuntimeError('rows %d unknowns %d'%(len(A),n))
        s.x=solve(A,n)
    def EI(s,x):
        v=None
        for a,e in s.ei:
            if a<=x: v=e
        return v
    def piece(s,a,b):
        """q(t)=q0+q1 t, kT constant, EI constant on (a,b), t=x-a."""
        q0=Fr(0);q1=Fr(0);k=Fr(0)
        for qa,qb,v1,v2 in s.q:
            if qa<=a and qb>=b:
                sl=(v2-v1)/(qb-qa);q0+=v1+sl*(a-qa);q1+=sl
        for ka,kb,v in s.kt:
            if ka<=a and kb>=b: k+=v
        return q0,q1,k,s.EI(a+(b-a)/2)
    def point(s,x,st,rows,side):
        """Apply jumps at x. Conditions are taken from the left state (w continuous, theta at a hinge-free point)."""
        for a,v in s.pf:
            if a==x: st['V']=st['V']+v
        for i,(a,cw) in enumerate(s.tr):
            if a==x:
                R=unknown(s.iR[i])
                if rows is not None: rows.append(st['w']+(R*cw if cw!='rigid' else Lin()))
                st['V']=st['V']+R
        for a,v in s.pm:
            if a==x: st['M']=st['M']-v
        for i,(a,ct) in enumerate(s.ro):
            if a==x:
                C=unknown(s.iC[i])
                if rows is not None: rows.append(st['t']+(C*ct if ct!='rigid' else Lin()))
                st['M']=st['M']-C
        for i,h in enumerate(s.h):
            if h==x:
                if rows is not None: rows.append(st['M'])
                st['t']=st['t']+unknown(s.iD[i])
    def run(s,stop,rows,x_eval=None,side='R'):
        st=dict(w=unknown(0),t=unknown(1),M=Lin(),V=Lin())
        out=None
        for j,x in enumerate(s.ev):
            if x_eval is not None and x==x_eval and side=='L': return st
            s.point(x,st,rows,'R')
            if x_eval is not None and x==x_eval: return st
            if j==len(s.ev)-1: break
            b=s.ev[j+1]
            if x_eval is not None and x<x_eval<b: return s.advance(st,x,x_eval)
            st=s.advance(st,x,b)
        return st
    def advance(s,st,a,b):
        q0,q1,k,EI=s.piece(a,b);t=b-a
        V,M,th,w=st['V'],st['M'],st['t'],st['w']
        Vn=V+q0*t+q1*t*t/2
        Mn=M+V*t+q0*t**2/2+q1*t**3/6
        thn=th+(M*t+V*t**2/2+q0*t**3/6+q1*t**4/24)*(1/EI)+k*t
        wn=w+th*t+(M*t**2/2+V*t**3/6+q0*t**4/24+q1*t**5/120)*(1/EI)+k*t*t/2
        return dict(w=wn,t=thn,M=Mn,V=Vn)
    def state(s,x,side):
        st=s.run(None,None,Fr(x),side)
        return {k:v.val(s.x) for k,v in st.items()}
    def w(s,x): return s.state(x,'R')['w']
    def reactions(s):
        return [(a,s.x[i]) for (a,_),i in zip(s.tr,s.iR)],[(a,s.x[i]) for (a,_),i in zip(s.ro,s.iC)]
def solve(A,n):
    for i in range(n):
        p=next((k for k in range(i,n) if A[k][i]!=0),None)
        if p is None: raise Mechanism()
        A[i],A[p]=A[p],A[i];d=A[i][i]
        for k in range(n):
            if k!=i and A[k][i]!=0:
                f=A[k][i]/d;A[k]=[u-f*v for u,v in zip(A[k],A[i])]
    return [A[i][n]/A[i][i] for i in range(n)]
