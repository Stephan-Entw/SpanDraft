import compare,spring_ref as S,s_fuzz,json
cs=s_fuzz.targeted();res=[json.loads(l) for l in open('cmp-s-out.jsonl')]
tab={}
for c,o in zip(cs,res):
    nm,r,u=c['id'].split('|')
    e=o.get('fail',o.get('crash')) or '%.0e'%max(S.errors(c,o))
    tab.setdefault(nm,{}).setdefault(u,{})[r]=e
for nm,t in tab.items():
    if nm in('nebengelenk','teileinsp'):continue
    print(nm,'  Zeilen kL^3/EI=10^u, Spalten Abstand/L')
    for u,row in t.items(): print('   u=%3s '%u+' '.join('%14s'%row[r][:14] for r in row))
