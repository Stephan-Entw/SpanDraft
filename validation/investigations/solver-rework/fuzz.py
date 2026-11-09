# Independent exact reference: Macaulay integration with rational arithmetic. No FEM, no SpanDraft code.
import json,random,math,subprocess,sys,os,collections
from fractions import Fraction as Fr
import numpy as np
S=os.path.dirname(os.path.abspath(__file__))
def H(x,a,side):  # Heaviside with side at x==a
    return 1 if (x>a or (x==a and side=='R')) else 0
def mac(x,a,n): return (x-a)**n if x>a else Fr(0)
class Exact:
    def __init__(s,c):
        F=lambda v:Fr(float(v))
        s.L=F(c['L']);s.EI=F(c['E'])*F(c['I'])
        s.sup=[(F(a),t) for a,t in c['sup']]
        s.pf=[(F(a),F(v)) for a,v in c['pf']];s.pm=[(F(a),F(v)) for a,v in c['pm']];s.q=[(F(a),F(b),F(v)) for a,b,v in c['q']]
        fixed=[a for a,t in s.sup if t=='Fixed']
        n=len(s.sup)+len(fixed)+2
        A=[[Fr(0)]*(n+1) for _ in range(n)]
        # unknown order: Ry(sup...), Rm(fixed...), C1, C2
        ns=len(s.sup);nf=len(fixed)
        r=0
        for i,(a,_) in enumerate(s.sup): A[r][i]=Fr(1)
        A[r][n]=-(sum(v for _,v in s.pf)+sum(v*(b-a) for a,b,v in s.q)); r+=1
        for i,(a,_) in enumerate(s.sup): A[r][i]=a
        for j in range(nf): A[r][ns+j]=Fr(1)
        A[r][n]=-(sum(v*a for a,v in s.pf)+sum(v for _,v in s.pm)+sum(v*(b-a)*(a+b)/2 for a,b,v in s.q)); r+=1
        for x,_ in s.sup:
            for i,(a,_) in enumerate(s.sup): A[r][i]=mac(x,a,3)/6
            for j,a in enumerate(fixed): A[r][ns+j]=-mac(x,a,2)/2
            A[r][ns+nf]=x;A[r][ns+nf+1]=Fr(1)
            A[r][n]=-s.wload(x); r+=1
        for x in fixed:
            for i,(a,_) in enumerate(s.sup): A[r][i]=mac(x,a,2)/2
            for j,a in enumerate(fixed): A[r][ns+j]=-mac(x,a,1)
            A[r][ns+nf]=Fr(1)
            A[r][n]=-s.tload(x); r+=1
        for i in range(n):
            p=next(k for k in range(i,n) if A[k][i]!=0); A[i],A[p]=A[p],A[i]
            d=A[i][i]
            for k in range(n):
                if k!=i and A[k][i]!=0:
                    f=A[k][i]/d
                    A[k]=[u-f*v for u,v in zip(A[k],A[i])]
        sol=[A[i][n]/A[i][i] for i in range(n)]
        s.Ry=sol[:ns];s.Rm=sol[ns:ns+nf];s.C1=sol[ns+nf];s.C2=sol[ns+nf+1];s.fixed=fixed
        s.F=[(a,v) for a,v in s.pf]+[(a,r) for (a,_),r in zip(s.sup,s.Ry)]
        s.C=[(a,v) for a,v in s.pm]+[(a,r) for a,r in zip(fixed,s.Rm)]
    def wload(s,x): return sum(v*mac(x,a,3)/6 for a,v in s.pf)-sum(v*mac(x,a,2)/2 for a,v in s.pm)+sum(v*(mac(x,a,4)-mac(x,b,4))/24 for a,b,v in s.q)
    def tload(s,x): return sum(v*mac(x,a,2)/2 for a,v in s.pf)-sum(v*mac(x,a,1) for a,v in s.pm)+sum(v*(mac(x,a,3)-mac(x,b,3))/6 for a,b,v in s.q)
    def w(s,x): return (sum(v*mac(x,a,3)/6 for a,v in s.F)-sum(v*mac(x,a,2)/2 for a,v in s.C)+sum(v*(mac(x,a,4)-mac(x,b,4))/24 for a,b,v in s.q)+s.C1*x+s.C2)/s.EI
    def t(s,x): return (sum(v*mac(x,a,2)/2 for a,v in s.F)-sum(v*mac(x,a,1) for a,v in s.C)+sum(v*(mac(x,a,3)-mac(x,b,3))/6 for a,b,v in s.q)+s.C1)/s.EI
    def V(s,x,side): return sum(v*H(x,a,side) for a,v in s.F)+sum(v*(mac(x,a,1)-mac(x,b,1)) for a,b,v in s.q)
    def M(s,x,side): return sum(v*mac(x,a,1) for a,v in s.F)-sum(v*H(x,a,side) for a,v in s.C)+sum(v*(mac(x,a,2)-mac(x,b,2))/2 for a,b,v in s.q)
    def events(s):
        e={Fr(0),s.L}|{a for a,_ in s.sup}|{a for a,_ in s.pf}|{a for a,_ in s.pm}
        for a,b,_ in s.q: e|={a,b}
        return sorted(e)
    def extrema(s):
        ev=s.events()
        wv=[];Vv=[];Mv=[]
        for a,b in zip(ev,ev[1:]):
            wv+= [s.w(a),s.w(b)];Vv+=[s.V(a,'R'),s.V(b,'L')];Mv+=[s.M(a,'R'),s.M(b,'L')]
            # M stationary: V linear on piece -> exact
            va,vb=s.V(a,'R'),s.V(b,'L')
            if va*vb<0:
                x=a+(b-a)*va/(va-vb); Mv.append(s.M(x,'R'))
            # w stationary: theta polynomial degree<=3 on the piece: exact interpolation then numeric roots
            n=4; xs=[a+(b-a)*Fr(i,3) for i in range(n)]; ys=[s.t(x) for x in xs]
            m=max(abs(y) for y in ys)
            if m==0: continue
            co=np.polyfit([0,1/3,2/3,1],[float(y/m) for y in ys],3)
            for r in np.roots(co):
                if abs(r.imag)<1e-6 and 0<r.real<1:
                    wv.append(s.w(a+(b-a)*Fr(float(r.real))))
        return dict(wMin=min(wv),wMax=max(wv),VMin=min(Vv),VMax=max(Vv),MMin=min(Mv),MMax=max(Mv))
