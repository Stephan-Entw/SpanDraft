import json,sys,compare,spring_ref as S
from spring_ref import INF
L=4.;P=-10000.;EI=210e9*8e-6
cs=[S.case('federmitte',L,[(0,'Pinned'),(L,'Roller')],springs=[(L/2,1e6,0,False)],pf=[(L/3,P)]),
    S.case('teileinsp',L,[(L,'Roller')],springs=[(0,INF,1e5,True)],pf=[(L/2,P)]),
    S.case('nurfedern',L,[],springs=[(0,1e6,0,True),(L,2e6,0,False)],pf=[(L/3,P)]),
    S.case('elast.einsp',L,[],springs=[(0,1e7,3e5,True)],pf=[(L,P)]),
    S.case('feder+gelenk',L,[(0,'Fixed')],springs=[(L,1e6,0,False)],hinges=[L/2],pf=[(L*.75,P)]),
    S.case('drehfeder ohne weg',L,[(0,'Pinned'),(L,'Roller')],springs=[(L/2,1e5,INF,False)],pf=[(L/3,P)])]
res=compare.run('harness',cs,'s')
for c,o in zip(cs,res):
    if 'fail' in o or 'crash' in o: print(c['id'],o);continue
    print('%-20s Felder %.1e Extrem %.1e Reaktion %.1e'%((c['id'],)+S.errors(c,o)))
