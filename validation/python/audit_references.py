#!/usr/bin/env python3
"""Direct analytic calibration and optional reproduction audit; no SpanDraft calls."""
import argparse
import hashlib
import json
import sys
from pathlib import Path
from common import ROOT, TOLERANCES, check_environment


def semantic(data):
    clone=json.loads(json.dumps(data))
    clone['provenance'].pop('generatedAt',None)
    return clone


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory',type=Path,default=ROOT/'validation/references')
    parser.add_argument('--against',type=Path,help='Existing golden directory for exact timestamp-independent reproduction comparison')
    parser.add_argument('--report',type=Path,default=ROOT/'validation/results/reproduction.json')
    args=parser.parse_args()
    check_environment()
    results=[]
    for path in sorted(args.directory.glob('*.analytical.json')):
        analytical=json.loads(path.read_text())
        for solver in ['indeterminatebeam','pycba']:
            external=json.loads(path.with_name(analytical['caseId']+'.'+solver+'.json').read_text())
            failures=[]; count=0
            for group in ['samples','reactions']:
                actual={(r['position'],r.get('side')):r['values'] for r in external[group]}
                for row in analytical[group]:
                    values=actual[(row['position'],row.get('side'))]
                    for q,value in row['values'].items():
                        if external['availability'][q]!='supported': continue
                        count+=1
                        kind='displacement' if q in ['u','w'] else 'rotation' if q=='theta' else 'moment' if q in ['M','Rm'] else 'force'
                        a,r=TOLERANCES[kind]
                        if abs(values[q]-value)>a+r*abs(value):
                            failures.append(dict(quantity=q,position=row['position'],side=row.get('side'),
                                                 external=values[q],analytical=value,absTol=a,relTol=r))
            for q,ext in analytical['extrema'].items():
                count+=1
                kind='displacement' if q[0]=='w' else 'moment' if q[0]=='M' else 'force'
                a,r=TOLERANCES[kind]
                observed=external['extrema'][q]['value']
                if abs(observed-ext['value'])>a+r*abs(ext['value']):
                    failures.append(dict(quantity=q,external=observed,analytical=ext['value'],absTol=a,relTol=r))
            results.append(dict(caseId=analytical['caseId'],solver=solver,comparisons=count,
                                status='FAIL' if failures else 'PASS',failures=failures))
    if len(results)!=18:
        raise RuntimeError('Calibration requires all 9 analytical cases against both external solvers')
    reproduced=[]; differing=[]
    if args.against:
        left={p.name:p for p in args.directory.glob('*.json')}
        right={p.name:p for p in args.against.glob('*.json')}
        if len(left)!=45 or left.keys()!=right.keys():
            raise RuntimeError('Reproduction requires exactly the same 45 reference files')
        for name,path in sorted(left.items()):
            actual=semantic(json.loads(path.read_text()))
            expected=semantic(json.loads(right[name].read_text()))
            if actual!=expected: differing.append(name)
            payload=json.dumps(actual,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
            reproduced.append(dict(file=name,semanticSha256=hashlib.sha256(payload).hexdigest()))
    data=dict(analyticalCalibration=results,calibrationComparisons=sum(r['comparisons'] for r in results),
              reproducedFiles=reproduced,differingFiles=differing,
              status='PASS' if not differing and all(r['status']=='PASS' for r in results) else 'FAIL',
              note='This audit verifies calibration/reproduction, not the full V1 acceptance gate.')
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(data,indent=2,allow_nan=False)+'\n')
    print(f"Analytical calibration: {data['status']}, {data['calibrationComparisons']} scalar comparisons; reproduced files: {len(reproduced)}, differences: {len(differing)}")
    return 0 if data['status']=='PASS' else 1


if __name__=='__main__': sys.exit(main())