def lu(r,a,b): return 10**r.uniform(math.log10(a),math.log10(b))
def gen(r,kind,i):
    if kind=='normal':
        L=round(r.uniform(.5,15),3);E=r.choice([210e9,200e9,70e9,110e9,11e9]);I=lu(r,1e-8,1e-3);A=lu(r,1e-4,.1);mag=lu(r,10,2e5);ne=r.randint(1,8);grid=lambda:round(r.uniform(0,L),3)
    elif kind=='wide':
        L=lu(r,1e-2,1e3);E=lu(r,1e8,1e12);I=lu(r,1e-14,1e-1);A=lu(r,1e-6,1);mag=lu(r,1e-3,1e8);ne=r.randint(1,8);grid=lambda:r.uniform(0,L)
    elif kind=='cluster':
        L=lu(r,1,30);E=210e9;I=lu(r,1e-8,1e-3);A=.01;mag=lu(r,10,1e5);ne=r.randint(2,6)
        c=r.uniform(0,L);gap=lu(r,1e-6,1e-2)
        grid=lambda:min(L,max(0,c+r.randint(-3,3)*gap)) if r.random()<.7 else r.uniform(0,L)
    else: # many
        L=lu(r,5,50);E=210e9;I=lu(r,1e-6,1e-3);A=.01;mag=lu(r,10,1e5);ne=r.randint(30,120);grid=lambda:round(r.uniform(0,L),3)
    ns=r.choice([1,2,2,2,3,3,4,5]) if kind!='many' else r.randint(2,15)
    pos=set()
    while len(pos)<ns: pos.add(r.choice([0.0,L,grid(),grid(),grid()]))
    pos=sorted(pos)
    types=[r.choice(['Roller','Roller','Pinned','Fixed']) for _ in pos]
    if ns==1: types=['Fixed']
    if not any(t in('Pinned','Fixed') for t in types): types[r.randrange(ns)]=r.choice(['Pinned','Fixed'])
    pf=[];pm=[];q=[];sc=lambda:r.choice([-1,-1,1])*mag*r.uniform(.1,1)
    for _ in range(ne):
        k=r.random()
        if k<.45: pf.append([r.choice([grid(),grid(),r.choice(pos),L,0.0]),sc()])
        elif k<.65: pm.append([r.choice([grid(),grid(),r.choice(pos),L,0.0]),sc()*L/4])
        else:
            a,b=sorted([grid(),grid()])
            if r.random()<.2:a,b=0.0,L
            if b>a: q.append([a,b,sc()/L])
    if not(pf or pm or q): pf.append([L/2,sc()])
    ev=sorted({0.0,L}|set(pos)|{a for a,_ in pf}|{a for a,_ in pm}|{x for a,b,_ in q for x in(a,b)})
    pts=[]
    for j,x in enumerate(ev):
        if j>0: pts.append([x,'L'])
        if j<len(ev)-1: pts.append([x,'R'])
    for a,b in zip(ev,ev[1:]):
        for f in (.13,r.random(),.87):
            x=a+(b-a)*f
            if a<x<b: pts.append([x,'R'])
    if len(pts)>120: pts=r.sample(pts,120)
    return dict(id=f'{kind}{i}',kind=kind,L=L,E=E,A=A,I=I,sup=[[p,t] for p,t in zip(pos,types)],pf=pf,pm=pm,q=q,pts=pts)
