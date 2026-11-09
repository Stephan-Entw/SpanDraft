# Errors of C# output against the exact transfer reference (any feature mix).
from fractions import Fraction as Fr
import transfer_ref as T, numpy as np
def pts(c):
    L=c['L']
    ev=sorted({0.0,L}|{s[0] for s in c['sup']}|{s[0] for s in c.get('springs',[])}|{a for a,_ in c['pf']}|{a for a,_ in c['pm']}
              |{x for a,b,_ in c['q'] for x in (a,b)}|{x for a,b,*_ in c.get('lin',[]) for x in (a,b)}|{x for a,b,_ in c.get('curv',[]) for x in (a,b)}
              |set(c.get('hinges',[]))|{x for x,_ in c.get('ei',[])})
    out=[]
    for j,x in enumerate(ev):
        if j>0: out.append([x,'L'])
        if j<len(ev)-1:
            out.append([x,'R'])
            for f in (.3,.7): out.append([x+(ev[j+1]-x)*f,'R'])
    return out
def case(id,L,sup,E=210e9,I=8e-6,**k):
    c=dict(id=id,L=L,E=E,A=.003,I=I,sup=[list(s) for s in sup],pf=[],pm=[],q=[],springs=[],hinges=[],ei=[],lin=[],curv=[])
    for key,v in k.items(): c[key]=[list(x) if isinstance(x,(tuple,list)) else x for x in v]
    c['pts']=pts(c);return c
def extrema(ex,c):
    # exact extrema on each piece: V and M piecewise polynomials; sample dense + roots via numpy refine on exact polys
    ev=ex.ev;wv=[];Vv=[];Mv=[]
    for a,b in zip(ev,ev[1:]):
        sa=ex.state(a,'R');sb=ex.state(b,'L')
        wv+=[sa['w'],sb['w']];Vv+=[sa['V'],sb['V']];Mv+=[sa['M'],sb['M']]
        xs=[a+(b-a)*Fr(i,6) for i in range(1,6)]
        for key,arr,deg,out in (('V',Mv,3,'M'),('t',wv,5,'w'),('q',Vv,2,'V')):
            if key=='q':
                q0,q1,_,_=ex.piece(a,b)
                if q1!=0:
                    t0=-q0/q1
                    if 0<t0<b-a: Vv.append(ex.state(a+t0,'R')['V'])
                continue
            ys=[ex.state(x,'R')[key] for x in [a]+xs]+[ex.state(b,'L')[key]];m=max(abs(y) for y in ys)
            if m==0: continue
            co=np.polyfit([float((x-a)/(b-a)) for x in [a]+xs+[b]],[float(y/m) for y in ys],min(deg,6))
            for r in np.roots(co):
                if abs(r.imag)<1e-7 and 0<r.real<1: arr.append(ex.state(a+(b-a)*Fr(float(r.real)),'R')[out])
    return dict(wMin=min(wv),wMax=max(wv),VMin=min(Vv),VMax=max(Vv),MMin=min(Mv),MMax=max(Mv))
def errors(c,out):
    ex=T.Transfer(c);rows=[]
    for x,sd in c['pts']:
        st=ex.state(Fr(float(x)),sd);rows.append([float(st['w']),float(st['t']),float(st['V']),float(st['M'])])
    sc=[max(abs(r[i]) for r in rows) or 1. for i in range(4)]
    L=float(c['L']);EI=min([c['E']*c['I']]+[e[1] for e in c.get('ei',[])])
    v,m=sc[2],sc[3];sc[2]=max(v,m/L);sc[3]=max(m,v*L);sc[1]=max(sc[1],sc[3]*L/EI);sc[0]=max(sc[0],sc[3]*L**2/EI,sc[1]*L)
    f=max(max(abs(r[i]-o[i])/sc[i] for i in range(4)) for r,o in zip(rows,out['S']))
    xr=extrema(ex,c);g=max(abs(out['X'][k][0]-float(xr[k]))/sc[{'w':0,'V':2,'M':3}[k[0]]] for k in out['X'])
    R={q[0]:q for q in out['R']};rt,rr=ex.reactions();r=0.
    for a,ry in rt:
        o=R.get(float(a));r=max(r,abs(o[1]-float(ry))/sc[2]) if o else 9.
    for a,rm in rr:
        o=R.get(float(a));r=max(r,abs((o[2] or 0)-float(rm))/sc[3]) if o else 9.
    return f,g,r
