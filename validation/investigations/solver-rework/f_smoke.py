import compare,f_ref as R
L=4.;P=-10000.;EI=210e9*8e-6;g=6e-9
cs=[R.case('EI-Wechsel',L,[(0,'Pinned'),(L,'Roller')],ei=[(L/2,EI*3)],pf=[(L/3,P)]),
    R.case('EI 6nm neben Lager',L,[(0,'Pinned'),(L/2,'Roller'),(L,'Roller')],ei=[(L/2+g,EI*5),(L*.8,EI/2)],pf=[(L/3,P),(.7*L,P)]),
    R.case('duenne Scheibe 6nm',L,[(0,'Fixed'),(L,'Roller')],ei=[(L/2,EI/100),(L/2+g,EI)],pf=[(L/3,P)],q=[(0,L,-2000)]),
    R.case('Trapezlast',L,[(0,'Pinned'),(L,'Roller')],lin=[(.5,3.,-1000.,-5000.)]),
    R.case('Dreieck 6nm kurz',L,[(0,'Fixed'),(L,'Roller')],lin=[(1.,1.+g,0.,-1e9)],pf=[(2.,P)]),
    R.case('Vorzeichenwechsel',L,[(0,'Fixed'),(L,'Fixed')],lin=[(0.,L,3000.,-6000.)]),
    R.case('Temperatur',L,[(0,'Fixed'),(L/2,'Roller'),(L,'Roller')],curv=[(0.,L,1e-3)]),
    R.case('alles+Gelenk+Feder',L,[(0,'Fixed')],springs=[(L,2e6,0,False),(L/2+g,1e9,0,False)],hinges=[L/2],ei=[(L*.25,EI*2),(L/2+2*g,EI/3)],lin=[(.2,3.8,-2000.,500.)],curv=[(1.,3.,5e-4)],pf=[(L*.75,P)])]
res=compare.run('harness',cs,'f')
for c,o in zip(cs,res):
    if 'fail' in o or 'crash' in o: print(c['id'],o);continue
    print('%-22s Felder %.1e Extrem %.1e Reaktion %.1e'%((c['id'],)+R.errors(c,o)))