def main():
    n=dict(normal=int(sys.argv[1]),wide=int(sys.argv[2]),cluster=int(sys.argv[3]),many=int(sys.argv[4]))
    r=random.Random(44); cases=[gen(r,k,i) for k,c in n.items() for i in range(c)]
    with open(S+'/fuzz-in.jsonl','w') as f:
        for c in cases: f.write(json.dumps(c)+'\n')
    subprocess.run(['dotnet','run','--no-build','-c','Release','--project',S+'/clean/fuzz','--',S+'/fuzz-in.jsonl',S+'/fuzz-out.jsonl'],check=True)
    TOL=dict(w=(1e-10,1e-6),t=(1e-10,1e-6),V=(1e-5,1e-6),M=(1e-5,1e-6))
    recs=[]; st=collections.defaultdict(collections.Counter); worst=collections.defaultdict(list); bad=[]
    for c,l in zip(cases,open(S+'/fuzz-out.jsonl')):
        o=json.loads(l);k=c['kind'];st[k]['n']+=1
        if 'crash' in o: st[k]['crash']+=1;bad.append((c['id'],'crash',o['crash'][:90]));continue
        if 'fail' in o:
            st[k]['refused:'+o['fail']]+=1;_ev=sorted({0.0,c['L']}|{s_[0] for s_ in c['sup']}|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}|{x for a,b,_ in c['q'] for x in(a,b)});_l=[b-a for a,b in zip(_ev,_ev[1:])];recs.append(dict(id=c['id'],kind=k,ratio=max(_l)/min(_l),minEl=min(_l),L=c['L'],refused=True));continue
        e=Exact(c); ev=e.events()
        # characteristic field scales for scale-free error
        S_=lambda fn:max(abs(float(y)) for y in fn) or 1.0
        rows=[];
        for (x,side),v in zip(c['pts'],o['S']):
            X=Fr(float(x));rows.append((float(e.w(X)),float(e.t(X)),float(e.V(X,side)),float(e.M(X,side)),v))
        sc=[max(abs(r[i]) for r in rows) or 1.0 for i in range(4)]
        n_err=0.0;fail_abs=0;cnt=0
        for r_ in rows:
            for i,q_ in enumerate('wtVM'):
                ref,act=r_[i],r_[4][i];d=abs(ref-act);cnt+=1
                a,rel=TOL[q_]
                if d>a+rel*abs(ref): fail_abs+=1
                n_err=max(n_err,d/sc[i])
        # reactions
        rmap={float(a):(ry) for (a,_),ry in zip(e.sup,e.Ry)}
        rm=dict(zip([float(a) for a in e.fixed],e.Rm))
        for p,ry,m in o['R']:
            d=abs(float(rmap[p])-ry);cnt+=1;fs=max(abs(float(v)) for v in e.Ry) or 1
            if d>1e-5+1e-6*abs(float(rmap[p])): fail_abs+=1
            n_err=max(n_err,d/fs)
            if m is not None:
                d=abs(float(rm[p])-m);cnt+=1
                if d>1e-5+1e-6*abs(float(rm[p])): fail_abs+=1
                n_err=max(n_err,d/sc[3])
        # extrema: value and that reported position really carries it
        xr=e.extrema();xerr=0.0;locbad=0
        for key,(val,pos,side) in o['X'].items():
            ref=float(xr[key]);i={'w':0,'V':2,'M':3}[key[0]];xerr=max(xerr,abs(val-ref)/sc[i]);cnt+=1
            P=Fr(float(pos));sd='L' if side=='Left' else 'R'
            at=float({'w':e.w(P),'V':e.V(P,sd),'M':e.M(P,sd)}[key[0]])
            if abs(at-ref)/sc[i]>1e-6: locbad+=1
        st[k]['ok']+=1;st[k]['cmp']+=cnt
        if fail_abs: st[k]['casesOutsideTol']+=1;st[k]['cmpOutsideTol']+=fail_abs
        if xerr>1e-6: st[k]['extremaBad']+=1
        if locbad: st[k]['extremaLocBad']+=1
        worst[k].append(max(n_err,xerr))
        _ev=[float(x) for x in ev];_l=[b-a for a,b in zip(_ev,_ev[1:])]
        recs.append(dict(id=c['id'],kind=k,ratio=max(_l)/min(_l),minEl=min(_l),L=c['L'],err=max(n_err,xerr),out=fail_abs,cnt=cnt,loc=locbad))
        if max(n_err,xerr)>1e-6 or locbad: bad.append((c['id'],f'err={max(n_err,xerr):.2e} locbad={locbad} outside={fail_abs}/{cnt}'))
    for k in st:
        w=np.array(worst[k]) if worst[k] else np.array([0.])
        print(k,dict(st[k]),'scaledErr median=%.1e p99=%.1e max=%.1e'%(np.median(w),np.percentile(w,99),w.max()))
    json.dump(recs,open(S+'/fuzz-recs.json','w'))
    print('bad',len(bad));print(*bad[:25],sep='\n')
    json.dump(bad,open(S+'/fuzz-bad.json','w'))
if __name__=="__main__": main()
