import json,os,subprocess,sys,random,importlib
S=os.path.dirname(os.path.abspath(__file__));sys.path.insert(0,S)
import compare,mutate3,s_fuzz,f_fuzz,f_ref as R,spring_ref as SR
compare_run=compare.run
def run(cases,tag): return compare_run('harness-mutation',cases,tag)
def detect():
    worst=0.;fails=0
    cs=s_fuzz.targeted()
    for c,o in zip(cs,run(cs,'m1')):
        if 'S' in o: worst=max(worst,max(SR.errors(c,o)))
        else: fails+=1
    rng=random.Random(5);cs=[f_fuzz.gen(rng,r0) for r0 in (1e-2,1e-6) for _ in range(120)]
    import transfer_ref as T
    for c,o in zip(cs,run(cs,'m2')):
        if 'S' in o:
            try: worst=max(worst,max(R.errors(c,o)))
            except T.Mechanism: worst=max(worst,9.)
        elif o.get('fail')=='UnstableModel':
            try: T.Transfer(c);fails+=1
            except T.Mechanism: pass
    return worst,fails
ids=sys.argv[1:]
B=S+'/base3'
subprocess.run(['dotnet','build','harness-mutation','-c','Release','-v','q','--nologo'],cwd=S,capture_output=True)
print('Basis',detect(),flush=True)
for mid,name,patches in mutate3.M:
    if ids and mid not in ids: continue
    saved={}
    for f,a,b in patches:
        p=B+'/'+f;t=open(p).read();saved.setdefault(p,t);open(p,'w').write(t.replace(a,b))
    r=subprocess.run(['dotnet','build','harness-mutation','-c','Release','-v','q','--nologo'],cwd=S,capture_output=True,text=True)
    res='Build-Fehler' if r.returncode else detect()
    for p,t in saved.items(): open(p,'w').write(t)
    print(mid,name,'->',res,flush=True)
